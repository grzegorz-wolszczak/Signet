using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// Converts a color stored as text (<c>#RRGGBB</c>, color name) to brushes — the color fields
/// in Preferences have a background in the color they describe. Invalid text yields
/// <see cref="AvaloniaProperty.UnsetValue"/>, so the field falls back to the theme's appearance.
/// </summary>
public static class ColorConverters
{
    /// <summary>Color text → brush in that color.</summary>
    public static readonly IValueConverter ToBrush = new Converter(color => new SolidColorBrush(color));

    /// <summary>Color text → black or white brush, readable on a background of that color.</summary>
    public static readonly IValueConverter ToContrastingForeground =
        new Converter(color => IsLight(color) ? Brushes.Black : Brushes.White);

    /// <summary>Whether the color is light (WCAG relative luminance above the equal-readability threshold).</summary>
    public static bool IsLight(Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double luminance = (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
        // The point where the contrast with black and with white is equal: (L + 0.05)^2 = 1.05 * 0.05.
        return luminance > 0.179;
    }

    private sealed class Converter(Func<Color, IBrush> toBrush) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string text && Color.TryParse(text.Trim(), out Color color)
                ? toBrush(color)
                : AvaloniaProperty.UnsetValue;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
