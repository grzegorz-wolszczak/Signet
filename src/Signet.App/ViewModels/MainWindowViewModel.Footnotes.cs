using System;
using System.Linq;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.Core.Footnotes;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// Footnotes from the Code View context menu ("Footnotes" submenu): adding, removing, moving between the local and
/// the global notes, going between a reference and its note, and renumbering. Every change saves the open tabs first,
/// is a text edit of the book files (<see cref="FootnoteEditor"/>) behind a checkpoint, and reloads the tabs and panels.
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
        bool hasFootnotes = hit is not null || FootnoteScanner.Scan(_currentBook).Count > 0;
        return new FootnoteMenuState(hit, addError, hasFootnotes);
    }

    /// <inheritdoc/>
    FootnotePlacement ICodeTabHost.LastFootnotePlacement =>
        _settings.FootnoteGlobalPlacement ? FootnotePlacement.Global : FootnotePlacement.Local;

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
        FootnoteError error = FootnoteEditor.Add(_currentBook, html, offset, content, placement, out Footnote? added);
        if (error != FootnoteError.None)
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(
                Strings.Format("Status_FootnoteNotAdded", AddFootnoteViewModel.ErrorText(error)),
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
        if (footnote is null || !FootnoteEditor.Remove(_currentBook, footnote))
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
    void ICodeTabHost.MoveFootnote(FootnoteHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Footnote? footnote = FootnoteEditor.Refresh(_currentBook, hit.Footnote);
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_MoveFootnote"));
        if (footnote is null || !FootnoteEditor.Move(_currentBook, footnote, out Footnote? moved))
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(Strings.Get("Status_FootnoteGone"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (moved is not null)
        {
            GoToFootnoteSide(moved, hit.Side);
        }

        _statusBar.ShowMessage(
            Strings.Get(moved?.Placement == FootnotePlacement.Local ? "Status_FootnoteMovedLocal" : "Status_FootnoteMovedGlobal"),
            TimeSpan.FromSeconds(4));
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
        bool checkpoint = AddCheckpointBefore(Strings.Get("CheckpointOp_RenumberFootnotes"));
        if (!FootnoteEditor.Renumber(_currentBook))
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(Strings.Get("Status_FootnotesUpToDate"), TimeSpan.FromSeconds(4));
            return;
        }

        RefreshAfterMaintenanceOperation();
        if (tab is not null && caret >= 0)
        {
            tab.GoToOffset(caret);
        }

        _statusBar.ShowMessage(Strings.Get("Status_FootnotesRenumbered"), TimeSpan.FromSeconds(4));
    }

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
