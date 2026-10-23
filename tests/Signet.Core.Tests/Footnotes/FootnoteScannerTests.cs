using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>
/// Tests of what <see cref="FootnoteScanner.Scan"/> promises about the references it finds. The editor looks a
/// reference up by its file, its href and which occurrence of that href it is
/// (<see cref="FootnoteEditor.PlanLetters"/> and the conversion both index the scan by those three), so that triple
/// has to name one reference and no other.
/// </summary>
public sealed class FootnoteScannerTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    private static HtmlResource AddFile(Book book, string name, string body)
    {
        HtmlResource html = book.CreateEmptyHtmlFile();
        html.RenameTo(name);
        html.SetText(Xhtml(body));
        return html;
    }

    private static string Ref(string href, string id, string marker) =>
        "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + href + "\" id=\"" + id + "\">" + marker + "</a>";

    /// <summary>
    /// A shared note read through the same href three times in one file, a note read from another file, a second note
    /// of the same file and a plain link to the first note — no two references may come out with the same triple.
    /// </summary>
    [Fact]
    public void Every_reference_is_identified_by_its_file_href_and_occurrence()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>A" + Ref("#fn-1", "fn-1a-ref", "[1a]") + " B" + Ref("#fn-1", "fn-1b-ref", "[1b]")
            + " C" + Ref("#fn-1", "fn-1c-ref", "[1c]") + " D" + Ref("#fn-2", "fn-2-ref", "[2]") + "</p>\n"
            + "<p>See <a href=\"#fn-1\">the first note</a>.</p>\n"
            + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
            + "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
            + "<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>, <a href=\"#fn-1c-ref\">1c</a>. First.</p></aside>\n"
            + "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-2\"><p class=\"signet-fn-p\">"
            + "<a href=\"#fn-2-ref\">2.</a> Second.</p></aside>\n"
            + "</section>");
        using (book)
        {
            AddFile(book, "Other.xhtml",
                "<p>E" + Ref(html.Filename + "#fn-1", "fn-1d-ref", "[1d]")
                + " F" + Ref(html.Filename + "#fn-1", "fn-1e-ref", "[1e]") + "</p>");

            // Act
            IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

            // Assert
            footnotes.Should().NotBeEmpty();
            footnotes.Select(f => (f.ReferenceFile.BookPath, f.ReferenceHref, f.ReferenceOccurrence))
                .Should().OnlyHaveUniqueItems();
        }
    }

    /// <summary>
    /// The occurrences of one href in one file are counted from zero in reading order; the count starts over in
    /// another file, so the file is part of what names a reference.
    /// </summary>
    [Fact]
    public void The_occurrences_of_one_href_in_a_file_are_counted_from_zero_in_reading_order()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>A" + Ref("#fn-1", "fn-1a-ref", "[1a]") + " B" + Ref("#fn-1", "fn-1b-ref", "[1b]")
            + " C" + Ref("#fn-1", "fn-1c-ref", "[1c]") + "</p>\n"
            + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
            + "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
            + "<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>, <a href=\"#fn-1c-ref\">1c</a>. First.</p></aside>\n"
            + "</section>");
        using (book)
        {
            HtmlResource other = AddFile(book, "Other.xhtml",
                "<p>D" + Ref(html.Filename + "#fn-1", "fn-1d-ref", "[1d]") + "</p>");

            // Act
            List<Footnote> footnotes = FootnoteScanner.Scan(book)
                .Where(f => f.NoteId == "fn-1")
                .ToList();

            // Assert
            footnotes.Where(f => ReferenceEquals(f.ReferenceFile, html))
                .Select(f => (f.ReferenceId, f.ReferenceOccurrence))
                .Should().Equal(("fn-1a-ref", 0), ("fn-1b-ref", 1), ("fn-1c-ref", 2));
            footnotes.Where(f => ReferenceEquals(f.ReferenceFile, other))
                .Select(f => (f.ReferenceId, f.ReferenceOccurrence))
                .Should().Equal(new[] { ("fn-1d-ref", 0) }, "the count starts over in another file");
        }
    }

    /// <summary>
    /// The triple finds the reference it names: the occurrence picks out the very anchor the scan reported, not
    /// another anchor with the same href.
    /// </summary>
    [Fact]
    public void The_triple_of_a_reference_leads_back_to_its_own_anchor()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>A" + Ref("#fn-1", "fn-1a-ref", "[1a]") + " B" + Ref("#fn-1", "fn-1b-ref", "[1b]")
            + " C" + Ref("#fn-1", "fn-1c-ref", "[1c]") + "</p>\n"
            + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
            + "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
            + "<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>, <a href=\"#fn-1c-ref\">1c</a>. First.</p></aside>\n"
            + "</section>");
        using (book)
        {
            string text = html.GetText();

            // Act
            List<Footnote> footnotes = FootnoteScanner.Scan(book).Where(f => f.NoteId == "fn-1").ToList();

            // Assert
            footnotes.Should().HaveCount(3);
            foreach (Footnote footnote in footnotes)
            {
                int at = text.IndexOf("id=\"" + footnote.ReferenceId + "\"", StringComparison.Ordinal);
                at.Should().BePositive("the reference {0} is in the file", footnote.ReferenceId);

                // The anchor the occurrence picks out is the one that carries this reference's own id.
                int anchor = Nth(text, "href=\"#fn-1\"", footnote.ReferenceOccurrence);
                text[anchor..(at + 1)].Should().NotContain("</a>", "the occurrence and the id are in the same anchor");
            }
        }
    }

    // The position of the nth (zero-based) occurrence of `what` in `text`.
    private static int Nth(string text, string what, int n)
    {
        int at = -1;
        for (int i = 0; i <= n; i++)
        {
            at = text.IndexOf(what, at + 1, StringComparison.Ordinal);
            at.Should().BePositive("occurrence {0} of {1}", i, what);
        }

        return at;
    }
}
