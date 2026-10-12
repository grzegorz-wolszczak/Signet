namespace Signet.Core.Themes;

/// <summary>How a fragment of text is marked besides its colors (IntelliJ's <c>EffectType</c>).</summary>
public enum EffectType
{
    /// <summary>No effect.</summary>
    None,

    /// <summary>A thin line under the text.</summary>
    Underline,

    /// <summary>A thick line under the text.</summary>
    BoldUnderline,

    /// <summary>A wavy line under the text (e.g. a spelling error).</summary>
    Wave,

    /// <summary>A dotted line under the text.</summary>
    DottedUnderline,

    /// <summary>A dashed line under the text (e.g. a special space).</summary>
    DashedUnderline,

    /// <summary>A frame around the text (e.g. the marked search results).</summary>
    Box,

    /// <summary>A line through the text.</summary>
    Strikeout,
}

/// <summary>
/// How a fragment of text looks (IntelliJ's <c>TextAttributes</c>). Every part is optional: an unset color leaves the
/// color of the text around (or no background), an unset <see cref="Bold"/> / <see cref="Italic"/> the normal weight
/// and style.
/// </summary>
public sealed record TextAttributes
{
    /// <summary>Attributes that set nothing.</summary>
    public static TextAttributes Empty { get; } = new();

    /// <summary>Text color.</summary>
    public ThemeColor? Foreground { get; init; }

    /// <summary>Background color.</summary>
    public ThemeColor? Background { get; init; }

    /// <summary>Bold text.</summary>
    public bool Bold { get; init; }

    /// <summary>Italic text.</summary>
    public bool Italic { get; init; }

    /// <summary>The effect (a line under or through the text, a frame).</summary>
    public EffectType Effect { get; init; }

    /// <summary>The color of the effect; unset = the text color.</summary>
    public ThemeColor? EffectColor { get; init; }
}
