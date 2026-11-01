using System;

namespace Signet.Core.Themes;

/// <summary>
/// Which of black and white is readable on a color — what the color fields of Preferences need, since they write the
/// hex of a color on the color itself.
/// </summary>
/// <remarks>
/// The relative luminance of WCAG 2.x (sRGB linearized, weighted 0.2126 / 0.7152 / 0.0722) decides, at the threshold
/// where black and white contrast equally against the color (≈0.179, not 0.5: the eye is far from linear). A color
/// that is not opaque is composited over the surface it is drawn on first — half-transparent black over a white panel
/// reads as a mid gray, and the text has to be chosen for what is seen, not for what is stored.
/// </remarks>
public static class ColorContrast
{
    // The luminance at which white and black contrast equally: (1.05 / 0.05) ^ 0.5 - 0.05.
    private const double EqualContrastLuminance = 0.1791287847;

    /// <summary>
    /// Black or white — whichever is readable on <paramref name="background"/> drawn over a surface that is
    /// <paramref name="darkSurface"/> (which matters only when the color is not opaque).
    /// </summary>
    public static ThemeColor ReadableOn(ThemeColor background, bool darkSurface) =>
        Luminance(Composite(background, darkSurface)) > EqualContrastLuminance
            ? ThemeColor.FromRgb(0, 0, 0)
            : ThemeColor.FromRgb(255, 255, 255);

    /// <summary>
    /// The color as it is seen: composited over white (or black when <paramref name="darkSurface"/>) by its alpha.
    /// An opaque color is returned unchanged.
    /// </summary>
    public static ThemeColor Composite(ThemeColor color, bool darkSurface)
    {
        if (color.IsOpaque)
        {
            return color;
        }

        double alpha = color.A / 255.0;
        byte surface = darkSurface ? (byte)0 : (byte)255;
        return ThemeColor.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));

        byte Mix(byte channel) => (byte)Math.Round((channel * alpha) + (surface * (1 - alpha)));
    }

    /// <summary>The relative luminance of an opaque color (WCAG 2.x), from 0 (black) to 1 (white).</summary>
    public static double Luminance(ThemeColor color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        double v = value / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
