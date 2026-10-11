using System;
using System.Collections.Generic;
using System.Linq;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// Footnotes from the Code View context menu ("Footnotes" submenu): adding, editing (the text and the placement, local
/// or global), removing, going between a reference and its note, and renumbering. Every change saves the open tabs first,
/// is a text edit of the book files (<see cref="FootnoteEditor"/>) behind a checkpoint, and reloads the tabs and panels.
/// Footnotes of other origin are read-only until they are converted: renumbering leaves them as they are.
/// </summary>
public sealed partial class MainWindowViewModel
{
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
        return new FootnoteMenuState(hit, addError, hasFootnotes, footnotes.Count(f => !f.IsSignet));
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
        int foreign = FootnoteScanner.Scan(_currentBook).Count(f => !f.IsSignet);
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
    private FootnoteOptions FootnoteOptions => new(_settings.FootnoteStyles, _settings.FootnoteGroupByChapter);

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
