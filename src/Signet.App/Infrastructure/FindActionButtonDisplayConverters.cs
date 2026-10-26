using Avalonia.Data.Converters;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Whether the icon / the text of a Find &amp; Replace action button (Find, Replace, Restart, …) should be shown,
/// from <see cref="FindActionButtonDisplay"/> (<c>FindReplaceView</c>'s "findAction" button template).
/// </summary>
public static class FindActionButtonDisplayConverters
{
    /// <summary>The icon shows unless the display is text-only.</summary>
    public static readonly IValueConverter ShowIcon =
        new FuncValueConverter<FindActionButtonDisplay, bool>(value => value != FindActionButtonDisplay.TextOnly);

    /// <summary>The text shows unless the display is icon-only.</summary>
    public static readonly IValueConverter ShowText =
        new FuncValueConverter<FindActionButtonDisplay, bool>(value => value != FindActionButtonDisplay.IconOnly);
}
