using System;
using System.Collections.Generic;
using System.Linq;
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
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup class=\"s\"><a class=\"r\" epub:type=\"noteref\" href=\"#n1\" id=\"n1-back\">1</a></sup>.</p>\n"
            + "<hr/>\n<section epub:type=\"footnotes\">\n"
            + "<aside class=\"note\" epub:type=\"footnote\" id=\"n1\"><p class=\"pn\"><a href=\"#n1-back\">1</a>. First <i>note</i>.</p><p>More.</p></aside>\n"
            + "</section>");
        using (book)
        {
            ConversionResult result = ConvertAll(book);

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
            Footnote footnote = FootnoteScanner.Scan(book).Single();
            footnote.IsSignet.Should().BeTrue();
            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent("First <i>note</i>.\n\nMore.", false));
        }
    }

    [Fact]
    public void The_classes_of_the_converted_elements_can_stay()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup class=\"s\"><a class=\"r\" epub:type=\"noteref\" href=\"#n1\" id=\"n1-back\">1</a></sup>.</p>\n"
            + "<aside class=\"note\" epub:type=\"footnote\" id=\"n1\"><p class=\"pn\"><a href=\"#n1-back\">1</a> Note.</p></aside>");
        using (book)
        {
            ConvertAll(book, keepClasses: true).Problems.Should().BeEmpty();

            string text = html.GetText();
            text.Should().Contain("<a class=\"signet-fn-ref s r\" epub:type=\"noteref\"")
                .And.Contain("<aside class=\"signet-fn note\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p pn\"><a href=\"#fn-1-ref\">1.</a> Note.</p></aside>");
        }
    }

    [Fact]
    public void InDesign_notes_of_a_notes_file_are_taken_over_with_the_links_to_them()
    {
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
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            plan.AutomaticFile!.File.Should().BeSameAs(notes);
            plan.Items.Should().OnlyContain(i => i.CanConvert);
            plan.Items[1].Warnings.Should().Equal(ConversionWarning.LinkedElsewhere);

            ConversionResult result = FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, notes, false));

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
    public void The_headings_between_the_notes_of_a_taken_over_file_become_groups()
    {
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
            ConvertAll(book, notes).Problems.Should().BeEmpty();

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
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            plan.Candidates.Should().HaveCount(2);
            plan.AutomaticFile.Should().BeNull();

            ConversionResult result = FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false));

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
    public void A_note_with_several_references_stays_as_it_is()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>A<a href=\"#n\" id=\"n-back\">1</a> again<a href=\"#n\">1</a>.</p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">1</a> Shared note.</p></div>");
        using (book)
        {
            string before = html.GetText();
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            plan.Items.Single().CanConvert.Should().BeFalse();
            FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false)).Converted.Should().Be(0);
            html.GetText().Should().Be(before);
        }
    }

    [Fact]
    public void The_plan_says_what_needs_a_look()
    {
        (Book book, HtmlResource _) = NewBook(
            "<p>A<a href=\"#x\" id=\"x-back\">*</a> B<a href=\"#y\" id=\"y-back\">5</a> C<a href=\"#z\" id=\"z-back\">3</a>.</p>\n"
            + "<div id=\"x\"><p>Starred <a href=\"#x-back\">back</a> note.</p></div>\n"
            + "<div id=\"y\"><ul><li>A list.</li></ul><p><a href=\"#y-back\">5</a></p></div>\n"
            + "<div id=\"z\"><p><a href=\"#z-back\">3</a> Third.</p></div>");
        using (book)
        {
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            plan.Items.Select(i => (i.Marker, i.NewNumber)).Should().Equal(("*", 1), ("5", 2), ("3", 3));
            plan.Items[0].Warnings.Should().Equal(ConversionWarning.NonNumericMarker, ConversionWarning.BacklinkInText);
            plan.Items[1].Warnings.Should().Equal(
                ConversionWarning.NumberChanges, ConversionWarning.BacklinkInText, ConversionWarning.FirstBlockNotParagraph);
            plan.Items[2].Warnings.Should().BeEmpty();
        }
    }

    [Fact]
    public void A_note_that_starts_with_a_list_gets_a_paragraph_for_its_link_back()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>A<a href=\"#y\" id=\"y-back\">1</a>.</p>\n"
            + "<div id=\"y\"><ul><li>A list.</li></ul><p><a href=\"#y-back\">1</a> After it.</p></div>");
        using (book)
        {
            ConvertAll(book).Problems.Should().BeEmpty();

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
        (Book book, HtmlResource html) = NewBook(
            "<p>First<a href=\"#n\" id=\"n-back\">7</a> sentence.</p>\n<p>Second sentence.</p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">7</a> Old note.</p></div>");
        using (book)
        {
            FootnoteEditor.Add(book, html, html.GetText().IndexOf("Second", StringComparison.Ordinal) + 6, "Signet note", FootnotePlacement.Local);

            ConvertAll(book).Problems.Should().BeEmpty();

            IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
            footnotes.Should().OnlyContain(f => f.IsSignet);
            footnotes.Select(f => f.NoteId).Should().Equal("fn-1", "fn-2");
            html.GetText().Should().Contain("id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> Old note.</p>");
        }
    }
}
