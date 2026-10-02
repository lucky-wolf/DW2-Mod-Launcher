using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.App
{
    public partial class MainForm
    {
        // Glyphs for the Mod State checkbox column - a UI affordance, not
        // translatable content, so it's kept out of the language files.
        private const string CheckedGlyph = "☑";
        private const string UncheckedGlyph = "☐";

        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.Text = text;
        }

        private string T(string key, params object[] args)
        {
            string template = Localization.Get(settings != null ? settings.Language : "en", key);
            return args != null && args.Length > 0 ? string.Format(template, args) : template;
        }

        // "label + colon + value" is generic formatting, not translatable
        // content, so it lives here once instead of being retyped (and
        // baked into the language files) at every call site that needs it.
        private string Labeled(string key, string value)
        {
            return T(key) + ": " + value;
        }
    }
}
