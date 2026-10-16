using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    /// Prepares the "Convert Footnotes of Other Origin" dialog: saves the open tabs and plans the conversion off the UI
    /// thread (a book can have thousands of footnotes; the caller shows that it is working, modally, so nothing edits the
    /// book meanwhile). Returns the dialog's view model, or <c>null</c> when no book is open, the book changed in the
    /// meantime, or the book has no footnotes of other origin (then a message goes to the status bar).
    /// </summary>
    /// <param name="confirmWithoutCheckpoint">
    /// Asks whether to convert although the checkpoint before the conversion could not be created.
    /// </param>
    public async Task<FootnoteConversionViewModel?> PrepareFootnoteConversionAsync(Func<Task<bool>> confirmWithoutCheckpoint)
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
            request => ConvertFootnotesAsync(request, confirmWithoutCheckpoint));
    }

    /// <summary>
    /// Converts footnotes of other origin ("Convert" in the dialog) behind a checkpoint; without one only after
    /// <paramref name="confirmWithoutCheckpoint"/>. When the check after the conversion finds problems the book is
    /// brought back to the checkpoint and the state of the conversion is thrown away. Returns the plan of the book
    /// afterwards with the problems, or <c>null</c> when no book is open or the user cancelled.
    /// </summary>
    public async Task<FootnoteConversionOutcome?> ConvertFootnotesAsync(
        ConversionRequest request, Func<Task<bool>> confirmWithoutCheckpoint)
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

        ConversionResult result = FootnoteEditor.Convert(_currentBook!, request, FootnoteOptions);
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
        return new FootnoteConversionOutcome(
            FootnoteEditor.PlanConversion(book), result.Problems, restored, FootnoteEditor.FindGlobalFile(book)?.Filename);
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
        return new FootnoteMenuState(hit, addError, hasFootnotes, footnotes.Count(f => !f.IsSignet && f.ReferenceIndex == 0));
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
                FootnoteEditor.NumberAt(_currentBook, at.File, at.Offset, FootnotePlacement.Global));
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
