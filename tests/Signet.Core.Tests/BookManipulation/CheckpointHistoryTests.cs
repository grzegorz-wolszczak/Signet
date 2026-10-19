using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="CheckpointHistory"/> — per-session book checkpoints.</summary>
public sealed class CheckpointHistoryTests
{
    private static Book Load(TempDir temp, string corpusDir)
    {
        string epub = EpubBuilder.BuildInto(corpusDir, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static HtmlResource Chapter(Book book) => book.GetHtmlResourcesExcludingNav().First();

    [Fact]
    public void Open_takes_ownership_of_the_working_folder_so_disposing_the_book_keeps_it()
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book book = Load(temp, CorpusPaths.Epub3Minimal);
        string folder = book.GetFolderKeeper().MainFolderPath;

        // Act
        history.Open(book);
        book.Dispose();

        // Assert
        Directory.Exists(folder).Should().BeTrue();
        history.States.Should().ContainSingle();
        history.Position.Should().Be(0);
        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse();

        // Act
        history.Clear();

        // Assert
        Directory.Exists(folder).Should().BeFalse();
        history.IsOpen.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void AddCheckpoint_freezes_a_copy_of_the_current_state_under_the_given_name(string message, string edit)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(book);
        HtmlResource chapter = Chapter(book);
        chapter.SetText(chapter.GetText().Replace("</body>", $"<p>{edit}</p></body>", StringComparison.Ordinal));

        // Act
        history.AddCheckpoint(book, message);

        // Assert
        history.States.Should().HaveCount(2);
        history.Position.Should().Be(1);
        history.CanUndo.Should().BeTrue();
        history.UndoMessage.Should().Be(message);
        history.Current.Message.Should().BeNull();
        history.Current.FolderPath.Should().Be(book.GetFolderKeeper().MainFolderPath);

        string frozenChapter = Path.Combine(history.States[0].FolderPath, chapter.BookPath.Replace('/', Path.DirectorySeparatorChar));
        File.ReadAllText(frozenChapter).Should().Contain(edit, "the checkpoint includes editor changes that were not saved before");
    }

    [Theory]
    [AutoData]
    public void Undo_returns_the_checkpointed_state_and_Redo_brings_back_later_edits(string message, string edit)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, message);
        HtmlResource chapter = Chapter(live);
        chapter.SetText(chapter.GetText().Replace("</body>", $"<p>{edit}</p></body>", StringComparison.Ordinal));

        // Act
        Book? before = history.Undo(live);
        live.Dispose();

        // Assert
        before.Should().NotBeNull();
        history.Position.Should().Be(0);
        history.CanRedo.Should().BeTrue();
        Chapter(before!).GetText().Should().NotContain(edit);

        // Act
        Book? after = history.Redo(before!);
        before!.Dispose();

        // Assert
        after.Should().NotBeNull();
        history.Position.Should().Be(1);
        Chapter(after!).GetText().Should().Contain(edit);
        after!.Dispose();
    }

    [Theory]
    [AutoData]
    public void Edits_after_reverting_change_the_reverted_state(string first, string second, string edit)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, first);
        history.AddCheckpoint(live, second);

        // Act
        Book reverted = history.RevertTo(live, history.States[0])!;
        live.Dispose();

        // Arrange
        HtmlResource chapter = Chapter(reverted);
        chapter.SetText(chapter.GetText().Replace("</body>", $"<p>{edit}</p></body>", StringComparison.Ordinal));

        // Act
        Book other = history.RevertTo(reverted, history.States[2])!;
        reverted.Dispose();
        Book back = history.RevertTo(other, history.States[0])!;
        other.Dispose();

        // Assert
        Chapter(back).GetText().Should().Contain(edit, "an edit after reverting changes the folder of the reverted state");
        history.States.Should().HaveCount(3, "moving between states does not cut anything off");
        back.Dispose();
    }

    [Theory]
    [AutoData]
    public void New_checkpoint_after_undo_discards_the_states_after_the_current_one(string first, string second, string third)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, first);
        history.AddCheckpoint(live, second);
        string discardedFolder = history.States[2].FolderPath;

        // Act
        Book reverted = history.Undo(live)!;
        live.Dispose();
        history.AddCheckpoint(reverted, third);

        // Assert
        history.States.Select(s => s.Message).Should().Equal(first, third, null);
        history.Position.Should().Be(2);
        history.CanRedo.Should().BeFalse();
        Directory.Exists(discardedFolder).Should().BeFalse();
        reverted.Dispose();
    }

    [Theory]
    [AutoData]
    public void Rewind_removes_the_checkpoint_just_created_and_restores_the_previous_name(string first, string second)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, first);

        // Act
        Book reverted = history.Undo(live)!;
        live.Dispose();

        history.AddCheckpoint(reverted, second);

        // Arrange
        string frozenFolder = history.States[0].FolderPath;

        // Act & Assert
        history.Rewind().Should().BeTrue();

        history.States.Should().ContainSingle();

        // Assert
        history.Current.Message.Should().Be(first, "the restored state gets its name back");
        Directory.Exists(frozenFolder).Should().BeFalse();
        history.Rewind().Should().BeFalse("rewind works only once, right after a checkpoint is created");
        reverted.Dispose();
    }

    [Theory]
    [AutoData]
    public void Rewind_does_nothing_after_moving_between_states(string first, string second)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, first);
        history.AddCheckpoint(live, second);

        // Act
        Book before = history.Undo(live)!;
        live.Dispose();
        Book after = history.Redo(before)!;
        before.Dispose();

        // Act & Assert
        history.Rewind().Should().BeFalse();

        // Assert
        history.States.Should().HaveCount(3);
        after.Dispose();
    }

    [Theory]
    [AutoData]
    public void Abandon_brings_back_the_checkpoint_and_deletes_the_state_after_it(string first, string second, string edit)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, first);

        // Act
        Book reverted = history.Undo(live)!;
        live.Dispose();
        string original = Chapter(reverted).GetText();
        history.AddCheckpoint(reverted, second);
        string failedFolder = history.Current.FolderPath;
        Chapter(reverted).SetText(original.Replace("</body>", $"<p>{edit}</p></body>", StringComparison.Ordinal));

        using Book restored = history.Abandon(reverted)!;
        reverted.Dispose();

        // Assert
        Chapter(restored).GetText().Should().Be(original);
        history.States.Should().ContainSingle("the state of the failed operation is not kept for Redo");
        history.CanRedo.Should().BeFalse();
        history.Current.Message.Should().Be(first, "the restored state gets its name back");
        Directory.Exists(failedFolder).Should().BeFalse();
        history.Rewind().Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void Abandon_does_nothing_without_a_checkpoint_just_created(string first, string second)
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, first);
        history.AddCheckpoint(live, second);

        // Act
        Book before = history.Undo(live)!;
        live.Dispose();

        // Act & Assert
        history.Abandon(before).Should().BeNull();

        // Assert
        history.States.Should().HaveCount(3);
        before.Dispose();
    }

    [Fact]
    public void Oldest_states_are_dropped_over_the_limit()
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new(maxStates: 3);
        using Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);

        // Act
        history.AddCheckpoint(live, "1");

        // Arrange
        string oldest = history.States[0].FolderPath;

        // Act
        history.AddCheckpoint(live, "2");
        history.AddCheckpoint(live, "3");

        // Assert
        history.States.Select(s => s.Message).Should().Equal("2", "3", null);
        history.Position.Should().Be(2);
        Directory.Exists(oldest).Should().BeFalse();
    }

    [Fact]
    public void RevertTo_the_current_state_does_nothing()
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);

        // Act & Assert
        history.RevertTo(live, history.Current).Should().BeNull();
    }

    [Fact]
    public void AddCheckpoint_rejects_a_book_that_does_not_work_in_the_current_state()
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book live = Load(temp, CorpusPaths.Epub3Minimal);
        using Book other = Load(temp, CorpusPaths.Epub2Minimal);
        history.Open(live);

        // Act
        Action act = () => history.AddCheckpoint(other, "x");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reverting_keeps_obfuscated_fonts_intact_and_their_algorithms()
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        Book live = Load(temp, CorpusPaths.Epub3ObfuscatedFonts);
        FontResource[] fonts = live.GetAllResources().OfType<FontResource>().OrderBy(f => f.BookPath).ToArray();

        fonts.Should().NotBeEmpty();
        fonts.Should().OnlyContain(f => f.ObfuscationAlgorithm.Length > 0);

        var expected = fonts.Select(f => (f.BookPath, f.ObfuscationAlgorithm, Bytes: File.ReadAllBytes(f.FullPath))).ToArray();
        history.Open(live);
        history.AddCheckpoint(live, "x");

        // Act
        Book reverted = history.Undo(live)!;
        live.Dispose();

        var actual = reverted.GetAllResources().OfType<FontResource>().OrderBy(f => f.BookPath)
            .Select(f => (f.BookPath, f.ObfuscationAlgorithm, Bytes: File.ReadAllBytes(f.FullPath))).ToArray();

        // Assert
        actual.Should().HaveCount(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            actual[i].BookPath.Should().Be(expected[i].BookPath);
            actual[i].ObfuscationAlgorithm.Should().Be(expected[i].ObfuscationAlgorithm);
            actual[i].Bytes.Should().Equal(expected[i].Bytes, "the font must not be processed again when loaded from the folder");
        }

        reverted.Dispose();
    }

    [Fact]
    public void Exported_epub_does_not_contain_the_state_file()
    {
        // Arrange
        using TempDir temp = new();
        using CheckpointHistory history = new();
        using Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, "x");

        File.Exists(Path.Combine(live.GetFolderKeeper().MainFolderPath, BookStateFile.FileName)).Should().BeTrue();
        // A checkpoint made before the state was stored as XML still holds the JSON file.
        File.WriteAllText(Path.Combine(live.GetFolderKeeper().MainFolderPath, BookStateFile.LegacyFileName), "{}");

        string exported = temp.Combine("out.epub");

        // Act
        new ExportEpub(live).WriteBook(exported, stampMetadata: false);

        using ZipArchive zip = ZipFile.OpenRead(exported);

        // Assert
        zip.Entries.Select(e => e.FullName).Should().NotContain(BookStateFile.FileName)
            .And.NotContain(BookStateFile.LegacyFileName);
    }

    [Fact]
    public void Dispose_removes_all_state_folders()
    {
        // Arrange
        using TempDir temp = new();
        CheckpointHistory history = new();
        using Book live = Load(temp, CorpusPaths.Epub3Minimal);
        history.Open(live);
        history.AddCheckpoint(live, "x");
        string[] folders = history.States.Select(s => s.FolderPath).ToArray();

        // Act
        history.Dispose();

        // Assert
        folders.Should().OnlyContain(f => !Directory.Exists(f));
    }
}
