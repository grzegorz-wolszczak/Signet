using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// Converters from <c>LiveCssDeclarationViewModel.IsOverridden</c> to the row appearance in the "Live CSS Panel"
/// — strikethrough + dimming of declarations overridden by a rule with a higher cascade
/// priority.
/// </summary>
public static class OverriddenDeclarationConverters
{
    /// <summary><c>true</c> → <see cref="TextDecorations.Strikethrough"/>, otherwise no decoration.</summary>
    public static readonly IValueConverter Strikethrough =
        new FuncValueConverter<bool, TextDecorationCollection?>(
            overridden => overridden ? TextDecorations.Strikethrough : null);

    /// <summary><c>true</c> → 0.55 (dimmed), otherwise 1.0.</summary>
    public static readonly IValueConverter Opacity =
        new FuncValueConverter<bool, double>(overridden => overridden ? 0.55 : 1.0);
}
