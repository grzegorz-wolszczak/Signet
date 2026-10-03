using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Toolbars;

namespace Signet.App.Menu;

/// <summary>Source of the "Recent Files" list inserted directly into the File menu.</summary>
public interface IRecentFilesMenu
{
    /// <summary>Paths of recently opened files to show in the menu (newest first, already trimmed to the limit).</summary>
    IReadOnlyList<string> RecentFiles { get; }

    /// <summary>Raised when the list changes.</summary>
    event EventHandler? Changed;

    /// <summary>Opens a file from the list.</summary>
    void Open(string path);

    /// <summary>Clears the whole list.</summary>
    void ClearAll();
}

/// <summary>Source of the session bookmarks list for the dynamic "Bookmarks" submenu.</summary>
public interface IBookmarksMenu
{
    /// <summary>Session bookmarks in the order they were added.</summary>
    IReadOnlyList<Signet.App.ViewModels.Bookmark> Bookmarks { get; }

    /// <summary>Raised when the bookmarks list changes.</summary>
    event EventHandler? BookmarksChanged;

    /// <summary>Navigates to the given bookmark.</summary>
    void GoToBookmark(Signet.App.ViewModels.Bookmark bookmark);

    /// <summary>Removes all bookmarks.</summary>
    void ClearBookmarks();
}

/// <summary>
/// Builds the <see cref="MenuItemViewModel"/> tree from the static <see cref="MenuLayout"/>,
/// binding items to actions from <see cref="AppActionRegistry"/>. The empty "Toolbars" submenu
/// is filled dynamically with toolbar visibility toggles.
/// </summary>
public sealed class MenuBuilder
{
    private readonly AppActionRegistry _actions;
    private readonly ToolbarManager _toolbars;
    private readonly ICommand _customizeToolbarsCommand;
    private readonly IRecentFilesMenu? _recentFiles;
    private readonly IBookmarksMenu? _bookmarks;

    // Items with text that does not come from an action (submenu headers, toolbar toggles…) — to be
    // recomputed after a language change; action items track AppAction.Text themselves.
    private readonly List<(MenuItemViewModel Item, Func<string> Header)> _localizedHeaders = new();
    private readonly List<RecentFilesSegment> _recentFileSegments = new();
    private readonly List<ObservableCollection<MenuItemViewModel>> _bookmarkLists = new();

    /// <summary>Creates the builder.</summary>
    /// <param name="actions">Action registry.</param>
    /// <param name="toolbars">Toolbar manager (for the "Toolbars" submenu).</param>
    /// <param name="customizeToolbars">Command that opens the toolbar configuration window.</param>
    /// <param name="recentFiles">Source of the "Recent Files" list (optional — without it the submenu is empty).</param>
    /// <param name="bookmarks">Source of the "Bookmarks" list (optional — without it the submenu is empty).</param>
    public MenuBuilder(
        AppActionRegistry actions,
        ToolbarManager toolbars,
        ICommand customizeToolbars,
        IRecentFilesMenu? recentFiles = null,
        IBookmarksMenu? bookmarks = null)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _toolbars = toolbars ?? throw new ArgumentNullException(nameof(toolbars));
        _customizeToolbarsCommand = customizeToolbars ?? throw new ArgumentNullException(nameof(customizeToolbars));
        _recentFiles = recentFiles;
        _bookmarks = bookmarks;
    }

    /// <summary>
    /// Recomputes the texts of the built menu after the UI language changes (in place — without
    /// rebuilding, so as not to duplicate the subscriptions of the recent files and bookmarks lists).
    /// </summary>
    public void RefreshTexts()
    {
        foreach ((MenuItemViewModel item, Func<string> header) in _localizedHeaders)
        {
            item.Header = header();
        }

        _recentFileSegments.ForEach(Rebuild);
        _bookmarkLists.ForEach(RebuildBookmarks);
    }

    private MenuItemViewModel Localized(MenuItemViewModel item, Func<string> header)
    {
        _localizedHeaders.Add((item, header));
        return item;
    }

    /// <summary>Builds the full main menu.</summary>
    public IReadOnlyList<MenuItemViewModel> Build() =>
        MenuLayout.TopLevel.Select(BuildNode).OfType<MenuItemViewModel>().ToList();

    private MenuItemViewModel? BuildNode(MenuNode node)
    {
        switch (node)
        {
            case MenuNode.SeparatorNode:
                return MenuItemViewModel.Separator();

            case MenuNode.ActionNode action:
                AppAction? resolved = _actions.Get(action.Id);
                return resolved is null ? null : MenuItemViewModel.ForAction(resolved);

            case MenuNode.BookmarksNode bookmarks:
                return Localized(
                    MenuItemViewModel.Submenu(AppAction.ConvertMnemonics(ActionTexts.MenuHeader(bookmarks.Header)), BuildBookmarksSubmenu()),
                    () => AppAction.ConvertMnemonics(ActionTexts.MenuHeader(bookmarks.Header)));

            case MenuNode.SubmenuNode submenu:
                string Header() => AppAction.ConvertMnemonics(ActionTexts.MenuHeader(submenu.Header));
                if (submenu.Children.Count == 0 && submenu.Header == "&Toolbars")
                {
                    return Localized(MenuItemViewModel.Submenu(Header(), BuildToolbarsSubmenu()), Header);
                }

                return Localized(MenuItemViewModel.Submenu(Header(), BuildChildren(submenu.Children)), Header);

            default:
                return null;
        }
    }

    private ObservableCollection<MenuItemViewModel> BuildChildren(IReadOnlyList<MenuNode> nodes)
    {
        ObservableCollection<MenuItemViewModel> items = new();
        foreach (MenuNode node in nodes)
        {
            if (node is MenuNode.RecentFilesNode)
            {
                // A flat list: items are inserted directly into the menu, right after the item
                // preceding the node.
                RecentFilesSegment segment = new(items, items.LastOrDefault());
                _recentFileSegments.Add(segment);
                Rebuild(segment);
                if (_recentFiles is not null)
                {
                    _recentFiles.Changed += (_, _) => Rebuild(segment);
                }

                continue;
            }

            if (BuildNode(node) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private void Rebuild(RecentFilesSegment segment)
    {
        ObservableCollection<MenuItemViewModel> items = segment.Items;
        int start = segment.Anchor is null ? 0 : items.IndexOf(segment.Anchor) + 1;
        for (int i = 0; i < segment.Count; i++)
        {
            items.RemoveAt(start);
        }

        List<MenuItemViewModel> entries = new();
        IReadOnlyList<string> files = _recentFiles?.RecentFiles ?? Array.Empty<string>();
        if (files.Count > 0)
        {
            int index = 1;
            foreach (string path in files)
            {
                string captured = path;
                // "&N name"; accelerators only for 1–9. Underscores in the file name
                // are doubled so that Avalonia does not take them for an access key.
                string name = Path.GetFileName(path).Replace("_", "__", StringComparison.Ordinal);
                string label = index <= 9 ? $"_{index} {name}" : $"{index} {name}";
                entries.Add(MenuItemViewModel.ForCommand(label, new RelayCommand(() => _recentFiles!.Open(captured))));
                index++;
            }

            // Extra item: clear the list.
            entries.Add(MenuItemViewModel.ForCommand(Strings.Get("Menu_ClearRecentFiles"), new RelayCommand(() => _recentFiles!.ClearAll())));
            entries.Add(MenuItemViewModel.Separator());
        }

        for (int i = 0; i < entries.Count; i++)
        {
            items.Insert(start + i, entries[i]);
        }

        segment.Count = entries.Count;
    }

    /// <summary>The fragment with recent files inserted into the menu: after <see cref="Anchor"/>, <see cref="Count"/> items.</summary>
    private sealed class RecentFilesSegment(ObservableCollection<MenuItemViewModel> items, MenuItemViewModel? anchor)
    {
        public ObservableCollection<MenuItemViewModel> Items { get; } = items;

        public MenuItemViewModel? Anchor { get; } = anchor;

        public int Count { get; set; }
    }

    private ObservableCollection<MenuItemViewModel> BuildBookmarksSubmenu()
    {
        ObservableCollection<MenuItemViewModel> items = new();
        RebuildBookmarks(items);
        _bookmarkLists.Add(items);

        if (_bookmarks is not null)
        {
            _bookmarks.BookmarksChanged += (_, _) => RebuildBookmarks(items);
        }

        return items;
    }

    private void RebuildBookmarks(ObservableCollection<MenuItemViewModel> items)
    {
        items.Clear();

        IReadOnlyList<Signet.App.ViewModels.Bookmark> marks =
            _bookmarks?.Bookmarks ?? Array.Empty<Signet.App.ViewModels.Bookmark>();
        if (marks.Count == 0)
        {
            items.Add(MenuItemViewModel.ForCommand(Strings.Get("Menu_NoBookmarks"), new RelayCommand(() => { }, () => false)));
            return;
        }

        int index = 1;
        foreach (Signet.App.ViewModels.Bookmark mark in marks)
        {
            Signet.App.ViewModels.Bookmark captured = mark;
            items.Add(MenuItemViewModel.ForCommand(
                $"_{index} {mark.Name}", new RelayCommand(() => _bookmarks!.GoToBookmark(captured))));
            index++;
        }

        items.Add(MenuItemViewModel.Separator());
        items.Add(MenuItemViewModel.ForCommand(Strings.Get("Menu_ClearBookmarks"), new RelayCommand(() => _bookmarks!.ClearBookmarks())));
    }

    private List<MenuItemViewModel> BuildToolbarsSubmenu()
    {
        List<MenuItemViewModel> items = new();
        foreach (ToolbarId toolbar in ToolbarManager.AllToolbars)
        {
            ToolbarId captured = toolbar;
            MenuItemViewModel item = MenuItemViewModel.ForToggle(
                ToolbarManager.DisplayName(toolbar),
                _toolbars.IsVisible(toolbar),
                new RelayCommand(() => _toolbars.ToggleVisible(captured)));
            items.Add(Localized(item, () => ToolbarManager.DisplayName(captured)));
        }

        _toolbars.Changed += (_, changed) =>
        {
            int index = Array.IndexOf(ToolbarManager.AllToolbars.ToArray(), changed);
            if (index >= 0 && index < items.Count)
            {
                items[index].IsChecked = _toolbars.IsVisible(changed);
            }
        };

        items.Add(MenuItemViewModel.Separator());
        static string Customize() => AppAction.ConvertMnemonics(Strings.Get("Menu_CustomizeToolbars"));
        items.Add(Localized(MenuItemViewModel.ForCommand(Customize(), _customizeToolbarsCommand), Customize));
        return items;
    }
}
