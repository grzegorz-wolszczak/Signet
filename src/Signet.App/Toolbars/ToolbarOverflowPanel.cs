using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Signet.App.Toolbars;

/// <summary>
/// Button panel of a single toolbar with overflow: when the buttons do not fit in the allotted
/// width, it shows as many as fit (always at least the first one), reserves room for the "»"
/// button (<see cref="ChevronWidth"/>), and exposes the hidden items through
/// <see cref="OverflowedItems"/> for the extension menu.
/// </summary>
public sealed class ToolbarOverflowPanel : Panel
{
    /// <summary>Whether some buttons did not fit (the "»" button is then visible).</summary>
    public static readonly DirectProperty<ToolbarOverflowPanel, bool> HasOverflowProperty =
        AvaloniaProperty.RegisterDirect<ToolbarOverflowPanel, bool>(nameof(HasOverflow), p => p.HasOverflow);

    /// <summary>Width reserved for the "»" button on overflow.</summary>
    public static readonly StyledProperty<double> ChevronWidthProperty =
        AvaloniaProperty.Register<ToolbarOverflowPanel, double>(nameof(ChevronWidth), 16);

    private bool _hasOverflow;
    private int _visibleCount;

    /// <summary>Whether some buttons did not fit.</summary>
    public bool HasOverflow
    {
        get => _hasOverflow;
        private set => SetAndRaise(HasOverflowProperty, ref _hasOverflow, value);
    }

    /// <summary>Width reserved for the "»" button.</summary>
    public double ChevronWidth
    {
        get => GetValue(ChevronWidthProperty);
        set => SetValue(ChevronWidthProperty, value);
    }

    /// <summary>Width of all buttons without overflow (from the last measure pass).</summary>
    public double FullContentWidth { get; private set; }

    /// <summary>Minimum width: first button + "»" (or the full width when there is only one button).</summary>
    public double MinContentWidth { get; private set; }

    /// <summary>Data contexts of the items that did not fit (in toolbar order).</summary>
    public IReadOnlyList<object?> OverflowedItems
    {
        get
        {
            List<object?> items = new();
            for (int i = _visibleCount; i < Children.Count; i++)
            {
                items.Add(Children[i].DataContext);
            }

            return items;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Size unbounded = new(double.PositiveInfinity, availableSize.Height);
        double full = 0;
        double height = 0;
        foreach (Control child in Children)
        {
            child.Measure(unbounded);
            full += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        FullContentWidth = full;
        MinContentWidth = Children.Count > 1 ? Children[0].DesiredSize.Width + ChevronWidth : full;

        if (full <= availableSize.Width || Children.Count <= 1)
        {
            _visibleCount = Children.Count;
            HasOverflow = false;
            return new Size(full, height);
        }

        double limit = availableSize.Width - ChevronWidth;
        double used = 0;
        int count = 0;
        foreach (Control child in Children)
        {
            if (count > 0 && used + child.DesiredSize.Width > limit)
            {
                break;
            }

            used += child.DesiredSize.Width;
            count++;
        }

        _visibleCount = count;
        HasOverflow = count < Children.Count;
        return new Size(used + ChevronWidth, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Control child = Children[i];
            if (i < _visibleCount)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width;
            }
            else
            {
                // Item shown in the "»" menu — takes no space on the toolbar (and cannot be clicked).
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        return finalSize;
    }
}
