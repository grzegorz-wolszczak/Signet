using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>Tests of converting footnotes of other origin to Signet's (<see cref="FootnoteEditor.PlanConversion"/>, <see cref="FootnoteEditor.Convert"/>).</summary>
public sealed class FootnoteConversionTests
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

    private static HtmlResource AddFile(Book book, string body)
    {
        HtmlResource html = book.CreateEmptyHtmlFile();
        html.SetText(Xhtml(body));
        return html;
    }

    private static string BodyOf(HtmlResource html) =>
        html.GetText()[html.GetText().IndexOf("<body>", StringComparison.Ordinal)..];

    private static ConversionResult ConvertAll(Book book, HtmlResource? globalFile = null, bool keepClasses = false)
    {
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);
        return FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, globalFile ?? plan.AutomaticFile?.File, keepClasses));
    }

    [Fact]
    public void A_local_aside_note_gets_the_template_and_its_old_container_and_line_go()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup class=\"s\"><a class=\"r\" epub:type=\"noteref\" href=\"#n1\" id=\"n1-back\">1</a></sup>.</p>\n"
            + "<hr/>\n<section epub:type=\"footnotes\">\n"
            + "<aside class=\"note\" epub:type=\"footnote\" id=\"n1\"><p class=\"pn\"><a href=\"#n1-back\">1</a>. First <i>note</i>.</p><p>More.</p></aside>\n"
            + "</section>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            result.Converted.Should().Be(1);
            BodyOf(html).Should().Be(
                "<body>\n<p>Text<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>.</p>\n"
                + "<hr class=\"signet-fn-separator\"/>\n"
                + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
                + "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\">\n"
                + "    <p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> First <i>note</i>.</p>\n"
                + "    <p class=\"signet-fn-p\">More.</p>\n"
                + "  </aside>\n"
                + "</section>\n"
                + "</body>\n</html>");

            // Act
            Footnote footnote = FootnoteScanner.Scan(book).Single();

            // Assert
            footnote.IsSignet.Should().BeTrue();
            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent("First <i>note</i>.\n\nMore.", false));
        }
    }

    [Fact]
    public void The_classes_of_the_converted_elements_can_stay()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup class=\"s\"><a class=\"r\" epub:type=\"noteref\" href=\"#n1\" id=\"n1-back\">1</a></sup>.</p>\n"
            + "<aside class=\"note\" epub:type=\"footnote\" id=\"n1\"><p class=\"pn\"><a href=\"#n1-back\">1</a> Note.</p></aside>");
        using (book)
        {
            // Act
            ConvertAll(book, keepClasses: true).Problems.Should().BeEmpty();

            // Assert
            string text = html.GetText();
            text.Should().Contain("<a class=\"signet-fn-ref s r\" epub:type=\"noteref\"")
                .And.Contain("<aside class=\"signet-fn note\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p pn\"><a href=\"#fn-1-ref\">1.</a> Note.</p></aside>");
        }
    }

    [Fact]
    public void InDesign_notes_of_a_notes_file_are_taken_over_with_the_links_to_them()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource notes = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>One<span id=\"footnote-1-backlink\"><a href=\"" + notes.Filename + "#footnote-1\">1</a></span> two"
            + "<span id=\"footnote-2-backlink\"><a href=\"" + notes.Filename + "#footnote-2\">2</a></span>.</p>\n"
            + "<p><a href=\"" + notes.Filename + "#footnote-2\">see the second note</a></p>"));
        notes.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-1\"><p><a href=\"" + chapter.Filename + "#footnote-1-backlink\">1</a> First.</p></div>\n"
            + "<div id=\"footnote-2\"><p><a href=\"" + chapter.Filename + "#footnote-2-backlink\">2</a> Second.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Assert
            plan.AutomaticFile!.File.Should().BeSameAs(notes);
            plan.Items.Should().OnlyContain(i => i.CanConvert);
            plan.Items[1].Warnings.Should().Equal(ConversionWarning.LinkedElsewhere);
            plan.Items[1].Links.Should().Equal(new ConversionLink(
                chapter, chapter.GetText().IndexOf("<a href=\"" + notes.Filename + "#footnote-2\">see", StringComparison.Ordinal), IsReference: false));
            plan.Items[1].OtherLinks.Should().Equal(chapter.BookPath);

            // Act
            ConversionResult result = FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, notes, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            result.Problems.Should().BeEmpty();
            BodyOf(notes).Should().Be(
                "<body>\n<h1>Notes</h1>\n"
                + "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
                + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\"" + chapter.Filename + "#en-1-ref\">1.</a> First.</p></aside>\n"
                + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-2\"><p class=\"signet-fn-p\"><a href=\"" + chapter.Filename + "#en-2-ref\">2.</a> Second.</p></aside>\n"
                + "</section>\n"
                + "</body>\n</html>");
            BodyOf(chapter).Should().Contain(
                "<p>One<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + notes.Filename + "#en-1\" id=\"en-1-ref\">[1]</a> two")
                .And.Contain("<p><a href=\"" + notes.Filename + "#en-2\">see the second note</a></p>")
                .And.NotContain("backlink");
            FootnoteEditor.FindGlobalFile(book).Should().BeSameAs(notes);
        }
    }

    [Fact]
    public void Signets_own_notes_follow_the_converted_ones_into_the_taken_over_file()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource signetNotes = AddFile(book, string.Empty);
        HtmlResource taken = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>One<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-1\" id=\"en-1-ref\">[1]</a>"
            + " two<span id=\"footnote-2-backlink\"><a href=\"" + taken.Filename + "#footnote-2\">2</a></span>.</p>"));
        signetNotes.SetText(Xhtml(
            "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
            + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\""
            + chapter.Filename + "#en-1-ref\">1.</a> Signet's own.</p></aside>\n"
            + "</section>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-2\"><p><a href=\"" + chapter.Filename + "#footnote-2-backlink\">2</a> Of other origin.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            result.Problems.Should().BeEmpty();
            BodyOf(taken).Should().Contain("Signet's own.").And.Contain("Of other origin.");
            BodyOf(signetNotes).Should().NotContain("Signet's own.");
            FootnoteEditor.FindGlobalFile(book).Should().BeSameAs(taken);

            // Both references lead to the file the notes are in now.
            BodyOf(chapter).Should().NotContain(signetNotes.Filename);
            FootnoteScanner.Scan(book).Should().HaveCount(2)
                .And.OnlyContain(f => f.IsSignet && ReferenceEquals(f.NoteFile, taken));
        }
    }

    [Fact]
    public void All_the_references_of_a_shared_note_of_Signet_follow_it_and_the_note_moves_once()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource signetNotes = AddFile(book, string.Empty);
        HtmlResource taken = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>One<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-1\" id=\"en-1a-ref\">[1a]</a>"
            + " again<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-1\" id=\"en-1b-ref\">[1b]</a>"
            + " two<span id=\"footnote-2-backlink\"><a href=\"" + taken.Filename + "#footnote-2\">2</a></span>.</p>"));
        signetNotes.SetText(Xhtml(
            "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
            + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\""
            + chapter.Filename + "#en-1a-ref\">1a</a>, <a href=\"" + chapter.Filename + "#en-1b-ref\">1b</a>. Shared one.</p></aside>\n"
            + "</section>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-2\"><p><a href=\"" + chapter.Filename + "#footnote-2-backlink\">2</a> Of other origin.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert: the note went over once, not once per reference.
            result.Problems.Should().BeEmpty();
            Regex.Matches(BodyOf(taken), "Shared one\\.").Should().ContainSingle();
            BodyOf(signetNotes).Should().NotContain("Shared one.");

            // Assert: neither reference is left pointing at the file the note came from.
            BodyOf(chapter).Should().NotContain(signetNotes.Filename);
            FootnoteScanner.Scan(book).Where(f => f.IsShared).Should().HaveCount(2)
                .And.OnlyContain(f => ReferenceEquals(f.NoteFile, taken));
        }
    }

    [Fact]
    public void Several_notes_of_Signet_move_in_reading_order_with_references_from_several_files()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook(string.Empty);
        HtmlResource second = AddFile(book, string.Empty);
        HtmlResource signetNotes = AddFile(book, string.Empty);
        HtmlResource taken = AddFile(book, string.Empty);
        first.SetText(Xhtml(
            "<p>One<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-1\" id=\"en-1-ref\">[1]</a>.</p>"));
        second.SetText(Xhtml(
            "<p>Two<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-2\" id=\"en-2-ref\">[2]</a>"
            + " three<span id=\"footnote-3-backlink\"><a href=\"" + taken.Filename + "#footnote-3\">3</a></span>.</p>"));
        signetNotes.SetText(Xhtml(
            "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
            + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\""
            + first.Filename + "#en-1-ref\">1.</a> Signet A.</p></aside>\n"
            + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-2\"><p class=\"signet-fn-p\"><a href=\""
            + second.Filename + "#en-2-ref\">2.</a> Signet B.</p></aside>\n"
            + "</section>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-3\"><p><a href=\"" + second.Filename + "#footnote-3-backlink\">3</a> Of other origin.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert: every note is in the taken-over file, in the reading order of its reference.
            result.Problems.Should().BeEmpty();
            string body = BodyOf(taken);
            body.IndexOf("Signet A.", StringComparison.Ordinal).Should()
                .BePositive().And.BeLessThan(body.IndexOf("Signet B.", StringComparison.Ordinal));
            body.IndexOf("Signet B.", StringComparison.Ordinal).Should()
                .BeLessThan(body.IndexOf("Of other origin.", StringComparison.Ordinal));
            BodyOf(signetNotes).Should().NotContain("Signet A.").And.NotContain("Signet B.");

            // Assert: the references of both files were retargeted.
            BodyOf(first).Should().NotContain(signetNotes.Filename);
            BodyOf(second).Should().NotContain(signetNotes.Filename);
        }
    }

    [Fact]
    public void A_link_to_a_moved_note_of_Signet_follows_it_and_the_emptied_notes_file_goes()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource signetNotes = AddFile(book, string.Empty);
        HtmlResource taken = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>One<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-1\" id=\"en-1-ref\">[1]</a>"
            + " two<span id=\"footnote-2-backlink\"><a href=\"" + taken.Filename + "#footnote-2\">2</a></span>.</p>\n"
            + "<p><a href=\"" + signetNotes.Filename + "#en-1\">see the first note</a></p>"));
        signetNotes.SetText(Xhtml(
            "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
            + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\""
            + chapter.Filename + "#en-1-ref\">1.</a> Signet's own.</p></aside>\n"
            + "</section>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-2\"><p><a href=\"" + chapter.Filename + "#footnote-2-backlink\">2</a> Of other origin.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert: with its notes gone the file has nothing left, so it goes with them.
            result.Problems.Should().BeEmpty();
            result.RemovedFiles.Should().Equal(signetNotes.BookPath);
            book.GetHtmlResources().Should().NotContain(signetNotes);

            // Assert: the link that led to the note, which is not a reference, leads to its new file.
            BodyOf(chapter).Should().Contain("see the first note").And.NotContain(signetNotes.Filename);
        }
    }

    [Fact]
    public void The_notes_of_Signet_move_into_a_taken_over_file_whose_headings_became_groups()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource signetNotes = AddFile(book, string.Empty);
        HtmlResource taken = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>A<a href=\"" + taken.Filename + "#a\" id=\"a-back\">1</a>"
            + " B<a href=\"" + taken.Filename + "#b\" id=\"b-back\">2</a>"
            + " C<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"" + signetNotes.Filename + "#en-9\" id=\"en-9-ref\">[3]</a>.</p>"));
        signetNotes.SetText(Xhtml(
            "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
            + "  <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-9\"><p class=\"signet-fn-p\"><a href=\""
            + chapter.Filename + "#en-9-ref\">3.</a> Signet's own.</p></aside>\n"
            + "</section>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n<h2>Chapter 1</h2>\n"
            + "<div id=\"a\"><p><a href=\"" + chapter.Filename + "#a-back\">1</a> Note a.</p></div>\n"
            + "<h2>Chapter 2</h2>\n"
            + "<div id=\"b\"><p><a href=\"" + chapter.Filename + "#b-back\">2</a> Note b.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert: the groups the headings became are kept, and the moved note is in the file with them.
            result.Problems.Should().BeEmpty();
            string body = BodyOf(taken);
            Regex.Matches(body, "signet-fn-group\"").Should().HaveCount(2);
            body.Should().Contain("Note a.").And.Contain("Note b.").And.Contain("Signet's own.");
            BodyOf(signetNotes).Should().NotContain("Signet's own.");
            FootnoteScanner.Scan(book).Should().HaveCount(3).And.OnlyContain(f => ReferenceEquals(f.NoteFile, taken));
        }
    }

    [Fact]
    public void A_notes_file_of_Signet_with_no_notes_in_it_does_not_get_in_the_way()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource signetNotes = AddFile(book, string.Empty);
        HtmlResource taken = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>One<span id=\"footnote-1-backlink\"><a href=\"" + taken.Filename + "#footnote-1\">1</a></span>.</p>"));
        signetNotes.SetText(Xhtml("<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n</section>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-1\"><p><a href=\"" + chapter.Filename + "#footnote-1-backlink\">1</a> Of other origin.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            result.Problems.Should().BeEmpty();
            BodyOf(taken).Should().Contain("Of other origin.");
            FootnoteEditor.FindGlobalFile(book).Should().BeSameAs(taken);
            FootnoteScanner.Scan(book).Should().ContainSingle().Which.NoteFile.Should().BeSameAs(taken);
        }
    }

    [Fact]
    public void Signets_own_notes_move_into_the_taken_over_file_of_an_epub2_book_too()
    {
        // Arrange
        Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav()[0];
        HtmlResource signetNotes = book.CreateEmptyHtmlFile();
        HtmlResource taken = book.CreateEmptyHtmlFile();
        chapter.SetText(Xhtml(
            "<p>One<a class=\"signet-fn-ref\" href=\"" + signetNotes.Filename + "#en-1\" id=\"en-1-ref\">[1]</a>"
            + " two<span id=\"footnote-2-backlink\"><a href=\"" + taken.Filename + "#footnote-2\">2</a></span>.</p>"));
        signetNotes.SetText(Xhtml(
            "<div class=\"signet-endnotes\">\n"
            + "  <div class=\"signet-fn\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\""
            + chapter.Filename + "#en-1-ref\">1.</a> Signet's own.</p></div>\n"
            + "</div>"));
        taken.SetText(Xhtml(
            "<h1>Notes</h1>\n"
            + "<div id=\"footnote-2\"><p><a href=\"" + chapter.Filename + "#footnote-2-backlink\">2</a> Of other origin.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, taken, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert: the notes of an EPUB 2 book are divs, with no epub:type on them.
            result.Problems.Should().BeEmpty();
            BodyOf(taken).Should().Contain("Signet's own.").And.Contain("Of other origin.").And.NotContain("epub:type");
            BodyOf(signetNotes).Should().NotContain("Signet's own.");
            BodyOf(chapter).Should().NotContain(signetNotes.Filename);
        }
    }

    [Fact]
    public void The_headings_between_the_notes_of_a_taken_over_file_become_groups()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource notes = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>A<a href=\"" + notes.Filename + "#a\" id=\"a-back\">1</a> B<a href=\"" + notes.Filename + "#b\" id=\"b-back\">2</a>.</p>"));
        notes.SetText(Xhtml(
            "<h1>Notes</h1>\n<h2>Chapter 1</h2>\n"
            + "<div id=\"a\"><p><a href=\"" + chapter.Filename + "#a-back\">1</a> Note a.</p></div>\n"
            + "<h2>Chapter 2</h2>\n"
            + "<div id=\"b\"><p><a href=\"" + chapter.Filename + "#b-back\">2</a> Note b.</p></div>"));
        using (book)
        {
            // Act
            ConvertAll(book, notes).Problems.Should().BeEmpty();

            // Assert
            BodyOf(notes).Should().Be(
                "<body>\n<h1>Notes</h1>\n"
                + "<section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
                + "  <section class=\"signet-fn-group\">\n"
                + "    <h2 class=\"signet-fn-group-title\">Chapter 1</h2>\n"
                + "    <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\"" + chapter.Filename + "#en-1-ref\">1.</a> Note a.</p></aside>\n"
                + "  </section>\n"
                + "  <section class=\"signet-fn-group\">\n"
                + "    <h2 class=\"signet-fn-group-title\">Chapter 2</h2>\n"
                + "    <aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-2\"><p class=\"signet-fn-p\"><a href=\"" + chapter.Filename + "#en-2-ref\">2.</a> Note b.</p></aside>\n"
                + "  </section>\n"
                + "</section>\n"
                + "</body>\n</html>");
        }
    }

    [Fact]
    public void Notes_of_several_files_go_to_Signets_notes_file_and_the_emptied_files_go()
    {
        // Arrange
        (Book book, HtmlResource chapter) = NewBook(string.Empty);
        HtmlResource first = AddFile(book, string.Empty);
        HtmlResource second = AddFile(book, string.Empty);
        chapter.SetText(Xhtml(
            "<p>A<a href=\"" + first.Filename + "#a\" id=\"a-back\">1</a> B<a href=\"" + second.Filename + "#b\" id=\"b-back\">2</a>.</p>\n"
            + "<p><a href=\"" + second.Filename + "\">The notes of part two</a></p>"));
        first.SetText(Xhtml("<h1>Notes</h1>\n<div id=\"a\"><p><a href=\"" + chapter.Filename + "#a-back\">1</a> Note a.</p></div>"));
        second.SetText(Xhtml("<h1>Notes</h1>\n<div id=\"b\"><p><a href=\"" + chapter.Filename + "#b-back\">2</a> Note b.</p></div>"));
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Assert
            plan.Candidates.Should().HaveCount(2);
            plan.AutomaticFile.Should().BeNull();

            // Act
            ConversionResult result = FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            result.Problems.Should().BeEmpty();
            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);
            notes.GetText().Should().Contain("Note a.</p></aside>").And.Contain("Note b.</p></aside>");
            result.RemovedFiles.Should().BeEquivalentTo(new[] { first.BookPath, second.BookPath });
            book.GetHtmlResources().Should().NotContain(first).And.NotContain(second);
            BodyOf(chapter).Should().Contain("<p><a href=\"" + notes.Filename + "\">The notes of part two</a></p>");
            LinkIntegrityValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_note_with_several_references_becomes_a_shared_note()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>A<a href=\"#n\" id=\"n-back\">1</a> again<a href=\"#n\">1</a>.</p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">1</a> Shared note.</p></div>");
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionItem item = plan.Items.Single();

            // Assert
            item.CanConvert.Should().BeTrue();
            item.Notices.Should().Equal(ConversionNotice.SharedNote);
            item.Warnings.Should().BeEmpty("a shared note is for information only");
            item.Links.Should().BeEmpty("the other reference is the note's own");
            item.References.Select(r => r.ReferenceIndex).Should().Equal(0, 1);

            // Act
            ConversionResult result = FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false), cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            result.Problems.Should().BeEmpty();
            result.Converted.Should().Be(1);
            BodyOf(html).Should().Be(
                "<body>\n<p>A<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1a-ref\">[1a]</a>"
                + " again<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1b-ref\">[1b]</a>.</p>\n"
                + "<hr class=\"signet-fn-separator\"/>\n"
                + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
                + "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
                + "<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. Shared note.</p></aside>\n"
                + "</section>\n"
                + "</body>\n</html>");
            FootnoteScanner.Scan(book).Should().HaveCount(2).And.OnlyContain(f => f.IsSignet && f.IsShared);
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void References_that_repeat_the_number_and_the_id_of_the_first_one_become_lettered_references()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Balak<sup class=\"fnref\"><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>, Gilgal"
            + "<sup class=\"fnref\"><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>, works"
            + "<sup class=\"fnref\"><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>.</p>\n"
            + "<aside id=\"fn4\" epub:type=\"footnote\" class=\"footnote\">\n  <a href=\"#ref4\" class=\"fn-back\">[4]</a> Three places.\n</aside>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            string body = BodyOf(html);
            body.Should().Contain("Balak<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1a-ref\">[1a]</a>, Gilgal"
                + "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1b-ref\">[1b]</a>, works"
                + "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1c-ref\">[1c]</a>.</p>")
                .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>, <a href=\"#fn-1c-ref\">1c</a>. Three places.</p>")
                .And.NotContain("ref4", "the duplicate ids are gone");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_note_with_references_from_several_files_becomes_global()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook(
            "<p>Here<a epub:type=\"noteref\" href=\"#n\" id=\"n-back\">1</a>.</p>\n"
            + "<aside epub:type=\"footnote\" id=\"n\"><a href=\"#n-back\">1</a> Note.</aside>");
        using (book)
        {
            HtmlResource second = AddFile(book, "<p>There<a epub:type=\"noteref\" href=\""
                + first.Filename + "#n\">1</a>.</p>");

            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionItem item = plan.Items.Single();

            // Assert
            item.Footnote.Placement.Should().Be(FootnotePlacement.Global);
            item.Notices.Should().Equal(ConversionNotice.SharedNote, ConversionNotice.BecomesGlobal);

            // Act & Assert
            FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false), cancellationToken: TestContext.Current.CancellationToken).Problems.Should().BeEmpty();

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;

            // Assert
            notes.GetText().Should().Contain("id=\"en-1\"").And.Contain(">1a</a>, <a href=").And.Contain(">1b</a>. Note.");
            BodyOf(first).Should().Contain("[1a]</a>").And.NotContain("aside");
            BodyOf(second).Should().Contain("[1b]</a>");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void The_plan_gives_the_range_of_each_reference()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>A<sup><a href=\"#a\" id=\"a-back\">1</a></sup> B<a href=\"#b\" id=\"b-back\">2</a>.</p>\n"
            + "<div id=\"a\"><p><a href=\"#a-back\">1</a> First.</p></div>\n"
            + "<div id=\"b\"><p><a href=\"#b-back\">2</a> Second.</p></div>");
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Act & Assert
            plan.Items.Select(i => i.ReferenceRange).Should().Equal(
                plan.Items.Select(i => FootnoteEditor.LocateReferenceRange(i.Footnote) is { } r ? (r.Start, r.End) : ((int, int)?)null));

            // Assert
            html.GetText()[plan.Items[0].ReferenceRange!.Value.Start..plan.Items[0].ReferenceRange!.Value.End]
                .Should().Be("<sup><a href=\"#a\" id=\"a-back\">1</a></sup>");
        }
    }

    [Fact]
    public void The_plan_says_what_needs_a_look()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook(
            "<p>A<a href=\"#x\" id=\"x-back\">*</a> B<a href=\"#y\" id=\"y-back\">5</a> C<a href=\"#z\" id=\"z-back\">3</a>.</p>\n"
            + "<div id=\"x\"><p>Starred <a href=\"#x-back\">back</a> note.</p></div>\n"
            + "<div id=\"y\"><ul><li>A list.</li></ul><p><a href=\"#y-back\">5</a></p></div>\n"
            + "<div id=\"z\"><p><a href=\"#z-back\">3</a> Third.</p></div>");
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Assert
            plan.Items.Select(i => (i.Marker, i.NewNumber)).Should().Equal(("*", 1), ("5", 2), ("3", 3));
            plan.Items[0].Warnings.Should().Equal(ConversionWarning.NonNumericMarker, ConversionWarning.BacklinkInText);
            plan.Items[1].Warnings.Should().Equal(
                ConversionWarning.NumberChanges, ConversionWarning.BacklinkInText, ConversionWarning.FirstBlockNotParagraph);
            plan.Items[2].Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void The_plan_has_the_text_of_each_note_without_its_link_back()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook(
            "<p>A<a href=\"#x\" id=\"x-back\">1</a>.</p>\n"
            + "<div id=\"x\"><p><a href=\"#x-back\">1</a>. First <i>note</i>, &amp;\n  more.</p><p>Second.</p></div>");
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Assert
            plan.Items.Single().NoteText.Should().Be("First note, & more. Second.");
        }
    }

    [Fact]
    public void A_note_that_starts_with_a_list_gets_a_paragraph_for_its_link_back()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>A<a href=\"#y\" id=\"y-back\">1</a>.</p>\n"
            + "<div id=\"y\"><ul><li>A list.</li></ul><p><a href=\"#y-back\">1</a> After it.</p></div>");
        using (book)
        {
            // Act & Assert
            ConvertAll(book).Problems.Should().BeEmpty();

            // Assert
            html.GetText().Should().Contain(
                "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\">\n"
                + "    <p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a></p>\n"
                + "    <ul><li>A list.</li></ul>\n"
                + "    <p class=\"signet-fn-p\">After it.</p>\n"
                + "  </aside>");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Signet_footnotes_and_converted_ones_are_numbered_together()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>First<a href=\"#n\" id=\"n-back\">7</a> sentence.</p>\n<p>Second sentence.</p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">7</a> Old note.</p></div>");
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, html.GetText().IndexOf("Second", StringComparison.Ordinal) + 6, "Signet note", FootnotePlacement.Local);

            // Act & Assert
            ConvertAll(book).Problems.Should().BeEmpty();

            // Assert
            IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
            footnotes.Should().OnlyContain(f => f.IsSignet);
            footnotes.Select(f => f.NoteId).Should().Equal("fn-1", "fn-2");
            html.GetText().Should().Contain("id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> Old note.</p>");
        }
    }

    // Two notes nested by a comment that swallows the closing tag of the first (as in a scraped book), then a normal note.
    private const string NestedNotesBody =
        "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a> two<a epub:type=\"noteref\" href=\"#n2\" id=\"r2\">[2]</a>"
        + " three<a epub:type=\"noteref\" href=\"#n3\" id=\"r3\">[3]</a>.</p>\n"
        + "<section epub:type=\"footnotes\">\n"
        + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.<!--\n</aside-->\n"
        + "<aside epub:type=\"footnote\" id=\"n2\"><a href=\"#r2\">[2]</a> Second.</aside>\n</aside>\n"
        + "<aside epub:type=\"footnote\" id=\"n3\"><a href=\"#r3\">[3]</a> Third.</aside>\n"
        + "</section>";

    [Fact]
    public void Notes_nested_in_each_other_are_planned_with_a_warning_that_names_the_other_note()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook(NestedNotesBody);
        using (book)
        {
            // Act
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Assert
            plan.Items.Select(i => (i.Footnote.NoteId, i.CanConvert, i.NestedWith, i.Warnings.Contains(ConversionWarning.ContainsNote), i.Warnings.Contains(ConversionWarning.InsideNote)))
                .Should().Equal(
                    ("n1", true, "n2", true, false),
                    ("n2", true, "n1", false, true),
                    ("n3", true, string.Empty, false, false));
        }
    }

    [Fact]
    public void Notes_nested_in_each_other_become_separate_notes_and_the_outer_one_keeps_only_its_own_text()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(NestedNotesBody);
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            result.Converted.Should().Be(3);
            IReadOnlyList<Footnote> notes = FootnoteScanner.Scan(book);
            notes.Should().HaveCount(3).And.OnlyContain(n => n.IsSignet);
            string body = BodyOf(html);
            body.Should().NotContain("id=\"n1\"").And.NotContain("id=\"n2\"");
            string first = NoteText(body, notes[0].NoteId);
            string second = NoteText(body, notes[1].NoteId);
            first.Should().Contain("First.").And.NotContain("Second.");
            second.Should().Contain("Second.").And.NotContain("First.");
            NoteText(body, notes[2].NoteId).Should().Contain("Third.");
        }
    }

    // The markup of the note with the id, up to its closing tag (after the conversion the notes do not nest).
    private static string NoteText(string body, string id)
    {
        int start = body.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        start.Should().BePositive();
        return body[start..body.IndexOf("</aside>", start, StringComparison.Ordinal)];
    }

    /// <summary>
    /// The notes of one file all leave it in one write, each of them taking the wrapper it emptied. This is the case
    /// the conversion used to read, parse and rewrite the whole file for, once per note.
    /// </summary>
    [Fact]
    public void All_the_notes_of_one_file_leave_it_together_and_their_wrappers_go()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a> two<a epub:type=\"noteref\" href=\"#n2\" id=\"r2\">[2]</a>"
            + " three<a epub:type=\"noteref\" href=\"#n3\" id=\"r3\">[3]</a>.</p>\n"
            + "<div class=\"notes\">\n"
            + "<div class=\"n\"><aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside></div>\n"
            + "<div class=\"n\"><aside epub:type=\"footnote\" id=\"n2\"><a href=\"#r2\">[2]</a> Second.</aside></div>\n"
            + "<div class=\"n\"><aside epub:type=\"footnote\" id=\"n3\"><a href=\"#r3\">[3]</a> Third.</aside></div>\n"
            + "</div>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            result.Converted.Should().Be(3);
            string body = BodyOf(html);
            body.Should().NotContain("class=\"notes\"").And.NotContain("class=\"n\"")
                .And.NotContain("id=\"n1\"").And.NotContain("id=\"n2\"").And.NotContain("id=\"n3\"");
            IReadOnlyList<Footnote> notes = FootnoteScanner.Scan(book);
            notes.Should().HaveCount(3).And.OnlyContain(n => n.IsSignet);
            notes.Select(n => FootnoteEditor.GetContent(n)!.Text).Should().Equal("First.", "Second.", "Third.");
            body.IndexOf("id=\"" + notes[0].ReferenceId + "\"", StringComparison.Ordinal)
                .Should().BeLessThan(body.IndexOf("id=\"" + notes[1].ReferenceId + "\"", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A wrapper around several notes goes when the last of them is out — the note that leaves first does not take it,
    /// because the others are still inside. The conversion now decides that on the text as the whole of it leaves it,
    /// which says the same thing.
    /// </summary>
    [Fact]
    public void A_wrapper_that_held_several_notes_goes_once_all_of_them_are_out()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a> two<a epub:type=\"noteref\" href=\"#n2\" id=\"r2\">[2]</a>.</p>\n"
            + "<div class=\"wrap\">\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside>\n"
            + "<aside epub:type=\"footnote\" id=\"n2\"><a href=\"#r2\">[2]</a> Second.</aside>\n"
            + "</div>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            BodyOf(html).Should().NotContain("class=\"wrap\"");
        }
    }

    /// <summary>A wrapper left empty stays when something links to it: the link would lead nowhere.</summary>
    [Fact]
    public void An_emptied_wrapper_that_something_links_to_stays()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a>.</p>\n"
            + "<p><a href=\"#wrap\">To the notes</a></p>\n"
            + "<div id=\"wrap\">\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside>\n"
            + "</div>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            BodyOf(html).Should().Contain("id=\"wrap\"").And.NotContain("id=\"n1\"");
        }
    }

    /// <summary>The wrappers a note leaves empty go all the way up, but the body stays.</summary>
    [Fact]
    public void The_wrappers_a_note_leaves_empty_go_up_to_the_body()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a>.</p>\n"
            + "<div class=\"outer\">\n<div class=\"middle\">\n<div class=\"inner\">\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside>\n"
            + "</div>\n</div>\n</div>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            string body = BodyOf(html);
            body.Should().NotContain("class=\"outer\"").And.NotContain("class=\"middle\"").And.NotContain("class=\"inner\"");
            body.Should().StartWith("<body>").And.Contain("</body>");
        }
    }

    /// <summary>
    /// Every reference of a shared note in one paragraph is replaced. The references used to be taken the last first,
    /// because replacing one moved the ones behind it; they are now all read in the text as the conversion found it,
    /// so the order no longer matters — this test says the result is the same either way.
    /// </summary>
    [Fact]
    public void Every_reference_of_a_shared_note_in_one_paragraph_is_replaced()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a> two<a epub:type=\"noteref\" href=\"#n1\">[1]</a>"
            + " three<a epub:type=\"noteref\" href=\"#n1\">[1]</a>.</p>\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            string body = BodyOf(html);
            body.Should().NotContain("href=\"#n1\"");
            Regex.Matches(body, "class=\"signet-fn-ref\"").Should().HaveCount(3);
            FootnoteScanner.Scan(book).Should().HaveCount(3).And.OnlyContain(n => n.IsSignet);
        }
    }

    /// <summary>
    /// References of different notes standing next to each other keep their order and their numbers: their
    /// replacements are collected together and written in one go, so none of them may move another.
    /// </summary>
    [Fact]
    public void References_of_different_notes_next_to_each_other_keep_their_order()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a><a epub:type=\"noteref\" href=\"#n2\" id=\"r2\">[2]</a>"
            + "<a epub:type=\"noteref\" href=\"#n3\" id=\"r3\">[3]</a>.</p>\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside>\n"
            + "<aside epub:type=\"footnote\" id=\"n2\"><a href=\"#r2\">[2]</a> Second.</aside>\n"
            + "<aside epub:type=\"footnote\" id=\"n3\"><a href=\"#r3\">[3]</a> Third.</aside>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            string body = BodyOf(html);
            Regex.Matches(body, "signet-fn-ref\" epub:type=\"noteref\" href=\"#(fn-\\d+)\"")
                .Select(m => m.Groups[1].Value).Should().Equal("fn-1", "fn-2", "fn-3");
            FootnoteScanner.Scan(book).Select(n => FootnoteEditor.GetContent(n)!.Text)
                .Should().Equal("First.", "Second.", "Third.");
        }
    }

    /// <summary>
    /// A footnote citing a footnote: the reference of one note stands in the text of another. Every reference is
    /// replaced before any note is cut out, so the citing note takes Signet's reference into its own new text.
    /// </summary>
    [Fact]
    public void A_note_cited_from_inside_another_note_is_converted_with_it()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<a epub:type=\"noteref\" href=\"#nB\" id=\"rB\">[B]</a>.</p>\n"
            + "<section epub:type=\"footnotes\">\n"
            + "<aside epub:type=\"footnote\" id=\"nB\"><a href=\"#rB\">[B]</a> Bee, see also"
            + "<a epub:type=\"noteref\" href=\"#nA\" id=\"rA\">[A]</a>.</aside>\n"
            + "<aside epub:type=\"footnote\" id=\"nA\"><a href=\"#rA\">[A]</a> Aye.</aside>\n"
            + "</section>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            string body = BodyOf(html);
            result.Problems.Should().BeEmpty();
            result.Converted.Should().Be(2);
            body.Should().NotContain("fn-new-", "no temporary id is left behind")
                .And.NotContain("id=\"nA\"").And.NotContain("id=\"nB\"");

            // The citing note holds Signet's reference to the cited one, and the cited one links back to it.
            body.Should().Contain(
                "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
                + "<a href=\"#fn-1-ref\">1.</a> Bee, see also"
                + "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-2\" id=\"fn-2-ref\">[2]</a>.</p></aside>")
                .And.Contain(
                "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-2\"><p class=\"signet-fn-p\">"
                + "<a href=\"#fn-2-ref\">2.</a> Aye.</p></aside>");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    /// <summary>
    /// Two notes that cite each other — each one's reference stands in the other's text. No order of whole footnotes
    /// can serve this: whichever went first would be cut out before the other's reference was replaced. Replacing
    /// every reference before cutting any note settles it without an order at all, so a book that does this converts
    /// like any other instead of being refused.
    /// </summary>
    [Fact]
    public void Two_notes_that_cite_each_other_are_both_converted()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Start<a epub:type=\"noteref\" href=\"#nA\" id=\"rA0\">[A]</a>.</p>\n"
            + "<section epub:type=\"footnotes\">\n"
            + "<aside epub:type=\"footnote\" id=\"nA\"><a href=\"#rA0\">[A]</a> Aye, cf."
            + "<a epub:type=\"noteref\" href=\"#nB\" id=\"rB\">[B]</a>.</aside>\n"
            + "<aside epub:type=\"footnote\" id=\"nB\"><a href=\"#rB\">[B]</a> Bee, cf."
            + "<a epub:type=\"noteref\" href=\"#nA\" id=\"rA1\">[A2]</a>.</aside>\n"
            + "</section>");
        using (book)
        {
            // Act
            ConversionResult result = ConvertAll(book);

            // Assert
            result.Problems.Should().BeEmpty();
            result.Converted.Should().Be(2);
            string body = BodyOf(html);

            // "Aye" is shared: read from the text and from inside "Bee", so it gets a letter per reference.
            body.Should().Contain(
                "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\">"
                + "<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. Aye, cf."
                + "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-2\" id=\"fn-2-ref\">[2]</a>.</p></aside>")
                .And.Contain(
                "<aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-2\"><p class=\"signet-fn-p\">"
                + "<a href=\"#fn-2-ref\">2.</a> Bee, cf."
                + "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1b-ref\">[1b]</a>.</p></aside>");
            body.Should().NotContain("fn-new-").And.NotContain("id=\"nA\"").And.NotContain("id=\"nB\"");

            // Act & Assert
            FootnoteValidator.Validate(book).Should().BeEmpty();
            FootnoteScanner.Scan(book).Should().HaveCount(3).And.OnlyContain(f => f.IsSignet);
        }
    }

    [Fact]
    public void Converting_reports_its_stages_and_how_many_footnotes_are_done()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook(
            "<p>One<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">[1]</a> two<a epub:type=\"noteref\" href=\"#n2\" id=\"r2\">[2]</a>.</p>\n"
            + "<section epub:type=\"footnotes\">\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">[1]</a> First.</aside>\n"
            + "<aside epub:type=\"footnote\" id=\"n2\"><a href=\"#r2\">[2]</a> Second.</aside>\n"
            + "</section>");
        using (book)
        {
            List<ConversionProgress> reports = new();
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            int files = book.GetHtmlResources().Count;

            // Act
            ConversionResult result = FootnoteEditor.Convert(
                book, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, new SyncProgress(reports.Add),
                TestContext.Current.CancellationToken);

            // Assert
            result.Problems.Should().BeEmpty();
            reports.Select(r => r.Stage).Distinct().Should().Equal(
                ConversionStage.Preparing, ConversionStage.Converting, ConversionStage.Finishing, ConversionStage.Checking);
            reports.Where(r => r.Stage == ConversionStage.Converting).Select(r => r.Done).Should().BeInAscendingOrder().And.EndWith(2);
            reports.Where(r => r.Stage != ConversionStage.Preparing).Should().OnlyContain(r => r.Total == 2);
            List<ConversionProgress> preparing = reports.Where(r => r.Stage == ConversionStage.Preparing).ToList();
            preparing[0].Total.Should().Be(0, "the count of the files is not known before the scan starts");
            preparing.Skip(1).Should().NotBeEmpty().And.OnlyContain(r => r.Total == files);
            preparing[^1].Done.Should().Be(files);
        }
    }

    [Fact]
    public void A_cancelled_token_stops_the_conversion_with_an_OperationCanceledException()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook(NestedNotesBody);
        using (book)
        {
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            // Act & Assert
            book.Invoking(b => FootnoteEditor.Convert(b, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, null, cancelled.Token))
                .Should().Throw<OperationCanceledException>();
        }
    }

    [Fact]
    public void The_conversion_can_be_cancelled_in_the_middle_of_the_footnotes()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook(NestedNotesBody);
        using (book)
        {
            using CancellationTokenSource source = new();
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            SyncProgress cancelWhenConverting = new(report =>
            {
                if (report.Stage == ConversionStage.Converting)
                {
                    source.Cancel();
                }
            });

            // Act & Assert
            book.Invoking(b => FootnoteEditor.Convert(b, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, cancelWhenConverting, source.Token))
                .Should().Throw<OperationCanceledException>();
        }
    }

    [Fact]
    public void Planning_the_conversion_reports_a_file_scanned_of_the_books_total()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p>Text.</p>");
        using (book)
        {
            AddFile(book, "<p>More text.</p>");
            int totalFiles = book.GetHtmlResources().Count;
            List<ScanProgress> reported = new();

            // Act
            FootnoteEditor.PlanConversion(book, new ScanSyncProgress(reported.Add));

            // Assert
            reported.Select(r => r.Done).Should().Equal(Enumerable.Range(0, totalFiles + 1));
            reported.Should().OnlyContain(r => r.Total == totalFiles);
        }
    }

    // Reports on the thread that reports (Progress<T> would post them to another one).
    private sealed class SyncProgress : IProgress<ConversionProgress>
    {
        private readonly Action<ConversionProgress> _report;

        public SyncProgress(Action<ConversionProgress> report) => _report = report;

        public void Report(ConversionProgress value) => _report(value);
    }

    private sealed class ScanSyncProgress : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _report;

        public ScanSyncProgress(Action<ScanProgress> report) => _report = report;

        public void Report(ScanProgress value) => _report(value);
    }
}
