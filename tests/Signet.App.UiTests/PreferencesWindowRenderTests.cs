using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Signet.Controls.TreeDataGrid;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Actions;
using Signet.App.CodeView;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Themes;
using Signet.Core.Spellcheck;
using Xunit;

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

    private static PreferencesWindow BuildWindow() => BuildWindow(out _);

    private static PreferencesWindow BuildWindow(out SettingsStore settings)
    {
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-ui-" + Guid.NewGuid().ToString("N"));
        settings = new(Path.Combine(root, "settings.json"));
        SpellChecker spellChecker = new(settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        LocalizationManager localization = new(settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(settings, NullLogger<IconThemeManager>.Instance);
        KeyboardShortcutManager shortcuts = new(settings);
        using StatusBarService statusBar = new();
        AppActionRegistry registry = new(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance);

        UiDensityManager uiDensity = new(settings, iconTheme, NullLogger<UiDensityManager>.Instance);
        ThemeManager themes = new(settings, new ThemeStore(Path.Combine(root, "themes")), NullLogger<ThemeManager>.Instance);
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
    /// Save (filled) is enabled only with changes, writes them and keeps the window open; Cancel (a frame) closes it
    /// without the later changes and without a question. There is no Apply button any more.
    /// </summary>
    [AvaloniaFact]
    public void Save_writes_the_changes_and_keeps_the_window_open_and_Cancel_drops_them()
    {
        PreferencesWindow window = BuildWindow(out SettingsStore settings);
        var vm = (PreferencesViewModel)window.DataContext!;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool before = settings.SpellCheck;
        Button save = window.FindControl<Button>("SaveButton")!;
        Button cancel = window.FindControl<Button>("CancelButton")!;
        window.FindControl<Button>("ApplyButton").Should().BeNull();
        save.Classes.Should().Contain("accent");
        cancel.Classes.Should().Contain("outline");
        save.IsEffectivelyEnabled.Should().BeFalse("nothing has changed yet");

        vm.SpellCheckEnabled = !before;
        Dispatcher.UIThread.RunJobs();
        save.IsEffectivelyEnabled.Should().BeTrue();
        save.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        settings.SpellCheck.Should().Be(!before);
        closed.Should().BeFalse("Save keeps the window open");
        save.IsEffectivelyEnabled.Should().BeFalse("everything is saved");

        vm.SpellCheckEnabled = before;
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        closed.Should().BeTrue();
        window.OwnedWindows.Should().BeEmpty("Cancel does not ask");
        settings.SpellCheck.Should().Be(!before, "Cancel drops the change made after Save");
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

    /// <summary>
    /// The sections that define a look have the subsections Appearance › Fonts; the path of a subsection is shown above
    /// its page.
    /// </summary>
    // The parts with fonts of their own, in the order of Appearance › Fonts.
    private static readonly string[] PartsWithFonts =
    {
        "StartScreen", "BookBrowser", "TableOfContents", "CodeView", "Preview", "FindReplace", "SpecialCharacters",
        "Checkpoints", "Validation", "MetadataEditor", "Cleanup", "Footnotes",
    };

    [AvaloniaFact]
    public void The_fonts_of_the_parts_are_subsections_of_appearance_fonts()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        string appearance = Strings.Get("PreferencesWindow_Appearance");
        string fonts = Strings.Get("PreferencesWindow_Fonts");
        PreferencesSectionNode appearanceFonts = window.Sections.All.Single(n => n.Path == appearance + " › " + fonts);

        // The parts follow the work on a book: from the start screen, through editing, to cleaning up.
        appearanceFonts.Children.Select(n => n.Title).Should().Equal(PartsWithFonts.Select(s => Strings.Get("PreferencesWindow_" + s)));
        window.Sections.All.Where(n => n.Title == fonts).Should().ContainSingle("a section has no Fonts page of its own");
        window.Sections.All.Should().NotContain(n => n.Title == Strings.Get("PreferencesWindow_StartScreen") && n.Children.Count > 0,
            "a part with only fonts is no section");

        PreferencesSectionNode codeViewFonts = appearanceFonts.Children.Single(n => n.Title == Strings.Get("PreferencesWindow_CodeView"));
        window.Sections.Selected = codeViewFonts;
        Dispatcher.UIThread.RunJobs();

        window.FindControl<TextBlock>("Breadcrumb")!.Text.Should().Be(string.Join(" › ", appearance, fonts, Strings.Get("PreferencesWindow_CodeView")));
        window.Close();
    }

    // The items of the gear menu of Appearance › Color Scheme.
    private static readonly string[] SchemeActionItems =
    {
        "DuplicateThemeItem", "RenameThemeItem", "DeleteThemeItem", "RestoreThemeItem", "ExportThemeItem", "ImportThemeItem",
    };

    /// <summary>
    /// Appearance › Color Scheme lists the themes with the one in use selected, offers the scheme actions in the gear
    /// menu and the settings of the theme as a whole.
    /// </summary>
    [AvaloniaFact]
    public void Color_scheme_page_lists_the_themes_with_their_actions()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ShowPage(window, "ColorSchemePage");

        window.Sections.Selected!.Path.Should().Be(
            Strings.Get("PreferencesWindow_Appearance") + " › " + Strings.Get("PreferencesWindow_ColorScheme"));
        ComboBox themes = window.FindControl<ComboBox>("ThemeBox")!;
        themes.ItemCount.Should().Be(2);
        ((ThemeOption)themes.SelectedItem!).Id.Should().Be(Signet.Core.Themes.ThemeStore.LightId);
        window.FindControl<ComboBox>("VariantBox")!.SelectedItem.Should().Be(vm.ColorScheme.Variant);
        SchemeActionItems.Should().OnlyContain(name => window.FindControl<MenuItem>(name) != null, "the gear menu has every scheme action");
        vm.ColorScheme.RenameCommand.CanExecute(null).Should().BeFalse("a bundled theme keeps its name");

        themes.SelectedItem = vm.ColorScheme.Themes.Single(t => t.Id == Signet.Core.Themes.ThemeStore.DarkId);
        Dispatcher.UIThread.RunJobs();

        vm.HasChanges.Should().BeTrue();
        ((VariantOption)window.FindControl<ComboBox>("VariantBox")!.SelectedItem!).Value.Should().Be(Signet.Core.Themes.InterfaceVariant.Dark);
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
        ColorSchemePageViewModel xhtml = vm.ColorScheme.PageFor(Signet.Core.Themes.ThemePage.Xhtml);
        xhtml.SelectKey(Signet.Core.Themes.ThemeKeys.XhtmlCss);

        ShowPage(window, "ThemeXhtmlPage");

        window.Sections.Selected!.Path.Should().Be(string.Join(" › ",
            Strings.Get("PreferencesWindow_Appearance"), Strings.Get("PreferencesWindow_ColorScheme"), Strings.Get("PreferencesWindow_ThemeXhtml")));
        ColorSchemeKeysView view = window.FindControl<ColorSchemeKeysView>("ThemeXhtmlView")!;
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
        ColorSchemePageViewModel xhtml = vm.ColorScheme.PageFor(ThemePage.Xhtml);
        ThemePreviewEditor preview = window.FindControl<ColorSchemeKeysView>("ThemeXhtmlView")!
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

    /// <summary>A section with more appearance settings than fonts links to its fonts at the top of its Appearance page.</summary>
    [AvaloniaTheory]
    [InlineData("CodeView")]
    [InlineData("Preview")]
    public void The_appearance_of_a_section_links_to_its_fonts_at_the_top(string section)
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        PreferencesSectionNode appearance = window.Sections.Roots.Single(n => n.Title == Strings.Get("PreferencesWindow_Appearance"));
        var source = (HierarchicalTreeDataGridSource<PreferencesSectionNode>)window.FindControl<TreeDataGrid>("SectionTree")!.Source!;
        int appearanceRow = window.Sections.Roots.IndexOf(appearance);
        source.Collapse(new IndexPath(appearanceRow));
        appearance.IsExpanded = false;
        ShowPage(window, section + "AppearancePage");
        HyperlinkButton link = window.FindControl<HyperlinkButton>(section + "FontsLink")!;
        ((StackPanel)link.Parent!).Children[0].Should().BeSameAs(link, "the link is above every other setting");

        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        PreferencesSectionNode target = window.Sections.Selected!;
        target.Path.Should().Be(string.Join(
            " › ", Strings.Get("PreferencesWindow_Appearance"), Strings.Get("PreferencesWindow_Fonts"), Strings.Get("PreferencesWindow_" + section)));
        appearance.IsExpanded.Should().BeTrue("the link opens the collapsed branch, as a click in the tree");
        target.Parent!.IsExpanded.Should().BeTrue();
        source.RowSelection!.SelectedItem.Should().BeSameAs(target, "the section is selected in the tree itself");
        window.Close();
    }

    /// <summary>A section without settings of its own lists links to its subsections; a link opens the subsection.</summary>
    [AvaloniaFact]
    public void A_section_without_settings_links_to_its_subsections()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "CodeViewPage");

        HyperlinkButton link = window.GetVisualDescendants().OfType<HyperlinkButton>().Should().ContainSingle().Subject;
        link.Content.Should().Be(Strings.Get("PreferencesWindow_Appearance"));

        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        window.Sections.Selected!.Path.Should().Be(
            Strings.Get("PreferencesWindow_CodeView") + " › " + Strings.Get("PreferencesWindow_Appearance"));
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
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_Footnotes");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        TextBlock error = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FootnoteNoteStyleError");
        error.IsVisible.Should().BeFalse();
        vm.FootnoteNoteStyle = "fnot-size: 1em;";
        Dispatcher.UIThread.RunJobs();
        error.IsVisible.Should().BeTrue();
        error.Text.Should().Be(vm.FootnoteNoteStyleError).And.NotBeEmpty();

        TextBox box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "FootnoteNoteStyleBox");
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

    /// <summary>
    /// The labels on the "General" tab (and the other "label + field" grids) get 2/3
    /// of the width and wrap — previously long Polish texts hid under the fields.
    /// </summary>
    [AvaloniaFact]
    public void Labeled_grids_give_two_thirds_to_the_wrapping_label()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        SelectTab(tabs, "PreferencesWindow_General");
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Grid grid = window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("labeled") && g.IsEffectivelyVisible);
        double label = grid.ColumnDefinitions[0].ActualWidth;
        double control = grid.ColumnDefinitions[1].ActualWidth;

        (label / (label + control)).Should().BeInRange(0.6, 0.7, "about 2/3 for the label");
        grid.Children.OfType<TextBlock>().Should().OnlyContain(t => t.TextWrapping == TextWrapping.Wrap);
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
    /// A "Fonts" page, as Rider's Editor › Font: a row per font setting (typeface list, size); the preview on the right
    /// shows the setting focused last in its font and size, with the colors of the place it is used in.
    /// </summary>
    [AvaloniaFact]
    public void Fonts_page_previews_the_setting_focused_last()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "CodeViewFontsPage");

        FontsPageView page = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible);
        FontSettingView[] rows = page.GetVisualDescendants().OfType<FontSettingView>().ToArray();
        rows.Select(r => r.Name).Should().Equal("FontCodeViewEditor", "FontOpenTagHint");
        CodePreviewEditor code = page.FindControl<CodePreviewEditor>("CodePreview")!;
        TextBlock preview = page.FindControl<TextBlock>("PreviewText")!;
        code.IsVisible.Should().BeTrue("the editor font is previewed as code");
        page.FindControl<ScrollViewer>("PlainPreview")!.IsVisible.Should().BeFalse();
        code.Text.Should().Be(Strings.Get("FontPreview_Code"));
        code.FontFamily.Name.Should().Be(vm.CodeView.FontFamily);
        code.FontSize.Should().Be(vm.CodeView.FontSize);
        code.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Should().ContainSingle("line numbers in the colors of the theme");
        code.TextArea.TextView.LineTransformers.Should().Contain(t => t.GetType().Name.StartsWith("LineStateSyntaxColorizer"),
            "the sample is highlighted as in Code View");

        rows[1].FindControl<ComboBox>("FamilyBox")!.Focus();
        Dispatcher.UIThread.RunJobs();

        vm.CodeViewFonts.Active!.Key.Should().Be("OpenTagHint");
        page.FindControl<ScrollViewer>("PlainPreview")!.IsVisible.Should().BeTrue();
        code.IsVisible.Should().BeFalse();
        preview.Text.Should().StartWith(Strings.Get("FontPreview_OpenTagHint"));
        page.FindControl<Border>("PreviewSurface")!.Background.Should().BeAssignableTo<ISolidColorBrush>()
            .Which.Color.Should().Be(AppTheme.Current.Background(ThemeKeys.HintsOpenTag));

        rows[1].FindControl<NumericUpDown>("SizeBox")!.Value = 21;
        Dispatcher.UIThread.RunJobs();

        vm.OpenTagHintFontSize.Should().Be(21);
        preview.FontSize.Should().Be(21);
        window.Close();
    }

    /// <summary>"Wrap lines" over the preview wraps both the code sample and a text sample.</summary>
    [AvaloniaFact]
    public void Fonts_page_wraps_the_preview_lines_on_request()
    {
        PreferencesWindow window = BuildWindow();
        PreferencesViewModel vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        ShowPage(window, "CodeViewFontsPage");
        FontsPageView page = window.GetVisualDescendants().OfType<FontsPageView>().Single(p => p.IsEffectivelyVisible);
        CodePreviewEditor code = page.FindControl<CodePreviewEditor>("CodePreview")!;
        TextBlock preview = page.FindControl<TextBlock>("PreviewText")!;
        code.WordWrap.Should().BeFalse();
        preview.TextWrapping.Should().Be(TextWrapping.NoWrap);

        page.FindControl<CheckBox>("WrapPreviewBox")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        vm.CodeViewFonts.WrapPreview.Should().BeTrue();
        code.WordWrap.Should().BeTrue();
        preview.TextWrapping.Should().Be(TextWrapping.Wrap);
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

        search.Text = Strings.Get("PreferencesWindow_CodeViewEditorFont");
        Dispatcher.UIThread.RunJobs();

        window.Sections.Selected!.Path.Should().Be(string.Join(
            " › ", Strings.Get("PreferencesWindow_Appearance"), Strings.Get("PreferencesWindow_Fonts"), Strings.Get("PreferencesWindow_CodeView")));

        search.Text = Strings.Get("FontPreview_SpecialCharacters").Split('\n')[0].Trim();
        Dispatcher.UIThread.RunJobs();

        window.Sections.All.Should().NotContain(n => n.IsVisible, "the sample of a preview is not a setting");
        window.Close();
    }

    /// <summary>
    /// The "Preview" section: the refresh delay on its page, the highlighting of the cursor position under Appearance
    /// (its color field in its own color) and the preview fonts under Appearance › Fonts.
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
            "the color of the highlight is in the theme");
        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Get("PreferencesWindow_ColorsInTheme"));

        ShowPage(window, "PreviewFontsPage");
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
        SettingsStore settings = new(Path.Combine(root, "settings.json"));
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
