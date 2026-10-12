using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Signet.Core.Themes;

/// <summary>
/// A color of a theme: <c>#RRGGBB</c>, or <c>#AARRGGBB</c> with transparency (e.g. the fills of the diff). The Core
/// layer does not depend on Avalonia; the application converts it to its color type.
/// </summary>
/// <param name="A">Alpha (255 = opaque).</param>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    /// <summary>An opaque color.</summary>
    public static ThemeColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    /// <summary>Whether the color is opaque.</summary>
    public bool IsOpaque => A == 255;

    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c> (hex digits in either case).</summary>
    /// <exception cref="FormatException">The text is not such a color.</exception>
    public static ThemeColor Parse(string text) =>
        TryParse(text, out ThemeColor color) ? color : throw new FormatException($"Not a #RRGGBB or #AARRGGBB color: '{text}'.");

    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c> (hex digits in either case).</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out ThemeColor color)
    {
        color = default;
        if (text is null || text.Length is not (7 or 9) || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        if (text.Length == 7)
        {
            value |= 0xFF000000;
        }

        color = new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary><c>#RRGGBB</c> for an opaque color, <c>#AARRGGBB</c> otherwise (upper-case hex digits).</summary>
    public override string ToString() => IsOpaque
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");
}
