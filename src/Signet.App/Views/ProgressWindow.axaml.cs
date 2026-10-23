using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Signet.App.Infrastructure;
using Signet.App.Resources;

namespace Signet.App.Views;

/// <summary>
/// A modal window shown while work runs (e.g. loading a big book, the analysis of its footnotes): its message, an
/// indeterminate progress bar and, when the work can be left, Cancel. It appears only when the work takes longer than a
/// moment.
/// </summary>
public partial class ProgressWindow : Window
{
    /// <summary>How long the work may take before the window appears.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Initializes the window.</summary>
    public ProgressWindow()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close();
    }

    /// <summary>
    /// A <see cref="ProgressRunner"/> for a quick edit that only takes a while on a very large book: the window over
    /// <paramref name="owner"/> appears only once the work has run longer than <see cref="DefaultDelay"/> (instead of
    /// flashing on every edit), and never offers Cancel.
    /// </summary>
    public static ProgressRunner DelayedRunner(Window owner) =>
        (title, work, _) => RunWithProgressAsync(owner, title, Strings.Get("Progress_Starting"), work, cancellable: false, DefaultDelay);

    /// <summary>
    /// Runs <paramref name="work"/>, which reports how far it is (<see cref="WorkProgress"/>) from any thread; when it is
    /// not done after <paramref name="delay"/> (<see cref="DefaultDelay"/> when <c>null</c>), shows the window over
    /// <paramref name="owner"/>, with <paramref name="message"/> until the first report, until it is. Returns the result
    /// of the work, or <c>null</c> when the user cancelled (the work is not stopped, its result is dropped). An exception
    /// of the work is thrown here unless the user cancelled.
    /// </summary>
    public static async Task<T?> RunAsync<T>(
        Window owner, string title, string message, Func<IProgress<WorkProgress>, Task<T?>> work, TimeSpan? delay = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(work);
        UiProgress progress = new(message);
        Task<T?> task = work(progress);
        await Task.WhenAny(task, Task.Delay(delay ?? DefaultDelay)).ConfigureAwait(true);
        if (task.IsCompleted)
        {
            return await task.ConfigureAwait(true);
        }

        ProgressWindow window = new() { Title = title };
        progress.Attach(window);
        bool done = false;
        _ = task.ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                done = true;
                window.Close();
            }),
            TaskScheduler.Default);
        await window.ShowDialog(owner).ConfigureAwait(true);
        if (!done)
        {
            // Cancelled: whatever the work ends with is not wanted any more (an exception included).
            _ = task.ContinueWith(t => t.Exception, TaskScheduler.Default);
            return null;
        }

        return await task.ConfigureAwait(true);
    }

    /// <summary>
    /// Runs <paramref name="work"/> with a window over <paramref name="owner"/> that shows what it is doing and how far it is
    /// (<see cref="WorkProgress"/>): the work reports to the progress it is given, from any thread. The window appears at once
    /// (a long operation says so even when it is short; <paramref name="delay"/> keeps it away from the work that finishes
    /// within it) and cannot be closed until the work is done and the UI has caught up with it. An exception of the work is
    /// thrown here.
    /// </summary>
    /// <remarks>
    /// The work must leave the UI thread before it does anything long (<c>Task.Run</c>), or the window cannot be drawn. With
    /// <c>cancellable</c> the window has Cancel: it cancels the token the work is given, the window then says it is cancelling
    /// and stays until the work has stopped (an <see cref="OperationCanceledException"/> is thrown here); undoing what the work
    /// did is the caller's.
    /// </remarks>
    /// <param name="owner">The window the progress window is shown over.</param>
    /// <param name="title">The name of the operation (the title of the window).</param>
    /// <param name="message">What it is doing at the start.</param>
    /// <param name="work">The work.</param>
    /// <param name="cancellable">Whether the window has Cancel.</param>
    /// <param name="delay">How long the work may take before the window appears (none by default).</param>
    public static async Task RunWithProgressAsync(
        Window owner, string title, string message, Func<IProgress<WorkProgress>, CancellationToken, Task> work,
        bool cancellable = false, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(work);
        using CancellationTokenSource cancel = new();
        UiProgress progress = new(message);
        Task task = work(progress, cancel.Token);
        await Task.WhenAny(task, Task.Delay(delay ?? TimeSpan.Zero)).ConfigureAwait(true);
        if (!task.IsCompleted)
        {
            ProgressWindow window = new() { Title = title };
            window.CancelButton.IsVisible = cancellable;
            window.CancelButton.IsCancel = cancellable;
            if (cancellable)
            {
                window.CancelButton.Click += (_, _) =>
                {
                    cancel.Cancel();
                    window.CancelButton.IsEnabled = false;
                    progress.Stop();
                    window.MessageText.Text = Strings.Get("ProgressWindow_Cancelling");
                    window.Bar.IsIndeterminate = true;
                    window.PercentText.IsVisible = false;
                };
            }

            progress.Attach(window);
            bool done = false;
            window.Closing += (_, e) => e.Cancel = !done;
            _ = task.ContinueWith(
                _ => Dispatcher.UIThread.Post(
                    () =>
                    {
                        done = true;
                        window.Close();
                    },
                    DispatcherPriority.Background),
                TaskScheduler.Default);
            await window.ShowDialog(owner).ConfigureAwait(true);
        }

        await task.ConfigureAwait(true);
    }

    // Takes reports from any thread and shows the last one in the window, on the UI thread, when there is one.
    private sealed class UiProgress : IProgress<WorkProgress>
    {
        private readonly object _gate = new();
        private WorkProgress _latest;
        private ProgressWindow? _window;

        public UiProgress(string message) => _latest = new WorkProgress(message, null);

        // The reports that come after Cancel was pressed are not shown: the window says it is cancelling.
        public void Stop()
        {
            lock (_gate)
            {
                _window = null;
            }
        }

        public void Attach(ProgressWindow window)
        {
            lock (_gate)
            {
                _window = window;
                window.Apply(_latest);
            }
        }

        public void Report(WorkProgress value)
        {
            lock (_gate)
            {
                _latest = value with { Message = value.Message ?? _latest.Message };
                if (_window is null)
                {
                    return;
                }
            }

            Dispatcher.UIThread.Post(
                () =>
                {
                    WorkProgress latest;
                    lock (_gate)
                    {
                        latest = _latest;
                    }

                    _window?.Apply(latest);
                },
                DispatcherPriority.Render);
        }
    }

    /// <summary>Shows <paramref name="progress"/>: the message, and the bar with the percent when the fraction is known.</summary>
    internal void Apply(WorkProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.Message is not null)
        {
            MessageText.Text = progress.Message;
        }

        if (progress.Fraction is { } fraction)
        {
            int percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
            Bar.IsIndeterminate = false;
            Bar.Value = percent;
            PercentText.Text = percent.ToString(System.Globalization.CultureInfo.CurrentCulture) + " %";
            PercentText.IsVisible = true;
        }
        else
        {
            Bar.IsIndeterminate = true;
            PercentText.IsVisible = false;
        }
    }

    /// <summary>
    /// Waits for <paramref name="work"/>; when it is not done after <paramref name="delay"/> (<see cref="DefaultDelay"/>
    /// when <c>null</c>), shows the window over <paramref name="owner"/> — without Cancel, and it cannot be closed —
    /// until the work is done and the UI has caught up with it (layout and rendering go before the window closes, so
    /// the result is on screen when it does). An exception of the work is thrown here.
    /// </summary>
    public static async Task ShowWhileAsync(Window owner, string title, string message, Task work, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(work);
        await Task.WhenAny(work, Task.Delay(delay ?? DefaultDelay)).ConfigureAwait(true);
        if (!work.IsCompleted)
        {
            ProgressWindow window = new() { Title = title };
            window.MessageText.Text = message;
            window.CancelButton.IsVisible = false;
            window.CancelButton.IsCancel = false;
            bool done = false;
            window.Closing += (_, e) => e.Cancel = !done;
            _ = work.ContinueWith(
                _ => Dispatcher.UIThread.Post(
                    () =>
                    {
                        done = true;
                        window.Close();
                    },
                    DispatcherPriority.Background),
                TaskScheduler.Default);
            await window.ShowDialog(owner).ConfigureAwait(true);
        }

        await work.ConfigureAwait(true);
    }
}
