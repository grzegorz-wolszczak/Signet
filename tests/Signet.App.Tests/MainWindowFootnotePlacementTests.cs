using System;
using System.Collections.Generic;
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
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>"Change Footnote Placement" in the main window: preparing the dialog and moving the footnotes under a progress window.</summary>
public sealed class MainWindowFootnotePlacementTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    // The minimal chapter with a local footnote of Signet after "Hello" (no tab is open: its editor document belongs to the thread
    // that made it, and the tests continue on another after an await).
    private (MainWindowViewModel Sut, HtmlResource Chapter) OpenChapterWithFootnote(TempDir temp, bool withFootnote = true)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        if (withFootnote)
        {
            FootnoteEditor.Add(book, chapter, chapter.GetText().IndexOf("Hello", StringComparison.Ordinal) + 5, "A note.", FootnotePlacement.Local)
                .Should().Be(FootnoteError.None);
        }

        sut.LoadBook(book, epub);
        sut.Tabs.CloseAllTabs();
        return (sut, chapter);
    }

    private sealed class RecordingProgress : IProgress<WorkProgress>
    {
        public List<WorkProgress> Reports { get; } = new();

        public void Report(WorkProgress value)
        {
            lock (Reports)
            {
                Reports.Add(value);
            }
        }
    }

    [Fact]
    public void The_action_asks_the_view_for_the_dialog()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithFootnote(temp);
        bool raised = false;
        sut.FootnotePlacementRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.ChangeFootnotePlacement).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public async Task Without_footnotes_of_Signet_there_is_no_dialog()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithFootnote(temp, withFootnote: false);

        // Act & Assert
        (await sut.PrepareFootnotePlacementAsync()).Should().BeNull();

        // Assert
        sut.StatusMessage.Should().Be(Strings.Get("Status_NoSignetFootnotes"));
    }

    [Fact]
    public async Task The_listing_reports_a_file_scanned_of_the_books_total()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithFootnote(temp);
        RecordingProgress analysisProgress = new();

        // Act
        FootnotePlacementViewModel? dialog = await sut.PrepareFootnotePlacementAsync(analysisProgress: analysisProgress);

        // Assert
        dialog.Should().NotBeNull();
        analysisProgress.Reports.Should().NotBeEmpty();
        analysisProgress.Reports[^1].Fraction.Should().Be(1);
        analysisProgress.Reports.Should().Contain(r => r.Message != null && r.Message.StartsWith("Analyzing the footnotes: file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_footnotes_move_under_the_progress_runner_behind_a_checkpoint()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithFootnote(temp);
        RecordingProgress progress = new();
        List<(string Title, bool Cancellable)> runs = new();
        ProgressRunner runner = (title, work, cancellable) =>
        {
            runs.Add((title, cancellable));
            return work(progress, CancellationToken.None);
        };
        FootnotePlacementViewModel dialog = (await sut.PrepareFootnotePlacementAsync(runner))!;
        int states = sut.CheckpointHistory.States.Count;
        dialog.Rows.Should().ContainSingle().Which.Item.Placement.Should().Be(FootnotePlacement.Local);
        dialog.SelectAllCommand.Execute(null);
        bool done = false;
        dialog.Done += (_, _) => done = true;

        // Act
        await dialog.ApplyCommand.ExecuteAsync(null);

        // Assert
        done.Should().BeTrue();
        runs.Should().Equal((Strings.Get("FootnotePlacementWindow_Title"), true));
        Book moved = ((IBookWorkspace)sut).CurrentBook!;
        FootnoteScanner.Scan(moved).Should().ContainSingle().Which.Placement.Should().Be(FootnotePlacement.Global);
        FootnoteEditor.FindGlobalFile(moved)!.Filename.Should().Be(FootnoteEditor.GlobalFileName);
        chapter.GetText().Should().Contain("#en-1", "the book was edited in place");
        sut.CheckpointHistory.States.Should().HaveCount(states + 1, "a checkpoint was taken before the change");
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotePlacementChanged", 1));
        progress.Reports.Select(r => r.Phase).Should().OnlyContain(p => p != null && p.Count == 8, "every report names its phase of eight: three stages and five of the renumbering");
        progress.Reports.Select(r => r.Phase!.Index).Should().BeInAscendingOrder().And.Subject.Distinct().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        progress.Reports.Select(r => r.Phase!.Name).Distinct().Should().Equal(
            Strings.Get("FootnotePlacement_Phase_Preparing"), Strings.Get("FootnotePlacement_Phase_Moving"),
            Strings.Get("FootnotePlacement_Phase_Finishing"), Strings.Get("FootnoteRenumber_Phase_Scanning"),
            Strings.Get("FootnoteRenumber_Phase_Numbering"), Strings.Get("FootnoteRenumber_Phase_Writing"),
            Strings.Get("FootnoteRenumber_Phase_Sorting"), Strings.Get("FootnoteRenumber_Phase_Styling"));
        progress.Reports.Where(r => r.Phase!.Index == 3).Should().OnlyContain(r => r.Fraction == null && r.Message == null, "the writing cannot tell how far it is");
        progress.Reports.Where(r => r.Phase!.Index == 5).Should().Contain(r => r.Message != null && r.Message.StartsWith("footnote", StringComparison.Ordinal), "the renumbering counts the footnotes");
        progress.Reports.Where(r => r.Phase!.Index == 2).Should().Contain(r => r.Message == Strings.Format("Progress_Detail_Footnote", 0, 1), "the moving phase counts the notes");
        sut.BookBrowser.SuspendRebuilds.Should().BeFalse("the tree follows the book again when the work is done");
    }

    [Fact]
    public async Task Cancelling_brings_the_book_back_and_leaves_the_dialog_open()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithFootnote(temp);
        string before = chapter.GetText();
        int states = sut.CheckpointHistory.States.Count;
        using CancellationTokenSource pressed = new();
        ProgressRunner runner = (_, work, cancellable) =>
        {
            cancellable.Should().BeTrue();
            pressed.Cancel();
            return work(new RecordingProgress(), pressed.Token);
        };
        FootnotePlacementViewModel dialog = (await sut.PrepareFootnotePlacementAsync(runner))!;
        dialog.SelectAllCommand.Execute(null);
        bool done = false;
        dialog.Done += (_, _) => done = true;

        // Act
        await dialog.ApplyCommand.ExecuteAsync(null);

        // Assert
        done.Should().BeFalse();
        ((IBookWorkspace)sut).CurrentBook!.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Be(before);
        sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the cancelled change is forgotten");
        sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelledRestored", Strings.Get("FootnotePlacementWindow_Title")));
        dialog.Rows.Should().ContainSingle().Which.IsChecked.Should().BeTrue("the dialog stays as it was");
        sut.BookBrowser.SuspendRebuilds.Should().BeFalse();
    }
}
