using System;

namespace DW2ModLauncher.Avalonia.Services
{
    /// <summary>A clickable underlined line in a dialog: what it says, its tooltip, and what clicking it does.</summary>
    public sealed class DialogLink
    {
        public string Text { get; set; }
        public string Tooltip { get; set; }
        public Action Open { get; set; }
    }
}
