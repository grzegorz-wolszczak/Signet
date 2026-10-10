using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Signet.Core;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the modal "Edit Table Of Contents" dialog. An editable tree of entries:
/// add / delete / rename, move (up / down / indent left / right — preserving the contiguous
/// ranges of a multi-selection), target selection through the "Select Target" dialog. Saving
/// writes the <c>nav[epub:type=toc]</c> section (EPUB 3) or the NCX <c>navMap</c> (EPUB 2).
/// </summary>
public sealed class EditTocViewModel : ViewModelBase
{
    private readonly Book _book;
    private readonly string _baseBookPath;
    private readonly string _baseFolder;
    private readonly string? _navBookPath;
    private readonly EditTocNodeViewModel _root = new();

    private IReadOnlyList<EditTocNodeViewModel> _selectedNodes = Array.Empty<EditTocNodeViewModel>();
    private string _message = string.Empty;

    /// <summary>Creates the view model for a publication and builds the tree from its current table of contents.</summary>
    public EditTocViewModel(Book book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));

        HtmlResource? nav = book.IsEpub3 ? book.GetNavResource() : null;
        _navBookPath = nav?.BookPath;
        if (nav is not null)
        {
            _baseBookPath = nav.BookPath;
        }
        else if (book.GetNcx() is { } ncx)
        {
            _baseBookPath = ncx.BookPath;
        }
        else
        {
            _baseBookPath = string.Empty;
        }

        _baseFolder = _baseBookPath.Length > 0 ? BookPath.StartingDir(_baseBookPath) : string.Empty;

        AddAboveCommand = new RelayCommand(() => AddEntry(above: true));
        AddBelowCommand = new RelayCommand(() => AddEntry(above: false));
        DeleteCommand = new RelayCommand(DeleteEntry);
        MoveUpCommand = new RelayCommand(MoveUp);
        MoveDownCommand = new RelayCommand(MoveDown);
        MoveLeftCommand = new RelayCommand(MoveLeft);
        MoveRightCommand = new RelayCommand(MoveRight);
        SortByBookOrderCommand = new RelayCommand(SortByBookOrder);
        SelectTargetCommand = new RelayCommand(() => SelectTargetRequested?.Invoke(this, EventArgs.Empty));
        RenameCommand = new RelayCommand(() => RenameRequested?.Invoke(this, EventArgs.Empty));
        AcceptCommand = new RelayCommand(Accept);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        Build();
    }

    /// <summary>Raised when the dialog should close (after "OK" or "Cancel").</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the view should show the "Select Target" dialog for the selected entry.</summary>
    public event EventHandler? SelectTargetRequested;

    /// <summary>Raised when the view should start editing the selected entry's name (F2 / the "Rename" menu).</summary>
    public event EventHandler? RenameRequested;

    /// <summary>Raised after a move operation — the view should set the selection to the given nodes.</summary>
    public event EventHandler<IReadOnlyList<EditTocNodeViewModel>>? SelectionChangeRequested;

    /// <summary>Top-level nodes of the tree (children of the artificial root).</summary>
    public ObservableCollection<EditTocNodeViewModel> Nodes => _root.Children;

    /// <summary>
    /// All entries as a flat list in reading order (depth-first) — the rows of the table; the level of each one is
    /// <see cref="EditTocNodeViewModel.Level"/>. Rebuilt after every change of the structure.
    /// </summary>
    public ObservableCollection<EditTocNodeViewModel> Rows { get; } = new();

    /// <summary>
    /// Sorts the entries on every level by their place in the book: the spine position of the target file, then the
    /// position of the <c>#fragment</c> anchor in it. Entries without a resolvable target keep their place after the
    /// sorted ones (the sort is stable). "Cancel" discards it like any other change.
    /// </summary>
    public RelayCommand SortByBookOrderCommand { get; }

    /// <summary>"Add Above" — insert an empty entry above the selected one.</summary>
    public RelayCommand AddAboveCommand { get; }

    /// <summary>"Add Below" — insert an empty entry below the selected one.</summary>
    public RelayCommand AddBelowCommand { get; }

    /// <summary>"Delete" — delete the selected entry.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>"Move Up" — move the selected entries up by one within their parent.</summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>"Move Down" — move the selected entries down by one within their parent.</summary>
    public RelayCommand MoveDownCommand { get; }

    /// <summary>"Move Left" — decrease the level (to the grandparent).</summary>
    public RelayCommand MoveLeftCommand { get; }

    /// <summary>"Move Right" — increase the level (into the entry directly above).</summary>
    public RelayCommand MoveRightCommand { get; }

    /// <summary>"Select Target" — open the target picker dialog.</summary>
    public RelayCommand SelectTargetCommand { get; }

    /// <summary>"Rename" — start editing the selected entry's name.</summary>
    public RelayCommand RenameCommand { get; }

    /// <summary>"OK" — validate, save to nav/NCX and close.</summary>
    public RelayCommand AcceptCommand { get; }

    /// <summary>"Cancel" — close without saving.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Whether the user accepted the dialog and saving succeeded.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Validation / status message below the tree.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>The single selected node (or <c>null</c> when 0 or &gt; 1 are selected).</summary>
    public EditTocNodeViewModel? SelectedNode => _selectedNodes.Count == 1 ? _selectedNodes[0] : null;

    /// <summary>Sets the set of selected nodes (from the view).</summary>
    public void SetSelectedNodes(IEnumerable<EditTocNodeViewModel> nodes)
    {
        _selectedNodes = nodes is null ? Array.Empty<EditTocNodeViewModel>() : new List<EditTocNodeViewModel>(nodes);
        OnPropertyChanged(nameof(SelectedNode));
    }

    // --- "Select Target" (the dialog is handled in the view) ---

    /// <summary>
    /// Link targets ((X)HTML files + their identifiers, media files) as <c>href</c>s relative
    /// to the base resource (nav / NCX). Flattened, and skipping the nav document itself.
    /// </summary>
    public IReadOnlyList<HyperlinkTargetItem> GetTargetItems()
    {
        List<HyperlinkTargetItem> items = new();

        foreach (HtmlResource html in _book.GetHtmlResources())
        {
            if (_navBookPath is { } navPath && string.Equals(html.BookPath, navPath, StringComparison.Ordinal))
            {
                continue;
            }

            string relative = Utility.UrlEncodePath(BookPath.Relative(_baseBookPath, html.BookPath));
            items.Add(new HyperlinkTargetItem(html.Filename, relative));
            foreach (string id in _book.GetIdsInHtmlFile(html))
            {
                items.Add(new HyperlinkTargetItem($"{html.Filename} # {id}", relative + "#" + id));
            }
        }

        foreach (Resource media in _book.GetMediaResources())
        {
            items.Add(new HyperlinkTargetItem(
                media.Filename, Utility.UrlEncodePath(BookPath.Relative(_baseBookPath, media.BookPath))));
        }

        return items;
    }

    /// <summary>The selected entry's current target converted to an <c>href</c> relative to the base resource.</summary>
    public string CurrentTargetHref()
    {
        if (SelectedNode is not { } node || node.Target.Length == 0)
        {
            return string.Empty;
        }

        if (node.Target.Contains(':', StringComparison.Ordinal))
        {
            return node.Target;
        }

        (string basePart, string fragment) = SplitFragment(node.Target);
        string relative = Utility.UrlEncodePath(BookPath.Relative(_baseBookPath, Utility.UrlDecodePath(basePart)));
        return fragment.Length > 0 ? relative + "#" + fragment : relative;
    }

    /// <summary>Sets the selected entry's target from an <c>href</c> relative to the base resource.</summary>
    public void ApplyTarget(string? href)
    {
        if (SelectedNode is not { } node || href is null)
        {
            return;
        }

        string trimmed = href.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            node.Target = trimmed;
            return;
        }

        (string basePart, string fragment) = SplitFragment(trimmed);
        string bookPath = basePart.Length == 0
            ? Utility.UrlEncodePath(_baseBookPath)
            : Utility.UrlEncodePath(BookPath.BuildBookPath(Utility.UrlDecodePath(basePart), _baseFolder));
        node.Target = fragment.Length > 0 ? bookPath + "#" + fragment : bookPath;
    }

    // --- building the tree ---

    private void Build()
    {
        _root.Children.Clear();
        TocEntry root = TocEditModel.GetRootTocEntry(_book);
        foreach (TocEntry child in root.Children)
        {
            _root.Children.Add(CreateNode(child, _root));
        }

        RefreshRows();
    }

    // Rebuilds Rows (depth-first order) and the levels; the collection is left alone when nothing changed.
    private void RefreshRows()
    {
        List<EditTocNodeViewModel> rows = new();
        void Add(IEnumerable<EditTocNodeViewModel> nodes, int level)
        {
            foreach (EditTocNodeViewModel node in nodes)
            {
                node.Level = level;
                rows.Add(node);
                Add(node.Children, level + 1);
            }
        }

        Add(_root.Children, 1);
        if (rows.SequenceEqual(Rows))
        {
            return;
        }

        Rows.Clear();
        foreach (EditTocNodeViewModel row in rows)
        {
            Rows.Add(row);
        }
    }

    private static EditTocNodeViewModel CreateNode(TocEntry entry, EditTocNodeViewModel parent)
    {
        EditTocNodeViewModel node = new() { Parent = parent };
        node.SetInitial(entry.Text, entry.Target);
        foreach (TocEntry child in entry.Children)
        {
            node.Children.Add(CreateNode(child, node));
        }

        return node;
    }

    // --- sorting ---

    private void SortByBookOrder()
    {
        Dictionary<string, int> spine = new(StringComparer.Ordinal);
        foreach (string path in _book.GetOpf().GetSpineOrderBookPaths())
        {
            spine.TryAdd(path, spine.Count);
        }

        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        (int File, int Anchor) Key(EditTocNodeViewModel node)
        {
            if (node.Target.Length == 0 || node.Target.Contains(':', StringComparison.Ordinal))
            {
                return (int.MaxValue, int.MaxValue);
            }

            (string basePart, string fragment) = SplitFragment(node.Target);
            string bookPath = Utility.UrlDecodePath(basePart);
            if (!spine.TryGetValue(bookPath, out int file))
            {
                return (int.MaxValue, int.MaxValue);
            }

            if (fragment.Length == 0)
            {
                return (file, -1);
            }

            if (!texts.TryGetValue(bookPath, out string? text))
            {
                text = _book.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is HtmlResource html ? html.GetText() : string.Empty;
                texts[bookPath] = text;
            }

            int anchor = LinkReference.FindAnchorOffset(text, Utility.UrlDecodePath(fragment));
            return (file, anchor < 0 ? int.MaxValue : anchor);
        }

        bool changed = false;
        void Sort(EditTocNodeViewModel parent)
        {
            List<EditTocNodeViewModel> sorted = parent.Children.OrderBy(Key).ToList();
            if (!sorted.SequenceEqual(parent.Children))
            {
                changed = true;
                for (int i = 0; i < sorted.Count; i++)
                {
                    parent.Children.Move(parent.Children.IndexOf(sorted[i]), i);
                }
            }

            foreach (EditTocNodeViewModel child in parent.Children)
            {
                Sort(child);
            }
        }

        Sort(_root);
        Message = changed ? string.Empty : Strings.Get("EditToc_AlreadyInBookOrder");
        if (changed)
        {
            Reselect(_selectedNodes.ToList());
        }
    }

    // --- adding / deleting ---

    private void AddEntry(bool above)
    {
        if (SelectedNode is not { } item)
        {
            return;
        }

        EditTocNodeViewModel parent = item.Parent ?? _root;
        int index = parent.Children.IndexOf(item) + (above ? 0 : 1);
        EditTocNodeViewModel entry = new() { Parent = parent };
        parent.Children.Insert(index, entry);
        RefreshRows();
        RequestSelection(new[] { entry });
    }

    private void DeleteEntry()
    {
        if (SelectedNode is not { } item)
        {
            return;
        }

        (item.Parent ?? _root).Children.Remove(item);

        if (_root.Children.Count == 0)
        {
            EditTocNodeViewModel placeholder = new() { Parent = _root };
            placeholder.SetInitial(Strings.Get("EditToc_PlaceholderEntry"), string.Empty);
            _root.Children.Add(placeholder);
        }

        RefreshRows();
    }

    // --- moving (preserving contiguous ranges) ---

    private void MoveLeft()
    {
        List<MoveRange> ranges = SortedRanges();
        if (ranges.Count == 0 || ranges.Exists(r => ReferenceEquals(r.Parent, _root)))
        {
            return;
        }

        List<EditTocNodeViewModel> moved = new();
        for (int i = ranges.Count - 1; i >= 0; i--)
        {
            MoveRange range = ranges[i];
            EditTocNodeViewModel parent = range.Parent;
            EditTocNodeViewModel grandparent = parent.Parent ?? _root;
            int rowToPut = grandparent.Children.IndexOf(parent) + 1;
            for (int r = range.StartRow; r <= range.EndRow; r++)
            {
                EditTocNodeViewModel child = parent.Children[range.StartRow];
                parent.Children.RemoveAt(range.StartRow);
                child.Parent = grandparent;
                grandparent.Children.Insert(rowToPut, child);
                moved.Add(child);
                rowToPut++;
            }
        }

        Reselect(moved);
    }

    private void MoveRight()
    {
        List<MoveRange> ranges = SortedRanges();
        if (ranges.Count == 0 || ranges.Exists(r => r.StartRow == 0))
        {
            return;
        }

        List<EditTocNodeViewModel> moved = new();
        for (int i = ranges.Count - 1; i >= 0; i--)
        {
            MoveRange range = ranges[i];
            EditTocNodeViewModel parent = range.Parent;
            EditTocNodeViewModel newParent = parent.Children[range.StartRow - 1];
            for (int r = range.StartRow; r <= range.EndRow; r++)
            {
                EditTocNodeViewModel child = parent.Children[range.StartRow];
                parent.Children.RemoveAt(range.StartRow);
                child.Parent = newParent;
                newParent.Children.Add(child);
                moved.Add(child);
            }
        }

        Reselect(moved);
    }

    private void MoveUp()
    {
        List<MoveRange> ranges = SortedRanges();
        if (ranges.Count == 0 || ranges.Exists(r => r.StartRow == 0))
        {
            return;
        }

        List<EditTocNodeViewModel> moved = new();
        foreach (MoveRange range in ranges)
        {
            EditTocNodeViewModel parent = range.Parent;
            for (int r = range.StartRow; r <= range.EndRow; r++)
            {
                EditTocNodeViewModel item = parent.Children[r];
                int row = parent.Children.IndexOf(item);
                if (row == 0)
                {
                    continue;
                }

                parent.Children.RemoveAt(row);
                parent.Children.Insert(row - 1, item);
                moved.Add(item);
            }
        }

        Reselect(moved);
    }

    private void MoveDown()
    {
        List<MoveRange> ranges = SortedRanges();
        if (ranges.Count == 0 || ranges.Exists(r => r.EndRow == r.Parent.Children.Count - 1))
        {
            return;
        }

        List<EditTocNodeViewModel> moved = new();
        foreach (MoveRange range in ranges)
        {
            EditTocNodeViewModel parent = range.Parent;
            for (int r = range.EndRow; r >= range.StartRow; r--)
            {
                EditTocNodeViewModel item = parent.Children[r];
                if (r == parent.Children.Count - 1)
                {
                    continue;
                }

                parent.Children.RemoveAt(r);
                parent.Children.Insert(r + 1, item);
                moved.Add(item);
            }
        }

        Reselect(moved);
    }

    private List<MoveRange> SortedRanges()
    {
        List<MoveRange> ranges = ContiguousRanges(_selectedNodes);
        ranges.Sort(CompareRanges);
        return ranges;
    }

    private static List<MoveRange> ContiguousRanges(IReadOnlyList<EditTocNodeViewModel> items)
    {
        Dictionary<EditTocNodeViewModel, List<int>> grouped = new();
        foreach (EditTocNodeViewModel item in items)
        {
            if (item.Parent is not { } parent)
            {
                continue;
            }

            int row = parent.Children.IndexOf(item);
            if (row < 0)
            {
                continue;
            }

            if (!grouped.TryGetValue(parent, out List<int>? rows))
            {
                rows = new List<int>();
                grouped[parent] = rows;
            }

            rows.Add(row);
        }

        List<MoveRange> ranges = new();
        foreach (KeyValuePair<EditTocNodeViewModel, List<int>> pair in grouped)
        {
            List<int> rows = pair.Value;
            rows.Sort();
            int start = rows[0];
            int prev = rows[0];
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i] != prev + 1)
                {
                    ranges.Add(new MoveRange(pair.Key, start, prev));
                    start = rows[i];
                }

                prev = rows[i];
            }

            ranges.Add(new MoveRange(pair.Key, start, prev));
        }

        return ranges;
    }

    private static int CompareRanges(MoveRange a, MoveRange b)
    {
        List<int> pa = PathTo(a.Parent);
        pa.Add(a.StartRow + 1);
        List<int> pb = PathTo(b.Parent);
        pb.Add(b.StartRow + 1);

        int n = Math.Min(pa.Count, pb.Count);
        for (int i = 0; i < n; i++)
        {
            int cmp = pa[i].CompareTo(pb[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return pa.Count.CompareTo(pb.Count);
    }

    private static List<int> PathTo(EditTocNodeViewModel node)
    {
        List<int> path = new();
        EditTocNodeViewModel? current = node;
        while (current?.Parent is { } parent)
        {
            path.Insert(0, parent.Children.IndexOf(current) + 1);
            current = parent;
        }

        return path;
    }

    private void Reselect(List<EditTocNodeViewModel> moved)
    {
        RefreshRows();
        RequestSelection(moved);
    }

    private void RequestSelection(IReadOnlyList<EditTocNodeViewModel> nodes)
    {
        _selectedNodes = nodes;
        OnPropertyChanged(nameof(SelectedNode));
        SelectionChangeRequested?.Invoke(this, nodes);
    }

    // --- saving ---

    private void Accept()
    {
        int invalid = CountInvalid(_root.Children);
        if (invalid > 0)
        {
            Message = invalid == 1
                ? Strings.Get("EditToc_OneInvalidEntry")
                : Strings.Format("EditToc_InvalidEntries", invalid);
            return;
        }

        TocEditModel.Save(_book, BuildRoot());
        Accepted = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static int CountInvalid(IEnumerable<EditTocNodeViewModel> nodes)
    {
        int count = 0;
        foreach (EditTocNodeViewModel node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text) || node.Target.Length == 0)
            {
                count++;
            }

            count += CountInvalid(node.Children);
        }

        return count;
    }

    private TocEntry BuildRoot()
    {
        TocEntry root = new() { IsRoot = true };
        foreach (EditTocNodeViewModel node in _root.Children)
        {
            root.Children.Add(BuildEntry(node));
        }

        return root;
    }

    private static TocEntry BuildEntry(EditTocNodeViewModel node)
    {
        TocEntry entry = new() { Text = node.Text, Target = node.Target };
        foreach (EditTocNodeViewModel child in node.Children)
        {
            entry.Children.Add(BuildEntry(child));
        }

        return entry;
    }

    private static (string Path, string Fragment) SplitFragment(string value)
    {
        int hash = value.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (value, string.Empty) : (value[..hash], value[(hash + 1)..]);
    }

    private sealed record MoveRange(EditTocNodeViewModel Parent, int StartRow, int EndRow);
}

/// <summary>Node of the editable table of contents tree in the "Edit Table Of Contents" dialog.</summary>
public sealed class EditTocNodeViewModel : ObservableObject
{
    private string _text = string.Empty;
    private string _target = string.Empty;
    private int _level = 1;

    /// <summary>Parent in the tree (an artificial root for top-level entries).</summary>
    public EditTocNodeViewModel? Parent { get; set; }

    /// <summary>Child nodes.</summary>
    public ObservableCollection<EditTocNodeViewModel> Children { get; } = new();

    /// <summary>Entry text (editable inline).</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty);
    }

    /// <summary>Entry target — URL-encoded book path (+ optional <c>#fragment</c>); set by "Select Target".</summary>
    public string Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(TargetDisplay));
            }
        }
    }

    /// <summary>Target label shown in the "Target" column.</summary>
    public string TargetDisplay => _target.Length > 0 ? _target : Strings.Get("EditToc_NoTarget");

    /// <summary>The level of the entry (1 = top level); the "Level" column and the indentation of the title.</summary>
    public int Level
    {
        get => _level;
        internal set
        {
            if (SetProperty(ref _level, value))
            {
                OnPropertyChanged(nameof(Indent));
            }
        }
    }

    /// <summary>The indentation of the title in the table — 16 px per level below the top one.</summary>
    public Thickness Indent => new((_level - 1) * 16, 0, 0, 0);

    /// <summary>Sets the initial values without notifications (while building the tree).</summary>
    internal void SetInitial(string text, string target)
    {
        _text = text ?? string.Empty;
        _target = target ?? string.Empty;
    }
}
