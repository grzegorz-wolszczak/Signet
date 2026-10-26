using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// "Saved Searches" panel view (Calibre-style) — a one-column (Name) tree over
/// <see cref="SearchEditorViewModel"/>, a read-only preview of the selected entry, and an Add/Edit view swapped
/// in over the list (<see cref="SearchEditorViewModel.IsEditing"/>). Synchronizes the selection, opens the
/// Add/Edit view on a double click on an entry (a group renames in place instead), confirms Delete, and handles
/// the import/export file pickers.
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> built here (a TreeDataGrid source is bound to the UI thread) over the
/// visible nodes (<see cref="VisibleItemsView{T}"/> — the filter sets <see cref="SearchEntryNodeViewModel.IsVisible"/>);
/// its multi-selection and the view model's selected nodes follow each other.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's nodes it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class SearchEditorView : UserControl
{
    private static FilePickerFileType XmlType => new(Strings.Get("SearchEditor_XmlFileType"))
    {
        Patterns = new[] { "*.xml" },
    };

    private static FilePickerFileType TsvType => new(Strings.Get("SearchEditor_TsvFileType"))
    {
        Patterns = new[] { "*.txt" },
    };

    private static readonly FilePickerFileType CsvType = new("CSV (*.csv)")
    {
        Patterns = new[] { "*.csv" },
    };

    private const string RenameEditorClass = "inPlaceRename";

    private SearchEditorViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<SearchEntryNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<SearchEntryNodeViewModel>? _source;
    private TreeMultiSelectionSync<SearchEntryNodeViewModel>? _selection;
    private SearchEditorAppearance _look = SearchEditorAppearance.Default;

    /// <summary>Initializes the view.</summary>
    public SearchEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Tree.DoubleTapped += OnDoubleTapped;
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnRenameEditorLostFocus, RoutingStrategies.Bubble);
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);

        // Esc discards the Add/Edit view, same as its own ✕ / Cancel button — Tunnel (not a KeyBinding on the
        // Add/Edit Grid) so it fires regardless of which control inside it has focus (the Name box, one of the
        // embedded Find & Replace panel's fields, …).
        AddHandler(KeyDownEvent, OnEditEscape, RoutingStrategies.Tunnel);

        // The row font (Preferences › Appearance › Saved Searches › Fonts) is set on each cell as it is realized
        // (TreeDataGridCell.FontFamily/Size/Weight are inherited, so they reach the Name template's TextBlocks/TextBox
        // without naming them); the header is a part of the grid's own template, made once the column is in view.
        Tree.CellPrepared += (_, e) => SetFont(e.Cell, _look.RowsFontFamily, _look.RowsFontSize, _look.RowsFontWeight);
        Tree.LayoutUpdated += (_, _) => ApplyHeaderFont();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ImportRequested -= OnImportRequested;
            _bound.ExportRequested -= OnExportRequested;
            _bound.DeleteConfirmRequested -= OnDeleteConfirmRequested;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
            _bound.AppearanceChanged -= OnAppearanceChanged;
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as SearchEditorViewModel;

        if (_bound is not null)
        {
            _bound.ImportRequested += OnImportRequested;
            _bound.ExportRequested += OnExportRequested;
            _bound.DeleteConfirmRequested += OnDeleteConfirmRequested;
            _bound.PropertyChanged += OnViewModelPropertyChanged;
            _bound.AppearanceChanged += OnAppearanceChanged;
            ApplyAppearance();
            SearchEditorViewModel vm = _bound;
            _columns = new LocalizedColumns<SearchEntryNodeViewModel>();
            VisibleItemsView<SearchEntryNodeViewModel> roots = Visible(vm.Nodes);
            _source = new HierarchicalTreeDataGridSource<SearchEntryNodeViewModel>(roots)
            {
                Columns =
                {
                    // hasChildrenSelector must reflect actual children, not just IsGroup: claiming an empty group
                    // "has children" makes the TreeDataGrid fork's HierarchicalRow.Expand() try to expand it, find
                    // nothing, and permanently hide its chevron as a one-time self-correction (ShowExpander = false).
                    // Groups still read apart from entries through the bold name (NameCellTemplate) instead.
                    _columns.Expander(
                        _columns.Template("ReportsWindow_Name", "NameCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => Visible(n.Children),
                        n => n.Children.Count > 0,
                        n => n.IsExpanded),
                },
            };
            _selection = new TreeMultiSelectionSync<SearchEntryNodeViewModel>(
                _source, roots, n => Visible(n.Children), () => vm.SelectedNodes, vm.SetSelectedNodes,
                n => Contains(vm.Nodes, n));
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    // The visible nodes of a level (the filter's result).
    private static VisibleItemsView<SearchEntryNodeViewModel> Visible(ObservableCollection<SearchEntryNodeViewModel> nodes) =>
        VisibleItems.For(nodes, n => n.IsVisible, nameof(SearchEntryNodeViewModel.IsVisible));

    // Whether a node is in the tree (also when the filter hides it).
    private static bool Contains(IEnumerable<SearchEntryNodeViewModel> nodes, SearchEntryNodeViewModel node) =>
        nodes.Any(n => ReferenceEquals(n, node) || Contains(n.Children, node));

    // A plain click (no modifiers) on the row that is already the sole selection clears the selection instead of
    // just reselecting it — the only way to deselect, since the tree has no empty area of its own to click into.
    // The expander chevron and the rename editor keep their own click handling.
    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // The Name column is also the expander column (one column holds both the chevron and the label), so only
        // the chevron's own ToggleButton is excluded here — not the whole cell, or no click on the label would ever
        // reach this handler.
        if (_bound is not { } vm
            || e.KeyModifiers != KeyModifiers.None
            || e.ClickCount != 1 // a double click must reach the normal handling (rename / Edit Search), not this
            || !e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed
            || e.Source is not Visual visual
            || visual.FindAncestorOfType<ToggleButton>() is not null
            || IsInRenameEditor(e.Source))
        {
            return;
        }

        if (visual.FindAncestorOfType<TreeDataGridRow>()?.Model is not SearchEntryNodeViewModel node
            || vm.SelectedNodes.Count != 1
            || !ReferenceEquals(vm.SelectedNodes[0], node))
        {
            return;
        }

        e.Handled = true;
        vm.SetSelectedNodes(Array.Empty<SearchEntryNodeViewModel>());
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchEditorViewModel.SelectedNode))
        {
            _selection?.SelectFromViewModel();
        }
        else if (e.PropertyName == nameof(SearchEditorViewModel.IsEditing) && _bound is { IsEditing: true })
        {
            // So the Name box is ready to type into right away, and so Esc (OnEditEscape, a Tunnel handler) has a
            // focused descendant of this view to tunnel through — nothing is focused yet right after Add/Edit
            // Search is reached programmatically (a command execution, not a real click moving focus).
            Dispatcher.UIThread.Post(() => EditNameBox.Focus(), DispatcherPriority.Loaded);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearance();

    /// <summary>
    /// Applies the fonts (Preferences › Appearance › Saved Searches › Fonts): the "Name" column header, the tree
    /// rows (already-realized cells; <see cref="SearchEditorView"/>'s constructor applies new ones as they are
    /// prepared), the Details preview and the Add/Edit view's Name field. The values are set on the controls
    /// themselves (cleared again for a font left to the interface): FontFamily/Size/Weight are inherited, so setting
    /// them on a cell or a Border reaches its template's / its children's text without naming each one.
    /// </summary>
    private void ApplyAppearance()
    {
        _look = _bound?.Appearance ?? SearchEditorAppearance.Default;
        foreach (TreeDataGridCell cell in Tree.GetVisualDescendants().OfType<TreeDataGridCell>())
        {
            SetFont(cell, _look.RowsFontFamily, _look.RowsFontSize, _look.RowsFontWeight);
        }

        ApplyHeaderFont();
        SetFont(DetailsPane, _look.RowsFontFamily, _look.RowsFontSize, _look.RowsFontWeight);
        SetFont(EditNamePane, _look.RowsFontFamily, _look.RowsFontSize, _look.RowsFontWeight);
    }

    private void ApplyHeaderFont()
    {
        if (Tree.ColumnHeadersPresenter is { } presenter)
        {
            foreach (TreeDataGridColumnHeader header in presenter.GetVisualChildren().OfType<TreeDataGridColumnHeader>())
            {
                SetFont(header, _look.HeaderFontFamily, _look.HeaderFontSize, _look.HeaderFontWeight);
            }
        }
    }

    // A font setting that is empty / 0 clears the value, so the control follows the interface font again.
    private static void SetFont(Control control, string family, int size, int weight)
    {
        if (family.Length > 0)
        {
            control.SetValue(TemplatedControl.FontFamilyProperty, new FontFamily(family));
        }
        else
        {
            control.ClearValue(TemplatedControl.FontFamilyProperty);
        }

        if (size > 0)
        {
            control.SetValue(TemplatedControl.FontSizeProperty, (double)size);
        }
        else
        {
            control.ClearValue(TemplatedControl.FontSizeProperty);
        }

        if (weight > 0)
        {
            control.SetValue(TemplatedControl.FontWeightProperty, FontWeightOptions.ToAvalonia(weight));
        }
        else
        {
            control.ClearValue(TemplatedControl.FontWeightProperty);
        }
    }

    // A single click only selects a row (the Name cell is a plain label, not a text box). A double click on a
    // group starts renaming it in place; a double click on an entry opens the Add/Edit view instead (Calibre's
    // "Edit search") — an entry's name is never typed directly in the tree.
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double click inside the rename editor selects a word — it must not restart the rename or reopen Edit Search.
        if (IsInRenameEditor(e.Source))
        {
            return;
        }

        if (_bound?.SelectedNode is not { } node)
        {
            return;
        }

        if (node.IsGroup)
        {
            node.BeginRename();
            Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Loaded);
        }
        else if (_bound.EditSearchCommand.CanExecute(null))
        {
            _bound.EditSearchCommand.Execute(null);
        }
    }

    private void FocusRenameEditor()
    {
        TextBox? editor = Tree.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains(RenameEditorClass) && t.DataContext is SearchEntryNodeViewModel { IsEditing: true });
        if (editor is null)
        {
            return;
        }

        editor.Focus();
        editor.SelectAll();
    }

    private void OnEditEscape(object? sender, KeyEventArgs e)
    {
        if (_bound is { IsEditing: true } vm && e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEditCommand.Execute(null);
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsInRenameEditor(e.Source) || e.Source is not Visual visual)
        {
            return;
        }

        SearchEntryNodeViewModel? node = (visual as TextBox ?? visual.FindAncestorOfType<TextBox>())?.DataContext as SearchEntryNodeViewModel;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                node?.CommitRename();
                break;
            case Key.Escape:
                e.Handled = true;
                node?.CancelRename();
                break;
            case Key.Up or Key.Down or Key.PageUp or Key.PageDown:
                // Without this the tree would move the selection under the editor.
                e.Handled = true;
                break;
        }
    }

    private void OnRenameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsInRenameEditor(e.Source)
            && (e.Source as TextBox ?? (e.Source as Visual)?.FindAncestorOfType<TextBox>())?.DataContext is SearchEntryNodeViewModel node)
        {
            node.CommitRename();
        }
    }

    private static bool IsInRenameEditor(object? source) =>
        source is Visual visual &&
        (visual as TextBox ?? visual.FindAncestorOfType<TextBox>())?.Classes.Contains(RenameEditorClass) == true;

    private async void OnDeleteConfirmRequested(object? sender, SearchDeleteCounts e)
    {
        if (_bound is null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var parts = new List<string>();
        if (e.GroupCount > 0)
        {
            parts.Add(Strings.Format("SearchEditorView_DeleteConfirmGroups", e.GroupCount));
        }

        if (e.EntryCount > 0)
        {
            parts.Add(Strings.Format("SearchEditorView_DeleteConfirmEntries", e.EntryCount));
        }

        string message = Strings.Format("SearchEditorView_DeleteConfirmMessage", string.Join(", ", parts));
        if (await ConfirmWindow.AskAsync(owner, Strings.Get("Common_Delete"), message, Strings.Get("Common_Delete"), destructive: true))
        {
            _bound.DeleteConfirmed();
        }
    }

    private async void OnImportRequested(object? sender, EventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _bound is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = Strings.Get("SearchEditor_ImportTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { XmlType, TsvType, CsvType },
            });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            _bound.ImportFile(path);
        }
    }

    private async void OnExportRequested(object? sender, bool all)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _bound is null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = all ? Strings.Get("SearchEditor_ExportAllTitle") : Strings.Get("SearchEditor_ExportTitle"),
                DefaultExtension = "xml",
                SuggestedFileName = "saved_searches.xml",
                FileTypeChoices = new[] { XmlType, TsvType, CsvType },
            });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            _bound.ExportFile(path, all);
        }
    }
}
