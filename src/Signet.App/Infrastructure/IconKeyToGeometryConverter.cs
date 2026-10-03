using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// Converts <c>AppAction.IconKey</c> (e.g. <c>"document-new"</c>) to a <see cref="Geometry"/> from the
/// merged resource dictionary loaded by <see cref="IconThemeManager"/> —
/// used only by the "Custom" set (single-color paths from <c>icons.json</c>); the built-in
/// sets are colored images handled by <see cref="IconKeyToImageConverter"/>. A missing
/// key or a resource of another type yields <see langword="null"/>.
/// </summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    /// <summary>Shared instance for use in XAML (<c>{x:Static ...}</c>).</summary>
    public static readonly IconKeyToGeometryConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } key || Application.Current is not { } app)
        {
            return null;
        }

        return app.TryFindResource(key, out object? resource) && resource is Geometry geometry ? geometry : null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
