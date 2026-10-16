using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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

    /// <summary>Initializes the view.</summary>
    public BookBrowserView()
    {
        InitializeComponent();
        _shortcutBindings = new ShortcutKeyBindings(this);
        DataContextChanged += OnDataContextChanged;
        Tree.DoubleTapped += OnDoubleTapped;
        Tree.AddHandler(ContextRequestedEvent, OnTreeContextRequested, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        // Tunnel: in the bubble phase Enter is already consumed before it reaches the tree.
        Tree.AddHandler(KeyDownEvent, OnRenameEditorKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnRenameEditorLostFocus, RoutingStrategies.Bubble);
        AttachedToVisualTree += (_, _) =>
        {
            AppTheme.Changed += OnAppThemeChanged;
            ApplyAppearance();
        };
        DetachedFromVisualTree += (_, _) => AppTheme.Changed -= OnAppThemeChanged;
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
        AddFont(cells, look.ItemsFontFamily, look.ItemsFontSize);

        // Not a descendant of a cell: the headers keep the interface font unless they have a font of their own.
        Style headers = new(x => x.OfType<TreeDataGridColumnHeader>());
        AddFont(headers, look.HeaderFontFamily, look.HeaderFontSize);

        Style rows = new(x => x.OfType<TreeDataGridRow>());
        rows.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, look.RowSpacing)));

        Style indent = new(x => x.OfType<TreeDataGridExpanderCell>());
        indent.Setters.Add(new Setter(TreeDataGridExpanderCell.IndentSizeProperty, (double)look.Indent));

        _appearanceStyles = new[] { cells, headers, rows, indent };
        foreach (Style style in _appearanceStyles)
        {
            Tree.Styles.Add(style);
        }
    }

    private static void AddFont(Style style, string family, int size)
    {
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
        if (_boundViewModel is not null)
        {
            _boundViewModel.AppearanceChanged -= OnAppearanceChanged;
            _boundViewModel.RenameRequested -= OnRenameRequested;
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.DeleteRequested -= OnDeleteRequested;
            _boundViewModel.MergeErrorRequested -= OnMergeErrorRequested;
            _boundViewModel.AddExistingFilesRequested -= OnAddExistingFilesRequested;
            _boundViewModel.AddFontFilesRequested -= OnAddFontFilesRequested;
            _boundViewModel.RenameWithTemplateRequested -= OnRenameWithTemplateRequested;
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

        if (_boundViewModel is not null)
        {
            _boundViewModel.AppearanceChanged += OnAppearanceChanged;
            _boundViewModel.RenameRequested += OnRenameRequested;
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _boundViewModel.DeleteRequested += OnDeleteRequested;
            _boundViewModel.MergeErrorRequested += OnMergeErrorRequested;
            _boundViewModel.AddExistingFilesRequested += OnAddExistingFilesRequested;
            _boundViewModel.AddFontFilesRequested += OnAddFontFilesRequested;
            _boundViewModel.RenameWithTemplateRequested += OnRenameWithTemplateRequested;
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
        if (_boundViewModel is null)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<TreeDataGridColumnHeader>(includeSelf: true) is not { } header)
        {
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
        // A double click in the name editor selects a word — it does not open the file.
        if (IsInRenameEditor(e.Source))
        {
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

    private void OnRenameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsInRenameEditor(e.Source) || _boundViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _boundViewModel.CommitInPlaceRename();
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

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = Strings.Get("BookBrowser_AddExistingFilesTitle"), AllowMultiple = true });

        List<string> paths = files
            .Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        // Fonts among the added files are offered @font-face rules (skipping leaves them without rules).
        List<FontResource> fonts = _boundViewModel.ApplyAddExistingFiles(paths).OfType<FontResource>().ToList();
        if (fonts.Count > 0 && this.FindAncestorOfType<Window>() is { } owner)
        {
            await FontDialogs.OfferFontFacesAsync(owner, _boundViewModel, fonts);
        }
    }

    private async void OnRenameWithTemplateRequested(object? sender, IReadOnlyList<OpfModelEntry> entries)
    {
        Window? owner = this.FindAncestorOfType<Window>();
        if (owner is null || entries.Count == 0 || _boundViewModel is null)
        {
            return;
        }

        string initial = _boundViewModel.GetInitialRenameTemplate();
        string? result = await TextPromptWindow.AskAsync(
            owner,
            Strings.Get("BookBrowser_RenameWithTemplateTitle"),
            Strings.Format("BookBrowser_RenameWithTemplatePrompt", entries.Count),
            initial);

        if (!string.IsNullOrWhiteSpace(result))
        {
            _boundViewModel.ApplyRenameWithTemplate(entries, result.Trim());
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
            _boundViewModel.ApplyRenameList(resources, result.Value.NewFilenames);
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
            _boundViewModel.ApplyMove(entries, folder);
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
            _boundViewModel.ApplySemanticCode(entry, code);
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
            _boundViewModel.ApplyLinkStylesheets(resources, result.BookPaths);
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
            _boundViewModel.ApplyLinkJavascripts(resources, result.BookPaths);
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

    private void OnTreeSelectionRequested(object? sender, IReadOnlyList<BookBrowserNode> nodes) => SelectNodes(nodes);

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
