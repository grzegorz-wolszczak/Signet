using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Signet.App.Actions;
using Signet.App.CodeEditor;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// The main application window. Registers the global keyboard shortcuts from the view model's actions,
/// provides the File menu dialogs (<see cref="IFileWorkflowPrompts"/>), the missing DOCTYPE warning
/// (<see cref="IMissingDoctypePrompt"/>) and guards unsaved changes.
/// </summary>
public partial class MainWindow : Window, IFileWorkflowPrompts, IMissingDoctypePrompt
{
    private MainWindowViewModel? _boundViewModel;
    private bool _forceClose;
    private SearchEditorWindow? _searchEditorWindow;
    private CssPropertyReferenceWindow? _cssPropertyReferenceWindow;

    private static readonly FilePickerFileType EpubFileType =
        new("EPUB") { Patterns = new[] { "*.epub" } };

    private static FilePickerFileType ImportableFileType =>
        new(Strings.Get("MainWindow_ImportableFiles"))
        {
            Patterns = new[] { "*.epub", "*.xhtml", "*.html", "*.htm", "*.txt" },
        };

    // The window-wide keyboard shortcuts (one- and two-stroke) of the actions.
    private readonly ShortcutKeyBindings _shortcutBindings;

    // The mouse shortcuts of the actions — for now only in Code Editor (MouseShortcutRouter.IsInCodeEditor).
    private readonly MouseShortcutRouter _mouseShortcuts;

    /// <summary>Initializes a new window instance and loads its XAML definition.</summary>
    public MainWindow()
    {
        InitializeComponent();
        _shortcutBindings = new ShortcutKeyBindings(this);
        // Mouse shortcuts work only in Code Editor, so the code editor actions take part too.
        _mouseShortcuts = new MouseShortcutRouter(
            this,
            () => _boundViewModel is { } vm ? vm.ShortcutActions.Concat(vm.CodeEditorShortcutActions) : Array.Empty<AppAction>(),
            MouseShortcutRouter.IsInCodeEditor);
        DataContextChanged += OnDataContextChanged;

        // Drag & drop of a file (e.g. an EPUB) onto the window = File → Open. handledEventsToo, because child
        // controls (the code editor, WebView) may handle their own drag events.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnFileDrop, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        // A right click on a main menu item with its own context items (e.g. a "Recent Files" entry). The
        // event bubbles from the submenu popups to the Menu; Avalonia does not click items with the right button.
        MainMenu.ContextRequested += OnMainMenuContextRequested;

        // Every opening of a menu checks the recent files again: a file deleted while Signet was running has to show as
        // missing without waiting for the list itself to change (File.Exists on at most a few dozen paths).
        MainMenu.AddHandler(MenuItem.SubmenuOpenedEvent, OnMainMenuSubmenuOpened);
    }

    // A top-level menu (File, Edit, ...) opening: the recent files are read from disk again. Nested submenus do not
    // carry the list, so they are left alone.
    private void OnMainMenuSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { DataContext: Menu.MenuItemViewModel } item && item.Parent is Avalonia.Controls.Menu)
        {
            _boundViewModel?.RefreshRecentFilesMenu();
        }
    }

    private static void OnMainMenuContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        MenuItem? item = (e.Source as Visual)?.FindAncestorOfType<MenuItem>(includeSelf: true);
        if (item?.DataContext is not Menu.MenuItemViewModel { ContextItems: { Count: > 0 } contextItems })
        {
            return;
        }

        ContextMenu menu = new()
        {
            ItemsSource = contextItems
                .Select(c => new MenuItem
                {
                    Header = c.Header,
                    Icon = CreateActionIcon(c.IconKey),
                    // Posted: the command may rebuild the list (removing the item this menu is opened on)
                    // — let the context menu close first.
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(
                        () => Dispatcher.UIThread.Post(() => c.Command?.Execute(null))),
                })
                .ToList(),
        };
        menu.Open(item);
        e.Handled = true;
    }

    private static Control? CreateActionIcon(string? iconKey)
    {
        if (ActionIcons.Resolve(iconKey, ActionIconState.Normal) is { } image)
        {
            return new Image { Width = 16, Height = 16, Source = image };
        }

        if (IconKeyToGeometryConverter.Instance.Convert(
                iconKey, typeof(Geometry), null, CultureInfo.InvariantCulture) is not Geometry geometry)
        {
            return null;
        }

        return new PathIcon { Width = 16, Height = 16, Data = geometry };
    }

    private static string? FirstDroppedFilePath(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (FirstDroppedFilePath(e) is not null)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        string? path = FirstDroppedFilePath(e);
        if (path is null || _boundViewModel?.FileWorkflow is not { } workflow)
        {
            return;
        }

        e.Handled = true;
        await workflow.OpenDroppedFileAsync(path);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.CustomizeToolbarsRequested -= OnCustomizeToolbarsRequested;
            _boundViewModel.CustomizeToolbarRequested -= OnCustomizeToolbarRequested;
            _boundViewModel.GoToLineRequested -= OnGoToLineRequested;
            _boundViewModel.AboutRequested -= OnAboutRequested;
            _boundViewModel.CssPropertyReferenceRequested -= OnCssPropertyReferenceRequested;
            _boundViewModel.RenameTagRequested -= OnRenameTagRequested;
            _boundViewModel.MergeContentConfirmationRequested -= OnMergeContentConfirmationRequested;
            _boundViewModel.UnwrapDivConfirmationRequested -= OnUnwrapDivConfirmationRequested;
            _boundViewModel.RevertConfirmationRequested -= OnRevertConfirmationRequested;
            _boundViewModel.RecentLocationsRequested -= OnRecentLocationsRequested;
            _boundViewModel.CreateCheckpointRequested -= OnCreateCheckpointRequested;
            _boundViewModel.CompareCheckpointRequested -= OnCompareCheckpointRequested;
            _boundViewModel.MendDiffRequested -= OnCompareCheckpointRequested;
            _boundViewModel.FindPanelFocusRequested -= OnFindPanelFocusRequested;
            _boundViewModel.AddCoverRequested -= OnAddCoverRequested;
            _boundViewModel.InsertSpecialCharacterRequested -= OnInsertSpecialCharacterRequested;
            _boundViewModel.PasteClipboardHistoryRequested -= OnPasteClipboardHistoryRequested;
            _boundViewModel.InsertFileRequested -= OnInsertFileRequested;
            _boundViewModel.InsertIdRequested -= OnInsertIdRequested;
            _boundViewModel.InsertHyperlinkRequested -= OnInsertHyperlinkRequested;
            _boundViewModel.InsertAriaClipRequested -= OnInsertAriaClipRequested;
            _boundViewModel.SelectClipRequested -= OnSelectClipRequested;
            _boundViewModel.ClipEditorRequested -= OnClipEditorRequested;
            _boundViewModel.ViewImageRequested -= OnViewImageRequested;
            _boundViewModel.DryRunReplaceRequested -= OnDryRunReplaceRequested;
            _boundViewModel.FilterReplacementsRequested -= OnFilterReplacementsRequested;
            _boundViewModel.GenerateTocRequested -= OnGenerateTocRequested;
            _boundViewModel.EditTocRequested -= OnEditTocRequested;
            _boundViewModel.MetadataEditorRequested -= OnMetadataEditorRequested;
            _boundViewModel.PreferencesRequested -= OnPreferencesRequested;
            _boundViewModel.ReportsRequested -= OnReportsRequested;
            _boundViewModel.SpellcheckEditorRequested -= OnSpellcheckEditorRequested;
            _boundViewModel.CleanupRequested -= OnCleanupRequested;
            _boundViewModel.ManageFontsRequested -= OnManageFontsRequested;
            _boundViewModel.FootnoteConversionRequested -= OnFootnoteConversionRequested;
            _boundViewModel.FootnoteLettersRequested -= OnFootnoteLettersRequested;
            _boundViewModel.FootnotePlacementRequested -= OnFootnotePlacementRequested;
            _boundViewModel.StandardizeRequested -= OnStandardizeRequested;
            _boundViewModel.LiveCssPanelRequested -= OnLiveCssPanelRequested;
            _boundViewModel.LiveCssContextChanged -= OnLiveCssContextChanged;
            _boundViewModel.SearchEditorRequested -= OnSearchEditorRequested;
            _boundViewModel.StandardizeEpubRequested -= OnStandardizeEpubRequested;
            _boundViewModel.CloseWindowRequested -= OnCloseWindowRequested;
            _boundViewModel.SkippedFilesReportRequested -= OnSkippedFilesReportRequested;
        }

        if (_boundViewModel?.DockFactory is MainDockFactory oldFactory)
        {
            oldFactory.HostWindowCreated -= OnHostWindowCreated;
        }

        _boundViewModel = DataContext as MainWindowViewModel;
        _shortcutBindings.Detach();

        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.CustomizeToolbarsRequested += OnCustomizeToolbarsRequested;
        _boundViewModel.CustomizeToolbarRequested += OnCustomizeToolbarRequested;
        _boundViewModel.GoToLineRequested += OnGoToLineRequested;
        _boundViewModel.AboutRequested += OnAboutRequested;
        _boundViewModel.CssPropertyReferenceRequested += OnCssPropertyReferenceRequested;
        _boundViewModel.RenameTagRequested += OnRenameTagRequested;
        _boundViewModel.MergeContentConfirmationRequested += OnMergeContentConfirmationRequested;
        _boundViewModel.UnwrapDivConfirmationRequested += OnUnwrapDivConfirmationRequested;
        _boundViewModel.RevertConfirmationRequested += OnRevertConfirmationRequested;
        _boundViewModel.RecentLocationsRequested += OnRecentLocationsRequested;
        _boundViewModel.CreateCheckpointRequested += OnCreateCheckpointRequested;
        _boundViewModel.CompareCheckpointRequested += OnCompareCheckpointRequested;
        _boundViewModel.MendDiffRequested += OnCompareCheckpointRequested;
        _boundViewModel.FindPanelFocusRequested += OnFindPanelFocusRequested;
        _boundViewModel.AddCoverRequested += OnAddCoverRequested;
        _boundViewModel.InsertSpecialCharacterRequested += OnInsertSpecialCharacterRequested;
        _boundViewModel.PasteClipboardHistoryRequested += OnPasteClipboardHistoryRequested;
        _boundViewModel.InsertFileRequested += OnInsertFileRequested;
        _boundViewModel.InsertIdRequested += OnInsertIdRequested;
        _boundViewModel.InsertHyperlinkRequested += OnInsertHyperlinkRequested;
        _boundViewModel.InsertAriaClipRequested += OnInsertAriaClipRequested;
        _boundViewModel.SelectClipRequested += OnSelectClipRequested;
        _boundViewModel.ClipEditorRequested += OnClipEditorRequested;
        _boundViewModel.ViewImageRequested += OnViewImageRequested;
        _boundViewModel.DryRunReplaceRequested += OnDryRunReplaceRequested;
        _boundViewModel.FilterReplacementsRequested += OnFilterReplacementsRequested;
        _boundViewModel.GenerateTocRequested += OnGenerateTocRequested;
        _boundViewModel.EditTocRequested += OnEditTocRequested;
        _boundViewModel.MetadataEditorRequested += OnMetadataEditorRequested;
        _boundViewModel.PreferencesRequested += OnPreferencesRequested;
        _boundViewModel.ReportsRequested += OnReportsRequested;
        _boundViewModel.SpellcheckEditorRequested += OnSpellcheckEditorRequested;
        _boundViewModel.CleanupRequested += OnCleanupRequested;
        _boundViewModel.ManageFontsRequested += OnManageFontsRequested;
        _boundViewModel.FootnoteConversionRequested += OnFootnoteConversionRequested;
        _boundViewModel.FootnoteLettersRequested += OnFootnoteLettersRequested;
        _boundViewModel.FootnotePlacementRequested += OnFootnotePlacementRequested;
        _boundViewModel.StandardizeRequested += OnStandardizeRequested;
        _boundViewModel.LiveCssPanelRequested += OnLiveCssPanelRequested;
        _boundViewModel.LiveCssContextChanged += OnLiveCssContextChanged;
        _boundViewModel.SearchEditorRequested += OnSearchEditorRequested;
        _boundViewModel.StandardizeEpubRequested += OnStandardizeEpubRequested;
        _boundViewModel.CloseWindowRequested += OnCloseWindowRequested;
        _boundViewModel.SkippedFilesReportRequested += OnSkippedFilesReportRequested;
        _boundViewModel.AttachFileWorkflowPrompts(this);

        // The long operations on the whole book show how far they are over this window (and can be cancelled).
        _boundViewModel.ProgressRunner = (title, work, cancellable) =>
            ProgressWindow.RunWithProgressAsync(this, title, Strings.Get("Progress_Starting"), work, cancellable);

        // Quick edits that are usually immediate but can take a while on a very large book (e.g. Remove Edition Page,
        // Spellcheck's "Change All to...") — a window only once that happens, and never with Cancel.
        _boundViewModel.DelayedProgressRunner = ProgressWindow.DelayedRunner(this);

        // A shortcut change in Preferences takes effect immediately, without restarting the application.
        _shortcutBindings.Attach(_boundViewModel.ShortcutActions);

        if (_boundViewModel.DockFactory is MainDockFactory factory)
        {
            factory.HostWindowCreated += OnHostWindowCreated;
        }
    }

    // A document tab dragged out of the window works like in the main window: the same keyboard and mouse shortcuts.
    private void OnHostWindowCreated(object? sender, HostWindow host)
    {
        if (_boundViewModel is not { } vm)
        {
            return;
        }

        ShortcutKeyBindings bindings = new(host);
        bindings.Attach(vm.ShortcutActions);
        _ = new MouseShortcutRouter(
            host,
            () => vm.ShortcutActions.Concat(vm.CodeEditorShortcutActions),
            MouseShortcutRouter.IsInCodeEditor);
        host.Closed += (_, _) => bindings.Detach();

        // The tabs in the window of a tab dragged out are laid out like those of the main window.
        void UpdateTabLayout(object? changed, System.ComponentModel.PropertyChangedEventArgs? e)
        {
            if (e is null || e.PropertyName == nameof(MainWindowViewModel.MultilineTabs))
            {
                host.Classes.Set("multilineTabs", vm.MultilineTabs);
            }
        }

        UpdateTabLayout(null, null);
        vm.PropertyChanged += UpdateTabLayout;
        host.Closed += (_, _) => vm.PropertyChanged -= UpdateTabLayout;
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_boundViewModel is { } vm)
        {
            if (Clipboard is { } clipboard)
            {
                vm.AttachClipboard(clipboard);
            }

            vm.AttachMissingDoctypePrompt(this);
            LogStartupEnvironment();
            vm.RestoreFloatingPanels();

            _ = vm.RunStartupAsync();
        }
    }

    // The debug log starts with what a window problem needs: version, system, monitors and their scaling, language.
    private void LogStartupEnvironment()
    {
        if (!DebugLog.IsEnabled)
        {
            return;
        }

        DebugLog.Write(
            "Startup",
            $"{AppVersion.Text}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; UI language {CultureInfo.CurrentUICulture.Name}");
        if (Screens is { } screens)
        {
            foreach (Avalonia.Platform.Screen screen in screens.All)
            {
                DebugLog.Write(
                    "Startup",
                    FormattableString.Invariant(
                        $"monitor{(screen.IsPrimary ? " (primary)" : string.Empty)}: bounds {screen.Bounds}, working area {screen.WorkingArea}, scaling {screen.Scaling:0.##}"));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_forceClose && _boundViewModel is { } vm && (vm.FileWorkflow is not null || vm.Clips.IsDataModified))
        {
            e.Cancel = true;
            _ = ConfirmAndCloseAsync(vm);
            return;
        }

        _boundViewModel?.PersistState();
        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // The layout (with floating panels saved in their regions) was persisted in OnClosing.
        _boundViewModel?.CloseFloatingPanels();
        base.OnClosed(e);
    }

    private void OnCloseWindowRequested(object? sender, EventArgs e) => Close();

    private async Task ConfirmAndCloseAsync(MainWindowViewModel vm)
    {
        if (vm.FileWorkflow is { } workflow && !await workflow.MaybeSaveDialogSaysProceedAsync())
        {
            return;
        }

        // Unsaved clips — Cancel
        // aborts closing the application.
        Window clipOwner = _clipEditorWindow is { IsVisible: true } clipWindow ? clipWindow : this;
        if (!await ClipEditorWindow.MaybeSaveDialogSaysProceedAsync(vm.Clips, clipOwner))
        {
            return;
        }

        _forceClose = true;
        Close();
    }

    // ------------------------------------------------ IFileWorkflowPrompts --- //

    /// <inheritdoc />
    public async Task<T> ShowProgressAsync<T>(string title, string message, Func<IProgress<WorkProgress>, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Task<T>? running = null;
        await ProgressWindow.RunWithProgressAsync(
            this, title, message, (progress, _) => running = work(progress), cancellable: false, ProgressWindow.DefaultDelay);
        return await running!;
    }

    /// <inheritdoc />
    public async Task<string?> AskOpenPathAsync(string startFolder)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("MainWindow_OpenFileTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[] { ImportableFileType, EpubFileType },
        });

        if (files.Count == 0)
        {
            return null;
        }

        return files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
    }

    /// <inheritdoc />
    public async Task<string?> AskSavePathAsync(string suggestedPath)
    {
        string? dir = Path.GetDirectoryName(suggestedPath);
        IStorageFolder? startLocation = dir is not null && Directory.Exists(dir)
            ? await StorageProvider.TryGetFolderFromPathAsync(dir)
            : null;

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("MainWindow_SaveEpubTitle"),
            SuggestedFileName = Path.GetFileName(suggestedPath),
            DefaultExtension = "epub",
            FileTypeChoices = new[] { EpubFileType },
            SuggestedStartLocation = startLocation,
        });

        return file is null ? null : file.TryGetLocalPath() ?? file.Path.LocalPath;
    }

    /// <inheritdoc />
    public Task<SaveChangesChoice> AskSaveChangesAsync() => SaveChangesDialog.AskAsync(this);

    /// <inheritdoc />
    public Task<FileInUseChoice> AskFileInUseAsync(string fileName) => FileInUseDialog.AskAsync(this, fileName);

    /// <inheritdoc />
    public void CloseApplication() => Dispatcher.UIThread.Post(Close);

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message) => MessageDialog.ShowAsync(this, title, message);

    /// <inheritdoc />
    public Task ShowLoadWarningsAsync(string fileName, IReadOnlyList<LoadWarning> warnings)
    {
        // Non-modal, so that its files can be opened one after another; a window left from the previous book goes.
        _loadWarningsWindow?.Close();
        _loadWarningsWindow = LoadWarningsDialog.Show(
            this, fileName, warnings, _boundViewModel is { } vm ? vm.NavigateToBookPathAtLocation : null);
        return Task.CompletedTask;
    }

    private LoadWarningsDialog? _loadWarningsWindow;

    /// <summary>
    /// The report of an operation that skipped some files: the same non-modal window as the load warnings, with the
    /// skipped files; a report left from the previous operation goes.
    /// </summary>
    private void OnSkippedFilesReportRequested(object? sender, SkippedFilesReport report)
    {
        _skippedFilesWindow?.Close();
        _skippedFilesWindow = LoadWarningsDialog.ShowReport(
            this, report, _boundViewModel is { } vm ? vm.NavigateToBookPathAtLocation : null);
    }

    private LoadWarningsDialog? _skippedFilesWindow;

    /// <inheritdoc />
    public async Task<MissingRecentAnswer> ConfirmRemoveMissingRecentAsync(string path)
    {
        (bool confirmed, bool allMissing) = await ConfirmWindow.AskWithOptionAsync(
            this,
            "Signet",
            Strings.Format("MainWindow_RecentFileMissing", path),
            Strings.Get("MainWindow_RemoveAllMissingRecent"));
        return new MissingRecentAnswer(confirmed, allMissing);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>?> DesignCustomLayoutAsync(string version) =>
        EmptyLayoutWindow.DesignAsync(this, version, Signet.Core.Misc.EmptyEpubLayout.ReadDefault() is { Count: > 0 } d ? d : null);

    // ----------------------------------------------- IMissingDoctypePrompt --- //

    /// <inheritdoc />
    public Task<MissingDoctypeAnswer> AskAsync(string operationName, IReadOnlyList<string> fileNames) =>
        MissingDoctypeDialog.AskAsync(this, operationName, fileNames);

    // --------------------------------------------------------------- misc --- //

    private void OnFindPanelFocusRequested(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (Visual descendant in this.GetVisualDescendants())
            {
                if (descendant is FindReplaceView view)
                {
                    view.FocusFindField();
                    return;
                }
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private async void OnAboutRequested(object? sender, EventArgs e) =>
        await new AboutWindow().ShowDialog(this);

    private void OnCssPropertyReferenceRequested(object? sender, EventArgs e) => ShowCssPropertyReferenceWindow();

    internal void ShowCssPropertyReferenceWindow()
    {
        if (_cssPropertyReferenceWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        CssPropertyReferenceWindow window = new() { DataContext = new CssPropertyReferenceViewModel() };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_cssPropertyReferenceWindow, window))
            {
                _cssPropertyReferenceWindow = null;
            }
        };
        _cssPropertyReferenceWindow = window;
        RememberPlacement(window);
        window.Show(this);
    }

    private async void OnGoToLineRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm?.ActiveCodeTab is null)
        {
            return;
        }

        string? answer = await TextPromptWindow.AskAsync(
            this, Strings.Get("MainWindow_GoToLineTitle"), Strings.Get("MainWindow_GoToLinePrompt"), vm.ActiveCodeTab.CaretLine.ToString(CultureInfo.InvariantCulture));

        if (int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int line) && line > 0)
        {
            vm.GoToLine(line);
        }
    }

    // "Unwrap div": the div has an id — the links to it would break.
    private async void OnUnwrapDivConfirmationRequested(object? sender, string id)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is not null
            && await ConfirmWindow.AskAsync(
                this, Strings.Get("UnwrapDiv_Title"), Strings.Format("UnwrapDiv_IdWarning", id), Strings.Get("UnwrapDiv_Unwrap")))
        {
            vm.ConfirmUnwrapDiv();
        }
    }

    // "Merge Content": different attributes, text or comments between the elements — a single window
    // with all the warnings.
    private async void OnMergeContentConfirmationRequested(object? sender, ElementMergeCandidate candidate)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string message = MainWindowViewModel.MergeContentWarning(candidate);
        if (await ConfirmWindow.AskAsync(this, Strings.Get("MergeContent_Title"), message, Strings.Get("MergeContent_Merge")))
        {
            vm.ConfirmMergeContent();
        }
    }

    // "Revert to before / after …" that removes, brings back or renames files — the list of those files first.
    private async void OnRevertConfirmationRequested(object? sender, CheckpointRevertRequest request)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null || !await CheckpointRevertWindow.AskAsync(this, request))
        {
            return;
        }

        if (request.Forward)
        {
            await vm.RevertToAfterCheckpointAsync();
        }
        else
        {
            await vm.RevertToBeforeCheckpointAsync();
        }
    }

    // Recent Locations — a popup that closes when it loses focus; its size and place are remembered.
    private void OnRecentLocationsRequested(object? sender, RecentLocationsViewModel model)
    {
        RecentLocationsWindow window = new(_boundViewModel?.Actions.Require(AppActionIds.RecentLocations).Gesture) { DataContext = model };
        RememberPlacement(window);
        window.Show(this);
    }

    // "Create Checkpoint…" — a prompt asking for a name.
    private async void OnCreateCheckpointRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? name = await TextPromptWindow.AskAsync(
            this, Strings.Get("Checkpoint_CreateTitle"), Strings.Get("Checkpoint_CreatePrompt"), string.Empty);
        if (name is not null)
        {
            await vm.CreateCheckpointAsync(name);
        }
    }

    // "Compare" in the checkpoints panel, and the changes of "Mend Code" — a non-modal diff window; a double click
    // in it jumps to the editor in this window.
    private void OnCompareCheckpointRequested(object? sender, DiffViewModel diff)
    {
        diff.OpenInEditorRequested += (_, _) => Activate();
        DiffWindow.ShowFor(this, diff);
    }

    private async void OnRenameTagRequested(object? sender, string currentName)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? answer = await TextPromptWindow.AskAsync(
            this, Strings.Get("RenameTag_Title"), Strings.Get("RenameTag_Prompt"), currentName);
        if (!string.IsNullOrWhiteSpace(answer))
        {
            vm.RenameEnclosingTag(answer);
        }
    }

    private async void OnAddCoverRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        IReadOnlyList<ImageResource> images = vm.GetCoverCandidateImages();
        if (images.Count == 0)
        {
            await MessageDialog.ShowAsync(
                this, Strings.Get("MainWindow_AddCoverTitle"), Strings.Get("MainWindow_AddCoverNoImages"));
            return;
        }

        SelectFilesResult? result = await SelectFilesWindow.AskAsync(
            this,
            Strings.Get("MainWindow_AddCoverTitle"),
            Strings.Get("MainWindow_AddCoverPrompt"),
            SelectFilesMode.SingleFile,
            images.Select(i => new SelectFilesEntry(i.BookPath, i.Filename, i.Type, i.FullPath)).ToList(),
            allowInsertFromDisk: true,
            defaultSelectedBookPath: vm.LastInsertedFile);
        if (result is null)
        {
            return;
        }

        if (result.FromDisk)
        {
            IReadOnlyList<string> paths = await PickLocalFilePathsAsync(Strings.Get("MainWindow_AddCoverTitle"));
            IReadOnlyList<Resource> added = vm.AddFilesFromDisk(paths);
            ImageResource? addedImage = added.OfType<ImageResource>().FirstOrDefault();
            if (addedImage is null)
            {
                return;
            }

            await FinishAddCoverAsync(vm, addedImage);
            return;
        }

        ImageResource? image = images.FirstOrDefault(
            i => string.Equals(i.BookPath, result.BookPaths.FirstOrDefault(), StringComparison.Ordinal));
        if (image is null)
        {
            return;
        }

        await FinishAddCoverAsync(vm, image);
    }

    private async Task FinishAddCoverAsync(MainWindowViewModel vm, ImageResource image)
    {
        if (vm.FindExistingCoverHtml() is { } existing)
        {
            bool overwrite = await ConfirmWindow.AskAsync(
                this, Strings.Get("MainWindow_AddCoverTitle"),
                Strings.Format("MainWindow_AddCoverOverwrite", existing.Filename));
            if (!overwrite)
            {
                return;
            }
        }

        await vm.ApplyAddCoverAsync(image);
    }

    // ------------------------------------------------- "Insert" menu --- //

    private async void OnInsertSpecialCharacterRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        await SpecialCharacterWindow.ShowAsync(
            this,
            new SpecialCharacterDialogContext(
                vm.SpecialCharacterAppearance,
                vm.RecentSpecialCharacters,
                vm.FavoriteSpecialCharacters,
                vm.SpecialCharacterSearchAll,
                vm.ActiveDocumentDefinesHtmlEntities)
            {
                DoubleClickInsertsCell = vm.SpecialCharacterDoubleClickInsertsCell,
                OnAddFavorite = vm.AddFavoriteSpecialCharacter,
                OnRemoveFavorite = vm.RemoveFavoriteSpecialCharacter,
                OnSearchAllChanged = value => vm.SpecialCharacterSearchAll = value,
                OnDoubleClickInsertsCellChanged = value => vm.SpecialCharacterDoubleClickInsertsCell = value,
                OnInsert = chosen => vm.InsertSpecialCharacter(chosen.Text, chosen.Character),
            });
    }

    private async void OnPasteClipboardHistoryRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? chosen = await ClipboardHistorySelectorWindow.AskAsync(this, vm.GetClipboardHistory());
        if (!string.IsNullOrEmpty(chosen))
        {
            vm.PasteClipboardHistoryEntry(chosen);
        }
    }

    private async void OnInsertFileRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        IReadOnlyList<Resource> media = vm.GetInsertableMediaResources();
        SelectFilesResult? result = await SelectFilesWindow.AskAsync(
            this,
            Strings.Get("MainWindow_InsertFileTitle"),
            Strings.Get("MainWindow_InsertFilePrompt"),
            SelectFilesMode.Multiple,
            media.Select(r => new SelectFilesEntry(r.BookPath, r.Filename, r.Type, r.FullPath)).ToList(),
            allowInsertFromDisk: true,
            showTypeFilter: true,
            defaultSelectedBookPath: vm.LastInsertedFile);
        if (result is null)
        {
            return;
        }

        if (result.FromDisk)
        {
            IReadOnlyList<string> paths = await PickLocalFilePathsAsync(Strings.Get("MainWindow_InsertFileTitle"));
            vm.InsertFilesFromDisk(paths);
            return;
        }

        List<Resource> chosen = result.BookPaths
            .Select(bp => media.FirstOrDefault(r => string.Equals(r.BookPath, bp, StringComparison.Ordinal)))
            .OfType<Resource>()
            .ToList();
        vm.InsertMediaResources(chosen);
    }

    /// <summary>
    /// Opens the native file picker and returns the local paths of the chosen files (the "Insert from
    /// disk" option of the shared Select Files dialog — Insert File / Add Cover).
    /// </summary>
    private async Task<IReadOnlyList<string>> PickLocalFilePathsAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = title, AllowMultiple = true });

        var paths = new List<string>();
        foreach (IStorageFile file in files)
        {
            if (file.TryGetLocalPath() is { Length: > 0 } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private async void OnInsertIdRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? id = await SelectIdWindow.AskAsync(this, vm.GetIdsInActiveFile(), vm.GetInsertIdInitialValue());
        if (!string.IsNullOrEmpty(id))
        {
            vm.ApplyInsertId(id);
        }
    }

    private async void OnInsertHyperlinkRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? target = await SelectHyperlinkWindow.AskAsync(
            this, vm.GetHyperlinkTargets(), vm.GetInsertHyperlinkInitialValue());
        if (!string.IsNullOrEmpty(target))
        {
            vm.ApplyInsertHyperlink(target);
        }
    }

    private async void OnInsertAriaClipRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? code = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MainWindow_InsertAriaClipTitle"), vm.GetAriaClipOptions());
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        string? roleCode = null;
        if (vm.AriaClipRequiresRole(code))
        {
            roleCode = await PickMetadataFieldWindow.AskOneAsync(this, Strings.Get("MetadataEditorWindow_SelectRole"), vm.GetAriaRoleOptions(code));
        }

        vm.ApplyAriaClip(code, roleCode);
    }

    private async void OnSelectClipRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        string? text = await SelectClipWindow.AskAsync(this, vm.GetLeafClipPickerItems());
        if (!string.IsNullOrEmpty(text))
        {
            vm.ApplySelectedClip(text);
        }
    }

    private ClipEditorWindow? _clipEditorWindow;

    /// <summary>
    /// A non-modal window opens where it was the last time (by its type; corrected when that place is no longer
    /// fully on screen) and remembers its place when it closes. Dialogs keep opening centered over the main window.
    /// </summary>
    internal static void RememberPlacement(Window window)
    {
        if (App.Services?.GetService<SettingsStore>() is { } settings)
        {
            WindowPlacement.Remember(window, window.GetType().Name, settings);
        }
    }

    /// <summary>
    /// Opens (or activates) the non-modal "Clip Editor" window — a single persistent instance,
    /// the same pattern as <see cref="OnSpellcheckEditorRequested"/>; unlike Spellcheck, the
    /// <c>DataContext</c> (<see cref="MainWindowViewModel.Clips"/>) is always the same, shared
    /// with the docked "Clips" panel, so it needs no refresh on open.
    /// </summary>
    private void OnClipEditorRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        if (_clipEditorWindow is null || !_clipEditorWindow.IsVisible)
        {
            _clipEditorWindow = new ClipEditorWindow { DataContext = vm.Clips };
            RememberPlacement(_clipEditorWindow);
            _clipEditorWindow.Show(this);
        }
        else
        {
            _clipEditorWindow.Activate();
        }

        // "Add To Clips..." from the Code Editor context menu selects the added clip.
        if (vm.Clips.SelectedNode is { } selected)
        {
            _clipEditorWindow.SelectNode(selected);
        }
    }

    private ViewImageWindow? _viewImageWindow;

    /// <summary>
    /// "View Image" from the Code Editor context menu — a non-modal window with a single persistent
    /// instance (shown/activated, then <c>ShowImage</c>).
    /// </summary>
    private void OnViewImageRequested(object? sender, Signet.Core.Resources.Resource resource)
    {
        if (_viewImageWindow is null || !_viewImageWindow.IsVisible)
        {
            _viewImageWindow = new ViewImageWindow();
            RememberPlacement(_viewImageWindow);
            _viewImageWindow.Show(this);
        }
        else
        {
            _viewImageWindow.Activate();
        }

        _viewImageWindow.ShowImage(resource);
    }

    private LiveCssPanelWindow? _liveCssPanelWindow;
    private LiveCssPanelViewModel? _liveCssPanelViewModel;
    private DispatcherTimer? _liveCssRefreshTimer;

    /// <summary>
    /// Opens (or activates) the non-modal "Live CSS Panel" window — a single persistent instance
    /// (the same pattern as <see cref="OnClipEditorRequested"/>) whose content follows the element
    /// under the caret of the active Code Editor tab (see <see cref="OnLiveCssContextChanged"/>).
    /// </summary>
    private void OnLiveCssPanelRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        CssCascadeResult? result = vm.TryResolveLiveCssPanel();
        if (result is null)
        {
            return;
        }

        _liveCssPanelViewModel ??= new LiveCssPanelViewModel(App.Services?.GetService<SettingsStore>());
        _liveCssPanelViewModel.Update(result, vm.GetResourceTextForLiveCssPanel, vm.JumpToCssLocation);

        if (_liveCssPanelWindow is null || !_liveCssPanelWindow.IsVisible)
        {
            _liveCssPanelWindow = new LiveCssPanelWindow { DataContext = _liveCssPanelViewModel };
            RememberPlacement(_liveCssPanelWindow);
            _liveCssPanelWindow.Show(this);
        }
        else
        {
            _liveCssPanelWindow.Activate();
        }
    }

    /// <summary>
    /// Automatic refresh of an open Live CSS Panel after the caret moves or the content changes —
    /// debounced, so typing or holding an arrow key does not recompute the cascade on every keystroke.
    /// </summary>
    private void OnLiveCssContextChanged(object? sender, EventArgs e)
    {
        if (_liveCssPanelWindow is not { IsVisible: true })
        {
            return;
        }

        if (_liveCssRefreshTimer is null)
        {
            _liveCssRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _liveCssRefreshTimer.Tick += OnLiveCssRefreshTimerTick;
        }

        _liveCssRefreshTimer.Stop();
        _liveCssRefreshTimer.Start();
    }

    private void OnLiveCssRefreshTimerTick(object? sender, EventArgs e)
    {
        _liveCssRefreshTimer?.Stop();
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null || _liveCssPanelViewModel is null || _liveCssPanelWindow is not { IsVisible: true })
        {
            return;
        }

        // No active HTML tab (e.g. after jumping to a rule in a CSS file) or the caret outside an element:
        // keep the previous content instead of clearing the panel.
        if (vm.TryResolveLiveCssPanel(reportFailure: false) is { } result)
        {
            _liveCssPanelViewModel.Update(result, vm.GetResourceTextForLiveCssPanel, vm.JumpToCssLocation);
        }
    }

    // The non-modal "Saved Searches" window — a single persistent instance.
    private void OnSearchEditorRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        if (_searchEditorWindow is null || !_searchEditorWindow.IsVisible)
        {
            _searchEditorWindow = new SearchEditorWindow { DataContext = vm.SearchEditor };
            RememberPlacement(_searchEditorWindow);
            _searchEditorWindow.Show(this);
        }
        else
        {
            _searchEditorWindow.Activate();
        }
    }

    private void OnDryRunReplaceRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        ReplacePreviewRequest? request = vm.FindReplace.TryBuildReplacePreviewRequest();
        if (request is null)
        {
            return;
        }

        var window = new DryRunReplaceWindow
        {
            DataContext = new DryRunReplaceViewModel(
                request.Resources,
                request.SearchRegex,
                request.ReplaceText,
                vm.FindReplace.OpenAtOffset),
        };
        RememberPlacement(window);
        window.Show(this);
    }

    private async void OnFilterReplacementsRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        ReplacePreviewRequest? request = vm.FindReplace.TryBuildReplacePreviewRequest();
        if (request is null)
        {
            return;
        }

        var viewModel = new ReplacementChooserViewModel(
            request.Resources,
            request.SearchRegex,
            request.ReplaceText,
            vm.FindReplace.ApplyChosenReplacements);

        await ReplacementChooserWindow.RunAsync(this, viewModel);
    }

    private async void OnGenerateTocRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        HeadingSelectorViewModel? selector = await vm.CreateHeadingSelectorViewModelAsync();
        if (selector is null)
        {
            return;
        }

        selector.NavigateToSource = heading =>
            vm.NavigateToBookPathAtOffset(heading.ResourceFile.BookPath, heading.SourceOffset);

        bool accepted = await HeadingSelectorWindow.RunAsync(this, selector);
        if (accepted)
        {
            await vm.CompleteGenerateTocAsync(selector.BookChanged);
        }
        else
        {
            vm.RewindCheckpoint();
        }
    }

    private async void OnEditTocRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        EditTocViewModel? editor = await vm.CreateEditTocViewModelAsync();
        if (editor is null)
        {
            return;
        }

        bool accepted = await EditTocWindow.RunAsync(this, editor);
        if (accepted)
        {
            vm.CompleteEditToc();
        }
        else
        {
            vm.RewindCheckpoint();
        }
    }

    private async void OnMetadataEditorRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        MetadataEditorViewModel? editor = vm.CreateMetadataEditorViewModel();
        if (editor is null)
        {
            return;
        }

        bool accepted = await MetadataEditorWindow.RunAsync(this, editor);
        if (accepted)
        {
            vm.CompleteMetadataEditor();
        }
    }

    private const string PreferencesWindowGeometryKey = "PreferencesWindow";

    // The open Preferences window (only a non-modal one can be asked for again while it is open).
    private PreferencesWindow? _preferencesWindow;

    private async void OnPreferencesRequested(object? sender, EventArgs e)
    {
        IServiceProvider? services = App.Services;
        if (services is null)
        {
            return;
        }

        if (_preferencesWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }

        SettingsStore settings = services.GetRequiredService<SettingsStore>();
        PreferencesViewModel preferences = services.GetRequiredService<PreferencesViewModel>();
        preferences.AttachEditionPageHost(_boundViewModel);
        PreferencesWindow window = new()
        {
            DataContext = preferences,
        };

        // Only the size is remembered — the dialog still opens centered over the main window.
        if (settings.GetWindowGeometry(PreferencesWindowGeometryKey) is { Width: > 0, Height: > 0 } geometry)
        {
            window.Width = Math.Max(geometry.Width, window.MinWidth);
            window.Height = Math.Max(geometry.Height, window.MinHeight);
        }

        // The window opens on the page it was closed on, with the field heights and pane sizes the user set.
        window.SelectedPageIndex = settings.PreferencesPageIndex;
        window.FieldHeights = settings.PreferencesFieldHeights;
        window.SplitterSizes = settings.PreferencesSplitterSizes;
        window.CollapsedSections = settings.PreferencesCollapsedSections;
        EditorZoom.Delta = settings.PreferencesEditorZoom;

        window.Closing += (_, _) =>
        {
            settings.SetWindowGeometry(PreferencesWindowGeometryKey, new WindowGeometry(
                0, 0, (int)window.Width, (int)window.Height, Maximized: false, FullScreen: false));
            settings.PreferencesPageIndex = window.SelectedPageIndex;
            settings.PreferencesFieldHeights = window.FieldHeights;
            settings.PreferencesSplitterSizes = window.SplitterSizes;
            settings.PreferencesCollapsedSections = window.CollapsedSections;
            settings.PreferencesEditorZoom = EditorZoom.Delta;
            settings.Save();
        };

        // A changed letter style offers to rewrite the shared footnotes of the book written in another one.
        Signet.Core.Footnotes.FootnoteLetterStyle letters = settings.FootnoteLetterStyle;

        // Save commits the changes (the language, theme etc. switch live then); refresh what depends on them.
        preferences.Applied += (_, _) => _boundViewModel?.ApplyPreferencesChanges();

        // Every setting changed while the dialog is open goes to the debug log as "before → after".
        using (DebugLog.TrackChanges(preferences, "Preferences"))
        {
            await ShowPreferencesAsync(window, settings.PreferencesWindowModal);
        }

        // User dictionary operations happen at once, even when the window is then cancelled.
        _boundViewModel?.ApplyPreferencesChanges();
        if (settings.FootnoteLetterStyle != letters && _boundViewModel is { } vm && vm.CountFootnoteLetterMismatches() is > 0 and var count
            && await ConfirmWindow.AskAsync(
                this,
                Strings.Get("FootnoteLettersWindow_Title"),
                Strings.Format("FootnoteLetters_OfferAfterPreferences", count, Strings.Get("FootnoteLetterStyle_" + settings.FootnoteLetterStyle)),
                Strings.Get("FootnoteLetters_OfferOpen")))
        {
            OnFootnoteLettersRequested(this, EventArgs.Empty);
        }
    }

    // Shows the Preferences window and waits until it is closed: modal (the main window is blocked meanwhile), or — when
    // the user turned that off — as a normal window over the main window.
    private async Task ShowPreferencesAsync(PreferencesWindow window, bool modal)
    {
        if (modal)
        {
            await window.ShowDialog(this);
            return;
        }

        TaskCompletionSource closed = new();
        window.Closed += (_, _) => closed.TrySetResult();
        _preferencesWindow = window;
        window.Show(this);
        await closed.Task.ConfigureAwait(true);
        _preferencesWindow = null;
    }

    /// <summary>Handles "Unify Letters of Shared Footnotes…": shows the modal dialog (or only a status message).</summary>
    private async void OnFootnoteLettersRequested(object? sender, EventArgs e)
    {
        if (_boundViewModel is not { } vm)
        {
            return;
        }

        if (vm.PrepareFootnoteLetters(ProgressWindow.DelayedRunner(this)) is { } letters)
        {
            // Kept in sync for as long as the dialog is open: another book operation can start elsewhere (e.g. a
            // keyboard shortcut caught just before a menu item disables) while this one is still showing.
            letters.IsBookOperationBusy = vm.IsBookOperationBusy;
            PropertyChangedEventHandler busyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsBookOperationBusy))
                {
                    letters.IsBookOperationBusy = vm.IsBookOperationBusy;
                }
            };
            vm.PropertyChanged += busyChanged;
            try
            {
                await FootnoteLettersWindow.ShowAsync(this, letters);
            }
            finally
            {
                vm.PropertyChanged -= busyChanged;
            }
        }
    }

    /// <summary>
    /// Handles "Change Footnote Placement…": lists the footnotes of Signet (a progress window while it takes long) and shows the
    /// modal dialog; it moves the checked footnotes and closes. The progress of the change shows over the dialog.
    /// </summary>
    private async void OnFootnotePlacementRequested(object? sender, EventArgs e)
    {
        if (_boundViewModel is not { } vm)
        {
            return;
        }

        FootnotePlacementWindow? window = null;
        FootnotePlacementViewModel? placement = await ProgressWindow.RunAsync(
            this,
            Strings.Get("FootnotePlacementWindow_Title"),
            Strings.Get("FootnotePlacement_Analyzing"),
            analysisProgress => vm.PrepareFootnotePlacementAsync(
                (title, work, cancellable) => ProgressWindow.RunWithProgressAsync(
                    window ?? (Window)this, title, Strings.Get("Progress_Starting"), work, cancellable),
                analysisProgress));
        if (placement is not null)
        {
            window = new FootnotePlacementWindow { DataContext = placement };
            placement.IsBookOperationBusy = vm.IsBookOperationBusy;
            PropertyChangedEventHandler busyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsBookOperationBusy))
                {
                    placement.IsBookOperationBusy = vm.IsBookOperationBusy;
                }
            };
            vm.PropertyChanged += busyChanged;
            try
            {
                await window.ShowDialog(this);
            }
            finally
            {
                vm.PropertyChanged -= busyChanged;
            }
        }
    }

    private ReportsWindow? _reportsWindow;

    /// <summary>
    /// Opens (or refreshes and activates) the non-modal "Reports" window. We keep a single window
    /// instance — subsequent invocations of the action replace its <c>DataContext</c> with a freshly
    /// computed view model instead of opening a second window.
    /// </summary>
    private void OnReportsRequested(object? sender, EventArgs e)
    {
        ReportsViewModel? report = _boundViewModel?.CreateReportsViewModel();
        if (report is null)
        {
            return;
        }

        if (_reportsWindow is null || !_reportsWindow.IsVisible)
        {
            _reportsWindow = new ReportsWindow { DataContext = report };
            RememberPlacement(_reportsWindow);
            _reportsWindow.Show(this);
        }
        else
        {
            _reportsWindow.DataContext = report;
            _reportsWindow.Activate();
        }
    }

    private SpellcheckEditorWindow? _spellcheckEditorWindow;

    /// <summary>
    /// Opens (or refreshes and activates) the non-modal "Spellcheck Editor" window. As with
    /// <see cref="OnReportsRequested"/> we keep a single window instance; unlike Reports
    /// the view model is persistent too (the <c>DataContext</c> is set only once, on first open).
    /// </summary>
    private void OnSpellcheckEditorRequested(object? sender, EventArgs e)
    {
        SpellcheckEditorViewModel? editor = _boundViewModel?.OpenSpellcheckEditor();
        if (editor is null)
        {
            return;
        }

        if (_spellcheckEditorWindow is null || !_spellcheckEditorWindow.IsVisible)
        {
            _spellcheckEditorWindow = new SpellcheckEditorWindow { DataContext = editor };
            RememberPlacement(_spellcheckEditorWindow);
            _spellcheckEditorWindow.Show(this);
        }
        else
        {
            _spellcheckEditorWindow.Activate();
        }
    }

    /// <summary>
    /// Handles the "Cleanup" action: analyses the book and shows the modal Cleanup dialog with the preview of the
    /// changes; each of its tabs applies its own changes ("Clean") while the dialog stays open.
    /// </summary>
    private async void OnManageFontsRequested(object? sender, EventArgs e)
    {
        if (_boundViewModel is { } vm && vm.PrepareManageFonts() is { } fonts)
        {
            await ManageFontsWindow.ShowAsync(this, fonts, vm.BookBrowser);
        }
    }

    private async void OnCleanupRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        // "Clean" shows how far it is over the dialog.
        CleanupWindow? dialog = null;
        CleanupViewModel? cleanup = await vm.PrepareCleanupAsync(
            (title, work, cancellable) => ProgressWindow.RunWithProgressAsync(
                dialog ?? (Window)this, title, Strings.Get("Progress_Starting"), work, cancellable));
        if (cleanup is not null)
        {
            // "Clean" is off while a book operation runs (this dialog's own included).
            cleanup.IsBookOperationBusy = vm.IsBookOperationBusy;
            PropertyChangedEventHandler busyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsBookOperationBusy))
                {
                    cleanup.IsBookOperationBusy = vm.IsBookOperationBusy;
                }
            };
            vm.PropertyChanged += busyChanged;
            try
            {
                await CleanupWindow.ShowAsync(this, cleanup, window => dialog = window);
            }
            finally
            {
                vm.PropertyChanged -= busyChanged;
            }
        }
    }

    /// <summary>
    /// Handles "Convert Footnotes of Other Origin…": plans the conversion and shows the modal dialog; it converts the
    /// checked footnotes and stays open until it is closed. The question about converting without a checkpoint is
    /// asked over the dialog.
    /// </summary>
    // The height of the details of the "Convert Footnotes of Other Origin" dialog (SettingsStore.PaneSizes).
    private const string FootnoteConversionDetailsKey = "FootnoteConversion.details";

    private async void OnFootnoteConversionRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        // The analysis of a big book takes a while: it runs in the background, with a progress window when it lasts.
        FootnoteConversionWindow? window = null;
        FootnoteConversionViewModel? conversion = await ProgressWindow.RunAsync(
            this,
            Strings.Get("FootnoteConversionWindow_Title"),
            Strings.Get("FootnoteConversion_Analyzing"),
            analysisProgress => vm.PrepareFootnoteConversionAsync(
                () => FootnoteConversionWindow.ConfirmWithoutCheckpointAsync(window ?? (Window)this),
                // The conversion itself (tens of seconds for thousands of footnotes) shows how far it is over the dialog.
                (title, work, cancellable) => ProgressWindow.RunWithProgressAsync(
                    window ?? (Window)this, title, Strings.Get("FootnoteConversion_Progress_Preparing"), work, cancellable),
                analysisProgress));
        if (conversion is not null)
        {
            window = new FootnoteConversionWindow { DataContext = conversion };
            SettingsStore? settings = App.Services?.GetService<SettingsStore>();
            if (settings?.PaneSizes.GetValueOrDefault(FootnoteConversionDetailsKey) is > 0 and var height)
            {
                window.DetailsHeight = height;
            }

            window.Closing += (_, _) =>
            {
                if (settings is not null)
                {
                    Dictionary<string, int> sizes = new(settings.PaneSizes, StringComparer.Ordinal)
                    {
                        [FootnoteConversionDetailsKey] = (int)Math.Round(window.DetailsHeight),
                    };
                    settings.PaneSizes = sizes;
                    settings.Save();
                }
            };

            conversion.IsBookOperationBusy = vm.IsBookOperationBusy;
            PropertyChangedEventHandler busyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsBookOperationBusy))
                {
                    conversion.IsBookOperationBusy = vm.IsBookOperationBusy;
                }
            };
            vm.PropertyChanged += busyChanged;
            try
            {
                await window.ShowDialog(this);
            }
            finally
            {
                vm.PropertyChanged -= busyChanged;
            }
        }
    }

    /// <summary>
    /// Handles the "Standardize EPUB" action: checks the book and shows the modal dialog with the preview of the
    /// changes; its "Apply" runs the checked steps and closes it.
    /// </summary>
    private async void OnStandardizeRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        // "Apply" shows how far it is over the dialog.
        StandardizationWindow? dialog = null;
        StandardizationViewModel? standardization = await vm.PrepareStandardizationAsync(
            (title, work, cancellable) => ProgressWindow.RunWithProgressAsync(
                dialog ?? (Window)this, title, Strings.Get("Progress_Starting"), work, cancellable));
        if (standardization is not null)
        {
            // "Apply" is off while a book operation runs (this dialog's own included).
            standardization.IsBookOperationBusy = vm.IsBookOperationBusy;
            PropertyChangedEventHandler busyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsBookOperationBusy))
                {
                    standardization.IsBookOperationBusy = vm.IsBookOperationBusy;
                }
            };
            vm.PropertyChanged += busyChanged;
            try
            {
                await StandardizationWindow.ShowAsync(this, standardization, window => dialog = window);
            }
            finally
            {
                vm.PropertyChanged -= busyChanged;
            }
        }
    }

    /// <summary>
    /// Handles the "Restructure Epub to Signet Norm" action: asks for confirmation (the operation
    /// is irreversible) and, once confirmed, calls <see cref="MainWindowViewModel.ApplyStandardizeEpubAsync"/>.
    /// </summary>
    private async void OnStandardizeEpubRequested(object? sender, EventArgs e)
    {
        MainWindowViewModel? vm = _boundViewModel;
        if (vm is null)
        {
            return;
        }

        bool confirmed = await ConfirmWindow.AskAsync(
            this,
            "Signet",
            Strings.Get("MainWindow_RestructureConfirm"));
        if (!confirmed)
        {
            return;
        }

        await vm.ApplyStandardizeEpubAsync();
    }

    private void OnCustomizeToolbarsRequested(object? sender, EventArgs e) => ShowToolbarCustomizeWindow(null);

    private void OnCustomizeToolbarRequested(object? sender, ToolbarId toolbar) => ShowToolbarCustomizeWindow(toolbar);

    private void ShowToolbarCustomizeWindow(ToolbarId? selected)
    {
        IServiceProvider? services = App.Services;
        if (services is null)
        {
            return;
        }

        ToolbarCustomizeViewModel customize = services.GetRequiredService<ToolbarCustomizeViewModel>();
        if (selected is { } toolbar)
        {
            customize.SelectedToolbar = toolbar;
        }

        ToolbarCustomizeWindow window = new() { DataContext = customize };
        _ = window.ShowDialog(this);
    }
}
