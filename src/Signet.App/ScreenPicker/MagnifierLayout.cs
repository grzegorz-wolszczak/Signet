using System;
using Avalonia;

namespace Signet.App.ScreenPicker;

/// <summary>Where the magnifier panel goes relative to the cursor.</summary>
public static class MagnifierLayout
{
    /// <summary>
    /// The top-left corner of a panel of size <paramref name="panel"/> placed diagonally below-right of
    /// <paramref name="cursor"/> (<paramref name="gap"/> away); flipped to the other side of the cursor on an axis
    /// where it would not fit inside <paramref name="bounds"/>, and clamped to the bounds as a last resort.
    /// </summary>
    public static Point Place(Point cursor, Size panel, Size bounds, double gap = 24)
    {
        double x = cursor.X + gap;
        if (x + panel.Width > bounds.Width)
        {
            x = cursor.X - gap - panel.Width;
        }

        double y = cursor.Y + gap;
        if (y + panel.Height > bounds.Height)
        {
            y = cursor.Y - gap - panel.Height;
        }

        return new Point(
            Math.Max(0, Math.Min(x, Math.Max(0, bounds.Width - panel.Width))),
            Math.Max(0, Math.Min(y, Math.Max(0, bounds.Height - panel.Height))));
    }
}
