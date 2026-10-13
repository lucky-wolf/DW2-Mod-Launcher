using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DW2ModLauncher.Core.Services;

namespace DW2ModLauncher.Avalonia.Views
{
    /// <summary>
    /// The mod list's column order, shared by the header row and every mod row so they always line up. Changing it re-lays-out
    /// every <see cref="ColumnRowPanel"/> that is on screen.
    /// </summary>
    public static class ModColumnLayout
    {
        private static IReadOnlyList<string> order = ModListColumns.Default;

        public static IReadOnlyList<string> Order { get { return order; } }

        /// <summary>Raised after <see cref="Order"/> changes.</summary>
        public static event Action Changed;

        public static void Set(IEnumerable<string> newOrder)
        {
            order = ModListColumns.Normalize(newOrder);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// A row of cells laid out left to right in the order of <see cref="ModColumnLayout.Order"/>. Each child names its column with
    /// <see cref="ColumnIdProperty"/>. Every column has a fixed width except the name, which takes the rest.
    /// </summary>
    public class ColumnRowPanel : Panel
    {
        public static readonly AttachedProperty<string> ColumnIdProperty =
            AvaloniaProperty.RegisterAttached<ColumnRowPanel, Control, string>("ColumnId");

        public static string GetColumnId(Control control) { return control.GetValue(ColumnIdProperty); }

        public static void SetColumnId(Control control, string value) { control.SetValue(ColumnIdProperty, value); }

        private static readonly Dictionary<string, double> FixedWidths = new Dictionary<string, double>
        {
            [ModListColumns.State] = 90,
            [ModListColumns.Source] = 150,
            [ModListColumns.Author] = 110,
        };

        static ColumnRowPanel()
        {
            ColumnIdProperty.Changed.AddClassHandler<Control>((control, e) => (control.GetVisualParent() as ColumnRowPanel)?.InvalidateMeasure());
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            ModColumnLayout.Changed += OnLayoutChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ModColumnLayout.Changed -= OnLayoutChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLayoutChanged()
        {
            InvalidateMeasure();
        }

        /// <summary>The cells in the order they are laid out; cells with an unknown column id are left out.</summary>
        public IEnumerable<Control> InLayoutOrder()
        {
            foreach (string id in ModColumnLayout.Order)
            {
                Control child = Children.FirstOrDefault(c => GetColumnId(c) == id);
                if (child != null) yield return child;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double used = 0;
            double height = 0;
            Control star = null;
            foreach (Control child in InLayoutOrder())
            {
                if (!FixedWidths.TryGetValue(GetColumnId(child), out double width))
                {
                    star = child;
                    continue;
                }
                child.Measure(new Size(width, double.PositiveInfinity));
                used += width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
            double rest = double.IsInfinity(availableSize.Width) ? 0 : Math.Max(0, availableSize.Width - used);
            if (star != null)
            {
                star.Measure(new Size(rest, double.PositiveInfinity));
                height = Math.Max(height, star.DesiredSize.Height);
                used += double.IsInfinity(availableSize.Width) ? star.DesiredSize.Width : rest;
            }
            return new Size(used, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double fixedTotal = 0;
            foreach (Control child in InLayoutOrder())
            {
                if (FixedWidths.TryGetValue(GetColumnId(child), out double w)) fixedTotal += w;
            }
            double x = 0;
            foreach (Control child in InLayoutOrder())
            {
                double width = FixedWidths.TryGetValue(GetColumnId(child), out double fixedWidth) ? fixedWidth : Math.Max(0, finalSize.Width - fixedTotal);
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width;
            }
            return finalSize;
        }
    }
}
