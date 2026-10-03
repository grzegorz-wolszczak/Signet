using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Signet.App.Toolbars;

/// <summary>
/// A row of toolbars laid out side by side. When they do not fit entirely, each gets at least
/// its minimum (first button + "»") and the remaining width is handed out to toolbars from the
/// left. Truncated toolbars show the rest of their buttons in the "»" menu
/// (<see cref="ToolbarOverflowPanel"/>).
/// </summary>
public sealed class ToolbarRowPanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        int n = Children.Count;
        double[] full = new double[n];
        double[] min = new double[n];
        Size unbounded = new(double.PositiveInfinity, availableSize.Height);

        for (int i = 0; i < n; i++)
        {
            Control child = Children[i];
            child.Measure(unbounded);
            full[i] = child.DesiredSize.Width;
            min[i] = full[i];

            // Toolbar chrome (margins, spacing) = full width − button panel content.
            if (full[i] > 0 && (child as ToolbarOverflowPanel ?? child.GetVisualDescendants().OfType<ToolbarOverflowPanel>().FirstOrDefault()) is { } strip)
            {
                min[i] = full[i] - strip.FullContentWidth + strip.MinContentWidth;
            }
        }

        double[] width = (double[])full.Clone();
        if (full.Sum() > availableSize.Width)
        {
            double remaining = availableSize.Width - min.Sum();
            for (int i = 0; i < n; i++)
            {
                double extra = Math.Clamp(full[i] - min[i], 0, Math.Max(remaining, 0));
                width[i] = min[i] + extra;
                remaining -= extra;
            }
        }

        double height = 0;
        for (int i = 0; i < n; i++)
        {
            Children[i].Measure(new Size(width[i], availableSize.Height));
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }

        return new Size(Math.Min(width.Sum(), availableSize.Width), height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (Control child in Children)
        {
            double w = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }

        return finalSize;
    }
}
