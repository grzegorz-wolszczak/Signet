using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The footnote services of the main window for the Code Editor "Footnotes" submenu (<see cref="ICodeTabHost"/>).</summary>
public sealed class MainWindowFootnotesTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) OpenMinimalChapter(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, book, chapter);
    }

    // The minimal chapter with a footnote of other origin after "Hello" (marked [7], its note at the end of the body).
    private (MainWindowViewModel Sut, HtmlResource Chapter) OpenChapterWithForeignFootnote(TempDir temp, bool openTab = true)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        string text = chapter.GetText().Replace(
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"",
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"",
            StringComparison.Ordinal);
        text = text.Insert(text.IndexOf("Hello", StringComparison.Ordinal) + 5, "<a epub:type=\"noteref\" href=\"#n7\" id=\"n7-back\">[7]</a>");
        int body = text.LastIndexOf("</body>", StringComparison.Ordinal);
        chapter.SetText(text.Insert(body, "<aside epub:type=\"footnote\" id=\"n7\"><p><a href=\"#n7-back\">7.</a> Other note.</p></aside>\n"));
        sut.LoadBook(book, epub);
        if (openTab)
        {
            sut.Tabs.OpenResources(new Resource[] { chapter });
        }

        return (sut, chapter);
    }

    private static int AfterHello(HtmlResource chapter) => chapter.GetText().IndexOf("Hello", StringComparison.Ordinal) + 5;

    [Fact]
    public void A_footnote_is_added_edited_into_the_global_notes_and_removed_with_its_notes_file()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        int offset = AfterHello(chapter);

        // Act & Assert
        host.GetFootnoteState(chapter, offset).Should().Be(new FootnoteMenuState(null, FootnoteError.None, false));
        host.GetFootnoteNumbers(chapter, offset).Should().Be((1, 1));

        // Act
        host.AddFootnote(chapter, offset, "A <i>note</i>.", FootnotePlacement.Local);

        // Assert
        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\"").And.Contain("A <i>note</i>.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteAddedLocal"));
        sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());

        // Act
        FootnoteMenuState onReference = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[1]", StringComparison.Ordinal))!;

        // Assert
        onReference.Hit!.Side.Should().Be(FootnoteSide.Reference);
        onReference.HasFootnotes.Should().BeTrue();

        // Act
        FootnoteEditState edit = host.GetFootnoteEdit(onReference.Hit)!;

        // Assert
        edit.Content.Should().Be(new FootnoteContent("A <i>note</i>.", false));
        (edit.LocalNumber, edit.GlobalNumber).Should().Be((1, 1));
        edit.ReferenceText[edit.ReferenceStart..edit.ReferenceEnd].Should().StartWith("<a class=\"signet-fn-ref\"").And.EndWith("[1]</a>");

        // Act
        host.EditFootnote(onReference.Hit, "A <b>new</b> note.", FootnotePlacement.Global);

        HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
        notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);

        // Assert
        notes.GetText().Should().Contain("A <b>new</b> note.");
        chapter.GetText().Should().NotContain("A <i>note</i>.").And.NotContain("A <b>new</b> note.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteMovedGlobal"));
        sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

        // Act
        FootnoteMenuState onNote = host.GetFootnoteState(notes, notes.GetText().IndexOf("A <b>new", StringComparison.Ordinal))!;

        // Assert
        onNote.Hit!.Side.Should().Be(FootnoteSide.Note);

        // Act
        host.GoToFootnote(onNote.Hit);

        // Assert
        sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

        // Act
        host.RemoveFootnote(onNote.Hit);

        // Assert
        book.GetHtmlResources().Should().NotContain(notes);
        chapter.GetText().Should().NotContain("<a class=\"signet-fn-ref\"").And.Contain("Hello, world.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteRemoved"));
    }

    [Fact]
    public void Editing_only_the_text_keeps_the_footnote_in_place_and_an_invalid_text_changes_nothing()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        // Act
        host.AddFootnote(chapter, AfterHello(chapter), "Old text.", FootnotePlacement.Local);
        FootnoteHit hit = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("Old text.", StringComparison.Ordinal))!.Hit!;

        // Assert
        hit.Side.Should().Be(FootnoteSide.Note);

        // Act & Assert
        host.ValidateFootnoteEdit(hit.Footnote, "a < b").Should().Be(FootnoteError.InvalidContent);

        // Act
        host.EditFootnote(hit, "a < b", FootnotePlacement.Local);

        // Assert
        chapter.GetText().Should().Contain("Old text.");
        sut.StatusMessage.Should().Be(
            Strings.Format("Status_FootnoteNotEdited", FootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));

        // Arrange
        int scrolledTo = -1;
        sut.ActiveCodeTab!.ScrollToOffsetRequested += offset => scrolledTo = offset;

        // Act
        host.EditFootnote(hit, "New text.", FootnotePlacement.Local);

        // Assert
        chapter.GetText().Should().Contain("1.</a> New text.</p>").And.NotContain("Old text.");
        FootnoteEditor.FindGlobalFile(book).Should().BeNull();
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteEdited"));
        scrolledTo.Should().Be(chapter.GetText().IndexOf("<aside", StringComparison.Ordinal), "the caret goes back to the note");
    }

    [Fact]
    public void A_reference_added_to_a_footnote_makes_it_shared_and_removing_it_makes_it_ordinary_again()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        host.AddFootnote(chapter, AfterHello(chapter), "Shared note.", FootnotePlacement.Local);
        int end = chapter.GetText().IndexOf("world", StringComparison.Ordinal) + 5;

        // Act & Assert
        host.GetFootnoteState(chapter, end)!.ReferenceTargetCount.Should().Be(1);

        // Act
        ReferenceTargetList targets = host.GetFootnoteReferenceTargets(chapter, end)!;

        // Assert
        targets.Targets.Should().ContainSingle().Which.Text.Should().Be("Shared note.");

        // Act
        host.AddFootnoteReference(chapter, end, targets.Targets[targets.Nearest].Footnote);

        // Assert
        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1a-ref\">[1a]</a>")
            .And.Contain("world<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1b-ref\">[1b]</a>")
            .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. Shared note.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteReferenceAdded"));
        sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());

        // Act
        FootnoteMenuState onSecond = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[1b]", StringComparison.Ordinal))!;
        onSecond.Hit!.Footnote.IsShared.Should().BeTrue();
        host.RemoveFootnoteReference(onSecond.Hit);

        // Assert
        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
            .And.NotContain("[1b]").And.Contain("<a href=\"#fn-1-ref\">1.</a> Shared note.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteReferenceRemoved"));
    }

    [Fact]
    public void Shared_footnotes_in_another_style_open_the_unifying_dialog_and_unifying_rewrites_them()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        // Act & Assert
        sut.PrepareFootnoteLetters().Should().BeNull();

        // Assert
        sut.StatusMessage.Should().Be(Strings.Get("Status_NoSharedFootnotes"));

        // Act
        host.AddFootnote(chapter, AfterHello(chapter), "Shared note.", FootnotePlacement.Local);
        int end = chapter.GetText().IndexOf("world", StringComparison.Ordinal) + 5;
        FootnoteEditor.AddReference(book, chapter, end, FootnoteScanner.Scan(book).Single(), out _,
            FootnoteOptions.Default with { LetterStyle = FootnoteLetterStyle.UpperLatin }).Should().Be(FootnoteError.None);
        sut.ActiveCodeTab!.Reload();

        // Assert
        sut.CountFootnoteLetterMismatches().Should().Be(1, "Preferences say a, b");

        // Act
        FootnoteLettersViewModel letters = sut.PrepareFootnoteLetters()!;

        // Assert
        letters.Style.Should().Be(FootnoteLetterStyle.LowerLatin);
        letters.Notes.Should().ContainSingle();

        // Act
        letters.UnifyCommand.Execute(null);

        // Assert
        chapter.GetText().Should().Contain("[1a]</a>").And.Contain("[1b]</a>").And.NotContain("[1A]");
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnoteLettersUnified", 1, Strings.Get("FootnoteLetterStyle_LowerLatin")));
        sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());
        sut.CountFootnoteLetterMismatches().Should().Be(0);
        sut.PrepareFootnoteLetters().Should().BeNull();
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnoteLettersAlreadyUnified", Strings.Get("FootnoteLetterStyle_LowerLatin")));
    }

    [Fact]
    public void The_removal_confirmation_is_a_setting()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource _) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        // Act & Assert
        host.ConfirmFootnoteRemoval.Should().BeTrue("removing asks by default");

        // Act
        host.ConfirmFootnoteRemoval = false;

        // Assert
        host.ConfirmFootnoteRemoval.Should().BeFalse();
    }

    [Fact]
    public void An_invalid_footnote_text_adds_nothing_and_says_why()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        string before = chapter.GetText();

        // Act & Assert
        host.ValidateFootnoteContent("a < b").Should().Be(FootnoteError.InvalidContent);

        // Act
        host.AddFootnote(chapter, AfterHello(chapter), "a < b", FootnotePlacement.Local);

        // Assert
        chapter.GetText().Should().Be(before);
        sut.StatusMessage.Should().Be(
            Strings.Format("Status_FootnoteNotAdded", FootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));
    }

    [Fact]
    public void The_footnote_state_inside_a_tag_has_the_reason_and_no_footnote()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        int insideTag = chapter.GetText().IndexOf("<p>Hello", StringComparison.Ordinal) + 1;

        // Act & Assert
        host.GetFootnoteState(chapter, insideTag).Should().Be(new FootnoteMenuState(null, FootnoteError.InsideTag, false));
    }

    [Fact]
    public void Renumbering_numbered_footnotes_reports_that_the_numbering_is_up_to_date()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        // Act
        host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);

        host.RenumberFootnotes();

        // Assert
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnotesUpToDate"));
    }

    [Fact]
    public void The_placement_of_the_last_added_footnote_is_the_next_default()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        // Act
        host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Global);

        // Assert
        host.LastFootnotePlacement.Should().Be(FootnotePlacement.Global);

        // Act
        host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);

        // Assert
        host.LastFootnotePlacement.Should().Be(FootnotePlacement.Local);
    }

    [Fact]
    public void Tools_renumber_footnotes_renumbers_like_the_context_menu()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);

        // Act
        sut.Actions.Require(AppActionIds.RenumberFootnotes).Execute(null);

        // Assert
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesUpToDateForeignSkipped", 1));
    }

    [Fact]
    public void The_footnote_state_counts_the_footnotes_of_other_origin()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
        ICodeTabHost host = sut;

        // Act
        FootnoteMenuState state = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[7]", StringComparison.Ordinal))!;

        // Assert
        state.Hit!.Footnote.IsSignet.Should().BeFalse();
        state.ForeignCount.Should().Be(1);
    }

    [Fact]
    public void Renumbering_leaves_footnotes_of_other_origin_as_they_are_and_warns_about_them()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
        ICodeTabHost host = sut;
        int afterWorld = chapter.GetText().IndexOf(", world", StringComparison.Ordinal) + ", world".Length;

        // Act
        host.AddFootnote(chapter, afterWorld, "Note.", FootnotePlacement.Local);

        host.RenumberFootnotes();

        // Assert
        chapter.GetText().Should().Contain(">[7]</a>").And.Contain(">7.</a> Other note.")
            .And.Contain("id=\"fn-2-ref\">[2]</a>", "the footnote of other origin before it counts");
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesUpToDateForeignSkipped", 1));
        sut.IsStatusMessageWarning.Should().BeTrue();
    }

    [Fact]
    public void The_preview_of_a_footnote_changes_nothing()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        string before = chapter.GetText();
        int offset = AfterHello(chapter);

        // Act
        FootnoteMarkup preview = host.PreviewFootnote(chapter, offset, offset, "One.\n\nTwo.", FootnotePlacement.Local, 1, null)!;

        // Assert
        preview.Reference.Should().Contain("href=\"#fn-1\"");
        preview.Note.Should().Contain("<p class=\"signet-fn-p\">Two.</p>");
        preview.Page.Should().Contain("<p>Hello" + preview.Reference);
        chapter.GetText().Should().Be(before);

        // Act & Assert
        host.PreviewFootnote(chapter, offset, offset, "a < b", FootnotePlacement.Local, 1, null).Should().BeNull();
    }

    [Fact]
    public void Tools_convert_footnotes_asks_the_view_for_the_dialog()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);
        bool raised = false;
        sut.FootnoteConversionRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.ConvertFootnotes).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public async Task Without_footnotes_of_other_origin_there_is_no_conversion_dialog()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource _) = OpenMinimalChapter(temp);

        // Act & Assert
        (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true))).Should().BeNull();

        // Assert
        sut.StatusMessage.Should().Be(Strings.Get("Status_NoForeignFootnotes"));
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
    public async Task The_conversion_runs_under_the_progress_runner_and_reports_its_progress()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        // No tab is open: its editor document belongs to the thread that made it, and the test continues on another after an await.
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp, openTab: false);
        sut.Tabs.CloseAllTabs();
        RecordingProgress progress = new();
        List<bool> cancellable = new();
        ProgressRunner runner = (_, work, canCancel) =>
        {
            cancellable.Add(canCancel);
            return work(progress, CancellationToken.None);
        };
        FootnoteConversionViewModel dialog = (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true), runner))!;
        dialog.SelectAllCommand.Execute(null);

        // Act
        await dialog.ConvertCommand.ExecuteAsync(null);

        // Assert
        // The conversion (a checkpoint was taken, so it can be cancelled) and the analysis after it (it cannot).
        cancellable.Should().Equal(true, false);
        chapter.GetText().Should().Contain("signet-fn-ref");
        dialog.HasProblems.Should().BeFalse();
        List<string?> messages = progress.Reports.Select(r => r.Message).ToList();
        messages.Should().Contain(Strings.Format("FootnoteConversion_Progress_Converting", 1, 1));
        messages.Should().Contain(Strings.Get("FootnoteConversion_Progress_Checking"));
        messages.Should().Contain(Strings.Get("FootnoteConversion_Analyzing"));
        progress.Reports.Select(r => r.Fraction).OfType<double>().Should().BeInAscendingOrder();
        sut.BookBrowser.SuspendRebuilds.Should().BeFalse("the tree follows the book again when the work is done");
    }

    [Fact]
    public async Task Cancelling_the_conversion_brings_the_book_back_and_leaves_the_dialog_as_it_was()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp, openTab: false);
        sut.Tabs.CloseAllTabs();
        string before = chapter.GetText();
        int states = sut.CheckpointHistory.States.Count;
        using CancellationTokenSource pressed = new();
        ProgressRunner runner = (_, work, cancellable) =>
        {
            cancellable.Should().BeTrue();
            pressed.Cancel();
            return work(new RecordingProgress(), pressed.Token);
        };
        FootnoteConversionViewModel dialog = (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true), runner))!;
        dialog.SelectAllCommand.Execute(null);

        // Act
        await dialog.ConvertCommand.ExecuteAsync(null);

        // Assert
        Book restored = ((IBookWorkspace)sut).CurrentBook!;
        restored.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Be(before);
        sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the cancelled conversion is forgotten");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteConversionCancelled"));
        dialog.HasProblems.Should().BeFalse();
        dialog.Items.Should().HaveCount(1, "the dialog stays as it was");
        dialog.IsConverting.Should().BeFalse();
        sut.BookBrowser.SuspendRebuilds.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public void Abandoning_a_checkpoint_brings_the_book_back_and_forgets_the_failed_state(string operation, string edit)
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        string before = chapter.GetText();
        int states = sut.CheckpointHistory.States.Count;

        // Act & Assert
        sut.AddCheckpointBefore(operation).Should().BeTrue();
        chapter.SetText(before.Replace("Hello", edit, StringComparison.Ordinal));

        // Act
        sut.AbandonCheckpoint().Should().BeTrue();

        Book restored = ((IBookWorkspace)sut).CurrentBook!;

        // Assert
        restored.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Be(before);
        sut.CheckpointHistory.States.Should().HaveCount(states);
        sut.CheckpointHistory.CanRedo.Should().BeFalse();

        // Act & Assert
        sut.AbandonCheckpoint().Should().BeFalse("there is no checkpoint just created any more");
    }
}
