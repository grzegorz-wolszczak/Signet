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

    /// <summary><see cref="Core.Misc.ToolbarTextPosition"/> → the same names as in a toolbar's context menu (keys <c>ToolbarStripView_*</c>).</summary>
    public static readonly IValueConverter ToolbarTextPosition =
        new FuncValueConverter<Core.Misc.ToolbarTextPosition, string>(
            value => Strings.TryGet("ToolbarStripView_" + value) ?? value.ToString());

    /// <summary><see cref="Core.Misc.ToolbarIconSize"/> → the same names as in a toolbar's context menu (keys <c>ToolbarStripView_*Icons</c>).</summary>
    public static readonly IValueConverter ToolbarIconSize =
        new FuncValueConverter<Core.Misc.ToolbarIconSize, string>(
            value => Strings.TryGet("ToolbarStripView_" + value + "Icons") ?? value.ToString());

    /// <summary><see cref="Core.Misc.PanelStripeWidthMode"/> → its name (keys <c>PanelStripeWidthMode_*</c>).</summary>
    public static readonly IValueConverter PanelStripeWidthMode =
        new FuncValueConverter<Core.Misc.PanelStripeWidthMode, string>(
            value => Strings.TryGet("PanelStripeWidthMode_" + value) ?? value.ToString());
}
