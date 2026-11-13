using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services.Catalog
{
    /// <summary>
    /// Reads the catalog's index.json. The file comes from outside the launcher, so every field the launcher acts on is checked here
    /// and a bad entry fails the whole read with a message naming the entry and the field: nothing is guessed or defaulted.
    /// </summary>
    public static class CatalogParser
    {
        public const int SupportedSchema = 1;
        private static readonly string[] Kinds = { "code", "xml", "translation", "font" };

        public static CatalogIndex Parse(string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException error)
            {
                throw new FormatException("The catalog is not valid JSON: " + error.Message, error);
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The catalog is not a JSON object.");
                long schema = RequiredLong(root, "schema", "the catalog");
                if (schema != SupportedSchema) throw new FormatException("The catalog uses schema " + schema + "; this launcher reads schema " + SupportedSchema + ". Update the launcher.");

                CatalogIndex index = new CatalogIndex { GeneratedUtc = RequiredDate(root, "generated", "the catalog") };
                if (!root.TryGetProperty("mods", out JsonElement mods) || mods.ValueKind != JsonValueKind.Array)
                    throw new FormatException("The catalog has no \"mods\" list.");
                HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> uids = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement item in mods.EnumerateArray())
                {
                    CatalogEntry entry = ParseEntry(item);
                    if (!ids.Add(entry.Id)) throw new FormatException("The catalog lists \"" + entry.Id + "\" twice.");
                    if (!uids.Add(entry.Uid)) throw new FormatException("The catalog lists the mod with uid " + entry.Uid + " twice (\"" + entry.Id + "\").");
                    index.Entries.Add(entry);
                }
                return index;
            }
        }

        private static CatalogEntry ParseEntry(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object) throw new FormatException("A catalog entry is not a JSON object.");
            string id = RequiredString(item, "id", "a catalog entry");
            string where = "catalog entry \"" + id + "\"";
            CatalogEntry entry = new CatalogEntry
            {
                Id = id,
                Uid = RequiredUid(item, where),
                Name = RequiredString(item, "name", where),
                Version = RequiredString(item, "version", where),
                Author = RequiredString(item, "author", where),
                ShortDescription = OptionalString(item, "shortDescription", where),
                WorkshopId = OptionalId(item, "workshopId", where),
                MinLauncherVersion = OptionalString(item, "minLauncherVersion", where),
                Size = RequiredLong(item, "size", where),
                Sha256 = RequiredString(item, "sha256", where).ToLowerInvariant(),
                DownloadUrl = RequiredString(item, "downloadUrl", where),
                ReleaseUrl = OptionalString(item, "releaseUrl", where),
                PreviewUrl = OptionalString(item, "previewUrl", where),
                PublishedUtc = RequiredDate(item, "publishedAt", where),
                Downloads = OptionalLong(item, "downloads", where),
                PeakDownloads = OptionalLong(item, "peakDownloads", where)
            };
            if (item.TryGetProperty("tags", out JsonElement tags) && tags.ValueKind != JsonValueKind.Null)
            {
                if (tags.ValueKind != JsonValueKind.Array) throw new FormatException(Capital(where) + " has a \"tags\" that is not a list.");
                List<string> written = new List<string>();
                foreach (JsonElement tag in tags.EnumerateArray())
                {
                    if (tag.ValueKind != JsonValueKind.String) throw new FormatException(Capital(where) + " has a tag that is not text.");
                    written.Add(tag.GetString());
                }
                entry.Tags.AddRange(CatalogTags.Normalize(written));
            }
            if (!item.TryGetProperty("kinds", out JsonElement kinds) || kinds.ValueKind != JsonValueKind.Array || kinds.GetArrayLength() == 0)
                throw new FormatException(Capital(where) + " has no \"kinds\" list.");
            foreach (JsonElement kind in kinds.EnumerateArray())
            {
                string name = kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
                if (name == null || Array.IndexOf(Kinds, name) < 0)
                    throw new FormatException(Capital(where) + " has kind \"" + (name ?? kind.ToString()) + "\"; expected any of " + string.Join(", ", Kinds) + ".");
                if (!entry.Kinds.Contains(name)) entry.Kinds.Add(name);
            }
            if (!Regex.IsMatch(entry.Sha256, "^[0-9a-f]{64}$")) throw new FormatException(Capital(where) + " has a sha256 that is not 64 hex digits.");
            if (entry.Size <= 0) throw new FormatException(Capital(where) + " has a size of " + entry.Size + " bytes.");
            if (!Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new FormatException(Capital(where) + " has a download address that is not an https URL.");

            if (entry.PreviewUrl != null && (!Uri.TryCreate(entry.PreviewUrl, UriKind.Absolute, out Uri preview) || preview.Scheme != Uri.UriSchemeHttps
                || !Regex.IsMatch(preview.AbsolutePath, @"\.(png|jpe?g)$", RegexOptions.IgnoreCase)))
                throw new FormatException(Capital(where) + " has a preview address that is not an https URL of a .png or .jpg image.");

            if (item.TryGetProperty("dependencies", out JsonElement dependencies) && dependencies.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement dep in dependencies.EnumerateArray())
                {
                    entry.Dependencies.Add(new CatalogDependency
                    {
                        Mod = RequiredString(dep, "mod", where + " dependency"),
                        Uid = OptionalUid(dep, where + " dependency"),
                        WorkshopId = OptionalId(dep, "workshopId", where + " dependency"),
                        Required = dep.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.True
                    });
                }
            }
            return entry;
        }

        private static string Capital(string text) { return char.ToUpperInvariant(text[0]) + text.Substring(1); }

        /// <summary>The entry's "uid" in canonical form. A missing or malformed one rejects the entry: nothing is listed without an identity.</summary>
        private static string RequiredUid(JsonElement item, string where)
        {
            string text = RequiredString(item, "uid", where);
            return ModUid.Normalize(text) ?? throw new FormatException(Capital(where) + " has a \"uid\" (" + text + ") that is not a UUID.");
        }

        /// <summary>A "uid" the element may leave out (null or absent gives null); one that is present must be a UUID.</summary>
        private static string OptionalUid(JsonElement element, string where)
        {
            string text = OptionalString(element, "uid", where);
            if (text == null) return null;
            return ModUid.Normalize(text) ?? throw new FormatException(Capital(where) + " has a \"uid\" (" + text + ") that is not a UUID.");
        }

        private static string RequiredString(JsonElement element, string name, string where)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new FormatException(Capital(where) + " has no \"" + name + "\".");
            return value.GetString();
        }

        /// <summary>Text the entry may leave out (null, absent or empty gives null). A value of the wrong type is an error, not an absence.</summary>
        private static string OptionalString(JsonElement element, string name, string where)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String) throw new FormatException(Capital(where) + " has a \"" + name + "\" that is not text.");
            return string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString();
        }

        private static long RequiredLong(JsonElement element, string name, string where)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number))
                throw new FormatException(Capital(where) + " has no whole number \"" + name + "\".");
            return number;
        }

        private static long? OptionalLong(JsonElement element, string name, string where)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number))
                throw new FormatException(Capital(where) + " has a \"" + name + "\" that is not a whole number.");
            return number;
        }

        /// <summary>A Workshop id, written as a number or as text; null when absent.</summary>
        private static string OptionalId(JsonElement element, string name, string where)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number.ToString(CultureInfo.InvariantCulture);
            if (value.ValueKind == JsonValueKind.String && Regex.IsMatch(value.GetString(), "^[0-9]+$")) return value.GetString();
            throw new FormatException(Capital(where) + " has a \"" + name + "\" that is not a Workshop id.");
        }

        private static DateTime RequiredDate(JsonElement element, string name, string where)
        {
            string text = RequiredString(element, name, where);
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime date))
                throw new FormatException(Capital(where) + " has a \"" + name + "\" that is not a date: " + text);
            return date;
        }
    }
}
