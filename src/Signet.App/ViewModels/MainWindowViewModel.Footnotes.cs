using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// Footnotes from the Code Editor context menu ("Footnotes" submenu): adding, editing (the text and the placement, local
/// or global), removing, going between a reference and its note, and renumbering. Every change saves the open tabs first,
/// is a text edit of the book files (<see cref="FootnoteEditor"/>) behind a checkpoint, and reloads the tabs and panels.
/// Footnotes of other origin are read-only until they are converted (the "Convert Footnotes of Other Origin" dialog):
/// renumbering leaves them as they are.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Raised by "Convert Footnotes of Other Origin…" (Tools → Footnotes, the Code Editor menu, the notice on a footnote
    /// of other origin) — the view calls <see cref="PrepareFootnoteConversionAsync"/> and shows the modal dialog.
    /// </summary>
    public event EventHandler? FootnoteConversionRequested;

    /// <inheritdoc/>
    void ICodeTabHost.ShowFootnoteConversion() => FootnoteConversionRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raised by "Unify Letters of Shared Footnotes…" (Tools → Footnotes, the Code Editor menu) — the view calls
    /// <see cref="PrepareFootnoteLetters"/> and shows the modal dialog.
    /// </summary>
    public event EventHandler? FootnoteLettersRequested;

    /// <inheritdoc/>
    void ICodeTabHost.ShowFootnoteLetters() => FootnoteLettersRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Prepares the "Unify Letters of Shared Footnotes" dialog with the style of Preferences: saves the open tabs and
    /// returns its view model, or <c>null</c> (with a message in the status bar) when no book is open, the book has no
    /// shared footnotes, or all of them are written in that style.
    /// </summary>
    public FootnoteLettersViewModel? PrepareFootnoteLetters()
    {
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        FootnoteLetterStyle style = _settings.FootnoteLetterStyle;
        if (!FootnoteScanner.Scan(book).Any(f => f.IsSignet && f.IsShared))
        {
            _statusBar.ShowMessage(Strings.Get("Status_NoSharedFootnotes"), TimeSpan.FromSeconds(4));
            return null;
        }

        if (FootnoteEditor.PlanLetters(book, style).Count == 0)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_FootnoteLettersAlreadyUnified", Strings.Get("FootnoteLetterStyle_" + style)), TimeSpan.FromSeconds(5));
            return null;
        }

        return new FootnoteLettersViewModel(
            style, s => FootnoteEditor.PlanLetters(book, s), NavigateToBookPathAtOffset, UnifyFootnoteLetters);
    }

    /// <summary>
    /// How many shared footnotes of the current book are not written in the style of Preferences (0 without a book): after
    /// the style changes in Preferences the window offers to unify them.
    /// </summary>
    public int CountFootnoteLetterMismatches()
    {
        if (_currentBook is not { } book)
        {
            return 0;
        }

        _tabManager.SaveAllTabs();
        return FootnoteEditor.PlanLetters(book, _settings.FootnoteLetterStyle).Count;
    }

    /// <summary>
    /// "Unify" in the dialog: rewrites the letters of <paramref name="items"/> in <paramref name="style"/> behind a
    /// checkpoint and refreshes the tabs. Returns whether anything changed.
    /// </summary>
    public bool UnifyFootnoteLetters(IReadOnlyList<LetterItem> items, FootnoteLetterStyle style)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_currentBook is null || items.Count == 0)
        {
            return false;
        }

        _tabManager.SaveAllTabs();
        AddCheckpointBefore(Strings.Get("CheckpointOp_UnifyFootnoteLetters"));
        int changed = FootnoteEditor.UnifyLetters(_currentBook, items, style, FootnoteOptions);
        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(
            Strings.Format("Status_FootnoteLettersUnified", changed, Strings.Get("FootnoteLetterStyle_" + style)), TimeSpan.FromSeconds(5));
        return true;
    }

    /// <summary>
    /// Prepares the "Convert Footnotes of Other Origin" dialog: saves the open tabs and plans the conversion off the UI
    /// thread (a book can have thousands of footnotes; the caller shows that it is working, modally, so nothing edits the
    /// book meanwhile). Returns the dialog's view model, or <c>null</c> when no book is open, the book changed in the
    /// meantime, or the book has no footnotes of other origin (then a message goes to the status bar).
    /// </summary>
    /// <param name="confirmWithoutCheckpoint">
    /// Asks whether to convert although the checkpoint before the conversion could not be created.
    /// </param>
    /// <param name="runWithProgress">
    /// Runs the conversion with a window that shows how far it is (<c>null</c>: without one, as in the tests).
    /// </param>
    public async Task<FootnoteConversionViewModel?> PrepareFootnoteConversionAsync(
        Func<Task<bool>> confirmWithoutCheckpoint, ProgressRunner? runWithProgress = null)
    {
        ArgumentNullException.ThrowIfNull(confirmWithoutCheckpoint);
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        ConversionPlan plan = await Task.Run(() => FootnoteEditor.PlanConversion(book)).ConfigureAwait(true);
        if (!ReferenceEquals(book, _currentBook))
        {
            return null;
        }

        if (plan.Items.Count == 0)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NoForeignFootnotes"), TimeSpan.FromSeconds(4));
            return null;
        }

        return new FootnoteConversionViewModel(
            plan, FootnoteEditor.FindGlobalFile(_currentBook)?.Filename, NavigateToBookPathAtOffset,
            request => ConvertFootnotesAsync(request, confirmWithoutCheckpoint, runWithProgress));
    }

    /// <summary>
    /// Converts footnotes of other origin ("Convert" in the dialog) behind a checkpoint; without one only after
    /// <paramref name="confirmWithoutCheckpoint"/>. When the check after the conversion finds problems the book is
    /// brought back to the checkpoint and the state of the conversion is thrown away. Returns the plan of the book
    /// afterwards with the problems, or <c>null</c> when no book is open or the user cancelled.
    /// </summary>
    /// <remarks>
    /// The conversion of a big book takes tens of seconds, so it runs off the UI thread under <paramref name="runWithProgress"/>
    /// (a progress window with the percent); the UI waits for it, so nothing else edits the book meanwhile.
    /// </remarks>
    public async Task<FootnoteConversionOutcome?> ConvertFootnotesAsync(
        ConversionRequest request, Func<Task<bool>> confirmWithoutCheckpoint, ProgressRunner? runWithProgress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(confirmWithoutCheckpoint);
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_ConvertFootnotes"));
        if (!checkpoint && !await confirmWithoutCheckpoint().ConfigureAwait(true))
        {
            return null;
        }

        Book converting = _currentBook!;
        ConversionResult result = await RunInBackgroundAsync(
            runWithProgress, editsBook: true,
            progress => FootnoteEditor.Convert(converting, request, FootnoteOptions, new ConversionProgressMap(progress))).ConfigureAwait(true);
        bool restored = false;
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Footnote conversion failed the check: {Problems}",
                string.Join("; ", result.Problems.Select(p => $"{p.Kind} {p.BookPath} {p.Detail}")));
            restored = checkpoint && AbandonCheckpoint();
            if (!restored)
            {
                RefreshAfterMaintenanceOperation();
            }

            _statusBar.ShowMessage(
                Strings.Get(restored ? "Status_FootnoteConversionFailedRestored" : "Status_FootnoteConversionFailedKept"),
                TimeSpan.FromSeconds(8), NotificationLevel.Warning);
        }
        else if (result.Converted == 0)
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }
        }
        else
        {
            RefreshAfterMaintenanceOperation();
            _statusBar.ShowMessage(
                result.RemovedFiles.Count == 0
                    ? Strings.Format("Status_FootnotesConverted", result.Converted)
                    : Strings.Format("Status_FootnotesConvertedFilesRemoved", result.Converted, string.Join(", ", result.RemovedFiles)),
                TimeSpan.FromSeconds(6));
        }

        Book book = _currentBook!;
        ConversionPlan plan = await RunInBackgroundAsync(
            runWithProgress, editsBook: false,
            progress =>
            {
                progress.Report(new WorkProgress(Strings.Get("FootnoteConversion_Analyzing"), null));
                return FootnoteEditor.PlanConversion(book);
            }).ConfigureAwait(true);
        return new FootnoteConversionOutcome(plan, result.Problems, restored, FootnoteEditor.FindGlobalFile(book)?.Filename);
    }

    // The book raises its events (a file added or removed, the book modified) on the thread that edits it. While that is not the
    // UI thread the handlers that touch the UI wait; the UI is brought up to date once the work is done.
    private bool _bookEditedInBackground;

    // Runs `work` off the UI thread (under `runner`, when there is one: the progress window) and returns its result.
    private async Task<T> RunInBackgroundAsync<T>(ProgressRunner? runner, bool editsBook, Func<IProgress<WorkProgress>, T> work)
    {
        T result = default!;
        Task Run(IProgress<WorkProgress> progress) => Task.Run(() => result = work(progress));
        if (editsBook)
        {
            _bookEditedInBackground = true;
            BookBrowser.SuspendRebuilds = true;
        }

        try
        {
            await (runner is null ? Run(new Progress<WorkProgress>()) : runner(Run)).ConfigureAwait(true);
        }
        finally
        {
            if (editsBook)
            {
                _bookEditedInBackground = false;
                BookBrowser.SuspendRebuilds = false;
                RefreshTitle();
                _toc.Refresh();
            }
        }

        return result;
    }

    // What Convert reports as what the progress window shows: the percent while the footnotes are converted, the stages around it
    // without one (the cleaning up and the numbering take most of the time and cannot tell how far they are).
    private sealed class ConversionProgressMap : IProgress<ConversionProgress>
    {
        private readonly IProgress<WorkProgress> _target;

        public ConversionProgressMap(IProgress<WorkProgress> target) => _target = target;

        public void Report(ConversionProgress value) => _target.Report(value.Stage switch
        {
            ConversionStage.Preparing => new WorkProgress(Strings.Get("FootnoteConversion_Progress_Preparing"), 0),
            ConversionStage.Converting => WorkProgress.InStage(
                Strings.Format("FootnoteConversion_Progress_Converting", value.Done, value.Total), 0.05, 0.3, value.Done, value.Total),
            ConversionStage.Finishing => new WorkProgress(Strings.Get("FootnoteConversion_Progress_Finishing"), null),
            _ => new WorkProgress(Strings.Get("FootnoteConversion_Progress_Checking"), null),
        });
    }

    /// <inheritdoc/>
    FootnoteMenuState? ICodeTabHost.GetFootnoteState(HtmlResource html, int offset)
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        FootnoteError addError = FootnoteEditor.CanAdd(_currentBook, html, offset);
        FootnoteHit? hit = addError == FootnoteError.None ? null : FootnoteEditor.FindAt(_currentBook, html, offset);
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(_currentBook);
        bool hasFootnotes = hit is not null || footnotes.Count > 0;
        int targets = footnotes.Count(f => f.IsSignet && f.ReferenceIndex == 0
            && (f.Placement == FootnotePlacement.Global || ReferenceEquals(f.NoteFile, html)));
        return new FootnoteMenuState(hit, addError, hasFootnotes, footnotes.Count(f => !f.IsSignet && f.ReferenceIndex == 0), targets);
    }

    /// <inheritdoc/>
    FootnotePlacement ICodeTabHost.LastFootnotePlacement =>
        _settings.FootnoteGlobalPlacement ? FootnotePlacement.Global : FootnotePlacement.Local;

    /// <inheritdoc/>
    (int Local, int Global) ICodeTabHost.GetFootnoteNumbers(HtmlResource html, int offset) =>
        _currentBook is null
            ? (1, 1)
            : (FootnoteEditor.NumberAt(_currentBook, html, offset, FootnotePlacement.Local),
                FootnoteEditor.NumberAt(_currentBook, html, offset, FootnotePlacement.Global));

    /// <inheritdoc/>
    FootnoteError ICodeTabHost.ValidateFootnoteContent(string content) =>
        FootnoteEditor.ValidateContent(content, _currentBook?.EpubVersion ?? "3.0");

    /// <inheritdoc/>
    void ICodeTabHost.AddFootnote(HtmlResource html, int offset, string content, FootnotePlacement placement)
    {
        if (_currentBook is null)
        {
            return;
        }

        _settings.FootnoteGlobalPlacement = placement == FootnotePlacement.Global;
        _tabManager.SaveAllTabs();
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_AddFootnote"));
        FootnoteError error = FootnoteEditor.Add(
            _currentBook, html, offset, content, placement, out Footnote? added, FootnoteOptions);
        if (error != FootnoteError.None)
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(
                Strings.Format("Status_FootnoteNotAdded", FootnoteViewModel.ErrorText(error)),
                TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (added is not null)
        {
            GoToFootnoteSide(added, FootnoteSide.Reference);
        }

        _statusBar.ShowMessage(
            Strings.Get(placement == FootnotePlacement.Local ? "Status_FootnoteAddedLocal" : "Status_FootnoteAddedGlobal"),
            TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    ReferenceTargetList? ICodeTabHost.GetFootnoteReferenceTargets(HtmlResource html, int offset)
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        return FootnoteEditor.ReferenceTargets(_currentBook, html, offset);
    }

    /// <inheritdoc/>
    void ICodeTabHost.AddFootnoteReference(HtmlResource html, int offset, Footnote target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_AddFootnoteReference"));
        FootnoteError error = FootnoteEditor.AddReference(_currentBook, html, offset, target, out Footnote? added, FootnoteOptions);
        if (error != FootnoteError.None)
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(
                Strings.Format("Status_FootnoteReferenceNotAdded", FootnoteViewModel.ErrorText(error)),
                TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (added is not null)
        {
            GoToFootnoteSide(added, FootnoteSide.Reference);
        }

        _statusBar.ShowMessage(Strings.Get("Status_FootnoteReferenceAdded"), TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    void ICodeTabHost.RemoveFootnote(FootnoteHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Footnote? footnote = FootnoteEditor.Refresh(_currentBook, hit.Footnote);

        // Where the caret goes afterwards: where the removed part was, or where the reference was when the file of the
        // note goes away with it (the last global note).
        (HtmlResource File, int Offset)? reference = footnote is null ? null : FootnoteEditor.Locate(footnote, FootnoteSide.Reference);
        (HtmlResource File, int Offset)? place = footnote is null ? null : FootnoteEditor.Locate(footnote, hit.Side);
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_RemoveFootnote"));
        if (footnote is null || !FootnoteEditor.Remove(_currentBook, footnote, FootnoteOptions))
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(Strings.Get("Status_FootnoteGone"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (place is { } target && _currentBook.GetHtmlResources().Contains(target.File))
        {
            NavigateToBookPathAtOffset(target.File.BookPath, target.Offset);
        }
        else if (reference is { } fallback)
        {
            NavigateToBookPathAtOffset(fallback.File.BookPath, fallback.Offset);
        }

        _statusBar.ShowMessage(Strings.Get("Status_FootnoteRemoved"), TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    void ICodeTabHost.RemoveFootnoteReference(FootnoteHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Footnote? footnote = FootnoteEditor.Refresh(_currentBook, hit.Footnote);
        (HtmlResource File, int Offset)? place = footnote is null ? null : FootnoteEditor.Locate(footnote, FootnoteSide.Reference);
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_RemoveFootnoteReference"));
        if (footnote is null || !FootnoteEditor.RemoveReference(_currentBook, footnote, FootnoteOptions))
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(Strings.Get("Status_FootnoteGone"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (place is { } target)
        {
            NavigateToBookPathAtOffset(target.File.BookPath, target.Offset);
        }

        _statusBar.ShowMessage(Strings.Get("Status_FootnoteReferenceRemoved"), TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    FootnoteEditState? ICodeTabHost.GetFootnoteEdit(FootnoteHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        if (FootnoteEditor.Refresh(_currentBook, hit.Footnote) is { } footnote
            && FootnoteEditor.GetContent(footnote) is { } content
            && FootnoteEditor.LocateReferenceRange(footnote) is { } reference
            && FootnoteEditor.Locate(footnote, FootnoteSide.Reference) is { } at)
        {
            return new FootnoteEditState(
                footnote, reference.File.GetText(), reference.Start, reference.End, content,
                FootnoteEditor.NumberAt(_currentBook, at.File, at.Offset, FootnotePlacement.Local),
                FootnoteEditor.NumberAt(_currentBook, at.File, at.Offset, FootnotePlacement.Global),
                FootnoteEditor.CanMove(_currentBook, footnote));
        }

        _statusBar.ShowMessage(Strings.Get("Status_FootnoteGone"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        return null;
    }

    /// <inheritdoc/>
    FootnoteError ICodeTabHost.ValidateFootnoteEdit(Footnote footnote, string content) =>
        FootnoteEditor.ValidateEdit(footnote, content, _currentBook?.EpubVersion ?? "3.0");

    /// <inheritdoc/>
    void ICodeTabHost.EditFootnote(FootnoteHit hit, string content, FootnotePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(content);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Footnote? footnote = FootnoteEditor.Refresh(_currentBook, hit.Footnote);
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_EditFootnote"));
        Footnote? edited = null;
        FootnoteError error = footnote is null
            ? FootnoteError.NotFound
            : FootnoteEditor.Edit(_currentBook, footnote, content, placement, out edited, FootnoteOptions);
        if (error != FootnoteError.None)
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(
                Strings.Format("Status_FootnoteNotEdited", FootnoteViewModel.ErrorText(error)),
                TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (edited is not null)
        {
            GoToFootnoteSide(edited, hit.Side);
        }

        string message = footnote!.Placement == placement ? "Status_FootnoteEdited"
            : placement == FootnotePlacement.Local ? "Status_FootnoteMovedLocal"
            : "Status_FootnoteMovedGlobal";
        _statusBar.ShowMessage(Strings.Get(message), TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    FootnoteMarkup? ICodeTabHost.PreviewFootnote(
        HtmlResource html, int referenceStart, int referenceEnd, string content, FootnotePlacement placement, int number,
        Footnote? footnote) =>
        _currentBook is null
            ? null
            : FootnoteEditor.Preview(
                _currentBook, html, referenceStart, referenceEnd, content, placement, number, footnote, FootnoteOptions);

    /// <inheritdoc/>
    bool ICodeTabHost.ConfirmFootnoteRemoval
    {
        get => _settings.ConfirmFootnoteRemoval;
        set => _settings.ConfirmFootnoteRemoval = value;
    }

    /// <inheritdoc/>
    void ICodeTabHost.GoToFootnote(FootnoteHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        FootnoteSide other = hit.Side == FootnoteSide.Reference ? FootnoteSide.Note : FootnoteSide.Reference;
        if (FootnoteEditor.Refresh(_currentBook, hit.Footnote) is not { } footnote || !GoToFootnoteSide(footnote, other))
        {
            _statusBar.ShowMessage(Strings.Get("Status_FootnoteGone"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }
    }

    /// <inheritdoc/>
    void ICodeTabHost.RenumberFootnotes()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        CodeTabViewModel? tab = ActiveCodeTab;
        int caret = tab?.CaretOffset ?? -1;
        int foreign = FootnoteScanner.Scan(_currentBook).Count(f => !f.IsSignet && f.ReferenceIndex == 0);
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_RenumberFootnotes"));
        if (!FootnoteEditor.Renumber(_currentBook, FootnoteOptions))
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            ShowRenumberResult("Status_FootnotesUpToDate", foreign);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (tab is not null && caret >= 0)
        {
            tab.GoToOffset(caret);
        }

        ShowRenumberResult("Status_FootnotesRenumbered", foreign);
    }

    // The result of "Renumber Footnotes"; with footnotes of other origin in the book, a warning that they were skipped.
    private void ShowRenumberResult(string key, int foreign)
    {
        if (foreign == 0)
        {
            _statusBar.ShowMessage(Strings.Get(key), TimeSpan.FromSeconds(4));
            return;
        }

        _statusBar.ShowMessage(
            Strings.Format(key + "ForeignSkipped", foreign), TimeSpan.FromSeconds(8), NotificationLevel.Warning);
    }

    // How Signet writes its footnotes (Preferences → Footnotes).
    private FootnoteOptions FootnoteOptions =>
        new(_settings.FootnoteStyles, _settings.FootnoteGroupByChapter, _settings.FootnoteLetterStyle);

    // Opens the file of the reference or the note of `footnote` with the caret on it; false when it is gone.
    private bool GoToFootnoteSide(Footnote footnote, FootnoteSide side)
    {
        if (FootnoteEditor.Locate(footnote, side) is not { } place)
        {
            return false;
        }

        NavigateToBookPathAtOffset(place.File.BookPath, place.Offset);
        return true;
    }
}
