using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Resources;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// The Show Bookmarks popup (modelled on IntelliJ's): a flat list of the bookmarks, "file :line", on the left and, on the
/// right, the whole file of the selected one — read-only, coloured like Code Editor, with the line of the bookmark in
/// view. Choosing a bookmark goes to it and closes the popup. With no bookmark the popup opens all the same and says how
/// to add one.
/// </summary>
public sealed partial class BookmarksPopupViewModel : ViewModelBase
{
    private readonly BookmarkList _bookmarks;
    private readonly Func<string, Resource?> _resolve;
    private readonly Func<string, string?> _textOf;
    private readonly Action<Bookmark> _navigate;
    private readonly string _toggleShortcutText;

    [ObservableProperty]
    private BookmarkPopupItem? _selected;

    /// <summary>Creates the popup model.</summary>
    /// <param name="bookmarks">The bookmarks of the book.</param>
    /// <param name="resolve">The resource of a bookpath (<c>null</c> when it no longer exists).</param>
    /// <param name="textOf">The current text of a file (of its open tab when there is one).</param>
    /// <param name="navigate">Goes to a bookmark.</param>
    /// <param name="toggleShortcutText">The shortcut of Toggle Bookmark, for the hint of the empty popup (empty when none).</param>
    /// <param name="appearance">The font of Code Editor, which the preview uses; <see langword="null"/> = the default one.</param>
    public BookmarksPopupViewModel(
        BookmarkList bookmarks,
        Func<string, Resource?> resolve,
        Func<string, string?> textOf,
        Action<Bookmark> navigate,
        string toggleShortcutText,
        CodeEditorAppearance? appearance = null)
    {
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _textOf = textOf ?? throw new ArgumentNullException(nameof(textOf));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        _toggleShortcutText = toggleShortcutText ?? string.Empty;
        Appearance = appearance ?? CodeEditorAppearance.Default;
        foreach (Bookmark bookmark in _bookmarks.Items)
        {
            Items.Add(new BookmarkPopupItem(bookmark, $"{System.IO.Path.GetFileName(bookmark.BookPath)} :{bookmark.Line}"));
        }

        Selected = Items.FirstOrDefault();
    }

    /// <summary>Raised when the popup should close (after going to a bookmark).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The bookmarks, ordered by file and line.</summary>
    public ObservableCollection<BookmarkPopupItem> Items { get; } = new();

    /// <summary>Whether there is any bookmark (otherwise the popup shows <see cref="EmptyTitle"/> and <see cref="EmptyHint"/>).</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>The font of Code Editor (Preferences), which the preview is shown in.</summary>
    public CodeEditorAppearance Appearance { get; }

    /// <summary>The window title.</summary>
    public string Title => Strings.Get("Panel_Bookmarks");

    /// <summary>The first line of the empty popup.</summary>
    public string EmptyTitle => Strings.Get("BookmarksPopup_EmptyTitle");

    /// <summary>The second line of the empty popup: how to add a bookmark, with the shortcut when there is one.</summary>
    public string EmptyHint => _toggleShortcutText.Length > 0
        ? Strings.Format("BookmarksPopup_EmptyHintShortcut", _toggleShortcutText)
        : Strings.Get("BookmarksPopup_EmptyHint");

    /// <summary>The text of the file of the selected bookmark; empty without one or when the file is gone.</summary>
    public string PreviewText => Selected is { } item ? _textOf(item.Bookmark.BookPath) ?? string.Empty : string.Empty;

    /// <summary>The syntax of the file of the selected bookmark (for the colours of the preview).</summary>
    public CodeEditorSyntax PreviewSyntax =>
        Selected is { } item && _resolve(item.Bookmark.BookPath) is { } resource
            ? CodeEditorSyntaxMap.ForResource(resource)
            : CodeEditorSyntax.PlainText;

    /// <summary>The file of the selected bookmark; empty without one.</summary>
    public string PreviewBookPath => Selected?.Bookmark.BookPath ?? string.Empty;

    /// <summary>The lines of the file of the selected bookmark that carry a bookmark (the bookmark column of the preview).</summary>
    public System.Collections.Generic.IReadOnlyCollection<int> BookmarkedLinesOfPreview =>
        Selected is { } item ? _bookmarks.LinesIn(item.Bookmark.BookPath) : Array.Empty<int>();

    /// <summary>The line of the selected bookmark (1-based); 0 without one.</summary>
    public int PreviewLine => Selected?.Bookmark.Line ?? 0;

    /// <summary>Raised when the selection changed — the view shows the preview of the new one.</summary>
    public event EventHandler? PreviewChanged;

    /// <summary>Moves the selection by <paramref name="delta"/> items, staying within the list.</summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        int index = Selected is { } current ? Items.IndexOf(current) : -1;
        Selected = Items[Math.Clamp(index + delta, 0, Items.Count - 1)];
    }

    /// <summary>Goes to the bookmark of an item and closes the popup.</summary>
    public void Navigate(BookmarkPopupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _navigate(item.Bookmark);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes the selected bookmark and selects the one that takes its place.</summary>
    public void RemoveSelected()
    {
        if (Selected is not { } item)
        {
            return;
        }

        int index = Items.IndexOf(item);
        _bookmarks.Remove(item.Bookmark);
        Items.Remove(item);
        OnPropertyChanged(nameof(HasItems));
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    partial void OnSelectedChanged(BookmarkPopupItem? value)
    {
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewSyntax));
        OnPropertyChanged(nameof(PreviewBookPath));
        OnPropertyChanged(nameof(PreviewLine));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>An entry of the Show Bookmarks popup.</summary>
/// <param name="Bookmark">The bookmark.</param>
/// <param name="Label">Its text in the list: the file name and the line, e.g. <c>chapter1.xhtml :8</c>.</param>
public sealed record BookmarkPopupItem(Bookmark Bookmark, string Label);
