using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the main window view model: the menu is built, toolbars are present, the dock
/// layout is created, panel actions work, and unimplemented actions report on the status bar.
/// </summary>
public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel New() => new();

    [Fact]
    public void WindowTitle_is_the_application_name()
    {
        New().WindowTitle.Should().Be(ApplicationInfo.Name);
    }

    [Fact]
    public void Open_action_routes_through_the_file_workflow()
    {
        MainWindowViewModel sut = New();
        FakeFileWorkflowPrompts prompts = new();
        sut.AttachFileWorkflowPrompts(prompts);

        sut.Actions.Require(AppActionIds.Open).Execute(null);

        prompts.AskOpenPathCalls.Should().Be(1);
    }

    [Fact]
    public void LoadBook_feeds_the_book_browser_and_updates_the_title()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();

        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);

        sut.BookBrowser.HasBook.Should().BeTrue();
        sut.BookBrowser.Nodes.Should().NotBeEmpty();
        sut.WindowTitle.Should().Contain(ApplicationInfo.Name).And.Contain(".epub");
    }

    [Fact]
    public void Opening_a_resource_from_the_book_browser_creates_a_document_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);

        BookBrowserNode text = sut.BookBrowser.Nodes.Single(n => n.Header == "Text");
        sut.BookBrowser.UpdateSelection(new object?[] { text.Children[0] });
        sut.BookBrowser.OpenSelectedCommand.Execute(null);

        sut.Tabs.Count.Should().Be(1);
        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(text.Children[0].Entry!.BookPath);
    }

    [Fact]
    public void Menu_has_all_top_level_entries_and_no_excluded_features()
    {
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = New();

        sut.MenuItems.Select(m => m.Header).Should().Equal(
            "_File", "_Edit", "_Insert", "For_mat", "_Search", "_Tools", "_View", "_Window", "_Help");

        static string Flatten(MenuItemViewModel item) =>
            item.Header + " " + string.Concat((item.Items ?? System.Array.Empty<MenuItemViewModel>()).Select(Flatten));

        string all = string.Concat(sut.MenuItems.Select(Flatten));
        all.Should().NotContainAny("Plugin", "Manage Repositories", "Automat", "Index Editor");
    }

    [Fact]
    public void Every_menu_leaf_is_bound_to_a_command()
    {
        MainWindowViewModel sut = New();

        static void Check(MenuItemViewModel item)
        {
            if (item.Items is { } children)
            {
                foreach (MenuItemViewModel child in children)
                {
                    Check(child);
                }
            }
            else if (!item.IsSeparator)
            {
                item.Command.Should().NotBeNull($"menu item \"{item.Header}\" should have a command");
            }
        }

        foreach (MenuItemViewModel top in sut.MenuItems)
        {
            Check(top);
        }
    }

    [Fact]
    public void Main_toolbars_are_exposed_in_rows()
    {
        MainWindowViewModel sut = New();

        sut.Toolbars.Should().HaveCount(ToolbarManager.AllToolbars.Count);
        sut.ToolbarRows.Should().HaveCount(ToolbarManager.Rows.Count);
        sut.ToolbarRows[0].HasVisibleToolbars.Should().BeTrue();
        sut.ToolbarRows[2].HasVisibleToolbars.Should().BeFalse("the Clip Bar is hidden by default");
    }

    [Fact]
    public void Dock_layout_contains_a_documents_area()
    {
        MainWindowViewModel sut = New();

        sut.Layout.Should().NotBeNull();
        sut.DockFactory.Should().NotBeNull();
    }

    [Fact]
    public void Documents_area_holds_its_space_and_does_not_collapse_when_empty()
    {
        MainWindowViewModel sut = New();
        MainDockFactory factory = (MainDockFactory)sut.DockFactory;

        // Without this, an empty tab area collapsed to zero and the bottom panel took over the whole center.
        factory.DocumentDock!.IsCollapsable.Should().BeFalse();
        factory.DocumentDock!.Proportion.Should().BeGreaterThan(0.5);

        // Validation Results starts hidden, so it does not dominate the center.
        factory.IsToolVisible(DockableIds.ValidationResults).Should().BeFalse();
    }

    [Fact]
    public void Panel_toggle_action_is_enabled_and_flips_visibility()
    {
        MainWindowViewModel sut = New();
        AppAction toggle = sut.Actions.Require(AppActionIds.ToggleBookBrowser);
        MainDockFactory factory = (MainDockFactory)sut.DockFactory;

        toggle.IsEnabled.Should().BeTrue();
        factory.IsToolVisible(DockableIds.BookBrowser).Should().BeTrue();

        toggle.Execute(null);
        factory.IsToolVisible(DockableIds.BookBrowser).Should().BeFalse();

        toggle.Execute(null);
        factory.IsToolVisible(DockableIds.BookBrowser).Should().BeTrue();
    }

    [Fact]
    public void Unimplemented_action_reports_on_the_status_bar()
    {
        // InsertRole (Insert -> Role...) is not wired up.
        MainWindowViewModel sut = New();
        AppAction insertRole = sut.Actions.Require(AppActionIds.InsertRole);
        insertRole.IsEnabled = true;

        insertRole.Execute(null);

        sut.StatusMessage.Should().Be(Strings.Format("Status_NotImplemented", insertRole.DefaultText.Replace("&", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public void AddCover_action_raises_AddCoverRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.AddCoverRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.AddCover).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void Preferences_action_raises_PreferencesRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.PreferencesRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.Preferences).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void Reports_action_raises_ReportsRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.ReportsRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.Reports).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void Cleanup_action_raises_CleanupRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.CleanupRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.Cleanup).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareCleanup_returns_null_without_a_book()
    {
        MainWindowViewModel sut = New();

        CleanupViewModel? cleanup = await sut.PrepareCleanupAsync();

        cleanup.Should().BeNull();
    }

    [Fact]
    public async Task PrepareCleanup_and_cleaning_the_CSS_tab_remove_an_unused_selector_and_report_it()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.ghost { color: red }\n");
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");

        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        CleanupViewModel? cleanup = await sut.PrepareCleanupAsync();
        cleanup.Should().NotBeNull();
        cleanup!.Sections.Single(s => s.Step == CleanupStep.UnusedSelectors).IsEnabled = true;
        CleanupTabViewModel cssTab = cleanup.Tabs[0];
        cssTab.HasChanges.Should().BeTrue();

        cssTab.CleanCommand.Execute(null);

        book.GetCssResources().Single().GetText().Should().NotContain(".ghost");
        book.Modified.Should().BeTrue();
        sut.StatusMessage.Should().Be(Strings.Get("Status_CleanupDone"));
        cssTab.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void ApplyAddCover_MarksImageAsCoverAndOpensTheCoverPage()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);

        ImageResource figure = (ImageResource)sut.GetCoverCandidateImages().Single(i => i.Filename == "figure.png");
        sut.FindExistingCoverHtml().Should().BeNull();

        sut.ApplyAddCover(figure);

        sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("cover.xhtml");
        sut.FindExistingCoverHtml().Should().NotBeNull();
    }

    [Fact]
    public void AddNavToSpine_and_RemoveNavFromSpine_actions_toggle_spine_membership()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        book.IsNavInSpine.Should().BeFalse();

        sut.Actions.Require(AppActionIds.AddNavToSpineNonLinear).Execute(null);
        book.IsNavInSpine.Should().BeTrue();

        sut.Actions.Require(AppActionIds.RemoveNavFromSpine).Execute(null);
        book.IsNavInSpine.Should().BeFalse();
    }

    [Fact]
    public void WellFormedCheckEpub_action_populates_ValidationResults_and_shows_the_panel()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);
        MainDockFactory factory = (MainDockFactory)sut.DockFactory;

        sut.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);

        sut.ValidationResults.HasResults.Should().BeTrue();
        sut.ValidationResults.Rows.Should().Contain(r => r.FileName == "chapter1.xhtml");
        factory.IsToolVisible(DockableIds.ValidationResults).Should().BeTrue();
    }

    [Fact]
    public void WellFormedCheckEpub_action_reports_no_results_for_a_wellformed_book()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);

        sut.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);

        sut.ValidationResults.HasResults.Should().BeFalse();
    }

    [Fact]
    public void ValidationResults_activation_opens_the_offending_resource_in_a_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);
        sut.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);

        ValidationResultRow row = sut.ValidationResults.Rows.First();
        sut.ValidationResults.Activate(row);

        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(row.Result.BookPath);
    }

    [Fact]
    public void ValidateStylesheetsWithW3C_opens_a_generated_page_per_stylesheet()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        List<string> openedPaths = new();
        sut.AttachExternalFileOpener(path => { openedPaths.Add(path); return true; });

        sut.Actions.Require(AppActionIds.ValidateStylesheetsWithW3C).Execute(null);

        openedPaths.Should().HaveCount(book.GetCssResources().Count);
        string html = File.ReadAllText(openedPaths[0]);
        html.Should().Contain(W3CValidation.ValidatorUrl);
    }

    [Fact]
    public void Print_action_delegates_to_PreviewViewModel_RequestPrint()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        bool raised = false;
        sut.Preview.PrintRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.Print).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void PrintPreview_action_generates_a_pdf_and_opens_it_via_the_external_file_opener()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        List<string> openedPaths = new();
        sut.AttachExternalFileOpener(path => { openedPaths.Add(path); return true; });
        string? requestedPath = null;
        sut.Preview.PrintToPdfRequested += (_, path) => requestedPath = path;

        sut.Actions.Require(AppActionIds.PrintPreview).Execute(null);

        // The view (PreviewView) generates the PDF through the WebView and only on success calls
        // PreviewViewModel.NotifyPrintPreviewReady; without a WebView the test simulates that step.
        requestedPath.Should().NotBeNullOrEmpty();
        sut.Preview.NotifyPrintPreviewReady(requestedPath!);
        openedPaths.Should().Equal(requestedPath);
    }

    [Fact]
    public void NcxGuideFromNav_generates_ncx_and_guide_entries_from_the_nav()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        book.GetNcx().Should().BeNull();

        sut.Actions.Require(AppActionIds.NcxGuideFromNav).Execute(null);

        NcxResource? ncx = book.GetNcx();
        ncx.Should().NotBeNull();
        ncx!.GetText().Should().Contain("chapter1.xhtml");

        HtmlResource chapter = book.GetHtmlResources().First(r => r.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
        book.GetOpf().GetGuideSemanticCodeForResource(chapter).Should().Be("text");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void RemoveNcxGuide_removes_the_ncx_resource_and_clears_the_guide()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        book.GetNcx().Should().NotBeNull();

        sut.Actions.Require(AppActionIds.RemoveNcxGuide).Execute(null);

        book.GetNcx().Should().BeNull();
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void UpdateManifestMediaTypes_and_UpdateManifestProperties_actions_mark_the_book_modified()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        sut.Actions.Require(AppActionIds.UpdateManifestMediaTypes).Execute(null);
        book.Modified.Should().BeTrue();

        book.Modified = false;
        sut.Actions.Require(AppActionIds.UpdateManifestProperties).Execute(null);
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void LoadBook_opens_the_first_html_file_in_spine_order()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();

        sut.LoadBook(book, epub);

        Resource firstInSpine = book.GetOpf().GetSpineOrderResources(book.GetFolderKeeper().GetResourceList())[0];
        sut.Tabs.ActiveTab.Should().NotBeNull();
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(firstInSpine);
    }

    [Fact]
    public void Blocked_action_shows_the_notifications_panel_collapsed_with_an_unread_warning()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);
        var factory = (MainDockFactory)sut.DockFactory;
        factory.IsToolVisible(DockableIds.Notifications).Should().BeFalse("the panel is hidden until needed");
        sut.HasUnreadWarnings.Should().BeFalse("the 'book loaded' message is only informational");

        // "Add Nav to Reading Order" is not available for EPUB 2 — the action is blocked.
        sut.Actions.Require(AppActionIds.AddNavToSpine).Execute(null);

        sut.Notifications.Entries[0].IsWarning.Should().BeTrue();
        sut.IsStatusMessageWarning.Should().BeTrue();
        factory.IsToolVisible(DockableIds.Notifications).Should().BeTrue();
        factory.IsToolPinned(DockableIds.Notifications).Should().BeTrue("the panel appears collapsed, not over the editor");
        sut.HasUnreadWarnings.Should().BeTrue();
        sut.UnreadWarningsText.Should().Be("1");

        sut.OpenNotificationsCommand.Execute(null);

        sut.HasUnreadWarnings.Should().BeFalse("opening the panel marks the warnings as read");
    }

    [Fact]
    public void Notifications_panel_action_toggles_the_panel()
    {
        MainWindowViewModel sut = New();
        var factory = (MainDockFactory)sut.DockFactory;

        sut.Actions.Require(AppActionIds.ToggleNotifications).Execute(null);
        factory.IsToolVisible(DockableIds.Notifications).Should().BeTrue();

        sut.Actions.Require(AppActionIds.ToggleNotifications).Execute(null);
        factory.IsToolVisible(DockableIds.Notifications).Should().BeFalse();
    }

    [Fact]
    public void Format_actions_are_enabled_only_for_an_html_code_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        AppAction bold = sut.Actions.Require(AppActionIds.Bold);
        sut.Tabs.CloseAllTabs(); // LoadBook opens the first HTML file; this test needs a state without tabs
        bold.IsEnabled.Should().BeFalse();

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        bold.IsEnabled.Should().BeTrue();

        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        sut.Tabs.OpenResources(new Resource[] { css });
        bold.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Preview_click_while_another_tab_is_active_switches_to_the_previewed_file()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        var htmlTab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        int? htmlJump = null;
        htmlTab.ScrollToOffsetRequested += offset => htmlJump = offset;

        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        sut.Tabs.OpenResources(new Resource[] { css });
        var cssTab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        bool cssJumped = false;
        cssTab.ScrollToOffsetRequested += _ => cssJumped = true;
        sut.Preview.CurrentBookPath.Should().Be(html.BookPath, "the preview stays on the last HTML file");

        sut.Preview.HandlePreviewMessage("signet-loc:5");

        sut.Tabs.ActiveTab.Should().BeSameAs(htmlTab, "the click refers to the previewed file, not the CSS tab");
        htmlJump.Should().Be(5);
        cssJumped.Should().BeFalse();
    }

    [Fact]
    public void Bold_action_wraps_the_active_code_tab_selection()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;

        tab.Document.Text = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body>\n" +
            "<p>alpha beta</p>\n</body>\n</html>\n";
        int start = tab.Document.Text.IndexOf("beta", System.StringComparison.Ordinal);
        tab.UpdateSelection(start, start + 4);

        sut.Actions.Require(AppActionIds.Bold).Execute(null);

        tab.Document.Text.Should().Contain("<p>alpha <b>beta</b></p>");
    }

    [Fact]
    public void Insert_actions_are_enabled_only_for_an_html_code_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        AppAction special = sut.Actions.Require(AppActionIds.InsertSpecialCharacter);
        sut.Tabs.CloseAllTabs(); // LoadBook opens the first HTML file; this test needs a state without tabs
        special.IsEnabled.Should().BeFalse();

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        special.IsEnabled.Should().BeTrue();

        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        sut.Tabs.OpenResources(new Resource[] { css });
        special.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void PasteClipboardHistory_action_is_enabled_only_with_an_open_tab_and_raises_the_request()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        AppAction paste = sut.Actions.Require(AppActionIds.PasteClipboardHistory);
        sut.Tabs.CloseAllTabs(); // LoadBook opens the first HTML file; this test needs a state without tabs
        paste.IsEnabled.Should().BeFalse();

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        paste.IsEnabled.Should().BeTrue();

        bool raised = false;
        sut.PasteClipboardHistoryRequested += (_, _) => raised = true;

        paste.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void PasteClipboardHistoryEntry_inserts_the_chosen_text_in_the_active_code_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        int caret = tab.Document.Text.IndexOf("<body>", System.StringComparison.Ordinal) + "<body>".Length;
        tab.UpdateSelection(caret, caret);

        sut.PasteClipboardHistoryEntry("PASTED");

        tab.Document.Text.Should().Contain("<body>PASTED");
    }

    [Fact]
    public void InsertClip_action_raises_InsertAriaClipRequested()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        bool raised = false;
        sut.InsertAriaClipRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.InsertClip).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void ApplyAriaClip_for_a_number_only_code_inserts_the_translated_template()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });

        sut.ApplyAriaClip("pagebreak_hr", roleCode: null);

        sut.ActiveCodeTab!.DocumentText.Should().Contain("epub:type=\"pagebreak\"");
    }

    [Fact]
    public void ClipEditor_action_raises_ClipEditorRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.ClipEditorRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.ClipEditor).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectClip_action_raises_SelectClipRequested_and_ApplySelectedClip_pastes_it()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        bool raised = false;
        sut.SelectClipRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.SelectClip).Execute(null);
        sut.ApplySelectedClip("<mark>library clip</mark>");

        raised.Should().BeTrue();
        sut.ActiveCodeTab!.DocumentText.Should().Contain("<mark>library clip</mark>");
    }

    [Fact]
    public void Adding_a_top_level_clip_refreshes_the_first_clip_bar_action()
    {
        MainWindowViewModel sut = New();
        AppAction clip1 = sut.Actions.Require(AppActionIds.Clip(1));
        clip1.IsEnabled.Should().BeFalse();

        sut.Clips.AddEntryCommand.Execute(null);
        sut.Clips.Nodes.Single().Name = "MyClip";

        clip1.IsEnabled.Should().BeTrue();
        clip1.Text.Should().Be("MyClip");
    }

    [Fact]
    public void ApplyInsertId_rejects_an_invalid_identifier_with_a_status_message()
    {
        MainWindowViewModel sut = New();

        sut.ApplyInsertId("1-bad-start");

        sut.StatusMessage.Should().Contain("ID");
    }

    [Fact]
    public void InsertMediaResources_inserts_an_img_tag_in_the_active_code_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { html });
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.Document.Text = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body>\n" +
            "<p>x</p>\n</body>\n</html>\n";
        int caret = tab.Document.Text.IndexOf("<p>x</p>", System.StringComparison.Ordinal) + "<p>x</p>".Length;
        tab.UpdateSelection(caret, caret);
        tab.UpdateCaret(1, 1, caret, '\n');

        ImageResource figure = book.GetAllResources().OfType<ImageResource>().First(i => i.Filename == "figure.png");
        sut.InsertMediaResources(new Resource[] { figure });

        tab.Document.Text.Should().Contain("<img alt=\"figure\" src=\"../images/figure.png\"/>");
    }

    [Fact]
    public void HeadingPreserveAttributes_action_toggles_and_shows_a_status_message()
    {
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = New();
        AppAction preserve = sut.Actions.Require(AppActionIds.HeadingPreserveAttributes);

        preserve.IsCheckable.Should().BeTrue();
        preserve.IsChecked.Should().BeFalse();

        preserve.IsEnabled = true;
        preserve.Execute(null);

        preserve.IsChecked.Should().BeTrue();
        sut.StatusMessage.Should().Contain("ON");
    }

    [Fact]
    public void MendHtml_action_fixes_malformed_html_and_reports_on_status_bar()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        HtmlResource chapter = book.GetAllResources().OfType<HtmlResource>().Single(r => r.Filename == "chapter1.xhtml");

        sut.Actions.Require(AppActionIds.MendHtml).Execute(null);

        WellFormedChecker.CheckXhtmlStructure(chapter.GetText(), chapter.EpubVersion).IsWellFormed.Should().BeTrue();
        sut.StatusMessage.Should().Contain("Mend All");
    }

    [Fact]
    public void MendPrettifyHtml_action_cancels_when_html_not_well_formed()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        sut.Actions.Require(AppActionIds.MendPrettifyHtml).Execute(null);

        sut.StatusMessage.Should().Contain("well-formed");
    }

    [Fact]
    public void StandardizeEpub_action_raises_StandardizeEpubRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.StandardizeEpubRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.StandardizeEpub).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyStandardizeEpub_moves_every_resource_to_its_standard_folder()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        await sut.ApplyStandardizeEpubAsync();

        book.GetOpf().BookPath.Should().Be("OEBPS/content.opf");
        book.GetAllResources().OfType<HtmlResource>().Should().OnlyContain(r => r.BookPath.StartsWith("OEBPS/Text/", System.StringComparison.Ordinal));
        sut.StatusMessage.Should().Contain("Restructuring");
    }

    [Fact]
    public void UseStandardFileExtensions_action_renames_mismatched_extension_and_reports_on_status_bar()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.EdgeDeepFolders, tree);
        string oldCssPath = Path.Combine(tree, "content", "resources", "styles", "main.css");
        string newCssPath = Path.Combine(tree, "content", "resources", "styles", "main.txt");
        File.Move(oldCssPath, newCssPath);
        string opfPath = Path.Combine(tree, "content", "book.opf");
        File.WriteAllText(
            opfPath,
            File.ReadAllText(opfPath).Replace("resources/styles/main.css", "resources/styles/main.txt"));
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");

        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        sut.Actions.Require(AppActionIds.UseStandardFileExtensions).Execute(null);

        book.GetCssResources().Single().Filename.Should().Be("main.css");
        sut.StatusMessage.Should().Contain("Bulk rename");
    }

    [Fact]
    public void RebaseManifestIds_action_renumbers_ids_and_reports_on_status_bar()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        sut.Actions.Require(AppActionIds.RebaseManifestIds).Execute(null);

        book.Modified.Should().BeTrue();
        sut.StatusMessage.Should().Contain("rebased");
    }

    // ---------------------------------------------------------- Spellcheck --- //

    private static Signet.App.ViewModels.Tabs.CodeTabViewModel OpenHtmlTabWithMisspelling(MainWindowViewModel sut, Book book)
    {
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.Document.Text = "<html><body><p>hello wrold</p></body></html>";
        return tab;
    }

    [Fact]
    public void WordWrap_is_on_by_default_and_the_View_action_toggles_open_code_tabs()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        AppAction toggle = sut.Actions.Require(AppActionIds.WordWrap);

        toggle.IsCheckable.Should().BeTrue();
        toggle.IsChecked.Should().BeTrue();
        tab.WordWrap.Should().BeTrue("Code View wraps lines by default");

        toggle.Execute(null);

        toggle.IsChecked.Should().BeFalse();
        tab.WordWrap.Should().BeFalse();

        toggle.Execute(null);

        tab.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void AutoSpellCheck_action_toggles_setting_and_refreshes_open_tabs()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        AppAction toggle = sut.Actions.Require(AppActionIds.AutoSpellCheck);
        toggle.IsChecked.Should().BeFalse();
        tab.MisspelledWordHighlights.Should().BeEmpty();

        toggle.Execute(null);

        toggle.IsChecked.Should().BeTrue();
        tab.MisspelledWordHighlights.Should().ContainSingle();

        toggle.Execute(null);

        toggle.IsChecked.Should().BeFalse();
        tab.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void Spellcheck_actions_are_enabled_only_for_an_html_code_tab_with_spellcheck_on()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);

        AppAction next = sut.Actions.Require(AppActionIds.Spellcheck);
        next.IsEnabled.Should().BeFalse();

        OpenHtmlTabWithMisspelling(sut, book);
        next.IsEnabled.Should().BeFalse("spellcheck is still disabled");

        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        next.IsEnabled.Should().BeTrue();

        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        sut.Tabs.OpenResources(new Resource[] { css });
        next.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Spellcheck_action_jumps_to_the_next_misspelled_word()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        tab.UpdateCaret(1, 1, 0, -1);

        (int Start, int End, bool Wrapped)? found = null;
        tab.SearchResultRequested += (start, end, wrapped) => found = (start, end, wrapped);

        sut.Actions.Require(AppActionIds.Spellcheck).Execute(null);

        found.Should().NotBeNull();
        tab.Document.GetText(found!.Value.Start, found.Value.End - found.Value.Start).Should().Be("wrold");
    }

    [Fact]
    public void AddMisspelledWord_and_IgnoreMisspelledWord_actions_delegate_to_the_active_code_tab()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        int wordStart = tab.Document.Text.IndexOf("wrold", System.StringComparison.Ordinal);
        tab.UpdateCaret(1, 1, wordStart + 2, -1);

        sut.Actions.Require(AppActionIds.IgnoreMisspelledWord).Execute(null);

        tab.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void ClearIgnoredWords_action_makes_previously_ignored_words_misspelled_again()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        int wordStart = tab.Document.Text.IndexOf("wrold", System.StringComparison.Ordinal);
        tab.UpdateCaret(1, 1, wordStart + 2, -1);
        sut.Actions.Require(AppActionIds.IgnoreMisspelledWord).Execute(null);
        tab.MisspelledWordHighlights.Should().BeEmpty();

        sut.Actions.Require(AppActionIds.ClearIgnoredWords).Execute(null);

        tab.MisspelledWordHighlights.Should().ContainSingle();
    }

    // --------------------------------------------------- Spellcheck Editor --- //

    [Fact]
    public void SpellcheckEditor_action_raises_SpellcheckEditorRequested()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.SpellcheckEditorRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.SpellcheckEditor).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void OpenSpellcheckEditor_returns_null_without_a_book()
    {
        MainWindowViewModel sut = New();

        sut.OpenSpellcheckEditor().Should().BeNull();
    }

    [Fact]
    public void OpenSpellcheckEditor_lists_misspelled_words_from_open_tab_edits()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        OpenHtmlTabWithMisspelling(sut, book);

        SpellcheckEditorViewModel? editor = sut.OpenSpellcheckEditor();

        editor.Should().NotBeNull();
        editor!.Words.Should().ContainSingle(w => w.Word == "wrold" && w.Misspelled);
    }

    [Fact]
    public void SpellcheckEditor_ChangeAll_replaces_word_in_all_files_and_reloads_open_tabs()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        SpellcheckEditorViewModel editor = sut.OpenSpellcheckEditor()!;
        SpellcheckWordRow row = editor.Words.Single(w => w.Word == "wrold");
        editor.SetSelectedWords(new[] { row });
        editor.ChangeAllText = "world";

        editor.ChangeAllCommand.Execute(null);

        tab.Document.Text.Should().Contain("world").And.NotContain("wrold");
        book.Modified.Should().BeTrue();
        editor.Words.Should().NotContain(w => w.Word == "wrold");
    }

    [Fact]
    public void SpellcheckEditor_Ignore_refreshes_open_tab_highlights()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        tab.MisspelledWordHighlights.Should().ContainSingle();

        SpellcheckEditorViewModel editor = sut.OpenSpellcheckEditor()!;
        SpellcheckWordRow row = editor.Words.Single(w => w.Word == "wrold");
        editor.SetSelectedWords(new[] { row });

        editor.IgnoreCommand.Execute(null);

        tab.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void AddFavoriteSpecialCharacter_is_idempotent_and_RemoveFavoriteSpecialCharacter_removes_it()
    {
        MainWindowViewModel sut = New();

        sut.AddFavoriteSpecialCharacter("—");
        sut.AddFavoriteSpecialCharacter("—");
        sut.AddFavoriteSpecialCharacter("§");

        sut.FavoriteSpecialCharacters.Should().Equal("—", "§");

        sut.RemoveFavoriteSpecialCharacter("—");

        sut.FavoriteSpecialCharacters.Should().Equal("§");
    }

    // --- Book Browser action shortcuts (the panel context menu) ----------------- //

    /// <summary>
    /// Every Book Browser context menu item must be visible in the shortcut editor so that a key
    /// can be assigned to it.
    /// </summary>
    [Fact]
    public void Book_browser_context_menu_actions_are_available_as_bindable_actions()
    {
        MainWindowViewModel sut = New();

        AppAction[] bookBrowserActions = sut.Actions.Actions
            .Where(a => a.Category == AppActionIds.BookBrowserCategory)
            .ToArray();

        bookBrowserActions.Should().HaveCount(26);
        bookBrowserActions.Should().OnlyContain(a => a.IsEnabled, "each one has a handler attached");
        sut.Actions.Require(AppActionIds.BookBrowserRename).InputGestureText.Should().Be("F2");
    }

    /// <summary>
    /// Scope: Book Browser shortcuts must not be window KeyBindings, otherwise e.g. F2 in Code View
    /// would start a file rename. They go to a separate list that the panel registers itself.
    /// </summary>
    [Fact]
    public void Book_browser_shortcuts_are_scoped_to_the_panel_not_the_window()
    {
        MainWindowViewModel sut = New();

        sut.ShortcutActions.Should().NotContain(a => a.Category == AppActionIds.BookBrowserCategory);
        sut.BookBrowser.ShortcutActions.Should().OnlyContain(a => a.Category == AppActionIds.BookBrowserCategory);
        sut.BookBrowser.ShortcutActions.Should().Contain(a => a.Id == AppActionIds.BookBrowserRename);
    }

    /// <summary>
    /// Actions without a default shortcut are also on the window shortcut list; otherwise a shortcut
    /// assigned to them in Preferences would only work after restarting the application.
    /// </summary>
    [Fact]
    public void Window_shortcut_actions_include_actions_without_a_current_shortcut()
    {
        MainWindowViewModel sut = New();

        sut.ShortcutActions.Should().Contain(a => a.Gesture == null);
        sut.ShortcutActions.Should().Contain(a => a.Gesture != null);
    }

    /// <summary>The shortcut runs exactly the same command as the context menu item.</summary>
    [Fact]
    public void Book_browser_rename_action_runs_the_panel_command()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(new ImportEpub(epub).GetBook(), epub);

        BookBrowserNode file = sut.BookBrowser.Nodes
            .SelectMany(n => n.Children)
            .First(n => !n.IsFolder);
        sut.BookBrowser.UpdateSelection(new object?[] { file });

        Signet.Core.MainUI.OpfModelEntry? renamed = null;
        sut.BookBrowser.RenameRequested += (_, entry) => renamed = entry;

        sut.Actions.Require(AppActionIds.BookBrowserRename).Execute(null);

        renamed.Should().NotBeNull();
    }

    /// <summary>Without a selection the shortcut does nothing; it respects the panel command's CanExecute.</summary>
    [Fact]
    public void Book_browser_action_does_nothing_without_a_selection()
    {
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.BookBrowser.RenameRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.BookBrowserRename).Execute(null);

        raised.Should().BeFalse();
    }

    /// <summary>
    /// Live language switch: the menu, action texts and categories, and toolbar names are refreshed
    /// without a restart. <c>OnLanguageChanged</c> is called directly, because the global
    /// <c>Strings.NotifyLanguageChanged</c> would affect models of tests running in parallel.
    /// </summary>
    [Fact]
    public void Switching_the_ui_language_live_updates_menu_actions_and_toolbars()
    {
        using UiCultureScope culture = new("pl");
        MainWindowViewModel sut = New();
        sut.MenuItems[0].Header.Should().Be("_Plik");

        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en");
        sut.OnLanguageChanged();

        sut.MenuItems[0].Header.Should().Be("_File");
        sut.MenuItems[1].Header.Should().Be("_Edit");
        sut.MenuItems[0].Items!.Should().Contain(m => m.Header == "_Save");
        sut.Actions.Require(AppActionIds.Save).CategoryDisplayName.Should().Be("File");
        sut.Toolbars.Should().Contain(t => t.Name == "Undo/Redo");
    }
}
