using System.Text;

namespace DW2ModLauncher.Core.Services
{
    /// <summary>
    /// Turns a PascalCase INI key (e.g. "WarControlEnabled") into a readable label
    /// ("War Control Enabled"). This is the only "translation" a MOD's own settings get unless the
    /// MOD supplies an explicit label (schema) or a comment above the key (INI) - the launcher has
    /// no built-in knowledge of any specific MOD's fields.
    /// </summary>
    public static class IniKeyHumanizer
    {
        public static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(key[i - 1]) || char.IsDigit(key[i - 1])))
                    result.Append(' ');
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
