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
            plan.Items[1].Links.Should().Equal(new ConversionLink(
                chapter, chapter.GetText().IndexOf("<a href=\"" + notes.Filename + "#footnote-2\">see", StringComparison.Ordinal), IsReference: false));
            plan.Items[1].OtherLinks.Should().Equal(chapter.BookPath);

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
    public void A_note_with_several_references_becomes_a_shared_note()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>A<a href=\"#n\" id=\"n-back\">1</a> again<a href=\"#n\">1</a>.</p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">1</a> Shared note.</p></div>");
        using (book)
        {
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionItem item = plan.Items.Single();
            item.CanConvert.Should().BeTrue();
            item.Notices.Should().Equal(ConversionNotice.SharedNote);
            item.Warnings.Should().BeEmpty("a shared note is for information only");
            item.Links.Should().BeEmpty("the other reference is the note's own");
            item.References.Select(r => r.ReferenceIndex).Should().Equal(0, 1);

            ConversionResult result = FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false));

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
        (Book book, HtmlResource html) = NewBook(
            "<p>Balak<sup class=\"fnref\"><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>, Gilgal"
            + "<sup class=\"fnref\"><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>, works"
            + "<sup class=\"fnref\"><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>.</p>\n"
            + "<aside id=\"fn4\" epub:type=\"footnote\" class=\"footnote\">\n  <a href=\"#ref4\" class=\"fn-back\">[4]</a> Three places.\n</aside>");
        using (book)
        {
            ConversionResult result = ConvertAll(book);

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
        (Book book, HtmlResource first) = NewBook(
            "<p>Here<a epub:type=\"noteref\" href=\"#n\" id=\"n-back\">1</a>.</p>\n"
            + "<aside epub:type=\"footnote\" id=\"n\"><a href=\"#n-back\">1</a> Note.</aside>");
        using (book)
        {
            HtmlResource second = AddFile(book, "<p>There<a epub:type=\"noteref\" href=\""
                + first.Filename + "#n\">1</a>.</p>");
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionItem item = plan.Items.Single();
            item.Footnote.Placement.Should().Be(FootnotePlacement.Global);
            item.Notices.Should().Equal(ConversionNotice.SharedNote, ConversionNotice.BecomesGlobal);

            FootnoteEditor.Convert(book, new ConversionRequest(plan.Items, null, false)).Problems.Should().BeEmpty();

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.GetText().Should().Contain("id=\"en-1\"").And.Contain(">1a</a>, <a href=").And.Contain(">1b</a>. Note.");
            BodyOf(first).Should().Contain("[1a]</a>").And.NotContain("aside");
            BodyOf(second).Should().Contain("[1b]</a>");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void The_plan_gives_the_range_of_each_reference()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>A<sup><a href=\"#a\" id=\"a-back\">1</a></sup> B<a href=\"#b\" id=\"b-back\">2</a>.</p>\n"
            + "<div id=\"a\"><p><a href=\"#a-back\">1</a> First.</p></div>\n"
            + "<div id=\"b\"><p><a href=\"#b-back\">2</a> Second.</p></div>");
        using (book)
        {
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            plan.Items.Select(i => i.ReferenceRange).Should().Equal(
                plan.Items.Select(i => FootnoteEditor.LocateReferenceRange(i.Footnote) is { } r ? (r.Start, r.End) : ((int, int)?)null));
            html.GetText()[plan.Items[0].ReferenceRange!.Value.Start..plan.Items[0].ReferenceRange!.Value.End]
                .Should().Be("<sup><a href=\"#a\" id=\"a-back\">1</a></sup>");
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
    public void The_plan_has_the_text_of_each_note_without_its_link_back()
    {
        (Book book, HtmlResource _) = NewBook(
            "<p>A<a href=\"#x\" id=\"x-back\">1</a>.</p>\n"
            + "<div id=\"x\"><p><a href=\"#x-back\">1</a>. First <i>note</i>, &amp;\n  more.</p><p>Second.</p></div>");
        using (book)
        {
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);

            plan.Items.Single().NoteText.Should().Be("First note, & more. Second.");
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
