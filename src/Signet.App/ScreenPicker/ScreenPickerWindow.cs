using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Signet.App.ScreenPicker;

/// <summary>
/// A borderless, topmost window covering one monitor and showing the frozen snapshot of it. A magnifier next to the
/// pointer shows the 9 × 9 pixels around it; a click picks the center pixel, Esc / right click cancels, the arrow keys
/// move the sampled pixel by one.
/// </summary>
internal sealed class ScreenPickerWindow : Window
{
    private readonly PickerSurface _surface;

    /// <summary>Creates the overlay for <paramref name="screen"/>.</summary>
    /// <param name="snapshot">The desktop snapshot.</param>
    /// <param name="bitmap">The snapshot as a bitmap (owned by the caller, shared between the overlays).</param>
    /// <param name="screen">The monitor to cover.</param>
    /// <param name="hint">The hint line shown in the magnifier.</param>
    /// <param name="onPicked">Called with the picked color, or <see langword="null"/> on cancel.</param>
    public ScreenPickerWindow(ScreenSnapshot snapshot, Bitmap bitmap, Screen screen, string hint, Action<Color?> onPicked)
    {
        _surface = new PickerSurface(snapshot, bitmap, screen.Bounds, hint, onPicked);
        Content = _surface;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _surface.Cancel();
                break;
            case Key.Enter or Key.Space:
                _surface.PickCurrent();
                break;
            case Key.Left:
                _surface.Nudge(-1, 0);
                break;
            case Key.Right:
                _surface.Nudge(1, 0);
                break;
            case Key.Up:
                _surface.Nudge(0, -1);
                break;
            case Key.Down:
                _surface.Nudge(0, 1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The control that draws the snapshot and the magnifier.</summary>
    private sealed class PickerSurface : Control
    {
        private const int Cells = 9;
        private const double CellSize = 14;
        private const double Padding = 8;

        private readonly ScreenSnapshot _snapshot;
        private readonly Bitmap _bitmap;
        private readonly PixelRect _screenBounds;
        private readonly string _hint;
        private readonly Action<Color?> _onPicked;
        private Point _pointer;
        private PixelPoint _sample;
        private bool _hasPointer;

        public PickerSurface(ScreenSnapshot snapshot, Bitmap bitmap, PixelRect screenBounds, string hint, Action<Color?> onPicked)
        {
            _snapshot = snapshot;
            _bitmap = bitmap;
            _screenBounds = screenBounds;
            _hint = hint;
            _onPicked = onPicked;
            ClipToBounds = true;
        }

        private double Scaling => (TopLevel.GetTopLevel(this)?.RenderScaling) ?? 1;

        public void Cancel() => _onPicked(null);

        public void PickCurrent()
        {
            if (_hasPointer && _snapshot.Contains(_sample.X, _sample.Y))
            {
                _onPicked(_snapshot.GetPixel(_sample.X, _sample.Y));
            }
        }

        public void Nudge(int dx, int dy)
        {
            if (_hasPointer)
            {
                _sample = new PixelPoint(_sample.X + dx, _sample.Y + dy);
                InvalidateVisual();
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            _pointer = e.GetPosition(this);
            double scaling = Scaling;
            _sample = new PixelPoint(
                _screenBounds.X + (int)Math.Floor(_pointer.X * scaling),
                _screenBounds.Y + (int)Math.Floor(_pointer.Y * scaling));
            _hasPointer = true;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hasPointer = false;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsLeftButtonPressed)
            {
                // Make sure the sample matches the press position even when no move was seen first.
                OnPointerMoved(e);
                PickCurrent();
            }
            else if (properties.IsRightButtonPressed)
            {
                Cancel();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect destination = new(Bounds.Size);
            Rect source = new(_screenBounds.X - _snapshot.OriginX, _screenBounds.Y - _snapshot.OriginY, _screenBounds.Width, _screenBounds.Height);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            {
                context.DrawImage(_bitmap, source, destination);
            }

            if (!_hasPointer)
            {
                return;
            }

            DrawSampleMarker(context);
            DrawMagnifier(context);
        }

        // Outlines the sampled pixel on the screen, so a keyboard nudge is visible.
        private void DrawSampleMarker(DrawingContext context)
        {
            double scaling = Scaling;
            double size = Math.Max(1 / scaling, 1);
            Rect pixel = new((_sample.X - _screenBounds.X) / scaling, (_sample.Y - _screenBounds.Y) / scaling, size, size);
            context.DrawRectangle(null, new Pen(Brushes.Black, 1), pixel.Inflate(1));
            context.DrawRectangle(null, new Pen(Brushes.White, 1), pixel.Inflate(2));
        }

        private void DrawMagnifier(DrawingContext context)
        {
            Color[] region = _snapshot.GetRegion(_sample.X, _sample.Y, Cells);
            Color center = region[(Cells / 2 * Cells) + (Cells / 2)];
            bool valid = center.A != 0;
            string hex = valid ? string.Create(CultureInfo.InvariantCulture, $"#{center.R:X2}{center.G:X2}{center.B:X2}") : "—";
            string rgb = valid ? string.Create(CultureInfo.InvariantCulture, $"RGB {center.R}, {center.G}, {center.B}") : string.Empty;

            FormattedText hexText = Text(hex, 16, FontWeight.Bold);
            FormattedText rgbText = Text(rgb, 12, FontWeight.Normal);
            FormattedText hintText = Text(_hint, 11, FontWeight.Normal, Brushes.Gray);
            double gridSize = Cells * CellSize;
            double width = Math.Max(gridSize, Math.Max(hintText.Width, Math.Max(hexText.Width, rgbText.Width) + 28)) + (2 * Padding);
            double height = gridSize + hexText.Height + rgbText.Height + hintText.Height + (2 * Padding) + 12;
            Point origin = MagnifierLayout.Place(_pointer, new Size(width, height), Bounds.Size);

            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x20, 0x20)), new Pen(Brushes.White, 1), new Rect(origin, new Size(width, height)), 4, 4);

            Point grid = origin + new Point(Padding, Padding);
            for (int row = 0; row < Cells; row++)
            {
                for (int column = 0; column < Cells; column++)
                {
                    Color color = region[(row * Cells) + column];
                    Rect cell = new(grid.X + (column * CellSize), grid.Y + (row * CellSize), CellSize, CellSize);
                    context.FillRectangle(color.A == 0 ? Brushes.Black : new SolidColorBrush(color), cell);
                }
            }

            Pen gridPen = new(new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)), 1);
            for (int i = 0; i <= Cells; i++)
            {
                context.DrawLine(gridPen, new Point(grid.X + (i * CellSize), grid.Y), new Point(grid.X + (i * CellSize), grid.Y + gridSize));
                context.DrawLine(gridPen, new Point(grid.X, grid.Y + (i * CellSize)), new Point(grid.X + gridSize, grid.Y + (i * CellSize)));
            }

            Rect middle = new(grid.X + (Cells / 2 * CellSize), grid.Y + (Cells / 2 * CellSize), CellSize, CellSize);
            context.DrawRectangle(null, new Pen(Brushes.Black, 2), middle.Inflate(1));
            context.DrawRectangle(null, new Pen(Brushes.White, 1), middle.Inflate(1));

            double y = grid.Y + gridSize + 6;
            if (valid)
            {
                context.DrawRectangle(new SolidColorBrush(center), new Pen(Brushes.White, 1), new Rect(grid.X, y + 2, 20, 20), 2, 2);
            }

            context.DrawText(hexText, new Point(grid.X + 28, y));
            context.DrawText(rgbText, new Point(grid.X + 28, y + hexText.Height));
            context.DrawText(hintText, new Point(grid.X, y + hexText.Height + rgbText.Height + 4));
        }

        private static FormattedText Text(string text, double size, FontWeight weight, IBrush? brush = null) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush ?? Brushes.White);
    }
}
