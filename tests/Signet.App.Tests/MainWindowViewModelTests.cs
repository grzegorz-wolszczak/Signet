using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Signet.App.Infrastructure;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Tests.TestSupport;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the main window view model: the menu is built, toolbars are present, the dock
/// layout is created, panel actions work, and unimplemented actions report on the status bar.
/// </summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private MainWindowViewModel New() => _disposables.Add(new MainWindowViewModel());

    // The actions that work on a book are blocked until one is open.
    private MainWindowViewModel NewWithBook()
    {
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        sut.LoadBook(_disposables.Add(BookCreator.CreateNewBook("3.0")), string.Empty);
        return sut;
    }

    [Fact]
    public void The_title_shows_the_file_name_or_the_full_path_and_never_the_epub_version()
    {
        // Act & Assert
        MainWindowViewModel.FormatWindowTitle(true, "Book - Part 2.epub", @"C:\Books\Book - Part 2.epub", false, false)
            .Should().Be($"Book - Part 2.epub - {ApplicationInfo.Name}");
        MainWindowViewModel.FormatWindowTitle(true, "Book.epub", @"C:\Books\Book.epub", true, true)
            .Should().Be($@"C:\Books\Book.epub* - {ApplicationInfo.Name}");
        MainWindowViewModel.FormatWindowTitle(true, "Untitled.epub", string.Empty, false, true)
            .Should().Be($"Untitled.epub - {ApplicationInfo.Name}", "a book that was not saved has no path yet");
        MainWindowViewModel.FormatWindowTitle(false, "Book.epub", "x", true, true).Should().Be(ApplicationInfo.Name);
    }

    [Fact]
    public void WindowTitle_is_the_application_name()
    {
        // Act & Assert
        New().WindowTitle.Should().Be(ApplicationInfo.Name);
    }

    [Fact]
    public void Open_action_routes_through_the_file_workflow()
    {
        // Arrange
        MainWindowViewModel sut = New();
        FakeFileWorkflowPrompts prompts = new();
        sut.AttachFileWorkflowPrompts(prompts);

        // Act
        sut.Actions.Require(AppActionIds.Open).Execute(null);

        // Assert
        prompts.AskOpenPathCalls.Should().Be(1);
    }

    [Fact]
    public void LoadBook_feeds_the_book_browser_and_updates_the_title()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();

        // Act
        sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);

        // Assert
        sut.BookBrowser.HasBook.Should().BeTrue();
        sut.BookBrowser.Nodes.Should().NotBeEmpty();
        sut.WindowTitle.Should().Contain(ApplicationInfo.Name).And.Contain(".epub");
        sut.BookFileName.Should().Be(System.IO.Path.GetFileName(epub));
        sut.BookFileTooltip.Should().Be(epub, "the status bar shows the full path of the book in the tooltip");
        sut.StatusMessage.Should().Be(Strings.Format("Status_FileLoaded", epub), "books of the same name may come from other folders");
    }

    [Fact]
    public void SearchAllFilesOfSameType_action_opens_find_panel_and_selects_scope_by_active_resource()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        sut.IsFindReplaceVisible = false;
        sut.FindReplace.FindText = "search value";
        sut.FindReplace.ReplaceText = "replacement value";
        HtmlResource html = book.GetHtmlResources().First();
        CssResource css = book.GetCssResources().First();
        OpfResource opf = book.GetOpf();
        AppAction action = sut.Actions.Require(AppActionIds.SearchAllFilesOfSameType);

        // Act
        sut.Tabs.OpenResources(new Resource[] { html });
        action.Execute(null);

        // Assert
        sut.IsFindReplaceVisible.Should().BeTrue();
        sut.FindReplace.LookWhereIndex.Should().Be((int)LookWhere.AllHtmlFiles);
        sut.FindReplace.FindText.Should().Be("search value");
        sut.FindReplace.ReplaceText.Should().Be("replacement value");

        // Act
        sut.IsFindReplaceVisible = false;
        sut.Tabs.OpenResources(new Resource[] { css });
        action.Execute(null);

        // Assert
        sut.IsFindReplaceVisible.Should().BeTrue();
        sut.FindReplace.LookWhereIndex.Should().Be((int)LookWhere.AllCssFiles);
        sut.FindReplace.FindText.Should().Be("search value");
        sut.FindReplace.ReplaceText.Should().Be("replacement value");

        // Act
        sut.IsFindReplaceVisible = false;
        sut.Tabs.OpenResources(new Resource[] { opf });
        action.Execute(null);

        // Assert
        sut.IsFindReplaceVisible.Should().BeTrue();
        sut.FindReplace.LookWhereIndex.Should().Be((int)LookWhere.AllFiles);
        sut.FindReplace.FindText.Should().Be("search value");
        sut.FindReplace.ReplaceText.Should().Be("replacement value");
    }

    [Fact]
    public void FindReplace_all_files_scope_resolves_every_text_resource_in_book()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        IReadOnlyList<TextResource> expected = book.GetAllResources().OfType<TextResource>().ToList();

        // Act
        IReadOnlyList<TextResource> actual = ((IMultiFileSearchHost)sut).ResolveLookWhere(LookWhere.AllFiles);

        // Assert
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Opening_a_resource_from_the_book_browser_creates_a_document_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);

        BookBrowserNode text = sut.BookBrowser.Nodes.Single(n => n.Header == "Text");

        // Act
        sut.BookBrowser.UpdateSelection(new object?[] { text.Children[0] });
        sut.BookBrowser.OpenSelectedCommand.Execute(null);

        // Assert
        sut.Tabs.Count.Should().Be(1);
        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(text.Children[0].Entry!.BookPath);
    }

    [Fact]
    public void Menu_has_all_top_level_entries_and_no_excluded_features()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.MenuItems.Select(m => m.Header).Should().Equal(
            "_File", "_Edit", "_Insert", "For_mat", "_Search", "_Tools", "_View", "_Window", "_Help");

        static string Flatten(MenuItemViewModel item) =>
            item.Header + " " + string.Concat((item.Items ?? System.Array.Empty<MenuItemViewModel>()).Select(Flatten));

        string all = string.Concat(sut.MenuItems.Select(Flatten));

        // Act & Assert
        all.Should().NotContainAny("Plugin", "Manage Repositories", "Automat", "Index Editor");
    }

    [Fact]
    public void Every_menu_leaf_is_bound_to_a_command()
    {
        // Arrange
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

        // Act & Assert
        foreach (MenuItemViewModel top in sut.MenuItems)
        {
            Check(top);
        }
    }

    [Fact]
    public void Main_toolbars_are_exposed_in_rows()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.Toolbars.Should().HaveCount(ToolbarManager.AllToolbars.Count);
        sut.TopToolbars.Rows.Should().HaveCount(ToolbarManager.DefaultRows.Count);
        sut.TopToolbars.Rows[0].HasVisibleToolbars.Should().BeTrue();
        sut.TopToolbars.Rows[2].HasVisibleToolbars.Should().BeFalse("the Clip Bar is hidden by default");
        sut.BottomToolbars.Rows.Should().BeEmpty();
        sut.BottomToolbars.HasVisibleToolbars.Should().BeFalse();
    }

    [Fact]
    public void Dock_layout_contains_a_documents_area()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.Layout.Should().NotBeNull();
        sut.DockFactory.Should().NotBeNull();
    }

    [Fact]
    public void Documents_area_holds_its_space_and_does_not_collapse_when_empty()
    {
        // Arrange
        MainWindowViewModel sut = New();
        MainDockFactory factory = (MainDockFactory)sut.DockFactory;

        // Without this, an empty tab area collapsed to zero and the other panels took over its space.
        // Act & Assert
        factory.DocumentDock!.IsCollapsable.Should().BeFalse();

        // Validation Results starts hidden, so it does not dominate the center.
        factory.IsToolVisible(DockableIds.ValidationResults).Should().BeFalse();
    }

    [Fact]
    public void Panel_toggle_action_is_enabled_and_flips_visibility()
    {
        // Arrange
        MainWindowViewModel sut = New();
        AppAction toggle = sut.Actions.Require(AppActionIds.ToggleBookBrowser);
        MainDockFactory factory = (MainDockFactory)sut.DockFactory;

        // Act & Assert
        toggle.IsEnabled.Should().BeTrue();
        factory.IsToolVisible(DockableIds.BookBrowser).Should().BeTrue();

        // Act
        toggle.Execute(null);

        // Assert
        factory.IsToolVisible(DockableIds.BookBrowser).Should().BeFalse();

        // Act
        toggle.Execute(null);

        // Assert
        factory.IsToolVisible(DockableIds.BookBrowser).Should().BeTrue();
    }

    [Fact]
    public void Without_a_book_only_the_actions_that_need_none_are_enabled()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.HasBook.Should().BeFalse();
        sut.StartScreen.IsVisible.Should().BeTrue();
        foreach (string id in new[]
                 {
                     AppActionIds.Open, AppActionIds.NewDefault, AppActionIds.NewEpub2, AppActionIds.Preferences,
                     AppActionIds.Exit, AppActionIds.ClipEditor, AppActionIds.SearchEditor, AppActionIds.CustomLayout,
                     AppActionIds.ToggleBookBrowser, AppActionIds.ToggleNotifications, AppActionIds.About,
                 })
        {
            sut.Actions.Require(id).IsEnabled.Should().BeTrue(id + " works without a book");
        }

        foreach (string id in new[]
                 {
                     AppActionIds.Close, AppActionIds.Save, AppActionIds.SaveAs, AppActionIds.Find, AppActionIds.MetaEditor,
                     AppActionIds.Cleanup, AppActionIds.CreateCheckpoint, AppActionIds.ZoomIn, AppActionIds.BookBrowserRename,
                 })
        {
            sut.Actions.Require(id).IsEnabled.Should().BeFalse(id + " needs a book");
        }

        sut.Actions.Require(AppActionIds.ClipEditorRename).IsEnabled.Should().BeTrue("the clip library is not the book's");
    }

    [Fact]
    public void Closing_the_book_leaves_the_window_without_one()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = NewWithBook();

        // Act & Assert
        sut.HasBook.Should().BeTrue();
        sut.StartScreen.IsVisible.Should().BeFalse();
        sut.Actions.Require(AppActionIds.Save).IsEnabled.Should().BeTrue();
        sut.Actions.Require(AppActionIds.Close).IsEnabled.Should().BeTrue();

        // Act
        ((IBookWorkspace)sut).CloseBook();

        // Assert
        sut.HasBook.Should().BeFalse();
        ((IBookWorkspace)sut).CurrentBook.Should().BeNull();
        sut.StartScreen.IsVisible.Should().BeTrue();
        sut.BookBrowser.HasBook.Should().BeFalse();
        sut.Tabs.Count.Should().Be(0);
        sut.WindowTitle.Should().Be(ApplicationInfo.Name);
        sut.BookFileName.Should().BeNull();
        sut.Actions.Require(AppActionIds.Save).IsEnabled.Should().BeFalse();
        sut.Actions.Require(AppActionIds.Close).IsEnabled.Should().BeFalse();
        sut.Actions.Require(AppActionIds.Open).IsEnabled.Should().BeTrue();
        sut.StatusMessage.Should().Be(Strings.Get("Status_BookClosed"));
    }

    [Fact]
    public void Unimplemented_action_reports_on_the_status_bar()
    {
        // InsertRole (Insert -> Role...) is not wired up.
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        AppAction insertRole = sut.Actions.Require(AppActionIds.InsertRole);
        insertRole.IsEnabled = true;

        // Act
        insertRole.Execute(null);

        // Assert
        sut.StatusMessage.Should().Be(Strings.Format("Status_NotImplemented", insertRole.DefaultText.Replace("&", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public void AddCover_action_raises_AddCoverRequested()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        bool raised = false;
        sut.AddCoverRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.AddCover).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void Preferences_action_raises_PreferencesRequested()
    {
        // Arrange
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.PreferencesRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.Preferences).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void Reports_action_raises_ReportsRequested()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        bool raised = false;
        sut.ReportsRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.Reports).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void Cleanup_action_raises_CleanupRequested()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        bool raised = false;
        sut.CleanupRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.Cleanup).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareCleanup_returns_null_without_a_book()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act
        CleanupViewModel? cleanup = await sut.PrepareCleanupAsync();

        // Assert
        cleanup.Should().BeNull();
    }

    [Fact]
    public async Task PrepareCleanup_and_cleaning_the_CSS_tab_remove_an_unused_selector_and_report_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.ghost { color: red }\n");
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");

            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);

            // Act
            CleanupViewModel? cleanup = await sut.PrepareCleanupAsync();

            // Assert
            cleanup.Should().NotBeNull();
            cleanup!.Sections.Single(s => s.Step == CleanupStep.UnusedSelectors).IsEnabled = true;
            CleanupTabViewModel cssTab = cleanup.Tabs[0];
            cssTab.HasChanges.Should().BeTrue();

            // Act
            await cssTab.CleanCommand.ExecuteAsync(null);

            // Assert
            book.GetCssResources().Single().GetText().Should().NotContain(".ghost");
            book.Modified.Should().BeTrue();
            sut.StatusMessage.Should().Be(Strings.Get("Status_CleanupDone"));
            cssTab.HasChanges.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Cleaning_runs_under_the_progress_runner_in_two_phases_and_disables_the_other_book_operations()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.ghost { color: red }\n");
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            List<(string Title, bool Cancellable)> runs = new();
            List<bool> busyWhileRunning = new();
            List<WorkProgress> reports = new();
            ProgressRunner runner = (title, work, cancellable) =>
            {
                runs.Add((title, cancellable));
                busyWhileRunning.Add(sut.IsBookOperationBusy && !sut.Actions.Require(AppActionIds.RenumberFootnotes).CanExecute(null)
                    && !sut.Actions.Require(AppActionIds.Cleanup).CanExecute(null));
                return work(new ListProgress(reports), CancellationToken.None);
            };
            CleanupViewModel cleanup = (await sut.PrepareCleanupAsync(runner))!;
            cleanup.Sections.Single(s => s.Step == CleanupStep.UnusedSelectors).IsEnabled = true;
            int states = sut.CheckpointHistory.States.Count;

            // Act
            await cleanup.Tabs[0].CleanCommand.ExecuteAsync(null);

            // Assert
            runs.Should().ContainSingle().Which.Should().Be((Strings.Get("Operation_Cleanup"), true), "a checkpoint was taken, so the cleaning can be cancelled");
            sut.CheckpointHistory.States.Should().HaveCount(states + 1, "a checkpoint was taken before the cleaning");
            busyWhileRunning.Should().Equal(true);
            reports.Select(r => r.Phase!.Name).Distinct().Should().Equal(Strings.Get("Cleanup_Phase_Cleaning"), Strings.Get("Cleanup_Phase_Analyzing"));
            reports.Should().OnlyContain(r => r.Phase!.Count == 2 && r.Message == null && r.Fraction == null);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Cancelling_the_cleaning_brings_the_book_back_and_closes_the_dialog()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.ghost { color: red }\n");
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            using CancellationTokenSource pressed = new();
            ProgressRunner runner = (_, work, cancellable) =>
            {
                cancellable.Should().BeTrue();
                pressed.Cancel();
                return work(new ListProgress(new List<WorkProgress>()), pressed.Token);
            };
            CleanupViewModel cleanup = (await sut.PrepareCleanupAsync(runner))!;
            cleanup.Sections.Single(s => s.Step == CleanupStep.UnusedSelectors).IsEnabled = true;
            string before = book.GetCssResources().Single().GetText();
            int states = sut.CheckpointHistory.States.Count;
            bool closeRequested = false;
            cleanup.CloseRequested += (_, _) => closeRequested = true;

            // Act
            await cleanup.Tabs[0].CleanCommand.ExecuteAsync(null);

            // Assert
            Book restored = ((IBookWorkspace)sut).CurrentBook!;
            restored.GetCssResources().Single().GetText().Should().Be(before).And.Contain(".ghost");
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the cancelled cleaning is forgotten");
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelledRestored", Strings.Get("Operation_Cleanup")));
            closeRequested.Should().BeTrue("the analysis of the dialog belongs to the book that was replaced");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    private sealed class ListProgress : IProgress<WorkProgress>
    {
        private readonly List<WorkProgress> _reports;

        public ListProgress(List<WorkProgress> reports) => _reports = reports;

        public void Report(WorkProgress value) => _reports.Add(value);
    }

    [Fact]
    public async Task ApplyAddCover_MarksImageAsCoverAndOpensTheCoverPage()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            MainWindowViewModel sut = New();
            sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);

            // Act
            ImageResource figure = (ImageResource)sut.GetCoverCandidateImages().Single(i => i.Filename == "figure.png");

            // Assert
            sut.FindExistingCoverHtml().Should().BeNull();

            // Act
            await sut.ApplyAddCoverAsync(figure);

            // Assert
            sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("cover.xhtml");
            sut.FindExistingCoverHtml().Should().NotBeNull();
        });
    }

    [Fact]
    public void AddNavToSpine_and_RemoveNavFromSpine_actions_toggle_spine_membership()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        // Act & Assert
        book.IsNavInSpine.Should().BeFalse();

        // Act
        sut.Actions.Require(AppActionIds.AddNavToSpineNonLinear).Execute(null);

        // Assert
        book.IsNavInSpine.Should().BeTrue();

        // Act
        sut.Actions.Require(AppActionIds.RemoveNavFromSpine).Execute(null);

        // Assert
        book.IsNavInSpine.Should().BeFalse();
    }

    [Fact]
    public async Task WellFormedCheckEpub_action_populates_ValidationResults_and_shows_the_panel()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);
            MainDockFactory factory = (MainDockFactory)sut.DockFactory;

            // Act
            sut.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            sut.ValidationResults.HasResults.Should().BeTrue();
            sut.ValidationResults.Rows.Should().Contain(r => r.FileName == "chapter1.xhtml");
            factory.IsToolVisible(DockableIds.ValidationResults).Should().BeTrue();
        });
    }

    [Fact]
    public async Task WellFormedCheckEpub_action_reports_no_results_for_a_wellformed_book()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            MainWindowViewModel sut = New();
            sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);

            // Act
            sut.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
            await sut.WhenIdleAsync();

            // The corpus font is a dummy, not a real font (see tests/corpus/generate-binaries.sh).
            // Assert
            sut.ValidationResults.Rows.Should().ContainSingle().Which.Result.Code.Should().Be("Validation_FontCorrupt");
        });
    }

    [Fact]
    public async Task ValidationResults_activation_opens_the_offending_resource_in_a_tab()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);
            sut.Actions.Require(AppActionIds.WellFormedCheckEpub).Execute(null);
            await sut.WhenIdleAsync();

            ValidationResultRow row = sut.ValidationResults.Rows.First();

            // Act
            sut.ValidationResults.Activate(row);

            // Assert
            sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(row.Result.BookPath);
        });
    }

    [Fact]
    public void ValidateStylesheetsWithW3C_opens_a_generated_page_per_stylesheet()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        List<string> openedPaths = new();
        sut.AttachExternalFileOpener(path => { openedPaths.Add(_disposables.DeleteFile(path)); return true; });

        // Act
        sut.Actions.Require(AppActionIds.ValidateStylesheetsWithW3C).Execute(null);

        // Assert
        openedPaths.Should().HaveCount(book.GetCssResources().Count);
        string html = File.ReadAllText(openedPaths[0]);
        html.Should().Contain(W3CValidation.ValidatorUrl);
    }

    [Fact]
    public void Print_action_delegates_to_PreviewViewModel_RequestPrint()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        bool raised = false;
        sut.Preview.PrintRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.Print).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void PrintPreview_action_generates_a_pdf_and_opens_it_via_the_external_file_opener()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        List<string> openedPaths = new();
        sut.AttachExternalFileOpener(path => { openedPaths.Add(path); return true; });
        string? requestedPath = null;
        sut.Preview.PrintToPdfRequested += (_, path) => requestedPath = path;

        // Act
        sut.Actions.Require(AppActionIds.PrintPreview).Execute(null);

        // The view (PreviewView) generates the PDF through the WebView and only on success calls
        // PreviewViewModel.NotifyPrintPreviewReady; without a WebView the test simulates that step.
        // Assert
        requestedPath.Should().NotBeNullOrEmpty();

        // Act
        sut.Preview.NotifyPrintPreviewReady(requestedPath!);

        // Assert
        openedPaths.Should().Equal(requestedPath);
    }

    [Fact]
    public void NcxGuideFromNav_generates_ncx_and_guide_entries_from_the_nav()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        // Act & Assert
        book.GetNcx().Should().BeNull();

        // Act
        sut.Actions.Require(AppActionIds.NcxGuideFromNav).Execute(null);

        NcxResource? ncx = book.GetNcx();

        // Assert
        ncx.Should().NotBeNull();
        ncx!.GetText().Should().Contain("chapter1.xhtml");

        HtmlResource chapter = book.GetHtmlResources().First(r => r.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
        book.GetOpf().GetGuideSemanticCodeForResource(chapter).Should().Be("text");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void RemoveNcxGuide_removes_the_ncx_resource_and_clears_the_guide()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        // Act & Assert
        book.GetNcx().Should().NotBeNull();

        // Act
        sut.Actions.Require(AppActionIds.RemoveNcxGuide).Execute(null);

        // Assert
        book.GetNcx().Should().BeNull();
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void UpdateManifestMediaTypes_and_UpdateManifestProperties_actions_mark_the_book_modified()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        // Act
        sut.Actions.Require(AppActionIds.UpdateManifestMediaTypes).Execute(null);

        // Assert
        book.Modified.Should().BeTrue();

        book.Modified = false;

        // Act
        sut.Actions.Require(AppActionIds.UpdateManifestProperties).Execute(null);

        // Assert
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void LoadBook_opens_the_first_html_file_in_spine_order()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());

        // Act
        sut.LoadBook(book, epub);

        Resource firstInSpine = book.GetOpf().GetSpineOrderResources(book.GetFolderKeeper().GetResourceList())[0];

        // Assert
        sut.Tabs.ActiveTab.Should().NotBeNull();
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(firstInSpine);
    }

    [Fact]
    public void Blocked_action_shows_the_notifications_panel_collapsed_with_an_unread_warning()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);
        var factory = (MainDockFactory)sut.DockFactory;

        // Act & Assert
        factory.IsToolVisible(DockableIds.Notifications).Should().BeFalse("the panel is hidden until needed");
        sut.HasUnreadWarnings.Should().BeFalse("the 'book loaded' message is only informational");

        // "Add Nav to Reading Order" is not available for EPUB 2 — the action is blocked.
        // Act
        sut.Actions.Require(AppActionIds.AddNavToSpine).Execute(null);

        // Assert
        sut.Notifications.Entries[0].IsWarning.Should().BeTrue();
        sut.IsStatusMessageWarning.Should().BeTrue();
        factory.IsToolVisible(DockableIds.Notifications).Should().BeTrue();
        factory.IsToolPinned(DockableIds.Notifications).Should().BeTrue("the panel appears collapsed, not over the editor");
        sut.HasUnreadWarnings.Should().BeTrue();
        sut.Notifications.UnreadWarnings.Should().Be(1);

        // Act
        sut.OpenNotificationsCommand.Execute(null);

        // Assert
        sut.HasUnreadWarnings.Should().BeFalse("opening the panel marks the warnings as read");
    }

    [Fact]
    public void Notifications_panel_action_toggles_the_panel()
    {
        // Arrange
        MainWindowViewModel sut = New();
        var factory = (MainDockFactory)sut.DockFactory;

        // Act
        sut.Actions.Require(AppActionIds.ToggleNotifications).Execute(null);

        // Assert
        factory.IsToolVisible(DockableIds.Notifications).Should().BeTrue();

        // Act
        sut.Actions.Require(AppActionIds.ToggleNotifications).Execute(null);

        // Assert
        factory.IsToolVisible(DockableIds.Notifications).Should().BeFalse();
    }

    [Fact]
    public void Format_actions_are_enabled_only_for_an_html_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        AppAction bold = sut.Actions.Require(AppActionIds.Bold);

        // Act
        sut.Tabs.CloseAllTabs(); // LoadBook opens the first HTML file; this test needs a state without tabs

        // Assert
        bold.IsEnabled.Should().BeFalse();

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { html });

        // Assert
        bold.IsEnabled.Should().BeTrue();

        CssResource css = book.GetAllResources().OfType<CssResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { css });

        // Assert
        bold.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Preview_click_while_another_tab_is_active_switches_to_the_previewed_file()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { html });
        var htmlTab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        int? htmlJump = null;
        htmlTab.ScrollToOffsetRequested += offset => htmlJump = offset;

        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        sut.Tabs.OpenResources(new Resource[] { css });
        var cssTab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        bool cssJumped = false;
        cssTab.ScrollToOffsetRequested += _ => cssJumped = true;

        // Assert
        sut.Preview.CurrentBookPath.Should().Be(html.BookPath, "the preview stays on the last HTML file");

        // Act
        sut.Preview.HandlePreviewMessage("signet-loc:5");

        // Assert
        sut.Tabs.ActiveTab.Should().BeSameAs(htmlTab, "the click refers to the previewed file, not the CSS tab");
        htmlJump.Should().Be(5);
        cssJumped.Should().BeFalse();
    }

    [Fact]
    public void Bold_action_wraps_the_active_code_tab_selection()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { html });
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;

        tab.Document.Text = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body>\n" +
            "<p>alpha beta</p>\n</body>\n</html>\n";
        int start = tab.Document.Text.IndexOf("beta", System.StringComparison.Ordinal);
        tab.UpdateSelection(start, start + 4);

        sut.Actions.Require(AppActionIds.Bold).Execute(null);

        // Assert
        tab.Document.Text.Should().Contain("<p>alpha <b>beta</b></p>");
    }

    [Fact]
    public void Insert_actions_are_enabled_only_for_an_html_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        AppAction special = sut.Actions.Require(AppActionIds.InsertSpecialCharacter);

        // Act
        sut.Tabs.CloseAllTabs(); // LoadBook opens the first HTML file; this test needs a state without tabs

        // Act & Assert
        special.IsEnabled.Should().BeFalse();

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { html });

        // Act & Assert
        special.IsEnabled.Should().BeTrue();

        // Act
        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        sut.Tabs.OpenResources(new Resource[] { css });

        // Act & Assert
        special.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void PasteClipboardHistory_action_is_enabled_only_with_an_open_tab_and_raises_the_request()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        AppAction paste = sut.Actions.Require(AppActionIds.PasteClipboardHistory);

        // Act
        sut.Tabs.CloseAllTabs(); // LoadBook opens the first HTML file; this test needs a state without tabs

        // Assert
        paste.IsEnabled.Should().BeFalse();

        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { html });

        // Assert
        paste.IsEnabled.Should().BeTrue();

        bool raised = false;
        sut.PasteClipboardHistoryRequested += (_, _) => raised = true;

        // Act
        paste.Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void PasteClipboardHistoryEntry_inserts_the_chosen_text_in_the_active_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        sut.Tabs.OpenResources(new Resource[] { html });
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        int caret = tab.Document.Text.IndexOf("<body>", System.StringComparison.Ordinal) + "<body>".Length;
        tab.UpdateSelection(caret, caret);

        // Act
        sut.PasteClipboardHistoryEntry("PASTED");

        // Assert
        tab.Document.Text.Should().Contain("<body>PASTED");
    }

    [Fact]
    public void InsertClip_action_raises_InsertAriaClipRequested()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        bool raised = false;
        sut.InsertAriaClipRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.InsertClip).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void ApplyAriaClip_for_a_number_only_code_inserts_the_translated_template()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });

        // Act
        sut.ApplyAriaClip("pagebreak_hr", roleCode: null);

        // Assert
        sut.ActiveCodeTab!.DocumentText.Should().Contain("epub:type=\"pagebreak\"");
    }

    // Without a view the caret is set the way the editor view reports it.
    private static void SetCaret(MainWindowViewModel sut, int offset) => sut.ActiveCodeTab!.UpdateCaret(1, 1, offset, 0);

    private (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) OpenMinimalChapter(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, book, chapter);
    }

    [Fact]
    public async Task SplitSection_splits_the_file_at_the_caret_and_opens_the_new_file_at_its_content()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            SetCaret(sut, sut.ActiveCodeTab!.DocumentText.IndexOf("<p>Hello", StringComparison.Ordinal));

            // Act
            sut.Actions.Require(AppActionIds.SplitSection).Execute(null);
        await sut.WhenIdleAsync();

            HtmlResource created = book.GetHtmlResources().Single(h => h.Filename == "chapter1_0001.xhtml");

            // Assert
            chapter.GetText().Should().Contain("Chapter 1</h1>").And.NotContain("Hello, world.");
            created.GetText().Should().Contain("<p>Hello, world.</p>").And.NotContain("<h1");
            sut.ActiveCodeTab!.Resource.Should().BeSameAs(created);
            sut.StatusMessage.Should().Be(Strings.Format("Status_FileSplit", "chapter1_0001.xhtml"));
        });
    }

    [Fact]
    public async Task SplitSection_inside_a_tag_changes_nothing_and_reports_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource _) = OpenMinimalChapter(temp);
            SetCaret(sut, sut.ActiveCodeTab!.DocumentText.IndexOf("<p>Hello", StringComparison.Ordinal) + 1);

            // Act
            sut.Actions.Require(AppActionIds.SplitSection).Execute(null);
        await sut.WhenIdleAsync();

            // Assert
            book.GetHtmlResources().Should().NotContain(h => h.Filename.StartsWith("chapter1_", StringComparison.Ordinal));
            sut.StatusMessage.Should().Be(Strings.Get("Status_CannotSplitFileHere"));
        });
    }

    [Fact]
    public async Task SplitOnSgfSectionMarkers_splits_the_current_file_at_its_markers()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            sut.ActiveCodeTab!.InsertSectionMarker(sut.ActiveCodeTab.DocumentText.IndexOf("<p>Hello", StringComparison.Ordinal));

            // Act
            sut.Actions.Require(AppActionIds.SplitOnSgfSectionMarkers).Execute(null);
        await sut.WhenIdleAsync();

            // Assert
            book.GetHtmlResources().Should().Contain(h => h.Filename == "chapter1_0001.xhtml");
            chapter.GetText().Should().NotContain("Hello, world.").And.NotContain("signet_split_marker");
            sut.StatusMessage.Should().Be(Strings.Format("Status_FileSplitAtMarkers", 1));
        });
    }

    [Fact]
    public async Task SplitOnSgfSectionMarkers_without_markers_reports_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource _) = OpenMinimalChapter(temp);
            int before = book.GetHtmlResources().Count;

            // Act
            sut.Actions.Require(AppActionIds.SplitOnSgfSectionMarkers).Execute(null);
        await sut.WhenIdleAsync();

            // Assert
            book.GetHtmlResources().Should().HaveCount(before);
            sut.StatusMessage.Should().Be(Strings.Get("Status_NoSplitMarkersInFile"));
        });
    }

    [Fact]
    public async Task Removing_a_span_in_the_whole_book_writes_the_files_and_reports_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapterPath, File.ReadAllText(chapterPath).Replace(
                "<p>Hello, world.</p>", "<p><span class=\"s\">Hello</span>, <span class=\"s\">world</span>.</p>", StringComparison.Ordinal));
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
            Signet.App.ViewModels.Tabs.ICodeTabHost host = sut;
            string text = chapter.GetText();
            int offset = text.IndexOf("<span", StringComparison.Ordinal) + 1;

            // Act & Assert
            host.CountSameSpansInBook(chapter, text, offset).Should().Be(2);

            // Act
            SpanRemovalPlan plan = host.PlanSpanRemoval(chapter, text, offset, SpanRemovalScope.Book)!;
            await host.ApplySpanRemovalInBook(plan);

            // Assert
            chapter.GetText().Should().Contain("<p>Hello, world.</p>");
            book.Modified.Should().BeTrue();
            sut.StatusMessage.Should().Be(Strings.Format("Status_SpansRemovedInBook", 2, 1));
        });
    }

    [Fact]
    public void ClipEditor_action_raises_ClipEditorRequested()
    {
        // Arrange
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.ClipEditorRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.ClipEditor).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectClip_action_raises_SelectClipRequested_and_ApplySelectedClip_pastes_it()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        bool raised = false;
        sut.SelectClipRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.SelectClip).Execute(null);
        sut.ApplySelectedClip("<mark>library clip</mark>");

        // Assert
        raised.Should().BeTrue();
        sut.ActiveCodeTab!.DocumentText.Should().Contain("<mark>library clip</mark>");
    }

    [Fact]
    public void Adding_a_top_level_clip_refreshes_the_first_clip_bar_action()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        AppAction clip1 = sut.Actions.Require(AppActionIds.Clip(1));

        // Act & Assert
        clip1.IsEnabled.Should().BeFalse();

        // Act
        sut.Clips.AddEntryCommand.Execute(null);
        sut.Clips.Nodes.Single().Name = "MyClip";

        // Assert
        clip1.IsEnabled.Should().BeTrue();
        clip1.Text.Should().Be("MyClip");
    }

    [Fact]
    public void ApplyInsertId_rejects_an_invalid_identifier_with_a_status_message()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act
        sut.ApplyInsertId("1-bad-start");

        // Assert
        sut.StatusMessage.Should().Contain("ID");
    }

    [Fact]
    public void InsertMediaResources_inserts_an_img_tag_in_the_active_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
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

        // Act
        sut.InsertMediaResources(new Resource[] { figure });

        // Assert
        tab.Document.Text.Should().Contain("<img alt=\"figure\" src=\"../images/figure.png\"/>");
    }

    [Fact]
    public void HeadingPreserveAttributes_action_toggles_and_shows_a_status_message()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = NewWithBook();
        AppAction preserve = sut.Actions.Require(AppActionIds.HeadingPreserveAttributes);

        // Act & Assert
        preserve.IsCheckable.Should().BeTrue();
        preserve.IsChecked.Should().BeFalse();

        // Act
        preserve.IsEnabled = true;
        preserve.Execute(null);

        // Assert
        preserve.IsChecked.Should().BeTrue();
        sut.StatusMessage.Should().Contain("ON");
    }

    // The long whole-book operations run off the UI thread: wait until the condition holds (the operation is done).
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        condition().Should().BeTrue("the operation is done");
    }

    [Fact]
    public async Task MendHtml_action_fixes_malformed_html_and_reports_on_status_bar()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            HtmlResource chapter = book.GetAllResources().OfType<HtmlResource>().Single(r => r.Filename == "chapter1.xhtml");

            // Act
            sut.Actions.Require(AppActionIds.MendHtml).Execute(null);
            await WaitUntilAsync(() => sut.StatusMessage.Contains("Mend All", StringComparison.Ordinal));

            // Assert
            WellFormedChecker.CheckXhtmlStructure(chapter.GetText(), chapter.EpubVersion).IsWellFormed.Should().BeTrue();
            sut.StatusMessage.Should().Contain("Mend All");
        });
    }

    [Fact]
    public async Task MendPrettifyHtml_action_skips_html_that_is_not_well_formed_and_reports_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            SkippedFilesReport? report = null;
            sut.SkippedFilesReportRequested += (_, r) => report = r;

            // Act
            sut.Actions.Require(AppActionIds.MendPrettifyHtml).Execute(null);
            await WaitUntilAsync(() => report is not null);

            // Assert
            report.Should().NotBeNull();
            report!.Skipped.Select(s => s.BookPath).Should().Contain(p => p.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
            sut.StatusMessage.Should().Be(Strings.Format(
                "Status_BatchPartial", Strings.Get("Operation_MendPrettifyAll"), report.Processed, report.Skipped.Count));
        });
    }

    [Fact]
    public async Task MendHtml_action_repairs_html_that_is_not_well_formed_without_a_report()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            bool reported = false;
            sut.SkippedFilesReportRequested += (_, _) => reported = true;

            // Act
            sut.Actions.Require(AppActionIds.MendHtml).Execute(null);
            await WaitUntilAsync(() => sut.StatusMessage.Contains("Mend All", StringComparison.Ordinal));

            // Assert
            reported.Should().BeFalse();
            book.GetHtmlResources().Should().OnlyContain(
                h => WellFormedChecker.CheckXhtmlStructure(h.GetText(), h.EpubVersion).IsWellFormed);
        });
    }

    private sealed class CollectingProgress : IProgress<WorkProgress>
    {
        public List<WorkProgress> Reports { get; } = new();

        public void Report(WorkProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task Mend_All_runs_under_the_progress_runner_with_the_name_of_the_operation_and_reports_the_files()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            int files = book.GetHtmlResources().Count;
            CollectingProgress progress = new();
            string? title = null;
            bool? cancellable = null;
            sut.ProgressRunner = (t, work, c) =>
            {
                title = t;
                cancellable = c;
                return work(progress, CancellationToken.None);
            };

            // Act
            sut.Actions.Require(AppActionIds.MendHtml).Execute(null);
            await WaitUntilAsync(() => sut.StatusMessage.Contains("Mend All", StringComparison.Ordinal));

            // Assert
            title.Should().Be("Mend All HTML Files");
            cancellable.Should().BeTrue("a checkpoint was taken before the operation");
            progress.Reports.Should().HaveCount(files);
            progress.Reports[0].Message.Should().StartWith(Strings.Format("Progress_FileBatch", 1, files, string.Empty));
            progress.Reports.Select(r => r.Fraction).OfType<double>().Should().BeInAscendingOrder().And.OnlyContain(f => f >= 0 && f < 1);
        });
    }

    [Fact]
    public async Task Cancelling_Mend_All_brings_the_book_back_and_says_so()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            Dictionary<string, string> before = book.GetHtmlResources().ToDictionary(h => h.BookPath, h => h.GetText());
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            sut.ProgressRunner = (_, work, _) =>
            {
                pressed.Cancel();
                return work(new CollectingProgress(), pressed.Token);
            };

            // Act
            sut.Actions.Require(AppActionIds.MendHtml).Execute(null);
            await WaitUntilAsync(() => sut.StatusMessage.Contains("Cancelled", StringComparison.Ordinal));

            // Assert
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelledRestored", "Mend All HTML Files"));
            Book restored = ((IBookWorkspace)sut).CurrentBook!;
            restored.GetHtmlResources().ToDictionary(h => h.BookPath, h => h.GetText()).Should().Equal(before);
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the cancelled operation is forgotten");
            sut.BookBrowser.SuspendRebuilds.Should().BeFalse();
        });
    }

    [Fact]
    public void Standardize_action_raises_StandardizeRequested()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        bool raised = false;
        sut.StandardizeRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.Standardize).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareStandardization_returns_null_without_a_book()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act
        StandardizationViewModel? standardization = await sut.PrepareStandardizationAsync();

        // Assert
        standardization.Should().BeNull();
    }

    [Fact]
    public async Task Applying_the_Standardize_dialog_standardizes_the_book_and_remembers_the_checked_steps()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);

            // Act
            StandardizationViewModel? standardization = await sut.PrepareStandardizationAsync();

            // Assert
            standardization.Should().NotBeNull();
            standardization!.EnabledSteps.Should().Equal(EpubStandardization.AllSteps, "every step is checked the first time");
            standardization.Sections.Single(s => s.Step == StandardizationStep.RebaseManifestIds).IsEnabled = false;

            // Act
            await standardization.ApplyCommand.ExecuteAsync(null);

            // Assert
            book.GetOpf().BookPath.Should().Be("OEBPS/content.opf");
            book.GetAllResources().OfType<HtmlResource>().Should().OnlyContain(r => r.BookPath.StartsWith("OEBPS/Text/", StringComparison.Ordinal));
            book.Modified.Should().BeTrue();
            sut.StatusMessage.Should().Be(Strings.Get("Status_StandardizeDone"));
            (await sut.PrepareStandardizationAsync())!.EnabledSteps.Should().Equal(
                StandardizationStep.StandardFolders, StandardizationStep.StandardFileExtensions, StandardizationStep.ManifestMediaTypes);
        });
    }

    [Fact]
    public void StandardizeEpub_action_raises_StandardizeEpubRequested()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        bool raised = false;
        sut.StandardizeEpubRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.StandardizeEpub).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyStandardizeEpub_moves_every_resource_to_its_standard_folder()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);

            // Act
            await sut.ApplyStandardizeEpubAsync();

            // Assert
            book.GetOpf().BookPath.Should().Be("OEBPS/content.opf");
            book.GetAllResources().OfType<HtmlResource>().Should().OnlyContain(r => r.BookPath.StartsWith("OEBPS/Text/", System.StringComparison.Ordinal));
            sut.StatusMessage.Should().Contain("Restructuring");
        });
    }

    [Fact]
    public async Task UseStandardFileExtensions_action_renames_mismatched_extension_and_reports_on_status_bar()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
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
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);

            // Act
            sut.Actions.Require(AppActionIds.UseStandardFileExtensions).Execute(null);
        await sut.WhenIdleAsync();

            // Assert
            book.GetCssResources().Single().Filename.Should().Be("main.css");
            sut.StatusMessage.Should().Contain("Bulk rename");
        });
    }

    [Fact]
    public async Task RebaseManifestIds_action_renumbers_ids_and_reports_on_status_bar()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);

            // Act
            sut.Actions.Require(AppActionIds.RebaseManifestIds).Execute(null);
        await sut.WhenIdleAsync();

            // Assert
            book.Modified.Should().BeTrue();
            sut.StatusMessage.Should().Contain("rebased");
        });
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
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        var tab = (Signet.App.ViewModels.Tabs.CodeTabViewModel)sut.Tabs.ActiveTab!;
        AppAction toggle = sut.Actions.Require(AppActionIds.WordWrap);

        // Act & Assert
        toggle.IsCheckable.Should().BeTrue();
        toggle.IsChecked.Should().BeTrue();
        tab.WordWrap.Should().BeTrue("Code Editor wraps lines by default");

        // Act
        toggle.Execute(null);

        // Assert
        toggle.IsChecked.Should().BeFalse();
        tab.WordWrap.Should().BeFalse();

        // Act
        toggle.Execute(null);

        // Assert
        tab.WordWrap.Should().BeTrue();
    }

    [Fact]
    public void AutoSpellCheck_action_toggles_setting_and_refreshes_open_tabs()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        AppAction toggle = sut.Actions.Require(AppActionIds.AutoSpellCheck);

        // Act & Assert
        toggle.IsChecked.Should().BeFalse();
        tab.MisspelledWordHighlights.Should().BeEmpty();

        // Act
        toggle.Execute(null);

        // Assert
        toggle.IsChecked.Should().BeTrue();
        tab.MisspelledWordHighlights.Should().ContainSingle();

        // Act
        toggle.Execute(null);

        // Assert
        toggle.IsChecked.Should().BeFalse();
        tab.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void Spellcheck_actions_are_enabled_only_for_an_html_code_tab_with_spellcheck_on()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);

        AppAction next = sut.Actions.Require(AppActionIds.Spellcheck);

        // Act & Assert
        next.IsEnabled.Should().BeFalse();

        // Act
        OpenHtmlTabWithMisspelling(sut, book);

        // Assert
        next.IsEnabled.Should().BeFalse("spellcheck is still disabled");

        // Act
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);

        // Assert
        next.IsEnabled.Should().BeTrue();

        CssResource css = book.GetAllResources().OfType<CssResource>().First();

        // Act
        sut.Tabs.OpenResources(new Resource[] { css });

        // Assert
        next.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Spellcheck_action_jumps_to_the_next_misspelled_word()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        tab.UpdateCaret(1, 1, 0, -1);

        (int Start, int End, bool Wrapped)? found = null;
        tab.SearchResultRequested += (start, end, wrapped) => found = (start, end, wrapped);

        // Act
        sut.Actions.Require(AppActionIds.Spellcheck).Execute(null);

        // Assert
        found.Should().NotBeNull();
        tab.Document.GetText(found!.Value.Start, found.Value.End - found.Value.Start).Should().Be("wrold");
    }

    [Fact]
    public void AddMisspelledWord_and_IgnoreMisspelledWord_actions_delegate_to_the_active_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        int wordStart = tab.Document.Text.IndexOf("wrold", System.StringComparison.Ordinal);
        tab.UpdateCaret(1, 1, wordStart + 2, -1);

        // Act
        sut.Actions.Require(AppActionIds.IgnoreMisspelledWord).Execute(null);

        // Assert
        tab.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void ClearIgnoredWords_action_makes_previously_ignored_words_misspelled_again()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);
        int wordStart = tab.Document.Text.IndexOf("wrold", System.StringComparison.Ordinal);
        tab.UpdateCaret(1, 1, wordStart + 2, -1);
        sut.Actions.Require(AppActionIds.IgnoreMisspelledWord).Execute(null);

        tab.MisspelledWordHighlights.Should().BeEmpty();

        // Act
        sut.Actions.Require(AppActionIds.ClearIgnoredWords).Execute(null);

        // Assert
        tab.MisspelledWordHighlights.Should().ContainSingle();
    }

    // --------------------------------------------------- Spellcheck Editor --- //

    [Fact]
    public void SpellcheckEditor_action_raises_SpellcheckEditorRequested()
    {
        // Arrange
        MainWindowViewModel sut = NewWithBook();
        bool raised = false;
        sut.SpellcheckEditorRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.SpellcheckEditor).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public void OpenSpellcheckEditor_returns_null_without_a_book()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.OpenSpellcheckEditor().Should().BeNull();
    }

    [Fact]
    public void OpenSpellcheckEditor_lists_misspelled_words_from_open_tab_edits()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        OpenHtmlTabWithMisspelling(sut, book);

        // Act
        SpellcheckEditorViewModel? editor = sut.OpenSpellcheckEditor();

        // Assert
        editor.Should().NotBeNull();
        editor!.Words.Should().ContainSingle(w => w.Word == "wrold" && w.Misspelled);
    }

    [Fact]
    public async Task SpellcheckEditor_ChangeAll_replaces_word_in_all_files_and_reloads_open_tabs()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            var tab = OpenHtmlTabWithMisspelling(sut, book);
            SpellcheckEditorViewModel editor = sut.OpenSpellcheckEditor()!;
            SpellcheckWordRow row = editor.Words.Single(w => w.Word == "wrold");
            editor.SetSelectedWords(new[] { row });
            editor.ChangeAllText = "world";
            List<(string Title, bool Cancellable)> runs = new();
            sut.DelayedProgressRunner = (t, work, c) =>
            {
                runs.Add((t, c));
                return work(new CollectingProgress(), CancellationToken.None);
            };

            // Act
            editor.ChangeAllCommand.Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            tab.Document.Text.Should().Contain("world").And.NotContain("wrold");
            book.Modified.Should().BeTrue();
            editor.Words.Should().NotContain(w => w.Word == "wrold");
            runs.Select(r => r.Title).Should().Equal(
                Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_SpellCheck")), Strings.Get("CheckpointOp_SpellCheck"));
            runs[^1].Cancellable.Should().BeFalse("a quick edit like this one offers no Cancel");
        });
    }

    [Fact]
    public async Task RemoveEditionPageAsync_runs_under_the_delayed_progress_runner()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            MainWindowViewModel sut = New();
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            EditionPage.Stamp(book, EditionPageSettings.Default with { Enabled = true }, DateTime.UtcNow, "en", "0.0.0-test");
            EditionPage.Find(book).Should().NotBeNull("a precondition of this test");
            List<(string Title, bool Cancellable)> runs = new();
            sut.DelayedProgressRunner = (t, work, c) =>
            {
                runs.Add((t, c));
                return work(new CollectingProgress(), CancellationToken.None);
            };
            IEditionPageHost host = sut;

            // Act
            bool removed = await host.RemoveEditionPageAsync();

            // Assert
            removed.Should().BeTrue();
            EditionPage.Find(book).Should().BeNull();
            runs.Select(r => r.Title).Should().Equal(
                Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_RemoveEditionPage")), Strings.Get("CheckpointOp_RemoveEditionPage"));
            runs[^1].Cancellable.Should().BeFalse("a quick edit like this one offers no Cancel");
        });
    }

    [Fact]
    public void SpellcheckEditor_Ignore_refreshes_open_tab_highlights()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        var tab = OpenHtmlTabWithMisspelling(sut, book);
        sut.Actions.Require(AppActionIds.AutoSpellCheck).Execute(null);

        tab.MisspelledWordHighlights.Should().ContainSingle();

        SpellcheckEditorViewModel editor = sut.OpenSpellcheckEditor()!;
        SpellcheckWordRow row = editor.Words.Single(w => w.Word == "wrold");
        editor.SetSelectedWords(new[] { row });

        // Act
        editor.IgnoreCommand.Execute(null);

        // Assert
        tab.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void AddFavoriteSpecialCharacter_is_idempotent_and_RemoveFavoriteSpecialCharacter_removes_it()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act
        sut.AddFavoriteSpecialCharacter("—");
        sut.AddFavoriteSpecialCharacter("—");
        sut.AddFavoriteSpecialCharacter("§");

        // Assert
        sut.FavoriteSpecialCharacters.Should().Equal("—", "§");

        // Act
        sut.RemoveFavoriteSpecialCharacter("—");

        // Assert
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
        // Arrange
        MainWindowViewModel sut = NewWithBook();

        // Act
        AppAction[] bookBrowserActions = sut.Actions.Actions
            .Where(a => a.Category == AppActionIds.BookBrowserCategory)
            .ToArray();

        // Assert
        bookBrowserActions.Should().HaveCount(30);
        bookBrowserActions.Should().OnlyContain(a => a.IsEnabled, "each one has a handler attached");
        sut.Actions.Require(AppActionIds.BookBrowserRename).InputGestureText.Should().Be("F2");
    }

    /// <summary>
    /// Scope: Book Browser shortcuts must not be window KeyBindings, otherwise e.g. F2 in Code Editor
    /// would start a file rename. They go to a separate list that the panel registers itself.
    /// </summary>
    [Fact]
    public void Book_browser_shortcuts_are_scoped_to_the_panel_not_the_window()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.ShortcutActions.Should().NotContain(a => a.Category == AppActionIds.BookBrowserCategory);
        sut.BookBrowser.ShortcutActions.Should().OnlyContain(a => a.Category == AppActionIds.BookBrowserCategory);
        sut.BookBrowser.ShortcutActions.Should().Contain(a => a.Id == AppActionIds.BookBrowserRename);
    }

    /// <summary>
    /// The code editor actions are not window KeyBindings either: the code tab views bind them, so their shortcuts work
    /// only in Code Editor (and the same keys may do something else in a panel).
    /// </summary>
    [Fact]
    public void Code_editor_shortcuts_are_bound_by_the_code_tabs_not_the_window()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.ShortcutActions.Should().NotContain(a => AppActionIds.IsCodeEditorAction(a.Id));
        sut.CodeEditorShortcutActions.Should().OnlyContain(a => AppActionIds.IsCodeEditorAction(a.Id));
        sut.CodeEditorShortcutActions.Select(a => a.Id).Should().Contain(new[] { AppActionIds.RenameClass, AppActionIds.FindUsages });
        sut.ShortcutActions.Should().Contain(a => a.Id == AppActionIds.Save);
    }

    [Fact]
    public void Clip_editor_rename_is_a_panel_scoped_action_bound_to_F2_that_renames_the_selected_clip()
    {
        // Arrange
        MainWindowViewModel sut = New();

        AppAction rename = sut.Actions.Require(AppActionIds.ClipEditorRename);

        // Act & Assert
        rename.InputGestureText.Should().Be("F2");
        rename.IsEnabled.Should().BeTrue("it has a handler attached");
        sut.ShortcutActions.Should().NotContain(a => a.Category == AppActionIds.ClipEditorCategory);
        sut.Clips.ShortcutActions.Should().ContainSingle().Which.Id.Should().Be(AppActionIds.ClipEditorRename);

        // Act
        sut.Clips.AddGroupCommand.Execute(null);
        ClipNodeViewModel group = sut.Clips.Nodes.First();
        sut.Clips.SetSelectedNodes(new[] { group });
        rename.Execute(null);

        // Assert
        sut.Clips.EditingNode.Should().BeSameAs(group);
    }

    /// <summary>
    /// Actions without a default shortcut are also on the window shortcut list; otherwise a shortcut
    /// assigned to them in Preferences would only work after restarting the application.
    /// </summary>
    [Fact]
    public void Window_shortcut_actions_include_actions_without_a_current_shortcut()
    {
        // Arrange
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.ShortcutActions.Should().Contain(a => a.Gesture == null);
        sut.ShortcutActions.Should().Contain(a => a.Gesture != null);
    }

    /// <summary>The shortcut runs exactly the same command as the context menu item.</summary>
    [Fact]
    public void Book_browser_rename_action_runs_the_panel_command()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel sut = New();
        sut.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);

        BookBrowserNode file = sut.BookBrowser.Nodes
            .SelectMany(n => n.Children)
            .First(n => !n.IsFolder);
        sut.BookBrowser.UpdateSelection(new object?[] { file });

        Signet.Core.MainUI.OpfModelEntry? renamed = null;
        sut.BookBrowser.RenameRequested += (_, entry) => renamed = entry;

        // Act
        sut.Actions.Require(AppActionIds.BookBrowserRename).Execute(null);

        // Assert
        renamed.Should().NotBeNull();
    }

    /// <summary>Without a selection the shortcut does nothing; it respects the panel command's CanExecute.</summary>
    [Fact]
    public void Book_browser_action_does_nothing_without_a_selection()
    {
        // Arrange
        MainWindowViewModel sut = New();
        bool raised = false;
        sut.BookBrowser.RenameRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.BookBrowserRename).Execute(null);

        // Assert
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
        // Arrange
        using UiCultureScope culture = new("pl");
        MainWindowViewModel sut = New();

        // Act & Assert
        sut.MenuItems[0].Header.Should().Be("_Plik");

        // Act
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en");
        sut.OnLanguageChanged();

        // Assert
        sut.MenuItems[0].Header.Should().Be("_File");
        sut.MenuItems[1].Header.Should().Be("_Edit");
        sut.MenuItems[0].Items!.Should().Contain(m => m.Header == "_Save");
        sut.Actions.Require(AppActionIds.Save).CategoryDisplayName.Should().Be("File");
        sut.Toolbars.Should().Contain(t => t.Name == "Undo/Redo");
    }
}
