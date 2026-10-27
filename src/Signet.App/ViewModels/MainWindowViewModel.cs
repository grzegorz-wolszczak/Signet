using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.Toolbars;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Diff;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Semantics;
using Signet.Core.Spellcheck;
using Signet.Core.Toc;
using Signet.Core;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the main window: the menu, configurable toolbars, the layout of
/// dockable panels (Dock.Avalonia), the status bar and the registration of keyboard shortcuts.
/// An action without a handler shows a message on the status bar when executed.
/// </summary>
public sealed partial class MainWindowViewModel
    : ViewModelBase, IBookWorkspace, IEditionPageHost, IRecentFilesMenu, IBookmarksMenu, IMultiFileSearchHost, ICodeTabHost, ILanguageAware,
        IDisposable
{
    private readonly MenuBuilder _menuBuilder;

    // The folder of the designer preview's settings and of the files kept beside them (clips, saved searches, navigation
    // history, book locks, dictionaries); deleted with the view model. Null in the application.
    private readonly string? _designTimeFolder;
    private bool _disposed;

    private readonly TableOfContentsViewModel _toc = new();
    private readonly FindReplaceViewModel _findReplace;
    private readonly SearchEditorViewModel _searchEditor;
    private readonly ClipsViewModel _clips;
    private readonly SpellcheckEditorViewModel _spellcheckEditor;
    private readonly ValidationResultsViewModel _validationResults = new();
    private readonly FindUsagesViewModel _findUsages;
    private readonly List<Bookmark> _bookmarks = new();
    private bool _previewHasZoomFocus;
    private bool _isFindReplaceVisible;
    private Func<string, bool> _externalFileOpener = path => ExternalOpen.TryOpen(path, out _);
    private event EventHandler? BookmarksChangedInternal;

    /// <summary>Settings group holding panel visibility.</summary>
    public const string DockPanelsGroup = "dock_panels";

    /// <summary>Settings group holding the slid-out sizes of "Auto Hide" panels.</summary>
    public const string DockPinnedSizesGroup = "dock_pinned_sizes";

    /// <summary>Settings group of the floating panel windows (<see cref="MainDockFactory.CaptureFloatingPanels"/>).</summary>
    public const string DockFloatingGroup = "dock_floating";

    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly AppActionRegistry _actions;
    private readonly ToolbarManager _toolbarManager;
    private readonly MainDockFactory _dockFactory;
    private readonly NotificationsViewModel _notifications;
    private readonly IStatusBarService _statusBar;
    private readonly SettingsStore _settings;
    private readonly TabManager _tabManager;
    private readonly PreviewViewModel _preview;
    private readonly SpellChecker _spellChecker;
    private readonly ClipboardHistoryService _clipboardHistory;
    private readonly MissingDoctypeGuard _doctypeGuard;

    private Book? _currentBook;
    private string _windowTitle = ApplicationInfo.Name;
    private CodeTabViewModel? _activeCaretTab;
    private FileWorkflow? _fileWorkflow;
    private bool _startupHandled;
    private bool _preserveHeadingAttributes;

    // "Format" menu actions — active only for a Code Editor tab with (X)HTML.
    private static readonly string[] FormatActionIds =
    {
        AppActionIds.Bold, AppActionIds.Italic, AppActionIds.Underline, AppActionIds.Strikethrough,
        AppActionIds.Subscript, AppActionIds.Superscript,
        AppActionIds.AlignLeft, AppActionIds.AlignCenter, AppActionIds.AlignRight, AppActionIds.AlignJustify,
        AppActionIds.InsertBulletedList, AppActionIds.InsertNumberedList,
        AppActionIds.IncreaseIndent, AppActionIds.DecreaseIndent,
        AppActionIds.TextDirectionLtr, AppActionIds.TextDirectionRtl, AppActionIds.TextDirectionDefault,
        AppActionIds.Heading1, AppActionIds.Heading2, AppActionIds.Heading3,
        AppActionIds.Heading4, AppActionIds.Heading5, AppActionIds.Heading6, AppActionIds.HeadingNormal,
        AppActionIds.HeadingPreserveAttributes,
        AppActionIds.CasingLowercase, AppActionIds.CasingUppercase,
        AppActionIds.CasingTitlecase, AppActionIds.CasingCapitalize,
        AppActionIds.InsertClosingTag,
    };

    private static readonly (string ActionId, string Element)[] HeadingActions =
    {
        (AppActionIds.Heading1, "h1"), (AppActionIds.Heading2, "h2"), (AppActionIds.Heading3, "h3"),
        (AppActionIds.Heading4, "h4"), (AppActionIds.Heading5, "h5"), (AppActionIds.Heading6, "h6"),
        (AppActionIds.HeadingNormal, "p"),
    };

    /// <summary>Parameterless constructor — solely for the designer preview (XAML previewer).</summary>
    public MainWindowViewModel()
        : this(DesignTimeBundle.Create(), NullLogger<MainWindowViewModel>.Instance)
    {
    }

    private MainWindowViewModel(DesignTimeBundle b, ILogger<MainWindowViewModel> logger)
        : this(logger, b.Actions, b.Shortcuts,
            b.Toolbars, b.DockFactory, b.StatusBar, b.Settings, b.BookBrowser, b.Tabs, b.Preview, b.SpellChecker,
            b.ClipboardHistory, b.DoctypeGuard)
    {
        _designTimeFolder = b.Folder;
    }

    /// <summary>Constructor injected by DI.</summary>
    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        AppActionRegistry actions,
        KeyboardShortcutManager shortcuts,
        ToolbarManager toolbarManager,
        MainDockFactory dockFactory,
        IStatusBarService statusBar,
        SettingsStore settings,
        BookBrowserViewModel bookBrowser,
        TabManager tabManager,
        PreviewViewModel preview,
        SpellChecker spellChecker,
        ClipboardHistoryService clipboardHistory,
        MissingDoctypeGuard doctypeGuard)
    {
        _logger = logger;
        _actions = actions;
        Shortcuts = shortcuts;
        _toolbarManager = toolbarManager;
        _dockFactory = dockFactory;
        _statusBar = statusBar;
        _settings = settings;
        _tabManager = tabManager;
        _preview = preview;
        _spellChecker = spellChecker;
        _clipboardHistory = clipboardHistory;
        _doctypeGuard = doctypeGuard;
        BookBrowser = bookBrowser;
        BookBrowser.SkippedFilesReported += (_, report) => RaiseSkippedFilesReport(report);

        _dockFactory.TableOfContents = _toc;
        _toc.EntryActivated += (_, entry) => NavigateToToc(entry);
        _toc.EditRequested += (_, _) => EditTocRequested?.Invoke(this, EventArgs.Empty);

        _findReplace = new FindReplaceViewModel(settings, statusBar, () => ActiveCodeTab, this);

        // The panel is not dockable — it appears at the bottom and can only be closed;
        // its visibility is remembered.
        _isFindReplaceVisible = settings.GetFindReplaceSettings().PanelVisible;

        string savedSearchPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(settings.FilePath) ?? AppDirectories.PrefsDirectory,
            SavedSearchStore.DefaultFileName);

        // Its own instance (not _findReplace) for the Add/Edit view: editing a search must not change what is
        // typed in the real Find & Replace panel until "Load Search" says so — the two panels share the book (the
        // same host) but not their Find/Replace fields.
        FindReplaceViewModel searchEditorFindReplace = new(settings, statusBar, () => ActiveCodeTab, this);
        _searchEditor = new SearchEditorViewModel(new SavedSearchStore(savedSearchPath), settings, _findReplace, searchEditorFindReplace);

        string clipsPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(settings.FilePath) ?? AppDirectories.PrefsDirectory,
            ClipStore.DefaultFileName);
        _clips = new ClipsViewModel(new ClipStore(clipsPath), () => ActiveCodeTab, () => _currentBook, statusBar);
        _clips.ClipsChanged += (_, _) => RefreshClipActions();
        _tabManager.CodeTabHost = this;
        _tabManager.AnyTabModifiedChanged += (_, _) => RefreshTitle();
        _dockFactory.Clips = _clips;

        _dockFactory.ValidationResults = _validationResults;
        _validationResults.EntryActivated += (_, result) => NavigateToValidationResult(result);
        _validationResults.FixRequested += (_, results) => RunAsyncAction(() => FixValidationResultsAsync(results));
        _validationResults.SkipRuleRequested += (_, code) => SkipValidationRule(code);
        _validationResults.RestoreRulesRequested += (_, codes) => RunAsyncAction(() => RestoreValidationRulesAsync(codes));
        _validationResults.SetSkippedRules(_settings.CheckBookSkippedRules);

        _findUsages = new FindUsagesViewModel(_settings);
        _dockFactory.FindUsages = _findUsages;
        _findUsages.UsageActivated += (_, usage) =>
        {
            // A match of a text search is selected, as the multi-file Find Next does; a class usage gets the caret.
            if (usage.Length > 0)
            {
                ((IMultiFileSearchHost)this).OpenResourceAtMatch(usage.BookPath, usage.Offset, usage.Offset + usage.Length);
            }
            else
            {
                NavigateToBookPathAtOffset(usage.BookPath, usage.Offset);
            }
        };
        _findUsages.RefreshRequested += (_, _) =>
        {
            if (_findUsages.ClassName is { } className)
            {
                FindClassUsages(className);
            }
        };

        _spellcheckEditor = new SpellcheckEditorViewModel(_spellChecker, _settings);
        _spellcheckEditor.NavigationRequested += NavigateToBookPathAtOffset;
        _spellcheckEditor.ChangeAllRequested += (word, lang, newWord) => RunAsyncAction(() => ApplySpellcheckChangeAllAsync(word, lang, newWord));
        _spellcheckEditor.DictionaryStateChanged += (_, _) => RefreshOpenTabsSpellcheck();

        InitCheckpoints();

        // The Spellcheck Editor, the Checkpoints panel and the Validation Results panel are single persistent
        // instances (see their class remarks) that can stay open/visible while another book operation starts
        // elsewhere, so they are kept in sync for as long as this view model lives, not only at creation.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBookOperationBusy))
            {
                _spellcheckEditor.IsBookOperationBusy = IsBookOperationBusy;
                CheckpointsPanel.IsBookOperationBusy = IsBookOperationBusy;
                _validationResults.IsBookOperationBusy = IsBookOperationBusy;
            }
        };

        _notifications = new NotificationsViewModel(statusBar, _settings);
        InitNotifications();

        StartScreen = new StartScreenViewModel(
            _actions.Require(AppActionIds.Open), _actions.Require(AppActionIds.NewDefault),
            () => _settings.RecentFiles.Take(_settings.StartScreenRecentFilesLimit).ToList(),
            path => ((IRecentFilesMenu)this).Open(path), path => ((IRecentFilesMenu)this).Remove(path), _settings);
        Changed += (_, _) => StartScreen.Refresh();
        _dockFactory.DocumentsEmptyContent = StartScreen;

        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);
        UpdateBookOpenState();
        // RestoreDockLayout() is disabled: restoring a saved layout could degenerate the
        // assignment/proportions of the regions (e.g. the document area disappears entirely) —
        // so we always start with the default layout from CreateLayout/InitLayout.
        // Only the widths of Book Browser and Preview are restored (a safe subset).
        RestoreSideRegionWidths();
        RestorePanelVisibility();
        WirePanelActions();
        WireNotificationsActions();
        MarkCheckableFormatActions();
        if (_actions.Get(AppActionIds.WordWrap) is { } wordWrapAction)
        {
            wordWrapAction.IsCheckable = true;
            wordWrapAction.IsChecked = _settings.CodeEditorWordWrap;
        }

        _menuBuilder = new(_actions, _toolbarManager, CustomizeToolbarsCommand, this, this);
        MenuItems = _menuBuilder.Build();

        Toolbars = new ObservableCollection<ToolbarViewModel>(
            ToolbarManager.AllToolbars.Select(id => new ToolbarViewModel(id, _toolbarManager, _actions)));
        foreach (ToolbarViewModel toolbar in Toolbars)
        {
            toolbar.EditRequested += (_, _) => CustomizeToolbarRequested?.Invoke(this, toolbar.Id);
        }

        TopToolbars = new ToolbarAreaViewModel(ToolbarArea.Top, _toolbarManager, Toolbars);
        BottomToolbars = new ToolbarAreaViewModel(ToolbarArea.Bottom, _toolbarManager, Toolbars);
        Strings.RegisterLanguageAware(this);
        _toolbarManager.Changed += (_, id) =>
        {
            ToolbarViewModel? vm = Toolbars.FirstOrDefault(t => t.Id == id);
            vm?.Refresh();
        };

        _statusBar.PropertyChanged += OnStatusBarPropertyChanged;

        // Book Browser actions are excluded from the window shortcuts — their KeyBindings are created by
        // the panel itself, so that they work only while the file tree has focus (see BookBrowserView).
        // No filter on Gesture: an action without a default shortcut may get one in Preferences.
        // Code editor actions too: CodeTabView binds them, so that they work only while a code editor has focus.
        ShortcutActions = _actions.Actions
            .Where(x => !AppActionIds.IsPanelCategory(x.Category) && !AppActionIds.IsCodeEditorAction(x.Id))
            .ToList();
        CodeEditorShortcutActions = _actions.Actions.Where(x => AppActionIds.IsCodeEditorAction(x.Id)).ToList();
        BookBrowser.ShortcutActions = _actions.Actions.Where(IsBookBrowserAction).ToList();
        _clips.ShortcutActions = _actions.Actions
            .Where(x => string.Equals(x.Category, AppActionIds.ClipEditorCategory, StringComparison.Ordinal))
            .ToList();

        _actions.SetHandler(AppActionIds.NewDefault, () => RunFileAction(f => f.NewAsync(null)));
        _actions.SetHandler(AppActionIds.NewEpub2, () => RunFileAction(f => f.NewAsync("2.0")));
        _actions.SetHandler(AppActionIds.NewEpub3, () => RunFileAction(f => f.NewAsync("3.0")));
        _actions.SetHandler(AppActionIds.Open, () => RunFileAction(f => f.OpenAsync()));
        _actions.SetHandler(AppActionIds.Save, () => RunFileAction(async f => RefreshFindUsagesAfterSave(await f.SaveAsync().ConfigureAwait(true))));
        _actions.SetHandler(AppActionIds.SaveAs, () => RunFileAction(async f => RefreshFindUsagesAfterSave(await f.SaveAsAsync().ConfigureAwait(true))));
        _actions.SetHandler(AppActionIds.SaveACopy, () => RunFileAction(f => f.SaveACopyAsync()));
        _actions.SetHandler(AppActionIds.CustomLayout, () => RunFileAction(f => f.NewWithCustomLayoutAsync(null)));
        _actions.SetHandler(AppActionIds.NextTab, _tabManager.NextTab);
        _actions.SetHandler(AppActionIds.PreviousTab, _tabManager.PreviousTab);
        _actions.SetHandler(AppActionIds.Close, () => RunFileAction(f => f.CloseAsync()));
        _actions.SetHandler(AppActionIds.CloseTab, _tabManager.CloseActiveTab);
        _actions.SetHandler(AppActionIds.CloseOtherTabs, _tabManager.CloseOtherTabs);
        _actions.SetHandler(AppActionIds.Exit, () => CloseWindowRequested?.Invoke(this, EventArgs.Empty));

        // Code Editor actions — routed to the active tab.
        _actions.SetHandler(AppActionIds.Undo, () => ActiveTab?.Undo());
        _actions.SetHandler(AppActionIds.Redo, () => ActiveTab?.Redo());
        _actions.SetHandler(AppActionIds.GoToLine, () => GoToLineRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.About, () => AboutRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.ZoomIn, () => AdjustActiveZoom(1.1));
        _actions.SetHandler(AppActionIds.ZoomOut, () => AdjustActiveZoom(1 / 1.1));
        _actions.SetHandler(AppActionIds.ZoomReset, ResetActiveZoom);
        _actions.SetHandler(AppActionIds.InsertSgfSectionMarker, () => ActiveCodeTab?.InsertSectionMarkerAtCaret());
        _actions.SetHandler(AppActionIds.SplitSection, () => RunAsyncAction(SplitFileAtCaretAsync));
        _actions.SetHandler(AppActionIds.SplitOnSgfSectionMarkers, () => RunAsyncAction(SplitCurrentFileAtMarkersAsync));
        _actions.SetHandler(
            AppActionIds.PasteClipboardHistory,
            () => PasteClipboardHistoryRequested?.Invoke(this, EventArgs.Empty));

        WireFormatActions();
        WireInsertActions();
        WireClipActions();
        WireBookBrowserActions();
        _actions.SetHandler(AppActionIds.ClipEditorRename, () =>
        {
            if (_clips.RenameSelectedCommand.CanExecute(null))
            {
                _clips.RenameSelectedCommand.Execute(null);
            }
        });
        WireCheckpointActions();

        // Navigation: bookmarks, Navigate Back / Forward.
        _actions.SetHandler(AppActionIds.BookmarkLocation, AddBookmark);
        _actions.SetHandler(AppActionIds.GoToLinkOrStyle, GoToLinkOrStyle);
        WireNavigationActions();
        _actions.SetHandler(AppActionIds.JumpToOpeningTag, () => ActiveCodeTab?.JumpToOpeningTag());
        _actions.SetHandler(AppActionIds.JumpToClosingTag, () => ActiveCodeTab?.JumpToClosingTag());
        _actions.SetHandler(AppActionIds.SelectTagContents, () => ActiveCodeTab?.SelectTagContents());
        _actions.SetHandler(AppActionIds.SplitTag, () => ActiveCodeTab?.SplitTag());
        _actions.SetHandler(AppActionIds.MergeContent, RequestMergeContent);
        _actions.SetHandler(AppActionIds.UnwrapDiv, RequestUnwrapDiv);
        _actions.SetHandler(AppActionIds.RenameTag, () =>
        {
            if (ActiveCodeTab is { } tab)
            {
                RenameTagRequested?.Invoke(this, tab.EnclosingTagName ?? string.Empty);
            }
        });

        // Find & Replace in the current file.
        _actions.SetHandler(AppActionIds.Find, ShowFindPanel);
        _actions.SetHandler(AppActionIds.HideFind, HideFindPanel);
        _actions.SetHandler(AppActionIds.FindNext, () => _findReplace.FindNext());
        _actions.SetHandler(AppActionIds.FindPrevious, () => _findReplace.FindPrevious());
        _actions.SetHandler(AppActionIds.ReplaceCurrent, () => _findReplace.Replace());
        _actions.SetHandler(AppActionIds.ReplaceNext, () => _findReplace.ReplaceFind());
        _actions.SetHandler(AppActionIds.ReplacePrevious, () => _findReplace.ReplaceFind());
        _actions.SetHandler(AppActionIds.ReplaceAll, () => RunAsyncAction(_findReplace.ReplaceAllAsync));
        _actions.SetHandler(AppActionIds.Count, () => _findReplace.Count());
        _actions.SetHandler(AppActionIds.MarkSelection, () => _findReplace.ToggleMarkSelection());
        _actions.SetHandler(AppActionIds.FindNextInFile, () => _findReplace.InCurrentFile(p => p.FindNext()));
        _actions.SetHandler(AppActionIds.ReplaceNextInFile, () => _findReplace.InCurrentFile(p => p.ReplaceFind()));
        _actions.SetHandler(AppActionIds.ReplaceAllInFile, () => RunAsyncAction(() => _findReplace.InCurrentFileAsync(p => p.ReplaceAllAsync())));
        _actions.SetHandler(AppActionIds.CountInFile, () => _findReplace.InCurrentFile(p => p.Count()));

        // Dry Run Replace All / Filter Replacements.
        _actions.SetHandler(
            AppActionIds.DryRunReplaceAll, () => DryRunReplaceRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(
            AppActionIds.FilterReplaceAll, () => FilterReplacementsRequested?.Invoke(this, EventArgs.Empty));

        // Semantics/Guide/Cover/Nav-in-spine actions.
        _actions.SetHandler(AppActionIds.AddCover, () => AddCoverRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.AddNavToSpine, () => AddNavToSpine(nonlinear: false));
        _actions.SetHandler(AppActionIds.AddNavToSpineNonLinear, () => AddNavToSpine(nonlinear: true));
        _actions.SetHandler(AppActionIds.RemoveNavFromSpine, RemoveNavFromSpine);

        // Generate / Create HTML Table Of Contents.
        _actions.SetHandler(AppActionIds.GenerateToc, () => GenerateTocRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.CreateHtmlToc, () => RunAsyncAction(CreateHtmlTocAsync));
        _actions.SetHandler(
            AppActionIds.EditToc,
            () => EditTocRequested?.Invoke(this, EventArgs.Empty));

        // Metadata Editor.
        _actions.SetHandler(
            AppActionIds.MetaEditor,
            () => MetadataEditorRequested?.Invoke(this, EventArgs.Empty));

        // Preferences.
        _actions.SetHandler(
            AppActionIds.Preferences,
            () => PreferencesRequested?.Invoke(this, EventArgs.Empty));

        // Reports.
        _actions.SetHandler(AppActionIds.Reports, () => ReportsRequested?.Invoke(this, EventArgs.Empty));

        // Delete Unused Media Files / Delete Unused Stylesheet Selectors.
        _actions.SetHandler(AppActionIds.Cleanup, () => CleanupRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.ManageFonts, RequestManageFonts);
        _actions.SetHandler(AppActionIds.Standardize, () => StandardizeRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.LiveCssPanel, () => LiveCssPanelRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.RenumberFootnotes, () => RunAsyncAction(() => ((ICodeTabHost)this).RenumberFootnotes()));
        _actions.SetHandler(AppActionIds.ConvertFootnotes, () => ((ICodeTabHost)this).ShowFootnoteConversion());
        _actions.SetHandler(AppActionIds.UnifyFootnoteLetters, () => ((ICodeTabHost)this).ShowFootnoteLetters());
        _actions.SetHandler(AppActionIds.ChangeFootnotePlacement, () => FootnotePlacementRequested?.Invoke(this, EventArgs.Empty));

        // Reformat HTML / Restructure Epub to Signet Norm / Use Standard File Extensions /
        // Rebase Manifest IDs.
        _actions.SetHandler(AppActionIds.MendPrettifyHtml, () => RunAsyncAction(MendPrettifyHtmlAsync));
        _actions.SetHandler(AppActionIds.MendHtml, () => RunAsyncAction(MendHtmlAsync));
        _actions.SetHandler(AppActionIds.PrettifyCurrentHtml, () => RunAsyncAction(() => ActiveCodeTab?.ReformatHtmlAsync(toValid: false) ?? Task.CompletedTask));
        _actions.SetHandler(AppActionIds.MendCurrentHtml, () => RunAsyncAction(MendCurrentFileAsync));
        _actions.SetHandler(AppActionIds.PrettifyCurrentCss, () => ActiveCodeTab?.ReformatCss(multipleLineFormat: true));
        _actions.SetHandler(AppActionIds.PrettifyAllCss, () => RunAsyncAction(PrettifyAllCssAsync));
        _actions.SetHandler(AppActionIds.AddSoftHyphens, () => RunAsyncAction(AddSoftHyphensAsync));
        _actions.SetHandler(AppActionIds.RemoveSoftHyphens, () => RunAsyncAction(RemoveSoftHyphensAsync));
        _actions.SetHandler(AppActionIds.StandardizeEpub, () => StandardizeEpubRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.UseStandardFileExtensions, () => RunAsyncAction(UseStandardFileExtensionsAsync));
        _actions.SetHandler(AppActionIds.RebaseManifestIds, () => RunAsyncAction(RebaseManifestIdsAsync));

        // Spellcheck in Code Editor: Highlight toggle, Next Misspelled Word (F4), Add/Ignore,
        // Clear Ignored Words.
        _actions.SetHandler(AppActionIds.AutoSpellCheck, ToggleAutoSpellCheck);
        _actions.SetHandler(AppActionIds.WordWrap, ToggleWordWrap);
        _actions.SetHandler(AppActionIds.Spellcheck, () => ActiveCodeTab?.GoToNextMisspelledWord());
        _actions.SetHandler(AppActionIds.AddMisspelledWord, () => ActiveCodeTab?.AddCurrentMisspelledWordToDictionary());
        _actions.SetHandler(AppActionIds.IgnoreMisspelledWord, () => ActiveCodeTab?.IgnoreCurrentMisspelledWord());
        _actions.SetHandler(AppActionIds.ClearIgnoredWords, ClearIgnoredWords);
        if (_actions.Get(AppActionIds.AutoSpellCheck) is { } autoSpellCheckAction)
        {
            autoSpellCheckAction.IsChecked = _settings.SpellCheck;
        }

        RefreshSpellcheckActionState();

        // Spellcheck Editor.
        _actions.SetHandler(AppActionIds.SpellcheckEditor, () => SpellcheckEditorRequested?.Invoke(this, EventArgs.Empty));

        // Well-Formed Check EPUB, Validate Stylesheets With W3C, Epub3 Tools.
        _actions.SetHandler(AppActionIds.WellFormedCheckEpub, () => RunAsyncAction(WellFormedCheckEpubAsync));
        _actions.SetHandler(AppActionIds.ValidateStylesheetsWithW3C, ValidateStylesheetsWithW3C);
        _actions.SetHandler(AppActionIds.UpgradeToEpub3, () => RunAsyncAction(UpgradeToEpub3Async));
        _actions.SetHandler(AppActionIds.NcxGuideFromNav, GenerateNcxGuideFromNav);
        _actions.SetHandler(AppActionIds.RemoveNcxGuide, RemoveNcxGuideFromEpub3);
        _actions.SetHandler(AppActionIds.UpdateManifestMediaTypes, UpdateManifestMediaTypes);
        _actions.SetHandler(AppActionIds.UpdateManifestProperties, UpdateManifestProperties);

        // Print / Print Preview of the active resource through the native WebView API.
        _actions.SetHandler(AppActionIds.Print, () => _preview.RequestPrint());
        _actions.SetHandler(AppActionIds.PrintPreview, () => _preview.RequestPrintPreview());
        _preview.AttachExternalFileOpener(path => _externalFileOpener(path));
        _preview.AttachWorkingTextProvider(_tabManager.ModifiedCodeTexts);
        // The "live" preview delay from Preferences (uiPreviewTimeout) — a change takes effect immediately.
        // The same goes for highlighting the caret position in the preview (PreviewHighlight).
        _preview.DebounceInterval = TimeSpan.FromMilliseconds(_settings.UiPreviewTimeout);
        _preview.Highlight = _settings.PreviewHighlight;
        _preview.Fonts = _settings.PreviewAppearance;
        // The preview's own zoom, remembered across restarts (saved with the rest of the settings on exit).
        _preview.SetZoom(_settings.ZoomPreview);
        ZoomHint.Enabled = _settings.ShowZoomHint;
        _settings.SettingChanged += (_, e) =>
        {
            if (e.QualifiedKey.EndsWith("/show_zoom_hint", StringComparison.Ordinal))
            {
                ZoomHint.Enabled = _settings.ShowZoomHint;
            }
            else if (e.QualifiedKey.EndsWith("/window_title_full_path", StringComparison.Ordinal))
            {
                RefreshTitle();
            }
            else if (e.QualifiedKey.EndsWith("/ui_preview_timeout", StringComparison.Ordinal))
            {
                _preview.DebounceInterval = TimeSpan.FromMilliseconds(_settings.UiPreviewTimeout);
            }
            else if (e.QualifiedKey.EndsWith("/preview_highlight", StringComparison.Ordinal))
            {
                _preview.Highlight = _settings.PreviewHighlight;
            }
            else if (e.QualifiedKey.EndsWith("/preview_appearance", StringComparison.Ordinal))
            {
                _preview.Fonts = _settings.PreviewAppearance;
            }
            else if (e.QualifiedKey.EndsWith("/code_editor_multiline_tabs", StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(MultilineTabs));
            }
        };

        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _preview.CodeCaretJumpRequested += OnPreviewCodeCaretJumpRequested;
        _preview.PropertyChanged += OnPreviewPropertyChanged;
        BookBrowser.OpenResourceRequested += OnOpenResourceRequested;
        BookBrowser.TocChanged += (_, _) => ((IBookWorkspace)this).RefreshAfterBookEdit();
        BookBrowser.ReloadOpenTabs = ReloadOpenTabsAndPreview;
        BookBrowser.NavigateTo = NavigateToBookPathAtOffset;
        BookBrowser.ManageFontsRequested += (_, _) => RequestManageFonts();
        BookBrowser.ValidateWithW3CRequested += OnValidateSelectedCssWithW3C;
    }

    /// <summary>View model of the Book Browser panel (the resource tree of the current publication).</summary>
    public BookBrowserViewModel BookBrowser { get; }

    /// <summary>View model of the "Find &amp; Replace" panel.</summary>
    public FindReplaceViewModel FindReplace => _findReplace;

    /// <summary>View model of the preview panel — for tests of the Print/Print Preview action binding.</summary>
    public PreviewViewModel Preview => _preview;

    /// <summary>Request: show the Find &amp; Replace panel and focus the Find field.</summary>
    public event EventHandler? FindPanelFocusRequested;

    /// <summary>
    /// Raised by the "Dry Run Replace All" action — the view opens a non-modal window
    /// with a preview of all matches and proposed replacements.
    /// </summary>
    public event EventHandler? DryRunReplaceRequested;

    /// <summary>
    /// Raised by the "Filter Replacements" action — the view opens a modal window
    /// with a checkbox per match; "Apply" applies only the checked ones.
    /// </summary>
    public event EventHandler? FilterReplacementsRequested;

    /// <summary>Manager of the open document tabs.</summary>
    public TabManager Tabs => _tabManager;

    /// <summary>The active document tab, or <c>null</c>.</summary>
    public ContentTabViewModel? ActiveTab => _tabManager.ActiveTab;

    /// <summary>Whether the document tabs that do not fit in a row go to several rows (Preferences › Code Editor).</summary>
    public bool MultilineTabs => _settings.CodeEditorMultilineTabs;

    /// <summary>The active tab, if it is a code editor.</summary>
    public CodeTabViewModel? ActiveCodeTab => _tabManager.ActiveTab as CodeTabViewModel;

    /// <summary>Caret position of the active code editor for the status bar (empty when none).</summary>
    public string CaretStatus => _activeCaretTab?.CaretStatus ?? string.Empty;

    /// <summary>
    /// Additional status of the active tab for the status bar — the rule counter for CSS;
    /// empty for the other kinds of tabs.
    /// </summary>
    public string TabStatus => _activeCaretTab?.SecondaryStatus ?? string.Empty;

    /// <summary>
    /// Zoom of the active document tab (Code Editor) as "NNN%" — for the status bar. The preview has its own zoom
    /// bar in its panel (<see cref="PreviewViewModel.ZoomPercentText"/>).
    /// </summary>
    public string ZoomPercentText => $"{Math.Round((ActiveTab?.ZoomFactor ?? 1.0) * 100)}%";

    /// <summary>Zoom In of the active document tab (the "+" button in the status bar).</summary>
    [RelayCommand]
    private void ZoomIn() => AdjustTabZoom(1.1);

    /// <summary>Zoom Out of the active document tab (the "−" button in the status bar).</summary>
    [RelayCommand]
    private void ZoomOut() => AdjustTabZoom(1 / 1.1);

    /// <summary>Zoom Reset of the active document tab (the button with the percentage in the status bar).</summary>
    [RelayCommand]
    private void ZoomReset()
    {
        if (ActiveTab is { } tab)
        {
            tab.ZoomFactor = 1.0;
        }
    }

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewViewModel.ZoomFactor))
        {
            _settings.ZoomPreview = (float)_preview.ZoomFactor;
        }
    }

    /// <summary>Raised by the "Go To Line" action — the view shows the field and calls <see cref="GoToLine"/>.</summary>
    public event EventHandler? GoToLineRequested;

    /// <summary>Raised by the "About" action — the view shows the About window.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>
    /// "Rename Tag" — the view asks for a new name (argument: the current name of the enclosing element)
    /// and calls <see cref="RenameEnclosingTag"/>.
    /// </summary>
    public event EventHandler<string>? RenameTagRequested;

    /// <summary>Renames the element enclosing the caret in the active Code Editor tab.</summary>
    public void RenameEnclosingTag(string newName) => ActiveCodeTab?.RenameTag(newName);

    /// <summary>
    /// "Merge Content" — merging requires a warning (different attributes, text or comments
    /// between the elements): the view asks for confirmation and calls
    /// <see cref="ConfirmMergeContent"/>.
    /// </summary>
    public event EventHandler<ElementMergeCandidate>? MergeContentConfirmationRequested;

    /// <summary>Merges the selected elements in the active Code Editor tab (after the warning is confirmed).</summary>
    public void ConfirmMergeContent() => ActiveCodeTab?.MergeContent();

    /// <summary>Text of the "Merge Content" warning — a section for each reason, separated by a blank line.</summary>
    public static string MergeContentWarning(ElementMergeCandidate candidate)
    {
        List<string> sections = new();
        if (candidate.AttributesDiffer)
        {
            sections.Add(Strings.Format(
                "MergeContent_AttributesWarning",
                candidate.ElementName,
                candidate.FirstOpenTag,
                string.Join(Environment.NewLine, candidate.DifferingOpenTags)));
        }

        if (candidate.TextBetween.Count > 0)
        {
            sections.Add(Strings.Format(
                "MergeContent_TextBetweenWarning", string.Join(Environment.NewLine, candidate.TextBetween)));
        }

        if (candidate.RemovedNodes.Count > 0)
        {
            sections.Add(Strings.Format(
                "MergeContent_RemovedNodesWarning", string.Join(Environment.NewLine, candidate.RemovedNodes)));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    /// <summary>
    /// "Unwrap div" — the div has an <c>id</c> (a link target, perhaps): the view asks for confirmation (the argument
    /// is the id) and calls <see cref="ConfirmUnwrapDiv"/>.
    /// </summary>
    public event EventHandler<string>? UnwrapDivConfirmationRequested;

    /// <summary>Unwraps the div around the caret in the active Code Editor tab (after the warning is confirmed).</summary>
    public void ConfirmUnwrapDiv() => ActiveCodeTab?.UnwrapDiv();

    private void RequestUnwrapDiv()
    {
        if (ActiveCodeTab is not { } tab)
        {
            return;
        }

        DivUnwrapCheck check = tab.CheckUnwrapDiv();
        if (check.Div is not { } div)
        {
            _statusBar.ShowMessage(
                CodeTabViewModel.UnwrapDivProblemText(check.Problem) ?? string.Empty, TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }
        else if (div.Id is { } id)
        {
            UnwrapDivConfirmationRequested?.Invoke(this, id);
        }
        else
        {
            tab.UnwrapDiv();
        }
    }

    private void RequestMergeContent()
    {
        if (ActiveCodeTab is not { MergeCandidate: { } candidate } tab)
        {
            return;
        }

        if (candidate.NeedsConfirmation)
        {
            MergeContentConfirmationRequested?.Invoke(this, candidate);
        }
        else
        {
            tab.MergeContent();
        }
    }

    /// <summary>
    /// Raised by the "Add Cover" action — the view shows an image picker and, if needed, a confirmation
    /// of overwriting the existing cover, then calls <see cref="ApplyAddCoverAsync"/>.
    /// </summary>
    public event EventHandler? AddCoverRequested;

    /// <summary>
    /// Raised by the "Generate Table Of Contents" action — the view opens a modal heading
    /// selection dialog (<see cref="HeadingSelectorViewModel"/>) and, once accepted, calls
    /// <see cref="CompleteGenerateTocAsync"/>.
    /// </summary>
    public event EventHandler? GenerateTocRequested;

    /// <summary>
    /// Raised by the "Edit Table Of Contents" action — the view opens the modal dialog
    /// <see cref="EditTocViewModel"/> and, once accepted, calls <see cref="CompleteEditToc"/>.
    /// </summary>
    public event EventHandler? EditTocRequested;

    /// <summary>
    /// Raised by the "Metadata Editor" action — the view opens the modal dialog
    /// <see cref="MetadataEditorViewModel"/> and, once accepted, calls <see cref="CompleteMetadataEditor"/>.
    /// </summary>
    public event EventHandler? MetadataEditorRequested;

    /// <summary>
    /// Raised by the "Preferences" action (F5) — the view opens a modal window with the DI-resolved
    /// <c>PreferencesViewModel</c> (Save / Cancel — changes take effect only when saved).
    /// </summary>
    public event EventHandler? PreferencesRequested;

    /// <summary>
    /// Raised by the "Quit" action (Ctrl+Q, <c>MainWindow.Exit</c>) — the view calls
    /// <c>Window.Close()</c>, which runs the standard <c>OnClosing</c> path asking
    /// about saving unsaved changes (the same as when closing with the X button).
    /// </summary>
    public event EventHandler? CloseWindowRequested;

    /// <summary>
    /// Raised by the "Reports" action — the view shows a non-modal, reusable
    /// <see cref="ReportsViewModel"/> window (a single instance, data refreshed on
    /// every open).
    /// </summary>
    public event EventHandler? ReportsRequested;

    /// <summary>
    /// Raised by the "Spellcheck Editor" action — the view shows a non-modal,
    /// reusable <see cref="SpellcheckEditorViewModel"/> window (a single persistent view model
    /// instance and a single window instance, like <see cref="ReportsRequested"/>).
    /// </summary>
    public event EventHandler? SpellcheckEditorRequested;

    /// <summary>
    /// Raised by the "Cleanup" action — the view calls <see cref="PrepareCleanupAsync"/>, shows the modal Cleanup
    /// dialog and, once confirmed, calls <see cref="ApplyCleanupAsync"/>.
    /// </summary>
    public event EventHandler? CleanupRequested;

    /// <summary>
    /// Raised by "Manage Fonts" (the Tools menu or the fonts menu of the Book Browser) — the view shows the dialog for
    /// <see cref="PrepareManageFonts"/>.
    /// </summary>
    public event EventHandler? ManageFontsRequested;

    /// <summary>The "Manage Fonts" dialog for the open book, or <c>null</c> without a book.</summary>
    public ManageFontsViewModel? PrepareManageFonts() =>
        _currentBook is null ? null : new ManageFontsViewModel(BookBrowser, NavigateToBookPathAtOffset);

    private void RequestManageFonts()
    {
        if (_currentBook is not null)
        {
            ManageFontsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised by the "Standardize EPUB" action — the view calls <see cref="PrepareStandardizationAsync"/> and shows the
    /// modal dialog, whose "Apply" calls <see cref="ApplyStandardizationAsync"/>.
    /// </summary>
    public event EventHandler? StandardizeRequested;

    /// <summary>
    /// Raised by the "Live CSS Panel" action — the view calls <see cref="TryResolveLiveCssPanel"/> for the
    /// element under the caret of the active Code Editor tab and opens/refreshes the non-modal panel window.
    /// </summary>
    public event EventHandler? LiveCssPanelRequested;

    /// <summary>
    /// Raised whenever the input of the Live CSS Panel may have changed — the caret moved or the content
    /// was edited in the active Code Editor tab, or another tab became active. The view refreshes an open
    /// panel (debounced) by calling <see cref="TryResolveLiveCssPanel"/> with <c>reportFailure: false</c>.
    /// </summary>
    public event EventHandler? LiveCssContextChanged;

    /// <summary>
    /// Raised by the "Saved Searches" action — the view opens (or activates) the non-modal window
    /// of the saved searches editor.
    /// </summary>
    public event EventHandler? SearchEditorRequested;

    /// <summary>View model of the "Saved Searches" window.</summary>
    public SearchEditorViewModel SearchEditor => _searchEditor;

    /// <summary>
    /// Raised by the "Restructure Epub to Signet Norm" action — the view asks for
    /// confirmation (the operation is irreversible) and, once confirmed, calls <see cref="ApplyStandardizeEpubAsync"/>.
    /// </summary>
    public event EventHandler? StandardizeEpubRequested;

    /// <summary>
    /// Raised by the "Insert &#8594; Special Character" action — the view shows a character
    /// picker and calls <see cref="InsertSpecialCharacter"/>.
    /// </summary>
    public event EventHandler? InsertSpecialCharacterRequested;

    /// <summary>
    /// Raised by the "Edit &#8594; Paste From Clipboard History" action — the view shows a
    /// window for choosing a clipboard history entry and calls <see cref="PasteClipboardHistoryEntry"/>.
    /// </summary>
    public event EventHandler? PasteClipboardHistoryRequested;

    /// <summary>
    /// Raised by the "Insert &#8594; File" action — the view shows a resource picker
    /// (with a "from disk" option) and calls <see cref="InsertMediaResources"/>.
    /// </summary>
    public event EventHandler? InsertFileRequested;

    /// <summary>
    /// Raised by the "Insert &#8594; ID" action — the view shows a dialog with the list of
    /// existing identifiers and calls <see cref="ApplyInsertId"/>.
    /// </summary>
    public event EventHandler? InsertIdRequested;

    /// <summary>
    /// Raised by the "Insert &#8594; Link" action — the view shows a target picker dialog
    /// and calls <see cref="ApplyInsertHyperlink"/>.
    /// </summary>
    public event EventHandler? InsertHyperlinkRequested;

    /// <summary>
    /// Raised by the "Insert &#8594; Aria Clip" action — the view shows the list of ARIA
    /// clips (<see cref="GetAriaClipOptions"/>), optionally a role picker dialog
    /// (<see cref="AriaClipRequiresRole"/>/<see cref="GetAriaRoleOptions"/>) and calls
    /// <see cref="ApplyAriaClip"/>.
    /// </summary>
    public event EventHandler? InsertAriaClipRequested;

    /// <summary>
    /// Raised by the "Insert Clip From Library" action (see
    /// <see cref="AppActionIds.SelectClip"/>) — the view shows <c>SelectClipWindow</c> with the list
    /// <see cref="GetLeafClipPickerItems"/> and calls <see cref="ApplySelectedClip"/>.
    /// </summary>
    public event EventHandler? SelectClipRequested;

    /// <summary>
    /// Raised by the "Clip Editor" action — the view shows a non-modal, reusable
    /// <c>ClipEditorWindow</c> bound to <see cref="Clips"/> (the same pattern as
    /// <see cref="SpellcheckEditorRequested"/>, but without refreshing data on open — a single,
    /// always-current view model shared with the docked "Clips" panel).
    /// </summary>
    public event EventHandler? ClipEditorRequested;

    /// <summary>Shortcut manager (for the view — to build <c>KeyBinding</c>s and for the shortcut editor).</summary>
    public KeyboardShortcutManager Shortcuts { get; }

    /// <summary>Action registry (for the view and for layers that attach implementations).</summary>
    public AppActionRegistry Actions => _actions;

    /// <summary>Layout of the dockable panels.</summary>
    public IRootDock Layout { get; }

    /// <summary>Dock factory (for the <c>DockControl</c> — drag operations at runtime).</summary>
    public Dock.Model.Core.IFactory DockFactory => _dockFactory;

    /// <summary>The main menu tree.</summary>
    public IReadOnlyList<MenuItemViewModel> MenuItems { get; }

    /// <summary>
    /// Builds the entries of the "Recent Files" list of the menu again — the window calls it whenever a menu is
    /// opened, so that a file removed from disk meanwhile is shown as missing.
    /// </summary>
    public void RefreshRecentFilesMenu() => _menuBuilder.RefreshRecentFiles();

    /// <summary>Configurable toolbars.</summary>
    public ObservableCollection<ToolbarViewModel> Toolbars { get; }

    /// <summary>The toolbar rows under the menu.</summary>
    public ToolbarAreaViewModel TopToolbars { get; }

    /// <summary>The toolbar rows above the status bar.</summary>
    public ToolbarAreaViewModel BottomToolbars { get; }

    /// <summary>
    /// Actions whose shortcuts are the window's <c>KeyBinding</c>s (all except the panel and code editor ones, including
    /// those without a current shortcut — the window rebuilds the bindings when <see cref="AppAction.Gesture"/> changes).
    /// </summary>
    public IReadOnlyList<AppAction> ShortcutActions { get; }

    /// <inheritdoc/>
    public IReadOnlyList<AppAction> CodeEditorShortcutActions { get; }

    /// <summary>Window title: <c>name[*] - epubN - Signet</c>.</summary>
    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    /// <summary>The File menu workflow (New/Open/Save/…); created once the view attaches the dialogs.</summary>
    public FileWorkflow? FileWorkflow => _fileWorkflow;

    /// <summary>
    /// Attaches the File menu dialog handling (from the view) and creates <see cref="FileWorkflow"/>.
    /// Called once, after the window's <c>DataContext</c> is set. A stub can be passed in tests.
    /// </summary>
    public void AttachFileWorkflowPrompts(IFileWorkflowPrompts prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        if (_fileWorkflow is not null)
        {
            return;
        }

        _fileWorkflow = new FileWorkflow(_settings, prompts, this, _logger, BookFileLocks.BesideSettings(_settings));
        _fileWorkflow.CurrentFileChanged += (_, _) => RefreshTitle();
        _fileWorkflow.RecentFilesChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        // Changing the limit in Preferences rebuilds the File menu list immediately.
        _settings.SettingChanged += (_, e) =>
        {
            if (e.QualifiedKey.EndsWith("/recent_files_limit", StringComparison.Ordinal))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        Changed?.Invoke(this, EventArgs.Empty);
        RefreshTitle();
    }

    /// <summary>
    /// File given on the command line (<see cref="StartupArguments.FileToOpen"/>), opened at
    /// startup instead of the most recently used publication; <c>null</c> — none.
    /// </summary>
    public string? StartupFilePath { get; set; }

    /// <summary>
    /// Runs at startup: opens the file from the command line or restores the last publication; otherwise the window
    /// starts without a book (the start screen). Called once from the view.
    /// </summary>
    public async Task RunStartupAsync()
    {
        if (_startupHandled || _fileWorkflow is null)
        {
            return;
        }

        _startupHandled = true;

        try
        {
            if (StartupFilePath is { } requested)
            {
                // A nonexistent or unsupported file: the message from LoadFileAsync, and no book.
                await _fileWorkflow.LoadFileAsync(requested).ConfigureAwait(true);
            }
            else
            {
                // Not when the application was closed without a book (after File → Close).
                string? last = _settings.ReopenLastFileOnStartup && _settings.BookOpenAtExit
                    ? _settings.RecentFiles.FirstOrDefault(System.IO.File.Exists)
                    : null;

                if (last is not null)
                {
                    await _fileWorkflow.LoadFileAsync(last).ConfigureAwait(true);
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing the publication at startup");
            _statusBar.ShowMessage(Strings.Get("Status_StartupBookFailed"), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }

        RefreshTitle();
    }

    // -------------------------------------------------- IMultiFileSearchHost --- //

    /// <inheritdoc />
    bool IMultiFileSearchHost.HasBook => _currentBook is not null;

    /// <inheritdoc />
    void IMultiFileSearchHost.FlushOpenTabs() => _tabManager.SaveAllTabs();

    /// <inheritdoc />
    IReadOnlyList<TextResource> IMultiFileSearchHost.ResolveLookWhere(LookWhere lookWhere)
    {
        Book? book = _currentBook;
        if (book is null)
        {
            return Array.Empty<TextResource>();
        }

        return lookWhere switch
        {
            LookWhere.AllHtmlFiles => Cast(book.GetHtmlResources()),
            LookWhere.AllCssFiles => Cast(book.GetCssResources()),
            LookWhere.OpfFile => new List<TextResource> { book.GetOpf() },
            LookWhere.NcxFile => book.GetNcx() is { } ncx
                ? new List<TextResource> { ncx }
                : Array.Empty<TextResource>(),
            LookWhere.TabbedHtmlFiles => TabbedTextResources<HtmlResource>(),
            LookWhere.TabbedCssFiles => TabbedTextResources<CssResource>(),
            LookWhere.SelectedHtmlFiles => ValidSelectedOfType<HtmlResource>(),
            LookWhere.SelectedCssFiles => ValidSelectedOfType<CssResource>(),
            LookWhere.SelectedSvgFiles => ValidSelectedOfType<SvgResource>(),
            LookWhere.SelectedJsFiles => ValidSelectedByMediaType(FindReplaceViewModel.JavascriptMediaTypes),
            LookWhere.SelectedMiscXmlFiles => ValidSelectedByMediaType(FindReplaceViewModel.MiscXmlMediaTypes),
            _ => Array.Empty<TextResource>(),
        };

        static IReadOnlyList<TextResource> Cast<T>(IReadOnlyList<T> source)
            where T : TextResource => source.Cast<TextResource>().ToList();
    }

    /// <inheritdoc />
    CodeTabViewModel? IMultiFileSearchHost.FindOpenTab(TextResource resource)
    {
        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            if (view is CodeTabViewModel code && ReferenceEquals(code.Resource, resource))
            {
                return code;
            }
        }

        return null;
    }

    /// <inheritdoc />
    void IMultiFileSearchHost.ShowTextUsages(
        Func<string> header, IReadOnlyList<ClassUsage> usages, Func<IReadOnlyList<ClassUsage>> rerun)
    {
        _findUsages.LoadText(header, usages, rerun);
        ShowFindUsagesTool();
    }

    /// <inheritdoc />
    void IMultiFileSearchHost.OpenResourceAtMatch(string bookPath, int startOffset, int endOffset)
    {
        Resource? resource = _currentBook?
            .GetAllResources()
            .FirstOrDefault(r => string.Equals(r.BookPath, bookPath, StringComparison.Ordinal));
        if (resource is null)
        {
            return;
        }

        _tabManager.OpenResource(resource);

        if (ActiveCodeTab is { } tab &&
            string.Equals(tab.ResourceBookPath, bookPath, StringComparison.Ordinal))
        {
            tab.SelectMatch(startOffset, endOffset);
        }
    }

    private List<TextResource> TabbedTextResources<T>()
        where T : TextResource
    {
        var result = new List<TextResource>();
        foreach (ContentTabViewModel view in _tabManager.OrderedTabViews)
        {
            if (view.Resource is T typed)
            {
                result.Add(typed);
            }
        }

        return result;
    }

    // All selected resources must be of the same type, otherwise the result is empty.
    private List<TextResource> ValidSelectedOfType<T>()
        where T : TextResource
    {
        IReadOnlyList<Resource> selected = BookBrowser.SelectedResources;
        if (selected.Count == 0 || selected.Any(r => r is not T))
        {
            return new List<TextResource>();
        }

        return selected.Cast<TextResource>().ToList();
    }

    private List<TextResource> ValidSelectedByMediaType(IReadOnlyCollection<string> mediaTypes)
    {
        IReadOnlyList<Resource> selected = BookBrowser.SelectedResources;
        if (selected.Count == 0 ||
            selected.Any(r => r is not TextResource || !mediaTypes.Contains(r.MediaType)))
        {
            return new List<TextResource>();
        }

        return selected.Cast<TextResource>().ToList();
    }

    // ------------------------------------------------------- IBookWorkspace --- //

    /// <inheritdoc />
    Book? IBookWorkspace.CurrentBook => _currentBook;

    /// <inheritdoc />
    bool IBookWorkspace.HasUnsavedChanges =>
        (_currentBook?.Modified ?? false) || _tabManager.AnyTabModified;

    /// <inheritdoc />
    void IBookWorkspace.SaveOpenTabs()
    {
        _tabManager.SaveAllTabs();
        _preview.Refresh();
        _toc.Refresh();
    }

    // ----------------------------------------------------- IEditionPageHost --- //

    /// <inheritdoc />
    bool IEditionPageHost.HasOpenBook => _currentBook is not null;

    /// <inheritdoc />
    string? IEditionPageHost.EditionPageBookPath =>
        _currentBook is null ? null : EditionPage.Find(_currentBook)?.BookPath;

    /// <inheritdoc />
    async Task<bool> IEditionPageHost.RemoveEditionPageAsync()
    {
        if (_currentBook is not { } book)
        {
            return false;
        }

        if (!TryEnterBookOperation())
        {
            return false;
        }

        try
        {
            _tabManager.SaveAllTabs();
            string title = Strings.Get("CheckpointOp_RemoveEditionPage");
            (bool cancelled, bool checkpoint) = await AddCheckpointOrCancelAsync(title).ConfigureAwait(true);
            if (cancelled)
            {
                return false;
            }

            bool removed = await RunInBackgroundAsync(
                title, DelayedProgressRunner, editsBook: true, cancellable: false, (_, _) => EditionPage.Remove(book)).ConfigureAwait(true);
            if (!removed)
            {
                if (checkpoint)
                {
                    RewindCheckpoint();
                }

                return false;
            }

            ((IBookWorkspace)this).RefreshAfterBookEdit();
            _statusBar.ShowMessage(Strings.Get("Status_EditionPageRemoved"), TimeSpan.FromSeconds(4));
            return true;
        }
        finally
        {
            ExitBookOperation();
        }
    }

    /// <inheritdoc />
    void IBookWorkspace.RefreshAfterBookEdit()
    {
        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            view.Reload();
        }

        BookBrowser.Refresh();
        _preview.Refresh();
        _toc.Refresh();
    }

    /// <inheritdoc />
    void IBookWorkspace.ApplyBook(Book book, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(book);

        // The previous book's history is kept (Preferences) before the switch; closing and opening tabs is no jump.
        SaveNavigationHistory();
        using IDisposable navigationSuspended = Tracker.Suspend();
        _tabManager.SetBook(null);

        if (_currentBook is not null)
        {
            _currentBook.ModifiedStateChanged -= OnBookModifiedStateChanged;
            _currentBook.Dispose();
        }

        _currentBook = book;
        _currentBook.ModifiedStateChanged += OnBookModifiedStateChanged;

        // TabManager and Preview get the book before Book Browser: BookBrowser.SetBook immediately
        // opens the first HTML file — and ActiveTabChanged shows it in the preview only when the
        // preview already knows the book.
        _tabManager.SetBook(book);
        _preview.SetBook(book);
        _findUsages.Clear();
        BookBrowser.SetBook(book);
        _toc.SetBook(book);
        _tabManager.RestoreSession(_settings);
        StartCheckpointHistory(book);
        LoadNavigationHistory(sourcePath);
        UpdateBookOpenState();
        RefreshTitle();

        // With the full path of the file: books of the same name may come from different folders.
        bool fromFile = !string.IsNullOrEmpty(sourcePath);
        _statusBar.ShowMessage(
            (book.LoadWarnings.Count > 0, fromFile) switch
            {
                (true, true) => Strings.Format("Status_FileLoadedWithWarnings", book.LoadWarnings.Count, sourcePath),
                (true, false) => Strings.Format("Status_BookLoadedWithWarnings", book.LoadWarnings.Count),
                (false, true) => Strings.Format("Status_FileLoaded", sourcePath),
                _ => Strings.Get("Status_BookLoaded"),
            },
            TimeSpan.FromSeconds(book.LoadWarnings.Count > 0 ? 6 : 3),
            book.LoadWarnings.Count > 0 ? NotificationLevel.Warning : NotificationLevel.Info);
    }

    /// <inheritdoc />
    void IBookWorkspace.CloseBook()
    {
        if (_currentBook is null)
        {
            return;
        }

        // The session (open tabs) and the navigation history of the closed book are kept for when it is opened again.
        SaveNavigationHistory();
        _tabManager.SaveAllTabs();
        _tabManager.CaptureSession(_settings);
        using (Tracker.Suspend())
        {
            _tabManager.SetBook(null);
        }

        _currentBook.ModifiedStateChanged -= OnBookModifiedStateChanged;
        _currentBook.Dispose();
        _currentBook = null;
        _legacyFileName = null;

        _preview.Clear();
        _preview.SetBook(null);
        _findUsages.Clear();
        BookBrowser.SetBook(null);
        _toc.SetBook(null);
        _validationResults.ClearResults();
        _checkpoints.Clear();
        RefreshCheckpointActions();
        _navigationBookPath = null;
        _navigation.Clear();
        WatchNavigationFolderKeeper();
        UpdateNavigationPlace();
        _bookmarks.Clear();
        BookmarksChangedInternal?.Invoke(this, EventArgs.Empty);
        UpdateBookOpenState();
        RefreshTitle();
        _statusBar.ShowMessage(Strings.Get("Status_BookClosed"), TimeSpan.FromSeconds(3));
    }

    /// <summary>The start screen of the empty document area (shown while no book is open).</summary>
    public StartScreenViewModel StartScreen { get; }

    /// <summary>Whether a book is open — without one only the actions that make sense then are enabled.</summary>
    public bool HasBook
    {
        get => _hasBook;
        private set => SetProperty(ref _hasBook, value);
    }

    private bool _hasBook;

    // The actions, the panels and the start screen follow whether a book is open.
    private void UpdateBookOpenState()
    {
        bool open = _currentBook is not null;
        HasBook = open;
        _actions.SetBookOpen(open);
        _dockFactory.SetBookOpen(open);
        StartScreen.IsVisible = !open;
        _searchEditor.RefreshBookAvailability();
    }

    // ------------------------------------------------------ IRecentFilesMenu --- //

    /// <inheritdoc />
    IReadOnlyList<string> IRecentFilesMenu.RecentFiles =>
        _settings.RecentFiles.Take(_settings.RecentFilesLimit).ToList();

    /// <inheritdoc />
    event EventHandler? IRecentFilesMenu.Changed
    {
        add => Changed += value;
        remove => Changed -= value;
    }

    /// <inheritdoc />
    void IRecentFilesMenu.Open(string path) => RunFileAction(f => f.OpenRecentAsync(path));

    /// <inheritdoc />
    void IRecentFilesMenu.Remove(string path)
    {
        _settings.RecentFiles = _settings.RecentFiles
            .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    void IRecentFilesMenu.ClearAll()
    {
        _settings.RecentFiles = System.Array.Empty<string>();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private event EventHandler? Changed;

    // ---------------------------------------------------- IBookmarksMenu --- //

    /// <inheritdoc />
    IReadOnlyList<Bookmark> IBookmarksMenu.Bookmarks => _bookmarks;

    /// <inheritdoc />
    event EventHandler? IBookmarksMenu.BookmarksChanged
    {
        add => BookmarksChangedInternal += value;
        remove => BookmarksChangedInternal -= value;
    }

    /// <inheritdoc />
    void IBookmarksMenu.GoToBookmark(Bookmark bookmark) => GoToBookmark(bookmark);

    /// <inheritdoc />
    void IBookmarksMenu.ClearBookmarks() => ClearBookmarks();

    /// <summary>Session bookmarks (for tests and preview) — in the order they were added.</summary>
    public IReadOnlyList<Bookmark> Bookmarks => _bookmarks;

    /// <summary>View model of the "Table Of Contents" panel.</summary>
    public TableOfContentsViewModel TableOfContents => _toc;

    /// <summary>View model of the "Validation Results" panel.</summary>
    public ValidationResultsViewModel ValidationResults => _validationResults;

    /// <summary>
    /// Substitutes opening files in the system application ("Validate Stylesheets With
    /// W3C" opens the generated page in the default browser). By default
    /// <see cref="ExternalOpen.TryOpen(string, out string?)"/>; tests substitute a fake
    /// so as not to launch a real browser.
    /// </summary>
    public void AttachExternalFileOpener(Func<string, bool> opener) => _externalFileOpener = opener;

    /// <summary>
    /// "Bookmark Location" (Ctrl+Alt+B) — appends the current Code Editor position as a bookmark
    /// at the end of the list in the Window menu.
    /// </summary>
    private void AddBookmark()
    {
        if (ActiveCodeTab is not { } tab)
        {
            _statusBar.ShowMessage(Strings.Get("Status_BookmarkOnlyInCodeEditor"), TimeSpan.FromSeconds(4));
            return;
        }

        string filename = System.IO.Path.GetFileName(tab.ResourceBookPath);
        Bookmark mark = new(tab.ResourceBookPath, $"{filename}:{tab.CaretLine}", tab.CaretLine, tab.CaretOffset);
        _bookmarks.Add(mark);
        BookmarksChangedInternal?.Invoke(this, EventArgs.Empty);
        _statusBar.ShowMessage(Strings.Format("Status_BookmarkAdded", mark.Name), TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// "Go To Link Or Style" (F3) — on a link opens its target, on a class name — the CSS rule. The jump enters the
    /// Navigate Back / Forward history like any other.
    /// </summary>
    private void GoToLinkOrStyle()
    {
        if (ActiveCodeTab is { } tab && !tab.GoToLinkOrStyleAtCaret())
        {
            _statusBar.ShowMessage(Strings.Get("Status_CaretNotOnLinkOrClass"), TimeSpan.FromSeconds(4));
        }
    }

    private void GoToBookmark(Bookmark bookmark)
    {
        if (_currentBook is null)
        {
            return;
        }

        Resource? resource = _currentBook.GetAllResources()
            .FirstOrDefault(r => string.Equals(r.BookPath, bookmark.BookPath, StringComparison.Ordinal));
        if (resource is null)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NavigationFileGone"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            _bookmarks.Remove(bookmark);
            BookmarksChangedInternal?.Invoke(this, EventArgs.Empty);
            return;
        }

        _tabManager.OpenResource(resource);
        if (ActiveCodeTab is { } tab)
        {
            tab.GoToOffset(bookmark.Offset);
        }
    }

    private void ClearBookmarks()
    {
        if (_bookmarks.Count == 0)
        {
            return;
        }

        _bookmarks.Clear();
        BookmarksChangedInternal?.Invoke(this, EventArgs.Empty);
        _statusBar.ShowMessage(Strings.Get("Status_BookmarksCleared"), TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Navigation from the "Table Of Contents" panel: opens the target file in Code Editor,
    /// scrolls to the fragment and synchronizes the Preview panel.
    /// </summary>
    private void NavigateToToc(Signet.Core.Toc.TocDisplayEntry entry)
    {
        if (_currentBook is null || entry.TargetBookPath.Length == 0)
        {
            return;
        }

        Resource? resource = _currentBook.GetAllResources()
            .FirstOrDefault(r => string.Equals(r.BookPath, entry.TargetBookPath, StringComparison.Ordinal));
        if (resource is null)
        {
            _statusBar.ShowMessage(Strings.Format("Status_TocTargetMissing", entry.TargetBookPath), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _tabManager.OpenResource(resource);
        if (ActiveCodeTab is { } tab)
        {
            tab.GoToFragment(entry.Fragment);
            _preview.SyncCaretToPreview(tab.CaretOffset);
        }
    }

    /// <summary>
    /// The "Find" action (Ctrl+F): shows the Find &amp; Replace panel, puts the text selected in the
    /// editor into the Find field and raises a request to focus it.
    /// </summary>
    private void ShowFindPanel()
    {
        IsFindReplaceVisible = true;
        _findReplace.SeedFindFromSelection();
        FindPanelFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the Find &amp; Replace panel (non-dockable, at the bottom of the window) is visible.</summary>
    public bool IsFindReplaceVisible
    {
        get => _isFindReplaceVisible;
        set => SetProperty(ref _isFindReplaceVisible, value);
    }

    /// <summary>Closes the Find &amp; Replace panel (the X button, Esc, "Hide Find").</summary>
    [RelayCommand]
    private void HideFindPanel() => IsFindReplaceVisible = false;

    /// <summary>
    /// Convenience for tests: sets the publication and the title from the path.
    /// The preferred route is <see cref="FileWorkflow"/>.
    /// </summary>
    public void LoadBook(Book book, string sourcePath)
    {
        ((IBookWorkspace)this).ApplyBook(book, sourcePath);
        if (!string.IsNullOrEmpty(sourcePath))
        {
            _fileWorkflow?.AddRecentFile(sourcePath);
        }

        _legacyFileName = string.IsNullOrEmpty(sourcePath)
            ? null
            : System.IO.Path.GetFileName(sourcePath);
        _legacyFilePath = string.IsNullOrEmpty(sourcePath) ? null : sourcePath;
        RefreshTitle();
    }

    private string? _legacyFileName;

    private string? _legacyFilePath;

    private string? _bookFileName;

    private string? _bookFileTooltip;

    /// <summary>The file name of the open book for the status bar; <c>null</c> without a book.</summary>
    public string? BookFileName
    {
        get => _bookFileName;
        private set => SetProperty(ref _bookFileName, value);
    }

    /// <summary>
    /// The tooltip of <see cref="BookFileName"/>: the full path of the book file (two books may have the same name), or
    /// that the book has not been saved yet.
    /// </summary>
    public string? BookFileTooltip
    {
        get => _bookFileTooltip;
        private set => SetProperty(ref _bookFileTooltip, value);
    }

    private void OnBookModifiedStateChanged(object? sender, bool modified)
    {
        if (_bookEditedInBackground)
        {
            // Raised off the UI thread; the title and the table of contents are refreshed when the work is done.
            return;
        }

        RefreshTitle();
        _toc.Refresh();
    }

    /// <summary>
    /// The title bar text: the application name alone without a book, otherwise the file name (or, with <paramref name="fullPath"/>,
    /// the full path; a book that was not saved yet has none) with <c>*</c> when modified, then the application name.
    /// </summary>
    public static string FormatWindowTitle(bool hasBook, string fileName, string path, bool modified, bool fullPath)
    {
        if (!hasBook)
        {
            return ApplicationInfo.Name;
        }

        string shown = fullPath && path.Length > 0 ? path : fileName;
        return $"{shown}{(modified ? "*" : string.Empty)} - {ApplicationInfo.Name}";
    }

    private void RefreshTitle()
    {
        string name = _fileWorkflow?.CurrentFileName
            ?? _legacyFileName
            ?? FileWorkflow.DefaultFileName;

        bool modified = (_currentBook?.Modified ?? false) || _tabManager.AnyTabModified;
        string path = _fileWorkflow is not null ? _fileWorkflow.CurrentFilePath : _legacyFilePath ?? string.Empty;

        WindowTitle = FormatWindowTitle(_currentBook is not null, name, path, modified, _settings.FullPathInTitle);

        BookFileName = _currentBook is null ? null : name;
        BookFileTooltip = _currentBook is null ? null
            : path.Length > 0 ? path
            : Strings.Get("MainWindow_BookFileNotSaved");

        // The "Save" floppy disk: gray without changes, red with unsaved changes (the action works
        // in both states).
        if (_actions.Get(AppActionIds.Save) is { } save)
        {
            save.IconState = modified ? ActionIconState.Attention : ActionIconState.Inactive;
        }
    }

    // Runs an asynchronous action handler (e.g. one that may show the missing DOCTYPE dialog) from a
    // synchronous action callback, logging errors instead of losing them.
    private async void RunAsyncAction(System.Func<Task> op)
    {
        _asyncActionsRunning++;
        try
        {
            await op().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action error");
            _statusBar.ShowMessage(Strings.Format("Status_FileOperationError", ex.Message), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }
        finally
        {
            ActionFinished();
        }
    }

    // One asynchronous action less to wait for (see WhenIdleAsync).
    private void ActionFinished()
    {
        if (--_asyncActionsRunning == 0)
        {
            TaskCompletionSource? idle = _asyncActionsIdle;
            _asyncActionsIdle = null;
            idle?.TrySetResult();
        }
    }

    // The asynchronous actions (RunAsyncAction, RunFileAction) that have not finished; they start and end on the UI thread.
    private int _asyncActionsRunning;
    private TaskCompletionSource? _asyncActionsIdle;

    /// <summary>
    /// A task that is done when every asynchronous action that was started (a long operation under a progress window, a dialog
    /// that asks first…) has finished — already done when none is running. For the code that has to wait for what an action
    /// started (the tests); call it on the thread the actions run on.
    /// </summary>
    public Task WhenIdleAsync()
    {
        if (_asyncActionsRunning == 0)
        {
            return Task.CompletedTask;
        }

        _asyncActionsIdle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _asyncActionsIdle.Task;
    }

    private async void RunFileAction(System.Func<FileWorkflow, Task> op)
    {
        if (_fileWorkflow is null)
        {
            _statusBar.ShowMessage(Strings.Get("Status_FileOperationsNotReady"), TimeSpan.FromSeconds(3), NotificationLevel.Warning);
            return;
        }

        _asyncActionsRunning++;
        try
        {
            await op(_fileWorkflow).ConfigureAwait(true);
            RefreshTitle();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File operation error");
            _statusBar.ShowMessage(Strings.Format("Status_FileOperationError", ex.Message), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }
        finally
        {
            ActionFinished();
        }
    }

    /// <summary>The current status bar message.</summary>
    public string StatusMessage => _statusBar.CurrentMessage;

    /// <summary>Raised when the user chooses "Customize Toolbars…" (the window is handled in the view).</summary>
    public event EventHandler? CustomizeToolbarsRequested;

    /// <summary>Raised when the user chooses "Edit toolbar" for one toolbar (the window opens with that toolbar selected).</summary>
    public event EventHandler<ToolbarId>? CustomizeToolbarRequested;

    /// <summary>Closes the floating panel windows together with the main window (otherwise the application keeps running).</summary>
    public void CloseFloatingPanels() => _dockFactory.CloseFloatingWindows();

    /// <summary>Floats the panels that floated when the application was closed (called once the main window is shown).</summary>
    public void RestoreFloatingPanels() => _dockFactory.ApplyFloatingPanels(_settings.GetStringMap(DockFloatingGroup));

    /// <summary>Saves panel visibility to the settings (called when the window closes).</summary>
    public void PersistState()
    {
        SaveNavigationHistory();
        _settings.BookOpenAtExit = _currentBook is not null;
        _settings.SetStringMap(DockPanelsGroup, _dockFactory.CaptureToolVisibility());
        _settings.SetStringMap(DockPinnedSizesGroup, _dockFactory.CapturePinnedSizes());
        _settings.SetStringMap(DockFloatingGroup, _dockFactory.CaptureFloatingPanels());
        _settings.MainWindowDockLayout = _dockFactory.CaptureLayoutState();
        if (_currentBook is not null)
        {
            // Without a book the session of the last closed one stays (CloseBook captured it).
            _tabManager.CaptureSession(_settings);
        }

        _findReplace.PersistState(IsFindReplaceVisible);
        _searchEditor.PersistIfModified();
        _clips.PersistIfModified();
        _settings.Save();
        Dispose();
    }

    /// <summary>
    /// Lets go of the book, the tabs, the preview and the other resources (<see cref="PersistState"/> does it after saving
    /// the state when the window closes). The designer preview's folder goes with it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_activeCaretTab is not null)
        {
            _activeCaretTab.PropertyChanged -= OnActiveTabPropertyChanged;
            _activeCaretTab.CaretOffsetChanged -= OnActiveCaretOffsetChanged;
            _activeCaretTab.ContentChanged -= OnActiveContentChanged;
            _activeCaretTab = null;
        }

        _tabManager.ActiveTabChanged -= OnActiveTabChanged;
        _preview.CodeCaretJumpRequested -= OnPreviewCodeCaretJumpRequested;
        _preview.PropertyChanged -= OnPreviewPropertyChanged;
        _tabManager.Dispose();
        _preview.Dispose();
        BookBrowser.Dispose();
        _currentBook?.Dispose();
        _currentBook = null;
        _checkpoints.Dispose();

        // The lock on the book file is held open in the locks folder, which may be the one deleted below.
        _fileWorkflow?.Dispose();
        if (_designTimeFolder is not null && System.IO.Directory.Exists(_designTimeFolder))
        {
            System.IO.Directory.Delete(_designTimeFolder, recursive: true);
        }
    }

    /// <summary>Shows a message on the status bar (used by the view, e.g. on a file open error).</summary>
    public void ShowStatus(string message) => _statusBar.ShowMessage(message, TimeSpan.FromSeconds(6));

    private void OnOpenResourceRequested(object? sender, IReadOnlyList<Core.Resources.Resource> resources) =>
        _tabManager.OpenResources(resources);

    /// <summary>Scrolls the active code editor to the given line (1-based).</summary>
    public void GoToLine(int line) => ActiveCodeTab?.GoToLine(line);

    /// <summary>
    /// Book path of the most recently inserted resource (Insert File) — the default selection in the
    /// "Select Files" window for Insert File and Add Cover.
    /// </summary>
    public string LastInsertedFile => _settings.LastInsertedFile;

    /// <summary>Images available in the current publication to choose as the cover (for the "Add Cover" picker).</summary>
    public IReadOnlyList<ImageResource> GetCoverCandidateImages() =>
        _currentBook is null
            ? Array.Empty<ImageResource>()
            : _currentBook.GetAllResources().OfType<ImageResource>().ToList();

    /// <summary>The existing cover page (for the view's decision about asking to overwrite), or <c>null</c>.</summary>
    public HtmlResource? FindExistingCoverHtml() => _currentBook?.FindExistingCoverHtmlResource();

    /// <summary>
    /// Confirms "Add Cover" for the chosen image (the view has already decided about a possible overwrite
    /// of the existing cover). Opens the resulting page in a tab.
    /// </summary>
    public async Task ApplyAddCoverAsync(ImageResource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (_currentBook is not { } book)
        {
            return;
        }

        string title = ActionLabel(AppActionIds.AddCover);
        BookOperationOutcome<HtmlResource> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => book.SetCoverImage(image, book.FindExistingCoverHtmlResource())).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        HtmlResource cover = run.Result!;
        BookBrowser.Refresh();
        _tabManager.OpenResources(new Resource[] { cover });
        _statusBar.ShowMessage(Strings.Get("Status_CoverAdded"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// Builds the view model of the "Generate Table Of Contents" dialog for the current publication
    /// (first flushing the content of the open tabs into the resources), or <c>null</c> when there is no book
    /// or there are no XHTML files.
    /// </summary>
    public async Task<HeadingSelectorViewModel?> CreateHeadingSelectorViewModelAsync()
    {
        if (_currentBook is not { } book || book.GetHtmlResources().Count == 0)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        // A checkpoint before opening the window, because the window changes headings
        // in the sources; the view rolls it back after cancelling (RewindCheckpoint). Taking it and listing the headings of the
        // book run off the UI thread under a progress window; cancelling it puts the book back and opens nothing.
        string title = ActionLabel(AppActionIds.GenerateToc);
        BookOperationOutcome<HeadingSelectorViewModel> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => new HeadingSelectorViewModel(book)).ConfigureAwait(true);
        return run.Completed ? run.Result : null;
    }

    /// <summary>
    /// Completes "Generate Table Of Contents" after the dialog closes: rebuilds the
    /// <c>nav[epub:type=toc]</c> section (EPUB 3) or the NCX <c>navMap</c> from the book's headings, reloads
    /// the open tabs and refreshes the panels.
    /// </summary>
    /// <param name="headingsChanged">Whether the dialog changed headings in the sources (from <see cref="HeadingSelectorViewModel.BookChanged"/>).</param>
    public async Task CompleteGenerateTocAsync(bool headingsChanged)
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        // The checkpoint was taken before the dialog, so this is a book operation without one of its own (no Cancel).
        BookOperationOutcome<bool> run = await RunBookOperationAsync(
            ActionLabel(AppActionIds.GenerateToc), checkpointName: null, editsBook: true, (_, _) => TocGenerator.GenerateToc(book))
            .ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        bool tocChanged = run.Result;

        if (headingsChanged || tocChanged)
        {
            foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
            {
                view.Reload();
            }

            _currentBook.Modified = true;
            BookBrowser.Refresh();
            _preview.Refresh();
            _toc.Refresh();
            _statusBar.ShowMessage(Strings.Get("Status_TocGenerated"), TimeSpan.FromSeconds(4));
        }
        else
        {
            RewindCheckpoint();
            _statusBar.ShowMessage(Strings.Get("Status_TocNoChanges"), TimeSpan.FromSeconds(4));
        }
    }

    /// <summary>
    /// Builds the view model of the "Edit Table Of Contents" dialog for the current publication (first
    /// flushing the content of the open tabs), or <c>null</c> when no book is open.
    /// </summary>
    public async Task<EditTocViewModel?> CreateEditTocViewModelAsync()
    {
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        string title = ActionLabel(AppActionIds.EditToc);
        BookOperationOutcome<EditTocViewModel> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => new EditTocViewModel(book)).ConfigureAwait(true);
        return run.Completed ? run.Result : null;
    }

    /// <summary>
    /// Completes "Edit Table Of Contents" after saving from the dialog: reloads the open tabs
    /// and refreshes the panels.
    /// </summary>
    public void CompleteEditToc()
    {
        if (_currentBook is null)
        {
            return;
        }

        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            view.Reload();
        }

        _currentBook.Modified = true;
        BookBrowser.Refresh();
        _preview.Refresh();
        _toc.Refresh();
        _statusBar.ShowMessage(Strings.Get("Status_TocUpdated"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// Builds the view model of the "Metadata Editor" dialog for the current publication (first flushing
    /// the content of the open tabs), or <c>null</c> when no book is open.
    /// </summary>
    public MetadataEditorViewModel? CreateMetadataEditorViewModel()
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        return new MetadataEditorViewModel(_currentBook);
    }

    /// <summary>
    /// Completes "Metadata Editor" after saving from the dialog: reloads the open tabs and refreshes
    /// the panels (metadata can affect Book Browser / Preview).
    /// </summary>
    public void CompleteMetadataEditor()
    {
        if (_currentBook is null)
        {
            return;
        }

        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            view.Reload();
        }

        BookBrowser.Refresh();
        _preview.Refresh();
        _statusBar.ShowMessage(Strings.Get("Status_MetadataUpdated"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// Builds the view model of the "Reports" dialog for the current publication (first flushing the content
    /// of the open tabs so that the report sees the current state), or <c>null</c> when no book is
    /// open. Unlike the other dialogs, calling this method is safe when the window is opened
    /// repeatedly — Reports is non-modal and rebuilt from scratch every time.
    /// </summary>
    public ReportsViewModel? CreateReportsViewModel()
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        ReportsViewModel viewModel = new(_currentBook);
        viewModel.NavigationRequested += NavigateToBookPathAtOffset;
        return viewModel;
    }

    /// <summary>
    /// Refreshes and returns the persistent <see cref="SpellcheckEditorViewModel"/> instance for the current
    /// book (first flushing the content of the open tabs), or <c>null</c> when no book is open.
    /// Unlike <see cref="CreateReportsViewModel"/>, the view model is NOT rebuilt on every open —
    /// it keeps the filter/selected dictionary between opens.
    /// </summary>
    public SpellcheckEditorViewModel? OpenSpellcheckEditor()
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        _spellcheckEditor.Refresh(_currentBook);
        return _spellcheckEditor;
    }

    /// <summary>
    /// Performs "Change All to..." from the Spellcheck Editor: flushes the open tabs
    /// (the content may have changed since the window was last refreshed), replaces the word in all
    /// (X)HTML files, reloads the open tabs and panels, refreshes the editor table.
    /// </summary>
    private async Task ApplySpellcheckChangeAllAsync(string word, string lang, string newWord)
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        if (!TryEnterBookOperation())
        {
            return;
        }

        try
        {
            _tabManager.SaveAllTabs();
            string title = Strings.Get("CheckpointOp_SpellCheck");
            (bool cancelled, bool checkpoint) = await AddCheckpointOrCancelAsync(title).ConfigureAwait(true);
            if (cancelled)
            {
                return;
            }

            int replaced = await RunInBackgroundAsync(
                title, DelayedProgressRunner, editsBook: true, cancellable: false,
                (_, _) => SpellcheckEditorEngine.ReplaceWordInAllFiles(book, _spellChecker, _settings, word, lang, newWord))
                .ConfigureAwait(true);
            if (replaced == 0)
            {
                if (checkpoint)
                {
                    RewindCheckpoint();
                }

                _spellcheckEditor.Refresh(book);
                return;
            }

            book.Modified = true;
            RefreshAfterMaintenanceOperation();
            _spellcheckEditor.Refresh(book);
            _statusBar.ShowMessage(Strings.Get("Status_WordUpdated"), TimeSpan.FromSeconds(4));
        }
        finally
        {
            ExitBookOperation();
        }
    }

    /// <summary>
    /// Applies the changes from the Preferences window after Save: refreshes spelling underlines in the open
    /// tabs (dictionaries, user words) and the settings of the "Find Usages" panel.
    /// </summary>
    public void ApplyPreferencesChanges()
    {
        RefreshOpenTabsSpellcheck();
        _findUsages.ReloadSettings();
    }

    /// <summary>
    /// Refreshes spelling highlighting in all open Code Editor tabs — after a change of the
    /// dictionary state (Ignore/Add in the Spellcheck Editor) without changing the file contents.
    /// </summary>
    private void RefreshOpenTabsSpellcheck()
    {
        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            if (view is CodeTabViewModel codeTab)
            {
                codeTab.NotifySpellCheckSettingChanged();
            }
        }
    }

    /// <summary>
    /// Prepares the "Cleanup" dialog: flushes the open tabs, asks about files without a DOCTYPE and analyses the
    /// book (<see cref="CleanupAnalysis.Prepare"/>). Returns the dialog's view model, or <c>null</c> when no book is
    /// open, the user cancelled, or an (X)HTML file is not well-formed (then a message goes to the status bar).
    /// </summary>
    public async Task<CleanupViewModel?> PrepareCleanupAsync(ProgressRunner? runWithProgress = null)
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_Cleanup").ConfigureAwait(true))
        {
            return null;
        }

        // Parsing every file and matching every selector takes long on a big book: off the UI thread under a progress window, which can
        // be cancelled (the analysis only reads the book).
        Book book = _currentBook;
        BookOperationOutcome<CleanupPreparation> run = await RunBookOperationAsync(
            Strings.Get("Operation_Cleanup"), checkpointName: null, editsBook: false,
            (progress, cancel) =>
            {
                progress.Report(new WorkProgress(Strings.Get("Cleanup_Phase_Analyzing"), null));
                return CleanupAnalysis.PrepareCancellable(book, cancel);
            }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return null;
        }

        CleanupPreparation preparation = run.Result!;
        if (preparation.Analysis is null)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_Cleanup"), preparation.NotWellFormed?.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return null;
        }

        return new CleanupViewModel(
            preparation.Analysis, _settings.CleanupEnabledSteps, NavigateToBookPathAtOffset,
            (plan, steps) => ApplyCleanupAsync(plan, steps, runWithProgress));
    }

    /// <summary>
    /// Applies the plan of one tab of the "Cleanup" dialog ("Clean"), remembers the checked steps for the next time,
    /// refreshes the open tabs and panels and re-analyses the changed book, so the dialog can stay open. Returns the
    /// new analysis, or <c>null</c> when no book is open, an (X)HTML file is not well-formed any more, or another book operation
    /// is running (the dialog disables "Clean" meanwhile, so that is only a race).
    /// </summary>
    /// <remarks>
    /// The cleaning and the analysis of the changed book take long on a big book, so both run off the UI thread under
    /// <paramref name="runWithProgress"/> (a progress window over the dialog, in two phases) behind a checkpoint — a cleaning
    /// changes a lot (it deletes stylesheets and media). The user can cancel it: the book is put back from the checkpoint, and
    /// since the analysis the dialog holds belongs to the book that was replaced, the dialog closes (<c>null</c>).
    /// </remarks>
    public async Task<CleanupAnalysis?> ApplyCleanupAsync(
        CleanupPlan plan, IReadOnlyList<CleanupStep> enabledSteps, ProgressRunner? runWithProgress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(enabledSteps);
        _settings.CleanupEnabledSteps = enabledSteps;
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        DebugLog.Write(
            "Cleanup",
            "apply: " + string.Join("; ", plan.Steps.Select(st => $"{st.Step} {st.AppliedCount}/{st.Items.Count} items")) +
            $"; files changed {plan.NewTexts.Count}, files removed {plan.ResourcesToDelete.Count}");
        BookOperationOutcome<(bool Changed, CleanupAnalysis? Analysis)> run = await RunBookOperationAsync(
            Strings.Get("Operation_Cleanup"), Strings.Get("CheckpointOp_Cleanup"), editsBook: true,
            (progress, cancel) =>
            {
                // Neither step can tell how far it is, so each phase has only a bar that works.
                progress.Report(new WorkProgress(null, null, new PhaseInfo(1, 2, Strings.Get("Cleanup_Phase_Cleaning"))));
                bool changed = book.ApplyCleanupCancellable(plan, cancel);
                progress.Report(new WorkProgress(null, null, new PhaseInfo(2, 2, Strings.Get("Cleanup_Phase_Analyzing"))));
                return (changed, CleanupAnalysis.PrepareCancellable(book, cancel).Analysis);
            },
            runWithProgress).ConfigureAwait(true);
        if (!run.Completed)
        {
            // Cancelled (RunBookOperationAsync put the book back and said so) or refused: the analysis of the dialog is stale.
            return null;
        }

        if (!run.Result.Changed)
        {
            if (run.Checkpoint)
            {
                RewindCheckpoint();
            }

            return run.Result.Analysis;
        }

        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(Strings.Get("Status_CleanupDone"), TimeSpan.FromSeconds(4));
        return run.Result.Analysis;
    }

    /// <summary>
    /// Prepares the "Standardize EPUB" dialog: flushes the open tabs, asks about files without a DOCTYPE and checks
    /// that the HTML files, the OPF and the NCX are well-formed (their references are rewritten). Returns the dialog's
    /// view model, or <c>null</c> when no book is open, the user cancelled, or a file is not well-formed (then a message
    /// goes to the status bar).
    /// </summary>
    public async Task<StandardizationViewModel?> PrepareStandardizationAsync(ProgressRunner? runWithProgress = null)
    {
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_Standardize").ConfigureAwait(true))
        {
            return null;
        }

        // Checking every file takes long on a big book: off the UI thread under a progress window, which can be cancelled.
        BookOperationOutcome<Resource?> check = await RunBookOperationAsync(
            Strings.Get("Operation_Standardize"), checkpointName: null, editsBook: false,
            (_, cancel) =>
            {
                cancel.ThrowIfCancellationRequested();
                return EpubStandardization.FindNotWellFormed(book);
            }).ConfigureAwait(true);
        if (!check.Completed)
        {
            return null;
        }

        if (check.Result is { } bad)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_Standardize"), bad.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return null;
        }

        return new StandardizationViewModel(
            _settings.StandardizationEnabledSteps,
            steps => EpubStandardization.Plan(book, steps),
            steps => ApplyStandardizationAsync(steps, runWithProgress));
    }

    /// <summary>
    /// Runs the checked steps of the "Standardize EPUB" dialog ("Apply") behind one automatic checkpoint, remembers the
    /// checked steps for the next time and refreshes the open tabs and panels.
    /// </summary>
    public async Task ApplyStandardizationAsync(IReadOnlyList<StandardizationStep> enabledSteps, ProgressRunner? runWithProgress = null)
    {
        ArgumentNullException.ThrowIfNull(enabledSteps);
        _settings.StandardizationEnabledSteps = enabledSteps;
        if (_currentBook is not { } book)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (!EpubStandardization.Plan(book, enabledSteps).HasChanges)
        {
            _statusBar.ShowMessage(Strings.Get("Status_StandardizeNothing"), TimeSpan.FromSeconds(4));
            return;
        }

        string title = ActionLabel(AppActionIds.Standardize);
        if (!await RunMaintenanceAsync(
                title, title, "Operation_Standardize", b => EpubStandardization.Apply(b, enabledSteps), runWithProgress).ConfigureAwait(true))
        {
            return;
        }

        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(Strings.Get("Status_StandardizeDone"), TimeSpan.FromSeconds(4));
    }

    // A maintenance operation of the whole book (Standardize, Restructure, Use Standard File Extensions…): the checkpoint named
    // `checkpointName` (when given) and `apply` run off the UI thread under the progress window `title`, with Cancel when there is
    // a checkpoint to go back to. When `apply` refuses (an (X)HTML file is not well-formed) the checkpoint is rewound and the status
    // bar says so (`operationKey` is the resource key of the operation's name). Returns whether it was applied; the caller then
    // refreshes the UI and reports.
    private async Task<bool> RunMaintenanceAsync(
        string title, string? checkpointName, string operationKey, Func<Book, MaintenanceOperationResult> apply,
        ProgressRunner? runWithProgress = null)
    {
        if (_currentBook is not { } book)
        {
            return false;
        }

        BookOperationOutcome<MaintenanceOperationResult> run = await RunBookOperationAsync(
            title, checkpointName, editsBook: true, (_, _) => apply(book), runWithProgress).ConfigureAwait(true);
        if (!run.Completed)
        {
            return false;
        }

        if (!run.Result!.Applied)
        {
            if (run.Checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get(operationKey), run.Result.NotWellFormed?.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return false;
        }

        return true;
    }

    /// <summary>
    /// "Split File At Cursor": splits the (X)HTML file of the active Code Editor tab at the caret
    /// (<see cref="Book.SplitAtPosition"/>) — the part before the caret stays in the file, the part after it goes to
    /// a new file right after it, which is then opened with the caret at the start of its content (as in Sigil, so
    /// splitting can go on). Refused (status bar message) for the nav file, a file that is not well-formed, or a
    /// caret inside a tag.
    /// </summary>
    public async Task SplitFileAtCaretAsync()
    {
        if (_currentBook is null || ActiveCodeTab is not { IsHtmlFlow: true, Resource: HtmlResource html } tab)
        {
            return;
        }

        if (ReferenceEquals(_currentBook.GetNavResource(), html))
        {
            _statusBar.ShowMessage(Strings.Get("Status_CannotSplitNav"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        if (tab.RunWellFormedCheck() == false)
        {
            return;
        }

        int caret = tab.CaretOffset;
        _tabManager.SaveAllTabs();
        Book book = _currentBook;
        string title = ActionLabel(AppActionIds.SplitSection);
        BookOperationOutcome<HtmlResource?> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => book.SplitAtPosition(html, caret)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        HtmlResource? created = run.Result;
        if (created is null)
        {
            if (run.Checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(Strings.Get("Status_CannotSplitFileHere"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        RefreshAfterMaintenanceOperation();
        NavigateToBookPathAtOffset(created.BookPath, BodyContentStart(created.GetText()));
        _statusBar.ShowMessage(Strings.Format("Status_FileSplit", created.Filename), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// "Split At Markers" (Edit menu): splits the (X)HTML file of the active Code Editor tab at its split markers
    /// (<see cref="Book.SplitOnSectionMarkers"/>) — the first section stays in the file, the others go to new files
    /// right after it. The Book Browser has the same operation for the selected files.
    /// </summary>
    public async Task SplitCurrentFileAtMarkersAsync()
    {
        if (_currentBook is null || ActiveCodeTab is not { IsHtmlFlow: true, Resource: HtmlResource html } tab)
        {
            return;
        }

        if (ReferenceEquals(_currentBook.GetNavResource(), html))
        {
            _statusBar.ShowMessage(Strings.Get("Status_CannotSplitNav"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        if (tab.RunWellFormedCheck() == false)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Book book = _currentBook;
        string title = ActionLabel(AppActionIds.SplitOnSgfSectionMarkers);
        BookOperationOutcome<IReadOnlyList<HtmlResource>> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => book.SplitOnSectionMarkers(html)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        IReadOnlyList<HtmlResource> created = run.Result!;
        if (created.Count == 0)
        {
            if (run.Checkpoint)
            {
                RewindCheckpoint();
            }

            _statusBar.ShowMessage(Strings.Get("Status_NoSplitMarkersInFile"), TimeSpan.FromSeconds(4));
            return;
        }

        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(Strings.Format("Status_FileSplitAtMarkers", created.Count), TimeSpan.FromSeconds(4));
    }

    // The offset of the first non-whitespace character after the <body> opening tag (0 without a body).
    private static int BodyContentStart(string text)
    {
        TagLister lister = TagLister.For(text);
        int bodyOpen = lister.FindBodyOpenTag();
        if (bodyOpen < 0)
        {
            return 0;
        }

        TagLister.TagInfo tag = lister.At(bodyOpen);
        int offset = tag.Pos + tag.Len;
        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }

        return offset;
    }

    /// <summary>Refreshes the open tabs, the Book Browser panel and Preview after a whole-book maintenance operation.</summary>
    private void RefreshAfterMaintenanceOperation()
    {
        ReloadOpenTabsAndPreview();
        BookBrowser.Refresh();
    }

    // Reloads every open tab from its resource (discarding the editors' undo history) and refreshes Preview.
    private void ReloadOpenTabsAndPreview()
    {
        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            view.Reload();
        }

        _preview.Refresh();
    }

    // =====================================================================
    //  ICodeTabHost — services for the Code Editor context menu
    // =====================================================================

    /// <summary>
    /// Request to show an image in the "View Image" window (Code Editor context menu). Argument:
    /// the image resource (raster or SVG).
    /// </summary>
    public event EventHandler<Resource>? ViewImageRequested;

    /// <inheritdoc/>
    ClipEditorNode ICodeTabHost.ClipLibraryRoot => _clips.LibraryRoot;

    /// <inheritdoc/>
    bool ICodeTabHost.IsBookOperationBusy => IsBookOperationBusy;

    /// <inheritdoc/>
    void ICodeTabHost.PasteClip(string text) => _clips.PasteText(text);

    /// <inheritdoc/>
    void ICodeTabHost.AddToClips(string text)
    {
        _clips.AddPendingEntry(text);
        ClipEditorRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    Avalonia.Input.KeyGesture? ICodeTabHost.GetActionGesture(string actionId) => _actions.Get(actionId)?.Gesture;

    /// <inheritdoc/>
    string? ICodeTabHost.GetActionIcon(string actionId) => _actions.Get(actionId)?.Descriptor.IconKey;

    /// <inheritdoc/>
    void ICodeTabHost.ExecuteAction(string actionId)
    {
        if (_actions.Get(actionId) is { } action && action.CanExecute(null))
        {
            action.Execute(null);
        }
    }

    /// <inheritdoc/>
    async Task<string?> ICodeTabHost.ReformatHtmlTextAsync(Resource resource, string text, bool toValid)
    {
        if (_currentBook is null || resource is not HtmlResource html)
        {
            return null;
        }

        if (toValid)
        {
            return _currentBook.MendHtmlText(html, text, BuildEntityOverrides(), _settings.MendAddMissingDoctype);
        }

        // The editor text (possibly unsaved) is what gets pretty-printed, so it is what is checked.
        if (WellFormedChecker.IsMissingDoctype(text, html.EpubVersion)
            && !await _doctypeGuard.ConfirmAsync(Strings.Get("Operation_PrettyPrint"), new[] { html.BookPath }).ConfigureAwait(true))
        {
            return null;
        }

        string? formatted = _currentBook.SafePrettyPrintHtmlText(
            html, text, BuildEntityOverrides(), _settings.PrettifyAddMissingDoctype);
        if (formatted is null)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_PrettyPrint"), html.Filename), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }

        return formatted;
    }

    /// <inheritdoc/>
    async Task<ClassRenamer?> ICodeTabHost.PrepareClassRenameAsync()
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_RenameClass").ConfigureAwait(true))
        {
            return null;
        }

        ClassRenamePreparation preparation = _currentBook.PrepareClassRename();
        if (preparation.Renamer is null)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_RenameClass"), preparation.NotWellFormed?.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }

        return preparation.Renamer;
    }

    /// <inheritdoc/>
    async Task ICodeTabHost.ApplyClassRename(ClassRenameResult result, ProgressRunner? runWithProgress)
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        if (!TryEnterBookOperation())
        {
            return;
        }

        try
        {
            string title = Strings.Get("Operation_RenameClass");
            (bool cancelled, bool checkpoint) = await AddCheckpointOrCancelAsync(title).ConfigureAwait(true);
            if (cancelled)
            {
                return;
            }

            bool applied = await RunInBackgroundAsync(
                title, runWithProgress, editsBook: true, cancellable: false, (_, _) => book.ApplyClassRename(result)).ConfigureAwait(true);
            if (!applied)
            {
                if (checkpoint)
                {
                    RewindCheckpoint();
                }

                return;
            }

            RefreshAfterMaintenanceOperation();
            _statusBar.ShowMessage(Strings.Get("CodeEditor_ClassRenamed"), TimeSpan.FromSeconds(4));
        }
        finally
        {
            ExitBookOperation();
        }
    }

    /// <inheritdoc/>
    async Task<IdRenamer?> ICodeTabHost.PrepareIdRenameAsync()
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_RenameId").ConfigureAwait(true))
        {
            return null;
        }

        IdRenamePreparation preparation = _currentBook.PrepareIdRename();
        if (preparation.Renamer is null)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_RenameId"), preparation.NotWellFormed?.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }

        return preparation.Renamer;
    }

    /// <inheritdoc/>
    async Task ICodeTabHost.ApplyIdRename(IdRenameResult result, ProgressRunner? runWithProgress)
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        if (!TryEnterBookOperation())
        {
            return;
        }

        try
        {
            string title = Strings.Get("Operation_RenameId");
            (bool cancelled, bool checkpoint) = await AddCheckpointOrCancelAsync(title).ConfigureAwait(true);
            if (cancelled)
            {
                return;
            }

            bool applied = await RunInBackgroundAsync(
                title, runWithProgress, editsBook: true, cancellable: false, (_, _) => book.ApplyIdRename(result)).ConfigureAwait(true);
            if (!applied)
            {
                if (checkpoint)
                {
                    RewindCheckpoint();
                }

                return;
            }

            RefreshAfterMaintenanceOperation();
            _statusBar.ShowMessage(Strings.Get("CodeEditor_IdRenamed"), TimeSpan.FromSeconds(4));
        }
        finally
        {
            ExitBookOperation();
        }
    }

    /// <inheritdoc/>
    async Task<ClassRemover?> ICodeTabHost.PrepareClassRemovalAsync()
    {
        if (_currentBook is null)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_RemoveClass").ConfigureAwait(true))
        {
            return null;
        }

        ClassRemovalPreparation preparation = _currentBook.PrepareClassRemoval();
        if (preparation.Remover is null)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_RemoveClass"), preparation.NotWellFormed?.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }

        return preparation.Remover;
    }

    /// <inheritdoc/>
    async Task ICodeTabHost.ApplyClassRemoval(ClassRemovalResult result, ProgressRunner? runWithProgress)
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        if (!TryEnterBookOperation())
        {
            return;
        }

        try
        {
            string title = Strings.Get("Operation_RemoveClass");
            (bool cancelled, bool checkpoint) = await AddCheckpointOrCancelAsync(title).ConfigureAwait(true);
            if (cancelled)
            {
                return;
            }

            bool applied = await RunInBackgroundAsync(
                title, runWithProgress, editsBook: true, cancellable: false, (_, _) => book.ApplyClassRemoval(result)).ConfigureAwait(true);
            if (!applied)
            {
                if (checkpoint)
                {
                    RewindCheckpoint();
                }

                return;
            }

            RefreshAfterMaintenanceOperation();
            _statusBar.ShowMessage(Strings.Get("CodeEditor_ClassRemoved"), TimeSpan.FromSeconds(4));
        }
        finally
        {
            ExitBookOperation();
        }
    }

    /// <inheritdoc/>
    void ICodeTabHost.FindIdUsages(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        IReadOnlyList<ClassUsage> usages = _currentBook.FindIdUsages(id);
        _findUsages.LoadText(
            () => Strings.Format("FindUsages_HeaderId", id), usages, () => _currentBook?.FindIdUsages(id) ?? Array.Empty<ClassUsage>());
        ShowFindUsagesTool();
        _statusBar.ShowMessage(
            Strings.Format("Status_FindUsagesId", id, FindUsagesViewModel.ResultsText(usages.Count)), TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    void ICodeTabHost.FindLanguageUsages(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        IReadOnlyList<ClassUsage> usages = _currentBook.FindLanguageUsages(language);
        _findUsages.LoadText(
            () => Strings.Format("FindUsages_HeaderLanguage", language),
            usages,
            () => _currentBook?.FindLanguageUsages(language) ?? Array.Empty<ClassUsage>());
        ShowFindUsagesTool();
        _statusBar.ShowMessage(
            Strings.Format("Status_FindUsagesLanguage", language, FindUsagesViewModel.ResultsText(usages.Count)),
            TimeSpan.FromSeconds(4));
    }

    /// <inheritdoc/>
    int ICodeTabHost.CountSameSpansInBook(HtmlResource html, string text, int offset) =>
        _currentBook is null ? 0 : SpanRemoval.CountInBook(_currentBook, html, text, offset);

    /// <inheritdoc/>
    SpanRemovalPlan? ICodeTabHost.PlanSpanRemoval(HtmlResource html, string text, int offset, SpanRemovalScope scope)
    {
        if (_currentBook is null)
        {
            return null;
        }

        if (scope == SpanRemovalScope.Book)
        {
            _tabManager.SaveAllTabs();
        }

        return SpanRemoval.Plan(_currentBook, html, text, offset, scope);
    }

    /// <inheritdoc/>
    async Task ICodeTabHost.ApplySpanRemovalInBook(SpanRemovalPlan plan, ProgressRunner? runWithProgress)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_currentBook is not { } book || plan.NewTexts.Count == 0)
        {
            return;
        }

        if (!TryEnterBookOperation())
        {
            return;
        }

        try
        {
            string title = Strings.Get("Operation_RemoveSpans");
            (bool cancelled, _) = await AddCheckpointOrCancelAsync(title).ConfigureAwait(true);
            if (cancelled)
            {
                return;
            }

            await RunInBackgroundAsync(
                title, runWithProgress, editsBook: true, cancellable: false,
                (_, _) =>
                {
                    foreach (HtmlResource html in book.GetHtmlResources())
                    {
                        if (plan.NewTexts.TryGetValue(html.BookPath, out string? text))
                        {
                            html.SetText(text);
                        }
                    }

                    book.Modified = true;
                    return true;
                }).ConfigureAwait(true);

            RefreshAfterMaintenanceOperation();
            _statusBar.ShowMessage(Strings.Format("Status_SpansRemovedInBook", plan.SpanCount, plan.NewTexts.Count), TimeSpan.FromSeconds(4));
        }
        finally
        {
            ExitBookOperation();
        }
    }

    /// <inheritdoc/>
    void ICodeTabHost.ViewImage(string bookPath)
    {
        Resource? resource = _currentBook?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath);
        if (resource is ImageResource or SvgResource)
        {
            ViewImageRequested?.Invoke(this, resource);
        }
        else
        {
            _statusBar.ShowMessage(Strings.Format("Status_ImageMissing", bookPath), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }
    }

    /// <summary>"Mend &amp; Prettify All HTML Files".</summary>
    private async Task MendPrettifyHtmlAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_MendPrettifyAll").ConfigureAwait(true))
        {
            return;
        }

        Book book = _currentBook;
        Dictionary<char, string> overrides = BuildEntityOverrides();
        bool addDoctype = _settings.PrettifyAddMissingDoctype;
        string title = ActionLabel(AppActionIds.MendPrettifyHtml);
        BookOperationOutcome<FileBatchResult> run = await RunBookOperationAsync(
            title, title, editsBook: true,
            (progress, cancel) => book.PrettyPrintAllHtml(overrides, addDoctype, new FileBatchRun(new FileBatchProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        if (run.Completed)
        {
            FinishFileBatch(run.Result!, run.Checkpoint, "Operation_MendPrettifyAll", "Status_MendPrettifyAllDone");
        }
    }

    /// <summary>
    /// "Prettify All Stylesheets" — the multi-line form over every CSS file of the book. A stylesheet whose
    /// structure is broken is skipped and reported, the rest is reformatted.
    /// </summary>
    private async Task PrettifyAllCssAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Book book = _currentBook;
        string title = ActionLabel(AppActionIds.PrettifyAllCss);
        BookOperationOutcome<FileBatchResult> run = await RunBookOperationAsync(
            title, title, editsBook: true,
            (progress, cancel) => book.ReformatAllCss(
                multipleLineFormat: true, new FileBatchRun(new FileBatchProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        if (run.Completed)
        {
            FinishFileBatch(run.Result!, run.Checkpoint, "Operation_PrettifyAllCss", "Status_PrettifyAllCssDone");
        }
    }

    /// <summary>
    /// "Mend All HTML Files" — without the well-formed guard, since repairing such files is exactly what Mend does.
    /// A file the repair fails on is skipped and the rest goes on.
    /// </summary>
    private async Task MendHtmlAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        Book book = _currentBook;
        Dictionary<char, string> overrides = BuildEntityOverrides();
        bool addDoctype = _settings.MendAddMissingDoctype;
        string title = ActionLabel(AppActionIds.MendHtml);
        BookOperationOutcome<FileBatchResult> run = await RunBookOperationAsync(
            title, title, editsBook: true,
            (progress, cancel) => book.MendAllHtml(overrides, addDoctype, new FileBatchRun(new FileBatchProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        if (run.Completed)
        {
            FinishFileBatch(run.Result!, run.Checkpoint, "Operation_MendAll", "Status_MendAllDone");
        }
    }

    /// <summary>
    /// The end of an operation applied file by file: when no file could be processed the checkpoint taken before it
    /// is rewound (the book is unchanged), otherwise the views are refreshed. The status bar gets only the numbers
    /// of processed and skipped files; the skipped files themselves go to the report window
    /// (<see cref="SkippedFilesReportRequested"/>).
    /// </summary>
    private void FinishFileBatch(FileBatchResult result, bool checkpoint, string operationKey, string successMessageKey)
    {
        if (result.NothingProcessed)
        {
            if (checkpoint)
            {
                RewindCheckpoint();
            }
        }
        else
        {
            RefreshAfterMaintenanceOperation();
        }

        RaiseSkippedFilesReport(FileBatchReporter.ShowStatus(
            _statusBar, Strings.Get(operationKey), result, Strings.Get(successMessageKey)));
    }

    /// <summary>
    /// Raised when an operation applied file by file skipped some files — the view shows the report window (the
    /// skipped files with the reason; double-clicking one opens it at the problem).
    /// </summary>
    public event EventHandler<SkippedFilesReport>? SkippedFilesReportRequested;

    private void RaiseSkippedFilesReport(SkippedFilesReport? report)
    {
        if (report is not null)
        {
            SkippedFilesReportRequested?.Invoke(this, report);
        }
    }

    /// <summary>"Add Soft Hyphens".</summary>
    private async Task AddSoftHyphensAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_AddSoftHyphens").ConfigureAwait(true))
        {
            return;
        }

        Book book = _currentBook;
        BookOperationOutcome<FileBatchResult> run = await RunBookOperationAsync(
            ActionLabel(AppActionIds.AddSoftHyphens), checkpointName: null, editsBook: true,
            (progress, cancel) => book.AddSoftHyphens(new FileBatchRun(new FileBatchProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        FinishFileBatch(run.Result!, checkpoint: false, "Operation_AddSoftHyphens", "Status_SoftHyphensAdded");
    }

    /// <summary>"Remove Soft Hyphens" — the inverse of <see cref="AddSoftHyphensAsync"/>.</summary>
    private async Task RemoveSoftHyphensAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_RemoveSoftHyphens").ConfigureAwait(true))
        {
            return;
        }

        Book book = _currentBook;
        BookOperationOutcome<FileBatchResult> run = await RunBookOperationAsync(
            ActionLabel(AppActionIds.RemoveSoftHyphens), checkpointName: null, editsBook: true,
            (progress, cancel) => book.RemoveSoftHyphens(new FileBatchRun(new FileBatchProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        FinishFileBatch(run.Result!, checkpoint: false, "Operation_RemoveSoftHyphens", "Status_SoftHyphensRemoved");
    }

    /// <summary>
    /// "Live CSS Panel": computes the CSS cascade (<see cref="CssCascadeResolver"/>) for the element
    /// under the caret of the active Code Editor tab. The trigger is an action/shortcut or a caret move (not a hover
    /// in Preview), so it works only when the active tab is editing an (X)HTML file.
    /// <c>null</c> when there is no active HTML tab or the caret is not inside any element —
    /// a message is then shown in the status bar, unless <paramref name="reportFailure"/> is <c>false</c>
    /// (the automatic refresh of an already open panel, which silently keeps its previous content).
    /// </summary>
    public CssCascadeResult? TryResolveLiveCssPanel(bool reportFailure = true)
    {
        if (_currentBook is not { } book || ActiveCodeTab is not { } tab || tab.Resource is not HtmlResource html)
        {
            return null;
        }

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        CssCascadeResult? result = CssCascadeResolver.Resolve(
            html,
            tab.CaretOffset,
            cssBookPath => folderKeeper.GetResourceByBookPathNoThrow(cssBookPath) is CssResource css
                ? new CssInfo(css.GetText())
                : null);

        if (result is null && reportFailure)
        {
            _statusBar.ShowMessage(Strings.Get("Status_CaretNotInElement"), TimeSpan.FromSeconds(4));
        }

        return result;
    }

    /// <summary>Content of the file with the given book path (to compute line numbers in the Live CSS Panel), or <c>""</c>.</summary>
    public string GetResourceTextForLiveCssPanel(string bookPath) =>
        _currentBook?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is TextResource text
            ? text.GetText()
            : string.Empty;

    /// <summary>Opens (or activates) the tab of the rule clicked in the Live CSS Panel and places the caret on it.</summary>
    public void JumpToCssLocation(string bookPath, int offset) => _tabManager.OpenResourceAtOffset(bookPath, offset);

    /// <summary>
    /// Warns about (X)HTML files without a DOCTYPE before an operation that rewrites them
    /// (<see cref="MissingDoctypeGuard"/>). Call after <c>SaveAllTabs</c>, so the open tabs' texts are checked.
    /// Returns <c>false</c> when the user cancelled.
    /// </summary>
    /// <param name="operationKey">Resource key of the operation name shown in the dialog.</param>
    private Task<bool> ConfirmMissingDoctypeAsync(string operationKey) =>
        _currentBook is null
            ? Task.FromResult(true)
            : _doctypeGuard.ConfirmAsync(Strings.Get(operationKey), _currentBook.FindHtmlMissingDoctype());

    /// <summary>
    /// Connects the "missing DOCTYPE" warning dialog (implemented by the main window). Until then
    /// operations run without asking.
    /// </summary>
    public void AttachMissingDoctypePrompt(IMissingDoctypePrompt prompt) =>
        _doctypeGuard.Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

    /// <summary>
    /// Builds the character → entity text map from the "Preserve Entities" panel (Preferences) to be
    /// passed to <see cref="CleanSource.Mend"/>/<see cref="CleanSource.PrettyPrint"/>.
    /// Deliberately limited: wired only into the two "Mend" actions invoked directly from the menu — the
    /// automatic mending on open/save (<see cref="SettingsStore.CleanOn"/>) goes through
    /// <c>Importers</c>/<c>FileWorkflow</c>, not through these methods, and is not wired here.
    /// </summary>
    private Dictionary<char, string> BuildEntityOverrides() =>
        _settings.PreserveEntityCodeNames.ToDictionary(p => (char)p.Code, p => p.Name);

    /// <summary>
    /// Completes "Restructure Epub to Signet Norm" after the user's confirmation
    /// in the view (see <see cref="StandardizeEpubRequested"/>).
    /// </summary>
    public async Task ApplyStandardizeEpubAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_Restructure").ConfigureAwait(true))
        {
            return;
        }

        string title = ActionLabel(AppActionIds.StandardizeEpub);
        if (!await RunMaintenanceAsync(title, title, "Operation_Restructure", b => b.RestructureToSignetNorm()).ConfigureAwait(true))
        {
            return;
        }

        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(Strings.Get("Status_RestructureDone"), TimeSpan.FromSeconds(4));
    }

    /// <summary>"Use Standard File Extensions".</summary>
    private async Task UseStandardFileExtensionsAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (!await ConfirmMissingDoctypeAsync("Operation_BulkRename").ConfigureAwait(true))
        {
            return;
        }

        if (!await RunMaintenanceAsync(
                ActionLabel(AppActionIds.UseStandardFileExtensions), null, "Operation_BulkRename", b => b.UseStandardFileExtensions())
                .ConfigureAwait(true))
        {
            return;
        }

        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(Strings.Get("Status_BulkRenameDone"), TimeSpan.FromSeconds(4));
    }

    /// <summary>"Rebase OPF Manifest IDs on Current Filenames".</summary>
    private async Task RebaseManifestIdsAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        if (!await RunMaintenanceAsync(
                ActionLabel(AppActionIds.RebaseManifestIds), null, "Operation_Rebase", b => b.RebaseManifestIds()).ConfigureAwait(true))
        {
            return;
        }

        BookBrowser.Refresh();
        _statusBar.ShowMessage(Strings.Get("Status_RebaseDone"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// Opens the resource with the given book path in Code Editor and scrolls to the character offset —
    /// the "go to" navigation from the Reports dialog. Like <c>NavigateToToc</c> but operating on an
    /// offset instead of a fragment.
    /// </summary>
    public void NavigateToBookPathAtOffset(string bookPath, int offset)
    {
        if (_currentBook is null)
        {
            return;
        }

        Resource? resource = _currentBook.GetAllResources()
            .FirstOrDefault(r => string.Equals(r.BookPath, bookPath, StringComparison.Ordinal));
        if (resource is null)
        {
            _statusBar.ShowMessage(Strings.Format("Status_ResourceMissing", bookPath), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _tabManager.OpenResource(resource);
        if (offset >= 0 && ActiveCodeTab is { } tab)
        {
            tab.GoToOffset(offset);
            _preview.SyncCaretToPreview(tab.CaretOffset);
        }
    }

    /// <summary>
    /// Opens the resource with the given book path in Code Editor with the caret at the 1-based line and column (the
    /// "go to" of the load warnings window); a line or column that is unknown (<c>&lt;= 0</c>) only opens the file, one
    /// past the end of the document or the line goes to its end.
    /// </summary>
    public void NavigateToBookPathAtLocation(string bookPath, int line, int column)
    {
        NavigateToBookPathAtOffset(bookPath, -1);
        if (line > 0 && ActiveCodeTab is { } tab && string.Equals(tab.ResourceBookPath, bookPath, StringComparison.Ordinal))
        {
            AvaloniaEdit.Document.DocumentLine documentLine = tab.Document.GetLineByNumber(Math.Min(line, tab.Document.LineCount));
            int offset = documentLine.Offset + Math.Clamp(column - 1, 0, documentLine.Length);
            tab.GoToOffset(offset);
            _preview.SyncCaretToPreview(tab.CaretOffset);
        }
    }

    /// <summary>The "Find Usages" panel view model.</summary>
    public FindUsagesViewModel FindUsages => _findUsages;

    /// <summary>
    /// "Find Usages" (Alt+F7, Code Editor context menu): the usages of the CSS class under the caret of the active
    /// Code Editor tab — in a <c>class</c> attribute or in a selector. Without a class under the caret only a status bar
    /// message.
    /// </summary>
    private void RenameClassAtCaret()
    {
        if (_currentBook is null)
        {
            return;
        }

        if (ActiveCodeTab?.RequestRenameClassAtCaret() != true)
        {
            _statusBar.ShowMessage(Strings.Get("Status_RenameClassNoClass"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
        }
    }

    private void FindUsagesAtCaret()
    {
        if (_currentBook is null)
        {
            return;
        }

        if (ActiveCodeTab?.ClassNameAtCaretForUsages() is not { } className)
        {
            _statusBar.ShowMessage(Strings.Get("Status_FindUsagesNoClass"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        FindClassUsages(className);
    }

    /// <summary>
    /// Searches the whole book (the open tabs flushed first) for the usages of <paramref name="className"/>, shows them
    /// in the "Find Usages" panel (shown and focused) and reports the count on the status bar.
    /// </summary>
    public void FindClassUsages(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        IReadOnlyList<ClassUsage> usages = _currentBook.FindClassUsages(className);
        _findUsages.Load(className, usages);
        ShowFindUsagesTool();
        _statusBar.ShowMessage(
            Strings.Format("Status_FindUsages", className, FindUsagesViewModel.ResultsText(usages.Count)), TimeSpan.FromSeconds(4));
    }

    // Shows the "Find Usages" window (when it is hidden) and focuses it.
    private void ShowFindUsagesTool()
    {
        if (!_dockFactory.IsToolVisible(DockableIds.FindUsages))
        {
            _dockFactory.ToggleTool(DockableIds.FindUsages);
        }

        _dockFactory.FocusTool(DockableIds.FindUsages);
    }

    /// <summary>Raised after "Mend Code" changed the current file when Preferences ask to see the changes — the view shows the diff window.</summary>
    public event EventHandler<DiffViewModel>? MendDiffRequested;

    /// <summary>
    /// "Mend Code" of the current file. When it changed the file and Preferences → Mend &amp; Prettify enable it
    /// (<see cref="SettingsStore.MendShowDiff"/>), the changes are shown in a diff window (before → after) whose
    /// "Undo the change" restores the text from before — as one undo step, and only while the tab is open and its
    /// text has not been edited since.
    /// </summary>
    public async Task MendCurrentFileAsync()
    {
        if (ActiveCodeTab is not { } tab)
        {
            return;
        }

        string before = tab.DocumentText;
        await tab.ReformatHtmlAsync(toValid: true).ConfigureAwait(true);
        string after = tab.DocumentText;
        if (!_settings.MendShowDiff || string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        DiffViewModel diff = new(
            new[] { BookFileDiff.FromTexts(tab.ResourceBookPath, before, after) },
            Strings.Get("Diff_BeforeMend"),
            Strings.Get("Diff_AfterMend"));
        diff.OpenInEditorRequested += (_, target) => OpenBookPathAtLine(target.BookPath, target.Line);
        diff.EnableRevert(() =>
        {
            if (!_tabManager.OpenTabViews.Contains(tab) || !string.Equals(tab.DocumentText, after, StringComparison.Ordinal))
            {
                return false;
            }

            tab.SetDocumentTextSingleUndo(before);
            DebugLog.Write("Mend", $"change undone from the diff window: {tab.ResourceBookPath}");
            return true;
        });
        MendDiffRequested?.Invoke(this, diff);
    }

    /// <summary>
    /// After File → Save / Save As (<paramref name="saved"/>): refreshes the "Find Usages" panel as its "Refresh" button
    /// would — when Preferences → General enables it, the panel is shown (docked, floating or collapsed by Auto Hide)
    /// and it holds a search. Unlike "Refresh" it does not take the focus or write to the status bar.
    /// </summary>
    public void RefreshFindUsagesAfterSave(bool saved = true)
    {
        if (!saved || !_settings.FindUsagesRefreshOnSave || _currentBook is null
            || !_findUsages.HasSearch || !_dockFactory.IsToolVisible(DockableIds.FindUsages))
        {
            return;
        }

        _tabManager.SaveAllTabs();
        if (_findUsages.ClassName is { } className)
        {
            _findUsages.Load(className, _currentBook.FindClassUsages(className));
            DebugLog.Write("FindUsages", $"refreshed after save: {className}");
        }
        else
        {
            _findUsages.Refresh();
            DebugLog.Write("FindUsages", $"refreshed after save: {_findUsages.Header}");
        }
    }

    /// <summary>
    /// Navigation from the "Validation Results" panel (double-clicking a row) — opens the
    /// resource and scrolls to the reported line (line-based only — the only validation, the
    /// Well-Formed Check, does not set a character offset).
    /// </summary>
    private void NavigateToValidationResult(ValidationResult result)
    {
        if (_currentBook is null)
        {
            return;
        }

        Resource? resource = _currentBook.GetAllResources()
            .FirstOrDefault(r => string.Equals(r.BookPath, result.BookPath, StringComparison.Ordinal));
        if (resource is null)
        {
            _statusBar.ShowMessage(Strings.Format("Status_ResourceMissing", result.BookPath), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _tabManager.OpenResource(resource);
        if (result.Line > 0 && ActiveCodeTab is { } tab)
        {
            tab.GoToLine(result.Line);
        }
    }

    /// <summary>
    /// "Create HTML from Table Of Contents": creates (or overwrites) the <c>TOC.xhtml</c> file with the
    /// book's table of contents, adds <c>sgc-toc.css</c> if needed and opens the result in a tab
    /// (no overwrite confirmation dialog).
    /// </summary>
    private async Task CreateHtmlTocAsync()
    {
        if (_currentBook is not { } book)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NoBookOpen"), TimeSpan.FromSeconds(4));
            return;
        }

        _tabManager.SaveAllTabs();
        string title = ActionLabel(AppActionIds.CreateHtmlToc);
        BookOperationOutcome<HtmlResource> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => TocGenerator.CreateHtmlToc(book)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        HtmlResource tocResource = run.Result!;

        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            view.Reload();
        }

        BookBrowser.Refresh();
        _tabManager.OpenResources(new Resource[] { tocResource });
        _preview.Refresh();
        _toc.Refresh();
        _statusBar.ShowMessage(Strings.Get("Status_HtmlTocCreated"), TimeSpan.FromSeconds(4));
    }

    private void AddNavToSpine(bool nonlinear)
    {
        if (_currentBook is null || !_currentBook.IsEpub3)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NotAvailableForEpub2"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        HtmlResource? nav = _currentBook.GetNavResource();
        if (nav is null)
        {
            return;
        }

        _currentBook.GetOpf().AppendResourceToSpine(nav, nonlinear);
        _currentBook.Modified = true;
        BookBrowser.Refresh();
        _statusBar.ShowMessage(
            nonlinear ? Strings.Get("Status_NavAddedToSpineNonLinear") : Strings.Get("Status_NavAddedToSpine"),
            TimeSpan.FromSeconds(4));
    }

    private void RemoveNavFromSpine()
    {
        if (_currentBook is null || !_currentBook.IsEpub3)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NotAvailableForEpub2"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        HtmlResource? nav = _currentBook.GetNavResource();
        if (nav is null)
        {
            return;
        }

        _currentBook.GetOpf().RemoveResourceFromSpine(nav);
        _currentBook.Modified = true;
        BookBrowser.Refresh();
        _statusBar.ShowMessage(Strings.Get("Status_NavRemovedFromSpine"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// "Well-Formed Check EPUB" (F7) — checks all XHTML resources of the book
    /// (<see cref="BookValidator.ValidateCurrentBook"/>) and shows the results in the "Validation
    /// Results" panel, using <see cref="Core.BookManipulation.WellFormedChecker"/>.
    /// </summary>
    private async Task WellFormedCheckEpubAsync()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        IReadOnlyList<ValidationResult>? results = await RunBookCheckAsync(_currentBook).ConfigureAwait(true);
        if (results is null)
        {
            return;
        }

        if (!_dockFactory.IsToolVisible(DockableIds.ValidationResults))
        {
            _dockFactory.ToggleTool(DockableIds.ValidationResults);
        }

        _dockFactory.FocusTool(DockableIds.ValidationResults);
        _statusBar.ShowMessage(
            results.Count == 0 ? Strings.Get("ValidationResultsView_NoProblems") : Strings.Format("Status_ProblemsFound", results.Count),
            TimeSpan.FromSeconds(4));
    }

    // Checks the book without the skipped rules (under the progress window, which can cancel it: the check only reads the book)
    // and shows the results in the Validation Results panel. Null when it was cancelled.
    private async Task<IReadOnlyList<ValidationResult>?> RunBookCheckAsync(Book book)
    {
        IReadOnlyList<string> skipped = _settings.CheckBookSkippedRules;
        BookOperationOutcome<IReadOnlyList<ValidationResult>> run = await RunBookOperationAsync(
            ActionLabel(AppActionIds.WellFormedCheckEpub), checkpointName: null, editsBook: false,
            (progress, cancel) => BookValidator.ValidateCurrentBook(book, skipped, new ValidationRun(new ValidationProgressMap(progress), cancel)))
            .ConfigureAwait(true);
        if (!run.Completed)
        {
            return null;
        }

        _validationResults.LoadResults(run.Result!);
        _validationResults.SetSkippedRules(skipped);
        return run.Result;
    }

    /// <summary>
    /// "Fix" / "Fix all" in the Validation Results panel: applies the automatic fixes after an automatic checkpoint
    /// (so the Checkpoints panel can undo them), reloads the open tabs and checks the book again.
    /// </summary>
    private async Task FixValidationResultsAsync(IReadOnlyList<ValidationResult> results)
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        string title = Strings.Get("CheckpointOp_FixProblems");
        BookOperationOutcome<int> run = await RunBookOperationAsync(
            title, title, editsBook: true,
            (progress, cancel) =>
            {
                int fixedSoFar = 0;
                for (int i = 0; i < results.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress.Report(new WorkProgress(Strings.Format("Progress_Fixing", i + 1, results.Count), WorkProgress.FractionOf(i, results.Count)));
                    if (results[i].Fix?.Apply(book) == true)
                    {
                        fixedSoFar++;
                    }
                }

                return fixedSoFar;
            }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        int fixedCount = run.Result;
        if (fixedCount == 0)
        {
            if (run.Checkpoint)
            {
                RewindCheckpoint();
            }

            await RunBookCheckAsync(book).ConfigureAwait(true);
            _statusBar.ShowMessage(Strings.Get("Status_NothingToFix"), TimeSpan.FromSeconds(4));
            return;
        }

        book.Modified = true;
        RefreshAfterMaintenanceOperation();
        _toc.Refresh();
        IReadOnlyList<ValidationResult>? remaining = await RunBookCheckAsync(book).ConfigureAwait(true);
        if (remaining is not null)
        {
            _statusBar.ShowMessage(Strings.Format("Status_ProblemsFixed", fixedCount, remaining.Count), TimeSpan.FromSeconds(5));
        }
    }

    // "Skip this type of problem": remembered in the settings, the results are filtered right away.
    private void SkipValidationRule(string code)
    {
        List<string> skipped = _settings.CheckBookSkippedRules.ToList();
        if (!skipped.Contains(code, StringComparer.Ordinal))
        {
            skipped.Add(code);
            _settings.CheckBookSkippedRules = skipped;
        }

        _validationResults.LoadResults(_validationResults.Rows.Select(r => r.Result).Where(r => r.Code != code).ToList());
        _validationResults.SetSkippedRules(skipped);
        _statusBar.ShowMessage(Strings.Get("Status_ProblemTypeSkipped"), TimeSpan.FromSeconds(5));
    }

    // "Restore": the rules are checked again (the book is checked again when one is open).
    private async Task RestoreValidationRulesAsync(IReadOnlyList<string> codes)
    {
        List<string> skipped = _settings.CheckBookSkippedRules.Where(c => !codes.Contains(c, StringComparer.Ordinal)).ToList();
        _settings.CheckBookSkippedRules = skipped;
        if (_currentBook is { } book)
        {
            _tabManager.SaveAllTabs();
            await RunBookCheckAsync(book).ConfigureAwait(true);
        }
        else
        {
            _validationResults.SetSkippedRules(skipped);
        }
    }

    /// <summary>
    /// "Validate Stylesheets With W3C" — for each CSS stylesheet writes a local
    /// page with an auto-submitted form (<see cref="W3CValidation"/>) and opens it in the default
    /// browser (<see cref="_externalFileOpener"/>). No network request is sent
    /// from the application: it is the user's browser that POSTs the data after the page opens.
    /// </summary>
    private void ValidateStylesheetsWithW3C()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        IReadOnlyList<CssResource> cssResources = _currentBook.GetCssResources();
        if (cssResources.Count == 0)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NoStylesheetsToValidate"), TimeSpan.FromSeconds(4));
            return;
        }

        ValidateCssResourcesWithW3C(cssResources);
    }

    /// <summary>
    /// "Validate With W3C" from the BookBrowser context menu — a per-file variant
    /// next to the global <see cref="ValidateStylesheetsWithW3C"/> (Tools menu, validates
    /// all stylesheets at once). It reuses the same validation page building logic.
    /// </summary>
    private void OnValidateSelectedCssWithW3C(object? sender, IReadOnlyList<CssResource> cssResources)
    {
        if (cssResources.Count == 0)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        ValidateCssResourcesWithW3C(cssResources);
    }

    private void ValidateCssResourcesWithW3C(IReadOnlyList<CssResource> cssResources)
    {
        foreach (CssResource css in cssResources)
        {
            string profile = css.EpubVersion.StartsWith('3')
                ? _settings.CssEpub3ValidationSpec
                : _settings.CssEpub2ValidationSpec;
            string html = W3CValidation.BuildCssValidationHtml(css.GetText(), profile);
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"signet-w3c-{Guid.NewGuid():N}.html");
            System.IO.File.WriteAllText(tempPath, html);
            _externalFileOpener(tempPath);
        }
    }

    /// <summary>
    /// "Upgrade to EPUB 3" — converts an EPUB 2 book in place (<see cref="EpubUpgrader"/>): a nav from the NCX and the
    /// guide, EPUB 3 metadata and manifest properties, HTML5 DOCTYPEs. The NCX and the guide stay. An automatic
    /// checkpoint before the conversion makes it undoable.
    /// </summary>
    private async Task UpgradeToEpub3Async()
    {
        if (_currentBook is not { } book)
        {
            return;
        }

        if (_currentBook.IsEpub3)
        {
            _statusBar.ShowMessage(Strings.Get("Status_AlreadyEpub3"), TimeSpan.FromSeconds(4));
            return;
        }

        _tabManager.SaveAllTabs();
        string title = ActionLabel(AppActionIds.UpgradeToEpub3);
        BookOperationOutcome<EpubUpgradeResult?> run = await RunBookOperationAsync(
            title, title, editsBook: true, (_, _) => EpubUpgrader.UpgradeToEpub3(book)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        EpubUpgradeResult result = run.Result!;

        RefreshAfterMaintenanceOperation();
        _toc.Refresh();
        RefreshTitle();
        _statusBar.ShowMessage(
            Strings.Format("Status_UpgradedToEpub3", result.TocEntries, result.Landmarks, result.Doctypes),
            TimeSpan.FromSeconds(6));
    }

    /// <summary>
    /// "Generate NCX/Guide for epub2 e-readers" — builds the NCX from the current nav (toc +
    /// page-list) and rebuilds the OPF guide from the nav's landmarks, using
    /// <see cref="NavProcessor.ToNcx"/> (toc/page-list from the nav → NCX navMap/pageList).
    /// </summary>
    private void GenerateNcxGuideFromNav()
    {
        if (_currentBook is null || !_currentBook.IsEpub3)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NotAvailableForEpub2"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _tabManager.SaveAllTabs();

        HtmlResource? nav = _currentBook.GetNavResource();
        if (nav is null || nav.GetText().Length == 0)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NcxGuideFailed"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        NcxResource? ncx = _currentBook.GetNcx();
        if (ncx is null)
        {
            ncx = _currentBook.GetFolderKeeper().AddNcxToFolder(_currentBook.EpubVersion);
            string ncxId = _currentBook.GetOpf().AddNcxItem(ncx.BookPath);
            _currentBook.GetOpf().UpdateNcxOnSpine(ncxId);
        }

        string docTitle = _currentBook.GetOpf().GetPrimaryBookTitle();
        if (docTitle.Length == 0)
        {
            docTitle = Strings.Get("Common_Unknown");
        }

        NavProcessor navProcessor = new(nav);
        NcxDocument document = navProcessor.ToNcx(
            ncx.BookPath, docTitle, _currentBook.GetOpf().GetMainIdentifierValue(), epub2: false);
        ncx.SetText(document.ToXml());

        _currentBook.GetOpf().ClearSemanticCodesInGuide();
        foreach (LandmarkInfo landmark in navProcessor.GetAllLandmarkInfoByBookPath())
        {
            string guideCode = Landmarks.GuideLandMapping(landmark.Code);
            if (guideCode.Length == 0)
            {
                continue;
            }

            if (_currentBook.GetFolderKeeper().GetResourceByBookPathNoThrow(landmark.BookPath) is HtmlResource html)
            {
                _currentBook.GetOpf().AddGuideSemanticCode(html, guideCode, toggle: false, tgtId: landmark.Fragment);
            }
        }

        _toc.Refresh();
        BookBrowser.Refresh();
        _currentBook.Modified = true;
        _statusBar.ShowMessage(Strings.Get("Status_NcxGuideGenerated"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// "Remove the NCX and Guide" — removes the NCX resource (if any) and clears the OPF guide.
    /// </summary>
    private void RemoveNcxGuideFromEpub3()
    {
        if (_currentBook is null || !_currentBook.IsEpub3)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NotAvailableForEpub2"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _tabManager.SaveAllTabs();

        if (_currentBook.GetNcx() is not null)
        {
            _currentBook.GetOpf().RemoveNcxOnSpine();
            _currentBook.GetFolderKeeper().RemoveNcxFromFolder();
        }

        _currentBook.GetOpf().ClearSemanticCodesInGuide();

        _toc.Refresh();
        BookBrowser.Refresh();
        _currentBook.Modified = true;
        _statusBar.ShowMessage(Strings.Get("Status_NcxGuideRemoved"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// "Update OPF Manifest Media Types" — refreshes the MIME types of all manifest entries
    /// according to the current resources.
    /// </summary>
    private void UpdateManifestMediaTypes()
    {
        if (_currentBook is null)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        _currentBook.GetOpf().UpdateManifestMediaTypes(_currentBook.GetAllResources());
        _currentBook.Modified = true;
        _statusBar.ShowMessage(Strings.Get("Status_ManifestMediaTypesUpdated"), TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// "Update Manifest Properties" — refreshes the manifest <c>properties</c> attribute
    /// (e.g. <c>nav</c>/<c>scripted</c>/<c>svg</c>/<c>cover-image</c>) for HTML files.
    /// </summary>
    private void UpdateManifestProperties()
    {
        if (_currentBook is null || !_currentBook.IsEpub3)
        {
            _statusBar.ShowMessage(Strings.Get("Status_NotAvailableForEpub2"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _tabManager.SaveAllTabs();
        _currentBook.GetOpf().UpdateManifestProperties(_currentBook.GetHtmlResources());
        _currentBook.Modified = true;
        _statusBar.ShowMessage(Strings.Get("Status_ManifestPropertiesUpdated"), TimeSpan.FromSeconds(4));
    }

    // Zoom In/Out/Reset act on whichever view (Code Editor / Preview) has focus —
    // the zoom values are independent.
    private void AdjustActiveZoom(double factor)
    {
        if (_previewHasZoomFocus && _preview.HasContent)
        {
            _preview.ApplyZoom(factor);
        }
        else
        {
            AdjustTabZoom(factor);
        }
    }

    private void AdjustTabZoom(double factor)
    {
        if (ActiveTab is { } tab)
        {
            tab.ZoomFactor = Math.Clamp(tab.ZoomFactor * factor, 0.5, 4.0);
        }
    }

    private void ResetActiveZoom()
    {
        if (_previewHasZoomFocus && _preview.HasContent)
        {
            _preview.ResetZoom();
        }
        else
        {
            ZoomReset();
        }
    }

    private void OnActiveTabChanged(object? sender, EventArgs e)
    {
        NoteNavigationTabSwitch();
        if (_activeCaretTab is not null)
        {
            _activeCaretTab.PropertyChanged -= OnActiveTabPropertyChanged;
            _activeCaretTab.CaretOffsetChanged -= OnActiveCaretOffsetChanged;
            _activeCaretTab.ContentChanged -= OnActiveContentChanged;
        }

        _activeCaretTab = ActiveCodeTab;
        if (_activeCaretTab is not null)
        {
            _activeCaretTab.PropertyChanged += OnActiveTabPropertyChanged;
            _activeCaretTab.CaretOffsetChanged += OnActiveCaretOffsetChanged;
            _activeCaretTab.ContentChanged += OnActiveContentChanged;
        }

        _findReplace.AttachToActiveTab(ActiveCodeTab);
        _toc.SetCurrentFile(ActiveTab?.Resource.BookPath);
        if (_settings.BookBrowserFollowsActiveTab && ActiveTab is { } shown)
        {
            BookBrowser.SelectResource(shown.Resource);
        }

        // Clicking a document tab moves the zoom focus to Code Editor.
        _previewHasZoomFocus = false;

        OnPropertyChanged(nameof(ZoomPercentText));
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(ActiveCodeTab));
        OnPropertyChanged(nameof(CaretStatus));
        OnPropertyChanged(nameof(TabStatus));
        RefreshFormatActionState();
        RefreshInsertActionState();
        RefreshSpellcheckActionState();

        // Closing the active tab passes through "no active tab" before its neighbour is activated —
        // the preview is emptied only once no tab is left (otherwise it would flicker).
        if (_tabManager.Count == 0)
        {
            _preview.Clear();
        }
        else
        {
            _preview.ShowResource(ActiveTab?.Resource);
        }

        LiveCssContextChanged?.Invoke(this, EventArgs.Empty);
        UpdateNavigationPlace();
    }

    // Only the caret of the file shown in the preview — an offset in a CSS/JS tab does not correspond
    // to any place on the page (the preview stays on the previous HTML).
    private void OnActiveCaretOffsetChanged(int offset)
    {
        UpdateNavigationPlace();
        if (string.Equals(ActiveCodeTab?.ResourceBookPath, _preview.CurrentBookPath, StringComparison.Ordinal))
        {
            _preview.SyncCaretToPreview(offset);
        }

        LiveCssContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnActiveContentChanged(object? sender, EventArgs e)
    {
        _preview.NotifyContentChanged();
        LiveCssContextChanged?.Invoke(this, EventArgs.Empty);
    }

    // A click in the preview always refers to the file shown in the preview. When another tab is
    // active (a CSS file, a different HTML file, an image…), the previewed file's tab is activated
    // (or opened) first, so the caret jump never lands in an unrelated file.
    private void OnPreviewCodeCaretJumpRequested(object? sender, int offset)
    {
        string? previewBookPath = _preview.CurrentBookPath;
        if (previewBookPath is null || _currentBook is null)
        {
            return;
        }

        if (!string.Equals(ActiveCodeTab?.ResourceBookPath, previewBookPath, StringComparison.Ordinal))
        {
            Resource? resource = _currentBook.GetAllResources()
                .FirstOrDefault(r => string.Equals(r.BookPath, previewBookPath, StringComparison.Ordinal));
            if (resource is null)
            {
                return;
            }

            _tabManager.OpenResource(resource);
        }

        if (ActiveCodeTab is { } tab && string.Equals(tab.ResourceBookPath, previewBookPath, StringComparison.Ordinal))
        {
            tab.GoToOffset(offset);
        }
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentTabViewModel.ZoomFactor))
        {
            OnPropertyChanged(nameof(ZoomPercentText));
        }
        else if (e.PropertyName == nameof(CodeTabViewModel.CaretStatus))
        {
            OnPropertyChanged(nameof(CaretStatus));
        }
        else if (e.PropertyName == nameof(CodeTabViewModel.SecondaryStatus))
        {
            OnPropertyChanged(nameof(TabStatus));
        }
        else if (e.PropertyName is nameof(CodeTabViewModel.CaretBlockElement)
            or nameof(CodeTabViewModel.RemoveFormattingEnabled)
            or nameof(CodeTabViewModel.RemoveTagPairEnabled)
            or nameof(CodeTabViewModel.UnwrapDivEnabled)
            or nameof(CodeTabViewModel.MergeCandidate))
        {
            RefreshFormatActionState();
        }
        else if (e.PropertyName is nameof(CodeTabViewModel.InsertFileEnabled)
            or nameof(CodeTabViewModel.InsertIdEnabled)
            or nameof(CodeTabViewModel.InsertHyperlinkEnabled))
        {
            RefreshInsertActionState();
        }
    }

    [RelayCommand]
    private void CustomizeToolbars() => CustomizeToolbarsRequested?.Invoke(this, EventArgs.Empty);

    private void RestoreSideRegionWidths()
    {
        if (_settings.MainWindowDockLayout is { } state)
        {
            _dockFactory.ApplySideRegionWidths(state);
        }
    }

    private void RestorePanelVisibility()
    {
        IReadOnlyDictionary<string, string> stored = _settings.GetStringMap(DockPanelsGroup);
        if (stored.Count > 0)
        {
            _dockFactory.ApplyToolVisibility(stored);
        }

        _dockFactory.ApplyPinnedSizes(_settings.GetStringMap(DockPinnedSizesGroup));
    }

    private void WirePanelActions()
    {
        WireToggle(AppActionIds.ToggleBookBrowser, DockableIds.BookBrowser);
        WireToggle(AppActionIds.ToggleClips, DockableIds.Clips);
        WireToggle(AppActionIds.TogglePreview, DockableIds.Preview);
        WireToggle(AppActionIds.ToggleToc, DockableIds.TableOfContents);
        WireToggle(AppActionIds.ToggleValidationResults, DockableIds.ValidationResults);
        WireToggle(AppActionIds.ToggleFindUsages, DockableIds.FindUsages);
        _actions.SetHandler(AppActionIds.FindUsages, FindUsagesAtCaret);
        _actions.SetHandler(AppActionIds.RenameClass, RenameClassAtCaret);
        _actions.SetHandler(AppActionIds.ToggleFindReplace, () =>
        {
            if (IsFindReplaceVisible)
            {
                HideFindPanel();
            }
            else
            {
                ShowFindPanel();
            }
        });
        _actions.SetHandler(AppActionIds.SearchEditor, () => SearchEditorRequested?.Invoke(this, EventArgs.Empty));

        WireFocus(AppActionIds.FocusBookBrowser, DockableIds.BookBrowser, Strings.Get("Panel_BookBrowser"));
        WireFocus(AppActionIds.FocusPreview, DockableIds.Preview, Strings.Get("Panel_Preview"));
        WireFocus(AppActionIds.FocusToc, DockableIds.TableOfContents, Strings.Get("Panel_TableOfContents"));
        WireFocus(AppActionIds.FocusClips, DockableIds.Clips, Strings.Get("Panel_Clips"));
        WireFocus(AppActionIds.FocusCodeEditor, DockableIds.Documents, Strings.Get("Panel_CodeEditor"));
    }

    private void WireToggle(string actionId, string dockableId) =>
        _actions.SetHandler(actionId, () => _dockFactory.ToggleTool(dockableId));

    // Activates the panel in the dock + a status bar message; for Code Editor it additionally
    // gives real focus to the editor control, and for Preview/Code it switches the target of the zoom actions.
    private void WireFocus(string actionId, string dockableId, string label)
    {
        _actions.SetHandler(actionId, () =>
        {
            _dockFactory.FocusTool(dockableId);
            _previewHasZoomFocus = dockableId == DockableIds.Preview;

            if (dockableId == DockableIds.Documents)
            {
                ActiveCodeTab?.RequestFocus();
            }

            _statusBar.ShowMessage(Strings.Format("Status_Focus", label), TimeSpan.FromSeconds(3));
        });
    }

    // The heading group + "Preserve Existing Attributes" render in the menu with a check mark.
    // Must run before the menu is built.
    private void MarkCheckableFormatActions()
    {
        foreach ((string actionId, string _) in HeadingActions)
        {
            if (_actions.Get(actionId) is { } action)
            {
                action.IsCheckable = true;
            }
        }

        if (_actions.Get(AppActionIds.HeadingPreserveAttributes) is { } preserve)
        {
            preserve.IsCheckable = true;
        }
    }

    private void WireFormatActions()
    {
        _actions.SetHandler(AppActionIds.Bold, () => ActiveCodeTab?.Bold());
        _actions.SetHandler(AppActionIds.Italic, () => ActiveCodeTab?.Italic());
        _actions.SetHandler(AppActionIds.Underline, () => ActiveCodeTab?.Underline());
        _actions.SetHandler(AppActionIds.Strikethrough, () => ActiveCodeTab?.Strikethrough());
        _actions.SetHandler(AppActionIds.Subscript, () => ActiveCodeTab?.Subscript());
        _actions.SetHandler(AppActionIds.Superscript, () => ActiveCodeTab?.Superscript());
        _actions.SetHandler(AppActionIds.AlignLeft, () => ActiveCodeTab?.AlignLeft());
        _actions.SetHandler(AppActionIds.AlignCenter, () => ActiveCodeTab?.AlignCenter());
        _actions.SetHandler(AppActionIds.AlignRight, () => ActiveCodeTab?.AlignRight());
        _actions.SetHandler(AppActionIds.AlignJustify, () => ActiveCodeTab?.AlignJustify());
        _actions.SetHandler(AppActionIds.InsertBulletedList, () => ActiveCodeTab?.InsertBulletedList());
        _actions.SetHandler(AppActionIds.InsertNumberedList, () => ActiveCodeTab?.InsertNumberedList());
        _actions.SetHandler(AppActionIds.IncreaseIndent, () => ActiveCodeTab?.IncreaseIndent());
        _actions.SetHandler(AppActionIds.DecreaseIndent, () => ActiveCodeTab?.DecreaseIndent());
        _actions.SetHandler(AppActionIds.TextDirectionLtr, () => ActiveCodeTab?.TextDirection("ltr"));
        _actions.SetHandler(AppActionIds.TextDirectionRtl, () => ActiveCodeTab?.TextDirection("rtl"));
        _actions.SetHandler(AppActionIds.TextDirectionDefault, () => ActiveCodeTab?.TextDirection("default"));
        _actions.SetHandler(AppActionIds.RemoveFormatting, () => ActiveCodeTab?.RemoveFormatting());
        _actions.SetHandler(AppActionIds.RemoveTagPair, () => ActiveCodeTab?.RemoveTagPair());
        _actions.SetHandler(AppActionIds.InsertClosingTag, () => ActiveCodeTab?.InsertClosingTag());
        _actions.SetHandler(AppActionIds.ToggleComment, () => ActiveCodeTab?.ToggleComment());
        _actions.SetHandler(AppActionIds.CasingLowercase, () => ActiveCodeTab?.ChangeCasing(Casing.Lowercase));
        _actions.SetHandler(AppActionIds.CasingUppercase, () => ActiveCodeTab?.ChangeCasing(Casing.Uppercase));
        _actions.SetHandler(AppActionIds.CasingTitlecase, () => ActiveCodeTab?.ChangeCasing(Casing.Titlecase));
        _actions.SetHandler(AppActionIds.CasingCapitalize, () => ActiveCodeTab?.ChangeCasing(Casing.Capitalize));

        foreach ((string actionId, string element) in HeadingActions)
        {
            string el = element;
            _actions.SetHandler(actionId, () => ActiveCodeTab?.HeadingStyle(el, _preserveHeadingAttributes));
        }

        _actions.SetHandler(AppActionIds.HeadingPreserveAttributes, ToggleHeadingPreserveAttributes);

        RefreshFormatActionState();
    }

    private void ToggleHeadingPreserveAttributes()
    {
        _preserveHeadingAttributes = !_preserveHeadingAttributes;
        if (_actions.Get(AppActionIds.HeadingPreserveAttributes) is { } action)
        {
            action.IsChecked = _preserveHeadingAttributes;
        }

        _statusBar.ShowMessage(
            _preserveHeadingAttributes
                ? Strings.Get("Status_PreserveHeadingAttributesOn")
                : Strings.Get("Status_PreserveHeadingAttributesOff"),
            TimeSpan.FromSeconds(4));
    }

    private void RefreshFormatActionState()
    {
        CodeTabViewModel? tab = ActiveCodeTab;
        bool isHtml = tab?.SupportsFormatting ?? false;

        _actions.SetEnabled(isHtml, FormatActionIds);
        _actions.SetEnabled(isHtml && (tab?.RemoveFormattingEnabled ?? false), AppActionIds.RemoveFormatting);
        _actions.SetEnabled(isHtml && (tab?.RemoveTagPairEnabled ?? false), AppActionIds.RemoveTagPair);
        _actions.SetEnabled(tab?.SupportsCommentToggle ?? false, AppActionIds.ToggleComment);
        _actions.SetEnabled(
            tab?.IsHtmlFlow ?? false, AppActionIds.PrettifyCurrentHtml, AppActionIds.MendCurrentHtml, AppActionIds.SplitSection,
            AppActionIds.SplitOnSgfSectionMarkers);
        _actions.SetEnabled(tab?.IsCss ?? false, AppActionIds.PrettifyCurrentCss);
        _actions.SetEnabled(
            tab?.SupportsTagStructure ?? false,
            AppActionIds.JumpToOpeningTag, AppActionIds.JumpToClosingTag, AppActionIds.SelectTagContents,
            AppActionIds.RenameTag, AppActionIds.SplitTag);
        _actions.SetEnabled(tab?.UnwrapDivEnabled ?? false, AppActionIds.UnwrapDiv);

        // "Merge Content" — a label with the element name and count when merging is possible.
        ElementMergeCandidate? merge = isHtml ? tab?.MergeCandidate : null;
        _actions.SetEnabled(merge is not null, AppActionIds.MergeContent);
        if (_actions.Get(AppActionIds.MergeContent) is { } mergeAction)
        {
            mergeAction.Text = merge is null
                ? AppAction.ConvertMnemonics(mergeAction.DefaultText)
                : CodeTabViewModel.MergeContentTextFor(merge).Replace("_", "__", StringComparison.Ordinal);
        }

        string block = tab?.CaretBlockElement ?? string.Empty;
        foreach ((string actionId, string element) in HeadingActions)
        {
            if (_actions.Get(actionId) is { } action)
            {
                action.IsChecked = isHtml && string.Equals(block, element, StringComparison.Ordinal);
            }
        }
    }

    // ------------------------------------------------- Spellcheck --- //

    /// <summary>
    /// Toggles the global highlighting of misspelled words, saves it in
    /// <see cref="SettingsStore"/> and refreshes the underlines in all open Code Editor tabs.
    /// </summary>
    private void ToggleAutoSpellCheck()
    {
        _settings.SpellCheck = !_settings.SpellCheck;
        if (_actions.Get(AppActionIds.AutoSpellCheck) is { } action)
        {
            action.IsChecked = _settings.SpellCheck;
        }

        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            if (view is CodeTabViewModel codeTab)
            {
                codeTab.NotifySpellCheckSettingChanged();
            }
        }

        RefreshSpellcheckActionState();
    }

    /// <summary>
    /// Toggles word wrap in Code Editor, saves it in <see cref="SettingsStore"/> and
    /// applies it to all open tabs.
    /// </summary>
    private void ToggleWordWrap()
    {
        _settings.CodeEditorWordWrap = !_settings.CodeEditorWordWrap;
        if (_actions.Get(AppActionIds.WordWrap) is { } action)
        {
            action.IsChecked = _settings.CodeEditorWordWrap;
        }

        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            if (view is CodeTabViewModel codeTab)
            {
                codeTab.WordWrap = _settings.CodeEditorWordWrap;
            }
        }
    }

    /// <summary>Clears the session list of ignored words and refreshes the underlines.</summary>
    private void ClearIgnoredWords()
    {
        _spellChecker.ClearIgnoredWords();
        foreach (ContentTabViewModel view in _tabManager.OpenTabViews)
        {
            if (view is CodeTabViewModel codeTab)
            {
                codeTab.NotifySpellCheckSettingChanged();
            }
        }
    }

    /// <summary>
    /// Enables/disables the spellcheck actions depending on the active tab (available only for
    /// (X)HTML with spell checking enabled).
    /// </summary>
    private void RefreshSpellcheckActionState()
    {
        bool enabled = (ActiveCodeTab?.SupportsSpellcheck ?? false) && _settings.SpellCheck;
        _actions.SetEnabled(enabled, AppActionIds.Spellcheck, AppActionIds.AddMisspelledWord, AppActionIds.IgnoreMisspelledWord);
    }

    // ------------------------------------------------- "Insert" menu --- //

    private void WireInsertActions()
    {
        _actions.SetHandler(
            AppActionIds.InsertSpecialCharacter,
            () => InsertSpecialCharacterRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(
            AppActionIds.InsertFile,
            () => InsertFileRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(
            AppActionIds.InsertId,
            () => InsertIdRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(
            AppActionIds.InsertHyperlink,
            () => InsertHyperlinkRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(
            AppActionIds.InsertClip,
            () => InsertAriaClipRequested?.Invoke(this, EventArgs.Empty));

        RefreshInsertActionState();
    }

    private void RefreshInsertActionState()
    {
        CodeTabViewModel? tab = ActiveCodeTab;
        bool isHtml = tab?.SupportsFormatting ?? false;

        _actions.SetEnabled(isHtml, AppActionIds.InsertSpecialCharacter, AppActionIds.InsertClip);
        _actions.SetEnabled(isHtml && (tab?.InsertFileEnabled ?? false), AppActionIds.InsertFile);
        _actions.SetEnabled(isHtml && (tab?.InsertIdEnabled ?? false), AppActionIds.InsertId);
        _actions.SetEnabled(isHtml && (tab?.InsertHyperlinkEnabled ?? false), AppActionIds.InsertHyperlink);
        _actions.SetEnabled(tab is not null, AppActionIds.PasteClipboardHistory);
    }

    // ------------------------------------------------- Clips --- //

    /// <summary>
    /// View model of the clip library — for the view (the "Clip Editor" window, the docked "Clips" panel,
    /// the library picker dialog).
    /// </summary>
    public ClipsViewModel Clips => _clips;

    private static bool IsBookBrowserAction(AppAction action) =>
        string.Equals(action.Category, AppActionIds.BookBrowserCategory, StringComparison.Ordinal);

    /// <summary>
    /// Connects the Book Browser context menu actions to the panel's commands so that a
    /// shortcut can be assigned to them in Preferences. A shortcut runs exactly the same command as the menu
    /// item — together with its CanExecute condition (e.g. no selection = the action does nothing).
    /// </summary>
    private void WireBookBrowserActions()
    {
        void Wire(string id, IRelayCommand command) =>
            _actions.SetHandler(id, () =>
            {
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }
            });

        Wire(AppActionIds.BookBrowserOpen, BookBrowser.OpenSelectedCommand);
        Wire(AppActionIds.BookBrowserRename, BookBrowser.RenameSelectedCommand);
        Wire(AppActionIds.BookBrowserRenameWithTemplate, BookBrowser.RenameSelectedWithTemplateCommand);
        Wire(AppActionIds.BookBrowserRegexRename, BookBrowser.BulkRegexRenameSelectedCommand);
        Wire(AppActionIds.BookBrowserDelete, BookBrowser.DeleteSelectedCommand);
        Wire(AppActionIds.BookBrowserMove, BookBrowser.MoveSelectedCommand);
        Wire(AppActionIds.BookBrowserMoveUp, BookBrowser.MoveTextUpCommand);
        Wire(AppActionIds.BookBrowserMoveDown, BookBrowser.MoveTextDownCommand);
        Wire(AppActionIds.BookBrowserMoveToTop, BookBrowser.MoveTextToTopCommand);
        Wire(AppActionIds.BookBrowserMoveToBottom, BookBrowser.MoveTextToBottomCommand);
        Wire(AppActionIds.BookBrowserSort, BookBrowser.SortTextCommand);
        Wire(AppActionIds.BookBrowserMerge, BookBrowser.MergeSelectedCommand);
        Wire(AppActionIds.BookBrowserSplit, BookBrowser.SplitSelectedCommand);
        Wire(AppActionIds.BookBrowserAddBlankHtml, BookBrowser.AddBlankHtmlCommand);
        Wire(AppActionIds.BookBrowserAddBlankCss, BookBrowser.AddBlankCssCommand);
        Wire(AppActionIds.BookBrowserAddBlankSvg, BookBrowser.AddBlankSvgCommand);
        Wire(AppActionIds.BookBrowserAddBlankJs, BookBrowser.AddBlankJsCommand);
        Wire(AppActionIds.BookBrowserAddExistingFiles, BookBrowser.AddExistingFilesCommand);
        Wire(AppActionIds.BookBrowserAddFontFiles, BookBrowser.AddFontFilesCommand);
        Wire(AppActionIds.BookBrowserAddSemantics, BookBrowser.AddSemanticsSelectedCommand);
        Wire(AppActionIds.BookBrowserCoverImage, BookBrowser.CoverImageSelectedCommand);
        Wire(AppActionIds.BookBrowserMarkAsNav, BookBrowser.MarkAsNavSelectedCommand);
        Wire(AppActionIds.BookBrowserLinkStylesheets, BookBrowser.LinkStylesheetsSelectedCommand);
        Wire(AppActionIds.BookBrowserLinkJavascripts, BookBrowser.LinkJavascriptsSelectedCommand);
        Wire(AppActionIds.BookBrowserValidateWithW3C, BookBrowser.ValidateSelectedWithW3CCommand);
        Wire(AppActionIds.BookBrowserAddCopy, BookBrowser.AddCopySelectedCommand);
        Wire(AppActionIds.BookBrowserRenumberToc, BookBrowser.RenumberTocSelectedCommand);
        Wire(AppActionIds.BookBrowserSaveAs, BookBrowser.SaveAsSelectedCommand);
        Wire(AppActionIds.BookBrowserSelectAll, BookBrowser.SelectAllInFolderCommand);
        Wire(AppActionIds.BookBrowserGetInfo, BookBrowser.GetInfoSelectedCommand);
    }

    private void WireClipActions()
    {
        _actions.SetHandler(AppActionIds.ClipEditor, () => ClipEditorRequested?.Invoke(this, EventArgs.Empty));
        _actions.SetHandler(AppActionIds.SelectClip, () => SelectClipRequested?.Invoke(this, EventArgs.Empty));

        for (int slot = 1; slot <= AppActionIds.ClipSlotCount; slot++)
        {
            int captured = slot;
            _actions.SetHandler(AppActionIds.Clip(captured), () => PasteClipSlot(captured));
        }

        RefreshClipActions();
    }

    private void PasteClipSlot(int slot)
    {
        ClipEditorNode? entry = _clips.GetSlot(slot);
        if (entry is not null)
        {
            _clips.PasteText(entry.Text);
        }
    }

    /// <summary>
    /// Live UI language switch (Preferences → Language): recomputes the texts of actions,
    /// menus, toolbars, panel titles and lists built once. Texts in XAML refresh
    /// themselves (<see cref="Localizer"/>); messages already shown stay in the previous language.
    /// </summary>
    public void OnLanguageChanged()
    {
        foreach (AppAction action in _actions.Actions)
        {
            action.RefreshLocalizedTexts();
        }

        RefreshClipActions();
        _menuBuilder.RefreshTexts();
        foreach (ToolbarViewModel toolbar in Toolbars)
        {
            toolbar.RefreshTexts();
        }

        _dockFactory.RefreshTitles();
        _findReplace.RefreshLocalizedTexts();
        _findUsages.OnLanguageChanged();
        BookBrowser.Refresh();
        RefreshTitle();
    }

    /// <summary>
    /// Refreshes the text/availability of the 60 Clip Bar actions (<c>MainWindow.Clip1</c>..<c>Clip60</c>)
    /// according to the current slot assignment (top-level clips, no groups). An unassigned slot
    /// is simply disabled and shows the placeholder label "Clip N" (<c>AppAction</c> has no visibility flag).
    /// </summary>
    private void RefreshClipActions()
    {
        for (int slot = 1; slot <= AppActionIds.ClipSlotCount; slot++)
        {
            AppAction? action = _actions.Get(AppActionIds.Clip(slot));
            if (action is null)
            {
                continue;
            }

            ClipEditorNode? entry = _clips.GetSlot(slot);

            // An empty slot goes back to the default name from the catalog ("Clip N") — a single source of that name.
            action.Text = entry is not null
                ? AppAction.ConvertMnemonics(entry.Name)
                : AppAction.ConvertMnemonics(action.DefaultText);
            action.IsEnabled = entry is not null;
        }
    }

    /// <summary>Language of the current book for the <c>AriaClips.TranslatePlaceholders</c> substitutions.</summary>
    private string GetActiveBookLanguage()
    {
        string? bookPath = ActiveCodeTab?.ResourceBookPath;
        if (bookPath is not null
            && _currentBook?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is HtmlResource html
            && html.GetLanguageAttribute() is { Length: > 0 } lang)
        {
            return lang;
        }

        return _currentBook?.GetOpf().GetPrimaryBookLanguage() ?? string.Empty;
    }

    private string GetActiveSelectedText()
    {
        CodeTabViewModel? tab = ActiveCodeTab;
        if (tab is null)
        {
            return string.Empty;
        }

        int start = Math.Min(tab.SelectionStart, tab.SelectionEnd);
        int end = Math.Max(tab.SelectionStart, tab.SelectionEnd);
        string text = tab.DocumentText;
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return text[start..end];
    }

    /// <summary>Candidates of the "Insert Aria Clip" list (code → "Name (code)" label + description translated into the book's language).</summary>
    public IReadOnlyDictionary<string, DescriptiveInfo> GetAriaClipOptions() =>
        AriaClipBuilder.ClipOptions(GetActiveBookLanguage());

    /// <summary>Whether the chosen ARIA clip code requires an additional role picker dialog (<c>section</c>/<c>aside</c>).</summary>
    public bool AriaClipRequiresRole(string code) => AriaClipBuilder.RequiresRoleSelection(code);

    /// <summary>Candidates of the "Select Role" list for an ARIA clip code that requires a role.</summary>
    public IReadOnlyDictionary<string, DescriptiveInfo> GetAriaRoleOptions(string code) =>
        AriaClipBuilder.RoleOptions(code);

    /// <summary>
    /// Builds and inserts the chosen ARIA clip (optionally with a role) into the active (X)HTML document.
    /// </summary>
    public void ApplyAriaClip(string code, string? roleCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        string clip = AriaClipBuilder.BuildClip(code, GetActiveSelectedText(), roleCode, GetActiveBookLanguage());
        _clips.PasteText(clip);
    }

    /// <summary>Library clips (leaves, no groups) as items of the "Insert Clip From Library" dialog.</summary>
    public IReadOnlyList<ClipPickerItem> GetLeafClipPickerItems() =>
        _clips.GetLeafClips().Select(c => new ClipPickerItem(c.FullName, c.Text)).ToList();

    /// <summary>Pastes the clip text chosen in the "Insert Clip From Library" dialog.</summary>
    public void ApplySelectedClip(string text) => _clips.PasteText(text);

    /// <summary>Recently inserted special characters (newest first) — for the "Insert Special Character" dialog.</summary>
    public IReadOnlyList<string> RecentSpecialCharacters => _settings.RecentSpecialCharacters;

    /// <summary>Font of the character column in the "Insert Special Character" window (Preferences).</summary>
    public SpecialCharacterAppearance SpecialCharacterAppearance =>
        _settings.SpecialCharacterAppearance;

    /// <summary>"Search all characters" in the "Insert Special Character" window (remembered).</summary>
    public bool SpecialCharacterSearchAll
    {
        get => _settings.SpecialCharacterSearchAll;
        set
        {
            _settings.SpecialCharacterSearchAll = value;
            _settings.Save();
        }
    }

    /// <summary>"Double-click inserts the cell content" in the "Insert Special Character" window (remembered).</summary>
    public bool SpecialCharacterDoubleClickInsertsCell
    {
        get => _settings.SpecialCharacterDoubleClickInsertsCell;
        set
        {
            _settings.SpecialCharacterDoubleClickInsertsCell = value;
            _settings.Save();
        }
    }

    /// <summary>
    /// Whether the document of the active code editor has an XHTML 1.x DOCTYPE, i.e. defines the named HTML
    /// entities (<see cref="XhtmlEntities.HasXhtml1Doctype"/>).
    /// </summary>
    public bool ActiveDocumentDefinesHtmlEntities =>
        ActiveCodeTab is { } tab && XhtmlEntities.HasXhtml1Doctype(tab.Document.Text);

    /// <summary>
    /// Inserts a special character (the character itself, its code or its entity — <paramref name="text"/>) in
    /// the active code editor and puts the character (<paramref name="character"/>, by default <paramref name="text"/>)
    /// at the top of the "recently used" list (limit 20).
    /// </summary>
    public void InsertSpecialCharacter(string text, string? character = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (ActiveCodeTab is not { SupportsFormatting: true } tab)
        {
            return;
        }

        tab.InsertRawText(text);
        string value = string.IsNullOrEmpty(character) ? text : character;

        List<string> recent = new() { value };
        recent.AddRange(_settings.RecentSpecialCharacters.Where(s => !string.Equals(s, value, StringComparison.Ordinal)));
        if (recent.Count > 20)
        {
            recent.RemoveRange(20, recent.Count - 20);
        }

        _settings.RecentSpecialCharacters = recent;
        _settings.Save();
    }

    /// <summary>Favorite special characters (in the order added) — for the "Insert Special Character" dialog.</summary>
    public IReadOnlyList<string> FavoriteSpecialCharacters => _settings.FavoriteSpecialCharacters;

    /// <summary>Adds a character to the favorites (no duplicates); a no-op when it is already a favorite.</summary>
    public void AddFavoriteSpecialCharacter(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (_settings.FavoriteSpecialCharacters.Contains(value, StringComparer.Ordinal))
        {
            return;
        }

        List<string> favorites = new(_settings.FavoriteSpecialCharacters) { value };
        _settings.FavoriteSpecialCharacters = favorites;
        _settings.Save();
    }

    /// <summary>Removes a character from the favorites; a no-op when it was not there.</summary>
    public void RemoveFavoriteSpecialCharacter(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        List<string> favorites = _settings.FavoriteSpecialCharacters
            .Where(s => !string.Equals(s, value, StringComparison.Ordinal))
            .ToList();
        _settings.FavoriteSpecialCharacters = favorites;
        _settings.Save();
    }

    /// <summary>Attaches the view's system clipboard, starting history tracking.</summary>
    public void AttachClipboard(Avalonia.Input.Platform.IClipboard clipboard) =>
        _clipboardHistory.AttachClipboard(clipboard);

    /// <summary>Clipboard history (newest entry first) — for the "Paste From Clipboard History" dialog.</summary>
    public IReadOnlyList<string> GetClipboardHistory() => _clipboardHistory.Items;

    /// <summary>Pastes the clipboard history entry chosen in the dialog into the active code editor.</summary>
    public void PasteClipboardHistoryEntry(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (ActiveCodeTab is { } tab)
        {
            tab.InsertRawText(tab.PrepareClipboardText(text));
        }
    }

    /// <summary>All media resources of the book that can be inserted (images/SVG/audio/video). Empty when there is no book.</summary>
    public IReadOnlyList<Resource> GetInsertableMediaResources() =>
        _currentBook?.GetMediaResources() ?? Array.Empty<Resource>();

    /// <summary>
    /// Inserts the given resources as <c>&lt;img&gt;</c>/<c>&lt;audio&gt;</c>/<c>&lt;video&gt;</c>/<c>&lt;a&gt;</c>
    /// in the active code editor (paths relative to the tab's file, URL-encoded).
    /// </summary>
    public void InsertMediaResources(IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0 || ActiveCodeTab is not { SupportsFormatting: true } tab)
        {
            return;
        }

        string fromBookPath = tab.ResourceBookPath;
        var parts = new List<string>(resources.Count);
        foreach (Resource resource in resources)
        {
            string relative = Utility.UrlEncodePath(BookPath.Relative(fromBookPath, resource.BookPath));
            string filename = resource.Filename;
            int dot = filename.LastIndexOf('.');
            string label = dot > 0 ? filename[..dot] : filename;
            parts.Add(CodeInsertOperations.BuildFileFragment(resource.Type, relative, label));
        }

        tab.InsertFileFragment(string.Concat(parts));
        _settings.LastInsertedFile = resources[^1].BookPath;
        _settings.Save();
    }

    /// <summary>
    /// Copies files from disk into the book and refreshes Book Browser (the "from disk" option of the shared
    /// "Select Files" dialog — Insert File / Add Cover). Inserts nothing in the editor.
    /// </summary>
    public IReadOnlyList<Resource> AddFilesFromDisk(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (filePaths.Count == 0 || _currentBook is null)
        {
            return Array.Empty<Resource>();
        }

        IReadOnlyList<Resource> added = _currentBook.AddExistingFiles(filePaths);
        BookBrowser.Refresh();
        return added;
    }

    /// <summary>Copies files from disk into the book and inserts them in the active editor (the "from disk" option of the "Insert File" dialog).</summary>
    public void InsertFilesFromDisk(IReadOnlyList<string> filePaths)
    {
        IReadOnlyList<Resource> added = AddFilesFromDisk(filePaths);
        if (added.Count > 0)
        {
            InsertMediaResources(added);
        }
    }

    /// <summary>Value of the <c>id</c> attribute under the caret (to fill in the "Insert ID" dialog).</summary>
    public string GetInsertIdInitialValue() => ActiveCodeTab?.CurrentIdValue ?? string.Empty;

    /// <summary>Identifiers already used in the active tab's file (for the list in the "Insert ID" dialog).</summary>
    public IReadOnlyList<string> GetIdsInActiveFile()
    {
        if (_currentBook is null || ActiveCodeTab?.Resource is not HtmlResource html)
        {
            return Array.Empty<string>();
        }

        return _currentBook.GetIdsInHtmlFile(html);
    }

    /// <summary>
    /// Inserts an identifier (validation: it must start with a letter, followed by
    /// letters/digits/<c>_ : - .</c>).
    /// </summary>
    public void ApplyInsertId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!CodeInsertOperations.IsValidId(id))
        {
            _statusBar.ShowMessage(
                Strings.Get("Status_InvalidId"),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return;
        }

        ActiveCodeTab?.InsertId(id);
    }

    /// <summary>Value of the <c>href</c> attribute under the caret (to fill in the "Insert Link" dialog).</summary>
    public string GetInsertHyperlinkInitialValue() => ActiveCodeTab?.CurrentHrefValue ?? string.Empty;

    /// <summary>
    /// List of link targets ((X)HTML files + their identifiers, media files) as <c>href</c>s relative
    /// to the active tab's file (flattened).
    /// </summary>
    public IReadOnlyList<HyperlinkTargetItem> GetHyperlinkTargets()
    {
        if (_currentBook is null || ActiveCodeTab is null)
        {
            return Array.Empty<HyperlinkTargetItem>();
        }

        string fromBookPath = ActiveCodeTab.ResourceBookPath;
        var items = new List<HyperlinkTargetItem>();

        foreach (HtmlResource html in _currentBook.GetHtmlResources())
        {
            string relative = Utility.UrlEncodePath(BookPath.Relative(fromBookPath, html.BookPath));
            bool isSelf = string.Equals(html.BookPath, fromBookPath, StringComparison.Ordinal);
            items.Add(new HyperlinkTargetItem(html.Filename, isSelf ? "#" : relative));
            foreach (string id in _currentBook.GetIdsInHtmlFile(html))
            {
                items.Add(new HyperlinkTargetItem($"{html.Filename} # {id}", (isSelf ? string.Empty : relative) + "#" + id));
            }
        }

        foreach (Resource media in _currentBook.GetMediaResources())
        {
            items.Add(new HyperlinkTargetItem(
                media.Filename, Utility.UrlEncodePath(BookPath.Relative(fromBookPath, media.BookPath))));
        }

        return items;
    }

    /// <summary>
    /// Inserts a link (rejects a target containing <c>&lt;</c> or <c>&gt;</c>).
    /// </summary>
    public void ApplyInsertHyperlink(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Contains('<', StringComparison.Ordinal) || target.Contains('>', StringComparison.Ordinal))
        {
            _statusBar.ShowMessage(
                Strings.Get("Status_InvalidLink"), TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return;
        }

        ActiveCodeTab?.InsertHyperlink(target);
    }

    private void OnStatusBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IStatusBarService.CurrentMessage))
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
        else if (e.PropertyName == nameof(IStatusBarService.CurrentLevel))
        {
            OnPropertyChanged(nameof(IsStatusMessageWarning));
        }
    }

    /// <summary>The set of services created for the designer preview (without DI).</summary>
    private sealed record DesignTimeBundle(
        AppActionRegistry Actions,
        KeyboardShortcutManager Shortcuts,
        ToolbarManager Toolbars,
        MainDockFactory DockFactory,
        IStatusBarService StatusBar,
        SettingsStore Settings,
        BookBrowserViewModel BookBrowser,
        TabManager Tabs,
        PreviewViewModel Preview,
        SpellChecker SpellChecker,
        ClipboardHistoryService ClipboardHistory,
        MissingDoctypeGuard DoctypeGuard,
        string Folder)
    {
        public static DesignTimeBundle Create()
        {
            // A folder of its own, so that nothing of the preview reaches the user's data folder.
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Signet.DesignTime", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            SettingsStore settings = new(System.IO.Path.Combine(folder, AppDirectories.SettingsFileName));
            KeyboardShortcutManager shortcuts = new(settings);
            ToolbarManager toolbars = new(settings);
            StatusBarService statusBar = new();
            AppActionRegistry actions = new(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance);
            MissingDoctypeGuard doctypeGuard = new(settings);
            BookBrowserViewModel bookBrowser = new(settings, statusBar, doctypeGuard);
            PreviewViewModel preview = new(statusBar, folder);
            MainDockFactory dockFactory = new(bookBrowser, preview);
            SpellChecker spellChecker = new(
                settings, System.IO.Path.Combine(folder, "hunspell_dictionaries"), System.IO.Path.Combine(folder, "user_dictionaries"));
            TabManager tabs = new(dockFactory, statusBar, settings, spellChecker);
            ClipboardHistoryService clipboardHistory = new(settings);
            return new DesignTimeBundle(
                actions, shortcuts, toolbars,
                dockFactory, statusBar, settings, bookBrowser, tabs, preview, spellChecker, clipboardHistory, doctypeGuard, folder);
        }
    }
}
