using Avalonia.Data.Converters;
using Signet.App.Resources;

namespace Signet.App.Infrastructure;

/// <summary>Names of enum values in the UI language (selection lists in dialogs).</summary>
public static class DisplayNameConverters
{
    /// <summary><see cref="Core.Misc.PreviewHighlightStyle"/> → "Block background" / "Border around block" (keys <c>PreviewHighlightStyle_*</c>).</summary>
    public static readonly IValueConverter PreviewHighlightStyle =
        new FuncValueConverter<Core.Misc.PreviewHighlightStyle, string>(
            value => Strings.TryGet("PreviewHighlightStyle_" + value) ?? value.ToString());

    /// <summary><see cref="Core.Misc.EditionPagePosition"/> → "Last" / "Second (after the cover)"… (keys <c>EditionPagePosition_*</c>).</summary>
    public static readonly IValueConverter EditionPagePosition =
        new FuncValueConverter<Core.Misc.EditionPagePosition, string>(
            value => Strings.TryGet("EditionPagePosition_" + value) ?? value.ToString());

    /// <summary><see cref="Core.Footnotes.FootnoteLetterStyle"/> → "a, b, c…" / "A, B, C…"… (keys <c>FootnoteLetterStyle_*</c>).</summary>
    public static readonly IValueConverter FootnoteLetterStyle =
        new FuncValueConverter<Core.Footnotes.FootnoteLetterStyle, string>(
            value => Strings.TryGet("FootnoteLetterStyle_" + value) ?? value.ToString());

    /// <summary><see cref="Core.Misc.FindActionButtonDisplay"/> → "Icon and text" / "Icon only" / "Text only" (keys <c>FindActionButtonDisplay_*</c>).</summary>
    public static readonly IValueConverter FindActionButtonDisplay =
        new FuncValueConverter<Core.Misc.FindActionButtonDisplay, string>(
            value => Strings.TryGet("FindActionButtonDisplay_" + value) ?? value.ToString());
}
