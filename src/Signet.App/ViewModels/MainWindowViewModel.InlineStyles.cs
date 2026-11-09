using System;
using System.Threading.Tasks;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// Tools → CSS Tools → "Extract Inline Styles…": the values of the <c>style</c> attribute that many elements share
/// become classes of a stylesheet (<see cref="InlineStyleExtraction"/>), after the user has looked at them in a dialog.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Raised by "Extract Inline Styles…" — the view calls <see cref="PrepareInlineStylesAsync"/> and shows the modal dialog.
    /// </summary>
    public event EventHandler? InlineStylesRequested;

    /// <summary>
    /// The "Extract Inline Styles" dialog for the open book: the open tabs are saved, and the book is surveyed off the UI thread
    /// under a progress window, which can be cancelled. <c>null</c> (with a message in the status bar) without a book, when
    /// the survey was cancelled, or when there is nothing to extract (no stylesheet, no <c>style</c> attribute that repeats).
    /// </summary>
    /// <param name="runWithProgress">Shows the progress of "Extract" over the dialog (<c>null</c>: without a window).</param>
    public async Task<InlineStylesViewModel?> PrepareInlineStylesAsync(ProgressRunner? runWithProgress = null)
    {
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        BookOperationOutcome<InlineStyleSurvey> run = await RunBookOperationAsync(
            ActionLabel(AppActionIds.ExtractInlineStyles), checkpointName: null, editsBook: false,
            (progress, cancel) => InlineStyleExtraction.Survey(book, new FileBatchRun(new FileBatchProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        if (!run.Completed || !ReferenceEquals(book, _currentBook))
        {
            return null;
        }

        InlineStyleSurvey survey = run.Result!;
        string? missing = survey.StylesheetBookPath is null
            ? Strings.Get(survey.Groups.Count == 0 && survey.ElementsOutsideStylesheet == 0 ? "Status_NoInlineStyles" : "Status_NoStylesheetForInlineStyles")
            : survey.Groups.Count == 0 ? Strings.Get("Status_NoInlineStyles") : null;
        if (missing is not null)
        {
            _statusBar.ShowMessage(missing, TimeSpan.FromSeconds(5));
            return null;
        }

        return new InlineStylesViewModel(
            survey, NavigateToBookPathAtOffset, plan => ExtractInlineStylesAsync(book, plan, survey, runWithProgress));
    }

    /// <summary>
    /// "Extract" in the dialog: the chosen values become classes behind a checkpoint, file by file off the UI thread; the
    /// skipped files are reported as for every operation applied file by file. Returns whether it ran.
    /// </summary>
    public async Task<bool> ExtractInlineStylesAsync(
        Book book, InlineStyleExtractionPlan plan, InlineStyleSurvey survey, ProgressRunner? runWithProgress = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(survey);
        if (!ReferenceEquals(book, _currentBook) || plan.ClassNames.Count == 0)
        {
            return false;
        }

        _tabManager.SaveAllTabs();
        string title = ActionLabel(AppActionIds.ExtractInlineStyles);
        BookOperationOutcome<FileBatchResult> run = await RunBookOperationAsync(
            title, title, editsBook: true,
            (progress, cancel) => book.ExtractInlineStyles(plan, survey, new FileBatchRun(new FileBatchProgressMap(progress), cancel)),
            runWithProgress)
            .ConfigureAwait(true);
        if (!run.Completed)
        {
            return false;
        }

        FinishFileBatch(run.Result!, run.Checkpoint, "Operation_ExtractInlineStyles", "Status_InlineStylesExtracted");
        return true;
    }
}
