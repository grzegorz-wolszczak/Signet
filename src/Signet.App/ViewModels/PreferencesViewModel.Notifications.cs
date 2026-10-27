using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Notifications: the font of the rows, the space between them and the sample panel of the preview.</summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>The font of the rows of the Notifications panel; empty = the interface font.</summary>
    [ObservableProperty]
    private string _notificationsFontFamily = string.Empty;

    /// <summary>The size (px) of the rows of the Notifications panel; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _notificationsFontSize;

    /// <summary>The weight of the rows of the Notifications panel; 0 = not set.</summary>
    [ObservableProperty]
    private int _notificationsFontWeight;

    /// <summary>Upper bound of <see cref="NotificationsEntrySpacing"/>.</summary>
    public static decimal NotificationsEntrySpacingMax => NotificationsAppearance.EntrySpacingMax;

    /// <summary>The vertical space (px) between two rows of the Notifications panel.</summary>
    [ObservableProperty]
    private int _notificationsEntrySpacing;

    partial void OnNotificationsFontFamilyChanged(string value) => PersistNotificationsAppearance();

    partial void OnNotificationsFontSizeChanged(int value) => PersistNotificationsAppearance();

    partial void OnNotificationsFontWeightChanged(int value) => PersistNotificationsAppearance();

    partial void OnNotificationsEntrySpacingChanged(int value) => PersistNotificationsAppearance();

    // Reads the look from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadNotificationsAppearance()
    {
        NotificationsAppearance look = _settings.NotificationsAppearance;
        NotificationsFontFamily = look.FontFamily;
        NotificationsFontSize = look.FontSize;
        NotificationsFontWeight = look.FontWeight;
        NotificationsEntrySpacing = look.EntrySpacing;
    }

    private void PersistNotificationsAppearance() =>
        _settings.NotificationsAppearance = new NotificationsAppearance(
            NotificationsFontFamily, NotificationsFontSize, NotificationsFontWeight, NotificationsEntrySpacing);

    /// <summary>"Choose…" — the font picker, starting from the font of the Notifications rows.</summary>
    [RelayCommand]
    private async Task ChooseNotificationsFont()
    {
        string family = NotificationsFontFamily.Length > 0 ? NotificationsFontFamily : string.Empty;
        int size = NotificationsFontSize > 0 ? NotificationsFontSize : 12;
        await PickFontAsync(
            new FontPickRequest(family, size, false, NotificationsFontWeight),
            new FontPickResult(NotificationsFontFamily, NotificationsFontSize, NotificationsFontWeight),
            result =>
            {
                NotificationsFontFamily = result.Family;
                NotificationsFontSize = result.Size;
                NotificationsFontWeight = result.Weight;
            });
    }

    // The row of the Appearance "Fonts" page.
    private FontSettingViewModel NotificationsFontRow() =>
        InterfaceFontRow(
            "NotificationsEntries", "PreferencesWindow_NotificationsEntriesFont", "FontPreview_NotificationsEntries",
            () => NotificationsFontFamily, v => NotificationsFontFamily = v,
            () => NotificationsFontSize, v => NotificationsFontSize = v,
            () =>
            {
                NotificationsFontFamily = string.Empty;
                NotificationsFontSize = 0;
                NotificationsFontWeight = 0;
            },
            ChooseNotificationsFontCommand,
            () => NotificationsFontWeight, v => NotificationsFontWeight = v,
            inheritance: ToolHeaderInheritance(() => _settings.NotificationsFontInheritsToolHeader, v => _settings.NotificationsFontInheritsToolHeader = v));

    private NotificationsViewModel? _notificationsSample;

    /// <summary>
    /// The Notifications panel of the preview of its Appearance page: the real view model, over fabricated entries, reading the
    /// look from the settings of this window (the draft), so every change shows in it at once. Made when first asked for.
    /// </summary>
    public NotificationsViewModel NotificationsSample => _notificationsSample ??= CreateNotificationsSample();

    private NotificationsViewModel CreateNotificationsSample()
    {
        NotificationsViewModel sample = new(SampleStatusBar, _settings);
        DateTime now = DateTime.Now;
        sample.Add(new StatusNotification(now.AddMinutes(-6), Strings.Get("NotificationsSample_Saved"), NotificationLevel.Info));
        sample.Add(new StatusNotification(now.AddMinutes(-3), Strings.Get("NotificationsSample_BrokenLink"), NotificationLevel.Warning));
        sample.Add(new StatusNotification(now.AddMinutes(-1), Strings.Get("NotificationsSample_Validated"), NotificationLevel.Info));
        return sample;
    }
}
