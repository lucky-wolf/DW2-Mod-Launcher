using System;
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
            ModList.DoubleTapped += delegate { if (DataContext is MainViewModel m && m.ModSettingsCommand.CanExecute(null)) m.ModSettingsCommand.Execute(null); };
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
