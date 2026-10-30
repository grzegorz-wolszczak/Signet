using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// The font of the names on the stripes of the panels (<see cref="PanelStripeAppearance"/>, Preferences › Appearance &amp;
/// Behavior › Action Bars) as resources, which the labels of <c>PanelStripeView</c> take: the application's, or those of
/// the preview of Preferences (over the draft of the settings).
/// <para>The resources always hold the font in use, also where the setting is empty: a style setter whose resource is
/// missing puts the property's default in place (not the interface font). So they are set again whenever the interface
/// font changes.</para>
/// </summary>
public static class PanelStripeLook
{
    /// <summary>Font family of the names.</summary>
    public const string FontFamilyKey = "SignetPanelStripeFontFamily";

    /// <summary>Font size of the names.</summary>
    public const string FontSizeKey = "SignetPanelStripeFontSize";

    /// <summary>Font weight of the names.</summary>
    public const string FontWeightKey = "SignetPanelStripeFontWeight";

    /// <summary>Sets the application resources from <paramref name="look"/> and the interface font now in use.</summary>
    public static void Apply(Application app, PanelStripeAppearance look)
    {
        ArgumentNullException.ThrowIfNull(app);
        Apply(app.Resources, app, look);
    }

    /// <summary>
    /// Sets the resources of <paramref name="target"/> from <paramref name="look"/>; what the setting leaves empty is the
    /// interface font found from <paramref name="source"/>.
    /// </summary>
    public static void Apply(IResourceDictionary target, IResourceHost source, PanelStripeAppearance look)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        FontFamily uiFamily = source.TryFindResource(FluentResourceKeys.ContentControlThemeFontFamily, out object? f) && f is FontFamily family
            ? family
            : FontFamily.Default;
        FontWeight uiWeight = source.TryFindResource(UiDensityManager.UiFontWeightKey, out object? w) && w is FontWeight weight
            ? weight
            : FontWeight.Normal;

        target[FontFamilyKey] = look.FontFamily.Length > 0 ? FontFamily.Parse(look.FontFamily) : uiFamily;
        target[FontSizeKey] = (double)(look.FontSize > 0 ? look.FontSize : PanelStripeAppearance.DefaultFontSize);
        target[FontWeightKey] = look.FontWeight > 0 ? FontWeightOptions.ToAvalonia(look.FontWeight) : uiWeight;
    }
}
