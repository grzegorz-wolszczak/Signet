using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The "Custom Epub Layout Designer" wizard — a dialog for designing the layout of an empty EPUB
/// (an in-memory tree with the buttons' logic and validity checks).
/// Result: a list of book paths, or <c>null</c> when cancelled.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "dialog and the view model's tree it observes.")]
public partial class EmptyLayoutWindow : Window
{
    private EmptyLayoutViewModel _viewModel = new("2.0");

    // The layout tree (Name, Type) over the view model's RootNodes, built here (a TreeDataGrid source is bound to the
    // UI thread); its selection and EmptyLayoutViewModel.SelectedNode follow each other.
    private LocalizedColumns<LayoutNode>? _columns;
    private HierarchicalTreeDataGridSource<LayoutNode>? _source;
    private bool _syncingSelection;

    private static FilePickerFileType LayoutFileType =>
        new(Strings.Get("EmptyLayoutWindow_JsonFileType")) { Patterns = new[] { "*.json" } };

    /// <summary>Initializes the window.</summary>
    public EmptyLayoutWindow()
    {
        InitializeComponent();

        AddFolderButton.Click += async (_, _) => await AddFolderAsync();
        AddFileButton.Click += (_, _) => ShowAddFileMenu();
        RenameButton.Click += async (_, _) => await RenameAsync();
        DeleteButton.Click += (_, _) => _viewModel.DeleteSelection();
        LoadButton.Click += async (_, _) => await LoadDesignAsync();
        SaveButton.Click += async (_, _) => await SaveDesignAsync();
        OkButton.Click += async (_, _) => await AcceptAsync();
        CancelButton.Click += (_, _) => Close(null);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is EmptyLayoutViewModel vm)
            {
                BindTree(vm);
            }
        };
    }

    private void BindTree(EmptyLayoutViewModel vm)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _source?.Dispose();
        _viewModel = vm;
        _columns = new LocalizedColumns<LayoutNode>();
        _source = new HierarchicalTreeDataGridSource<LayoutNode>(vm.RootNodes)
        {
            Columns =
            {
                _columns.Expander(
                    _columns.Text("ReportsWindow_Name", n => n.Name, new GridLength(1, GridUnitType.Star)),
                    n => n.Children,
                    n => n.Children.Count > 0,
                    n => n.IsExpanded),
                _columns.Text("ReportsWindow_Type", n => n.KindDisplay, new GridLength(110)),
            },
        };
        _source.RowSelection!.SelectionChanged += (_, _) =>
        {
            if (!_syncingSelection)
            {
                _viewModel.SelectedNode = _source.RowSelection.SelectedItem;
            }
        };
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Tree.Source = _source;
        SelectInTree(vm.SelectedNode);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmptyLayoutViewModel.SelectedNode))
        {
            SelectInTree(_viewModel.SelectedNode);
        }
    }

    // Selects the row of a node (its index path from the root), or nothing.
    private void SelectInTree(LayoutNode? node)
    {
        if (_source?.RowSelection is not { } selection || ReferenceEquals(selection.SelectedItem, node))
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            if (node is null)
            {
                selection.Clear();
                return;
            }

            List<int> path = new();
            for (LayoutNode current = node; current.Parent is { } parent; current = parent)
            {
                path.Insert(0, parent.Children.IndexOf(current));
            }

            path.Insert(0, 0);
            selection.SelectedIndex = new IndexPath(path);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Shows the wizard and returns the designed layout, or <c>null</c> when cancelled.</summary>
    public static Task<IReadOnlyList<string>?> DesignAsync(Window owner, string epubVersion, IReadOnlyList<string>? initialLayout)
    {
        EmptyLayoutWindow window = new()
        {
            DataContext = new EmptyLayoutViewModel(epubVersion, initialLayout),
        };
        return window.ShowDialog<IReadOnlyList<string>?>(owner);
    }

    private async Task AddFolderAsync()
    {
        string? name = await TextPromptWindow.AskAsync(
            this, Strings.Get("EmptyLayoutWindow_AddFolder"), Strings.Get("EmptyLayoutWindow_NewFolderNamePrompt"), Strings.Get("EmptyLayoutWindow_NewFolderDefaultName"));
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.AddFolder(name);
        }
    }

    private void ShowAddFileMenu()
    {
        MenuFlyout flyout = new();
        foreach (LayoutMarkerType type in _viewModel.MarkerTypes)
        {
            LayoutMarkerType captured = type;
            MenuItem item = new()
            {
                Header = $"{type.Label}  ({type.FileName})",
                IsEnabled = _viewModel.IsFileTypeAllowed(type.FileName),
            };
            item.Click += (_, _) => _viewModel.AddFile(captured);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(AddFileButton);
    }

    private async Task RenameAsync()
    {
        if (_viewModel.SelectedNode is not { } node)
        {
            return;
        }

        string? name = await TextPromptWindow.AskAsync(
            this, Strings.Get("EmptyLayoutWindow_RenameButton"), Strings.Get("EmptyLayoutWindow_NewNamePrompt"), node.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameSelection(name);
        }
    }

    private async Task LoadDesignAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("EmptyLayoutWindow_LoadDialogTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[] { LayoutFileType },
        });

        if (files.Count == 0)
        {
            return;
        }

        string path = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
        IReadOnlyList<string> bookPaths = EmptyEpubLayout.Read(path);
        if (bookPaths.Count > 0)
        {
            _viewModel.ReplaceWith(bookPaths);
        }
        else
        {
            await MessageDialog.ShowAsync(this, "Signet", Strings.Get("EmptyLayoutWindow_InvalidLayoutFile"));
        }
    }

    private async Task SaveDesignAsync()
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("EmptyLayoutWindow_SaveDialogTitle"),
            SuggestedFileName = "layout.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { LayoutFileType },
        });

        if (file is null)
        {
            return;
        }

        string path = file.TryGetLocalPath() ?? file.Path.LocalPath;
        EmptyEpubLayout.Write(path, _viewModel.GetBookPaths());
    }

    private async Task AcceptAsync()
    {
        if (!_viewModel.Validate())
        {
            return;
        }

        IReadOnlyList<string> bookPaths = _viewModel.GetBookPaths();

        bool makeDefault = await ConfirmWindow.AskAsync(
            this,
            "Signet",
            Strings.Get("EmptyLayoutWindow_MakeDefaultPrompt"));

        if (makeDefault)
        {
            try
            {
                EmptyEpubLayout.WriteDefault(bookPaths);
            }
            catch (IOException)
            {
                // ignore — do not block creating the book
            }
        }

        Close(bookPaths);
    }
}
