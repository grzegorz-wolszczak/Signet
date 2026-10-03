using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Notifications" panel view — the list of the status bar messages, newest first. While the panel
/// is actually visible on screen (expanded, not just a collapsed tab), the warnings count as read.
/// </summary>
public partial class NotificationsView : UserControl
{
    private NotificationsViewModel? _boundViewModel;
    private bool _isShown;

    /// <summary>Initializes the view.</summary>
    public NotificationsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        DetachedFromVisualTree += (_, _) => _isShown = false;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.WarningArrived -= OnWarningArrived;
        }

        _boundViewModel = DataContext as NotificationsViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.WarningArrived += OnWarningArrived;
        }
    }

    // The panel became visible (e.g. a collapsed tab was slid out) — the user can see the list.
    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _isShown = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
        if (_isShown)
        {
            _boundViewModel?.MarkAsRead();
        }
    }

    // A warning that arrives while the list is on screen is seen right away. Deferred, so the
    // handlers of WarningArrived in the main window (showing the panel) run with the count raised.
    private void OnWarningArrived(object? sender, EventArgs e)
    {
        if (_isShown)
        {
            Dispatcher.UIThread.Post(() => _boundViewModel?.MarkAsRead());
        }
    }
}
