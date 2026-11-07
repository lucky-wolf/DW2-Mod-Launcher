using System;
using System.Collections.Generic;
using Avalonia.Controls;
using DW2ModLauncher.Avalonia.Services;
using DW2ModLauncher.Core.Diagnostics;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>A message box with clickable links under the text (one line each) and a Close button.</summary>
    public partial class LinkMessageDialog : Window
    {
        public LinkMessageDialog()
        {
            InitializeComponent();
        }

        public LinkMessageDialog(string message, string title, IList<DialogLink> links, string closeText) : this()
        {
            Title = title;
            MessageText.Text = message;
            CloseButton.Content = closeText;
            CloseButton.Click += delegate { Close(); };
            foreach (DialogLink link in links)
            {
                Button button = new Button { Padding = new global::Avalonia.Thickness(0), Background = global::Avalonia.Media.Brushes.Transparent, BorderThickness = new global::Avalonia.Thickness(0), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
                button.Classes.Add("link");
                TextBlock text = new TextBlock { Text = link.Text, TextDecorations = global::Avalonia.Media.TextDecorations.Underline };
                text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Dw2BlueGlow"));
                button.Content = text;
                ToolTip.SetTip(button, link.Tooltip);
                button.Click += delegate
                {
                    try { link.Open(); }
                    catch (Exception ex) { Logger.LogException("Open link " + link.Text, ex); }
                };
                LinksPanel.Children.Add(button);
            }
        }
    }
}
