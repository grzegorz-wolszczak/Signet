using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="FootnoteValidator"/> — footnotes and their references point at each other.</summary>
public sealed class FootnoteValidatorTests
{
    private const string Reference =
        "<p>Text<sup class=\"sup_note\"><a class=\"a_note\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a></sup>.</p>";

    private const string Note =
        "<aside class=\"aside_footnote\" id=\"fn-1\" epub:type=\"footnote\">"
        + "<p><a href=\"#fn-1-ref\">1.</a> B. Lapunow, Iz glubin Wsielennoj.</p></aside>";

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head><title>t</title></head>\n<body>\n" + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    private static List<string> Codes(Book book) => FootnoteValidator.Validate(book).Select(r => r.Code).ToList();

    [Fact]
    public void A_footnote_at_the_end_of_its_file_is_correct()
    {
        (Book book, _) = NewBook(Reference + "\n<hr/>\n" + Note);
        using (book)
        {
            Codes(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_footnote_in_a_separate_notes_file_is_correct()
    {
        (Book book, HtmlResource html) = NewBook(Reference.Replace("#fn-1", "Notes.xhtml#fn-1", System.StringComparison.Ordinal));
        using (book)
        {
            HtmlResource notes = book.CreateEmptyHtmlFile();
            notes.RenameTo("Notes.xhtml");
            notes.SetText(Xhtml(Note.Replace("#fn-1-ref", html.Filename + "#fn-1-ref", System.StringComparison.Ordinal)));

            Codes(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_footnote_without_a_link_back_is_reported_with_its_line()
    {
        (Book book, _) = NewBook(Reference + "\n" + Note.Replace("<a href=\"#fn-1-ref\">1.</a>", "1.", System.StringComparison.Ordinal));
        using (book)
        {
            ValidationResult result = FootnoteValidator.Validate(book).Should().ContainSingle().Subject;

            result.Code.Should().Be("Validation_FootnoteNoBacklink");
            result.Line.Should().Be(7);
            result.Message.Should().Contain("fn-1-ref");
        }
    }

    [Fact]
    public void A_footnote_that_nothing_links_to_is_reported()
    {
        (Book book, _) = NewBook("<p>Text.</p>\n" + Note);
        using (book)
        {
            Codes(book).Should().Equal("Validation_FootnoteOrphan");
        }
    }

    [Fact]
    public void A_link_back_to_another_place_is_reported()
    {
        (Book book, _) = NewBook(Reference + "\n<p id=\"elsewhere\">x</p>\n"
            + Note.Replace("#fn-1-ref", "#elsewhere", System.StringComparison.Ordinal));
        using (book)
        {
            Codes(book).Should().Equal("Validation_FootnoteBacklinkMismatch");
        }
    }

    [Fact]
    public void A_footnote_with_two_references_is_reported()
    {
        (Book book, _) = NewBook(Reference + "\n<p><a epub:type=\"noteref\" href=\"#fn-1\">[1]</a></p>\n" + Note);
        using (book)
        {
            Codes(book).Should().Equal("Validation_FootnoteManyRefs");
        }
    }

    [Fact]
    public void Notes_linking_in_a_circle_do_not_make_the_check_loop()
    {
        (Book book, _) = NewBook(
            "<aside id=\"a\" epub:type=\"footnote\"><p><a epub:type=\"noteref\" href=\"#b\">b</a></p></aside>\n"
            + "<aside id=\"b\" epub:type=\"footnote\"><p><a epub:type=\"noteref\" href=\"#c\">c</a></p></aside>\n"
            + "<aside id=\"c\" epub:type=\"footnote\"><p><a epub:type=\"noteref\" href=\"#a\">a</a></p></aside>");
        using (book)
        {
            // Each note's only link leads to the next note, never back to the note that points at it.
            Codes(book).Should().HaveCount(3).And.OnlyContain(c => c == "Validation_FootnoteBacklinkMismatch");
        }
    }

    [Fact]
    public void A_heading_that_links_back_to_an_html_table_of_contents_is_not_a_footnote()
    {
        (Book book, HtmlResource html) = NewBook("<h1 id=\"ch1\"><a href=\"Contents.xhtml#toc-ch1\">Chapter 1</a></h1>");
        using (book)
        {
            HtmlResource contents = book.CreateEmptyHtmlFile();
            contents.RenameTo("Contents.xhtml");
            contents.SetText(Xhtml(
                $"<p><a id=\"toc-ch1\" href=\"{html.Filename}#ch1\">Chapter 1</a></p>\n<p><a href=\"{html.Filename}#ch1\">Again</a></p>"));

            Codes(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Footnote_problems_are_part_of_check_book()
    {
        (Book book, _) = NewBook("<p>Text.</p>\n" + Note);
        using (book)
        {
            BookValidator.ValidateCurrentBook(book).Should().Contain(r => r.Code == "Validation_FootnoteOrphan");
        }
    }
}
