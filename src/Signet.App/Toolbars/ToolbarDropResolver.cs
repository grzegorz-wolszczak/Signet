using System.Collections.Generic;
using Avalonia;

namespace Signet.App.Toolbars;

/// <summary>A visible toolbar of a row and its bounds (in the coordinates of the toolbar area).</summary>
public sealed record ToolbarSlot(ToolbarId Id, Rect Bounds);

/// <summary>
/// A visible row of an area. <paramref name="RowIndex"/> is the row's index in the layout kept by
/// <see cref="ToolbarManager"/> (rows without a visible toolbar are not shown, so it can differ from the
/// position among the shown rows).
/// </summary>
public sealed record ToolbarRowSlot(int RowIndex, Rect Bounds, IReadOnlyList<ToolbarSlot> Toolbars);

/// <summary>
/// Where a dragged toolbar lands: in a new row at <paramref name="RowIndex"/> (<paramref name="NewRow"/>), or in the
/// existing row <paramref name="RowIndex"/> before <paramref name="Before"/> (<see langword="null"/> = at the end).
/// <paramref name="Indicator"/> is the rectangle to highlight.
/// </summary>
public readonly record struct ToolbarDropTarget(bool NewRow, int RowIndex, ToolbarId? Before, Rect Indicator);

/// <summary>
/// Turns the pointer position over a toolbar area into a <see cref="ToolbarDropTarget"/>. The top and the bottom
/// part of a row, the gaps between rows and the space above the first and below the last row mean "a new row
/// here"; the middle of a row means "into this row, before the toolbar under the pointer".
/// </summary>
public static class ToolbarDropResolver
{
    private const double EdgeFraction = 0.3;
    private const double IndicatorThickness = 3;

    /// <summary>
    /// Resolves the drop target.
    /// </summary>
    /// <param name="rows">Visible rows, top to bottom (empty when the area shows no toolbar).</param>
    /// <param name="layoutRowCount">Number of rows of the area in the layout (including hidden ones).</param>
    /// <param name="area">Bounds of the whole area.</param>
    /// <param name="point">Pointer position in the coordinates of the area.</param>
    public static ToolbarDropTarget Resolve(
        IReadOnlyList<ToolbarRowSlot> rows, int layoutRowCount, Rect area, Point point)
    {
        if (rows.Count == 0)
        {
            return new ToolbarDropTarget(true, layoutRowCount, null, area);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            ToolbarRowSlot row = rows[i];
            Rect bounds = row.Bounds;

            // Above this row (the previous rows have already claimed everything up to their bottom edge).
            if (point.Y < bounds.Top)
            {
                double y = i == 0 ? bounds.Top : (rows[i - 1].Bounds.Bottom + bounds.Top) / 2;
                return NewRow(row.RowIndex, y, area);
            }

            if (point.Y > bounds.Bottom)
            {
                continue;
            }

            double band = bounds.Height * EdgeFraction;
            if (point.Y < bounds.Top + band)
            {
                return NewRow(row.RowIndex, bounds.Top, area);
            }

            if (point.Y > bounds.Bottom - band)
            {
                return NewRow(row.RowIndex + 1, bounds.Bottom, area);
            }

            return InRow(row, point.X);
        }

        ToolbarRowSlot last = rows[^1];
        return NewRow(last.RowIndex + 1, last.Bounds.Bottom, area);
    }

    private static ToolbarDropTarget NewRow(int rowIndex, double y, Rect area) =>
        new(true, rowIndex, null, new Rect(area.X, System.Math.Max(area.Y, y - IndicatorThickness / 2), area.Width, IndicatorThickness));

    private static ToolbarDropTarget InRow(ToolbarRowSlot row, double x)
    {
        foreach (ToolbarSlot slot in row.Toolbars)
        {
            if (x < slot.Bounds.Center.X)
            {
                return new ToolbarDropTarget(false, row.RowIndex, slot.Id, VerticalBar(slot.Bounds.Left, row.Bounds));
            }
        }

        double end = row.Toolbars.Count > 0 ? row.Toolbars[^1].Bounds.Right : row.Bounds.Left;
        return new ToolbarDropTarget(false, row.RowIndex, null, VerticalBar(end, row.Bounds));
    }

    private static Rect VerticalBar(double x, Rect row) =>
        new(System.Math.Max(0, x - IndicatorThickness / 2), row.Y, IndicatorThickness, row.Height);
}
