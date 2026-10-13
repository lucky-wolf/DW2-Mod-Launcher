using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DW2ModLauncher.Avalonia.ViewModels;

namespace DW2ModLauncher.Avalonia.Views
{
    public partial class ModsView : UserControl
    {
        // The drag is run in-app (pointer capture + a rendered ghost of the row + an insertion line)
        // rather than through DragDrop.DoDragDropAsync, which is the OS drag and can't show a drag image.
        private const double DragThreshold = 6;
        private const double GapHeight = 6; // rows at/after the drop point slide down by this much

        private ModRowViewModel dragRow;
        private Point pressPoint;
        private Point grabOffset;
        private bool dragging;
        private int insertIndex;
        private Control sourceContainer;
        private RenderTargetBitmap ghostBitmap;

        public ModsView()
        {
            InitializeComponent();
            ModList.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            ModList.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            ModList.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
            ModList.AddHandler(PointerCaptureLostEvent, delegate { EndDrag(false); }, RoutingStrategies.Tunnel);
            ModList.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            PreviewBorder.DoubleTapped += delegate { if (DataContext is MainViewModel m && m.OpenSteamPageCommand.CanExecute(null)) m.OpenSteamPageCommand.Execute(null); };
            // Bring the remembered selection into view once the list has been laid out (a restored row can be far down).
            Loaded += delegate { global::Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToSelection, global::Avalonia.Threading.DispatcherPriority.Background); };
            DataContextChanged += delegate
            {
                if (DataContext is MainViewModel main)
                    main.RevealSelectionRequested += delegate { global::Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToSelection, global::Avalonia.Threading.DispatcherPriority.Background); };
            };
            ModList.DoubleTapped += delegate { if (DataContext is MainViewModel m && m.ModSettingsCommand.CanExecute(null)) m.ModSettingsCommand.Execute(null); };
            DetailsToggle.Click += delegate { SetDetailsCollapsed(DetailsPane.IsVisible); RememberCollapsed(); };
            ToolbarGrid.SizeChanged += delegate { PlaceSplitter(); };
            // The split is remembered when a drag ends; collapsed / open is a separate flag, so hiding the pane never overwrites the width.
            DetailsSplitter.DragCompleted += delegate { RememberSplit(); };
            Loaded += delegate { ApplySavedLayout(); };
            HeaderPanel.AddHandler(PointerPressedEvent, OnHeaderPressed, RoutingStrategies.Tunnel);
            HeaderPanel.AddHandler(PointerMovedEvent, OnHeaderMoved, RoutingStrategies.Tunnel);
            HeaderPanel.AddHandler(PointerReleasedEvent, OnHeaderReleased, RoutingStrategies.Tunnel);
            // Taking the capture from the pressed header button raises a capture-lost on the button first; only the panel's own counts.
            HeaderPanel.AddHandler(PointerCaptureLostEvent, delegate (object s, PointerCaptureLostEventArgs e) { if (e.Source == HeaderPanel) EndColumnDrag(); }, RoutingStrategies.Tunnel);
        }

        // ---- Dragging a column header to reorder the mod list's columns (the order is saved with the settings).

        private string draggedColumn;
        private Point columnPress;
        private bool columnDragging;
        private int columnTarget;

        private void OnHeaderPressed(object sender, PointerPressedEventArgs e)
        {
            draggedColumn = null;
            if (!e.GetCurrentPoint(HeaderPanel).Properties.IsLeftButtonPressed) return;
            Button header = (e.Source as Visual)?.FindAncestorOfType<Button>(true);
            if (header == null) return;
            draggedColumn = ColumnRowPanel.GetColumnId(header);
            columnPress = e.GetPosition(HeaderPanel);
        }

        private void OnHeaderMoved(object sender, PointerEventArgs e)
        {
            if (draggedColumn == null) return;
            Point at = e.GetPosition(HeaderPanel);
            if (!columnDragging)
            {
                if (Math.Abs(at.X - columnPress.X) < DragThreshold) return;
                columnDragging = true;
                e.Pointer.Capture(HeaderPanel);
                foreach (Control cell in HeaderPanel.InLayoutOrder())
                {
                    if (ColumnRowPanel.GetColumnId(cell) == draggedColumn) cell.Opacity = 0.5;
                }
            }
            List<Control> others = HeaderPanel.InLayoutOrder().Where(c => ColumnRowPanel.GetColumnId(c) != draggedColumn).ToList();
            columnTarget = others.Count(c => c.Bounds.X + c.Bounds.Width / 2 < at.X);
            double lineX = others.Count == 0 ? 0 : columnTarget < others.Count ? others[columnTarget].Bounds.X : others[others.Count - 1].Bounds.Right;
            ColumnDropLine.Margin = new Thickness(Math.Max(0, lineX - 1), 0, 0, 0);
            ColumnDropLine.IsVisible = true;
        }

        private void OnHeaderReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!columnDragging)
            {
                draggedColumn = null;
                return;
            }
            // a drag is not a click on the header (sorting)
            e.Handled = true;
            string id = draggedColumn;
            int index = columnTarget;
            EndColumnDrag();
            if (DataContext is MainViewModel main) main.MoveColumn(id, index);
        }

        private void EndColumnDrag()
        {
            columnDragging = false;
            draggedColumn = null;
            ColumnDropLine.IsVisible = false;
            foreach (Control cell in HeaderPanel.Children) cell.Opacity = 1;
        }

        private bool layoutApplied;
        private const double MinDetailsFraction = 0.15;
        private const double MaxDetailsFraction = 0.7;

        /// <summary>Puts the split and the collapsed state back as they were when the launcher last closed.</summary>
        private void ApplySavedLayout()
        {
            if (layoutApplied || DataContext is not MainViewModel main) return;
            layoutApplied = true;
            if (main.DetailsFraction is double saved)
            {
                double f = Math.Clamp(saved, MinDetailsFraction, MaxDetailsFraction);
                RootGrid.ColumnDefinitions[0].Width = new GridLength(1 - f, GridUnitType.Star);
                RootGrid.ColumnDefinitions[2].Width = new GridLength(f, GridUnitType.Star);
            }
            PlaceSplitter();
            if (main.DetailsCollapsed) SetDetailsCollapsed(true);
        }

        /// <summary>The splitter starts level with the list, below the toolbar row (which the details pane shares).</summary>
        private void PlaceSplitter()
        {
            DetailsSplitter.Margin = new Thickness(0, ToolbarGrid.Bounds.Height + ToolbarGrid.Margin.Bottom, 0, 0);
        }

        private void RememberSplit()
        {
            if (DataContext is not MainViewModel main || !DetailsPane.IsVisible) return;
            double list = RootGrid.ColumnDefinitions[0].ActualWidth;
            double details = RootGrid.ColumnDefinitions[2].ActualWidth;
            if (list + details <= 0) return;
            main.SetDetailsFraction(details / (list + details));
        }

        private void RememberCollapsed()
        {
            if (DataContext is MainViewModel main) main.SetDetailsCollapsed(!DetailsPane.IsVisible);
        }

        private const double DetailsMinWidth = 220;
        private GridLength listWidthBeforeCollapse = new GridLength(7, GridUnitType.Star);
        private GridLength detailsWidthBeforeCollapse = new GridLength(3, GridUnitType.Star);

        /// <summary>Hides the details pane and gives its room to the list, or brings it back at the width it had.</summary>
        private void SetDetailsCollapsed(bool collapsed)
        {
            ColumnDefinition ListColumn = RootGrid.ColumnDefinitions[0];
            ColumnDefinition DetailsColumn = RootGrid.ColumnDefinitions[2];
            if (collapsed)
            {
                listWidthBeforeCollapse = ListColumn.Width;
                detailsWidthBeforeCollapse = DetailsColumn.Width;
                ListColumn.Width = new GridLength(1, GridUnitType.Star);
                DetailsColumn.MinWidth = 0;
                DetailsColumn.Width = new GridLength(0);
            }
            else
            {
                ListColumn.Width = listWidthBeforeCollapse;
                DetailsColumn.MinWidth = DetailsMinWidth;
                DetailsColumn.Width = detailsWidthBeforeCollapse;
            }
            DetailsPane.IsVisible = !collapsed;
            DetailsSplitter.IsVisible = !collapsed;
            // The toggle sits over the right end of the row; with the pane gone the list's toolbar makes room for it.
            ToolbarGrid.Margin = new Thickness(0, 0, collapsed ? DetailsToggle.Bounds.Width + 10 : 0, 10);
            DetailsToggleIcon.Data = Geometry.Parse(collapsed ? "M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z" : "M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z");
        }

        private void ScrollToSelection()
        {
            if (DataContext is not MainViewModel m || m.SelectedRow == null) return;
            ModList.ScrollIntoView(m.SelectedRow);
            // Then centre the row in the viewport where there is room (the offset is clamped, so rows near either end just stay put).
            ModList.UpdateLayout();
            ScrollViewer scroller = ModList.FindDescendantOfType<ScrollViewer>();
            int index = m.Mods.IndexOf(m.SelectedRow);
            if (scroller == null || index < 0 || ModList.ContainerFromIndex(index) is not Control row) return;
            Point top = row.TranslatePoint(new Point(0, 0), scroller) ?? new Point(0, 0);
            double target = scroller.Offset.Y + top.Y + row.Bounds.Height / 2 - scroller.Viewport.Height / 2;
            double max = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, Math.Min(target, max)));
        }

        private static ModRowViewModel RowAt(Visual source)
        {
            ListBoxItem item = source?.FindAncestorOfType<ListBoxItem>(true);
            return item?.DataContext as ModRowViewModel;
        }

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            dragRow = null;
            if (!e.GetCurrentPoint(ModList).Properties.IsLeftButtonPressed) return;
            // A click on the enable/disable glyph is a toggle, not the start of a drag.
            if ((e.Source as Visual)?.FindAncestorOfType<Button>(true) != null) return;
            dragRow = RowAt(e.Source as Visual);
            pressPoint = e.GetPosition(ModList);
        }

        private void OnMoved(object sender, PointerEventArgs e)
        {
            if (dragRow == null) return;
            if (!e.GetCurrentPoint(ModList).Properties.IsLeftButtonPressed) { EndDrag(false); return; }
            Point now = e.GetPosition(ModList);
            if (!dragging)
            {
                if (Math.Abs(now.X - pressPoint.X) < DragThreshold && Math.Abs(now.Y - pressPoint.Y) < DragThreshold) return;
                if (!BeginDrag(e)) { dragRow = null; return; }
            }
            UpdateDrag(now);
        }

        private bool BeginDrag(PointerEventArgs e)
        {
            int from = IndexOf(dragRow);
            ListBoxItem container = from < 0 ? null : ModList.ContainerFromIndex(from) as ListBoxItem;
            if (container == null || container.Bounds.Width < 1 || container.Bounds.Height < 1) return false;

            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            PixelSize size = new PixelSize((int)Math.Ceiling(container.Bounds.Width * scale), (int)Math.Ceiling(container.Bounds.Height * scale));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bitmap.Render(container);
            ghostBitmap = bitmap;
            GhostImage.Source = bitmap;
            Ghost.Width = container.Bounds.Width;
            Ghost.Height = container.Bounds.Height;

            Point origin = container.TranslatePoint(new Point(0, 0), ModList) ?? new Point(0, 0);
            grabOffset = new Point(pressPoint.X - origin.X, pressPoint.Y - origin.Y);

            sourceContainer = container;
            container.Opacity = 0.35;
            ModList.Cursor = new Cursor(StandardCursorType.DragMove);
            e.Pointer.Capture(ModList);
            dragging = true;
            Ghost.IsVisible = true;
            DropLine.IsVisible = true;
            return true;
        }

        private void UpdateDrag(Point now)
        {
            Canvas.SetLeft(Ghost, 0);
            Canvas.SetTop(Ghost, now.Y - grabOffset.Y);

            // Insertion point: before the first row whose midpoint is below the pointer, else after the last.
            int count = ModList.ItemCount;
            insertIndex = count;
            double lineY = 0;
            bool haveLine = false;
            for (int i = 0; i < count; i++)
            {
                if (ModList.ContainerFromIndex(i) is not Control c) continue;
                Point top = c.TranslatePoint(new Point(0, 0), ModList) ?? new Point(0, 0);
                if (now.Y < top.Y + c.Bounds.Height / 2) { insertIndex = i; lineY = top.Y; haveLine = true; break; }
                lineY = top.Y + c.Bounds.Height;
                haveLine = true;
            }
            ApplyGap(insertIndex < count ? insertIndex : int.MaxValue);
            // Line sits in the middle of the gap; at the very end there is no gap, so it hugs the last row.
            double lineTop = insertIndex < count ? lineY + (GapHeight - DropLine.Height) / 2 : lineY - DropLine.Height;
            DropLine.Width = ModList.Bounds.Width;
            Canvas.SetLeft(DropLine, 0);
            Canvas.SetTop(DropLine, Math.Max(0, Math.Min(lineTop, ModList.Bounds.Height - DropLine.Height)));
            DropLine.IsVisible = haveLine;

            AutoScroll(now.Y);
        }

        // Opens the visual gap by translating the realized rows (render-only; layout and hit-testing are unchanged).
        private void ApplyGap(int gapBefore)
        {
            for (int i = 0; i < ModList.ItemCount; i++)
            {
                if (ModList.ContainerFromIndex(i) is not Control c) continue;
                c.RenderTransform = i >= gapBefore ? new TranslateTransform(0, GapHeight) : null;
            }
        }

        private void AutoScroll(double y)
        {
            ScrollViewer scroller = ModList.FindDescendantOfType<ScrollViewer>();
            if (scroller == null) return;
            const double edge = 36, step = 18;
            if (y < edge) scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y - step));
            else if (y > ModList.Bounds.Height - edge) scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + step);
        }

        private void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            // Only release the capture after a real drag: doing it on a plain click cancels the enable button's own click.
            bool wasDragging = dragging;
            EndDrag(wasDragging);
            if (wasDragging) e.Pointer.Capture(null);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && dragging) EndDrag(false);
        }

        private void EndDrag(bool drop)
        {
            ModRowViewModel moving = dragRow;
            int ins = insertIndex;
            bool wasDragging = dragging;
            dragRow = null;
            dragging = false;

            Ghost.IsVisible = false;
            DropLine.IsVisible = false;
            GhostImage.Source = null;
            ghostBitmap?.Dispose();
            ghostBitmap = null;
            ModList.Cursor = Cursor.Default;
            ApplyGap(int.MaxValue);
            if (sourceContainer != null) sourceContainer.Opacity = 1;
            sourceContainer = null;

            if (!wasDragging || !drop || moving == null || DataContext is not MainViewModel main) return;
            int from = main.Mods.IndexOf(moving);
            if (from < 0) return;
            main.MoveRow(moving, ins > from ? ins - 1 : ins);
        }

        private int IndexOf(ModRowViewModel row)
        {
            return DataContext is MainViewModel main ? main.Mods.IndexOf(row) : -1;
        }
    }
}
