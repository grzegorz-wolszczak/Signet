using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Signet.Core.Localization;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for the File menu workflow: New / Open / Save / Save As, the unsaved-changes guard, "Recent Files".</summary>
public sealed class FileWorkflowTests
{
    private sealed class FakeWorkspace : IBookWorkspace
    {
        public Book? CurrentBook { get; private set; }

        public bool HasUnsavedChanges { get; set; }

        public int SaveOpenTabsCalls { get; private set; }

        public int ApplyBookCalls { get; private set; }

        public string? LastAppliedPath { get; private set; }

        public int RefreshAfterBookEditCalls { get; private set; }

        public void SaveOpenTabs() => SaveOpenTabsCalls++;

        public void RefreshAfterBookEdit() => RefreshAfterBookEditCalls++;

        public void ApplyBook(Book book, string? sourcePath)
        {
            ApplyBookCalls++;
            LastAppliedPath = sourcePath;
            CurrentBook?.Dispose();
            CurrentBook = book;
        }
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Dir = new TempDir();
            Settings = new SettingsStore(Path.Combine(Dir.Path, "settings.json"));
            Prompts = new FakeFileWorkflowPrompts();
            Workspace = new FakeWorkspace();
            Workflow = new FileWorkflow(Settings, Prompts, Workspace, NullLogger.Instance);
        }

        public TempDir Dir { get; }

        public SettingsStore Settings { get; }

        public FakeFileWorkflowPrompts Prompts { get; }

        public FakeWorkspace Workspace { get; }

        public FileWorkflow Workflow { get; }

        public string PathIn(string name) => Path.Combine(Dir.Path, name);

        public void Dispose()
        {
            Workspace.CurrentBook?.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public async Task NewAsync_builds_a_book_and_has_no_file_path()
    {
        using Harness h = new();

        await h.Workflow.NewAsync("3.0");

        h.Workspace.ApplyBookCalls.Should().Be(1);
        h.Workspace.CurrentBook!.IsEpub3.Should().BeTrue();
        h.Workspace.CurrentBook.GetHtmlResources().Should().NotBeEmpty();
        h.Workflow.CurrentFilePath.Should().BeEmpty();
        h.Workflow.CurrentFileName.Should().Be(FileWorkflow.DefaultFileName);
        h.Workflow.HasSavedFile.Should().BeFalse();
    }

    [Fact]
    public async Task Dirty_guard_cancel_aborts_New()
    {
        using Harness h = new();
        h.Workspace.HasUnsavedChanges = true;
        h.Prompts.SaveChoice = SaveChangesChoice.Cancel;

        await h.Workflow.NewAsync(null);

        h.Prompts.AskSaveChangesCalls.Should().Be(1);
        h.Workspace.ApplyBookCalls.Should().Be(0);
    }

    [Fact]
    public async Task Save_As_writes_an_epub_that_round_trips()
    {
        using Harness h = new();
        string target = h.PathIn("book.epub");
        h.Prompts.SavePath = target;

        await h.Workflow.NewAsync("3.0");
        bool ok = await h.Workflow.SaveAsAsync();

        ok.Should().BeTrue();
        File.Exists(target).Should().BeTrue();
        h.Workflow.CurrentFilePath.Should().Be(Path.GetFullPath(target));
        h.Workflow.HasSavedFile.Should().BeTrue();
        h.Workspace.SaveOpenTabsCalls.Should().Be(1);

        using Book reopened = new ImportEpub(target).GetBook();
        reopened.IsEpub3.Should().BeTrue();
        reopened.GetHtmlResources().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Save_after_Save_As_reuses_the_path_without_a_dialog()
    {
        using Harness h = new();
        h.Prompts.SavePath = h.PathIn("b.epub");

        await h.Workflow.NewAsync("2.0");
        await h.Workflow.SaveAsAsync();
        h.Prompts.AskSavePathCalls.Should().Be(1);

        bool ok = await h.Workflow.SaveAsync();

        ok.Should().BeTrue();
        h.Prompts.AskSavePathCalls.Should().Be(1);
    }

    [Fact]
    public async Task Saving_adds_the_file_to_recent_files()
    {
        using Harness h = new();
        string target = h.PathIn("recent.epub");
        h.Prompts.SavePath = target;

        await h.Workflow.NewAsync("2.0");
        await h.Workflow.SaveAsAsync();

        h.Settings.RecentFiles.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(target));
    }

    [Fact]
    public async Task Saving_with_the_edition_page_on_writes_the_page_into_the_saved_file()
    {
        using Harness h = new();
        h.Settings.EditionPage = EditionPageSettings.Default with
        {
            Enabled = true,
            Fields = new[] { new EditionPageField("Edited by", "GreatWorksPublishing", true) },
        };
        string target = h.PathIn("edition.epub");
        h.Prompts.SavePath = target;

        await h.Workflow.NewAsync("3.0");
        await h.Workflow.SaveAsAsync();
        await h.Workflow.SaveAsync();

        h.Workspace.RefreshAfterBookEditCalls.Should().Be(2, "the views show the refreshed page after every save");
        using Book reopened = new ImportEpub(target).GetBook();
        HtmlResource page = EditionPage.Find(reopened)!;
        page.Should().NotBeNull();
        page.GetText().Should().Contain("GreatWorksPublishing").And.Contain("<td>2</td>");
    }

    [Fact]
    public async Task Save_a_copy_also_refreshes_the_edition_page()
    {
        using Harness h = new();
        h.Settings.EditionPage = EditionPageSettings.Default with { Enabled = true };
        string target = h.PathIn("copy.epub");
        h.Prompts.SavePath = target;

        await h.Workflow.NewAsync("2.0");
        await h.Workflow.SaveACopyAsync();

        using Book reopened = new ImportEpub(target).GetBook();
        EditionPage.Find(reopened).Should().NotBeNull();
        h.Workspace.CurrentBook!.Modified.Should().BeTrue("the revision changed in the open book, which still has no file of its own");
    }

    [Fact]
    public async Task Saving_with_the_edition_page_off_adds_no_page()
    {
        using Harness h = new();
        string target = h.PathIn("plain.epub");
        h.Prompts.SavePath = target;

        await h.Workflow.NewAsync("3.0");
        await h.Workflow.SaveAsAsync();

        h.Workspace.RefreshAfterBookEditCalls.Should().Be(0);
        using Book reopened = new ImportEpub(target).GetBook();
        EditionPage.Find(reopened).Should().BeNull();
    }

    [Fact]
    public async Task OpenRecent_with_a_missing_file_removes_it_from_the_list()
    {
        using Harness h = new();
        string ghost = h.PathIn("gone.epub");
        h.Settings.RecentFiles = new[] { ghost };
        h.Prompts.RemoveMissingRecent = true;

        await h.Workflow.OpenRecentAsync(ghost);

        h.Settings.RecentFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadFileAsync_shows_load_warnings_in_one_dialog()
    {
        using Harness h = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("missing-mimetype"), h.Dir);

        bool ok = await h.Workflow.LoadFileAsync(epub);

        ok.Should().BeTrue();
        h.Prompts.LastLoadWarnings.Should().NotBeNull();
        h.Prompts.LastLoadWarnings!.Value.FileName.Should().Be(Path.GetFileName(epub));
        h.Prompts.LastLoadWarnings.Value.Warnings.Should().Equal(h.Workspace.CurrentBook!.LoadWarnings)
            .And.Contain(CoreStrings.Get("LoadWarning_MimetypeMissing"));
    }

    [Fact]
    public async Task LoadFileAsync_without_warnings_shows_no_dialog()
    {
        using Harness h = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir);

        await h.Workflow.LoadFileAsync(epub);

        h.Workspace.CurrentBook!.LoadWarnings.Should().BeEmpty();
        h.Prompts.LastLoadWarnings.Should().BeNull();
    }

    [Fact]
    public async Task LoadFileAsync_imports_a_txt_file()
    {
        using Harness h = new();
        string txt = h.PathIn("story.txt");
        File.WriteAllText(txt, "Rozdział pierwszy.\n\nByło ciemno i burzliwie.\n");

        bool ok = await h.Workflow.LoadFileAsync(txt);

        ok.Should().BeTrue();
        h.Workspace.CurrentBook!.GetHtmlResources().Should().Contain(r => r.Filename == "Section0001.xhtml");
        h.Workspace.LastAppliedPath.Should().Be(txt);
    }

    [Fact]
    public async Task OpenDroppedFile_loads_the_file_without_an_open_dialog()
    {
        using Harness h = new();
        string txt = h.PathIn("dropped.txt");
        File.WriteAllText(txt, "Przeciągnięty plik.\n");

        await h.Workflow.OpenDroppedFileAsync(txt);

        h.Workspace.LastAppliedPath.Should().Be(txt);
    }

    [Fact]
    public async Task Custom_layout_cancelled_does_not_build_a_book()
    {
        using Harness h = new();
        h.Prompts.CustomLayout = null;

        await h.Workflow.NewWithCustomLayoutAsync("3.0");

        h.Workspace.ApplyBookCalls.Should().Be(0);
    }
}
