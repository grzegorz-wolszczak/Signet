using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// Converts <c>AppAction.IconKey</c> (e.g. <c>"document-new"</c>) to a colored image
/// (<see cref="IImage"/>, in practice a <see cref="DrawingImage"/>) from the built-in icon set
/// loaded by <see cref="IconThemeManager"/>. For the "Custom" set (plain
/// <see cref="Geometry"/> only) it returns <see langword="null"/> — the icon is then drawn by
/// <c>PathIcon</c> through <see cref="IconKeyToGeometryConverter"/>.
/// </summary>
public sealed class IconKeyToImageConverter : IValueConverter
{
    /// <summary>Shared instance for use in XAML (<c>{x:Static ...}</c>).</summary>
    public static readonly IconKeyToImageConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } key || Application.Current is not { } app)
        {
            return null;
        }

        return app.TryFindResource(key, out object? resource) && resource is IImage image ? image : null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
