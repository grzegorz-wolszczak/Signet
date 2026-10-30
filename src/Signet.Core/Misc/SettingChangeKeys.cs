using System;

namespace Signet.Core.Misc;

/// <summary>
/// The keys <see cref="SettingsStore.SettingChanged"/> raises for a setting written as a whole (several entries of the file
/// at once), so that the store and those who listen to it name them alike.
/// </summary>
public static class SettingChangeKeys
{
    /// <summary>The look of the main window's toolbars (<see cref="SettingsStore.ToolbarAppearance"/>).</summary>
    public const string ToolbarAppearance = "toolbar_appearance";

    /// <summary>The fonts of the context menus (<see cref="SettingsStore.ContextMenuAppearance"/>).</summary>
    public const string ContextMenuAppearance = "context_menu_appearance";

    /// <summary>The look of the stripes of the panels (<see cref="SettingsStore.PanelStripeAppearance"/>).</summary>
    public const string PanelStripeAppearance = "panel_stripe_appearance";

    /// <summary>The appearance settings were reset to their defaults (<see cref="SettingsStore.ClearAppearanceSettings"/>).</summary>
    public const string AppearanceSettingsCleared = "appearance_settings_cleared";

    /// <summary>Whether a raised qualified key (<c>group/key</c>) is <paramref name="key"/>.</summary>
    public static bool Is(SettingChangedEventArgs e, string key)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.QualifiedKey.EndsWith("/" + key, StringComparison.Ordinal);
    }
}
