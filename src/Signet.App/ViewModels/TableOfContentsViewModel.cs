using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Toc;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Table Of Contents" panel — a tree built from the nav (EPUB 3) or the NCX
/// (EPUB 2), for preview and navigation only. Editing is done in the "Edit Table Of Contents" dialog.
/// </summary>
public sealed class TableOfContentsViewModel : ViewModelBase
{
    private Book? _book;
    private bool _hasEntries;

    /// <summary>Raised when an entry is chosen (double-click / Enter) — handled by the main window.</summary>
    public event EventHandler<TocDisplayEntry>? EntryActivated;

    /// <summary>Raised by the "Edit…" button — the main window opens the "Edit Table Of Contents" dialog.</summary>
    public event EventHandler? EditRequested;

    /// <summary>Command of the "Edit…" button in the panel.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Creates the panel view model.</summary>
    public TableOfContentsViewModel()
    {
        EditCommand = new RelayCommand(() => EditRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Top-level nodes of the table of contents tree.</summary>
    public ObservableCollection<TocEntryViewModel> Nodes { get; } = new();

    /// <summary>Whether the publication has any table of contents (for the empty-panel message).</summary>
    public bool HasEntries
    {
        get => _hasEntries;
        private set => SetProperty(ref _hasEntries, value);
    }

    /// <summary>Sets the publication and rebuilds the tree (or clears it for <c>null</c>).</summary>
    public void SetBook(Book? book)
    {
        _book = book;
        Refresh();
    }

    /// <summary>Rebuilds the tree from the current publication — called after changes to the book.</summary>
    public void Refresh()
    {
        Nodes.Clear();
        IReadOnlyList<TocDisplayEntry> entries =
            _book?.GetTocForDisplay() ?? Array.Empty<TocDisplayEntry>();

        foreach (TocDisplayEntry entry in entries)
        {
            Nodes.Add(new TocEntryViewModel(entry));
        }

        HasEntries = Nodes.Count > 0;
    }

    /// <summary>Requests navigation to an entry (called from the view).</summary>
    public void Activate(TocEntryViewModel node) => EntryActivated?.Invoke(this, node.Entry);
}

/// <summary>Tree node of the "Table Of Contents" panel.</summary>
public sealed class TocEntryViewModel
{
    /// <summary>Creates a node for a table of contents entry (recursively creates the children).</summary>
    public TocEntryViewModel(TocDisplayEntry entry)
    {
        Entry = entry;
        var children = new List<TocEntryViewModel>();
        foreach (TocDisplayEntry child in entry.Children)
        {
            children.Add(new TocEntryViewModel(child));
        }

        Children = children;
    }

    /// <summary>The underlying entry (navigation target).</summary>
    public TocDisplayEntry Entry { get; }

    /// <summary>Entry text.</summary>
    public string Title => Entry.Title.Length > 0 ? Entry.Title : Strings.Get("Toc_Untitled");

    /// <summary>The target as <c>bookpath#fragment</c> (only the bookpath without a fragment; empty without a link).</summary>
    public string TargetDisplay => Entry.Fragment.Length > 0 ? Entry.TargetBookPath + "#" + Entry.Fragment : Entry.TargetBookPath;

    /// <summary>Whether the node is expanded in the panel (all are at first).</summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>Child nodes.</summary>
    public IReadOnlyList<TocEntryViewModel> Children { get; }
}
