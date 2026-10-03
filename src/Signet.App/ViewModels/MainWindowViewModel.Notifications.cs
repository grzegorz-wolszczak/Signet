using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Resources;
using Signet.App.Services;

namespace Signet.App.ViewModels;

/// <summary>
/// The "Notifications" panel: the history of the status bar messages, the unread-warnings indicator
/// in the status bar and showing the panel (collapsed) when an operation was blocked or failed.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The "Notifications" panel view model.</summary>
    public NotificationsViewModel Notifications => _notifications;

    /// <summary>Whether the current status bar message is a warning (drawn with the warning color).</summary>
    public bool IsStatusMessageWarning => _statusBar.CurrentLevel == NotificationLevel.Warning;

    /// <summary>Whether the status bar shows the unread-warnings indicator.</summary>
    public bool HasUnreadWarnings => _notifications.HasUnreadWarnings;

    /// <summary>The number shown next to the indicator icon.</summary>
    public string UnreadWarningsText => _notifications.UnreadWarnings.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The tooltip of the unread-warnings indicator.</summary>
    public string UnreadWarningsTooltip => Strings.Format("MainWindow_UnreadWarningsTooltip", _notifications.UnreadWarnings);

    // Called from the constructor BEFORE the dock layout is created.
    private void InitNotifications()
    {
        _dockFactory.Notifications = _notifications;
        _notifications.WarningArrived += (_, _) => _dockFactory.ShowToolCollapsed(DockableIds.Notifications);
        _notifications.PropertyChanged += OnNotificationsPropertyChanged;
    }

    // Called from the constructor AFTER the dock layout is created.
    private void WireNotificationsActions() =>
        _actions.SetHandler(AppActionIds.ToggleNotifications, () =>
        {
            if (_dockFactory.ToggleTool(DockableIds.Notifications))
            {
                _notifications.MarkAsRead();
            }
        });

    /// <summary>
    /// Shows the "Notifications" panel expanded and marks the warnings as read — a click on the status
    /// bar message or on the unread-warnings indicator.
    /// </summary>
    [RelayCommand]
    private void OpenNotifications()
    {
        _dockFactory.ExpandTool(DockableIds.Notifications);
        _notifications.MarkAsRead();
    }

    private void OnNotificationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NotificationsViewModel.UnreadWarnings))
        {
            return;
        }

        _dockFactory.SetToolAttention(DockableIds.Notifications, _notifications.UnreadWarnings);
        OnPropertyChanged(nameof(HasUnreadWarnings));
        OnPropertyChanged(nameof(UnreadWarningsText));
        OnPropertyChanged(nameof(UnreadWarningsTooltip));
    }
}
