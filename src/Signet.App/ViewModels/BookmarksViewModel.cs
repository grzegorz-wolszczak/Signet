using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Bookmarks" panel: the bookmarks of the book as a tree grouped by file, like the Bookmarks tool
/// window of the JetBrains IDEs — a node per file with a node per bookmark (its line and the text of the line).
/// Double-clicking or pressing Enter on a bookmark raises <see cref="BookmarkActivated"/>; Delete (or the button)
/// removes the selected bookmark, or all of a file when its node is selected.
/// </summary>
public sealed class BookmarksViewModel : ViewModelBase
{
    private readonly BookmarkList _bookmarks;
    private BookmarkNode? _selected;

    /// <summary>Creates the view model over the bookmarks of the book (the tree follows <see cref="BookmarkList.Changed"/>).</summary>
    public BookmarksViewModel(BookmarkList bookmarks)
    {
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        DeleteCommand = new RelayCommand(DeleteSelected, () => Selected is not null);
        ClearCommand = new RelayCommand(_bookmarks.Clear, () => HasBookmarks);
        _bookmarks.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>The user chose a bookmark to go to (handled by the main window).</summary>
    public event EventHandler<Bookmark>? BookmarkActivated;

    /// <summary>The file nodes — each with the bookmarks of the file.</summary>
    public ObservableCollection<BookmarkNode> Roots { get; } = new();

    /// <summary>Whether the book has any bookmark (otherwise the panel says how to add one).</summary>
    public bool HasBookmarks => _bookmarks.Items.Count > 0;

    /// <summary>The selected node.</summary>
    public BookmarkNode? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Deletes the selected bookmark (the bookmarks of the file for a file node).</summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>Deletes all the bookmarks.</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>Goes to the bookmark of a node; a file node does nothing (the view expands or collapses it).</summary>
    public void Activate(BookmarkNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Bookmark is { } bookmark)
        {
            BookmarkActivated?.Invoke(this, bookmark);
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not { } node)
        {
            return;
        }

        if (node.Bookmark is { } bookmark)
        {
            _bookmarks.Remove(bookmark);
        }
        else
        {
            foreach (Bookmark child in node.Children.Select(c => c.Bookmark).OfType<Bookmark>().ToList())
            {
                _bookmarks.Remove(child);
            }
        }
    }

    private void Rebuild()
    {
        Roots.Clear();
        Selected = null;
        foreach (IGrouping<string, Bookmark> file in _bookmarks.Items.GroupBy(b => b.BookPath, StringComparer.Ordinal))
        {
            BookmarkNode node = new(file.Key, $"{System.IO.Path.GetFileName(file.Key)} ({file.Count()})");
            foreach (Bookmark bookmark in file)
            {
                string text = _bookmarks.LineText(bookmark);
                node.Children.Add(new BookmarkNode(file.Key, text.Length > 0 ? $"{bookmark.Line}: {text}" : bookmark.Line.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    Bookmark = bookmark,
                });
            }

            Roots.Add(node);
        }

        OnPropertyChanged(nameof(HasBookmarks));
        ClearCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>A node of the "Bookmarks" panel: a file (<see cref="Bookmark"/> is <see langword="null"/>) or a bookmark.</summary>
public sealed class BookmarkNode
{
    /// <summary>Creates a node of the file at <paramref name="filePath"/>.</summary>
    public BookmarkNode(string filePath, string text)
    {
        FilePath = filePath;
        Text = text;
    }

    /// <summary>Book path of the file the node is for (or in).</summary>
    public string FilePath { get; }

    /// <summary>The text of the node: a file with the number of its bookmarks, or a line with its text.</summary>
    public string Text { get; }

    /// <summary>The bookmark of the node; <see langword="null"/> for a file.</summary>
    public Bookmark? Bookmark { get; init; }

    /// <summary>
    /// Whether the node is expanded in the tree (all are at first). The tree writes it back when the user expands or
    /// collapses a file, so it needs a setter.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>The bookmarks of a file; none for a bookmark.</summary>
    public List<BookmarkNode> Children { get; } = new();
}
