using System;
using System.Threading;
using System.Threading.Tasks;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// Long operations on the whole book: they run off the UI thread under a progress window with the name of the operation, the
/// percent and, when the book can be brought back (a checkpoint was taken before), Cancel — which puts the book back to the
/// state before the operation.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Runs a long operation with a progress window over the main window; set by the view. <c>null</c> (the tests): the work
    /// just runs, off the UI thread.
    /// </summary>
    public ProgressRunner? ProgressRunner { get; set; }

    // The book raises its events (a file added or removed, the book modified) on the thread that edits it. While that is not the
    // UI thread the handlers that touch the UI wait; the UI is brought up to date once the work is done.
    private bool _bookEditedInBackground;

    // Set for the whole span of a background book operation: taking its checkpoint, running the work, and — when it is
    // cancelled — putting the book back from that checkpoint. All three steps can run off the UI thread, so a second
    // operation starting while the first one's checkpoint restore is still swapping/disposing the book's resources would
    // race it (found live: renumbering a huge book, cancelling, and immediately renumbering again threw
    // ObjectDisposedException reading a file of the book that the still-running restore had already disposed).
    private bool _bookOperationBusy;

    // Marks a background book operation as started; false (with a status message) when one is already running.
    private bool TryEnterBookOperation()
    {
        if (_bookOperationBusy)
        {
            _statusBar.ShowMessage(Strings.Get("Status_OperationBusy"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return false;
        }

        _bookOperationBusy = true;
        return true;
    }

    private void ExitBookOperation() => _bookOperationBusy = false;

    /// <summary>The outcome of <see cref="RunBookOperationAsync{T}"/>.</summary>
    /// <param name="Completed">Whether the work ran to the end (not cancelled).</param>
    /// <param name="Result">What the work returned; <c>default</c> when it was cancelled.</param>
    /// <param name="Checkpoint">Whether a checkpoint was taken before the operation (and, when cancelled, used to bring the book back).</param>
    private readonly record struct BookOperationOutcome<T>(bool Completed, T? Result, bool Checkpoint);

    // Runs `work` off the UI thread (under `runner`, when there is one: the progress window titled `title`) and returns its result.
    private async Task<T> RunInBackgroundAsync<T>(
        string title, ProgressRunner? runner, bool editsBook, bool cancellable, Func<IProgress<WorkProgress>, CancellationToken, T> work)
    {
        T result = default!;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        DebugLog.Write("Operation", $"{title}: starting in background" + (runner is null ? " (no progress window)" : string.Empty));

        // The work decides when it stops (it checks the token), so the task is not given the token: it always starts.
        Task Run(IProgress<WorkProgress> progress, CancellationToken cancel) => Task.Run(() => result = work(progress, cancel));
        if (editsBook)
        {
            _bookEditedInBackground = true;
            BookBrowser.SuspendRebuilds = true;
        }

        try
        {
            await (runner is null ? Run(new Progress<WorkProgress>(), CancellationToken.None) : runner(title, Run, cancellable)).ConfigureAwait(true);
            DebugLog.Write("Operation", $"{title}: finished ({DebugLog.Elapsed(start)})");
        }
        catch (OperationCanceledException)
        {
            DebugLog.Write("Operation", $"{title}: cancelled ({DebugLog.Elapsed(start)})");
            throw;
        }
        finally
        {
            if (editsBook)
            {
                _bookEditedInBackground = false;
                BookBrowser.SuspendRebuilds = false;
                RefreshTitle();
                _toc.Refresh();
                UpdateNavigationPlace();
            }
        }

        return result;
    }

    /// <summary>
    /// Runs an operation on the whole book as a long one: the open tabs are saved by the caller; this takes the checkpoint
    /// <paramref name="checkpointName"/> (when given), runs <paramref name="work"/> off the UI thread under the progress window
    /// <paramref name="title"/>, and when the user cancels puts the book back from the checkpoint. A read-only operation
    /// (<paramref name="editsBook"/> <c>false</c>) can always be cancelled; one that edits the book only when there is a checkpoint
    /// to go back to.
    /// </summary>
    private async Task<BookOperationOutcome<T>> RunBookOperationAsync<T>(
        string title, string? checkpointName, bool editsBook, Func<IProgress<WorkProgress>, CancellationToken, T> work,
        ProgressRunner? runner = null)
    {
        if (!TryEnterBookOperation())
        {
            return new BookOperationOutcome<T>(false, default, false);
        }

        try
        {
            bool checkpoint = checkpointName is not null && await AddCheckpointBeforeAsync(checkpointName).ConfigureAwait(true);
            try
            {
                T result = await RunInBackgroundAsync(title, runner ?? ProgressRunner, editsBook, cancellable: !editsBook || checkpoint, work)
                    .ConfigureAwait(true);
                return new BookOperationOutcome<T>(true, result, checkpoint);
            }
            catch (OperationCanceledException)
            {
                if (checkpoint)
                {
                    await AbandonCheckpointAsync().ConfigureAwait(true);
                }

                _statusBar.ShowMessage(
                    Strings.Format(checkpoint ? "Status_OperationCancelledRestored" : "Status_OperationCancelled", title),
                    TimeSpan.FromSeconds(6));
                return new BookOperationOutcome<T>(false, default, checkpoint);
            }
        }
        finally
        {
            ExitBookOperation();
        }
    }

    // What the validation of the book reports: the percent through the files, then one step for each further check.
    private sealed class ValidationProgressMap : IProgress<ValidationProgress>
    {
        // The files take the first part of the bar; the ten further checks share the rest.
        private const double FilesShare = 0.5;

        private readonly IProgress<WorkProgress> _target;

        public ValidationProgressMap(IProgress<WorkProgress> target) => _target = target;

        public void Report(ValidationProgress value) => _target.Report(value.Stage == ValidationStage.WellFormed
            ? WorkProgress.InStage(Strings.Format("ValidationProgress_Files", value.Done + 1, value.Total), 0, FilesShare, value.Done, value.Total)
            : new WorkProgress(
                Strings.Format("ValidationProgress_Stage", Strings.Get("ValidationProgress_Stage_" + value.Stage)),
                FilesShare + ((1 - FilesShare) * ((int)value.Stage - 1) / (Enum.GetValues<ValidationStage>().Length - 1))));
    }

    // What an operation applied file by file reports as what the progress window shows: the file it is on and the percent.
    private sealed class FileBatchProgressMap : IProgress<FileBatchProgress>
    {
        private readonly IProgress<WorkProgress> _target;

        public FileBatchProgressMap(IProgress<WorkProgress> target) => _target = target;

        public void Report(FileBatchProgress value) => _target.Report(new WorkProgress(
            Strings.Format("Progress_FileBatch", value.Done + 1, value.Total, value.BookPath),
            WorkProgress.FractionOf(value.Done, value.Total)));
    }
}
