using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// Converters from <c>LiveCssDeclarationViewModel</c> flags to declaration appearance in the Live CSS panel.
/// </summary>
public static class OverriddenDeclarationConverters
{
    /// <summary><c>true</c> → <see cref="TextDecorations.Strikethrough"/>, otherwise no decoration.</summary>
    public static readonly IValueConverter Strikethrough =
        new FuncValueConverter<bool, TextDecorationCollection?>(
            overridden => overridden ? TextDecorations.Strikethrough : null);

    /// <summary><c>true</c> → italic font style, otherwise normal.</summary>
    public static readonly IValueConverter ItalicWhenRepeated =
        new FuncValueConverter<bool, FontStyle>(
            repeated => repeated ? FontStyle.Italic : FontStyle.Normal);

    /// <summary><c>true</c> → 0.55 (dimmed), otherwise 1.0.</summary>
    public static readonly IValueConverter Opacity =
        new FuncValueConverter<bool, double>(isDimmed => isDimmed ? 0.55 : 1.0);
}
