using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Search;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The maintenance operations of the whole book driven through the main window's view model the way the window drives
/// them: every one of them has to take its checkpoint, run its work off the UI thread under a progress window, keep the
/// other book operations disabled while it runs, and — when it is cancelled or refuses to run — leave the book and the
/// checkpoint history as they were. The progress window itself is a delegate here, so no Avalonia window is needed.
/// </summary>
public sealed class MainWindowBookOperationsTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    // The name of the operation in a progress window / status message, as the view model builds it (ActionLabel).
    private static string Label(string actionId) =>
        Strings.Get("Action_" + actionId.Replace('.', '_'))
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .TrimEnd('.', '…')
            .Trim();

    private static readonly string[] ElementPath = ["html", "body", "p"];

    private static IEnumerable<BookBrowserNode> Flatten(BookBrowserNode node)
    {
        yield return node;
        foreach (BookBrowserNode child in node.Children)
        {
            foreach (BookBrowserNode descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private (MainWindowViewModel Sut, Book Book) Open(TempDir temp, string corpusDir)
    {
        string epub = EpubBuilder.BuildInto(corpusDir, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        return (sut, book);
    }

    // Records what every progress window was asked to show and whether it offered Cancel.
    private sealed class Runs : IProgress<WorkProgress>
    {
        private readonly MainWindowViewModel? _sut;

        public Runs(MainWindowViewModel? sut = null) => _sut = sut;

        public List<(string Title, bool Cancellable)> Windows { get; } = new();

        public List<WorkProgress> Reports { get; } = new();

        public List<bool> BusyWhileRunning { get; } = new();

        public ProgressRunner Runner => (title, work, cancellable) =>
        {
            Windows.Add((title, cancellable));
            if (_sut is not null)
            {
                BusyWhileRunning.Add(_sut.IsBookOperationBusy);
            }

            return work(this, CancellationToken.None);
        };

        public void Report(WorkProgress value) => Reports.Add(value);
    }

    // A runner that presses Cancel the moment the window would appear.
    private static ProgressRunner CancellingRunner(CancellationTokenSource pressed, Action<string, bool>? seen = null) =>
        (title, work, cancellable) =>
        {
            seen?.Invoke(title, cancellable);
            pressed.Cancel();
            return work(new Runs(), pressed.Token);
        };

    [Fact]
    public async Task Standardize_refusing_a_not_wellformed_file_rewinds_its_checkpoint_and_says_so()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — a book whose structure Standardize would change, with one file made not well-formed first.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.EdgeDeepFolders);
            HtmlResource broken = book.GetHtmlResources().First();
            broken.SetText("<html><body><p>Unclosed");
            string opfBefore = book.GetOpf().BookPath;
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            await sut.ApplyStandardizationAsync(new[] { StandardizationStep.StandardFolders });

            // Assert
            sut.StatusMessage.Should().Be(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_Standardize"), broken.Filename));
            book.GetOpf().BookPath.Should().Be(opfBefore, "a refused operation changes nothing");
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the refused operation is rewound");
            runs.Windows.Should().ContainSingle().Which.Should().Be((Label(AppActionIds.Standardize), true));
            runs.BusyWhileRunning.Should().Equal(true);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Standardize_cancelled_while_its_checkpoint_is_taken_never_runs()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.EdgeDeepFolders);
            string opfBefore = book.GetOpf().BookPath;
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            string? checkpointTitle = null;
            bool? checkpointCancellable = null;
            sut.DelayedProgressRunner = CancellingRunner(pressed, (t, c) => (checkpointTitle, checkpointCancellable) = (t, c));
            Runs runs = new();
            sut.ProgressRunner = runs.Runner;

            // Act
            await sut.ApplyStandardizationAsync(new[] { StandardizationStep.StandardFolders });

            // Assert
            runs.Windows.Should().BeEmpty("the operation's own window is never reached when its checkpoint is cancelled");
            checkpointTitle.Should().Be(Strings.Format("Checkpoint_Before", Label(AppActionIds.Standardize)));
            checkpointCancellable.Should().BeTrue("taking a checkpoint can always be cancelled — it leaves nothing behind");
            book.GetOpf().BookPath.Should().Be(opfBefore);
            sut.CheckpointHistory.States.Should().HaveCount(states, "the cancelled checkpoint was never added");
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelled", Label(AppActionIds.Standardize)));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Create_HTML_TOC_writes_the_file_under_a_progress_window_and_opens_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            sut.Actions.Require(AppActionIds.CreateHtmlToc).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            book.GetHtmlResources().Should().ContainSingle(h => h.Filename == "TOC.xhtml");
            sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("TOC.xhtml");
            sut.StatusMessage.Should().Be(Strings.Get("Status_HtmlTocCreated"));
            runs.Windows.Should().ContainSingle().Which.Should().Be((Label(AppActionIds.CreateHtmlToc), true));
            sut.CheckpointHistory.States.Should().HaveCount(states + 1, "a checkpoint was taken before the file was written");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Generate_TOC_takes_a_checkpoint_before_the_dialog_and_writes_the_nav_afterwards()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act — the window asks for the dialog's view model, which is built in the background behind a checkpoint.
            HeadingSelectorViewModel? dialog = await sut.CreateHeadingSelectorViewModelAsync();

            // Assert
            dialog.Should().NotBeNull();
            sut.CheckpointHistory.States.Should().HaveCount(states + 1, "the dialog changes headings in the sources");
            runs.Windows.Should().ContainSingle().Which.Should().Be((Label(AppActionIds.GenerateToc), true));
            sut.IsBookOperationBusy.Should().BeFalse("the dialog is open now; the operation window is gone");

            // Act — the dialog renamed a heading in the sources; it closes with OK and the table of contents is rebuilt.
            HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
            chapter.SetText(chapter.GetText().Replace("Chapter 1</h1>", "Prologue</h1>", StringComparison.Ordinal));
            await sut.CompleteGenerateTocAsync(headingsChanged: true);

            // Assert
            book.GetNavResource()!.GetText().Should().Contain("Prologue");
            sut.StatusMessage.Should().Be(Strings.Get("Status_TocGenerated"));
            runs.Windows.Should().HaveCount(2);
            runs.Windows[1].Should().Be(
                (Label(AppActionIds.GenerateToc), false),
                "the checkpoint was taken before the dialog, so there is nothing to cancel back to here");
            sut.CheckpointHistory.States.Should().HaveCount(states + 1, "the second step runs under the checkpoint of the first");
        });
    }

    [Fact]
    public async Task Generate_TOC_that_changes_nothing_rewinds_the_checkpoint_of_its_dialog()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — the book's table of contents already matches its headings.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;

            // Act
            HeadingSelectorViewModel? dialog = await sut.CreateHeadingSelectorViewModelAsync();
            await sut.CompleteGenerateTocAsync(headingsChanged: false);

            // Assert
            dialog.Should().NotBeNull();
            sut.StatusMessage.Should().Be(Strings.Get("Status_TocNoChanges"));
            sut.CheckpointHistory.States.Should().HaveCount(
                states, "nothing changed, so the checkpoint taken before the dialog is withdrawn");
        });
    }

    [Fact]
    public async Task Generate_TOC_cancelled_while_its_checkpoint_is_taken_opens_no_dialog()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            sut.DelayedProgressRunner = CancellingRunner(pressed);

            // Act
            HeadingSelectorViewModel? dialog = await sut.CreateHeadingSelectorViewModelAsync();

            // Assert
            dialog.Should().BeNull("nothing is opened when the user cancels the checkpoint the dialog needs");
            sut.CheckpointHistory.States.Should().HaveCount(states);
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelled", Label(AppActionIds.GenerateToc)));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Edit_TOC_builds_its_view_model_in_the_background_behind_a_checkpoint()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            EditTocViewModel? dialog = await sut.CreateEditTocViewModelAsync();

            // Assert
            dialog.Should().NotBeNull();
            dialog!.Nodes.Should().NotBeEmpty();
            runs.Windows.Should().ContainSingle().Which.Should().Be((Label(AppActionIds.EditToc), true));
            sut.CheckpointHistory.States.Should().HaveCount(states + 1);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Upgrade_to_EPUB_3_converts_an_EPUB_2_book_under_a_progress_window()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub2Minimal);
            book.IsEpub3.Should().BeFalse("a precondition of this test");
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            sut.Actions.Require(AppActionIds.UpgradeToEpub3).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            book.IsEpub3.Should().BeTrue();
            book.GetNavResource().Should().NotBeNull("the nav is built from the NCX");
            sut.StatusMessage.Should().StartWith("Upgraded");
            runs.Windows.Should().ContainSingle().Which.Should().Be((Label(AppActionIds.UpgradeToEpub3), true));
            sut.CheckpointHistory.States.Should().HaveCount(states + 1, "the conversion is undoable");
            sut.IsBookOperationBusy.Should().BeFalse();

            // Act — a second run has nothing to do.
            sut.Actions.Require(AppActionIds.UpgradeToEpub3).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            sut.StatusMessage.Should().Be(Strings.Get("Status_AlreadyEpub3"));
            runs.Windows.Should().HaveCount(1, "an already upgraded book starts no operation at all");
        });
    }

    [Fact]
    public async Task Saving_the_book_writes_the_archive_in_the_background_in_three_phases()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Media);
            FakeFileWorkflowPrompts prompts = new() { SavePath = temp.Combine("saved.epub") };
            sut.AttachFileWorkflowPrompts(prompts);
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            sut.Actions.Require(AppActionIds.SaveAs).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            File.Exists(prompts.SavePath).Should().BeTrue();
            runs.Windows.Should().ContainSingle().Which.Should().Be(
                (Strings.Get("File_SavingTitle"), false), "the writing of the archive cannot be stopped partway");
            // No other operation may touch the book while it is written out.
            runs.BusyWhileRunning.Should().Equal(true);
            runs.Reports.Select(r => r.Phase!.Name).Distinct().Should().Equal(
                Strings.Get("BookSave_Phase_Preparing"),
                Strings.Get("BookSave_Phase_Copying"),
                Strings.Get("BookSave_Phase_Archiving"));
            runs.Reports.Should().OnlyContain(r => r.Phase!.Count == 3);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Replace_All_in_the_whole_book_takes_a_checkpoint_first()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            sut.FindReplace.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
            sut.FindReplace.FindText = "world";
            sut.FindReplace.ReplaceText = "earth";
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.DelayedProgressRunner = runs.Runner;
            HtmlResource chapter = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");

            // Act
            int replaced = await sut.FindReplace.ReplaceAllAsync();

            // Assert
            replaced.Should().Be(1);

            // Assert — the replacing wrote into the open tab of the file; flushing the tabs puts it into the resource.
            ((IBookWorkspace)sut).SaveOpenTabs();
            chapter.GetText().Should().Contain("Hello, earth.");
            runs.Windows.Should().ContainSingle().Which.Should().Be(
                (Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_ReplaceAll")), true));
            sut.CheckpointHistory.States.Should().HaveCount(states + 1);
            sut.IsBookOperationBusy.Should().BeFalse("the busy state is lifted before the replacing itself, which runs on the UI thread");
        });
    }

    [Fact]
    public async Task Replace_All_cancelled_while_its_checkpoint_is_taken_replaces_nothing()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            sut.FindReplace.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
            sut.FindReplace.FindText = "world";
            sut.FindReplace.ReplaceText = "earth";
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            sut.DelayedProgressRunner = CancellingRunner(pressed);

            // Act
            int replaced = await sut.FindReplace.ReplaceAllAsync();

            // Assert
            replaced.Should().Be(0);
            book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Contain("Hello, world.");
            sut.CheckpointHistory.States.Should().HaveCount(states);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Replace_All_that_replaces_nothing_rewinds_its_checkpoint()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            sut.FindReplace.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
            sut.FindReplace.FindText = "nothing matches this";
            sut.FindReplace.ReplaceText = "x";
            int states = sut.CheckpointHistory.States.Count;

            // Act
            int replaced = await sut.FindReplace.ReplaceAllAsync();

            // Assert
            replaced.Should().Be(0);
            sut.CheckpointHistory.States.Should().HaveCount(states, "a checkpoint of nothing is withdrawn again");
        });
    }

    [Fact]
    public async Task Replace_All_in_the_current_file_leaves_the_other_files_alone()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — the same word in two files, one of them open in a tab.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            foreach (string file in new[] { Path.Combine(tree, "EPUB", "text", "chapter1.xhtml"), Path.Combine(tree, "EPUB", "nav.xhtml") })
            {
                File.WriteAllText(file, File.ReadAllText(file).Replace("</body>", "<p>target</p></body>", StringComparison.Ordinal));
            }

            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
            HtmlResource nav = book.GetHtmlResources().Single(h => h.Filename == "nav.xhtml");
            sut.Tabs.OpenResources(new Resource[] { chapter });
            sut.FindReplace.LookWhereIndex = (int)LookWhere.AllHtmlFiles;
            sut.FindReplace.FindText = "target";
            sut.FindReplace.ReplaceText = "hit";
            int states = sut.CheckpointHistory.States.Count;

            // Act — "Replace All in Current File" narrows the scope of the panel to the open tab.
            sut.Actions.Require(AppActionIds.ReplaceAllInFile).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            ((IBookWorkspace)sut).SaveOpenTabs();
            chapter.GetText().Should().Contain("<p>hit</p>").And.NotContain("<p>target</p>");
            nav.GetText().Should().Contain("<p>target</p>", "the scope was the current file only");
            sut.CheckpointHistory.States.Should().HaveCount(states + 1, "the replacing is undoable");
            sut.FindReplace.LookWhereIndex.Should().Be((int)LookWhere.AllHtmlFiles, "the scope of the panel is put back afterwards");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task A_manual_checkpoint_cancelled_while_it_is_taken_creates_nothing_and_says_so()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            string? title = null;
            sut.DelayedProgressRunner = CancellingRunner(pressed, (t, _) => title = t);

            // Act
            bool created = await sut.CreateCheckpointAsync("By hand");

            // Assert
            created.Should().BeFalse();
            title.Should().Be("By hand", "the window is headed with the name the user gave the checkpoint");
            sut.CheckpointHistory.States.Should().HaveCount(states);
            sut.StatusMessage.Should().Be(
                Strings.Format("Status_OperationCancelled", Strings.Get("Checkpoint_CreatingTitle")));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task A_manual_checkpoint_is_created_under_a_progress_window()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.DelayedProgressRunner = runs.Runner;

            // Act
            bool created = await sut.CreateCheckpointAsync("By hand");

            // Assert
            created.Should().BeTrue();
            sut.CheckpointHistory.States.Should().HaveCount(states + 1);
            sut.CheckpointHistory.States.Should().Contain(s => s.Message == "By hand");
            runs.Windows.Should().ContainSingle().Which.Should().Be(("By hand", true));
            runs.BusyWhileRunning.Should().Equal(true);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task The_Standardize_dialog_is_not_opened_for_a_book_with_a_file_that_is_not_wellformed()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            HtmlResource broken = book.GetHtmlResources().First();
            broken.SetText("<html><body><p>Unclosed");
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            StandardizationViewModel? dialog = await sut.PrepareStandardizationAsync();

            // Assert
            dialog.Should().BeNull();
            sut.StatusMessage.Should().Be(
                Strings.Format("Status_CancelledNotWellFormed", Strings.Get("Operation_Standardize"), broken.Filename));
            runs.Windows.Should().ContainSingle().Which.Should().Be(
                (Strings.Get("Operation_Standardize"), true), "checking the files changes nothing, so it can always be cancelled");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Cancelling_the_wellformedness_check_opens_no_Standardize_dialog()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _) = Open(temp, CorpusPaths.Epub3Minimal);
            using CancellationTokenSource pressed = new();
            sut.ProgressRunner = CancellingRunner(pressed);

            // Act
            StandardizationViewModel? dialog = await sut.PrepareStandardizationAsync();

            // Assert
            dialog.Should().BeNull();
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelled", Strings.Get("Operation_Standardize")));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Splitting_a_file_where_it_cannot_be_split_rewinds_the_checkpoint_and_says_so()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — the caret inside a tag.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
            sut.Tabs.OpenResources(new Resource[] { chapter });
            int files = book.GetHtmlResources().Count;
            string before = chapter.GetText();
            int insideTag = before.IndexOf("<p", StringComparison.Ordinal) + 1;
            sut.ActiveCodeTab!.UpdateCaret(1, 1, insideTag, 'p');
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            await sut.SplitFileAtCaretAsync();

            // Assert
            book.GetHtmlResources().Should().HaveCount(files, "no file was created");
            chapter.GetText().Should().Be(before);
            sut.StatusMessage.Should().Be(Strings.Get("Status_CannotSplitFileHere"));
            runs.Windows.Should().ContainSingle().Which.Should().Be((Label(AppActionIds.SplitSection), true));
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the refused split is rewound");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task A_Book_Browser_rename_runs_as_a_book_operation_behind_a_checkpoint()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Media);
            OpfModelEntry figure = sut.BookBrowser.Nodes.SelectMany(Flatten)
                .Select(n => n.Entry)
                .OfType<OpfModelEntry>()
                .First(e => e.Resource.Filename == "figure.png");
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            await sut.BookBrowser.ApplyRenameAsync(figure, "picture.png");

            // Assert
            book.GetAllResources().Should().Contain(r => r.Filename == "picture.png").And.NotContain(r => r.Filename == "figure.png");
            runs.Windows.Should().ContainSingle().Which.Should().Be(
                (Strings.Format("CheckpointOp_Rename", "figure.png"), false),
                "the work of a Book Browser operation does not check the token, so only taking its checkpoint can be cancelled");
            runs.BusyWhileRunning.Should().Equal(true);
            sut.CheckpointHistory.States.Should().HaveCount(states + 1);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task A_Book_Browser_rename_that_fails_rewinds_its_checkpoint_and_says_so()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — renaming a file to the name of a file that is already there.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Media);
            OpfModelEntry figure = sut.BookBrowser.Nodes.SelectMany(Flatten)
                .Select(n => n.Entry)
                .OfType<OpfModelEntry>()
                .First(e => e.Resource.Filename == "figure.png");
            int states = sut.CheckpointHistory.States.Count;

            // Act
            await sut.BookBrowser.ApplyRenameAsync(figure, "cover.png");

            // Assert
            book.GetAllResources().Should().Contain(r => r.Filename == "figure.png", "the rename was refused");
            sut.StatusMessage.Should().NotBeEmpty();
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of a failed operation is rewound");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Adding_a_file_to_the_book_runs_as_a_book_operation_and_a_missing_file_rewinds_it()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Minimal);
            string added = temp.Combine("extra.css");
            File.WriteAllText(added, ".extra { color: red }");
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            IReadOnlyList<Resource> result = await sut.BookBrowser.AddFilesAsync(new[] { added });

            // Assert
            result.Should().ContainSingle().Which.Filename.Should().Be("extra.css");
            book.GetCssResources().Should().Contain(c => c.Filename == "extra.css");
            runs.Windows.Should().ContainSingle().Which.Should().Be((Strings.Format("CheckpointOp_AddFile", "extra.css"), false));
            sut.CheckpointHistory.States.Should().HaveCount(states + 1);

            // Act — a file that is not there any more: the operation fails and takes its checkpoint back.
            states = sut.CheckpointHistory.States.Count;
            IReadOnlyList<Resource> missing = await sut.BookBrowser.AddFilesAsync(new[] { temp.Combine("gone.css") });

            // Assert
            missing.Should().BeEmpty();
            sut.StatusMessage.Should().StartWith("Could not add files");
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the failed operation is rewound");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task A_Book_Browser_operation_refused_because_another_one_runs_changes_nothing()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — a host that refuses, as the main window does while another operation on the book is running.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub3Media);
            RefusingOperationHost refusing = new();
            sut.BookBrowser.Operations = refusing;
            int files = book.GetAllResources().Count;

            // Act
            await sut.BookBrowser.DeleteResourcesAsync(new Resource[] { book.GetCssResources().First() });

            // Assert
            refusing.Titles.Should().ContainSingle().Which.Should().Be(Strings.Get("CheckpointOp_DeleteFiles"));
            book.GetAllResources().Should().HaveCount(files, "the work never ran");
        });
    }

    // Stands in for the main window while it is busy: every operation is refused before its work runs.
    private sealed class RefusingOperationHost : IBookOperationHost
    {
        public List<string> Titles { get; } = new();

        public Task<BookOperationOutcome<T>> RunAsync<T>(string title, string? checkpointName, Func<T> work)
        {
            Titles.Add(title);
            return Task.FromResult(new BookOperationOutcome<T>(false, default, false));
        }
    }

    [Fact]
    public async Task Deleting_every_HTML_file_is_refused_and_starts_no_operation()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book) = Open(temp, CorpusPaths.Epub2Minimal);
            int htmlFiles = book.GetHtmlResources().Count;
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);
            sut.ProgressRunner = runs.Runner;

            // Act
            await sut.BookBrowser.DeleteResourcesAsync(book.GetHtmlResources().Cast<Resource>().ToList());

            // Assert
            book.GetHtmlResources().Should().HaveCount(htmlFiles);
            sut.StatusMessage.Should().Be(Strings.Get("BookBrowser_CannotDeleteAllHtml"));
            runs.Windows.Should().BeEmpty();
            sut.CheckpointHistory.States.Should().HaveCount(states);
        });
    }

    [Fact]
    public async Task Renaming_a_class_in_the_whole_book_runs_behind_a_checkpoint()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(
                chapterPath,
                File.ReadAllText(chapterPath).Replace("<p>Hello, world.</p>", "<p class=\"c1\">Hello, world.</p>", StringComparison.Ordinal));
            File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.c1 { color: red }\n");
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            ICodeTabHost host = sut;
            int states = sut.CheckpointHistory.States.Count;
            Runs runs = new(sut);

            // Act
            ClassRenamer? renamer = await host.PrepareClassRenameAsync();

            // Assert
            renamer.Should().NotBeNull();

            // Act
            ClassRenameResult result = renamer!.Rename(
                ClassRenameRequest.ForElement(new ClassAtCaret("c1", ElementPath), ClassRenameScope.Everywhere),
                "lead");
            await host.ApplyClassRename(result, runs.Runner);

            // Assert
            book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText()
                .Should().Contain("class=\"lead\"").And.NotContain("class=\"c1\"");
            book.GetCssResources().Single().GetText().Should().Contain(".lead").And.NotContain(".c1");
            sut.StatusMessage.Should().Be(Strings.Get("CodeEditor_ClassRenamed"));
            runs.Windows.Should().ContainSingle().Which.Should().Be((Strings.Get("Operation_RenameClass"), false));
            runs.BusyWhileRunning.Should().Equal(true);
            sut.CheckpointHistory.States.Should().HaveCount(states + 1);
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Renaming_an_id_in_the_whole_book_runs_behind_a_checkpoint()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(
                chapterPath,
                File.ReadAllText(chapterPath).Replace("<p>Hello, world.</p>", "<p id=\"p1\">Hello, world.</p>", StringComparison.Ordinal));
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            ICodeTabHost host = sut;
            Runs runs = new(sut);

            // Act
            IdRenamer? renamer = await host.PrepareIdRenameAsync();

            // Assert
            renamer.Should().NotBeNull();

            // Act
            await host.ApplyIdRename(renamer!.Rename("p1", "intro"), runs.Runner);

            // Assert
            book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText()
                .Should().Contain("id=\"intro\"").And.NotContain("id=\"p1\"");
            sut.StatusMessage.Should().Be(Strings.Get("CodeEditor_IdRenamed"));
            runs.Windows.Should().ContainSingle().Which.Should().Be((Strings.Get("Operation_RenameId"), false));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Removing_a_class_from_the_whole_book_runs_behind_a_checkpoint()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            string tree = temp.Combine("tree");
            TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
            string chapterPath = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(
                chapterPath,
                File.ReadAllText(chapterPath).Replace("<p>Hello, world.</p>", "<p class=\"c1\">Hello, world.</p>", StringComparison.Ordinal));
            File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.c1 { color: red }\n");
            string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
            MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
            Book book = _disposables.Add(new ImportEpub(epub).GetBook());
            sut.LoadBook(book, epub);
            ICodeTabHost host = sut;
            Runs runs = new(sut);

            // Act
            ClassRemover? remover = await host.PrepareClassRemovalAsync();

            // Assert
            remover.Should().NotBeNull();

            // Act
            await host.ApplyClassRemoval(remover!.Remove("c1"), runs.Runner);

            // Assert
            book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().NotContain("class=");
            book.GetCssResources().Single().GetText().Should().NotContain(".c1");
            runs.Windows.Should().ContainSingle().Which.Should().Be((Strings.Get("Operation_RemoveClass"), false));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }
}
