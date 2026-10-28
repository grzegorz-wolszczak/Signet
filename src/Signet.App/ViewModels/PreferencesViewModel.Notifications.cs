using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// Appearance › Notifications: the fonts of the timestamp and the message columns (independent of each other), the
/// space between the rows, and the sample panel of the preview. The colors of the two columns (and of an ordinary
/// row vs. a warning row) are set on the Appearance › Interface Colors page.
/// </summary>
public sealed partial class PreferencesViewModel
{
    /// <summary>The font of the timestamp column of the Notifications panel; empty = the interface font.</summary>
    [ObservableProperty]
    private string _notificationsTimestampFontFamily = string.Empty;

    /// <summary>The size (px) of the timestamp column of the Notifications panel; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _notificationsTimestampFontSize;

    /// <summary>The weight of the timestamp column of the Notifications panel; 0 = not set.</summary>
    [ObservableProperty]
    private int _notificationsTimestampFontWeight;

    /// <summary>The font of the message column of the Notifications panel; empty = the interface font.</summary>
    [ObservableProperty]
    private string _notificationsMessageFontFamily = string.Empty;

    /// <summary>The size (px) of the message column of the Notifications panel; 0 = the interface size.</summary>
    [ObservableProperty]
    private int _notificationsMessageFontSize;

    /// <summary>The weight of the message column of the Notifications panel; 0 = not set.</summary>
    [ObservableProperty]
    private int _notificationsMessageFontWeight;

    /// <summary>Upper bound of <see cref="NotificationsEntrySpacing"/>.</summary>
    public static decimal NotificationsEntrySpacingMax => NotificationsAppearance.EntrySpacingMax;

    /// <summary>The vertical space (px) between two rows of the Notifications panel.</summary>
    [ObservableProperty]
    private int _notificationsEntrySpacing;

    partial void OnNotificationsTimestampFontFamilyChanged(string value) => PersistNotificationsAppearance();

    partial void OnNotificationsTimestampFontSizeChanged(int value) => PersistNotificationsAppearance();

    partial void OnNotificationsTimestampFontWeightChanged(int value) => PersistNotificationsAppearance();

    partial void OnNotificationsMessageFontFamilyChanged(string value) => PersistNotificationsAppearance();

    partial void OnNotificationsMessageFontSizeChanged(int value) => PersistNotificationsAppearance();

    partial void OnNotificationsMessageFontWeightChanged(int value) => PersistNotificationsAppearance();

    partial void OnNotificationsEntrySpacingChanged(int value) => PersistNotificationsAppearance();

    // Reads the look from the settings (the constructor; "Restore Appearance Defaults" after it cleared them).
    private void LoadNotificationsAppearance()
    {
        NotificationsAppearance look = _settings.NotificationsAppearance;
        NotificationsTimestampFontFamily = look.TimestampFontFamily;
        NotificationsTimestampFontSize = look.TimestampFontSize;
        NotificationsTimestampFontWeight = look.TimestampFontWeight;
        NotificationsMessageFontFamily = look.MessageFontFamily;
        NotificationsMessageFontSize = look.MessageFontSize;
        NotificationsMessageFontWeight = look.MessageFontWeight;
        NotificationsEntrySpacing = look.EntrySpacing;
    }

    private void PersistNotificationsAppearance() =>
        _settings.NotificationsAppearance = new NotificationsAppearance(
            NotificationsTimestampFontFamily, NotificationsTimestampFontSize, NotificationsTimestampFontWeight,
            NotificationsMessageFontFamily, NotificationsMessageFontSize, NotificationsMessageFontWeight,
            NotificationsEntrySpacing);

    /// <summary>"Choose…" — the font picker, starting from the font of the timestamp column.</summary>
    [RelayCommand]
    private async Task ChooseNotificationsTimestampFont()
    {
        string family = NotificationsTimestampFontFamily.Length > 0 ? NotificationsTimestampFontFamily : string.Empty;
        int size = NotificationsTimestampFontSize > 0 ? NotificationsTimestampFontSize : 12;
        await PickFontAsync(
            new FontPickRequest(family, size, false, NotificationsTimestampFontWeight),
            new FontPickResult(NotificationsTimestampFontFamily, NotificationsTimestampFontSize, NotificationsTimestampFontWeight),
            result =>
            {
                NotificationsTimestampFontFamily = result.Family;
                NotificationsTimestampFontSize = result.Size;
                NotificationsTimestampFontWeight = result.Weight;
            });
    }

    /// <summary>"Choose…" — the font picker, starting from the font of the message column.</summary>
    [RelayCommand]
    private async Task ChooseNotificationsMessageFont()
    {
        string family = NotificationsMessageFontFamily.Length > 0 ? NotificationsMessageFontFamily : string.Empty;
        int size = NotificationsMessageFontSize > 0 ? NotificationsMessageFontSize : 12;
        await PickFontAsync(
            new FontPickRequest(family, size, false, NotificationsMessageFontWeight),
            new FontPickResult(NotificationsMessageFontFamily, NotificationsMessageFontSize, NotificationsMessageFontWeight),
            result =>
            {
                NotificationsMessageFontFamily = result.Family;
                NotificationsMessageFontSize = result.Size;
                NotificationsMessageFontWeight = result.Weight;
            });
    }

    // The rows of the Appearance "Fonts" page.
    private FontSettingViewModel NotificationsTimestampFontRow() =>
        InterfaceFontRow(
            "NotificationsTimestamp", "PreferencesWindow_NotificationsTimestampFont", "FontPreview_NotificationsTimestamp",
            () => NotificationsTimestampFontFamily, v => NotificationsTimestampFontFamily = v,
            () => NotificationsTimestampFontSize, v => NotificationsTimestampFontSize = v,
            () =>
            {
                NotificationsTimestampFontFamily = string.Empty;
                NotificationsTimestampFontSize = 0;
                NotificationsTimestampFontWeight = 0;
            },
            ChooseNotificationsTimestampFontCommand,
            () => NotificationsTimestampFontWeight, v => NotificationsTimestampFontWeight = v,
            inheritance: ToolHeaderInheritance(
                () => _settings.NotificationsTimestampFontInheritsToolHeader, v => _settings.NotificationsTimestampFontInheritsToolHeader = v));

    private FontSettingViewModel NotificationsMessageFontRow() =>
        InterfaceFontRow(
            "NotificationsMessage", "PreferencesWindow_NotificationsMessageFont", "FontPreview_NotificationsMessage",
            () => NotificationsMessageFontFamily, v => NotificationsMessageFontFamily = v,
            () => NotificationsMessageFontSize, v => NotificationsMessageFontSize = v,
            () =>
            {
                NotificationsMessageFontFamily = string.Empty;
                NotificationsMessageFontSize = 0;
                NotificationsMessageFontWeight = 0;
            },
            ChooseNotificationsMessageFontCommand,
            () => NotificationsMessageFontWeight, v => NotificationsMessageFontWeight = v,
            inheritance: ToolHeaderInheritance(
                () => _settings.NotificationsMessageFontInheritsToolHeader, v => _settings.NotificationsMessageFontInheritsToolHeader = v));

    private NotificationsViewModel? _notificationsSample;

    /// <summary>
    /// The Notifications panel of the preview of its Appearance page: the real view model, over fabricated entries, reading the
    /// look from the settings of this window (the draft), so every change shows in it at once. Made when first asked for.
    /// </summary>
    public NotificationsViewModel NotificationsSample => _notificationsSample ??= CreateNotificationsSample();

    private NotificationsViewModel CreateNotificationsSample()
    {
        NotificationsViewModel sample = new(SampleStatusBar, _settings) { IsSample = true };
        DateTime now = DateTime.Now;
        sample.Add(new StatusNotification(now.AddMinutes(-6), Strings.Get("NotificationsSample_Saved"), NotificationLevel.Info));
        sample.Add(new StatusNotification(now.AddMinutes(-3), Strings.Get("NotificationsSample_BrokenLink"), NotificationLevel.Warning));
        sample.Add(new StatusNotification(now.AddMinutes(-1), Strings.Get("NotificationsSample_Validated"), NotificationLevel.Info));
        return sample;
    }
}
