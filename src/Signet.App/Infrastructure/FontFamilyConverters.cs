using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// Converts a font name stored in the settings (text, including a fallback list "A, B") to
/// <see cref="FontFamily"/>. A compiled binding <c>FontFamily="{Binding Name}"</c> does not
/// convert the text on its own — it would silently stay with the default font.
/// </summary>
public static class FontFamilyConverters
{
    /// <summary>Text → <see cref="FontFamily"/>; empty text → <see cref="FontFamily.Default"/>.</summary>
    public static readonly IValueConverter FromName =
        new FuncValueConverter<string?, FontFamily>(
            name => string.IsNullOrWhiteSpace(name) ? FontFamily.Default : FontFamily.Parse(name));
}
