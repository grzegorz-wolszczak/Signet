using System;
using System.ComponentModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Signet.App.Services;

/// <summary>
/// Status bar message service.
/// </summary>
public interface IStatusBarService : INotifyPropertyChanged
{
    /// <summary>Current message (empty = none).</summary>
    string CurrentMessage { get; }

    /// <summary>Shows a message for <paramref name="timeout"/> (zero/negative = until cleared).</summary>
    void ShowMessage(string message, TimeSpan timeout = default);

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
    public void ShowMessage(string message, TimeSpan timeout = default)
    {
        long generation = Interlocked.Increment(ref _generation);
        CurrentMessage = message ?? string.Empty;

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
