using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The footnote services of the main window for the Code View "Footnotes" submenu (<see cref="ICodeTabHost"/>).</summary>
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

    private static int AfterHello(HtmlResource chapter) => chapter.GetText().IndexOf("Hello", StringComparison.Ordinal) + 5;

    [Fact]
    public void A_footnote_is_added_moved_to_the_global_notes_and_removed_with_its_notes_file()
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
        host.MoveFootnote(onReference.Hit);

        HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
        notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);
        notes.GetText().Should().Contain("A <i>note</i>.");
        chapter.GetText().Should().NotContain("A <i>note</i>.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteMovedGlobal"));
        sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

        FootnoteMenuState onNote = host.GetFootnoteState(notes, notes.GetText().IndexOf("A <i>note", StringComparison.Ordinal))!;
        onNote.Hit!.Side.Should().Be(FootnoteSide.Note);
        host.GoToFootnote(onNote.Hit);
        sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

        host.RemoveFootnote(onNote.Hit);

        book.GetHtmlResources().Should().NotContain(notes);
        chapter.GetText().Should().NotContain("<a class=\"signet-fn-ref\"").And.Contain("Hello, world.");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteRemoved"));
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
            Strings.Format("Status_FootnoteNotAdded", AddFootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));
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
}
