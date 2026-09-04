namespace DW2ModLauncherBeta
{
    public partial class MainForm
    {
        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.Text = text;
        }

        private string T(string ja, string en)
        {
            return settings != null && settings.Language == "en" ? en : ja;
        }
    }
}
