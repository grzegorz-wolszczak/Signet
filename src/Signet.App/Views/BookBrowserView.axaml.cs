using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Semantics;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// View of the Book Browser panel: a tree over <see cref="BookBrowserViewModel"/>.
/// Handles selection synchronization, opening on double click, the right mouse button
/// (selects the item under the cursor), in-place renaming and dialogs (Delete etc.).
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> built here (a TreeDataGrid source is bound to the UI thread): the Name
/// column and the optional columns "#" (reading order), Semantics and Properties, switched from the context menu of
/// the column headers and remembered in <see cref="BookBrowserViewModel.OptionalColumns"/>. Its multi-selection is
/// passed to <see cref="BookBrowserViewModel.UpdateSelection"/>.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's nodes it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class BookBrowserView : UserControl
{
    // The ids of the optional columns, in their order (the values of BookBrowserViewModel.OptionalColumns).
    private static readonly string[] OptionalColumnIds = { "ReadingOrder", "Semantics", "Properties" };

    private BookBrowserViewModel? _boundViewModel;

    // The menu of the files, which the sample Book Browser of the Preferences window does not have.
    private readonly ContextMenu? _treeMenu;

    // The panel-scoped shortcuts (Rename, Merge, Delete…): bound on THIS view, so they fire only while the panel has
    // the focus — e.g. F2 in Code Editor does not rename a file.
    private readonly ShortcutKeyBindings _shortcutBindings;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<BookBrowserNode>? _columns;
    private HierarchicalTreeDataGridSource<BookBrowserNode>? _source;
    private TreeMultiSelectionSync<BookBrowserNode>? _selection;
    private Dictionary<string, IColumn<BookBrowserNode>> _optionalColumns = new();
    private IReadOnlyList<BookBrowserNode> _selectedNodes = Array.Empty<BookBrowserNode>();
    private BookBrowserNode? _editingNode;
    private IPointer? _textReorderPointer;
    private TreeDataGridRow? _textReorderSourceRow;
    private BookBrowserNode? _textReorderCandidate;
    private BookBrowserNode? _textReorderFolder;
    private Dictionary<BookBrowserNode, int>? _textReorderIndexes;
    private int _textReorderSourceIndex = -1;
    private Point _textReorderStart;
    private bool _isTextReorderDragging;

    private const double TextReorderDragThreshold = 4;

    /// <summary>Initializes the view.</summary>
    public BookBrowserView()
    {
        InitializeComponent();
        _treeMenu = Tree.ContextMenu;
        _shortcutBindings = new ShortcutKeyBindings(this);
        DataContextChanged += OnDataContextChanged;
        Tree.DoubleTapped += OnDoubleTapped;
        Tree.AddHandler(ContextRequestedEvent, OnTreeContextRequested, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerPressedEvent, OnTextReorderPointerPressed, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerMovedEvent, OnTextReorderPointerMoved, RoutingStrategies.Bubble);
        Tree.AddHandler(PointerReleasedEvent, OnTextReorderPointerReleased, RoutingStrategies.Bubble);
        Tree.PointerCaptureLost += OnTextReorderPointerCaptureLost;
        // Tunnel: in the bubble phase Enter is already consumed before it reaches the tree.
        Tree.AddHandler(KeyDownEvent, OnRenameEditorKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnRenameEditorLostFocus, RoutingStrategies.Bubble);
        AttachedToVisualTree += (_, _) =>
        {
            AppTheme.Changed += OnAppThemeChanged;
            ApplyAppearance();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            AppTheme.Changed -= OnAppThemeChanged;
            ResetTextReorderDrag(releasePointer: true);
        };
    }

    private void OnAppThemeChanged(object? sender, EventArgs e) => ApplyAppearance();

    // The styles of the tree that follow the settings of its look (Preferences › Appearance › Book Browser).
    private Style[] _appearanceStyles = Array.Empty<Style>();

    /// <summary>
    /// Applies the look of the tree: the font of the names and of the cells of the other columns, the font of the
    /// column headers, the indentation of a level and the space between the rows. A font left to the interface keeps
    /// the interface font (nothing is set). The styles of the previous look are replaced.
    /// </summary>
    private void ApplyAppearance()
    {
        foreach (Style old in _appearanceStyles)
        {
            Tree.Styles.Remove(old);
        }

        if (_boundViewModel is null)
        {
            _appearanceStyles = Array.Empty<Style>();
            return;
        }

        BookBrowserAppearance look = _boundViewModel.Appearance;

        // Every cell: the names (the template of the Name column) and the text of the other columns.
        Style cells = new(x => x.Is<TreeDataGridCell>());
        AddFont(cells, look.ItemsFontFamily, look.ItemsFontSize, look.ItemsFontWeight);
        cells.Setters.Add(new Setter(MinHeightProperty, (double)look.CellMinHeight));

        // The vertical padding of a cell: the text cells (left and right stay 4 px) and the name cell (its padding is
        // only the vertical part, the indentation is added by the cell).
        Style textCells = new(x => x.OfType<TreeDataGridTextCell>());
        textCells.Setters.Add(new Setter(PaddingProperty, new Thickness(4, look.CellPadding)));
        Style nameCells = new(x => x.OfType<TreeDataGridExpanderCell>());
        nameCells.Setters.Add(new Setter(PaddingProperty, new Thickness(0, look.CellPadding)));

        // Not a descendant of a cell: the headers keep the interface font unless they have a font of their own.
        Style headers = new(x => x.OfType<TreeDataGridColumnHeader>());
        AddFont(headers, look.HeaderFontFamily, look.HeaderFontSize, look.HeaderFontWeight);

        Style indent = new(x => x.OfType<TreeDataGridExpanderCell>());
        indent.Setters.Add(new Setter(TreeDataGridExpanderCell.IndentSizeProperty, (double)look.Indent));

        // The colors of the selected row come from the theme, for this tree only: the brush the selected rows take is a
        // resource of the tree (not of the application), the text color a style of the selected cells.
        const string selectionBrush = "TreeDataGridSelectedCellBackgroundBrush";
        ResolvedTheme theme = AppTheme.Current;
        if (theme.Background(ThemeKeys.UiBookBrowserSelection) is { } selectionColor)
        {
            Tree.Resources[selectionBrush] = new SolidColorBrush(selectionColor);
        }
        else
        {
            Tree.Resources.Remove(selectionBrush);
        }

        Style selected = new(x => x.Is<TreeDataGridCell>().Class(":selected"));
        if (theme.Foreground(ThemeKeys.UiBookBrowserSelection) is { } selectionText)
        {
            selected.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(selectionText)));
        }

        _appearanceStyles = new[] { cells, textCells, nameCells, headers, indent, selected };
        foreach (Style style in _appearanceStyles)
        {
            Tree.Styles.Add(style);
        }
    }

    private static void AddFont(Style style, string family, int size, int weight)
    {
        // Always set (Regular when not set): a style taken away does not give the cells of the grid their weight back, a
        // style added later wins over it.
        style.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeightOptions.ToAvalonia(weight)));

        if (family.Length > 0)
        {
            style.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, new FontFamily(family)));
        }

        if (size > 0)
        {
            style.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, (double)size));
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        ResetTextReorderDrag(releasePointer: true);
        if (_boundViewModel is not null)
        {
            _boundViewModel.AppearanceChanged -= OnAppearanceChanged;
            _boundViewModel.RenameRequested -= OnRenameRequested;
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.DeleteRequested -= OnDeleteRequested;
            _boundViewModel.MergeErrorRequested -= OnMergeErrorRequested;
            _boundViewModel.AddExistingFilesRequested -= OnAddExistingFilesRequested;
            _boundViewModel.AddFontFilesRequested -= OnAddFontFilesRequested;
            _boundViewModel.MassRenameRequested -= OnMassRenameRequested;
            _boundViewModel.BulkRegexRenameRequested -= OnBulkRegexRenameRequested;
            _boundViewModel.MoveRequested -= OnMoveRequested;
            _boundViewModel.AddSemanticsRequested -= OnAddSemanticsRequested;
            _boundViewModel.LinkStylesheetsRequested -= OnLinkStylesheetsRequested;
            _boundViewModel.LinkJavascriptsRequested -= OnLinkJavascriptsRequested;
            _boundViewModel.GetInfoRequested -= OnGetInfoRequested;
            _boundViewModel.SaveAsRequested -= OnSaveAsRequested;
            _boundViewModel.TreeSelectionRequested -= OnTreeSelectionRequested;
            DetachShortcutActions();
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _selectedNodes = Array.Empty<BookBrowserNode>();

        _boundViewModel = DataContext as BookBrowserViewModel;
        Tree.ContextMenu = _boundViewModel is { IsSample: true } ? null : _treeMenu;

        if (_boundViewModel is not null)
        {
            _boundViewModel.AppearanceChanged += OnAppearanceChanged;
            _boundViewModel.RenameRequested += OnRenameRequested;
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _boundViewModel.DeleteRequested += OnDeleteRequested;
            _boundViewModel.MergeErrorRequested += OnMergeErrorRequested;
            _boundViewModel.AddExistingFilesRequested += OnAddExistingFilesRequested;
            _boundViewModel.AddFontFilesRequested += OnAddFontFilesRequested;
            _boundViewModel.MassRenameRequested += OnMassRenameRequested;
            _boundViewModel.BulkRegexRenameRequested += OnBulkRegexRenameRequested;
            _boundViewModel.MoveRequested += OnMoveRequested;
            _boundViewModel.AddSemanticsRequested += OnAddSemanticsRequested;
            _boundViewModel.LinkStylesheetsRequested += OnLinkStylesheetsRequested;
            _boundViewModel.LinkJavascriptsRequested += OnLinkJavascriptsRequested;
            _boundViewModel.GetInfoRequested += OnGetInfoRequested;
            _boundViewModel.SaveAsRequested += OnSaveAsRequested;
            _boundViewModel.TreeSelectionRequested += OnTreeSelectionRequested;
            AttachShortcutActions();
            BuildTree(_boundViewModel);
        }

        Tree.Source = _source;
        ApplyAppearance();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearance();

    private void BuildTree(BookBrowserViewModel vm)
    {
        _columns = new LocalizedColumns<BookBrowserNode>();
        _source = new HierarchicalTreeDataGridSource<BookBrowserNode>(vm.Nodes)
        {
            Columns =
            {
                _columns.Expander(
                    _columns.Template("ReportsWindow_Name", "NameCellTemplate", new GridLength(1, GridUnitType.Star)),
                    n => n.Children,
                    n => n.Children.Count > 0,
                    n => n.IsExpanded),
            },
        };
        _optionalColumns = new Dictionary<string, IColumn<BookBrowserNode>>(StringComparer.Ordinal)
        {
            ["ReadingOrder"] = _columns.Text("BookBrowserView_ColumnOrderHeader", n => n.ReadingOrderNumber, new GridLength(40)),
            ["Semantics"] = _columns.Text("BookBrowserView_ColumnSemantics", n => n.SemanticsText, new GridLength(110)),
            ["Properties"] = _columns.Text("BookBrowserView_ColumnProperties", n => n.PropertiesText, new GridLength(110)),
        };
        ApplyOptionalColumns();
        _selection = new TreeMultiSelectionSync<BookBrowserNode>(
            _source,
            vm.Nodes,
            n => n.Children,
            () => _selectedNodes,
            nodes =>
            {
                _selectedNodes = nodes;
                vm.UpdateSelection(nodes);
            });
    }

    // The ids of the optional columns switched on.
    private HashSet<string> ShownOptionalColumns() =>
        (_boundViewModel?.OptionalColumns ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    // Puts exactly the switched-on optional columns after the Name column, in their fixed order.
    private void ApplyOptionalColumns()
    {
        if (_source is null)
        {
            return;
        }

        HashSet<string> shown = ShownOptionalColumns();
        foreach (IColumn<BookBrowserNode> column in _optionalColumns.Values)
        {
            _source.Columns.Remove(column);
        }

        int index = 1;
        foreach (string id in OptionalColumnIds.Where(shown.Contains))
        {
            _source.Columns.Insert(index++, _optionalColumns[id]);
        }
    }

    // A right click on a column header opens the columns menu instead of the files menu; on the Fonts folder or on
    // font files the fonts menu opens (only the actions that make sense for fonts).
    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_boundViewModel is null or { IsSample: true })
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<TreeDataGridColumnHeader>(includeSelf: true) is not { } header)
        {
            _boundViewModel.RefreshAddItems();
            if (_boundViewModel.IsFontMenu && Resources["FontContextMenu"] is ContextMenu fontMenu)
            {
                e.Handled = true;
                fontMenu.DataContext = _boundViewModel;
                fontMenu.Open(Tree);
            }

            return;
        }

        e.Handled = true;
        HashSet<string> shown = ShownOptionalColumns();
        ContextMenu menu = new();
        foreach (string id in OptionalColumnIds)
        {
            MenuItem item = new()
            {
                Header = Strings.Get("BookBrowserView_Column" + id),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = shown.Contains(id),
            };
            item.Click += (_, _) => ToggleOptionalColumn(id);
            menu.Items.Add(item);
        }

        menu.Open(header);
    }

    /// <summary>Shows or hides an optional column ("ReadingOrder", "Semantics", "Properties") and remembers it.</summary>
    public void ToggleOptionalColumn(string id)
    {
        if (_boundViewModel is null || !OptionalColumnIds.Contains(id, StringComparer.Ordinal))
        {
            return;
        }

        HashSet<string> shown = ShownOptionalColumns();
        if (!shown.Remove(id))
        {
            shown.Add(id);
        }

        _boundViewModel.OptionalColumns = string.Join(",", OptionalColumnIds.Where(shown.Contains));
        ApplyOptionalColumns();
    }

    // --- context menu shortcuts (only while the panel has focus) ---------------- //

    // KeyBindings are set up on THIS view, not on the window: the key event reaches here
    // only when the focus is in the panel, so e.g. F2 in Code Editor will not open the file rename.
    // The remaining actions are window-wide shortcuts.
    private void AttachShortcutActions()
    {
        if (_boundViewModel is not null)
        {
            _shortcutBindings.Attach(_boundViewModel.ShortcutActions);
        }
    }

    private void DetachShortcutActions() => _shortcutBindings.Detach();

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_boundViewModel is { IsSample: true })
        {
            return;
        }

        // A double click in the name editor selects a word — it does not open the file.
        if (IsInRenameEditor(e.Source))
        {
            return;
        }

        // A double click on a folder expands or collapses it.
        if ((e.Source as Visual)?.FindAncestorOfType<TreeDataGridRow>(includeSelf: true) is { Model: BookBrowserNode { IsFolder: true } } row
            && Tree.Rows is { } rows && row.RowIndex >= 0 && row.RowIndex < rows.Count
            && rows[row.RowIndex] is IExpander { ShowExpander: true } expander)
        {
            expander.IsExpanded = !expander.IsExpanded;
            return;
        }

        if (_boundViewModel?.OpenSelectedCommand.CanExecute(null) == true)
        {
            _boundViewModel.OpenSelectedCommand.Execute(null);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Tree).Properties.IsRightButtonPressed)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<TreeDataGridRow>(includeSelf: true)?.Model is not BookBrowserNode node)
        {
            return;
        }

        if (!_selectedNodes.Contains(node))
        {
            SelectNodes(new[] { node });
        }
    }

    private void OnTextReorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ResetTextReorderDrag(releasePointer: true);
        TreeDataGridRow? row = (e.Source as Visual)?.FindAncestorOfType<TreeDataGridRow>(includeSelf: true);
        if (_boundViewModel is not { HasBook: true, IsSample: false } vm
            || IsInRenameEditor(e.Source)
            || row?.Model is not BookBrowserNode node
            || node.Entry is not { ResourceType: ResourceType.Html }
            || node.IsEditing)
        {
            return;
        }

        BookBrowserNode? folder = vm.Nodes.FirstOrDefault(n => n.FolderKind == OpfModelGroupKind.Text);
        int sourceIndex = folder?.Children.IndexOf(node) ?? -1;
        if (folder is null || sourceIndex < 0)
        {
            return;
        }

        _textReorderPointer = e.Pointer;
        _textReorderSourceRow = row;
        _textReorderCandidate = node;
        _textReorderFolder = folder;
        _textReorderSourceIndex = sourceIndex;
        _textReorderStart = e.GetPosition(Tree);
    }

    private void OnTextReorderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_textReorderPointer, e.Pointer) || _textReorderCandidate is not { } candidate)
        {
            return;
        }

        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed)
        {
            ResetTextReorderDrag(releasePointer: true);
            return;
        }

        Point position = e.GetPosition(Tree);
        Vector delta = position - _textReorderStart;
        if (!_isTextReorderDragging)
        {
            if (Math.Abs(delta.X) < TextReorderDragThreshold && Math.Abs(delta.Y) < TextReorderDragThreshold)
            {
                return;
            }

            if (_boundViewModel is not { HasBook: true, IsSample: false } vm
                || _selectedNodes.Count != 1
                || !ReferenceEquals(_selectedNodes[0], candidate)
                || vm.SelectedResources is not { Count: 1 } selected
                || !ReferenceEquals(selected[0], candidate.Entry?.Resource)
                || _textReorderFolder is not { } folder
                || _textReorderSourceRow is not { } sourceRow)
            {
                e.Handled = true;
                ResetTextReorderDrag(releasePointer: false);
                return;
            }

            _textReorderIndexes = new Dictionary<BookBrowserNode, int>(folder.Children.Count);
            for (int i = 0; i < folder.Children.Count; i++)
            {
                _textReorderIndexes.Add(folder.Children[i], i);
            }

            _isTextReorderDragging = true;
            e.Pointer.Capture(sourceRow);
        }

        if (TryResolveTextReorderTarget(position, out TextReorderDropTarget target))
        {
            TextReorderIndicator.Margin = new Thickness(0, target.IndicatorY, 0, 0);
            TextReorderIndicator.IsVisible = true;
        }
        else
        {
            TextReorderIndicator.IsVisible = false;
        }

        e.Handled = true;
    }

    private bool TryResolveTextReorderTarget(Point pointerPosition, out TextReorderDropTarget target)
    {
        target = default;
        if (_textReorderFolder is not { } folder
            || _textReorderIndexes is not { } indexes
            || _textReorderSourceIndex < 0
            || _textReorderSourceIndex >= folder.Children.Count
            || !ReferenceEquals(folder.Children[_textReorderSourceIndex], _textReorderCandidate)
            || Tree.RowsPresenter is not { } rowsPresenter
            || rowsPresenter.TranslatePoint(default, Tree) is not { } rowsOrigin)
        {
            return false;
        }

        Rect rowsBounds = new(rowsOrigin, rowsPresenter.Bounds.Size);
        if (!rowsBounds.Contains(pointerPosition) || pointerPosition.X < 0 || pointerPosition.X > Tree.Bounds.Width)
        {
            return false;
        }

        List<TextReorderRowBounds> visibleRows = new();
        foreach (TreeDataGridRow row in Tree.GetVisualDescendants().OfType<TreeDataGridRow>())
        {
            if (!row.IsEffectivelyVisible
                || row.Model is not BookBrowserNode node
                || !indexes.TryGetValue(node, out int index)
                || row.Bounds.Height <= 0
                || row.TranslatePoint(default, Tree) is not { } rowOrigin)
            {
                continue;
            }

            double bottom = rowOrigin.Y + row.Bounds.Height;
            if (bottom <= rowsBounds.Top || rowOrigin.Y >= rowsBounds.Bottom)
            {
                continue;
            }

            visibleRows.Add(new TextReorderRowBounds(index, rowOrigin.Y, bottom));
        }

        if (visibleRows.Count == 0)
        {
            return false;
        }

        visibleRows.Sort(static (left, right) => left.Top.CompareTo(right.Top));
        if (pointerPosition.Y < visibleRows[0].Top || pointerPosition.Y > visibleRows[^1].Bottom)
        {
            return false;
        }

        int insertionIndex = -1;
        double indicatorY = 0;
        for (int i = 0; i < visibleRows.Count; i++)
        {
            TextReorderRowBounds row = visibleRows[i];
            if (pointerPosition.Y <= row.Center)
            {
                insertionIndex = row.Index;
                indicatorY = row.Top;
                break;
            }

            if (pointerPosition.Y <= row.Bottom)
            {
                insertionIndex = row.Index + 1;
                indicatorY = row.Bottom;
                break;
            }

            if (i + 1 < visibleRows.Count && pointerPosition.Y < visibleRows[i + 1].Top)
            {
                insertionIndex = row.Index + 1;
                indicatorY = row.Bottom;
                break;
            }
        }

        int targetIndex = insertionIndex > _textReorderSourceIndex ? insertionIndex - 1 : insertionIndex;
        if (targetIndex < 0 || targetIndex >= folder.Children.Count || targetIndex == _textReorderSourceIndex)
        {
            return false;
        }

        double visibleBottom = Math.Max(rowsBounds.Top, rowsBounds.Bottom - TextReorderIndicator.Height);
        target = new TextReorderDropTarget(
            targetIndex,
            Math.Clamp(indicatorY, rowsBounds.Top, visibleBottom));
        return true;
    }

    private async void OnTextReorderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_textReorderPointer, e.Pointer))
        {
            return;
        }

        bool wasDragging = _isTextReorderDragging;
        BookBrowserNode? node = _textReorderCandidate;
        TextReorderDropTarget? target = wasDragging && TryResolveTextReorderTarget(
            e.GetPosition(Tree), out TextReorderDropTarget resolvedTarget)
            ? resolvedTarget
            : null;
        BookBrowserViewModel? vm = _boundViewModel;
        ResetTextReorderDrag(releasePointer: true);

        if (!wasDragging)
        {
            return;
        }

        e.Handled = true;
        if (target is { } readyTarget && node is not null && vm is not null)
        {
            await vm.MoveTextToIndexAsync(node, readyTarget.Index);
        }
    }

    private void OnTextReorderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isTextReorderDragging
            && ReferenceEquals(_textReorderPointer, e.Pointer))
        {
            TextReorderIndicator.IsVisible = false;
        }
    }

    private void ResetTextReorderDrag(bool releasePointer)
    {
        IPointer? pointer = _textReorderPointer;
        bool wasDragging = _isTextReorderDragging;
        _textReorderPointer = null;
        _textReorderSourceRow = null;
        _textReorderCandidate = null;
        _textReorderFolder = null;
        _textReorderIndexes = null;
        _textReorderSourceIndex = -1;
        _isTextReorderDragging = false;
        TextReorderIndicator.IsVisible = false;
        TextReorderIndicator.Margin = default;

        if (releasePointer && wasDragging)
        {
            pointer?.Capture(null);
        }
    }

    private readonly record struct TextReorderDropTarget(int Index, double IndicatorY);

    private readonly record struct TextReorderRowBounds(int Index, double Top, double Bottom)
    {
        public double Center => (Top + Bottom) / 2;
    }

    // --- in-place rename ---- //

    private void OnRenameRequested(object? sender, OpfModelEntry entry) =>
        // The editor becomes visible only after a layout pass — the focus is set afterwards.
        Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Loaded);

    private void FocusRenameEditor()
    {
        TextBox? editor = Tree.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains(RenameEditorClass) && t.DataContext is BookBrowserNode { IsEditing: true });
        if (editor is null)
        {
            _boundViewModel?.CancelInPlaceRename();
            return;
        }

        editor.Focus();
        editor.SelectAll();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BookBrowserViewModel.EditingNode) || _boundViewModel is null)
        {
            return;
        }

        BookBrowserNode? finished = _editingNode;
        _editingNode = _boundViewModel.EditingNode;
        if (_boundViewModel.EditingNode is not null)
        {
            // Panel shortcuts (Del, Ctrl+M…) must not act on files while a name is being typed.
            _shortcutBindings.SetSuspended(true);
            return;
        }

        _shortcutBindings.SetSuspended(false);

        // The view model hides the editor (IsEditing = false) before this notification, so the focus it had is usually
        // already lost (null) here. A rename abandoned by clicking elsewhere leaves the focus on that element instead
        // (it is set before the editor's LostFocus) — that focus is not taken back.
        IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (finished is not null && (focused is null || IsInRenameEditor(focused)))
        {
            FocusRow(finished);

            // A committed rename rebuilds the tree right after this: the cell focused above is recycled and the focus
            // is lost, so the panel-scoped shortcuts (F2 to rename again) no longer reach this view. After the
            // rebuild's layout pass the focus goes to the row of the renamed file (selected again by the view model).
            Dispatcher.UIThread.Post(RestoreFocusAfterRename, DispatcherPriority.Loaded);
        }
    }

    private void RestoreFocusAfterRename()
    {
        // Only a focus lost with the recycled row is restored — not one the user has moved elsewhere meanwhile.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual { } focused
            && focused.IsAttachedToVisualTree()
            && focused.IsEffectivelyVisible)
        {
            return;
        }

        FocusRow(_selectedNodes.Count == 1 ? _selectedNodes[0] : null);
    }

    // Focuses the row of a node (in a TreeDataGrid the cells take the focus, not the rows or the grid), or the tree.
    private void FocusRow(BookBrowserNode? node)
    {
        if (node is not null && RowOf(node)?.TryGetCell(0) is { } cell)
        {
            cell.Focus();
        }
        else
        {
            Tree.Focus();
        }
    }

    private async void OnRenameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsInRenameEditor(e.Source) || _boundViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _boundViewModel.CommitInPlaceRenameAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _boundViewModel.CancelInPlaceRename();
                break;
            case Key.Up or Key.Down or Key.PageUp or Key.PageDown:
                // Without this the tree would move the selection under the editor.
                e.Handled = true;
                break;
        }
    }

    private void OnRenameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsInRenameEditor(e.Source))
        {
            _boundViewModel?.CancelInPlaceRename();
        }
    }

    private const string RenameEditorClass = "inPlaceRename";

    private static bool IsInRenameEditor(object? source) =>
        source is Visual visual &&
        (visual as TextBox ?? visual.FindAncestorOfType<TextBox>())?.Classes.Contains(RenameEditorClass) == true;

    // Deleting fonts used by the stylesheets shows the font removal window instead of the plain confirmation.
    private async void OnDeleteRequested(object? sender, IReadOnlyList<OpfModelEntry> entries)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || entries.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        await FontDialogs.DeleteAsync(owner, _boundViewModel, entries.Select(x => x.Resource).ToList());
    }

    private async void OnAddFontFilesRequested(object? sender, EventArgs e)
    {
        if (this.FindAncestorOfType<Window>() is { } owner && _boundViewModel is not null)
        {
            await FontDialogs.PickAndAddFontsAsync(owner, _boundViewModel);
        }
    }

    private async void OnMergeErrorRequested(object? sender, string message)
    {
        if (this.FindAncestorOfType<Window>() is { } owner)
        {
            await MessageDialog.ShowAsync(owner, Strings.Get("BookBrowser_MergeErrorTitle"), message);
        }
    }

    private async void OnAddExistingFilesRequested(object? sender, EventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        // The files the selected folder takes first (Misc: any file), the rest still reachable as "All files".
        IReadOnlyList<string> patterns = _boundViewModel?.AddExistingFilePatterns ?? Array.Empty<string>();
        FilePickerOpenOptions options = new() { Title = Strings.Get("BookBrowser_AddExistingFilesTitle"), AllowMultiple = true };
        if (patterns.Count > 0)
        {
            options.FileTypeFilter = new[]
            {
                new FilePickerFileType(_boundViewModel!.AddExistingFilesHeader.TrimEnd('\u2026', '.')) { Patterns = patterns },
                FilePickerFileTypes.All,
            };
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        List<string> paths = files
            .Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        // Fonts among the added files are offered @font-face rules (skipping leaves them without rules).
        List<FontResource> fonts = (await _boundViewModel.ApplyAddExistingFilesAsync(paths)).OfType<FontResource>().ToList();
        if (fonts.Count > 0 && this.FindAncestorOfType<Window>() is { } owner)
        {
            await FontDialogs.OfferFontFacesAsync(owner, _boundViewModel, fonts);
        }
    }

    private async void OnMassRenameRequested(object? sender, IReadOnlyList<OpfModelEntry> entries)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || entries.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        if (await MassRenameWindow.AskAsync(owner, _boundViewModel, entries) is { } options)
        {
            await _boundViewModel.ApplyMassRenameAsync(entries, options);
        }
    }

    private async void OnBulkRegexRenameRequested(object? sender, IReadOnlyList<OpfModelEntry> entries)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || entries.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        List<Resource> resources = entries.Select(e => e.Resource).ToList();
        (string Pattern, string Replacement, IReadOnlyList<string> NewFilenames)? result =
            await RegexRenameWindow.AskAsync(owner, resources);

        if (result is not null)
        {
            await _boundViewModel.ApplyRenameListAsync(resources, result.Value.NewFilenames);
        }
    }

    private async void OnMoveRequested(object? sender, IReadOnlyList<OpfModelEntry> entries)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || entries.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        IReadOnlyList<string> folders = _boundViewModel.GetFoldersForSelection(entries);
        string? folder = await SelectFolderWindow.AskAsync(owner, folders, entries[0].Resource.Folder);
        if (folder is not null)
        {
            await _boundViewModel.ApplyMoveAsync(entries, folder);
        }
    }

    private async void OnAddSemanticsRequested(object? sender, OpfModelEntry entry)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || _boundViewModel is null)
        {
            return;
        }

        (bool _, string currentCode, IReadOnlyDictionary<string, DescriptiveInfo> codeMap) =
            _boundViewModel.GetSemanticsInfo(entry);

        string? code = await AddSemanticsWindow.AskAsync(owner, currentCode, codeMap);
        if (!string.IsNullOrEmpty(code))
        {
            await _boundViewModel.ApplySemanticCodeAsync(entry, code);
        }
    }

    private async void OnLinkStylesheetsRequested(object? sender, IReadOnlyList<HtmlResource> resources)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || _boundViewModel is null || resources.Count == 0
            || !await _boundViewModel.ConfirmMissingDoctypeAsync(resources, Strings.Get("BookBrowserView_LinkStylesheetsTitle")))
        {
            return;
        }

        SelectFilesResult? result = await SelectFilesWindow.AskAsync(
            owner,
            Strings.Get("BookBrowserView_LinkStylesheetsTitle"),
            Strings.Get("BookBrowserView_LinkStylesheetsPrompt"),
            SelectFilesMode.MultipleOrdered,
            ToEntries(_boundViewModel.GetStylesheetsMap(resources)));

        if (result is not null)
        {
            await _boundViewModel.ApplyLinkStylesheetsAsync(resources, result.BookPaths);
        }
    }

    private async void OnLinkJavascriptsRequested(object? sender, IReadOnlyList<HtmlResource> resources)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || _boundViewModel is null || resources.Count == 0
            || !await _boundViewModel.ConfirmMissingDoctypeAsync(resources, Strings.Get("BookBrowserView_LinkJavascriptsTitle")))
        {
            return;
        }

        SelectFilesResult? result = await SelectFilesWindow.AskAsync(
            owner,
            Strings.Get("BookBrowserView_LinkJavascriptsTitle"),
            Strings.Get("BookBrowserView_LinkJavascriptsPrompt"),
            SelectFilesMode.MultipleOrdered,
            ToEntries(_boundViewModel.GetJavascriptsMap(resources)));

        if (result is not null)
        {
            await _boundViewModel.ApplyLinkJavascriptsAsync(resources, result.BookPaths);
        }
    }

    private static List<SelectFilesEntry> ToEntries(IReadOnlyList<LinkableResourceEntry> map) =>
        map.Select(e => new SelectFilesEntry(e.BookPath, e.BookPath, ResourceType.Css, null, e.Included)).ToList();

    private async void OnGetInfoRequested(object? sender, string infoText)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null)
        {
            return;
        }

        await MessageDialog.ShowAsync(owner, Strings.Get("BookBrowser_GetInfoTitle"), infoText);
    }

    private async void OnSaveAsRequested(object? sender, OpfModelEntry entry)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _boundViewModel is null)
        {
            return;
        }

        IStorageFile? destination = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("BookBrowser_SaveAsTitle"),
            SuggestedFileName = entry.Resource.Filename,
        });

        string? path = destination?.TryGetLocalPath() ?? destination?.Path.LocalPath;
        if (!string.IsNullOrEmpty(path))
        {
            _boundViewModel.ApplySaveAs(entry, path);
        }
    }

    private void OnTreeSelectionRequested(object? sender, IReadOnlyList<BookBrowserNode> nodes)
    {
        SelectNodes(nodes);

        if (nodes.Count > 0)
        {
            BookBrowserNode[] requested = nodes.ToArray();
            Dispatcher.UIThread.Post(
                () =>
                {
                    BookBrowserNode node = requested.FirstOrDefault(candidate => RowOf(candidate) is not null) ?? requested[0];
                    if (_selection?.FindPath(node) is { } path && Tree.Rows?.ModelIndexToRowIndex(new IndexPath(path)) is >= 0 and var index)
                    {
                        Tree.RowsPresenter?.BringIntoView(index);
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    // Selects exactly these nodes in the tree and passes them to the view model.
    private void SelectNodes(IReadOnlyList<BookBrowserNode> nodes)
    {
        _selectedNodes = nodes.ToArray();
        _selection?.SelectFromViewModel();
        _boundViewModel?.UpdateSelection(_selectedNodes);
    }

    // The realized row of a node, if it is shown.
    private TreeDataGridRow? RowOf(BookBrowserNode node) =>
        _selection?.FindPath(node) is { } path && Tree.Rows?.ModelIndexToRowIndex(new IndexPath(path)) is >= 0 and var index
            ? Tree.TryGetRow(index)
            : null;
}
