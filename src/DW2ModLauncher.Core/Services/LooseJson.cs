using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Parses JSON into a loosely-typed object graph (Dictionary/object[]/string/double/bool/null),
    /// mirroring the shape the launcher's mod.json/ACF-adjacent readers were originally written against.
    /// </summary>
    public static class LooseJson
    {
        public static object Parse(string text)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
                return Convert(document.RootElement);
        }

        private static object Convert(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    Dictionary<string, object> obj = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (JsonProperty property in element.EnumerateObject())
                        obj[property.Name] = Convert(property.Value);
                    return obj;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(Convert).ToArray();
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long l)) return l;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }

        public static string GetString(Dictionary<string, object> d, string[] keys, string fallback)
        {
            if (d == null) return fallback;
            foreach (string k in keys)
            {
                foreach (KeyValuePair<string, object> kv in d)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        return kv.Value.ToString();
                }
            }
            return fallback;
        }

        public static Dictionary<string, object> GetDictionary(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            foreach (KeyValuePair<string, object> kv in d)
                if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return kv.Value as Dictionary<string, object>;
            return null;
        }

        public static List<string> GetStringList(Dictionary<string, object> d, string[] keys)
        {
            List<string> result = new List<string>();
            if (d == null) return result;
            object value = null;
            foreach (string key in keys)
                foreach (KeyValuePair<string, object> kv in d)
                    if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; break; }
            if (value == null) return result;
            object[] array = value as object[];
            if (array != null)
                foreach (object item in array)
                {
                    Dictionary<string, object> itemObject = item as Dictionary<string, object>;
                    string text = itemObject == null ? System.Convert.ToString(item, CultureInfo.InvariantCulture) :
                        GetString(itemObject, new string[] { "id", "modId", "workshopId", "name" }, "");
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
                }
            else
            {
                string text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text)) result.AddRange(text.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string ReadJsonStringLoose(string text, string key, string fallback)
        {
            try
            {
                Match m = Regex.Match(text, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"", RegexOptions.IgnoreCase);
                if (m.Success) return Regex.Unescape(m.Groups[1].Value);
            }
            catch { }
            return fallback;
        }
    }
}
