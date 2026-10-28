using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Services;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Notifications" panel — the history of the messages shown in the status bar
/// (a status bar message disappears after a while, so a missed one could not be read again).
/// Only the current session is kept, newest first, limited to <see cref="MaxEntries"/>.
/// </summary>
/// <remarks>
/// A <see cref="NotificationLevel.Warning"/> message (an operation was blocked or failed) raises
/// <see cref="WarningArrived"/> and increases <see cref="UnreadWarnings"/> until
/// <see cref="MarkAsRead"/> is called (the user opened or clicked the panel).
/// </remarks>
public sealed partial class NotificationsViewModel : ViewModelBase
{
    /// <summary>The maximum number of entries kept in the list (the oldest ones are dropped).</summary>
    public const int MaxEntries = 500;

    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly SettingsStore? _settings;
    private int _unreadWarnings;

    /// <summary>Creates the panel and starts recording the messages of <paramref name="statusBar"/>.</summary>
    /// <param name="statusBar">The service whose shown messages are recorded.</param>
    /// <param name="settings">The settings the look of the list comes from (<see cref="Appearance"/>); the panel follows
    /// their changes. <c>null</c> = the default look (used by the sample panel of the Preferences preview, which reads
    /// the draft settings directly through the view's binding instead).</param>
    public NotificationsViewModel(IStatusBarService statusBar, SettingsStore? settings = null)
    {
        ArgumentNullException.ThrowIfNull(statusBar);
        statusBar.MessageShown += OnMessageShown;
        _settings = settings;
        if (_settings is not null)
        {
            _settings.SettingChanged += OnSettingChanged;
        }
    }

    /// <summary>Raised after a <see cref="NotificationLevel.Warning"/> message was recorded.</summary>
    public event EventHandler? WarningArrived;

    /// <summary>
    /// Whether this is the sample panel of the Preferences preview — its "Clear" button stays clickable,
    /// to look at, but does not actually clear the fabricated entries.
    /// </summary>
    public bool IsSample { get; init; }

    /// <summary>
    /// The look of the list now (the settings) — its own font, or, while it inherits, a tool panel's title bar font; the
    /// default look without settings.
    /// </summary>
    public NotificationsAppearance Appearance => _settings?.EffectiveNotificationsAppearance ?? NotificationsAppearance.Default;

    /// <summary>Raised when a setting of the look of the list changed (the view applies <see cref="Appearance"/> again).</summary>
    public event EventHandler? AppearanceChanged;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.QualifiedKey.Contains("notifications_", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("tool_header_font", StringComparison.Ordinal))
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The recorded messages, newest first.</summary>
    public ObservableCollection<NotificationEntry> Entries { get; } = new();

    /// <summary>Whether there are any entries (to switch between the list and the "No notifications" text).</summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>The number of warnings recorded since the panel was last read.</summary>
    public int UnreadWarnings
    {
        get => _unreadWarnings;
        private set
        {
            if (SetProperty(ref _unreadWarnings, value))
            {
                OnPropertyChanged(nameof(HasUnreadWarnings));
            }
        }
    }

    /// <summary>Whether there are unread warnings (drives the status bar indicator and the bell on the tab).</summary>
    public bool HasUnreadWarnings => _unreadWarnings > 0;

    /// <summary>Marks all warnings as read (the user opened or clicked the panel).</summary>
    public void MarkAsRead() => UnreadWarnings = 0;

    /// <summary>Clears the list (and the unread counter).</summary>
    [RelayCommand]
    private void Clear()
    {
        if (IsSample)
        {
            return;
        }

        Entries.Clear();
        UnreadWarnings = 0;
        OnPropertyChanged(nameof(HasEntries));
    }

    /// <summary>Records a message (public for tests; normally fed by <see cref="IStatusBarService.MessageShown"/>).</summary>
    public void Add(StatusNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        Entries.Insert(0, new NotificationEntry(notification));
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        OnPropertyChanged(nameof(HasEntries));
        if (notification.Level == NotificationLevel.Warning)
        {
            UnreadWarnings++;
            WarningArrived?.Invoke(this, EventArgs.Empty);
        }
    }

    // A message may be shown from a background continuation — the list is bound to the UI, so it
    // is updated on the thread (synchronization context) the panel was created on.
    private void OnMessageShown(object? sender, StatusNotification notification)
    {
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
        {
            Add(notification);
        }
        else
        {
            _context.Post(_ => Add(notification), null);
        }
    }
}

/// <summary>A single row of the "Notifications" panel.</summary>
public sealed class NotificationEntry
{
    /// <summary>Creates a row from a recorded status bar message.</summary>
    public NotificationEntry(StatusNotification notification)
    {
        Notification = notification;
    }

    /// <summary>The recorded message.</summary>
    public StatusNotification Notification { get; }

    /// <summary>The time the message was shown, as <c>HH:mm:ss</c>.</summary>
    public string TimeText => Notification.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The message text.</summary>
    public string Message => Notification.Message;

    /// <summary>Whether the message is a warning — a blocked or failed operation (drawn with the error icon color).</summary>
    public bool IsWarning => Notification.Level == NotificationLevel.Warning;
}
