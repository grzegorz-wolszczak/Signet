using System.Collections.Generic;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>The fixed grid of basic colors offered in the color picker window.</summary>
public static class BasicColorPalette
{
    /// <summary>Columns of the grid.</summary>
    public const int Columns = 12;

    /// <summary>
    /// Four rows of <see cref="Columns"/> colors, row-major: a grey ramp from white to black, then the twelve hues
    /// (every 30°) as light, pure and dark shades.
    /// </summary>
    public static IReadOnlyList<Color> Colors { get; } = Build();

    private static List<Color> Build()
    {
        List<Color> colors = new(Columns * 4);
        for (int i = 0; i < Columns; i++)
        {
            byte grey = (byte)(255 - (i * 255 / (Columns - 1)));
            colors.Add(Color.FromRgb(grey, grey, grey));
        }

        (double Saturation, double Value)[] shades = [(0.35, 1.0), (1.0, 1.0), (1.0, 0.55)];
        foreach ((double saturation, double value) in shades)
        {
            for (int i = 0; i < Columns; i++)
            {
                colors.Add(new HsvColor(1, i * 360.0 / Columns, saturation, value).ToRgb());
            }
        }

        return colors;
    }
}
