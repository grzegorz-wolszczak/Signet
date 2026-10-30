using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Signet.App.Infrastructure;

namespace Signet.App.Views;

/// <summary>
/// A grip at the bottom-right corner of a control (e.g. a multi-line text box) that the user drags to change the
/// control's height, like the corner of a text area in a web browser. Put it right under the control (e.g. in the next
/// grid row), so that the control's scroll bar never covers it.
/// </summary>
public sealed class HeightResizeGrip : Thumb
{
    /// <summary>The control whose height the grip changes.</summary>
    public static readonly StyledProperty<Control?> TargetProperty =
        AvaloniaProperty.Register<HeightResizeGrip, Control?>(nameof(Target));

    /// <summary>The smallest height the grip gives the control.</summary>
    public static readonly StyledProperty<double> MinTargetHeightProperty =
        AvaloniaProperty.Register<HeightResizeGrip, double>(nameof(MinTargetHeight), 48);

    /// <summary>Creates the grip: diagonal strokes and the vertical resize cursor.</summary>
    public HeightResizeGrip()
    {
        Width = 12;
        Height = 12;
        Margin = new Thickness(0, 1, 1, 0);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        Template = new FuncControlTemplate<HeightResizeGrip>((_, _) =>
        {
            Path path = new() { Data = Geometry.Parse("M 11,2 L 2,11 M 11,6 L 6,11 M 11,10 L 10,11"), StrokeThickness = 1 };
            path.Bind(Shape.StrokeProperty, path.GetResourceObservable(FluentResourceKeys.SystemControlForegroundBaseMediumBrush));
            return new Border { Background = Brushes.Transparent, Child = path };
        });
        DragDelta += OnDragDelta;
    }

    /// <summary>Raised after the user changed the height of <see cref="Target"/>.</summary>
    public event EventHandler? Resized;

    /// <summary>The control whose height the grip changes.</summary>
    public Control? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>The smallest height the grip gives the control.</summary>
    public double MinTargetHeight
    {
        get => GetValue(MinTargetHeightProperty);
        set => SetValue(MinTargetHeightProperty, value);
    }

    /// <summary>Changes the height of <see cref="Target"/> by <paramref name="delta"/> (not below the minimum).</summary>
    public void ResizeBy(double delta)
    {
        if (Target is not { } target)
        {
            return;
        }

        double current = double.IsNaN(target.Height) ? target.Bounds.Height : target.Height;
        target.Height = Math.Max(MinTargetHeight, current + delta);
        Resized?.Invoke(this, EventArgs.Empty);
    }

    // The grip moves with the bottom edge of the control, so each delta is the change since the previous one.
    private void OnDragDelta(object? sender, VectorEventArgs e) => ResizeBy(e.Vector.Y);
}
