using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The "Custom Epub Layout Designer" wizard — a dialog for designing the layout of an empty EPUB
/// (an in-memory tree with the buttons' logic and validity checks).
/// Result: a list of book paths, or <c>null</c> when cancelled.
/// </summary>
public partial class EmptyLayoutWindow : Window
{
    private EmptyLayoutViewModel _viewModel = new("2.0");

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
                _viewModel = vm;
            }
        };
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
