using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Actions;
using Signet.App.CodeEditor;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Footnotes;
using Signet.Core.Misc;
using Signet.Core.Themes;
using Signet.Core.Spellcheck;

namespace Signet.App.UiTests;

/// <summary>
/// Headless tests of the "Preferences" window — in particular the visual separation of the tab list
/// from the content area (in the light theme both parts were white and blended into one surface).
/// </summary>
public sealed class PreferencesWindowRenderTests
{
    // Selects a top-level page by its header (resource key), so the tests do not depend on the order of the pages.
    private static void SelectTab(TabControl tabs, string headerKey) =>
        tabs.SelectedItem = tabs.Items.OfType<TabItem>()
            .Single(t => Equals(t.Header, Strings.Get(headerKey)) && PreferencesPage.GetParent(t) is null);

    // Selects a page by its x:Name (the subsections share headers such as "Appearance" and "Fonts") and lays it out.
    private static TabControl ShowPage(PreferencesWindow window, string pageName)
    {
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => t.Name == pageName);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return tabs;
    }

    // Shows the Fonts tab of an Appearance section ("CodeEditor" = Appearance › Code Editor › Fonts) and lays it out.
    private static TabControl ShowFontsTab(PreferencesWindow window, string part)
    {
        ShowPage(window, part + "AppearancePage");
        TabControl tabs = window.FindControl<TabControl>(part + "AppearanceTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return tabs;
    }

    // Shows the Fonts tab of Appearance › General (the interface font, the warnings, the tooltips) and lays it out.
    private static void ShowGeneralFontsTab(PreferencesWindow window)
    {
        ShowPage(window, "AppearanceGeneralPage");
        TabControl tabs = window.FindControl<TabControl>("AppearanceGeneralTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static PreferencesWindow BuildWindow() => BuildWindow(out _);

    private static PreferencesWindow BuildWindow(out SettingsStore settings)
    {
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-ui-" + Guid.NewGuid().ToString("N"));
        settings = new(Path.Combine(root, "settings.xml"));
        SpellChecker spellChecker = new(settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        LocalizationManager localization = new(settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(settings, NullLogger<IconThemeManager>.Instance);
        KeyboardShortcutManager shortcuts = new(settings);
        using StatusBarService statusBar = new();
        AppActionRegistry registry = new(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance);

        UiDensityManager uiDensity = new(settings, iconTheme, NullLogger<UiDensityManager>.Instance);
        ThemeManager themes = new(settings, ThemeStores.In(root), NullLogger<ThemeManager>.Instance);
        PreferencesViewModel vm = new(settings, spellChecker, localization, iconTheme, shortcuts, registry, uiDensity, themes);
        return new PreferencesWindow { DataContext = vm };
    }

    /// <summary>The selected page can be set from outside (the main window restores the last one); a page that no longer exists falls back to the first.</summary>
    [AvaloniaFact]
    public void Selected_page_index_selects_the_page_and_ignores_a_page_that_does_not_exist()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.SelectedPageIndex = 2;
        window.SelectedPageIndex.Should().Be(2);

        window.SelectedPageIndex = 999;
        window.SelectedPageIndex.Should().Be(0);
        window.Close();
    }

    /// <summary>
    /// The "Edition page" tab: the remove button is disabled without a book, the options follow the main check box and
    /// "Add" puts a new editable row into the field list.
    /// </summary>
    [AvaloniaFact]
    public void Edition_page_tab_enables_its_options_with_the_check_box()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_EditionPage");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        Control Find(string name) => window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);
        Find("RemoveEditionPageButton").IsEffectivelyEnabled.Should().BeFalse("no book is open");
        Find("EditionPageOptions").IsEffectivelyEnabled.Should().BeFalse("the edition page is off by default");

        ((CheckBox)Find("EditionPageEnabledBox")).IsChecked = true;
        vm.AddEditionPageFieldCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        vm.EditionPageEnabled.Should().BeTrue();
        Find("EditionPageOptions").IsEffectivelyEnabled.Should().BeTrue();
        Find("EditionPageFieldList").GetVisualDescendants().OfType<TextBox>().Should().HaveCount(2, "a new row has a key and a value box");
        window.Close();
    }

    // Clicks the answer of the "unsaved changes" question the window shows over itself.
    private static void Answer(PreferencesWindow window, string answerKey)
    {
        ChoiceWindow prompt = window.OwnedWindows.OfType<ChoiceWindow>().Single();
        prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Strings.Get(answerKey)))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Save (filled) is enabled only with changes, writes them and keeps the window open; Close (a frame) closes the
    /// window right away when nothing is unsaved. There is no Apply or Cancel button any more.
    /// </summary>
    [AvaloniaFact]
    public void Save_writes_the_changes_and_keeps_the_window_open_and_Close_closes_it()
    {
        PreferencesWindow window = BuildWindow(out SettingsStore settings);
        var vm = (PreferencesViewModel)window.DataContext!;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool before = settings.SpellCheck;
        Button save = window.FindControl<Button>("SaveButton")!;
        Button close = window.FindControl<Button>("CloseButton")!;
        window.FindControl<Button>("ApplyButton").Should().BeNull();
        window.FindControl<Button>("CancelButton").Should().BeNull("the button is Close now");
        close.Content.Should().Be(Strings.Get("Common_Close"));
        save.Classes.Should().Contain("accent");
        close.Classes.Should().Contain("outline");
        save.IsEffectivelyEnabled.Should().BeFalse("nothing has changed yet");

        vm.SpellCheckEnabled = !before;
        Dispatcher.UIThread.RunJobs();
        save.IsEffectivelyEnabled.Should().BeTrue();
        save.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        settings.SpellCheck.Should().Be(!before);
        closed.Should().BeFalse("Save keeps the window open");
        save.IsEffectivelyEnabled.Should().BeFalse("everything is saved");

        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        closed.Should().BeTrue();
        window.OwnedWindows.Should().BeEmpty("nothing is unsaved: Close does not ask");
    }

    /// <summary>Close with unsaved changes asks what to do with them, like the window's close button and Escape.</summary>
    [AvaloniaFact]
    public void Close_with_unsaved_changes_asks_first()
    {
        PreferencesWindow window = BuildWindow(out SettingsStore settings);
        var vm = (PreferencesViewModel)window.DataContext!;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool before = settings.SpellCheck;
        vm.SpellCheckEnabled = !before;
        Dispatcher.UIThread.RunJobs();

        window.FindControl<Button>("CloseButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        closed.Should().BeFalse("the window waits for the answer");
        window.OwnedWindows.Should().ContainSingle("the question about the unsaved changes");
        settings.SpellCheck.Should().Be(before, "nothing was saved");
        window.OwnedWindows[0].Close();
        window.Close();
    }

    /// <summary>
    /// A Fonts page whose settings are switched off can still be scrolled: only the settings are disabled, not the scroll
    /// viewer with its scroll bar.
    /// </summary>
    [AvaloniaFact]
    public void A_disabled_fonts_page_can_still_be_scrolled()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "AppearanceGeneralPage");
        TabControl tabs = window.FindControl<TabControl>("AppearanceGeneralTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        FontsPageView page = window.FindControl<FontsPageView>("AppearanceFontsView")!;
        page.IsContentEnabled = false;
        Dispatcher.UIThread.RunJobs();

        page.FindControl<StackPanel>("Settings")!.IsEffectivelyEnabled.Should().BeFalse("the settings are switched off");
        ScrollViewer scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroll.IsEffectivelyEnabled.Should().BeTrue("the scroll viewer keeps working");

        page.IsContentEnabled = true;
        Dispatcher.UIThread.RunJobs();
        page.FindControl<StackPanel>("Settings")!.IsEffectivelyEnabled.Should().BeTrue();
        window.Close();
    }

    /// <summary>The preview of a Fonts page can be made wider or narrower with the splitter between it and the settings.</summary>
    [AvaloniaFact]
    public void The_preview_of_a_fonts_page_is_resized_with_the_splitter()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1500; // room for both columns to grow
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowFontsTab(window, "TableOfContents");
        FontsPageView page = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible);
        GridSplitter splitter = page.FindControl<GridSplitter>("PreviewSplitter")!;
        Grid grid = (Grid)splitter.Parent!;
        double before = grid.ColumnDefinitions[2].ActualWidth;
        Point center = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;

        window.MouseDown(center, MouseButton.Left);
        window.MouseMove(center - new Vector(120, 0));
        window.MouseUp(center - new Vector(120, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        grid.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThan(before + 60, "dragging the splitter to the left widens the preview");

        Point moved = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(moved, MouseButton.Left);
        window.MouseMove(moved + new Vector(400, 0));
        window.MouseUp(moved + new Vector(400, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        grid.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThanOrEqualTo(160, "the preview keeps a minimum width");
        grid.ColumnDefinitions[2].ActualWidth.Should().BeLessThan(before + 60, "dragging to the right narrows it again");
        window.Close();
    }

    private static PageWidthHost ShowLanguagePage(PreferencesWindow window)
    {
        window.Width = 1900;
        window.Height = 800;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        SelectTab(window.GetVisualDescendants().OfType<TabControl>().First(), "PreferencesWindow_Language");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<PageWidthHost>().Single(h => h.IsEffectivelyVisible);
    }

    /// <summary>The settings of a page do not stretch to the right edge: they take what their content needs and a visible splitter follows.</summary>
    [AvaloniaFact]
    public void The_settings_of_a_page_take_the_width_of_their_content_and_a_splitter_follows()
    {
        PreferencesWindow window = BuildWindow();
        PageWidthHost host = ShowLanguagePage(window);

        host.SettingsWidth.Should().BeInRange(PageWidthHost.MinSettingsWidth, PageWidthHost.DefaultMaxWidth);
        host.SettingsWidth.Should().BeLessThan(host.Bounds.Width / 2, "the page is wide and the content is not");
        host.GetVisualDescendants().OfType<GridSplitter>().Should().ContainSingle(s => s.IsEffectivelyVisible && s.Bounds.Width > 4);
        double.IsNaN(host.UserWidth).Should().BeTrue("the user has not moved it");
        window.Close();
    }

    /// <summary>The splitter moves the width of the settings, to the right edge of the page and back; the window remembers it per page.</summary>
    [AvaloniaFact]
    public void Dragging_the_splitter_sets_the_width_and_the_window_remembers_it()
    {
        PreferencesWindow window = BuildWindow();
        PageWidthHost host = ShowLanguagePage(window);
        GridSplitter splitter = host.GetVisualDescendants().OfType<GridSplitter>().Single();
        double before = host.SettingsWidth;
        Point center = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;

        window.MouseDown(center, MouseButton.Left);
        window.MouseMove(center + new Vector(300, 0));
        window.MouseUp(center + new Vector(300, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        host.SettingsWidth.Should().BeApproximately(before + 300, 2);
        host.UserWidth.Should().BeApproximately(before + 300, 2);
        window.SplitterSizes[host.Name + ".settingsWidth"].Should().BeCloseTo((int)(before + 300), 2);

        PreferencesWindow reopened = BuildWindow();
        reopened.SplitterSizes = window.SplitterSizes;
        PageWidthHost again = ShowLanguagePage(reopened);
        again.SettingsWidth.Should().BeApproximately(before + 300, 2, "the width is remembered between openings");

        again.UserWidth = 5000;
        Dispatcher.UIThread.RunJobs();
        again.SettingsWidth.Should().BeLessThanOrEqualTo(again.Bounds.Width, "the splitter cannot go past the right edge of the page");
        again.UserWidth = double.NaN;
        Dispatcher.UIThread.RunJobs();
        again.SettingsWidth.Should().BeApproximately(before, 2, "back to the default width");
        window.Close();
        reopened.Close();
    }

    /// <summary>Escape without changes closes the window right away.</summary>
    [AvaloniaFact]
    public void Escape_without_changes_closes_the_window()
    {
        PreferencesWindow window = BuildWindow();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        closed.Should().BeTrue();
    }

    /// <summary>
    /// Escape (or the close button) with unsaved changes asks: "Back to Editing" keeps the window, "Discard" closes it
    /// without saving, "Save and Close" saves and closes.
    /// </summary>
    [AvaloniaFact]
    public void Closing_with_unsaved_changes_asks_what_to_do_with_them()
    {
        PreferencesWindow window = BuildWindow(out SettingsStore settings);
        var vm = (PreferencesViewModel)window.DataContext!;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool before = settings.SpellCheck;
        vm.SpellCheckEnabled = !before;

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Answer(window, "PreferencesWindow_UnsavedBack");
        closed.Should().BeFalse();
        vm.HasChanges.Should().BeTrue();

        window.Close();
        Dispatcher.UIThread.RunJobs();
        Answer(window, "PreferencesWindow_UnsavedSave");

        closed.Should().BeTrue();
        settings.SpellCheck.Should().Be(!before, "Save and Close saved the change");
    }

    /// <summary>"Discard Changes" closes the window without saving.</summary>
    [AvaloniaFact]
    public void Discarding_unsaved_changes_closes_the_window_without_saving()
    {
        PreferencesWindow window = BuildWindow(out SettingsStore settings);
        var vm = (PreferencesViewModel)window.DataContext!;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool before = settings.SpellCheck;
        vm.SpellCheckEnabled = !before;

        window.Close();
        Dispatcher.UIThread.RunJobs();
        Answer(window, "PreferencesWindow_UnsavedDiscard");

        closed.Should().BeTrue();
        settings.SpellCheck.Should().Be(before);
    }

    /// <summary>
    /// The sections are a tree (every page is a top-level section, in bold) and selecting one shows its page and its
    /// path above it; the TabControl's own tab strip is hidden.
    /// </summary>
    [AvaloniaFact]
    public void The_sections_are_a_tree_that_selects_the_page_and_shows_its_path()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();

        window.Sections.Roots.Select(n => n.Title).Should().Equal(
            tabs.Items.OfType<TabItem>().Where(t => PreferencesPage.GetParent(t) is null).Select(t => (string)t.Header!));
        window.Sections.Roots.Should().OnlyContain(n => n.IsTopLevel);
        window.Sections.All.Should().HaveCount(tabs.ItemCount, "every page is a section");
        tabs.GetVisualDescendants().OfType<TabItem>().Should().NotContain(t => t.IsEffectivelyVisible, "the tree replaces the tab strip");
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("topSection"))
            .Should().NotBeEmpty().And.OnlyContain(t => t.FontWeight == FontWeight.Bold);

        PreferencesSectionNode spelling = window.Sections.Roots.Single(n => n.Title == Strings.Get("PreferencesWindow_Spellcheck"));
        window.Sections.Selected = spelling;
        Dispatcher.UIThread.RunJobs();

        tabs.SelectedIndex.Should().Be(spelling.PageIndex);
        window.FindControl<TextBlock>("Breadcrumb")!.Text.Should().Be(spelling.Title);
        window.Close();
    }

    // The parts with a page in Appearance, in its order: they follow the work on a book, from the start screen,
    // through editing, to cleaning up.
    private static readonly string[] PartsWithFonts =
    {
        "StartScreen", "BookBrowser", "TableOfContents", "CodeEditor", "Preview", "FindReplace", "SpecialCharacters",
        "Checkpoints", "Validation", "MetadataEditor", "Cleanup", "Footnotes",
    };

    /// <summary>
    /// Appearance and Behavior holds Appearance (the themes, one section per part of the application below it, each page
    /// with a tab bar: the look of the part where it has more than fonts, and Fonts), then the sections of the behaviour
    /// of Code Editor (with its Color Scheme), Preview and Footnotes. The path of a section is shown above its page.
    /// </summary>
    [AvaloniaFact]
    public void The_parts_are_sections_of_appearance_with_a_tab_bar()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        string appearanceAndBehavior = Strings.Get("PreferencesWindow_AppearanceAndBehavior");
        string appearance = Strings.Get("PreferencesWindow_Appearance");
        string codeEditor = Strings.Get("PreferencesWindow_CodeEditor");
        string fonts = Strings.Get("PreferencesWindow_Fonts");
        string general = Strings.Get("PreferencesWindow_General");
        PreferencesSectionNode root = window.Sections.Roots.Single(n => n.Title == appearanceAndBehavior);

        root.Children.Select(n => n.Title).Should().Equal(
            appearance, codeEditor, Strings.Get("PreferencesWindow_Preview"), Strings.Get("PreferencesWindow_FindUsagesWindow"),
            Strings.Get("PreferencesWindow_Footnotes"));
        PreferencesSectionNode look = root.Children.Single(n => n.Title == appearance);
        look.Children.Select(n => n.Title).Should().Equal(PartsWithFonts.Select(s => Strings.Get("PreferencesWindow_" + s)));
        look.Children.Should().OnlyContain(n => n.Children.Count == 0,
            "the parts are no sections with subsections: their tabs are inside the page");
        root.Children.Single(n => n.Title == codeEditor).Children.Select(n => n.Title)
            .Should().Equal(Strings.Get("PreferencesWindow_ColorScheme")); // the color scheme is managed under Code Editor
        window.Sections.Roots.Should().NotContain(n => n.Title == appearance, "Appearance is a subsection now");

        string[] bothTabs = { "Preview" };
        foreach (string part in PartsWithFonts.Prepend("General"))
        {
            string pageName = (part == "General" ? "AppearanceGeneral" : part + "Appearance") + "Page";
            string tabsName = (part == "General" ? "AppearanceGeneral" : part + "Appearance") + "Tabs";
            ShowPage(window, pageName);
            string[] headers = window.FindControl<TabControl>(tabsName)!.Items.OfType<TabItem>().Select(t => (string)t.Header!).ToArray();
            string[] expected = part == "General"
                ? new[] { general, fonts, Strings.Get("PreferencesWindow_InterfaceColors") } // the themes, the fonts, the colors of the theme
                : bothTabs.Contains(part)
                    ? new[] { Strings.Get("PreferencesWindow_" + part), fonts }
                    : part is "BookBrowser" or "StartScreen" or "CodeEditor"
                        ? new[] { general, fonts } // the general settings, then the fonts
                        : new[] { fonts };
            headers.Should().Equal(expected, "the tab bar of {0}", part);
        }

        PreferencesSectionNode codeEditorLook = look.Children.Single(n => n.Title == codeEditor);
        window.Sections.Selected = codeEditorLook;
        Dispatcher.UIThread.RunJobs();
        window.FindControl<TextBlock>("Breadcrumb")!.Text.Should().Be(string.Join(" › ", appearanceAndBehavior, appearance, codeEditor));
        window.Close();
    }

    // The items of the gear menu of the list of themes / color schemes.
    private static readonly string[] SchemeActionItems =
    {
        "DuplicateItem", "RenameItem", "DeleteItem", "RestoreItem", "ExportItem", "ImportItem",
    };

    /// <summary>
    /// Appearance (Rider's page of the same name) lists the themes and, apart, the color schemes of the editors, each with
    /// the one in use selected and the actions in a gear menu; the theme has the settings of a theme as a whole. Choosing a
    /// dark theme brings the dark scheme, as in Rider.
    /// </summary>
    [AvaloniaFact]
    public void Appearance_page_lists_the_themes_and_the_color_schemes_with_their_actions()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ShowPage(window, "AppearanceGeneralPage");

        window.Sections.Selected!.Path.Should().Be(
            Strings.Get("PreferencesWindow_AppearanceAndBehavior") + " › " + Strings.Get("PreferencesWindow_Appearance"));
        ThemeSelectorView theme = window.FindControl<ThemeSelectorView>("ThemeSelector")!;
        ThemeSelectorView scheme = window.FindControl<ThemeSelectorView>("SchemeSelector")!;
        theme.DataContext.Should().BeSameAs(vm.Theme);
        scheme.DataContext.Should().BeSameAs(vm.ColorScheme);
        theme.List.ItemCount.Should().Be(2);
        scheme.List.ItemCount.Should().Be(2);
        ((ThemeOption)theme.List.SelectedItem!).Id.Should().Be(Signet.Core.Themes.ThemeStore.LightId);
        ((ThemeOption)scheme.List.SelectedItem!).Id.Should().Be(Signet.Core.Themes.ThemeStore.LightId);
        window.FindControl<ComboBox>("VariantBox")!.SelectedItem.Should().Be(vm.Theme.Variant);
        foreach (ThemeSelectorView selector in new[] { theme, scheme })
        {
            SchemeActionItems.Should().OnlyContain(name => selector.FindControl<MenuItem>(name) != null, "the gear menu has every action");
        }

        vm.Theme.RenameCommand.CanExecute(null).Should().BeFalse("a bundled theme keeps its name");

        theme.List.SelectedItem = vm.Theme.Themes.Single(t => t.Id == Signet.Core.Themes.ThemeStore.DarkId);
        Dispatcher.UIThread.RunJobs();

        vm.HasChanges.Should().BeTrue();
        ((VariantOption)window.FindControl<ComboBox>("VariantBox")!.SelectedItem!).Value.Should().Be(Signet.Core.Themes.InterfaceVariant.Dark);
        ((ThemeOption)scheme.List.SelectedItem!).Id.Should().Be(Signet.Core.Themes.ThemeStore.DarkId, "a dark theme brings the dark scheme");
        window.Close();
    }

    /// <summary>
    /// Code Editor › Color Scheme manages the color schemes: the same list as on the Appearance page, without the settings
    /// of a theme.
    /// </summary>
    [AvaloniaFact]
    public void Color_scheme_page_lists_the_color_schemes_with_their_actions()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ShowPage(window, "ColorSchemePage");

        window.Sections.Selected!.Path.Should().Be(string.Join(" › ",
            Strings.Get("PreferencesWindow_AppearanceAndBehavior"), Strings.Get("PreferencesWindow_CodeEditor"),
            Strings.Get("PreferencesWindow_ColorScheme")));
        ThemeSelectorView selector = window.FindControl<ThemeSelectorView>("ColorSchemeSelector")!;
        selector.DataContext.Should().BeSameAs(vm.ColorScheme);
        selector.List.ItemCount.Should().Be(2);
        SchemeActionItems.Should().OnlyContain(name => selector.FindControl<MenuItem>(name) != null);

        selector.List.SelectedItem = vm.ColorScheme.Themes.Single(t => t.Id == Signet.Core.Themes.ThemeStore.DarkId);
        Dispatcher.UIThread.RunJobs();

        vm.HasChanges.Should().BeTrue();
        ((ThemeOption)window.FindControl<ThemeSelectorView>("SchemeSelector")!.List.SelectedItem!).Id
            .Should().Be(Signet.Core.Themes.ThemeStore.DarkId, "the Appearance page shows the same scheme");
        vm.Theme.Selected!.Id.Should().Be(Signet.Core.Themes.ThemeStore.LightId, "a scheme never changes the theme");
        window.Close();
    }

    /// <summary>
    /// A page of the keys of the theme shows the tree of its keys and the panel of the selected one; the link to the
    /// base of an inheriting key opens the page of the base with it selected.
    /// </summary>
    [AvaloniaFact]
    public void A_color_scheme_page_shows_its_keys_and_links_to_the_base_of_a_key()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ThemeKeysPageViewModel xhtml = vm.ColorScheme.PageFor(Signet.Core.Themes.ThemePage.Xhtml);
        xhtml.SelectKey(Signet.Core.Themes.ThemeKeys.XhtmlCss);

        ShowPage(window, "ThemeXhtmlPage");

        window.Sections.Selected!.Path.Should().Be(string.Join(" › ",
            Strings.Get("PreferencesWindow_AppearanceAndBehavior"), Strings.Get("PreferencesWindow_CodeEditor"),
            Strings.Get("PreferencesWindow_ColorScheme"), Strings.Get("PreferencesWindow_ThemeXhtml")));
        ThemeKeysView view = window.FindControl<ThemeKeysView>("ThemeXhtmlView")!;
        view.DataContext.Should().BeSameAs(xhtml);
        var source = (HierarchicalTreeDataGridSource<ThemeKeyNode>)view.FindControl<TreeDataGrid>("Tree")!.Source!;
        source.RowSelection!.SelectedItem.Should().BeSameAs(xhtml.Selected);
        view.FindControl<StackPanel>("AttributesPanel")!.IsVisible.Should().BeTrue();
        view.FindControl<StackPanel>("ColorPanel")!.IsVisible.Should().BeFalse();
        view.FindControl<CheckBox>("InheritBox")!.IsChecked.Should().BeFalse("Signet Light sets nothing, so xhtml.css keeps its default");

        view.FindControl<HyperlinkButton>("BaseLink")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        window.Sections.Selected!.Title.Should().Be(Strings.Get("PreferencesWindow_ThemeCss"));
        vm.ColorScheme.PageFor(Signet.Core.Themes.ThemePage.Css).Key.Should().Be(Signet.Core.Themes.ThemeKeys.CssValue);
        window.Close();
    }

    /// <summary>
    /// The preview of a page shows its demo text in the colors of the theme being edited (also the changes not saved
    /// yet), and a click on a fragment selects its key in the tree.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_of_a_color_scheme_page_follows_the_edits_and_a_click_selects_the_key()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "ThemeXhtmlPage");
        ThemeKeysPageViewModel xhtml = vm.ColorScheme.PageFor(ThemePage.Xhtml);
        ThemePreviewEditor preview = window.FindControl<ThemeKeysView>("ThemeXhtmlView")!
            .GetVisualDescendants().OfType<ThemePreviewEditor>().Single();
        preview.Document.Text.Should().Be(ThemeSamples.Xhtml.Text);
        int head = preview.Document.Text.IndexOf("<head>", StringComparison.Ordinal) + 2;

        xhtml.SelectKey(ThemeKeys.XhtmlTag);
        xhtml.Foreground = "#123456";
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        ForegroundAt(preview, head).Should().Be(Color.Parse("#123456"));

        AvaloniaEdit.Rendering.TextView view = preview.TextArea.TextView;
        AvaloniaEdit.TextViewPosition position = new(preview.Document.GetLocation(preview.Document.Text.IndexOf("xmlns:epub", StringComparison.Ordinal) + 1));
        Avalonia.Point point = view.GetVisualPosition(position, AvaloniaEdit.Rendering.VisualYPosition.LineMiddle) - view.ScrollOffset;
        Avalonia.Point at = Avalonia.VisualExtensions.TranslatePoint(view, point, window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        xhtml.Key!.Id.Should().Be(ThemeKeys.XhtmlNamespacePrefix.Id);
        window.Close();
    }

    /// <summary>
    /// A click on the number of the line with the caret in the preview selects its own key; a click on another number
    /// selects the line numbers. The number of the current line is drawn with the look of its key.
    /// </summary>
    [AvaloniaFact]
    public void The_number_of_the_current_line_in_the_preview_has_a_key_of_its_own()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "ThemeGeneralPage");
        ThemeKeysPageViewModel general = vm.ColorScheme.PageFor(ThemePage.General);
        ThemePreviewEditor preview = window.FindControl<ThemeKeysView>("ThemeGeneralView")!
            .GetVisualDescendants().OfType<ThemePreviewEditor>().Single();
        ThemedLineNumberMargin margin = preview.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single();
        int caretLine = ThemeSamples.General.CaretLine;
        AvaloniaEdit.Rendering.TextView view = preview.TextArea.TextView;

        void ClickNumberOf(int line)
        {
            AvaloniaEdit.TextViewPosition position = new(line, 1);
            double y = view.GetVisualPosition(position, AvaloniaEdit.Rendering.VisualYPosition.LineMiddle).Y - view.ScrollOffset.Y;
            Avalonia.Point at = Avalonia.VisualExtensions.TranslatePoint(view, new Avalonia.Point(-8, y), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        ClickNumberOf(caretLine);
        general.Key!.Id.Should().Be(ThemeKeys.EditorCurrentLineNumber.Id);

        ClickNumberOf(caretLine == 1 ? 2 : 1);
        general.Key!.Id.Should().Be(ThemeKeys.EditorLineNumbers.Id);

        general.SelectKey(ThemeKeys.EditorCurrentLineNumber);
        general.Foreground = "#112233";
        general.Bold = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        margin.CurrentLineOverride.Should().Be(caretLine);
        ((ISolidColorBrush)margin.CurrentForeground!).Color.Should().Be(Color.Parse("#112233"));
        margin.CurrentBold.Should().BeTrue();
        window.Close();
    }

    /// <summary>
    /// The Book Browser page has one "Preview" panel at the right, common to its General and Fonts tabs: the real Book Browser over a
    /// small sample book, in the look of the settings (the unsaved ones), and the sample of a tooltip. The fonts have no preview
    /// each under them.
    /// </summary>
    [AvaloniaFact]
    public void The_Book_Browser_page_has_one_preview_panel_for_both_its_tabs_that_follows_the_settings()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "BookBrowserAppearancePage");
        TabControl tabs = window.FindControl<TabControl>("BookBrowserAppearanceTabs")!;
        Border pane = window.FindControl<Border>("BookBrowserPreviewPane")!;
        BookBrowserView preview = window.FindControl<BookBrowserView>("BookBrowserPreview")!;

        pane.IsEffectivelyVisible.Should().BeTrue("the General tab is selected");
        pane.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == Strings.Get("PreferencesWindow_Preview"));
        preview.DataContext.Should().BeSameAs(vm.BookBrowserSample);
        pane.Bounds.Left.Should().BeGreaterThan(tabs.Bounds.Left + 300, "the panel is at the right of the tabs");

        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        pane.IsEffectivelyVisible.Should().BeTrue("the same panel stays while the Fonts tab is selected");
        FontsPageView fonts = window.FindControl<FontsPageView>("BookBrowserFontsView")!;
        FontSettingView[] rows = fonts.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => !r.FindControl<Border>("PreviewFrame")!.IsVisible, "the panel is the preview of the fonts");
        fonts.FindControl<GridSplitter>("PreviewSplitter")!.IsVisible.Should().BeFalse();

        TextBlock chapter = preview.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Chapter01.xhtml");
        TextBlock tip = window.FindControl<TooltipSample>("BookBrowserSampleTip")!.Text;
        tip.Text.Should().Be(Strings.Get("FontPreview_TooltipBookBrowser"));

        vm.BookBrowserItemsFontSize = 23;
        FontSettingViewModel tooltipFont = vm.BookBrowserFonts.Fonts.Single(f => f.Key == "Tooltip" + Signet.Core.Misc.TooltipArea.BookBrowser);
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 18;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        preview.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Chapter01.xhtml").FontSize.Should().Be(23, "the preview follows the font");
        tip.FontSize.Should().Be(18, "and so does the sample of a tooltip");
        _ = chapter;
        window.Close();
    }

    /// <summary>
    /// The Start Screen page has the same kind of panel: the real start screen with fabricated books, following the settings of
    /// both tabs, and the sample of a tooltip; the settings fill their tab.
    /// </summary>
    [AvaloniaFact]
    public void The_Start_Screen_page_has_one_preview_panel_with_the_list_of_recent_files_that_follows_the_settings()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "StartScreenAppearancePage");
        TabControl tabs = window.FindControl<TabControl>("StartScreenAppearanceTabs")!;
        Border pane = window.FindControl<Border>("StartScreenPreviewPane")!;
        StartScreenView preview = window.FindControl<StartScreenView>("StartScreenPreview")!;

        pane.IsEffectivelyVisible.Should().BeTrue("the General tab is selected");
        preview.DataContext.Should().BeSameAs(vm.StartScreenSample);
        window.FindControl<NumericUpDown>("StartScreenRecentFilesLimitBox")!.Value.Should().Be(vm.RecentFilesLimit);
        window.FindControl<NumericUpDown>("StartScreenIndentBox")!.Value = 30;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        vm.StartScreenIndent.Should().Be(30);
        Button[] entries = preview.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentFile")).ToArray();
        entries.Should().HaveCount(vm.RecentFilesLimit);
        entries.Should().OnlyContain(b => b.Padding.Left == 30, "the indentation of the entries follows the setting");
        entries.Where(b => b.Classes.Contains("highlighted")).Should().ContainSingle();

        window.FindControl<NumericUpDown>("StartScreenRecentFilesLimitBox")!.Value = 3;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        preview.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("recentFile")).Should().Be(3);

        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        pane.IsEffectivelyVisible.Should().BeTrue("the same panel stays while the Fonts tab is selected");
        FontsPageView fonts = window.FindControl<FontsPageView>("StartScreenFontsView")!;
        FontSettingView[] rows = fonts.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(r => !r.FindControl<Border>("PreviewFrame")!.IsVisible && r.Bounds.Width > fonts.Bounds.Width - 60);

        vm.StartScreenTitleFontSize = 22;
        vm.StartScreenLocationFontSize = 15;
        vm.StartScreenSectionFontSize = 19;
        FontSettingViewModel tooltipFont = vm.StartScreenFonts.Fonts.Single(f => f.Key == "Tooltip" + Signet.Core.Misc.TooltipArea.StartScreen);
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 17;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        preview.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("recentTitle")).Should().OnlyContain(t => t.FontSize == 22);
        preview.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("recentLocation")).Should().OnlyContain(t => t.FontSize == 15);
        preview.FindControl<TextBlock>("RecentFilesHeading")!.FontSize.Should().Be(19);
        window.FindControl<TooltipSample>("StartScreenSampleTip")!.Text.FontSize.Should().Be(17);
        window.Close();
    }

    /// <summary>
    /// The Code Editor page has the same kind of panel: the tabs of the documents and the real Code Editor tab over a chapter of the
    /// sample book, both following the settings, and the opening tag hint, which is always shown there. The look of the editor
    /// (highlighting, tabs in several rows) is on the General tab.
    /// </summary>
    [AvaloniaFact]
    public void The_Code_Editor_page_has_one_preview_panel_with_the_tabs_and_the_editor_that_follows_the_settings()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1200;
        window.Height = 900;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "CodeEditorAppearancePage");
        TabControl tabs = window.FindControl<TabControl>("CodeEditorAppearanceTabs")!;
        Border pane = window.FindControl<Border>("CodeEditorPreviewPane")!;
        Signet.App.Views.Tabs.CodeTabView preview = window.FindControl<Signet.App.Views.Tabs.CodeTabView>("CodeEditorPreview")!;
        DocumentTabsSample strip = window.FindControl<DocumentTabsSample>("DocumentTabsPreview")!;

        pane.IsEffectivelyVisible.Should().BeTrue("the General tab is selected");
        preview.DataContext.Should().BeSameAs(vm.CodeEditorSample);
        window.FindControl<CheckBox>("ExtendedHighlightingBox")!.IsEffectivelyVisible.Should().BeTrue("the look of the editor is on the General tab");
        window.FindControl<CheckBox>("MultilineTabsBox")!.IsEffectivelyVisible.Should().BeTrue();
        AvaloniaEdit.TextEditor editor = preview.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        editor.IsReadOnly.Should().BeTrue("the sample is not edited");
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        editor.Document.GetText(editor.Document.GetLineByOffset(editor.CaretOffset)).Should().Contain("class=\"lead\"", "the caret rests in a tag");

        strip.GetVisualDescendants().OfType<WrapPanel>().Should().BeEmpty("the tabs are in one row that scrolls");
        window.FindControl<CheckBox>("MultilineTabsBox")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        strip.GetVisualDescendants().OfType<WrapPanel>().Should().NotBeEmpty("the tabs that do not fit go to several rows now");

        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        pane.IsEffectivelyVisible.Should().BeTrue("the same panel stays while the Fonts tab is selected");
        FontsPageView fonts = window.FindControl<FontsPageView>("CodeEditorFontsView")!;
        FontSettingView[] rows = fonts.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.Select(r => r.Name).Should().Equal("FontCodeEditorText", "FontLineNumbers", "FontOpenTagHint", "FontDocumentTabs");
        rows.Should().OnlyContain(r => !r.FindControl<Border>("PreviewFrame")!.IsVisible && r.Bounds.Width > fonts.Bounds.Width - 60);

        vm.CodeEditor.FontSize = 19;
        vm.OpenTagHintFontSize = 15;
        vm.DocumentTabFontSize = 17;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        editor.FontSize.Should().Be(19, "the editor follows the font");
        window.FindControl<TooltipSample>("CodeEditorSampleHint")!.Text.FontSize.Should().Be(15);
        strip.GetVisualDescendants().OfType<TextBlock>().Should().OnlyContain(t => t.FontSize == 17, "the tabs follow their font");
        window.Close();
    }

    /// <summary>
    /// The Find Usages page is wide, so its preview panel is under the settings, not at their right: the real Find Usages panel over
    /// fabricated usages, following the settings of both tabs; its height is remembered.
    /// </summary>
    [AvaloniaFact]
    public void The_Find_Usages_page_has_one_preview_panel_under_the_settings_that_follows_them()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 900;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "FindUsagesPage");
        TabControl tabs = window.FindControl<TabControl>("FindUsagesTabs")!;
        Border pane = window.FindControl<Border>("FindUsagesPreviewPane")!;
        FindUsagesView preview = window.FindControl<FindUsagesView>("FindUsagesPreview")!;
        window.CaptureRenderedFrame();

        pane.IsEffectivelyVisible.Should().BeTrue("the General tab is selected");
        preview.DataContext.Should().BeSameAs(vm.FindUsagesSample);
        pane.Bounds.Top.Should().BeGreaterThanOrEqualTo(tabs.Bounds.Bottom, "the panel is under the tabs, as the window is wide");
        pane.Bounds.Width.Should().BeGreaterThan(tabs.Bounds.Width - 4);
        TreeDataGrid grid = preview.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        grid.GetVisualDescendants().OfType<TreeDataGridRow>().Should().NotBeEmpty("the sample usages are shown");

        window.FindControl<NumericUpDown>("FindUsagesRowSpacingBox")!.Value = 6;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        grid.GetVisualDescendants().OfType<TreeDataGridRow>().Should().OnlyContain(r => r.Margin.Bottom == 6, "the row spacing follows the setting");

        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        pane.IsEffectivelyVisible.Should().BeTrue("the same panel stays while the Fonts tab is selected");
        FontsPageView fonts = window.FindControl<FontsPageView>("FindUsagesFontsView")!;
        FontSettingView[] rows = fonts.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => !r.FindControl<Border>("PreviewFrame")!.IsVisible && r.Bounds.Width > fonts.Bounds.Width - 60);

        vm.FindUsagesCellsFontSize = 18;
        vm.FindUsagesHeaderFontSize = 15;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        grid.GetVisualDescendants().OfType<TreeDataGridTextCell>().Should().Contain(c => c.FontSize == 18, "the cells follow their font");
        grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Should().OnlyContain(h => h.FontSize == 15, "and so do the headers");

        window.SplitterSizes.Should().ContainKey("FindUsagesPreviewHost.paneHeight");
        window.SplitterSizes = new Dictionary<string, int> { ["FindUsagesPreviewHost.paneHeight"] = 310 };
        window.SplitterSizes["FindUsagesPreviewHost.paneHeight"].Should().Be(310);
        window.Close();
    }

    // A page of Appearance that has only the font of its tooltip (and perhaps others) and a preview panel next to its settings:
    // the panel is there with the sample of the tooltip, which follows the unsaved font; the rows have no previews of their own.
    private static (PreferencesWindow Window, PreferencesViewModel Vm, Border Pane, TooltipSample Tip) ShowPreviewPage(
        string part, string hostName, string paneName, string tipName, bool bottom)
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 900;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, part + "AppearancePage");
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        Border pane = window.FindControl<Border>(paneName)!;
        TabControl tabs = window.FindControl<TabControl>(part + "AppearanceTabs")!;
        pane.IsEffectivelyVisible.Should().BeTrue("the page has a preview panel");
        if (bottom)
        {
            pane.Bounds.Top.Should().BeGreaterThanOrEqualTo(tabs.Bounds.Bottom, "the panel is under the settings");
        }
        else
        {
            pane.Bounds.Left.Should().BeGreaterThanOrEqualTo(tabs.Bounds.Right, "the panel is at the right of the settings");
        }

        window.SplitterSizes.Should().ContainKey(hostName + (bottom ? ".paneHeight" : ".paneWidth"));
        // (A page with a General tab shows its fonts only on the other tab.)
        if (window.GetVisualDescendants().OfType<FontsPageView>().FirstOrDefault(p => p.IsEffectivelyVisible) is { } fonts)
        {
            fonts.GetVisualDescendants().OfType<FontSettingView>().Should().OnlyContain(
                r => !r.FindControl<Border>("PreviewFrame")!.IsVisible, "the panel is the preview of the fonts");
        }

        return (window, vm, pane, window.FindControl<TooltipSample>(tipName)!);
    }

    /// <summary>The Find and Replace panel is wide, so its preview is under the settings: the real panel with no book behind it.</summary>
    [AvaloniaFact]
    public void The_Find_and_Replace_page_has_a_preview_panel_with_the_real_panel_and_the_tooltip_sample()
    {
        (PreferencesWindow window, PreferencesViewModel vm, Border pane, TooltipSample tip) =
            ShowPreviewPage("FindReplace", "FindReplacePreviewHost", "FindReplacePreviewPane", "FindReplaceSampleTip", bottom: true);
        FindReplaceView preview = window.FindControl<FindReplaceView>("FindReplacePreview")!;

        preview.DataContext.Should().BeSameAs(vm.FindReplaceSample);
        preview.FindControl<AutoCompleteBox>("FindBox")!.Text.Should().Be(vm.FindReplaceSample.FindText);
        pane.IsEffectivelyVisible.Should().BeTrue();

        FontSettingViewModel tooltipFont = vm.FindReplaceFonts.Fonts.Single();
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 19;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        tip.Text.FontSize.Should().Be(19, "the sample of the tooltip follows the font");
        window.Close();
    }

    /// <summary>
    /// The Special Characters page: the preview is the "Insert Special Character" window (wide, so under the settings) with a few
    /// characters whose column takes the unsaved font, and a sample of the tooltip.
    /// </summary>
    [AvaloniaFact]
    public void The_Special_Characters_page_has_a_preview_panel_with_the_table_in_the_unsaved_font()
    {
        (PreferencesWindow window, PreferencesViewModel vm, _, TooltipSample tip) = ShowPreviewPage(
            "SpecialCharacters", "SpecialCharactersPreviewHost", "SpecialCharactersPreviewPane", "SpecialCharactersSampleTip", bottom: true);
        SpecialCharactersSample preview = window.FindControl<SpecialCharactersSample>("SpecialCharactersPreview")!;
        TreeDataGrid grid = preview.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        grid.GetVisualDescendants().OfType<TreeDataGridRow>().Should().NotBeEmpty("the sample characters are shown");

        vm.SpecialCharacterFontSize = 27;
        vm.SpecialCharacterFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.SpecialCharacters).InheritFromParent = false;
        vm.SpecialCharacterFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.SpecialCharacters).Size = 18;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        grid.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("symbol")).Should()
            .NotBeEmpty().And.OnlyContain(t => t.FontSize == 27, "the characters follow the font");
        tip.Text.FontSize.Should().Be(18);
        window.Close();
    }

    /// <summary>The Cleanup page: the preview is at the right of the settings, with the real dialog content and a sample of its tooltip.</summary>
    [AvaloniaFact]
    public void The_Cleanup_page_has_a_preview_panel_with_the_dialog_and_the_tooltip_sample()
    {
        (PreferencesWindow window, PreferencesViewModel vm, _, TooltipSample tip) = ShowPreviewPage(
            "Cleanup", "CleanupPreviewHost", "CleanupPreviewPane", "CleanupSampleTip", bottom: false);
        CleanupView preview = window.FindControl<CleanupView>("CleanupPreview")!;

        preview.DataContext.Should().BeSameAs(vm.CleanupSample);
        preview.GetVisualDescendants().OfType<TabControl>().Should().NotBeEmpty("the tabs of the cleanup steps are shown");

        FontSettingViewModel tooltipFont = vm.CleanupFonts.Fonts.Single();
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 20;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        tip.Text.FontSize.Should().Be(20);
        window.Close();
    }

    /// <summary>The Footnotes page: the preview is the footnote window (wide, so under the settings) with a note filled in, and a sample of the tooltip.</summary>
    [AvaloniaFact]
    public void The_Footnotes_page_has_a_preview_panel_with_the_footnote_window_and_the_tooltip_sample()
    {
        (PreferencesWindow window, PreferencesViewModel vm, _, TooltipSample tip) = ShowPreviewPage(
            "Footnotes", "FootnotesPreviewHost", "FootnotesPreviewPane", "FootnotesSampleTip", bottom: true);
        FootnoteView preview = window.FindControl<FootnoteView>("FootnotesPreview")!;

        preview.DataContext.Should().BeSameAs(vm.FootnoteSample);
        preview.GetVisualDescendants().OfType<TextBox>().Should().Contain(t => t.Text == vm.FootnoteSample.Content, "the note is filled in");
        preview.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single().Text.Should().Be(vm.FootnoteSample.PreviewCode);

        FontSettingViewModel tooltipFont = vm.FootnotesFonts.Fonts.Single();
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 21;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        tip.Text.FontSize.Should().Be(21);
        window.Close();
    }

    /// <summary>The Validation page: the preview is the real Validation Results panel (wide, so under the settings) over fabricated results, and a sample of the tooltip.</summary>
    [AvaloniaFact]
    public void The_Validation_page_has_a_preview_panel_with_the_results_panel_and_the_tooltip_sample()
    {
        (PreferencesWindow window, PreferencesViewModel vm, _, TooltipSample tip) = ShowPreviewPage(
            "Validation", "ValidationPreviewHost", "ValidationPreviewPane", "ValidationSampleTip", bottom: true);
        ValidationResultsView preview = window.FindControl<ValidationResultsView>("ValidationPreview")!;
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        preview.DataContext.Should().BeSameAs(vm.ValidationSample);
        preview.GetVisualDescendants().OfType<TreeDataGridRow>().Should().NotBeEmpty("the fabricated results are shown");

        FontSettingViewModel tooltipFont = vm.ValidationFonts.Fonts.Single();
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 22;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        tip.Text.FontSize.Should().Be(22);
        window.Close();
    }

    /// <summary>
    /// The Preview page: the panel at the right draws a page in the fonts of the settings with the mark of the caret as the real
    /// preview draws it (the real one is a web view), following both tabs; a click on a paragraph moves the mark.
    /// </summary>
    [AvaloniaFact]
    public void The_Preview_page_has_a_preview_panel_with_a_page_in_the_unsaved_fonts_and_the_mark_of_the_caret()
    {
        (PreferencesWindow window, PreferencesViewModel vm, _, TooltipSample tip) = ShowPreviewPage(
            "Preview", "PreviewPreviewHost", "PreviewPreviewPane", "PreviewSampleTip", bottom: false);
        PreviewSample sample = window.FindControl<PreviewSample>("PreviewSamplePage")!;
        TextBlock serif = sample.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SerifText");
        Border[] blocks = { sample.FindControl<Border>("Block0")!, sample.FindControl<Border>("Block1")!, sample.FindControl<Border>("Block2")! };

        sample.MarkedBlock.Should().Be(1);
        blocks[1].Background.Should().BeAssignableTo<ISolidColorBrush>("the marked block has the background of the highlight");
        ((ISolidColorBrush)blocks[1].Background!).Opacity.Should().BeApproximately(vm.PreviewHighlightOpacity / 100.0, 0.01);
        blocks[0].Background.Should().Be(Brushes.Transparent);

        vm.PreviewFontSerif = "Georgia";
        vm.PreviewFontSize = 24;
        vm.PreviewHighlightStyle = PreviewHighlightStyle.Outline;
        vm.PreviewHighlightOutlineWidth = 5;
        Dispatcher.UIThread.RunJobs();

        serif.FontFamily.Name.Should().Be("Georgia");
        serif.FontSize.Should().Be(24, "the page follows the fonts");
        blocks[1].BorderThickness.Should().Be(new Thickness(5));
        blocks[1].BorderBrush.Should().BeAssignableTo<ISolidColorBrush>("the marked block has the outline");
        blocks[1].Background.Should().Be(Brushes.Transparent);

        blocks[2].RaiseEvent(new PointerPressedEventArgs(
            blocks[2], new Pointer(0, PointerType.Mouse, true), blocks[2], new Point(2, 2),
            0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));

        sample.MarkedBlock.Should().Be(2, "a click moves the mark, as the caret does");

        TabControl tabs = window.FindControl<TabControl>("PreviewAppearanceTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        window.FindControl<Border>("PreviewPreviewPane")!.IsEffectivelyVisible.Should().BeTrue("the same panel stays on the Fonts tab");

        FontSettingViewModel tooltipFont = vm.PreviewFonts.Fonts.Single(f => f.Key == "Tooltip" + TooltipArea.Preview);
        tooltipFont.InheritFromParent = false;
        tooltipFont.Size = 19;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        tip.Text.FontSize.Should().Be(19);
        window.Close();
    }

    /// <summary>The colors of the theme: the name, the folder and the highlighted entry of the list take them when they are set.</summary>
    [AvaloniaFact]
    public void The_list_of_recent_files_takes_the_colors_of_the_theme_when_they_are_set()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "StartScreenAppearancePage");
        StartScreenView preview = window.FindControl<StartScreenView>("StartScreenPreview")!;
        TextBlock Title() => preview.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("recentTitle"));
        TextBlock Location() => preview.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("recentLocation"));
        double dimmed = Location().Opacity;
        dimmed.Should().BeLessThan(1, "nothing is set: the folder is dimmed text");

        Theme theme = new Theme("t", "T")
            .With(ThemeKeys.UiStartScreenTitle.Id, ThemeEntry.Of(ThemeColor.Parse("#00FF00")))
            .With(ThemeKeys.UiStartScreenLocation.Id, ThemeEntry.Of(ThemeColor.Parse("#0000FF")))
            .With(ThemeKeys.UiStartScreenHighlight.Id, ThemeEntry.Of(ThemeColor.Parse("#FF0000")));
        try
        {
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Of(ThemeDocumentKind.Theme, theme, new ThemeResolver(_ => null)));
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();

            ((ISolidColorBrush)Title().Foreground!).Color.Should().Be(Colors.Lime);
            ((ISolidColorBrush)Location().Foreground!).Color.Should().Be(Colors.Blue);
            Location().Opacity.Should().Be(1, "a color of the theme is the color shown");
            Button highlighted = preview.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("highlighted"));
            ((ISolidColorBrush)highlighted.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter").Background!)
                .Color.Should().Be(Colors.Red, "the entry drawn as if the pointer were on it");
        }
        finally
        {
            AppTheme.Apply(Avalonia.Application.Current!, ResolvedTheme.Defaults);
        }

        Dispatcher.UIThread.RunJobs();
        Location().Opacity.Should().Be(dimmed, "the default theme sets nothing again");
        window.Close();
    }

    /// <summary>The panel of the preview is not searched (it holds samples, not settings), and its width is remembered.</summary>
    [AvaloniaFact]
    public void The_width_of_the_preview_panel_is_remembered_and_its_samples_are_not_searched()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "BookBrowserAppearancePage");

        window.SplitterSizes = new Dictionary<string, int> { ["FontsPreview"] = 380, ["BookBrowserFontsView.previewWidth"] = 420 };
        TabControl tabs = window.FindControl<TabControl>("BookBrowserAppearanceTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Last();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        FontsPageView fonts = window.FindControl<FontsPageView>("BookBrowserFontsView")!;
        fonts.GetVisualDescendants().OfType<FontSettingView>().Should().OnlyContain(
            r => r.Bounds.Width > fonts.Bounds.Width - 60, "the settings take the whole tab, whatever width the old pages remembered");

        window.SplitterSizes.Should().ContainKey("BookBrowserPreviewHost.paneWidth");
        window.SplitterSizes = new Dictionary<string, int> { ["BookBrowserPreviewHost.paneWidth"] = 410 };
        window.SplitterSizes["BookBrowserPreviewHost.paneWidth"].Should().Be(410);

        window.Sections.Query = "Titlepage";

        window.Sections.All.Where(n => n.IsVisible && n.Id.Length > 0).Should().BeEmpty("a file name of the sample is not a setting");
        window.Close();
    }

    /// <summary>
    /// The link of the Book Browser page to the color of its background opens the Interface Colors tab of Appearance with
    /// that very key selected (its group expanded).
    /// </summary>
    [AvaloniaFact]
    public void The_link_to_the_book_browser_background_selects_its_key_in_the_theme()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "BookBrowserAppearancePage");
        HyperlinkButton link = window.GetVisualDescendants().OfType<HyperlinkButton>()
            .Single(l => l.IsEffectivelyVisible && Equals(l.Content, Strings.Get("PreferencesWindow_BookBrowserBackgroundInTheme")));

        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        window.Sections.Selected!.Title.Should().Be(Strings.Get("PreferencesWindow_Appearance"));
        window.FindControl<TabControl>("AppearanceGeneralTabs")!.SelectedItem.Should().BeSameAs(window.FindControl<TabItem>("InterfaceColorsTab"));
        window.FindControl<ThemeKeysView>("ThemeInterfaceView")!.IsEffectivelyVisible.Should().BeTrue();
        ThemeKeysPageViewModel page = vm.Theme.PageFor(ThemePage.Interface);
        page.Key.Should().Be(ThemeKeys.UiBookBrowserBackground);
        page.Roots.Single(r => r.Children.Any(c => c.Key == ThemeKeys.UiBookBrowserBackground)).IsExpanded.Should().BeTrue();
        window.Close();
    }

    /// <summary>
    /// Both splitters of a color scheme page move (the width of the tree, the height of the preview), and every page
    /// keeps its own sizes, which the window hands out to be remembered and takes back.
    /// </summary>
    [AvaloniaFact]
    public void The_splitters_of_a_color_scheme_page_resize_it_and_every_page_keeps_its_own_sizes()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "ThemeCssPage");
        window.CaptureRenderedFrame();
        ThemeKeysView css = window.FindControl<ThemeKeysView>("ThemeCssView")!;
        double treeWidth = css.TreeWidth;
        double previewHeight = css.PreviewHeight;

        Drag(window, css.FindControl<GridSplitter>("TreeSplitter")!, new Avalonia.Vector(60, 0));
        Drag(window, css.FindControl<GridSplitter>("PreviewSplitter")!, new Avalonia.Vector(0, -50));

        css.TreeWidth.Should().BeApproximately(treeWidth + 60, 1);
        css.PreviewHeight.Should().BeApproximately(previewHeight + 50, 1);
        IReadOnlyDictionary<string, int> sizes = window.SplitterSizes;
        sizes.Should().Contain("ThemeCssView.tree", (int)Math.Round(css.TreeWidth))
            .And.Contain("ThemeCssView.preview", (int)Math.Round(css.PreviewHeight))
            .And.Contain("ThemeXhtmlView.tree", (int)treeWidth, "the other pages keep theirs");

        window.SplitterSizes = new Dictionary<string, int> { ["ThemeXhtmlView.tree"] = 250, ["ThemeXhtmlView.preview"] = 20 };
        ThemeKeysView xhtml = window.FindControl<ThemeKeysView>("ThemeXhtmlView")!;
        xhtml.TreeWidth.Should().Be(250);
        xhtml.PreviewHeight.Should().Be(80, "not below the minimum");
        css.TreeWidth.Should().BeApproximately(treeWidth + 60, 1, "a page missing from the map keeps its size");
        window.Close();
    }

    /// <summary>The splitter between the section tree and the page moves, and the window remembers its place.</summary>
    [AvaloniaFact]
    public void The_splitter_of_the_section_tree_resizes_it_and_is_remembered()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1100;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Border panel = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("sectionPanel"));
        double width = panel.Bounds.Width;
        int remembered = window.SplitterSizes["Sections"];

        Drag(window, window.FindControl<GridSplitter>("SectionsSplitter")!, new Avalonia.Vector(70, 0));

        panel.Bounds.Width.Should().BeApproximately(width + 70, 1);
        window.SplitterSizes["Sections"].Should().BeCloseTo(remembered + 70, 1);
        window.SplitterSizes = new Dictionary<string, int> { ["Sections"] = 40 };
        window.SplitterSizes["Sections"].Should().Be(150, "not below the minimum");
        window.Close();
    }

    /// <summary>The code previews of the color scheme pages use the font set on the Color Scheme page, also when it changes.</summary>
    [AvaloniaFact]
    public void The_code_previews_use_the_font_of_the_color_scheme_page()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "ColorSchemePage");
        window.FindControl<FontSettingView>("ColorSchemePreviewFontView")!.DataContext.Should().BeSameAs(vm.ColorSchemePreviewFont);
        ShowPage(window, "ThemeJavaScriptJsonPage");
        ThemePreviewEditor[] editors = window.FindControl<ThemeKeysView>("ThemeJavaScriptJsonView")!
            .GetVisualDescendants().OfType<ThemePreviewEditor>().ToArray();

        editors.Should().HaveCount(2).And.OnlyContain(e => e.FontFamily.Name == ColorSchemePreviewAppearance.Default.FontFamily
            && e.FontSize == ColorSchemePreviewAppearance.Default.FontSize);

        vm.ColorScheme.PreviewFont = new ColorSchemePreviewAppearance("Courier New", 17);
        Dispatcher.UIThread.RunJobs();

        editors.Should().OnlyContain(e => e.FontFamily.Name == "Courier New" && e.FontSize == 17);
        window.Close();
    }

    // Drags a splitter by the offset with the mouse.
    private static void Drag(Window window, Control splitter, Avalonia.Vector offset)
    {
        Avalonia.Point from = Avalonia.VisualExtensions.TranslatePoint(
            splitter, new Avalonia.Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + offset / 2, RawInputModifiers.LeftMouseButton);
        window.MouseMove(from + offset, RawInputModifiers.LeftMouseButton);
        window.MouseUp(from + offset, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }

    // The text color the preview draws at a document offset.
    private static Color ForegroundAt(ThemePreviewEditor editor, int offset)
    {
        AvaloniaEdit.Document.DocumentLine line = editor.Document.GetLineByOffset(offset);
        AvaloniaEdit.Rendering.VisualLine visual = editor.TextArea.TextView.GetOrConstructVisualLine(line);
        int column = offset - line.Offset;
        AvaloniaEdit.Rendering.VisualLineElement element = visual.Elements
            .First(e => column >= e.RelativeTextOffset && column < e.RelativeTextOffset + e.DocumentLength);
        return ((ISolidColorBrush)element.TextRunProperties.ForegroundBrush!).Color;
    }

    /// <summary>The Preview section (behaviour) links to its Appearance section; a click opens it in the tree.</summary>
    [AvaloniaFact]
    public void The_preview_section_links_to_its_appearance()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        PreferencesSectionNode top = window.Sections.Roots.Single(n => n.Title == Strings.Get("PreferencesWindow_AppearanceAndBehavior"));
        PreferencesSectionNode appearance = top.Children.Single(n => n.Title == Strings.Get("PreferencesWindow_Appearance"));
        var source = (HierarchicalTreeDataGridSource<PreferencesSectionNode>)window.FindControl<TreeDataGrid>("SectionTree")!.Source!;
        source.Collapse(new IndexPath(window.Sections.Roots.IndexOf(top), top.Children.IndexOf(appearance)));
        appearance.IsExpanded = false;
        ShowPage(window, "PreviewPage");
        HyperlinkButton link = window.FindControl<HyperlinkButton>("PreviewAppearanceLink")!;
        link.Content.Should().Be(Strings.Get("PreferencesWindow_Appearance"));
        ((StackPanel)link.Parent!).Children[0].Should().BeSameAs(link, "the link is above every other setting");

        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        PreferencesSectionNode target = window.Sections.Selected!;
        target.Path.Should().Be(string.Join(
            " › ", Strings.Get("PreferencesWindow_AppearanceAndBehavior"), Strings.Get("PreferencesWindow_Appearance"), Strings.Get("PreferencesWindow_Preview")));
        appearance.IsExpanded.Should().BeTrue("the link opens the collapsed branch, as a click in the tree");
        source.RowSelection!.SelectedItem.Should().BeSameAs(target, "the section is selected in the tree itself");
        window.Close();
    }

    /// <summary>
    /// The sections the user collapsed in the tree are remembered (by the name of their page, so the language does not
    /// matter) and collapsed again when the window opens; the enclosing sections of the selected one stay open.
    /// </summary>
    [AvaloniaFact]
    public void Collapsed_sections_are_remembered_and_restored()
    {
        PreferencesWindow first = BuildWindow();
        first.Show();
        Dispatcher.UIThread.RunJobs();
        var source = (HierarchicalTreeDataGridSource<PreferencesSectionNode>)first.FindControl<TreeDataGrid>("SectionTree")!.Source!;
        PreferencesSectionNode appearance = first.Sections.Roots.Single(n => n.Title == Strings.Get("PreferencesWindow_AppearanceAndBehavior"));
        PreferencesSectionNode codeEditor = appearance.Children.Single(n => n.Title == Strings.Get("PreferencesWindow_CodeEditor"));
        PreferencesSectionNode colorScheme = codeEditor.Children.Single(n => n.Title == Strings.Get("PreferencesWindow_ColorScheme"));
        first.CollapsedSections.Should().BeEmpty("a fresh tree is fully expanded");

        source.Collapse(new IndexPath(first.Sections.Roots.IndexOf(appearance), appearance.Children.IndexOf(codeEditor), codeEditor.Children.IndexOf(colorScheme)));
        Dispatcher.UIThread.RunJobs();
        appearance.IsExpanded.Should().BeTrue();
        colorScheme.IsExpanded.Should().BeFalse("collapsing a row in the tree updates the section");
        IReadOnlyList<string> remembered = first.CollapsedSections;
        remembered.Should().Equal("ColorSchemePage");
        first.Close();

        PreferencesWindow second = BuildWindow();
        second.Show();
        Dispatcher.UIThread.RunJobs();
        second.CollapsedSections = remembered;
        Dispatcher.UIThread.RunJobs();

        second.Sections.All.Single(n => n.Id == "ColorSchemePage").IsExpanded.Should().BeFalse();
        second.Sections.All.Single(n => n.Id == "AppearancePage").IsExpanded.Should().BeTrue();
        second.CollapsedSections.Should().Equal("ColorSchemePage");

        // A section that contains the selected one is never collapsed: the selection must stay visible.
        second.Sections.Selected = second.Sections.All.Single(n => n.Id == "ThemeCssPage");
        second.CollapsedSections = new[] { "ColorSchemePage", "AppearancePage" };
        second.Sections.All.Single(n => n.Id == "ColorSchemePage").IsExpanded.Should().BeFalse("it was collapsed before, the setter does not reopen it");
        second.Sections.All.Single(n => n.Id == "AppearancePage").IsExpanded.Should().BeTrue("it contains the selected section");
        second.Close();
    }

    /// <summary>
    /// Every Fonts page has a width of the preview of its own: setting it (what the window does with the remembered size)
    /// changes only that page, and it is part of the remembered sizes. The legacy shared width is the start for the pages
    /// that have none.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_width_of_a_fonts_page_is_its_own_and_remembered()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1500;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.SplitterSizes.Keys.Should().NotContain(k => k.EndsWith(".previewWidth"), "nothing was resized yet");
        ShowFontsTab(window, "TableOfContents");
        double tocBefore = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible).PreviewWidth;

        window.SplitterSizes = new System.Collections.Generic.Dictionary<string, int> { ["CheckpointsFontsView.previewWidth"] = 420 };
        Dispatcher.UIThread.RunJobs();

        window.SplitterSizes.Should().Contain("CheckpointsFontsView.previewWidth", 420);
        ShowFontsTab(window, "Checkpoints");
        window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible)
            .PreviewWidth.Should().BeApproximately(420, 2);
        ShowFontsTab(window, "TableOfContents");
        window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible).PreviewWidth
            .Should().BeApproximately(tocBefore, 2, "another page is not resized with it");

        // The width of the old settings (one for all the pages) is taken by the pages that have no width of their own.
        window.SplitterSizes = new System.Collections.Generic.Dictionary<string, int> { ["FontsPreview"] = 380 };
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible).PreviewWidth
            .Should().BeApproximately(380, 2);
        ShowFontsTab(window, "Checkpoints");
        window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible).PreviewWidth
            .Should().BeApproximately(420, 2, "this page keeps its own");
        window.Close();
    }

    /// <summary>A section without settings of its own lists links to its subsections; a link opens the subsection.</summary>
    [AvaloniaFact]
    public void A_section_without_settings_links_to_its_subsections()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "AppearancePage");
        PreferencesSectionNode appearance = window.Sections.Roots.Single(n => n.Title == Strings.Get("PreferencesWindow_AppearanceAndBehavior"));

        HyperlinkButton[] links = window.GetVisualDescendants().OfType<HyperlinkButton>().Where(l => l.IsEffectivelyVisible).ToArray();
        links.Select(l => l.Content).Should().Equal(appearance.Children.Select(n => n.Title));

        links.Single(l => Equals(l.Content, Strings.Get("PreferencesWindow_CodeEditor"))).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        window.Sections.Selected!.Path.Should().Be(
            Strings.Get("PreferencesWindow_AppearanceAndBehavior") + " › " + Strings.Get("PreferencesWindow_CodeEditor"));
        window.Close();
    }

    /// <summary>
    /// The search keeps the sections whose page has the text (not only their names) and frames the matching
    /// controls of the shown page, as Rider does.
    /// </summary>
    [AvaloniaFact]
    public void Searching_filters_the_sections_by_page_text_and_frames_the_matches()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        string label = Strings.Get("PreferencesWindow_EditionPageAddToToc");

        window.FindControl<TextBox>("SectionSearch")!.Text = label;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        window.Sections.Roots.Where(n => n.IsVisible).Select(n => n.Title)
            .Should().Equal(Strings.Get("PreferencesWindow_EditionPage"));
        window.Sections.Selected!.Title.Should().Be(Strings.Get("PreferencesWindow_EditionPage"));
        CheckBox match = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, label));
        Control frame = Avalonia.Controls.Primitives.AdornerLayer.GetAdorner(match)!;
        frame.Should().NotBeNull("the matching control is framed");
        // Regression: the adorner layer clips an adorner to its control by default, which hid the whole frame.
        Avalonia.Controls.Primitives.AdornerLayer.GetIsClipEnabled(frame).Should().BeFalse();
        Avalonia.Controls.Primitives.AdornerLayer.GetAdornerLayer(match)!.Children.Should().Contain(frame, "the frame is shown");
        ((Border)frame).BorderBrush.Should().NotBeNull();

        window.FindControl<TextBox>("SectionSearch")!.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        window.Sections.Roots.Should().OnlyContain(n => n.IsVisible);
        Avalonia.Controls.Primitives.AdornerLayer.GetAdorner(match).Should().BeNull();
        window.Close();
    }

    // The settings on the General tab of the Footnotes page.
    private static readonly string[] FootnotesGeneralControls =
        { "ConfirmFootnoteRemovalBox", "FootnoteGroupByChapterBox", "FootnoteLetterStyleBox" };

    /// <summary>
    /// The Footnotes page has two tabs: General first (the behaviour: the removal confirmation, the grouping and the
    /// letters of shared notes, with its hint), then CSS (the definitions of the footnote classes).
    /// </summary>
    [AvaloniaFact]
    public void The_footnotes_page_has_a_general_tab_and_a_css_tab()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        TabControl footnotes = ShowPage(window, "FootnotesPage").SelectedContent.Should().BeOfType<TabControl>().Subject;

        footnotes.Items.OfType<TabItem>().Select(t => t.Name).Should().Equal("FootnotesGeneralTab", "FootnotesCssTab");
        footnotes.SelectedIndex.Should().Be(0, "General is shown first");
        TabItem general = (TabItem)footnotes.Items[0]!;
        TabItem css = (TabItem)footnotes.Items[1]!;
        general.Header.Should().Be(Strings.Get("PreferencesWindow_FootnotesGeneralTab"));
        css.Header.Should().Be(Strings.Get("PreferencesWindow_FootnotesCssTab"));

        Control[] inGeneral = general.GetLogicalDescendants().OfType<Control>().ToArray();
        inGeneral.Select(c => c.Name).Should().Contain(FootnotesGeneralControls);
        inGeneral.OfType<TextBlock>().Select(t => t.Text).Should().Contain(Strings.Get("PreferencesWindow_FootnoteLetterStyleHint"));
        css.GetLogicalDescendants().OfType<CssEditor>().Should().NotBeEmpty();
        inGeneral.OfType<CssEditor>().Should().BeEmpty("the definitions are on the CSS tab");

        ComboBox letters = inGeneral.OfType<ComboBox>().Single(c => c.Name == "FootnoteLetterStyleBox");
        letters.SelectedItem.Should().Be(vm.FootnoteLetterStyle);
        letters.SelectedItem = FootnoteLetterStyle.UpperLatin;
        vm.FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.UpperLatin);
        window.Close();
    }

    /// <summary>
    /// Footnote styles: an invalid definition shows its problem next to the label; the fields have a fixed height with a
    /// grip in the corner that changes it, and the heights can be remembered and restored.
    /// </summary>
    [AvaloniaFact]
    public void Footnote_style_fields_show_their_error_and_resize_with_the_grip()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        TabControl footnotes = (TabControl)ShowPage(window, "FootnotesPage").SelectedContent!;
        footnotes.SelectedItem = footnotes.Items.OfType<TabItem>().Single(t => t.Name == "FootnotesCssTab");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        TextBlock error = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FootnoteNoteStyleError");
        error.IsVisible.Should().BeFalse();
        vm.FootnoteNoteStyle = "fnot-size: 1em;";
        Dispatcher.UIThread.RunJobs();
        error.IsVisible.Should().BeTrue();
        error.Text.Should().Be(vm.FootnoteNoteStyleError).And.NotBeEmpty();

        CssEditor box = window.GetVisualDescendants().OfType<CssEditor>().Single(t => t.Name == "FootnoteNoteStyleBox");
        box.Height.Should().Be(PreferencesWindow.DefaultFieldHeight);
        HeightResizeGrip grip = window.GetVisualDescendants().OfType<HeightResizeGrip>().Single(g => g.Target == box);
        grip.ResizeBy(40);
        box.Height.Should().Be(PreferencesWindow.DefaultFieldHeight + 40);
        grip.ResizeBy(-1000);
        box.Height.Should().Be(grip.MinTargetHeight, "not below the minimum");

        grip.ResizeBy(60);
        window.FieldHeights.Should().Contain("FootnoteNoteStyleBox", (int)box.Height);
        window.FieldHeights = new System.Collections.Generic.Dictionary<string, int> { ["FootnoteNoteStyleBox"] = 200 };
        box.Height.Should().Be(200);
        window.Close();
    }

    /// <summary>
    /// Regression: the page must have its own background and border, and the section tree panel a dimmed background.
    /// The test also guards against a rename of a template part in Avalonia: if PART_SelectedContentHost disappeared,
    /// the style would silently stop applying.
    /// </summary>
    [AvaloniaFact]
    public void Section_tree_and_page_are_visually_separated()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Border panel = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("sectionPanel"));
        panel.Background.Should().NotBeNull("the section tree must differ from the page");

        ContentPresenter content = window.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(c => c.Name == "PART_SelectedContentHost");

        content.BorderThickness.Should().Be(new Avalonia.Thickness(1));
        content.BorderBrush.Should().NotBeNull();
        content.Background.Should().NotBeNull();
        content.Background.Should().NotBeSameAs(panel.Background, "the page has a different background than the tree");
    }

    /// <summary>
    /// The "Keyboard Shortcuts" tab is the keymap: a tree of the actions with their shortcuts, filtered by the search
    /// box, and "Find Shortcut" records a pressed key combination as the shortcut filter.
    /// </summary>
    [AvaloniaFact]
    public void Keymap_tab_shows_the_action_tree_and_finds_a_pressed_shortcut()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_KeyboardShortcuts");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        KeymapView keymap = window.GetVisualDescendants().OfType<KeymapView>().Single();
        window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == vm.Keymap.Roots[0].Name);

        ShortcutCaptureBox find = keymap.FindControl<ShortcutCaptureBox>("FindFirstStroke")!;
        find.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Control });
        Dispatcher.UIThread.RunJobs();

        vm.Keymap.ShortcutFilter.Should().Be(new KeyStrokeShortcut(new KeyGesture(Key.S, KeyModifiers.Control)));
        vm.Keymap.ActionNodes.Single(n => n.ActionId == AppActionIds.Save).IsVisible.Should().BeTrue();
        vm.Keymap.ActionNodes.Single(n => n.ActionId == AppActionIds.Open).IsVisible.Should().BeFalse();
    }

    /// <summary>
    /// "Find Shortcut" as in IntelliJ: a mouse shortcut made in the pad shows up in the first field, which is then
    /// cleared by its button; typing in the search box drops the shortcut filter and empties the popup's fields.
    /// </summary>
    [AvaloniaFact]
    public void Keymap_find_shortcut_field_shows_a_mouse_shortcut_and_is_cleared_by_its_button_or_by_typing()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_KeyboardShortcuts");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        KeymapView keymap = window.GetVisualDescendants().OfType<KeymapView>().Single();
        ShortcutCaptureBox find = keymap.FindControl<ShortcutCaptureBox>("FindFirstStroke")!;
        Button clear = keymap.FindControl<Button>("FindFirstStrokeClear")!;
        MouseShortcutPad pad = keymap.FindControl<MouseShortcutPad>("FindMousePad")!;
        clear.IsVisible.Should().BeFalse();

        MouseShortcut ctrlClick = new(MouseShortcutButton.Left, KeyModifiers.Control);
        pad.SetShortcut(ctrlClick);
        Dispatcher.UIThread.RunJobs();

        vm.Keymap.ShortcutFilter.Should().Be(ctrlClick);
        find.Text.Should().Be(ctrlClick.DisplayText);
        pad.Shortcut.Should().BeNull();
        clear.IsVisible.Should().BeTrue();

        clear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        vm.Keymap.ShortcutFilter.Should().BeNull();
        find.Text.Should().BeNullOrEmpty();
        clear.IsVisible.Should().BeFalse();

        find.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Control });
        Dispatcher.UIThread.RunJobs();
        vm.Keymap.HasShortcutFilter.Should().BeTrue();

        vm.Keymap.Filter = vm.Keymap.Roots[0].Name;
        Dispatcher.UIThread.RunJobs();

        vm.Keymap.HasShortcutFilter.Should().BeFalse();
        find.Gesture.Should().BeNull();
        find.Text.Should().BeNullOrEmpty();
        clear.IsVisible.Should().BeFalse();
    }

    /// <summary>
    /// The "Spellcheck" tab: dictionaries with language names, the list of user
    /// dictionaries with a toggle, the selected dictionary as the default, the list of its words with actions
    /// available after selecting a word, and a button that opens the settings folder.
    /// </summary>
    [AvaloniaFact]
    public void Spellcheck_tab_manages_user_dictionaries_and_their_words()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_Spellcheck");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ComboBox primary = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PrimaryDictionaryBox");
        primary.SelectedItem.Should().Be(new DictionaryOption("en_US", SpellChecker.DisplayName("en_US")));
        primary.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain(SpellChecker.DisplayName("en_US"));

        ListBox dictionaries = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "UserDictionaryList");
        dictionaries.SelectedItem.Should().Be(vm.SelectedUserDictionary);
        dictionaries.GetVisualDescendants().OfType<CheckBox>().Should().ContainSingle(c => c.IsChecked == true);
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DefaultUserDictionaryText")
            .Text.Should().Be(vm.DefaultUserDictionaryText);
        window.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Name == "OpenPreferencesLocationButton");

        TabControl inner = window.GetVisualDescendants().OfType<TabControl>().Single(t => t != tabs);
        vm.SelectedUserDictionary!.Name.Should().Be(SpellChecker.DefaultUserDictionaryName);
        inner.SelectedIndex = 1; // the word list
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Button remove = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveUserWordsButton");
        remove.IsEffectivelyEnabled.Should().BeFalse("without a selected word there is nothing to remove");
    }

    /// <summary>
    /// The font picker window: every name in the list is rendered in its own font,
    /// and the current font is selected.
    /// </summary>
    [AvaloniaFact]
    public void Font_picker_renders_every_font_name_in_its_own_font()
    {
        FontEntry[] fonts = { new("Arial", false), new("Consolas", true), new("Georgia", false) };
        FontPickerWindow window = new()
        {
            DataContext = new FontPickerViewModel(fonts, new FontPickRequest("Georgia", 12, false)),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ListBox list = window.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedItem.Should().Be(fonts[2]);
        TextBlock[] names = list.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => fonts.Any(f => f.Name == t.Text))
            .ToArray();
        names.Should().HaveCount(fonts.Length);
        names.Should().OnlyContain(t => t.FontFamily.Name == t.Text);
    }

    private static readonly int[] ArialWeights = { 300, 400, 700 };

    private static readonly int[] RegularOnly = { 400 };

    /// <summary>
    /// The font picker window lists the weights of the selected font under the list of fonts and shows the sample in the
    /// weight chosen; a request without a weight has no such list.
    /// </summary>
    [AvaloniaFact]
    public void Font_picker_lists_the_weights_of_the_font_and_previews_the_chosen_one()
    {
        FontEntry[] fonts = { new("Arial", false), new("Georgia", false) };
        FontPickerWindow window = new()
        {
            DataContext = new FontPickerViewModel(
                fonts, new FontPickRequest("Arial", 12, false, 300), family => family == "Arial" ? ArialWeights : RegularOnly),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        ComboBox weights = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "WeightBox");
        TextBlock sample = window.FindControl<TextBlock>("SampleText")!;

        weights.IsEffectivelyVisible.Should().BeTrue();
        weights.Items.OfType<FontWeightOption>().Select(w => w.Name).Should().Equal(FontWeightOptions.Default.Name, "Light", "Regular", "Bold");
        ((FontWeightOption)weights.SelectedItem!).Value.Should().Be(300);
        sample.FontWeight.Should().Be(FontWeight.Light);

        weights.SelectedItem = weights.Items.OfType<FontWeightOption>().Single(w => w.Value == 700);
        Dispatcher.UIThread.RunJobs();

        sample.FontWeight.Should().Be(FontWeight.Bold);
        ((FontPickerViewModel)window.DataContext!).Result()!.Weight.Should().Be(700);
        window.Close();

        FontPickerWindow withoutWeight = new() { DataContext = new FontPickerViewModel(fonts, new FontPickRequest("Arial", 12, false)) };
        withoutWeight.Show();
        Dispatcher.UIThread.RunJobs();
        withoutWeight.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "WeightBox").IsEffectivelyVisible.Should().BeFalse();
        withoutWeight.Close();
    }

    /// <summary>
    /// The choice of the font picker window ("OK") reaches the row: the typeface, the size and the weight are shown, and the
    /// weight list does not write the weight it had back over the one just chosen.
    /// </summary>
    [AvaloniaFact]
    public void Choosing_a_font_with_a_weight_in_the_picker_shows_the_weight_in_the_row()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "FindUsagesPage");
        TabControl tabs = window.FindControl<TabControl>("FindUsagesTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        vm.FontPicker = _ => Task.FromResult<FontPickResult?>(new FontPickResult("Consolas", 17, 350));
        FontSettingViewModel row = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesContext");
        TextBlock summary = window.FindControl<FontsPageView>("FindUsagesFontsView")!.RowOf(row)!.GetVisualDescendants()
            .OfType<TextBlock>().Single(c => c.Name == "FontSummary");

        row.ChooseCommand!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        vm.FindUsagesContextFontFamily.Should().Be("Consolas");
        vm.FindUsagesContextFontSize.Should().Be(17);
        vm.FindUsagesContextFontWeight.Should().Be(350);
        summary.Text.Should().Be("Consolas, 17, " + FontWeightScale.NameOf(350), "the row shows the font chosen");
        window.Close();
    }

    /// <summary>
    /// A font setting is one line (typeface, size, weight) with "Choose…" and no lists or size fields; the picker shows its
    /// changes in the row (and the preview) while it is open, and Cancel puts the font back.
    /// </summary>
    [AvaloniaFact]
    public void A_font_row_is_one_line_that_follows_the_picker_and_Cancel_puts_it_back()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "FindUsagesPage");
        TabControl tabs = window.FindControl<TabControl>("FindUsagesTabs")!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, Strings.Get("PreferencesWindow_Fonts")));
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        FontsPageView page = window.FindControl<FontsPageView>("FindUsagesFontsView")!;
        FontSettingViewModel row = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesContext");
        TextBlock summary = page.RowOf(row)!.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "FontSummary");

        FontSettingView[] rows = page.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.SelectMany(r => r.GetVisualDescendants().OfType<ComboBox>()).Should().BeEmpty("the picker is the only way to change a font");
        rows.SelectMany(r => r.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.IsEffectivelyVisible)).Should().BeEmpty();
        vm.FindUsagesContextFontFamily = "Arial";
        vm.FindUsagesContextFontSize = 12;
        Dispatcher.UIThread.RunJobs();
        summary.Text.Should().Be("Arial, 12");

        string? whileOpen = null;
        vm.FontPicker = request =>
        {
            request.Changed!(new FontPickResult("Consolas", 18, 700));
            Dispatcher.UIThread.RunJobs();
            whileOpen = summary.Text;
            return Task.FromResult<FontPickResult?>(null);
        };

        row.ChooseCommand!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        whileOpen.Should().Be("Consolas, 18, " + FontWeightScale.NameOf(700), "the row follows the window");
        summary.Text.Should().Be("Arial, 12", "Cancel puts the font back");
        window.Close();
    }

    /// <summary>
    /// "Label + field" grids: a wide control (a combo box) sits after a label column of a fixed width and takes the rest of
    /// the settings (the user sets their width with the splitter); a narrow input (a number) sits at the right edge of its box and its label does not wrap while the box
    /// is wide — previously long Polish texts hid under the fields, then the fields floated in the middle of the box.
    /// </summary>
    [AvaloniaFact]
    public void Labeled_grids_keep_the_fields_next_to_the_label_or_at_the_edge_of_the_box()
    {
        PreferencesWindow window = BuildWindow();
        window.Width = 1900;
        window.Height = 860;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_General");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Grid grid = window.GetVisualDescendants().OfType<Grid>().First(g =>
            g.Classes.Contains("labeled") && g.IsEffectivelyVisible && g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[0].Width.IsAbsolute);
        double label = grid.ColumnDefinitions[0].ActualWidth;
        double control = grid.ColumnDefinitions[1].ActualWidth;

        label.Should().BeApproximately(268, 1, "a label column of a fixed width (260 and the column spacing)");
        control.Should().BeGreaterThan(150, "the control takes the rest of the settings");
        grid.Children.OfType<TextBlock>().Should().OnlyContain(t => t.TextWrapping == TextWrapping.Wrap);

        ShowPage(window, "StartScreenAppearancePage");
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        NumericUpDown box = window.FindControl<NumericUpDown>("StartScreenRecentFilesLimitBox")!;
        Grid row = (Grid)box.Parent!;
        TextBlock text = row.Children.OfType<TextBlock>().Single();

        (row.Bounds.Width - (box.Bounds.Right)).Should().BeLessThan(2, "the field sits at the right edge of the box");
        text.Bounds.Height.Should().BeLessThan(text.FontSize * 2, "the label of a wide box is on one line");
    }

    /// <summary>
    /// The "Mend &amp; Prettify" tab: separate boxes for Prettify, Mend and the warnings; the
    /// DOCTYPE checkboxes reflect and change the view model (adding is off, warning is on by default).
    /// </summary>
    [AvaloniaFact]
    public void Mend_prettify_tab_has_separate_doctype_options()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_MendPrettify");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ((TabItem)tabs.SelectedItem!).Header.Should().Be(Strings.Get("PreferencesWindow_MendPrettify"));
        window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("section") && b.IsEffectivelyVisible)
            .Should().Be(3);
        CheckBox prettify = CheckBoxWith(window, "PreferencesWindow_PrettifyAddMissingDoctype", 0);
        CheckBox mend = CheckBoxWith(window, "PreferencesWindow_MendAddMissingDoctype", 1);
        CheckBox warn = CheckBoxWith(window, "PreferencesWindow_WarnMissingDoctype", 0);
        (prettify.IsChecked, mend.IsChecked, warn.IsChecked).Should().Be((false, false, true));

        mend.IsChecked = true;
        warn.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        (vm.PrettifyAddMissingDoctype, vm.MendAddMissingDoctype, vm.WarnMissingDoctype).Should().Be((false, true, false));
    }

    // The Prettify and Mend checkboxes share the caption text, hence the index among equal captions.
    private static CheckBox CheckBoxWith(Window window, string key, int index) =>
        window.GetVisualDescendants().OfType<CheckBox>()
            .Where(c => c.IsEffectivelyVisible && c.Content as string == Strings.Get(key))
            .ElementAt(index);

    /// <summary>
    /// A "Fonts" page that has no preview panel of its own (here Appearance › General): a row per font setting, each with the
    /// preview of its font under it (one or two lines).
    /// </summary>
    [AvaloniaFact]
    public void Each_font_row_has_a_preview_of_its_own_under_the_font()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowGeneralFontsTab(window);

        FontsPageView page = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible);
        FontSettingView[] rows = page.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.Select(r => r.Name).Take(2).Should().Equal("FontInterface", "FontWarnings");
        TextBlock preview = rows[1].FindControl<TextBlock>("PreviewText")!;
        rows[1].FindControl<ScrollViewer>("PlainPreview")!.IsVisible.Should().BeTrue();
        rows[1].FindControl<CodePreviewEditor>("CodePreview")!.IsVisible.Should().BeFalse("only a font of the editor is previewed as code");
        Panel area = rows[1].FindControl<Panel>("PreviewArea")!;
        window.CaptureRenderedFrame();
        area.TranslatePoint(default, rows[1])!.Value.Y.Should().BeGreaterThan(
            rows[1].FindControl<TextBlock>("FontSummary")?.TranslatePoint(default, rows[1])?.Y ?? 0, "the preview is under the font");

        vm.WarningFontSize = 21;
        Dispatcher.UIThread.RunJobs();

        preview.FontSize.Should().Be(21);
        window.Close();
    }

    /// <summary>The preview is two lines tall to start with (of its font), not the whole page, and grows with the font.</summary>
    [AvaloniaFact]
    public void A_font_preview_is_two_lines_tall_to_start_with_and_the_grip_makes_it_taller_and_is_remembered()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowGeneralFontsTab(window);
        FontsPageView page = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible);
        FontSettingView warnings = page.GetVisualDescendants().OfType<FontSettingView>().Single(r => r.Name == "FontWarnings");
        Panel area = warnings.FindControl<Panel>("PreviewArea")!;
        window.CaptureRenderedFrame();
        double start = area.Bounds.Height;

        start.Should().BeInRange(20, 90, "a line or two, not a pane");
        warnings.UserPreviewHeight.Should().BeNull();

        vm.WarningFontSize = 40;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        area.Bounds.Height.Should().BeGreaterThan(start, "the default height follows the font");

        warnings.FindControl<HeightResizeGrip>("PreviewGrip")!.ResizeBy(60);
        Dispatcher.UIThread.RunJobs();

        warnings.UserPreviewHeight.Should().NotBeNull();
        window.SplitterSizes.Should().ContainKey("AppearanceFontsView.Warnings.previewHeight");

        // What the window remembers is given back to a window opened later.
        int remembered = window.SplitterSizes["AppearanceFontsView.Warnings.previewHeight"];
        PreferencesWindow again = BuildWindow();
        again.SplitterSizes = new System.Collections.Generic.Dictionary<string, int> { ["AppearanceFontsView.Warnings.previewHeight"] = remembered };
        again.Show();
        Dispatcher.UIThread.RunJobs();
        ShowGeneralFontsTab(again);
        again.GetVisualDescendants().OfType<FontSettingView>().Single(r => r.Name == "FontWarnings" && r.IsEffectivelyVisible)
            .UserPreviewHeight.Should().Be(remembered);
        again.Close();
        window.Close();
    }

    /// <summary>"Wrap lines" under a preview wraps the text sample, of that row only.</summary>
    [AvaloniaFact]
    public void Wrapping_the_lines_of_a_preview_is_a_choice_of_its_row()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowGeneralFontsTab(window);
        FontsPageView page = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible);
        FontSettingView[] rows = page.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        TextBlock first = rows[0].FindControl<TextBlock>("PreviewText")!;
        TextBlock second = rows[1].FindControl<TextBlock>("PreviewText")!;
        first.TextWrapping.Should().Be(TextWrapping.NoWrap);
        second.TextWrapping.Should().Be(TextWrapping.NoWrap);

        rows[0].FindControl<CheckBox>("WrapPreviewBox")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        ((FontSettingViewModel)rows[0].DataContext!).WrapPreview.Should().BeTrue();
        first.TextWrapping.Should().Be(TextWrapping.Wrap);
        second.TextWrapping.Should().Be(TextWrapping.NoWrap, "another row is not wrapped with it");
        window.Close();
    }

    /// <summary>
    /// The search finds the font settings of a page that was not shown yet (its rows exist before), but not the sample
    /// texts of the previews.
    /// </summary>
    [AvaloniaFact]
    public void Searching_finds_the_font_settings_but_not_the_preview_samples()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TextBox search = window.FindControl<TextBox>("SectionSearch")!;

        search.Text = Strings.Get("PreferencesWindow_CodeEditorTextFont");
        Dispatcher.UIThread.RunJobs();

        window.Sections.Selected!.Path.Should().Be(string.Join(
            " › ", Strings.Get("PreferencesWindow_AppearanceAndBehavior"), Strings.Get("PreferencesWindow_Appearance"),
            Strings.Get("PreferencesWindow_CodeEditor")));
        window.FindControl<TabControl>("CodeEditorAppearanceTabs")!.SelectedItem.Should().BeAssignableTo<TabItem>()
            .Which.Header.Should().Be(Strings.Get("PreferencesWindow_Fonts"), "the hit is on the Fonts tab, which the search shows");

        search.Text = Strings.Get("FontPreview_SpecialCharacters").Split('\n')[0].Trim();
        Dispatcher.UIThread.RunJobs();

        window.Sections.All.Should().NotContain(n => n.IsVisible, "the sample of a preview is not a setting");
        window.Close();
    }

    /// <summary>
    /// The "Preview" section: the refresh delay on its page, the highlighting of the cursor position under Appearance
    /// (its color field in its own color) and the preview fonts under Appearance › Preview › Fonts.
    /// </summary>
    [AvaloniaFact]
    public void Preview_section_splits_refresh_highlight_and_fonts()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ShowPage(window, "PreviewPage");
        window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("section"))
            .Should().ContainSingle();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Get("PreferencesWindow_PreviewRefreshDelay"));

        ShowPage(window, "PreviewAppearancePage");
        window.GetVisualDescendants().OfType<TextBox>().Should().NotContain(t => t.Classes.Contains("colorValue"),
            "the color of the highlight is in the color scheme");
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Get("PreferencesWindow_ColorsInScheme"));

        ShowFontsTab(window, "Preview");
        window.GetVisualDescendants().OfType<FontSettingView>().Where(r => r.IsEffectivelyVisible).Select(r => r.Name)
            .Should().Equal("FontPreviewStandard", "FontPreviewSerif", "FontPreviewSansSerif", "FontPreviewSize", "FontTooltipPreview");
    }

    /// <summary>An invalid color value does not paint the field — the theme look returns.</summary>
    [AvaloniaFact]
    public void Invalid_color_value_leaves_the_theme_background()
    {
        ColorConverters.ToBrush.Convert("#zzz", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(Avalonia.AvaloniaProperty.UnsetValue);
    }

    /// <summary>Changing the UI font in Preferences takes effect immediately, without a restart.</summary>
    [AvaloniaFact]
    public void Ui_font_is_applied_live_and_reset_restores_the_default()
    {
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "uifont-" + Guid.NewGuid().ToString("N"));
        SettingsStore settings = new(Path.Combine(root, "settings.xml"));
        UiDensityManager manager = new(
            settings, new IconThemeManager(settings, NullLogger<IconThemeManager>.Instance), NullLogger<UiDensityManager>.Instance);
        TextBlock text = new() { Text = "Wycinki" };
        Window window = new() { Content = text };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        double before = text.FontSize;

        try
        {
            manager.ApplyUiFont("Tahoma", 17);
            Dispatcher.UIThread.RunJobs();

            text.FontSize.Should().Be(17);
            text.FontFamily.Name.Should().Be("Tahoma");
        }
        finally
        {
            manager.ApplyUiFont(string.Empty, 0);
            Dispatcher.UIThread.RunJobs();
        }

        text.FontSize.Should().Be(before);
    }
}
