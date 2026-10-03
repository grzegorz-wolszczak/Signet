using System;
using System.ComponentModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Signet.App.Services;

/// <summary>Importance of a status bar message.</summary>
public enum NotificationLevel
{
    /// <summary>An ordinary message (e.g. the result of an operation).</summary>
    Info,

    /// <summary>
    /// An operation was blocked or failed (e.g. cancelled because a file is not well-formed) — the
    /// user must not miss it, so the Notifications panel is shown and the unread indicator is raised.
    /// </summary>
    Warning,
}

/// <summary>A message shown in the status bar, as recorded in the Notifications panel.</summary>
/// <param name="Time">When the message was shown (local time).</param>
/// <param name="Message">The message text.</param>
/// <param name="Level">The message importance.</param>
public sealed record StatusNotification(DateTime Time, string Message, NotificationLevel Level);

/// <summary>
/// Status bar message service.
/// </summary>
public interface IStatusBarService : INotifyPropertyChanged
{
    /// <summary>Current message (empty = none).</summary>
    string CurrentMessage { get; }

    /// <summary>Importance of <see cref="CurrentMessage"/>.</summary>
    NotificationLevel CurrentLevel { get; }

    /// <summary>Raised for every non-empty message shown (the Notifications panel history).</summary>
    event EventHandler<StatusNotification>? MessageShown;

    /// <summary>
    /// Shows a message for <paramref name="timeout"/> (zero/negative = until cleared). Use
    /// <see cref="NotificationLevel.Warning"/> when an operation was blocked or failed.
    /// </summary>
    void ShowMessage(string message, TimeSpan timeout = default, NotificationLevel level = NotificationLevel.Info);

    /// <summary>Clears the current message.</summary>
    void Clear();
}

/// <summary>
/// Default implementation of <see cref="IStatusBarService"/>. Automatic clearing after a
/// timeout uses a plain <see cref="Timer"/>; the object is observable (the status bar is
/// bound to <see cref="CurrentMessage"/>).
/// </summary>
public sealed partial class StatusBarService : ObservableObject, IStatusBarService, IDisposable
{
    private readonly Timer _timer;
    private long _generation;

    /// <summary>Creates the service with an empty message.</summary>
    public StatusBarService()
    {
        _timer = new Timer(_ => OnTimeout(Interlocked.Read(ref _generation)));
    }

    /// <inheritdoc />
    [ObservableProperty]
    private string _currentMessage = string.Empty;

    /// <inheritdoc />
    [ObservableProperty]
    private NotificationLevel _currentLevel;

    /// <inheritdoc />
    public event EventHandler<StatusNotification>? MessageShown;

    /// <inheritdoc />
    public void ShowMessage(string message, TimeSpan timeout = default, NotificationLevel level = NotificationLevel.Info)
    {
        long generation = Interlocked.Increment(ref _generation);
        CurrentLevel = level;
        CurrentMessage = message ?? string.Empty;
        if (CurrentMessage.Length > 0)
        {
            MessageShown?.Invoke(this, new StatusNotification(DateTime.Now, CurrentMessage, level));
        }

        if (timeout > TimeSpan.Zero)
        {
            _timer.Change(timeout, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _ = generation;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        CurrentMessage = string.Empty;
        CurrentLevel = NotificationLevel.Info;
    }

    /// <inheritdoc />
    public void Dispose() => _timer.Dispose();

    private void OnTimeout(long generation)
    {
        void Apply()
        {
            if (Interlocked.Read(ref _generation) == generation)
            {
                CurrentMessage = string.Empty;
                CurrentLevel = NotificationLevel.Info;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }
}
