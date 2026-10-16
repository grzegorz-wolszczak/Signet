using System;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Actions;
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
public sealed class MainWindowFootnotesTests
{
    private static (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) OpenMinimalChapter(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = new();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, book, chapter);
    }

    // The minimal chapter with a footnote of other origin after "Hello" (marked [7], its note at the end of the body).
    private static (MainWindowViewModel Sut, HtmlResource Chapter) OpenChapterWithForeignFootnote(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = new();
        Book book = new ImportEpub(epub).GetBook();
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        string text = chapter.GetText().Replace(
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"",
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"",
            StringComparison.Ordinal);
        text = text.Insert(text.IndexOf("Hello", StringComparison.Ordinal) + 5, "<a epub:type=\"noteref\" href=\"#n7\" id=\"n7-back\">[7]</a>");
        int body = text.LastIndexOf("</body>", StringComparison.Ordinal);
        chapter.SetText(text.Insert(body, "<aside epub:type=\"footnote\" id=\"n7\"><p><a href=\"#n7-back\">7.</a> Other note.</p></aside>\n"));
        sut.LoadBook(book, epub);
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, chapter);
    }

    private static int AfterHello(HtmlResource chapter) => chapter.GetText().IndexOf("Hello", StringComparison.Ordinal) + 5;

    [Fact]
    public void A_footnote_is_added_edited_into_the_global_notes_and_removed_with_its_notes_file()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        int offset = AfterHello(chapter);

        host.GetFootnoteState(chapter, offset).Should().Be(new FootnoteMenuState(null, FootnoteError.None, false));
        host.GetFootnoteNumbers(chapter, offset).Should().Be((1, 1));
        host.AddFootnote(chapter, offset, "A <i>note</i>.", FootnotePlacement.Local);

        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\"").And.Contain("A <i>note</i>.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteAddedLocal"));
        sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());

        FootnoteMenuState onReference = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[1]", StringComparison.Ordinal))!;
        onReference.Hit!.Side.Should().Be(FootnoteSide.Reference);
        onReference.HasFootnotes.Should().BeTrue();
        FootnoteEditState edit = host.GetFootnoteEdit(onReference.Hit)!;
        edit.Content.Should().Be(new FootnoteContent("A <i>note</i>.", false));
        (edit.LocalNumber, edit.GlobalNumber).Should().Be((1, 1));
        edit.ReferenceText[edit.ReferenceStart..edit.ReferenceEnd].Should().StartWith("<a class=\"signet-fn-ref\"").And.EndWith("[1]</a>");
        host.EditFootnote(onReference.Hit, "A <b>new</b> note.", FootnotePlacement.Global);

        HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
        notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);
        notes.GetText().Should().Contain("A <b>new</b> note.");
        chapter.GetText().Should().NotContain("A <i>note</i>.").And.NotContain("A <b>new</b> note.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteMovedGlobal"));
        sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

        FootnoteMenuState onNote = host.GetFootnoteState(notes, notes.GetText().IndexOf("A <b>new", StringComparison.Ordinal))!;
        onNote.Hit!.Side.Should().Be(FootnoteSide.Note);
        host.GoToFootnote(onNote.Hit);
        sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

        host.RemoveFootnote(onNote.Hit);

        book.GetHtmlResources().Should().NotContain(notes);
        chapter.GetText().Should().NotContain("<a class=\"signet-fn-ref\"").And.Contain("Hello, world.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteRemoved"));
    }

    [Fact]
    public void Editing_only_the_text_keeps_the_footnote_in_place_and_an_invalid_text_changes_nothing()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        host.AddFootnote(chapter, AfterHello(chapter), "Old text.", FootnotePlacement.Local);
        FootnoteHit hit = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("Old text.", StringComparison.Ordinal))!.Hit!;
        hit.Side.Should().Be(FootnoteSide.Note);

        host.ValidateFootnoteEdit(hit.Footnote, "a < b").Should().Be(FootnoteError.InvalidContent);
        host.EditFootnote(hit, "a < b", FootnotePlacement.Local);
        chapter.GetText().Should().Contain("Old text.");
        sut.StatusMessage.Should().Be(
            Strings.Format("Status_FootnoteNotEdited", FootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));

        int scrolledTo = -1;
        sut.ActiveCodeTab!.ScrollToOffsetRequested += offset => scrolledTo = offset;
        host.EditFootnote(hit, "New text.", FootnotePlacement.Local);

        chapter.GetText().Should().Contain("1.</a> New text.</p>").And.NotContain("Old text.");
        FootnoteEditor.FindGlobalFile(book).Should().BeNull();
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteEdited"));
        scrolledTo.Should().Be(chapter.GetText().IndexOf("<aside", StringComparison.Ordinal), "the caret goes back to the note");
    }

    [Fact]
    public void A_reference_added_to_a_footnote_makes_it_shared_and_removing_it_makes_it_ordinary_again()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        host.AddFootnote(chapter, AfterHello(chapter), "Shared note.", FootnotePlacement.Local);
        int end = chapter.GetText().IndexOf("world", StringComparison.Ordinal) + 5;
        host.GetFootnoteState(chapter, end)!.ReferenceTargetCount.Should().Be(1);

        ReferenceTargetList targets = host.GetFootnoteReferenceTargets(chapter, end)!;
        targets.Targets.Should().ContainSingle().Which.Text.Should().Be("Shared note.");
        host.AddFootnoteReference(chapter, end, targets.Targets[targets.Nearest].Footnote);

        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1a-ref\">[1a]</a>")
            .And.Contain("world<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1b-ref\">[1b]</a>")
            .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. Shared note.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteReferenceAdded"));
        sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());

        FootnoteMenuState onSecond = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[1b]", StringComparison.Ordinal))!;
        onSecond.Hit!.Footnote.IsShared.Should().BeTrue();
        host.RemoveFootnoteReference(onSecond.Hit);

        chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
            .And.NotContain("[1b]").And.Contain("<a href=\"#fn-1-ref\">1.</a> Shared note.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteReferenceRemoved"));
    }

    [Fact]
    public void The_removal_confirmation_is_a_setting()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource _) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        host.ConfirmFootnoteRemoval.Should().BeTrue("removing asks by default");

        host.ConfirmFootnoteRemoval = false;

        host.ConfirmFootnoteRemoval.Should().BeFalse();
    }

    [Fact]
    public void An_invalid_footnote_text_adds_nothing_and_says_why()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        string before = chapter.GetText();

        host.ValidateFootnoteContent("a < b").Should().Be(FootnoteError.InvalidContent);
        host.AddFootnote(chapter, AfterHello(chapter), "a < b", FootnotePlacement.Local);

        chapter.GetText().Should().Be(before);
        sut.StatusMessage.Should().Be(
            Strings.Format("Status_FootnoteNotAdded", FootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));
    }

    [Fact]
    public void The_footnote_state_inside_a_tag_has_the_reason_and_no_footnote()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        int insideTag = chapter.GetText().IndexOf("<p>Hello", StringComparison.Ordinal) + 1;

        host.GetFootnoteState(chapter, insideTag).Should().Be(new FootnoteMenuState(null, FootnoteError.InsideTag, false));
    }

    [Fact]
    public void Renumbering_numbered_footnotes_reports_that_the_numbering_is_up_to_date()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);

        host.RenumberFootnotes();

        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnotesUpToDate"));
    }

    [Fact]
    public void The_placement_of_the_last_added_footnote_is_the_next_default()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Global);
        host.LastFootnotePlacement.Should().Be(FootnotePlacement.Global);

        host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);
        host.LastFootnotePlacement.Should().Be(FootnotePlacement.Local);
    }

    [Fact]
    public void Tools_renumber_footnotes_renumbers_like_the_context_menu()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);

        sut.Actions.Require(AppActionIds.RenumberFootnotes).Execute(null);

        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesUpToDateForeignSkipped", 1));
    }

    [Fact]
    public void The_footnote_state_counts_the_footnotes_of_other_origin()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
        ICodeTabHost host = sut;

        FootnoteMenuState state = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[7]", StringComparison.Ordinal))!;

        state.Hit!.Footnote.IsSignet.Should().BeFalse();
        state.ForeignCount.Should().Be(1);
    }

    [Fact]
    public void Renumbering_leaves_footnotes_of_other_origin_as_they_are_and_warns_about_them()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
        ICodeTabHost host = sut;
        int afterWorld = chapter.GetText().IndexOf(", world", StringComparison.Ordinal) + ", world".Length;
        host.AddFootnote(chapter, afterWorld, "Note.", FootnotePlacement.Local);

        host.RenumberFootnotes();

        chapter.GetText().Should().Contain(">[7]</a>").And.Contain(">7.</a> Other note.")
            .And.Contain("id=\"fn-2-ref\">[2]</a>", "the footnote of other origin before it counts");
        sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesUpToDateForeignSkipped", 1));
        sut.IsStatusMessageWarning.Should().BeTrue();
    }

    [Fact]
    public void The_preview_of_a_footnote_changes_nothing()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        string before = chapter.GetText();
        int offset = AfterHello(chapter);

        FootnoteMarkup preview = host.PreviewFootnote(chapter, offset, offset, "One.\n\nTwo.", FootnotePlacement.Local, 1, null)!;

        preview.Reference.Should().Contain("href=\"#fn-1\"");
        preview.Note.Should().Contain("<p class=\"signet-fn-p\">Two.</p>");
        preview.Page.Should().Contain("<p>Hello" + preview.Reference);
        chapter.GetText().Should().Be(before);
        host.PreviewFootnote(chapter, offset, offset, "a < b", FootnotePlacement.Local, 1, null).Should().BeNull();
    }

    [Fact]
    public void Tools_convert_footnotes_asks_the_view_for_the_dialog()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);
        bool raised = false;
        sut.FootnoteConversionRequested += (_, _) => raised = true;

        sut.Actions.Require(AppActionIds.ConvertFootnotes).Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public async Task Without_footnotes_of_other_origin_there_is_no_conversion_dialog()
    {
        using UiCultureScope culture = new("en");
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource _) = OpenMinimalChapter(temp);

        (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true))).Should().BeNull();

        sut.StatusMessage.Should().Be(Strings.Get("Status_NoForeignFootnotes"));
    }

    [Theory]
    [AutoData]
    public void Abandoning_a_checkpoint_brings_the_book_back_and_forgets_the_failed_state(string operation, string edit)
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        string before = chapter.GetText();
        int states = sut.CheckpointHistory.States.Count;
        sut.AddCheckpointBefore(operation).Should().BeTrue();
        chapter.SetText(before.Replace("Hello", edit, StringComparison.Ordinal));

        sut.AbandonCheckpoint().Should().BeTrue();

        Book restored = ((IBookWorkspace)sut).CurrentBook!;
        restored.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Be(before);
        sut.CheckpointHistory.States.Should().HaveCount(states);
        sut.CheckpointHistory.CanRedo.Should().BeFalse();
        sut.AbandonCheckpoint().Should().BeFalse("there is no checkpoint just created any more");
    }
}
