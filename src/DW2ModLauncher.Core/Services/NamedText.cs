using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Fills the named placeholders of a language-pack text: "Delete the profile {Profile}?" with ("Profile", "Raid") is "Delete the
    /// profile Raid?". A translator sees what each placeholder is and may put it anywhere in the sentence. A name is a letter followed by
    /// letters and digits; {Count:0.#} adds a format for a number or date; {{ and }} are literal braces. Anything else in braces (a
    /// stale {0}, a misspelt name) is left as written, so the mistake is visible in the text and is reported through
    /// <c>onUnknown</c> when one is given.
    /// This one file is compiled into both the launcher's Core and the loader (which cannot reference Core), so it uses the BCL only.
    /// </summary>
    public static class NamedText
    {
        public static string Fill(string template, params (string Name, object Value)[] values)
        {
            return Fill(template, null, values);
        }

        public static string Fill(string template, Action<string> onUnknown, params (string Name, object Value)[] values)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            Dictionary<string, object> byName = new Dictionary<string, object>(StringComparer.Ordinal);
            if (values != null)
            {
                foreach ((string name, object value) in values)
                {
                    if (!byName.TryAdd(name, value)) throw new ArgumentException("The placeholder '" + name + "' is given twice.", nameof(values));
                }
            }

            StringBuilder result = new StringBuilder(template.Length + 16);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { result.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { result.Append('}'); i += 2; continue; }
                if (c != '{') { result.Append(c); i++; continue; }

                int close = template.IndexOf('}', i);
                if (close < 0 || !TryParse(template.Substring(i + 1, close - i - 1), out string placeholder, out string format))
                {
                    result.Append(c);
                    i++;
                    continue;
                }
                if (byName.TryGetValue(placeholder, out object found))
                {
                    result.Append(format != null && found is IFormattable formattable
                        ? formattable.ToString(format, CultureInfo.CurrentCulture)
                        : Convert.ToString(found, CultureInfo.CurrentCulture));
                }
                else
                {
                    result.Append(template, i, close - i + 1);
                    onUnknown?.Invoke(placeholder);
                }
                i = close + 1;
            }
            return result.ToString();
        }

        /// <summary>The placeholder names a text uses, in order of first appearance, each once. Pack checks compare these across languages.</summary>
        public static IReadOnlyList<string> Names(string template)
        {
            List<string> names = new List<string>();
            int i = 0;
            while (i < template.Length)
            {
                if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '{') { i += 2; continue; }
                if (template[i] != '{') { i++; continue; }
                int close = template.IndexOf('}', i);
                if (close >= 0 && TryParse(template.Substring(i + 1, close - i - 1), out string name, out _))
                {
                    if (!names.Contains(name)) names.Add(name);
                    i = close + 1;
                }
                else i++;
            }
            return names;
        }

        private static bool TryParse(string inside, out string name, out string format)
        {
            int colon = inside.IndexOf(':');
            name = colon < 0 ? inside : inside.Substring(0, colon);
            format = colon < 0 ? null : inside.Substring(colon + 1);
            if (name.Length == 0 || !char.IsAsciiLetter(name[0])) return false;
            foreach (char c in name)
            {
                if (!char.IsAsciiLetterOrDigit(c)) return false;
            }
            return true;
        }
    }
}
