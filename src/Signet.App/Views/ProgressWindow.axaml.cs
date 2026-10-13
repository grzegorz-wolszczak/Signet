using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Signet.App.Views;

/// <summary>
/// A modal window shown while work runs in the background (e.g. the analysis of the footnotes of a big book): its
/// message, an indeterminate progress bar and Cancel. It appears only when the work takes longer than a moment.
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
    /// Waits for <paramref name="work"/>; when it is not done after <paramref name="delay"/> (<see cref="DefaultDelay"/>
    /// when <c>null</c>), shows the window over <paramref name="owner"/> until it is. Returns the result of the work, or
    /// <c>null</c> when the user cancelled (the work is not stopped, its result is dropped). An exception of the work is
    /// thrown here unless the user cancelled.
    /// </summary>
    public static async Task<T?> RunAsync<T>(Window owner, string title, string message, Task<T?> work, TimeSpan? delay = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(work);
        await Task.WhenAny(work, Task.Delay(delay ?? DefaultDelay)).ConfigureAwait(true);
        if (work.IsCompleted)
        {
            return await work.ConfigureAwait(true);
        }

        ProgressWindow window = new() { Title = title };
        window.MessageText.Text = message;
        bool done = false;
        _ = work.ContinueWith(
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
            _ = work.ContinueWith(t => t.Exception, TaskScheduler.Default);
            return null;
        }

        return await work.ConfigureAwait(true);
    }
}
