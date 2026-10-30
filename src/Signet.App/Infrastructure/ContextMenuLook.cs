using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// The fonts of the context menus (<see cref="ContextMenuAppearance"/>, Preferences › Appearance › Context Menus) as
/// application resources, which the <c>ContextMenu MenuItem</c> styles of <c>App.axaml</c> take: the items and the title
/// (<see cref="MenuTitle"/>). The main menu and the drop-down menus of buttons are not context menus and keep the
/// interface font.
/// <para>The resources always hold the font in use, also where the setting is empty: a style setter whose resource is
/// missing puts the property's default in place (not the interface font). So they are set again whenever the interface
/// font changes.</para>
/// </summary>
public static class ContextMenuLook
{
    /// <summary>Font family of the items.</summary>
    public const string ItemsFontFamilyKey = "SignetContextMenuItemsFontFamily";

    /// <summary>Font size of the items.</summary>
    public const string ItemsFontSizeKey = "SignetContextMenuItemsFontSize";

    /// <summary>Font weight of the items.</summary>
    public const string ItemsFontWeightKey = "SignetContextMenuItemsFontWeight";

    /// <summary>Font family of the title.</summary>
    public const string TitleFontFamilyKey = "SignetContextMenuTitleFontFamily";

    /// <summary>Font size of the title.</summary>
    public const string TitleFontSizeKey = "SignetContextMenuTitleFontSize";

    /// <summary>Font weight of the title.</summary>
    public const string TitleFontWeightKey = "SignetContextMenuTitleFontWeight";

    /// <summary>Sets the resources from <paramref name="look"/> and the interface font now in use.</summary>
    public static void Apply(Application app, ContextMenuAppearance look)
    {
        ArgumentNullException.ThrowIfNull(app);
        FontFamily uiFamily = app.TryFindResource("ContentControlThemeFontFamily", out object? f) && f is FontFamily family
            ? family
            : FontFamily.Default;
        double uiSize = app.TryFindResource("ControlContentThemeFontSize", out object? s) && s is double size ? size : 14;
        FontWeight uiWeight = app.TryFindResource(UiDensityManager.UiFontWeightKey, out object? w) && w is FontWeight weight
            ? weight
            : FontWeight.Normal;

        FontFamily itemsFamily = look.ItemsFontFamily.Length > 0 ? FontFamily.Parse(look.ItemsFontFamily) : uiFamily;
        double itemsSize = look.ItemsFontSize > 0 ? look.ItemsFontSize : uiSize;
        app.Resources[ItemsFontFamilyKey] = itemsFamily;
        app.Resources[ItemsFontSizeKey] = itemsSize;
        app.Resources[ItemsFontWeightKey] = look.ItemsFontWeight > 0 ? FontWeightOptions.ToAvalonia(look.ItemsFontWeight) : uiWeight;
        app.Resources[TitleFontFamilyKey] = look.TitleFontFamily.Length > 0 ? FontFamily.Parse(look.TitleFontFamily) : itemsFamily;
        app.Resources[TitleFontSizeKey] = look.TitleFontSize > 0 ? (double)look.TitleFontSize : itemsSize;
        app.Resources[TitleFontWeightKey] = look.TitleFontWeight > 0 ? FontWeightOptions.ToAvalonia(look.TitleFontWeight) : FontWeight.Bold;
    }
}
