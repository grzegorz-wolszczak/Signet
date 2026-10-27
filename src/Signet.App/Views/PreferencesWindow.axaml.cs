using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Signet.App.CodeEditor;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// The modal "Preferences" window, modelled on Rider's Settings (see <see cref="PreferencesViewModel"/>):
/// <list type="bullet">
/// <item>the sections as a tree (top-level ones in bold) with a search box — the search keeps the sections whose name
/// or page text contains it and frames the matching controls of the shown page;</item>
/// <item>a section without settings of its own (a page without content in the XAML) lists links to its
/// subsections;</item>
/// <item>the path of the section above its page;</item>
/// <item>Save writes the changes and keeps the window open (enabled only when something changed); Close closes it
/// like the window's close button (asking about unsaved changes); Escape and the close button ask what to do with unsaved changes.</item>
/// </list>
/// The pages are the tabs of a TabControl whose tab strip is hidden; the tree selects them. A page names the page of its
/// enclosing section with <see cref="PreferencesPage.ParentProperty"/>. A page can have a tab bar of its own (its content
/// is a TabControl), e.g. the sections of Appearance: the look of the part and its Fonts.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the window closes, like in the other TreeDataGrid views.")]
public partial class PreferencesWindow : Window
{
    /// <summary>The height of a resizable multi-line field until the user drags its grip (about six lines).</summary>
    public const double DefaultFieldHeight = 124;

    private readonly PreferencesSectionsViewModel _sections;
    private readonly HierarchicalTreeDataGridSource<PreferencesSectionNode> _source;
    private readonly TreeSelectionSync<PreferencesSectionNode> _selection;
    private readonly List<Control> _framed = new();
    private readonly HashSet<int> _linkPages = new();
    private bool _closeConfirmed;
    private bool _askingAboutChanges;

    /// <summary>Initializes the window.</summary>
    public PreferencesWindow()
    {
        InitializeComponent();

        _sections = new PreferencesSectionsViewModel(BuildSections(), PageTexts);
        FillLinkPages();
        WirePageLinks();
        WireFontsPreviews();
        WireFootnoteStyleErrors();
        LocalizedColumns<PreferencesSectionNode> columns = new();
        VisibleItemsView<PreferencesSectionNode> roots = Visible(_sections.Roots);
        _source = new HierarchicalTreeDataGridSource<PreferencesSectionNode>(roots)
        {
            Columns =
            {
                columns.Expander(
                    columns.Template("PreferencesWindow_Title", "PreferencesSectionCellTemplate", new GridLength(1, GridUnitType.Star)),
                    n => Visible(n.Children),
                    n => n.HasChildren,
                    n => n.IsExpanded),
            },
        };
        _selection = new TreeSelectionSync<PreferencesSectionNode>(
            _source, roots, n => Visible(n.Children), () => _sections.Selected, n => _sections.Selected = n);
        SectionTree.Source = _source;

        _sections.PropertyChanged += OnSectionsPropertyChanged;
        Pages.SelectionChanged += (_, _) =>
        {
            if (_sections.FindByPage(Pages.SelectedIndex) is { } node)
            {
                _sections.Selected = node;
            }
        };
        SectionSearch.TextChanged += (_, _) =>
        {
            _sections.Query = SectionSearch.Text ?? string.Empty;
            QueueFrameRefresh();
        };
        Closed += (_, _) =>
        {
            _sections.PropertyChanged -= OnSectionsPropertyChanged;
            _source.Dispose();
            (DataContext as PreferencesViewModel)?.Dispose();
        };

        ShowSelectedSection();
    }

    /// <summary>The section tree and its search (for tests and the main window).</summary>
    public PreferencesSectionsViewModel Sections => _sections;

    /// <summary>The index of the selected page (the main window remembers it between openings).</summary>
    public int SelectedPageIndex
    {
        get => Pages.SelectedIndex;
        set => _sections.Selected = _sections.FindByPage(value >= 0 && value < Pages.ItemCount ? value : 0) ?? _sections.Selected;
    }

    /// <summary>
    /// The heights the user gave the resizable fields (field name -&gt; height); the main window remembers them between
    /// openings, like the window size.
    /// </summary>
    public IReadOnlyDictionary<string, int> FieldHeights
    {
        get => ResizeGrips()
            .Where(g => g.Target is { Name.Length: > 0 } target && !double.IsNaN(target.Height))
            .ToDictionary(g => g.Target!.Name!, g => (int)Math.Round(g.Target!.Height), StringComparer.Ordinal);

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (HeightResizeGrip grip in ResizeGrips())
            {
                if (grip.Target is { Name: { } name } target && value.TryGetValue(name, out int height) && height > 0)
                {
                    target.Height = Math.Max(grip.MinTargetHeight, height);
                }
            }
        }
    }

    /// <summary>
    /// The sizes the user gave the split panes (pane name -&gt; pixels): the width of the section tree
    /// (<c>Sections</c>), the width of the preview of each Fonts page that was resized (e.g. <c>CodeEditorFontsView.previewWidth</c>;
    /// the legacy <c>FontsPreview</c>, one width for all of them, is only read, for the pages without a width of their own), and the
    /// width of the tree and the height of the preview of each Appearance › Color Scheme page, e.g. <c>ThemeCssView.tree</c>; the main window remembers them between openings, like the field heights.
    /// </summary>
    public IReadOnlyDictionary<string, int> SplitterSizes
    {
        get
        {
            Dictionary<string, int> sizes = new(StringComparer.Ordinal)
            {
                [SectionsSizeKey] = (int)Math.Round(Body.ColumnDefinitions[0].Width.Value),
            };
            foreach (PageWidthHost host in WidthHosts().Where(h => !double.IsNaN(h.UserWidth)))
            {
                sizes[host.Name + PageWidthSuffix] = (int)Math.Round(host.UserWidth);
            }

            foreach (FontsPageView page in _resizedFontsPages)
            {
                sizes[page.Name + FontsPreviewWidthSuffix] = (int)Math.Round(page.PreviewWidth);
            }

            foreach (Grid host in PreviewHosts())
            {
                // The splitter leaves the size of a column or a row that is not a star as pixels, so it is the size in use.
                sizes[host.Name + PaneSuffix(host)] = (int)Math.Round(host.RowDefinitions.Count > 0
                    ? host.RowDefinitions[2].Height.Value
                    : host.ColumnDefinitions[2].Width.Value);
            }

            foreach (FontsPageView page in FontsPages())
            {
                foreach ((string key, int height) in page.PreviewHeights)
                {
                    sizes[page.Name + "." + key + PreviewHeightSuffix] = height;
                }
            }

            foreach ((_, ThemeKeysView view) in ThemeKeyViews())
            {
                sizes[view.Name + TreeSizeSuffix] = (int)Math.Round(view.TreeWidth);
                sizes[view.Name + PreviewSizeSuffix] = (int)Math.Round(view.PreviewHeight);
            }

            return sizes;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.TryGetValue(SectionsSizeKey, out int sections) && sections > 0)
            {
                ColumnDefinition column = Body.ColumnDefinitions[0];
                column.Width = new GridLength(Math.Max(column.MinWidth, sections));
            }

            foreach (PageWidthHost host in WidthHosts())
            {
                host.UserWidth = value.TryGetValue(host.Name + PageWidthSuffix, out int settingsWidth) && settingsWidth > 0
                    ? settingsWidth
                    : double.NaN;
            }

            foreach (Grid host in PreviewHosts())
            {
                if (value.TryGetValue(host.Name + PaneSuffix(host), out int paneSize) && paneSize > 0)
                {
                    if (host.RowDefinitions.Count > 0)
                    {
                        RowDefinition row = host.RowDefinitions[2];
                        row.Height = new GridLength(Math.Max(row.MinHeight, paneSize));
                    }
                    else
                    {
                        ColumnDefinition pane = host.ColumnDefinitions[2];
                        pane.Width = new GridLength(Math.Max(pane.MinWidth, paneSize));
                    }
                }
            }

            value.TryGetValue(FontsPreviewSizeKey, out int legacyPreview);
            foreach (FontsPageView page in FontsPages())
            {
                foreach ((string name, int height) in value)
                {
                    if (height > 0 && name.StartsWith(page.Name + ".", StringComparison.Ordinal) && name.EndsWith(PreviewHeightSuffix, StringComparison.Ordinal))
                    {
                        string key = name[(page.Name!.Length + 1)..^PreviewHeightSuffix.Length];
                        page.SetPreviewHeight(key, height);
                    }
                }

                if (value.TryGetValue(page.Name + FontsPreviewWidthSuffix, out int preview) && preview > 0)
                {
                    SetFontsPreviewWidth(page, preview);
                }
                else if (legacyPreview > 0 && !_resizedFontsPages.Contains(page))
                {
                    SetFontsPreviewWidth(page, legacyPreview);
                }
            }

            foreach ((_, ThemeKeysView view) in ThemeKeyViews())
            {
                if (value.TryGetValue(view.Name + TreeSizeSuffix, out int width) && width > 0)
                {
                    view.TreeWidth = width;
                }

                if (value.TryGetValue(view.Name + PreviewSizeSuffix, out int height) && height > 0)
                {
                    view.PreviewHeight = height;
                }
            }
        }
    }

    /// <summary>
    /// The sections collapsed in the tree (the names of their pages); the main window remembers them between openings.
    /// Setting collapses those sections, except the enclosing sections of the selected one (it stays visible).
    /// </summary>
    public IReadOnlyList<string> CollapsedSections
    {
        get => _sections.All.Where(n => n.HasChildren && !n.IsExpanded && n.Id.Length > 0).Select(n => n.Id).ToList();

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            HashSet<string> collapsed = new(value, StringComparer.Ordinal);
            HashSet<PreferencesSectionNode> keepOpen = new();
            for (PreferencesSectionNode? node = _sections.Selected?.Parent; node is not null; node = node.Parent)
            {
                keepOpen.Add(node);
            }

            // The deepest first: collapsing an enclosing section hides the rows of the ones inside it.
            foreach (PreferencesSectionNode node in _sections.All
                         .Where(n => n.HasChildren && n.Id.Length > 0 && collapsed.Contains(n.Id) && !keepOpen.Contains(n))
                         .OrderByDescending(Depth))
            {
                node.IsExpanded = false;
                if (_selection.FindPath(node) is { } path)
                {
                    _source.Collapse(new IndexPath(path));
                }
            }
        }
    }

    private static int Depth(PreferencesSectionNode node)
    {
        int depth = 0;
        for (PreferencesSectionNode? parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        return depth;
    }

    private const string SectionsSizeKey = "Sections";
    private const string FontsPreviewSizeKey = "FontsPreview";
    private const string FontsPreviewWidthSuffix = ".previewWidth";
    private const string PreviewHeightSuffix = ".previewHeight";
    private const string PreviewPaneWidthSuffix = ".paneWidth";
    private const string PreviewPaneHeightSuffix = ".paneHeight";

    // A preview panel is at the right of the settings (its width is remembered) or, in a wide window, under them (its height).
    private const string PageWidthSuffix = ".settingsWidth";

    // The settings of the pages that have a splitter after them (the width of the controls is the user's), whose width the
    // window remembers.
    private IEnumerable<PageWidthHost> WidthHosts()
    {
        yield return LanguageWidth;
        yield return AppearanceGeneralWidth;
        yield return CodeEditorWidth;
        yield return ColorSchemeWidth;
        yield return PreviewWidth;
        yield return FindUsagesGeneralWidth;
        yield return FootnotesGeneralWidth;
        yield return FootnotesCssWidth;
        yield return GeneralWidth;
        yield return EditionPageWidth;
        yield return MendPrettifyWidth;
        yield return SpellcheckWidth;
        yield return PreserveEntitiesWidth;
    }

    private static string PaneSuffix(Grid host) => host.RowDefinitions.Count > 0 ? PreviewPaneHeightSuffix : PreviewPaneWidthSuffix;
    private readonly List<FontsPageView> _resizedFontsPages = new();

    // The grids of the pages that have a preview panel common to their tabs: the settings, a splitter and the panel (column 2),
    // whose width the window remembers.
    private IEnumerable<Grid> PreviewHosts()
    {
        yield return BookBrowserPreviewHost;
        yield return StartScreenPreviewHost;
        yield return CodeEditorPreviewHost;
        yield return FindUsagesPreviewHost;
        yield return FindReplacePreviewHost;
        yield return SearchEditorPreviewHost;
        yield return SpecialCharactersPreviewHost;
        yield return CleanupPreviewHost;
        yield return FootnotesPreviewHost;
        yield return ValidationPreviewHost;
        yield return PreviewPreviewHost;
        yield return NotificationsPreviewHost;
    }

    // The Fonts pages (every Appearance section and the theme's own) each have a width of the preview of their own: the
    // splitter of one page does not move the others, and the window remembers the width of every page that was resized.
    private IEnumerable<FontsPageView> FontsPages() =>
        Pages.Items.OfType<TabItem>().Select(t => t.Content).OfType<ILogical>().SelectMany(SelfAndLogicalDescendants).OfType<FontsPageView>();

    // The footnote style fields underline what the validation complains about (the message is next to the field's name).
    private void WireFootnoteStyleErrors()
    {
        string[] names =
        {
            "FootnoteReferenceStyleBox", "FootnoteNoteStyleBox", "FootnoteParagraphStyleBox", "FootnoteLocalContainerStyleBox",
            "FootnoteGlobalContainerStyleBox", "FootnoteSeparatorStyleBox", "FootnoteGroupStyleBox", "FootnoteGroupTitleStyleBox",
        };
        foreach (string name in names)
        {
            this.FindControl<CssEditor>(name)!.ErrorLocator = text =>
                Signet.Core.Footnotes.FootnoteStyles.Validate(text) is { Length: > 0 } error ? (error.Start, error.Length) : null;
        }
    }

    private void WireFontsPreviews()
    {
        foreach (FontsPageView page in FontsPages())
        {
            page.PreviewWidthChanged += (sender, _) =>
            {
                if (sender is FontsPageView resized && !_resizedFontsPages.Contains(resized))
                {
                    _resizedFontsPages.Add(resized);
                }
            };
        }
    }

    private void SetFontsPreviewWidth(FontsPageView page, int width)
    {
        if (!_resizedFontsPages.Contains(page))
        {
            _resizedFontsPages.Add(page);
        }

        page.PreviewWidth = width;
    }

    private const string TreeSizeSuffix = ".tree";
    private const string PreviewSizeSuffix = ".preview";

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PreferencesViewModel vm)
        {
            // The "Choose…" buttons next to the fonts open a modal picker window over this window.
            vm.FontPicker = request => FontPickerWindow.AskAsync(this, request);
            vm.ColorPicker = (title, initial) => ColorPickerWindow.AskAsync(this, title, initial);
            vm.TextPrompt = (title, prompt, initial) => TextPromptWindow.AskAsync(this, title, prompt, initial);
            vm.ErrorMessage = (title, message) => MessageDialog.ShowAsync(this, title, message);

            // Set here, not bound in the XAML: a binding on the content of a page that is not shown does not resolve,
            // and the search must find the font settings of every page.
            AppearanceFontsView.DataContext = vm.AppearanceFonts;
            CodeEditorFontsView.DataContext = vm.CodeEditorFonts;
            PreviewFontsView.DataContext = vm.PreviewFonts;
            SpecialCharacterFontsView.DataContext = vm.SpecialCharacterFonts;
            StartScreenFontsView.DataContext = vm.StartScreenFonts;
            BookBrowserFontsView.DataContext = vm.BookBrowserFonts;
            PreviewSampleTip.DataContext = vm.PreviewFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.Preview);
            ValidationPreview.DataContext = vm.ValidationSample;
            ValidationSampleTip.DataContext = vm.ValidationFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.Validation);
            NotificationsPreview.DataContext = vm.NotificationsSample;
            FootnotesPreview.DataContext = vm.FootnoteSample;
            FootnotesSampleTip.DataContext = vm.FootnotesFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.Footnotes);
            CleanupPreview.DataContext = vm.CleanupSample;
            CleanupSampleTip.DataContext = vm.CleanupFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.Cleanup);
            SpecialCharactersPreview.DataContext = vm.SpecialCharacterFonts.Fonts.Single(f => f.Key == "SpecialCharacters");
            SpecialCharactersSampleTip.DataContext = vm.SpecialCharacterFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.SpecialCharacters);
            FindReplacePreview.DataContext = vm.FindReplaceSample;
            FindReplaceSampleTip.DataContext = vm.FindReplaceFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.FindReplace);
            vm.FindReplaceSample.ActionButtonDisplay = vm.FindActionButtonDisplay;
            FindUsagesPreview.DataContext = vm.FindUsagesSample;
            SearchEditorListPreview.DataContext = vm.SearchEditorListSample;
            SearchEditorEditPreview.DataContext = vm.SearchEditorEditSample;
            CodeEditorPreview.DataContext = vm.CodeEditorSample;
            DocumentTabsPreview.DataContext = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "DocumentTabs");
            CodeEditorSampleHint.DataContext = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "OpenTagHint");
            DocumentTabsPreview.IsMultiline = vm.MultilineTabs;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PreferencesViewModel.MultilineTabs))
                {
                    DocumentTabsPreview.IsMultiline = vm.MultilineTabs;
                }
                else if (e.PropertyName == nameof(PreferencesViewModel.FindActionButtonDisplay))
                {
                    vm.FindReplaceSample.ActionButtonDisplay = vm.FindActionButtonDisplay;
                }
            };
            StartScreenPreview.DataContext = vm.StartScreenSample;
            StartScreenSampleTip.DataContext = vm.StartScreenFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.StartScreen);
            BookBrowserPreview.DataContext = vm.BookBrowserSample;
            BookBrowserSampleTip.DataContext = vm.BookBrowserFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.BookBrowser);
            TableOfContentsFontsView.DataContext = vm.TableOfContentsFonts;
            FindReplaceFontsView.DataContext = vm.FindReplaceFonts;
            FindUsagesFontsView.DataContext = vm.FindUsagesFonts;
            SearchEditorFontsView.DataContext = vm.SearchEditorFonts;
            CheckpointsFontsView.DataContext = vm.CheckpointsFonts;
            ValidationFontsView.DataContext = vm.ValidationFonts;
            NotificationsFontsView.DataContext = vm.NotificationsFonts;
            MetadataEditorFontsView.DataContext = vm.MetadataEditorFonts;
            CleanupFontsView.DataContext = vm.CleanupFonts;
            FootnotesFontsView.DataContext = vm.FootnotesFonts;
            ThemePanel.DataContext = vm.Theme;
            SchemeSelector.DataContext = vm.ColorScheme;
            ColorSchemePanel.DataContext = vm.ColorScheme;
            ColorSchemePreviewFontView.DataContext = vm.ColorSchemePreviewFont;
            foreach (ThemeDocumentViewModel document in new[] { vm.Theme, vm.ColorScheme })
            {
                ConfigureDocument(document);
            }

            foreach ((ThemePage page, ThemeKeysView view) in ThemeKeyViews())
            {
                view.DataContext = DocumentOf(vm, page).PageFor(page);
            }
        }
    }

    // The dialogs of a list of themes or of color schemes, and what it asks of the window.
    private void ConfigureDocument(ThemeDocumentViewModel document)
    {
        document.TextPrompt = (title, prompt, initial) => TextPromptWindow.AskAsync(this, title, prompt, initial);
        document.Confirm = (title, message) =>
            ConfirmWindow.AskAsync(this, title, message, Strings.Get("ColorScheme_Delete"), destructive: true);
        document.Message = (title, message) => MessageDialog.ShowAsync(this, title, message);
        document.PickExportPath = name => PickThemeExportPathAsync(document, name);
        document.PickImportPath = () => PickThemeImportPathAsync(document);
        document.ColorPicker = (title, initial) => ColorPickerWindow.AskAsync(this, title, initial, alpha: true);
        document.KeyRequested -= OnThemeKeyRequested;
        document.KeyRequested += OnThemeKeyRequested;
    }

    // The theme edits the keys of the interface, the color scheme all the others.
    private static ThemeDocumentViewModel DocumentOf(PreferencesViewModel vm, ThemePage page) =>
        page == ThemePage.Interface ? vm.Theme : vm.ColorScheme;

    // The pages of the keys: the interface keys in a tab of Appearance, the others under Code Editor › Color Scheme.
    private (ThemePage Page, ThemeKeysView View)[] ThemeKeyViews() => new[]
    {
        (ThemePage.General, ThemeGeneralView),
        (ThemePage.Xhtml, ThemeXhtmlView),
        (ThemePage.Css, ThemeCssView),
        (ThemePage.JavaScriptJson, ThemeJavaScriptJsonView),
        (ThemePage.Diff, ThemeDiffView),
        (ThemePage.Preview, ThemePreviewView),
        (ThemePage.Interface, ThemeInterfaceView),
    };

    // A link to a key (e.g. the base of an inherited key, or from the look of a part of the application): its page is shown.
    private void OnThemeKeyRequested(object? sender, ThemeKey key)
    {
        if (key.Page == ThemePage.Interface)
        {
            if (_sections.All.FirstOrDefault(n => ReferenceEquals(Pages.Items[n.PageIndex], AppearanceGeneralPage)) is { } appearance)
            {
                RevealSection(appearance);
                AppearanceGeneralTabs.SelectedItem = InterfaceColorsTab;
            }

            return;
        }

        ThemeKeysView view = ThemeKeyViews().Single(v => v.Page == key.Page).View;
        if (view.Parent is TabItem page && _sections.FindByPage(Pages.Items.IndexOf(page)) is { } node)
        {
            RevealSection(node);
        }
    }

    private static FilePickerFileType ThemeFileType(ThemeDocumentViewModel document) =>
        new(document.FileTypeName) { Patterns = new[] { "*" + Signet.Core.Themes.ThemeXml.ExtensionOf(document.Kind) } };

    // Where to export a theme or a color scheme (the gear › Export…).
    private async Task<string?> PickThemeExportPathAsync(ThemeDocumentViewModel document, string suggestedName)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = document.ExportTitle,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { ThemeFileType(document) },
        });
        return file?.TryGetLocalPath();
    }

    // A theme or color scheme file to import (the gear › Import…).
    private async Task<string?> PickThemeImportPathAsync(ThemeDocumentViewModel document)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = document.ImportTitle,
            AllowMultiple = false,
            FileTypeFilter = new[] { ThemeFileType(document) },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Escape closes the window (asking about unsaved changes) unless a control used it.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// With unsaved changes, closing by the user (the close button, Escape) asks first: save and close, discard and
    /// close, or go back. Closing with the owner or the application does not ask.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed
            || DataContext is not PreferencesViewModel { HasChanges: true }
            || e.CloseReason is not (WindowCloseReason.WindowClosing or WindowCloseReason.Undefined))
        {
            return;
        }

        e.Cancel = true;
        if (!_askingAboutChanges)
        {
            _ = AskAboutUnsavedChangesAsync();
        }
    }

    private async Task AskAboutUnsavedChangesAsync()
    {
        _askingAboutChanges = true;
        try
        {
            int answer = await ChoiceWindow.AskAsync(
                this,
                Strings.Get("PreferencesWindow_UnsavedTitle"),
                Strings.Get("PreferencesWindow_UnsavedMessage"),
                new ChoiceAnswer(Strings.Get("PreferencesWindow_UnsavedSave"), ButtonClasses.Accent),
                new ChoiceAnswer(Strings.Get("PreferencesWindow_UnsavedDiscard")),
                new ChoiceAnswer(Strings.Get("PreferencesWindow_UnsavedBack")));
            if (answer is 0 or 1)
            {
                if (answer == 0 && DataContext is PreferencesViewModel vm && vm.ApplyCommand.CanExecute(null))
                {
                    vm.ApplyCommand.Execute(null);
                }

                _closeConfirmed = true;
                Close();
            }
        }
        finally
        {
            _askingAboutChanges = false;
        }
    }

    // Close is the window's close button: with unsaved changes it asks what to do with them (OnClosing).
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnClearSectionSearchClicked(object? sender, RoutedEventArgs e)
    {
        SectionSearch.Text = string.Empty;
        SectionSearch.Focus();
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreferencesSectionsViewModel.Selected))
        {
            ShowSelectedSection();
        }
    }

    // The selected section: its page, its path above the page, its row in the tree and the search frames on the page.
    private void ShowSelectedSection()
    {
        if (_sections.Selected is { } node && Pages.SelectedIndex != node.PageIndex)
        {
            Pages.SelectedIndex = node.PageIndex;
        }

        Breadcrumb.Text = _sections.Breadcrumb;
        _selection.SelectFromViewModel();
        QueueFrameRefresh();
    }

    // The section tree from the pages: a page names the page of its enclosing section (PreferencesPage.Parent), and a
    // subsection follows its parent in the XAML.
    private List<PreferencesSectionNode> BuildSections()
    {
        List<PreferencesSectionNode> roots = new();
        Dictionary<string, PreferencesSectionNode> byName = new(StringComparer.Ordinal);
        int index = 0;
        foreach (TabItem page in Pages.Items.OfType<TabItem>())
        {
            PreferencesSectionNode? parent = null;
            if (PreferencesPage.GetParent(page) is { } parentName && !byName.TryGetValue(parentName, out parent))
            {
                throw new InvalidOperationException($"The Preferences page '{page.Name}' names an unknown parent '{parentName}'.");
            }

            PreferencesSectionNode node = new(page.Header as string ?? string.Empty, index++, parent, page.Name ?? string.Empty);
            if (parent is null)
            {
                roots.Add(node);
            }

            if (page.Name is { Length: > 0 } name)
            {
                byName[name] = node;
            }
        }

        return roots;
    }

    // A page without content is a section without settings of its own: it lists links to its subsections, as in Rider.
    private void FillLinkPages()
    {
        foreach (PreferencesSectionNode node in _sections.All)
        {
            if (Pages.Items[node.PageIndex] is TabItem { Content: null } page)
            {
                StackPanel links = new() { Spacing = 4, Margin = new Thickness(8) };
                foreach (PreferencesSectionNode child in node.Children)
                {
                    HyperlinkButton link = new() { Content = child.Title };
                    link.Click += (_, _) => RevealSection(child);
                    links.Children.Add(link);
                }

                page.Content = new ScrollViewer { Content = links };
                _linkPages.Add(node.PageIndex);
            }
        }
    }

    // Opens a section as a click in the tree would: its enclosing sections are expanded (also when the user collapsed
    // them), and the section is selected and scrolled into view.
    private void RevealSection(PreferencesSectionNode node)
    {
        List<PreferencesSectionNode> ancestors = new();
        for (PreferencesSectionNode? parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            ancestors.Insert(0, parent);
        }

        foreach (PreferencesSectionNode ancestor in ancestors)
        {
            ancestor.IsExpanded = true;
            if (_selection.FindPath(ancestor) is { } path)
            {
                _source.Expand(new IndexPath(path));
            }
        }

        _sections.Selected = node;
        if (_selection.FindPath(node) is { } nodePath)
        {
            Dispatcher.UIThread.Post(
                () => SectionTree.RowsPresenter?.BringIntoView(_source.Rows.ModelIndexToRowIndex(new IndexPath(nodePath))),
                DispatcherPriority.Loaded);
        }
    }

    // The links of the pages to other pages (PreferencesPage.Target): a click selects the target in the tree.
    private void WirePageLinks()
    {
        Dictionary<string, PreferencesSectionNode> byName = _sections.All
            .Where(n => Pages.Items[n.PageIndex] is TabItem { Name.Length: > 0 })
            .ToDictionary(n => ((TabItem)Pages.Items[n.PageIndex]!).Name!, StringComparer.Ordinal);
        foreach (TabItem page in Pages.Items.OfType<TabItem>())
        {
            foreach (HyperlinkButton link in page.GetLogicalDescendants().OfType<HyperlinkButton>())
            {
                if (PreferencesPage.GetTarget(link) is not { } target)
                {
                    continue;
                }

                PreferencesSectionNode node = byName.TryGetValue(target, out PreferencesSectionNode? found)
                    ? found
                    : throw new InvalidOperationException($"A link of the Preferences page '{page.Name}' names an unknown page '{target}'.");
                ThemeKey? key = null;
                if (PreferencesPage.GetTargetKey(link) is { } keyId
                    && !ThemeKeys.TryGet(keyId, out key))
                {
                    throw new InvalidOperationException($"A link of the Preferences page '{page.Name}' names an unknown theme key '{keyId}'.");
                }

                link.Click += (_, _) =>
                {
                    RevealSection(node);

                    // The key the link is about: shown in its page, with its group expanded.
                    if (key is not null && DataContext is PreferencesViewModel vm)
                    {
                        DocumentOf(vm, key.Page).RequestKey(key);
                    }
                };
            }
        }
    }

    /// <summary>
    /// Goes to a font setting of Appearance › General › Fonts (the link of a setting that inherits from it): opens the
    /// section and its Fonts tab, shows the setting in the preview and scrolls it into view.
    /// </summary>
    internal void RevealFont(FontSettingViewModel font)
    {
        if (AppearanceFontsView.DataContext is not FontPageViewModel page
            || _sections.All.FirstOrDefault(n => ReferenceEquals(Pages.Items[n.PageIndex], AppearanceGeneralPage)) is not { } node)
        {
            return;
        }

        RevealSection(node);
        AppearanceGeneralTabs.SelectedIndex = 1;
        Dispatcher.UIThread.Post(() => AppearanceFontsView.RowOf(font)?.BringIntoView(), DispatcherPriority.Loaded);
    }

    // After the layout pass: the page's controls exist and can be framed.
    private void QueueFrameRefresh() => Dispatcher.UIThread.Post(RefreshFrames, DispatcherPriority.Loaded);

    // Frames the controls of the shown page whose own text contains the search text (Rider's search highlight), and
    // brings the first one into view. A control inside a framed one is not framed again.
    private void RefreshFrames()
    {
        foreach (Control control in _framed)
        {
            AdornerLayer.SetAdorner(control, null);
        }

        _framed.Clear();
        if (!_sections.IsSearching || _linkPages.Contains(Pages.SelectedIndex) || Pages.SelectedItem is not TabItem { Content: ILogical page })
        {
            return;
        }

        // The hit is on another tab of the page's tab bar (e.g. Fonts): show that tab first, then frame again.
        if (ShowTabWithMatch(page))
        {
            QueueFrameRefresh();
            return;
        }

        foreach (ILogical element in SelfAndLogicalDescendants(page))
        {
            if (element is Control { IsEffectivelyVisible: true } control
                && _sections.Matches(OwnText(element))
                && !_framed.Any(framed => control.GetLogicalAncestors().Contains(framed)))
            {
                AdornerLayer.SetAdorner(control, SearchHitFrame());
                _framed.Add(control);
            }
        }

        _framed.FirstOrDefault()?.BringIntoView();
    }

    // When the page has a tab bar and the selected tab has no match for the search, selects the first tab that has one.
    private bool ShowTabWithMatch(ILogical page)
    {
        // The tab bar of a page is the page itself, or (a page with a preview panel next to its tabs) the first one in it.
        if ((page as TabControl ?? SelfAndLogicalDescendants(page).OfType<TabControl>().FirstOrDefault()) is not { } tabs
            || tabs.SelectedItem is not TabItem selected || TabHasMatch(selected))
        {
            return false;
        }

        if (tabs.Items.OfType<TabItem>().FirstOrDefault(TabHasMatch) is not { } other)
        {
            return false;
        }

        tabs.SelectedItem = other;
        return true;
    }

    private bool TabHasMatch(TabItem tab) =>
        _sections.Matches(tab.Header as string)
        || (tab.Content is ILogical content && SelfAndLogicalDescendants(content).Select(OwnText).OfType<string>().Any(_sections.Matches));

    // The frame around a control matching the search: in the accent color, a little outside the control. The adorner
    // layer clips an adorner to the control's bounds by default, which cut the whole frame away: clipping is off.
    private static Border SearchHitFrame()
    {
        Border frame = new()
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(-4, -2),
            IsHitTestVisible = false,
        };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("SystemControlHighlightAccentBrush"));
        AdornerLayer.SetIsClipEnabled(frame, false);
        return frame;
    }

    // The texts of a page the search looks in: labels, check boxes, radio and other buttons, headers of inner tabs. The
    // links of a section without settings are only the names of its subsections, which the search sees anyway.
    private IEnumerable<string> PageTexts(int index) =>
        index >= 0 && index < Pages.ItemCount && !_linkPages.Contains(index) && Pages.Items[index] is TabItem { Content: ILogical page }
            ? SelfAndLogicalDescendants(page).Select(OwnText).OfType<string>()
            : Enumerable.Empty<string>();

    // The element and everything below it, in document order. The content of a tab that is not selected is not in the
    // logical tree (only the selected one is presented), so a tab's Content is followed explicitly: the search must see
    // the settings of every tab, and the resize grips of every page must be found. The content of the selected tab of a
    // nested tab bar is reached twice (through its TabControl and its TabItem): each element is given once.
    private static IEnumerable<ILogical> SelfAndLogicalDescendants(ILogical root) =>
        SelfAndLogicalDescendants(root, new HashSet<ILogical>(ReferenceEqualityComparer.Instance));

    private static IEnumerable<ILogical> SelfAndLogicalDescendants(ILogical root, HashSet<ILogical> visited)
    {
        if (!visited.Add(root))
        {
            yield break;
        }

        yield return root;
        foreach (ILogical child in LogicalChildrenAndTabContent(root))
        {
            foreach (ILogical descendant in SelfAndLogicalDescendants(child, visited))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<ILogical> LogicalChildrenAndTabContent(ILogical element)
    {
        // A preview panel holds no settings (only samples): nothing below it is searched.
        if (element is StyledElement { Classes: var classes } && classes.Contains("noSearchTree"))
        {
            yield break;
        }

        foreach (ILogical child in element.LogicalChildren)
        {
            yield return child;
        }

        if (element is TabItem { Content: ILogical content } && !element.LogicalChildren.Contains(content))
        {
            yield return content;
        }
    }

    // The grips of the resizable fields of every page (the pages not shown are in the logical tree too).
    // (Not the grips under the font previews: the Fonts pages remember those with the sizes of the panes.)
    private IEnumerable<HeightResizeGrip> ResizeGrips() =>
        Pages.Items.OfType<TabItem>()
            .Select(t => t.Content)
            .OfType<ILogical>()
            .SelectMany(SelfAndLogicalDescendants)
            .OfType<HeightResizeGrip>()
            .Where(g => !g.GetLogicalAncestors().OfType<FontSettingView>().Any());

    // A text with the class "noSearch" (e.g. the sample of a font preview, or the link to a parent setting) is not a
    // setting: the search skips it.
    private static string? OwnText(ILogical element) => element switch
    {
        TextBlock text when text.Classes.Contains("noSearch") => null,
        ContentControl content when content.Classes.Contains("noSearch") => null,
        TextBlock text => text.Text,
        HeaderedContentControl { Header: string header } => header,
        ContentControl { Content: string content } => content,
        _ => null,
    };

    private static VisibleItemsView<PreferencesSectionNode> Visible(System.Collections.ObjectModel.ObservableCollection<PreferencesSectionNode> nodes) =>
        VisibleItems.For(nodes, n => n.IsVisible, nameof(PreferencesSectionNode.IsVisible));

    /// <summary>"Open settings folder".</summary>
    private async void OnOpenPreferencesLocationClicked(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppDirectories.EnsurePrefsDirectory()));
    }

    // "Browse…" next to the temp folder: a folder dialog, starting at the folder in use.
    private async void OnPickTempFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesViewModel vm)
        {
            return;
        }

        IStorageFolder? start = vm.TempFolderHome.Length > 0
            ? await StorageProvider.TryGetFolderFromPathAsync(vm.TempFolderHome)
            : null;
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Get("PreferencesWindow_TempFolderBrowseTitle"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            vm.TempFolderHome = path;
        }
    }

    // "Browse…" next to the custom icon folder: a folder dialog, starting at the folder in use.
    private async void OnPickCustomIconFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesViewModel vm)
        {
            return;
        }

        IStorageFolder? start = vm.CustomIconFolder.Length > 0
            ? await StorageProvider.TryGetFolderFromPathAsync(vm.CustomIconFolder)
            : null;
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Get("PreferencesWindow_CustomIconFolderBrowseTitle"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            vm.CustomIconFolder = path;
        }
    }

    private void OnUserWordSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm && sender is ListBox list)
        {
            vm.SetSelectedUserWords(list.SelectedItems?.OfType<string>() ?? Enumerable.Empty<string>());
        }
    }

    private void OnUserWordDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm && vm.EditUserWordCommand.CanExecute(null))
        {
            vm.EditUserWordCommand.Execute(null);
        }
    }
}
