using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Diff;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="CheckpointHistory.UndoImpact"/> / <see cref="CheckpointHistory.RedoImpact"/> — the files a move
/// between states removes, brings back or renames, and which removed files carry editor edits
/// (<see cref="CheckpointHistory.MarkEdited"/>).
/// </summary>
public sealed class CheckpointHistoryImpactTests
{
    private static Book Load(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static HtmlResource Chapter(Book book) => book.GetHtmlResourcesExcludingNav().First();

    [Theory]
    [AutoData]
    public void UndoImpact_lists_a_file_created_after_the_checkpoint_as_removed(string message)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp);
        history.Open(book);
        history.AddCheckpoint(book, message);

        HtmlResource created = book.CreateEmptyHtmlFile();

        history.UndoImpact(book).Should().Equal(
            new CheckpointFileImpact(CheckpointFileChange.Removed, created.BookPath, null, LosesEdits: false));
    }

    [Theory]
    [AutoData]
    public void UndoImpact_flags_a_removed_file_edited_after_the_checkpoint(string message)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp);
        history.Open(book);
        history.AddCheckpoint(book, message);
        HtmlResource created = book.CreateEmptyHtmlFile();

        history.MarkEdited(created.BookPath);

        history.UndoImpact(book).Should().ContainSingle().Which.LosesEdits.Should().BeTrue();
    }

    [Theory]
    [AutoData]
    public void UndoImpact_lists_a_deleted_file_as_restored_and_a_renamed_one_as_renamed(string message, string newName)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp);
        HtmlResource extra = book.CreateEmptyHtmlFile();
        HtmlResource chapter = Chapter(book);
        string extraPath = extra.BookPath;
        string chapterPath = chapter.BookPath;
        history.Open(book);
        history.AddCheckpoint(book, message);

        book.GetFolderKeeper().BulkRemoveResources(new Resource[] { extra });
        book.GetFolderKeeper().BulkRenameResources(new Resource[] { chapter }, new[] { newName + ".xhtml" });

        history.UndoImpact(book).Should().BeEquivalentTo(new[]
        {
            new CheckpointFileImpact(CheckpointFileChange.Restored, extraPath, null, false),
            new CheckpointFileImpact(CheckpointFileChange.Renamed, chapter.BookPath, chapterPath, false),
        });
    }

    [Theory]
    [AutoData]
    public void Edits_stay_attributed_to_their_span_across_a_later_checkpoint(string operation, string manual)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp);
        history.Open(live);
        history.AddCheckpoint(live, operation);
        HtmlResource created = live.CreateEmptyHtmlFile();
        history.MarkEdited(created.BookPath);
        history.AddCheckpoint(live, manual);

        history.UndoImpact(live).Should().BeEmpty("the manual checkpoint already contains the created file");
        Book atManual = history.Undo(live)!;
        live.Dispose();

        history.UndoImpact(atManual).Should().ContainSingle()
            .Which.Should().Be(new CheckpointFileImpact(CheckpointFileChange.Removed, created.BookPath, null, LosesEdits: true));
        atManual.Dispose();
    }

    [Theory]
    [AutoData]
    public void RedoImpact_flags_only_edits_made_after_moving_back(string message)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp);
        HtmlResource extra = live.CreateEmptyHtmlFile();
        string extraPath = extra.BookPath;
        history.Open(live);
        history.MarkEdited(extraPath);
        history.AddCheckpoint(live, message);
        live.GetFolderKeeper().BulkRemoveResources(new Resource[] { extra });
        Book before = history.Undo(live)!;
        live.Dispose();

        history.RedoImpact(before).Should().ContainSingle()
            .Which.LosesEdits.Should().BeFalse("the edit was made before the operation that removed the file");

        history.MarkEdited(extraPath);

        history.RedoImpact(before).Should().ContainSingle()
            .Which.Should().Be(new CheckpointFileImpact(CheckpointFileChange.Removed, extraPath, null, LosesEdits: true));
        before.Dispose();
    }

    [Theory]
    [AutoData]
    public void Rewind_keeps_the_edits_made_before_the_withdrawn_checkpoint(string operation, string withdrawn)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp);
        history.Open(book);
        history.AddCheckpoint(book, operation);
        HtmlResource created = book.CreateEmptyHtmlFile();
        history.MarkEdited(created.BookPath);
        history.AddCheckpoint(book, withdrawn);

        history.Rewind();

        history.UndoImpact(book).Should().ContainSingle().Which.LosesEdits.Should().BeTrue();
    }

    [Fact]
    public void Impacts_are_empty_when_there_is_nowhere_to_move()
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp);
        history.Open(book);

        history.UndoImpact(book).Should().BeEmpty();
        history.RedoImpact(book).Should().BeEmpty();
    }

    [Theory]
    [AutoData]
    public void CompareFileSets_skips_files_whose_content_only_changed(string message, string edit)
    {
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp);
        history.Open(book);
        history.AddCheckpoint(book, message);
        HtmlResource chapter = Chapter(book);
        chapter.SetText(chapter.GetText().Replace("</body>", $"<p>{edit}</p></body>", System.StringComparison.Ordinal));
        HtmlResource created = book.CreateEmptyHtmlFile();
        CheckpointHistory.Freeze(book);

        BookComparer.CompareFileSets(history.States[0].FolderPath, history.Current.FolderPath)
            .Should().ContainSingle()
            .Which.Should().Match<BookFileDiff>(d => d.Change == BookFileChange.Added && d.RightPath == created.BookPath);
    }
}
