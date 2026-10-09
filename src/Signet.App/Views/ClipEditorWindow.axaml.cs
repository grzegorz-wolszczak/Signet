using System.Collections.Generic;
using System.Linq;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The non-modal "Clip Editor" window. A single instance is
/// kept by <c>MainWindow</c> (Show/Activate, like <c>SpellcheckEditorWindow</c>)
/// and is bound to the same <see cref="ClipsViewModel"/> as the docked "Clips" panel.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "window and the view model's nodes it observes.")]
public partial class ClipEditorWindow : Window
{
    // The remembered layout: the share of the tree in the width of tree + details, and the widths of the columns the
    // user resized (a column never resized keeps its proportional width).
    private const string LayoutGroup = "clip_editor_layout";
    private const string TreeShareKey = "tree";
    private static readonly string[] ColumnKeys = { "name_column", "text_column" };

    private static FilePickerFileType JsonType => new(Strings.Get("ClipEditor_JsonFileType")) { Patterns = new[] { "*.json" } };

    private ClipsViewModel? _bound;
    private ClipNodeViewModel? _editingNode;
    private readonly ShortcutKeyBindings _keys;

    // The clips tree (Name, Text) over the visible nodes, built here (a TreeDataGrid source is bound to the UI thread);
    // its multi-selection and the view model's selected nodes follow each other.
    private LocalizedColumns<ClipNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<ClipNodeViewModel>? _source;
    private TreeMultiSelectionSync<ClipNodeViewModel>? _selection;
    private bool _forceClose;

    /// <summary>Initializes the window.</summary>
    public ClipEditorWindow()
    {
        InitializeComponent();
        _keys = new ShortcutKeyBindings(TreeHost);
        DataContextChanged += OnDataContextChanged;

        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnRenameEditorLostFocus, RoutingStrategies.Bubble);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestoreLayout();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        SaveLayout();

        // MainWindow creates a new window per opening while the view model is shared: a closed window must stop
        // reacting to it (otherwise its rename handler cancels the rename started in the new window).
        DataContext = null;
        base.OnClosed(e);
    }

    private void RestoreLayout()
    {
        if (App.Services?.GetService<SettingsStore>() is not { } settings)
        {
            return;
        }

        IReadOnlyDictionary<string, string> stored = settings.GetStringMap(LayoutGroup);
        if (TryRead(stored, TreeShareKey, out double share) && share is > 0.05 and < 0.95)
        {
            Body.ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
            Body.ColumnDefinitions[2].Width = new GridLength(1 - share, GridUnitType.Star);
        }

        for (int i = 0; _source is not null && i < ColumnKeys.Length && i < _source.Columns.Count; i++)
        {
            if (TryRead(stored, ColumnKeys[i], out double width) && width is >= 20 and <= 5000)
            {
                _source.Columns.SetColumnWidth(i, new GridLength(width, GridUnitType.Pixel));
            }
        }
    }

    private void SaveLayout()
    {
        if (App.Services?.GetService<SettingsStore>() is not { } settings)
        {
            return;
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        double tree = Body.ColumnDefinitions[0].ActualWidth;
        double details = Body.ColumnDefinitions[2].ActualWidth;
        if (tree > 0 && details > 0)
        {
            values[TreeShareKey] = (tree / (tree + details)).ToString("R", CultureInfo.InvariantCulture);
        }

        for (int i = 0; _source is not null && i < ColumnKeys.Length && i < _source.Columns.Count; i++)
        {
            if (_source.Columns[i].Width is { IsAbsolute: true } width)
            {
                values[ColumnKeys[i]] = width.Value.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        settings.SetStringMap(LayoutGroup, values);
        settings.Save();
    }

    private static bool TryRead(IReadOnlyDictionary<string, string> stored, string key, out double value)
    {
        value = 0;
        return stored.TryGetValue(key, out string? text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.ImportRequested -= OnImportRequested;
            _bound.ExportRequested -= OnExportRequested;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
            _keys.Detach();
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as ClipsViewModel;

        if (_bound is not null)
        {
            _bound.ImportRequested += OnImportRequested;
            _bound.ExportRequested += OnExportRequested;
            _bound.PropertyChanged += OnViewModelPropertyChanged;
            _keys.Attach(_bound.ShortcutActions);
            ClipsViewModel vm = _bound;
            _columns = new LocalizedColumns<ClipNodeViewModel>();
            VisibleItemsView<ClipNodeViewModel> roots = ClipsPanelView.Visible(vm.Nodes);
            _source = new HierarchicalTreeDataGridSource<ClipNodeViewModel>(roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("ReportsWindow_Name", "NameCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => ClipsPanelView.Visible(n.Children),
                        n => n.IsGroup,
                        n => n.IsExpanded),
                    _columns.Text("ReportsWindow_Text", n => n.TextPreview, new GridLength(1, GridUnitType.Star)),
                },
            };
            _selection = new TreeMultiSelectionSync<ClipNodeViewModel>(
                _source, roots, n => ClipsPanelView.Visible(n.Children), () => vm.SelectedNodes, vm.SetSelectedNodes,
                n => ClipsPanelView.Contains(vm.Nodes, n));
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    // --- in-place rename: double click / the Rename action (F2 by default, set in Preferences) ---- //

    private const string RenameEditorClass = "inPlaceRename";

    // Attached in the name cell template: a double click on a name enters rename (and is handled, so it does not
    // reach the tree).
    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double click in the editor selects a word, and on a group chevron only toggles it.
        if (_bound is null || IsInRenameEditor(e.Source) || IsOnGroupChevron(e.Source))
        {
            return;
        }

        if ((sender as Control)?.DataContext is ClipNodeViewModel node)
        {
            e.Handled = true;
            _bound.BeginRename(node);
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null)
        {
            return;
        }

        if (IsInRenameEditor(e.Source))
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    _bound.CommitRename();
                    break;
                case Key.Escape:
                    // Without Handled, Esc would close the window (the Close button has IsCancel).
                    e.Handled = true;
                    _bound.CancelRename();
                    break;
                case Key.Up or Key.Down or Key.PageUp or Key.PageDown:
                    // Without this the TreeView would move the selection under the editor.
                    e.Handled = true;
                    break;
            }

            return;
        }
    }

    private void OnRenameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsInRenameEditor(e.Source))
        {
            _bound?.CommitRename();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipsViewModel.SelectedNode))
        {
            _selection?.SelectFromViewModel();
            return;
        }

        if (e.PropertyName != nameof(ClipsViewModel.EditingNode) || _bound is null)
        {
            return;
        }

        ClipNodeViewModel? finished = _editingNode;
        _editingNode = _bound.EditingNode;

        // Panel shortcuts must not act on the tree while a name is being typed.
        _keys.SetSuspended(_editingNode is not null);
        if (_editingNode is not null)
        {
            // The view model is shared with the docked Clips panel: only the tree the rename was started in (the one
            // with the focus) shows the editor and takes the focus — the other one must not cancel the rename.
            if (Tree.IsKeyboardFocusWithin)
            {
                // The editor becomes visible only after a layout pass — the focus is set afterwards.
                Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Loaded);
            }
        }
        else if (finished is not null && IsInRenameEditor(FocusManager?.GetFocusedElement()))
        {
            // The focus returns to the node row so that the arrows / F2 keep working in the tree.
            // (In a TreeDataGrid the cells take the focus, not the rows.)
            if (RowOf(finished)?.TryGetCell(0) is { } cell)
            {
                cell.Focus();
            }
            else
            {
                Tree.Focus();
            }
        }
    }

    private void FocusRenameEditor()
    {
        TextBox? editor = Tree.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains(RenameEditorClass) && t.DataContext is ClipNodeViewModel { IsEditing: true });
        if (editor is null)
        {
            _bound?.CancelRename();
            return;
        }

        editor.Focus();
        editor.SelectAll();
    }

    private static bool IsInRenameEditor(object? source) =>
        source is Visual visual &&
        (visual as TextBox ?? visual.FindAncestorOfType<TextBox>())?.Classes.Contains(RenameEditorClass) == true;

    // The expander toggle of a group row (the only toggle button in the tree's cells).
    private static bool IsOnGroupChevron(object? source) =>
        source is Visual visual &&
        (visual as ToggleButton ?? visual.FindAncestorOfType<ToggleButton>()) is { } toggle &&
        toggle.FindAncestorOfType<TreeDataGridExpanderCell>() is not null;

    // The realized row of a node, if it is shown.
    private TreeDataGridRow? RowOf(ClipNodeViewModel node) =>
        _selection?.FindPath(node) is { } path && Tree.Rows?.ModelIndexToRowIndex(new IndexPath(path)) is >= 0 and var index
            ? Tree.TryGetRow(index)
            : null;

    /// <summary>Selects a node in the tree (e.g. a clip added through "Add To Clips..." from Code View).</summary>
    public void SelectNode(ClipNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_bound is null || _source is null || _selection?.FindPath(node) is not { } path)
        {
            return;
        }

        // Expand the groups above the node, select it and scroll it into view.
        for (int depth = 1; depth < path.Count; depth++)
        {
            _source.Expand(new IndexPath(path.Take(depth)));
        }

        _bound.SetSelectedNodes(new[] { node });
        if (Tree.Rows?.ModelIndexToRowIndex(new IndexPath(path)) is >= 0 and var index)
        {
            Tree.RowsPresenter?.BringIntoView(index);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // When the application closes, MainWindow asks (before the child windows are closed).
        if (!_forceClose && e.CloseReason != WindowCloseReason.OwnerWindowClosing && _bound is { } clips)
        {
            clips.CommitRename();
            if (clips.IsDataModified)
            {
                e.Cancel = true;
                _ = ConfirmAndCloseAsync(clips);
                return;
            }
        }

        base.OnClosing(e);
    }

    private async Task ConfirmAndCloseAsync(ClipsViewModel clips)
    {
        if (!await MaybeSaveDialogSaysProceedAsync(clips, this))
        {
            return;
        }

        _forceClose = true;
        Close();
    }

    /// <summary>
    /// Asks about saving unsaved changes in the clips (Save / Discard / Cancel). Returns false when
    /// the user cancelled. "Discard" reloads the library from disk.
    /// </summary>
    public static async Task<bool> MaybeSaveDialogSaysProceedAsync(ClipsViewModel clips, Window owner)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(owner);
        clips.CommitRename();
        if (!clips.IsDataModified)
        {
            return true;
        }

        switch (await SaveChangesDialog.AskAsync(owner, Strings.Get("ClipEditor_SaveChangesPrompt")))
        {
            case SaveChangesChoice.Save:
                clips.SaveCommand.Execute(null);
                return true;
            case SaveChangesChoice.Discard:
                clips.DiscardChanges();
                return true;
            default:
                return false;
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
            new FilePickerOpenOptions { Title = Strings.Get("ClipEditor_ImportTitle"), AllowMultiple = false, FileTypeFilter = new[] { JsonType } });

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
                Title = all ? Strings.Get("ClipEditor_ExportAllTitle") : Strings.Get("ClipEditor_ExportTitle"),
                DefaultExtension = "json",
                SuggestedFileName = "clips.json",
                FileTypeChoices = new[] { JsonType },
            });

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            _bound.ExportFile(path, all);
        }
    }
}
