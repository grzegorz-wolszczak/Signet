using System;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>
/// Tests of shared footnotes — a note that several references lead to: <see cref="FootnoteScanner"/> gives a footnote
/// per reference, <see cref="FootnoteEditor.Renumber"/> one number per note with a letter per reference.
/// </summary>
public sealed class SharedFootnoteTests
{
    private const string Body = "<p>First sentence.</p>\n<p>Second sentence.</p>\n<p>Third sentence.</p>";

    private readonly Fixture _fixture = new();

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body = Body)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    private static string Reference(string id, string note, string marker) =>
        "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + note + "\" id=\"" + id + "\">" + marker + "</a>";

    // A note with a reference after "First" and a second one after "Third".
    private static Footnote AddShared(Book book, HtmlResource html, string text, FootnoteOptions? options = null)
    {
        FootnoteEditor.Add(book, html, After(html, "First"), text, FootnotePlacement.Local, out Footnote? first, options)
            .Should().Be(FootnoteError.None);
        FootnoteEditor.AddReference(book, html, After(html, "Third"), first!, out Footnote? second, options)
            .Should().Be(FootnoteError.None);
        return second!;
    }

    [Fact]
    public void A_reference_added_to_a_note_makes_it_shared_with_a_letter_per_reference()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            string text = _fixture.Create<string>();
            Footnote second = AddShared(book, html, text);

            string markup = html.GetText();
            markup.Should().Contain("First" + Reference("fn-1a-ref", "#fn-1", "[1a]") + " sentence.")
                .And.Contain("Third" + Reference("fn-1b-ref", "#fn-1", "[1b]") + " sentence.")
                .And.Contain("<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
                    + "<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. " + text + "</p></aside>");
            second.ReferenceIndex.Should().Be(1);
            FootnoteScanner.Scan(book).Should().HaveCount(2).And.OnlyContain(f =>
                f.NoteId == "fn-1" && f.ReferenceCount == 2 && f.IsShared && f.IsSignet && f.Placement == FootnotePlacement.Local);
            FootnoteScanner.Scan(book).Select(f => f.ReferenceId).Should().Equal("fn-1a-ref", "fn-1b-ref");
            FootnoteValidator.Validate(book).Should().BeEmpty("the note has a link back to each reference");
        }
    }

    [Fact]
    public void The_letters_follow_the_reading_order_of_the_references()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "Third"), "note", FootnotePlacement.Local, out Footnote? footnote);
            FootnoteEditor.AddReference(book, html, After(html, "First"), footnote!, out Footnote? added).Should().Be(FootnoteError.None);

            added!.ReferenceIndex.Should().Be(0, "the new reference comes first in the text");
            html.GetText().Should().Contain("First" + Reference("fn-1a-ref", "#fn-1", "[1a]"))
                .And.Contain("Third" + Reference("fn-1b-ref", "#fn-1", "[1b]"));
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_shared_note_has_one_number_and_the_notes_between_its_references_go_on_counting()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            AddShared(book, html, "shared");
            FootnoteEditor.Add(book, html, After(html, "Second"), "between", FootnotePlacement.Local).Should().Be(FootnoteError.None);
            FootnoteEditor.NumberAt(book, html, html.GetText().Length - 1, FootnotePlacement.Local).Should().Be(3);

            string markup = html.GetText();
            markup.Should().Contain("First" + Reference("fn-1a-ref", "#fn-1", "[1a]"))
                .And.Contain("Second" + Reference("fn-2-ref", "#fn-2", "[2]"))
                .And.Contain("Third" + Reference("fn-1b-ref", "#fn-1", "[1b]"));
            markup.IndexOf("shared", StringComparison.Ordinal).Should().BeLessThan(markup.IndexOf("between", StringComparison.Ordinal));
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(FootnoteLetterStyle.UpperLatin, "A", "B", "C")]
    [InlineData(FootnoteLetterStyle.LowerRoman, "i", "ii", "iii")]
    public void A_shared_note_keeps_the_style_of_its_letters_when_the_option_changes(
        FootnoteLetterStyle style, string first, string second, string third)
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            Footnote footnote = AddShared(book, html, "note", FootnoteOptions.Default with { LetterStyle = style });
            FootnoteEditor.AddReference(book, html, After(html, "Second"), footnote, out _).Should().Be(FootnoteError.None);

            html.GetText().Should().Contain("First" + Reference("fn-1" + first + "-ref", "#fn-1", "[1" + first + "]"))
                .And.Contain("Second" + Reference("fn-1" + second + "-ref", "#fn-1", "[1" + second + "]"))
                .And.Contain("Third" + Reference("fn-1" + third + "-ref", "#fn-1", "[1" + third + "]"))
                .And.Contain(">1" + first + "</a>, <a href=\"#fn-1" + second + "-ref\">1" + second + "</a>, <a href=\"#fn-1"
                    + third + "-ref\">1" + third + "</a>. note</p>");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_global_note_takes_references_from_other_files()
    {
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));
            FootnoteEditor.Add(book, first, After(first, "First"), "endnote", FootnotePlacement.Global, out Footnote? footnote);
            FootnoteEditor.AddReference(book, second, After(second, "Second"), footnote!, out Footnote? added).Should().Be(FootnoteError.None);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            string toNotes = Core.BookPath.Relative(first.BookPath, notes.BookPath);
            first.GetText().Should().Contain(Reference("en-1a-ref", toNotes + "#en-1", "[1a]"));
            second.GetText().Should().Contain(Reference("en-1b-ref", toNotes + "#en-1", "[1b]"));
            notes.GetText().Should().Contain(">1a</a>, <a href=\"" + Core.BookPath.Relative(notes.BookPath, second.BookPath) + "#en-1b-ref\">1b</a>. endnote");
            added!.Placement.Should().Be(FootnotePlacement.Global);
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_local_note_takes_references_only_from_its_file_and_a_note_of_other_origin_none()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>Plain sentence.</p>\n<p>Text<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">1</a> more.</p>\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">1</a> Other note.</aside>");
        using (book)
        {
            HtmlResource other = book.CreateEmptyHtmlFile();
            other.SetText(Xhtml(Body));
            Footnote foreign = FootnoteScanner.Scan(book).Single();
            FootnoteEditor.AddReference(book, html, After(html, "more."), foreign, out _).Should().Be(FootnoteError.NotSignet);

            FootnoteEditor.Add(book, html, After(html, "Plain"), "local", FootnotePlacement.Local, out Footnote? local);
            FootnoteEditor.AddReference(book, other, After(other, "First"), local!, out Footnote? added).Should().Be(FootnoteError.OtherFile);
            added.Should().BeNull();
        }
    }

    [Fact]
    public void Editing_a_shared_note_keeps_its_links_back()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            Footnote footnote = AddShared(book, html, "old text");

            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent("old text", false));
            FootnoteEditor.Edit(book, footnote, "One.\n\nTwo.", FootnotePlacement.Local, out Footnote? edited).Should().Be(FootnoteError.None);

            html.GetText().Should().Contain("<p class=\"signet-fn-p\"><a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. One.</p>")
                .And.Contain("<p class=\"signet-fn-p\">Two.</p>");
            FootnoteEditor.GetContent(edited!).Should().Be(new FootnoteContent("One.\n\nTwo.", false));
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_note_of_other_origin_with_several_references_is_a_footnote_per_reference_and_one_item_of_the_plan()
    {
        // The shape of a book where every reference repeats the number and the id of the first one.
        (Book book, _) = NewBook(
            "<p>One<sup><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>, two<sup><a href=\"#fn4\" id=\"ref4\" "
            + "epub:type=\"noteref\">4</a></sup>.</p>\n<aside id=\"fn4\" epub:type=\"footnote\"><a href=\"#ref4\">[4]</a> Note.</aside>");
        using (book)
        {
            FootnoteScanner.Scan(book).Select(f => (f.ReferenceOccurrence, f.ReferenceIndex, f.ReferenceCount))
                .Should().Equal((0, 0, 2), (1, 1, 2));
            FootnoteEditor.PlanConversion(book).Items.Should().ContainSingle();
            FootnoteValidator.Validate(book).Select(r => r.Code).Should().Contain("Validation_FootnoteManyRefs",
                "the note has a link back to the first reference only");
        }
    }

    [Theory]
    [InlineData(FootnoteLetterStyle.LowerLatin, 0, "a")]
    [InlineData(FootnoteLetterStyle.LowerLatin, 25, "z")]
    [InlineData(FootnoteLetterStyle.LowerLatin, 26, "aa")]
    [InlineData(FootnoteLetterStyle.LowerLatin, 27, "ab")]
    [InlineData(FootnoteLetterStyle.LowerLatin, 701, "zz")]
    [InlineData(FootnoteLetterStyle.LowerLatin, 702, "aaa")]
    [InlineData(FootnoteLetterStyle.UpperLatin, 26, "AA")]
    [InlineData(FootnoteLetterStyle.LowerRoman, 0, "i")]
    [InlineData(FootnoteLetterStyle.LowerRoman, 3, "iv")]
    [InlineData(FootnoteLetterStyle.LowerRoman, 13, "xiv")]
    public void Letters_are_written_in_their_style(FootnoteLetterStyle style, int index, string expected) =>
        FootnoteLetters.Of(style, index).Should().Be(expected);

    [Theory]
    [InlineData(null, "", "")]
    [InlineData(FootnoteLetterStyle.LowerLatin, "a", "b")]
    [InlineData(FootnoteLetterStyle.LowerLatin, "b", "c")]
    [InlineData(FootnoteLetterStyle.LowerLatin, "", "b")]
    [InlineData(FootnoteLetterStyle.UpperLatin, "B", "")]
    [InlineData(FootnoteLetterStyle.LowerRoman, "ii", "iii")]
    [InlineData(FootnoteLetterStyle.LowerRoman, "i", "")]
    public void The_style_of_written_letters_is_recognized(FootnoteLetterStyle? expected, string first, string second) =>
        FootnoteLetters.Detect(new[] { first, second }).Should().Be(expected);
}
