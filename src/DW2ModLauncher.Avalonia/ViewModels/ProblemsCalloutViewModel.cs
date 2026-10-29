namespace DW2ModLauncher.Avalonia.ViewModels
{
    /// <summary>One coloured callout in the details pane (the conflicts or the cautions): a one-line header that expands to the list.</summary>
    public class ProblemsCalloutViewModel : ViewModelBase
    {
        private string summary = "";
        private string text = "";
        private bool expanded;
        private bool hasHooks;
        private string tip = "";

        public ProblemsCalloutViewModel(bool isConflict) { IsConflict = isConflict; }

        /// <summary>Red when true, gold when false.</summary>
        public bool IsConflict { get; }
        /// <summary>Always visible: "Conflict: 3 mods, 5 hooks" or "Caution (2)".</summary>
        public string Summary { get { return summary; } private set { Set(ref summary, value); } }
        /// <summary>One problem per line.</summary>
        public string Text { get { return text; } private set { if (Set(ref text, value)) Raise(nameof(IsVisible)); } }
        public bool IsVisible { get { return !string.IsNullOrEmpty(text); } }
        /// <summary>Whether the list is open: the player's one choice for every mod (kept in the settings).</summary>
        public bool Expanded { get { return expanded; } set { if (Set(ref expanded, value)) ExpandedChanged?.Invoke(value); } }
        /// <summary>Told when the player opens or closes the list, so the choice can be kept for every mod.</summary>
        public System.Action<bool> ExpandedChanged { get; set; }
        /// <summary>Whether the lines include code-hook overlaps, which the hooks report explains in detail.</summary>
        public bool HasHooks { get { return hasHooks; } private set { Set(ref hasHooks, value); } }
        public string ReportText { get; set; } = "";
        public RelayCommand ReportCommand { get; set; }

        /// <summary>The hover text of the header: the list, then the note (what the list means) when there is one.</summary>
        public string Tip { get { return tip; } private set { Set(ref tip, value); } }

        public void Show(string summary, string text, bool hasHooks, string note = null)
        {
            Summary = summary;
            Text = text;
            Tip = string.IsNullOrEmpty(note) ? text : text + "\n\n" + note;
            HasHooks = hasHooks;
        }

        public void Clear() { Show("", "", false); }
    }
}
