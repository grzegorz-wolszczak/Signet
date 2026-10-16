using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Resources;
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

        public int CloseBookCalls { get; private set; }

        public void CloseBook()
        {
            CloseBookCalls++;
            CurrentBook?.Dispose();
            CurrentBook = null;
        }

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
            Settings = new SettingsStore(Path.Combine(Dir.Path, "settings.xml"));
            Prompts = new FakeFileWorkflowPrompts();
            Workspace = new FakeWorkspace();
            OtherInstance = new BookFileLocks(Path.Combine(Dir.Path, "locks"));
            Workflow = new FileWorkflow(Settings, Prompts, Workspace, NullLogger.Instance, OtherInstance);
        }

        // The locks of the same folder: what another instance of the application would hold.
        public BookFileLocks OtherInstance { get; }

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
    public async Task Close_leaves_the_window_without_a_book()
    {
        using Harness h = new();
        h.Prompts.SavePath = h.PathIn("book.epub");
        await h.Workflow.NewAsync("2.0");
        await h.Workflow.SaveAsAsync();
        int titleChanges = 0;
        h.Workflow.CurrentFileChanged += (_, _) => titleChanges++;

        await h.Workflow.CloseAsync();

        h.Workspace.CloseBookCalls.Should().Be(1);
        h.Workspace.CurrentBook.Should().BeNull();
        h.Workflow.CurrentFilePath.Should().BeEmpty();
        h.Workflow.HasSavedFile.Should().BeFalse();
        titleChanges.Should().Be(1);
    }

    [Fact]
    public async Task Close_without_a_book_does_nothing()
    {
        using Harness h = new();
        h.Workspace.HasUnsavedChanges = true;

        await h.Workflow.CloseAsync();

        h.Prompts.AskSaveChangesCalls.Should().Be(0);
        h.Workspace.CloseBookCalls.Should().Be(0);
    }

    [Fact]
    public async Task Dirty_guard_cancel_aborts_Close()
    {
        using Harness h = new();
        await h.Workflow.NewAsync("3.0");
        Book open = h.Workspace.CurrentBook!;
        h.Workspace.HasUnsavedChanges = true;
        h.Prompts.SaveChoice = SaveChangesChoice.Cancel;

        await h.Workflow.CloseAsync();

        h.Prompts.AskSaveChangesCalls.Should().Be(1);
        h.Workspace.CurrentBook.Should().BeSameAs(open);
        h.Workspace.CloseBookCalls.Should().Be(0);
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

        h.Workspace.RefreshAfterBookEditCalls.Should().Be(1, "only the first save of the session writes the page");
        using (Book saved = new ImportEpub(target).GetBook())
        {
            HtmlResource page = EditionPage.Find(saved)!;
            page.Should().NotBeNull();
            page.GetText().Should().Contain("GreatWorksPublishing").And.Contain("<td>1</td>");
        }

        // Opening the book again starts a new session: its first save raises the revision, the next one does not.
        (await h.Workflow.LoadFileAsync(target)).Should().BeTrue();
        await h.Workflow.SaveAsync();
        await h.Workflow.SaveAsync();

        h.Workspace.RefreshAfterBookEditCalls.Should().Be(2);
        using Book reopened = new ImportEpub(target).GetBook();
        EditionPage.Find(reopened)!.GetText().Should().Contain("<td>2</td>");
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
        h.Prompts.LastLoadWarnings.Value.Warnings.Should().Equal(h.Workspace.CurrentBook!.LoadWarningDetails);
        h.Prompts.LastLoadWarnings.Value.Warnings.Select(w => w.Message)
            .Should().Contain(CoreStrings.Get("LoadWarning_MimetypeMissing"));
    }

    // A file the "other instance" has open: its lock is held until the returned lock is disposed.
    private static (string Path, BookFileLock Held) FileOpenElsewhere(Harness h, string name = "in-use.epub")
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir, name);
        return (epub, h.OtherInstance.TryAcquire(epub)!);
    }

    [Fact]
    public async Task A_file_open_in_another_instance_is_not_loaded_until_the_user_decides()
    {
        using Harness h = new();
        (string epub, BookFileLock held) = FileOpenElsewhere(h);
        using BookFileLock _ = held;

        bool loaded = await h.Workflow.LoadFileAsync(epub);

        loaded.Should().BeFalse();
        h.Prompts.FilesInUse.Should().Equal(Path.GetFileName(epub));
        h.Workspace.ApplyBookCalls.Should().Be(0);
    }

    [Fact]
    public async Task Close_closes_the_application_and_loads_nothing()
    {
        using Harness h = new();
        (string epub, BookFileLock held) = FileOpenElsewhere(h);
        using BookFileLock _ = held;
        h.Prompts.InUseChoices.Enqueue(FileInUseChoice.Close);

        await h.Workflow.LoadFileAsync(epub);

        h.Prompts.CloseApplicationCalls.Should().Be(1);
        h.Workspace.ApplyBookCalls.Should().Be(0);
    }

    [Fact]
    public async Task Open_without_a_book_starts_with_a_new_empty_book()
    {
        using Harness h = new();
        (string epub, BookFileLock held) = FileOpenElsewhere(h);
        using BookFileLock _ = held;
        h.Prompts.InUseChoices.Enqueue(FileInUseChoice.OpenEmpty);

        bool loaded = await h.Workflow.LoadFileAsync(epub);

        loaded.Should().BeFalse("the file itself was not loaded");
        h.Workspace.ApplyBookCalls.Should().Be(1);
        h.Workspace.LastAppliedPath.Should().BeNull();
        h.Workflow.CurrentFilePath.Should().BeEmpty();
        h.Prompts.CloseApplicationCalls.Should().Be(0);
    }

    [Fact]
    public async Task Open_anyway_loads_the_file_without_taking_its_lock()
    {
        using Harness h = new();
        (string epub, BookFileLock held) = FileOpenElsewhere(h);
        using BookFileLock _ = held;
        h.Prompts.InUseChoices.Enqueue(FileInUseChoice.OpenAnyway);

        bool loaded = await h.Workflow.LoadFileAsync(epub);

        loaded.Should().BeTrue();
        h.Workspace.LastAppliedPath.Should().Be(epub);
        h.Workflow.CurrentFilePath.Should().Be(Path.GetFullPath(epub));
    }

    [Fact]
    public async Task Open_another_file_asks_for_a_file_and_loads_it_instead()
    {
        using Harness h = new();
        (string busy, BookFileLock held) = FileOpenElsewhere(h);
        using BookFileLock _ = held;
        string other = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir, "free.epub");
        h.Prompts.InUseChoices.Enqueue(FileInUseChoice.ChooseAnother);
        h.Prompts.OpenPath = other;

        bool loaded = await h.Workflow.LoadFileAsync(busy);

        loaded.Should().BeTrue();
        h.Workspace.LastAppliedPath.Should().Be(other);
        h.Prompts.FilesInUse.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelling_the_file_picker_asks_again()
    {
        using Harness h = new();
        (string busy, BookFileLock held) = FileOpenElsewhere(h);
        using BookFileLock _ = held;
        h.Prompts.InUseChoices.Enqueue(FileInUseChoice.ChooseAnother);
        h.Prompts.InUseChoices.Enqueue(FileInUseChoice.Close);
        h.Prompts.OpenPath = null;

        await h.Workflow.LoadFileAsync(busy);

        h.Prompts.FilesInUse.Should().HaveCount(2);
        h.Prompts.CloseApplicationCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_file_this_instance_has_open_is_loaded_again_without_asking()
    {
        using Harness h = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir, "mine.epub");
        await h.Workflow.LoadFileAsync(epub);

        bool again = await h.Workflow.LoadFileAsync(epub);

        again.Should().BeTrue();
        h.Prompts.FilesInUse.Should().BeEmpty();
        h.OtherInstance.TryAcquire(epub).Should().BeNull("the lock is still held by this instance");
    }

    [Fact]
    public async Task The_lock_follows_the_book_and_is_released_when_it_is_closed_or_replaced()
    {
        using Harness h = new();
        string first = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir, "first.epub");
        string second = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir, "second.epub");

        await h.Workflow.LoadFileAsync(first);
        h.OtherInstance.TryAcquire(first).Should().BeNull("the first book is open here");

        await h.Workflow.LoadFileAsync(second);
        using (BookFileLock? freed = h.OtherInstance.TryAcquire(first))
        {
            freed.Should().NotBeNull("the first book is no longer open here");
        }

        h.OtherInstance.TryAcquire(second).Should().BeNull();
        await h.Workflow.CloseAsync();
        using BookFileLock? afterClose = h.OtherInstance.TryAcquire(second);
        afterClose.Should().NotBeNull("closing the book releases its file");
    }

    [Fact]
    public async Task Save_As_moves_the_lock_to_the_new_file()
    {
        using Harness h = new();
        string first = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir, "first.epub");
        await h.Workflow.LoadFileAsync(first);
        h.Prompts.SavePath = h.PathIn("renamed.epub");

        await h.Workflow.SaveAsAsync();

        h.OtherInstance.TryAcquire(h.PathIn("renamed.epub")).Should().BeNull("the book now lives in the new file");
        using BookFileLock? old = h.OtherInstance.TryAcquire(first);
        old.Should().NotBeNull("the old file is free");
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
    public async Task LoadFileAsync_loads_with_a_progress_window_and_shows_an_error_only_after_it()
    {
        using Harness h = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, h.Dir);
        string broken = h.PathIn("broken.epub");
        File.WriteAllText(broken, "not a zip archive");

        (await h.Workflow.LoadFileAsync(epub)).Should().BeTrue();
        (await h.Workflow.LoadFileAsync(broken)).Should().BeFalse();

        h.Prompts.ProgressMessages.Should().Equal(
            Strings.Format("File_Loading", Path.GetFileName(epub)), Strings.Format("File_Loading", "broken.epub"));
        h.Prompts.LastError.Should().NotBeNullOrEmpty();
        h.Workspace.LastAppliedPath.Should().Be(epub, "the broken file is not applied");
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
