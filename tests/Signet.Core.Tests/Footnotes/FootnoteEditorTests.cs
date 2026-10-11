using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>Tests of <see cref="FootnoteEditor"/> and <see cref="FootnoteScanner"/> — adding, removing, moving and renumbering footnotes.</summary>
public sealed class FootnoteEditorTests
{
    private const string Body = "<p>First sentence.</p>\n<p>Second sentence.</p>";

    private static string Xhtml(string body, string head = "") =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>" + head + "\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body = Body, string version = "3.0")
    {
        Book book = BookCreator.CreateNewBook(version);
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    // The markup of the body (the head holds the footnote rules).
    private static string BodyOf(HtmlResource html) =>
        html.GetText()[html.GetText().IndexOf("<body>", StringComparison.Ordinal)..];

    // The offset right after the given text.
    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    private static void ShouldBeConsistent(Book book) =>
        FootnoteValidator.Validate(book).Should().BeEmpty("the reference and the note point at each other");

    [Fact]
    public void A_local_footnote_goes_to_a_section_at_the_end_of_its_file()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "A <i>note</i>.", FootnotePlacement.Local)
                .Should().Be(FootnoteError.None);

            string text = html.GetText();
            text.Should().Contain(
                "<p>First<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a> sentence.</p>");
            text.Should().Contain("<p>Second sentence.</p>\n<hr class=\"signet-fn-separator\"/>\n"
                + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
                + "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p><a href=\"#fn-1-ref\">1.</a> A <i>note</i>.</p></aside>\n"
                + "</section>\n</body>");
            text.Should().Contain(".signet-fn-ref {", "the reference is styled as a superscript");
            book.Modified.Should().BeTrue();
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Local_footnotes_are_numbered_in_text_order()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "Second"), "second note", FootnotePlacement.Local);
            FootnoteEditor.Add(book, html, After(html, "First"), "first note", FootnotePlacement.Local);

            string text = html.GetText();
            text.Should().Contain("First<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
                .And.Contain("Second<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-2\" id=\"fn-2-ref\">[2]</a>");
            text.IndexOf("first note", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("second note", StringComparison.Ordinal));
            text.Should().Contain("id=\"fn-1\"><p><a href=\"#fn-1-ref\">1.</a> first note");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Local_numbering_starts_from_one_in_every_file_and_global_numbering_runs_through_the_book()
    {
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));
            FootnoteEditor.Add(book, second, After(second, "First"), "global in second", FootnotePlacement.Global);
            FootnoteEditor.Add(book, second, After(second, "Second"), "local in second", FootnotePlacement.Local);
            FootnoteEditor.Add(book, first, After(first, "First"), "global in first", FootnotePlacement.Global);
            FootnoteEditor.Add(book, first, After(first, "Second"), "local in first", FootnotePlacement.Local);

            first.GetText().Should().Contain(">[1]</a> sentence.</p>\n<p>Second<a").And.Contain("href=\"#fn-1\"");
            second.GetText().Should().Contain("href=\"#fn-1\"").And.Contain("[2]</a> sentence.</p>\n<p>Second<a");
            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            string notesText = notes.GetText();
            notesText.IndexOf("global in first", StringComparison.Ordinal)
                .Should().BeLessThan(notesText.IndexOf("global in second", StringComparison.Ordinal));
            notesText.Should().Contain("id=\"en-1\"").And.Contain("id=\"en-2\"");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void The_global_notes_file_is_created_at_the_end_and_removed_with_its_last_note()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "endnote", FootnotePlacement.Global);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);
            book.GetHtmlResources()[^1].Should().BeSameAs(notes);
            notes.GetText().Should().Contain("<h1>Przypisy</h1>")
                .And.Contain("<aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p><a href=\""
                    + Core.BookPath.Relative(notes.BookPath, html.BookPath) + "#en-1-ref\">1.</a> endnote</p></aside>");
            html.GetText().Should().Contain("href=\"" + Core.BookPath.Relative(html.BookPath, notes.BookPath) + "#en-1\"");
            ShouldBeConsistent(book);

            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            FootnoteEditor.FindGlobalFile(book).Should().BeNull();
            book.GetAllResources().Should().NotContain(notes);
        }
    }

    [Fact]
    public void Removing_the_last_local_footnote_leaves_the_file_as_it_was()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local);
            BodyOf(html).Should().Contain("signet-footnotes");

            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            BodyOf(html).Should().Be("<body>\n" + Body + "\n</body>\n</html>");
            html.GetText().Should().Contain(".signet-footnotes {", "the rules stay in the book");
        }
    }

    [Fact]
    public void A_footnote_moves_from_local_to_global_and_back()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "moving note", FootnotePlacement.Local);

            FootnoteEditor.Move(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.GetText().Should().Contain("epub:type=\"endnote\" id=\"en-1\"").And.Contain("moving note");
            BodyOf(html).Should().NotContain("signet-footnotes").And.Contain("#en-1\" id=\"en-1-ref\">[1]</a>");
            FootnoteScanner.Scan(book).Single().Placement.Should().Be(FootnotePlacement.Global);
            ShouldBeConsistent(book);

            FootnoteEditor.Move(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            FootnoteEditor.FindGlobalFile(book).Should().BeNull("the emptied notes file is removed");
            html.GetText().Should().Contain("href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
                .And.Contain("epub:type=\"footnote\" id=\"fn-1\"><p><a href=\"#fn-1-ref\">1.</a> moving note");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Add_and_Move_return_the_footnote_after_the_renumbering()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "Second"), "second note", FootnotePlacement.Local);
            FootnoteEditor.Add(book, html, After(html, "First"), "first note", FootnotePlacement.Local, out Footnote? added)
                .Should().Be(FootnoteError.None);

            added!.NoteId.Should().Be("fn-1", "the new footnote comes first in the text");
            added.ReferenceId.Should().Be("fn-1-ref");
            FootnoteEditor.Locate(added, FootnoteSide.Reference)!.Value.Offset
                .Should().Be(html.GetText().IndexOf("<a class=\"signet-fn-ref\"", StringComparison.Ordinal));

            FootnoteEditor.Move(book, added, out Footnote? moved).Should().BeTrue();

            moved!.Placement.Should().Be(FootnotePlacement.Global);
            moved.NoteId.Should().Be("en-1");
            moved.NoteFile.Should().BeSameAs(FootnoteEditor.FindGlobalFile(book));
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void NumberAt_is_the_number_the_renumbering_gives_a_new_footnote()
    {
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));
            FootnoteEditor.Add(book, first, After(first, "Second"), "global in the first file", FootnotePlacement.Global);
            FootnoteEditor.Add(book, first, After(first, "First"), "local", FootnotePlacement.Local);
            FootnoteEditor.Add(book, second, After(second, "Second"), "global in the second file", FootnotePlacement.Global);

            // Between the two references of the first file: one local and no global footnote before it.
            int middle = After(first, " sentence.</p>");
            FootnoteEditor.NumberAt(book, first, middle, FootnotePlacement.Local).Should().Be(2);
            FootnoteEditor.NumberAt(book, first, middle, FootnotePlacement.Global).Should().Be(1);

            // At the start of the second file: no local footnote of that file, the global one of the first file before it.
            int start = After(second, "<p>");
            FootnoteEditor.NumberAt(book, second, start, FootnotePlacement.Local).Should().Be(1);
            FootnoteEditor.NumberAt(book, second, start, FootnotePlacement.Global).Should().Be(2);

            FootnoteEditor.Add(book, second, start, "new", FootnotePlacement.Global, out Footnote? added);
            FootnoteEditor.Locate(added!, FootnoteSide.Reference)!.Value.File.GetText().Should().Contain(">[2]</a>First");
        }
    }

    [Fact]
    public void Refresh_finds_a_footnote_of_an_earlier_scan_by_its_reference()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "first note", FootnotePlacement.Local, out Footnote? first);
            FootnoteEditor.Add(book, html, After(html, "Second"), "second note", FootnotePlacement.Local, out Footnote? second);

            FootnoteEditor.Refresh(book, first!)!.NoteId.Should().Be("fn-1");
            FootnoteEditor.Remove(book, FootnoteEditor.Refresh(book, second!)!).Should().BeTrue();

            FootnoteEditor.Refresh(book, second!).Should().BeNull("its reference is gone");
        }
    }

    [Fact]
    public void FindAt_tells_the_reference_from_the_note()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "the note", FootnotePlacement.Local);
            string text = html.GetText();

            FootnoteEditor.FindAt(book, html, text.IndexOf("[1]", StringComparison.Ordinal))!.Side.Should().Be(FootnoteSide.Reference);
            FootnoteEditor.FindAt(book, html, text.IndexOf("the note", StringComparison.Ordinal))!.Side.Should().Be(FootnoteSide.Note);
            FootnoteEditor.FindAt(book, html, text.IndexOf("Second", StringComparison.Ordinal)).Should().BeNull();
        }
    }

    [Fact]
    public void A_footnote_cannot_be_added_where_a_reference_does_not_belong()
    {
        (Book book, HtmlResource html) = NewBook("<p>Some <a href=\"#x\">link</a> text.</p>\n<p id=\"x\">x</p>");
        using (book)
        {
            string text = html.GetText();
            FootnoteEditor.CanAdd(book, html, text.IndexOf("<title>", StringComparison.Ordinal)).Should().Be(FootnoteError.NotInBody);
            FootnoteEditor.CanAdd(book, html, text.IndexOf("href", StringComparison.Ordinal)).Should().Be(FootnoteError.InsideTag);
            FootnoteEditor.CanAdd(book, html, After(html, ">li")).Should().Be(FootnoteError.InsideLink);
            FootnoteEditor.CanAdd(book, html, After(html, "Some")).Should().Be(FootnoteError.None);

            FootnoteEditor.Add(book, html, After(html, "Some"), "note", FootnotePlacement.Local);
            FootnoteEditor.CanAdd(book, html, html.GetText().IndexOf("> note", StringComparison.Ordinal) + 3)
                .Should().Be(FootnoteError.InsideFootnote);
        }
    }

    [Theory]
    [InlineData("plain text", FootnoteError.None)]
    [InlineData("with <i>italics</i> &amp; an entity", FootnoteError.None)]
    [InlineData("  ", FootnoteError.EmptyContent)]
    [InlineData("an <i>unclosed tag", FootnoteError.InvalidContent)]
    [InlineData("a bare < sign", FootnoteError.InvalidContent)]
    public void The_text_of_a_footnote_must_be_well_formed(string content, FootnoteError expected)
    {
        FootnoteEditor.ValidateContent(content, "3.0").Should().Be(expected);
    }

    [Fact]
    public void An_epub2_footnote_uses_divs_without_epub_type()
    {
        (Book book, HtmlResource html) = NewBook(version: "2.0");
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            string text = html.GetText();
            text.Should().NotContain("epub:type").And.NotContain("<aside").And.NotContain("<section");
            text.Should().Contain("<div class=\"signet-footnotes\">").And.Contain("<div class=\"signet-fn\" id=\"fn-1\">");
            FootnoteScanner.Scan(book).Should().ContainSingle();
        }
    }

    [Fact]
    public void The_reference_style_goes_to_the_linked_stylesheet()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            css.SetText("p { margin: 0; }\n");
            html.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(html.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));

            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local);
            FootnoteEditor.Add(book, html, After(html, "Second"), "note 2", FootnotePlacement.Local);

            css.GetText().Should().Be("p { margin: 0; }\n\n" + FootnoteStyles.Default.ApplyTo(string.Empty));
            css.GetText().Should().Contain(".signet-fn-ref {\n  vertical-align: super;\n  font-size: smaller;\n  line-height: normal;\n}\n");
            html.GetText().Should().NotContain("<style");
        }
    }

    [Fact]
    public void Every_footnote_operation_keeps_the_rules_equal_to_the_styles()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            html.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(html.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));
            FootnoteStyles first = FootnoteStyles.Default with { Note = "color: blue;" };
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local, out _, first);
            css.GetText().Should().Contain(".signet-fn {\n  color: blue;\n}");

            FootnoteEditor.Renumber(book, first).Should().BeFalse("everything is up to date");
            FootnoteEditor.Renumber(book, first with { Note = "color: green;" }).Should().BeTrue();

            css.GetText().Should().Contain(".signet-fn {\n  color: green;\n}").And.NotContain("blue");
        }
    }

    [Fact]
    public void The_notes_file_links_the_stylesheet_of_the_reference()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            html.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(html.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));

            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Global);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.GetLinkedStylesheets().Should().Equal(css.BookPath);
            notes.GetText().Should().NotContain("<style", "the rules are in the linked stylesheet");
            css.GetText().Should().Contain(".signet-endnotes {");
        }
    }

    [Fact]
    public void Other_footnotes_get_new_numbers_but_keep_their_ids_and_look()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup class=\"sup_note\"><a class=\"a_note\" epub:type=\"noteref\" href=\"#footnote-07\" id=\"footnote-07-backlink\">[7]</a></sup>.</p>\n"
            + "<aside class=\"aside_footnote\" id=\"footnote-07\" epub:type=\"footnote\">"
            + "<p class=\"p_footnote\"><a class=\"p_a_footnote\" href=\"#footnote-07-backlink\">7.</a> B. Lapunow.</p></aside>");
        using (book)
        {
            FootnoteEditor.Renumber(book).Should().BeTrue();

            string text = html.GetText();
            text.Should().Contain("id=\"footnote-07-backlink\">[1]</a></sup>").And.Contain("href=\"#footnote-07-backlink\">1.</a>");
            Footnote footnote = FootnoteScanner.Scan(book).Single();
            footnote.IsSignet.Should().BeFalse();
            FootnoteEditor.FindAt(book, html, text.IndexOf("<sup", StringComparison.Ordinal) + 2)!.Side
                .Should().Be(FootnoteSide.Reference, "the <sup> around the link is part of the reference");

            FootnoteEditor.Remove(book, footnote).Should().BeTrue();

            html.GetText().Should().Contain("<p>Text.</p>").And.NotContain("aside");
        }
    }

    [Fact]
    public void The_content_of_a_signet_note_is_the_text_after_its_link_back_and_edits_keep_the_template()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "A <i>note</i>.", FootnotePlacement.Local, out Footnote? footnote);

            FootnoteEditor.GetContent(footnote!).Should().Be(new FootnoteContent("A <i>note</i>.", false));
            FootnoteEditor.Edit(book, footnote!, "  The <b>new</b> text.\n", FootnotePlacement.Local, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            html.GetText().Should().Contain("id=\"fn-1\"><p><a href=\"#fn-1-ref\">1.</a> The <b>new</b> text.</p></aside>")
                .And.NotContain("A <i>note</i>.");
            edited!.NoteId.Should().Be("fn-1");
            FootnoteEditor.GetContent(edited)!.Text.Should().Be("The <b>new</b> text.");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void An_edit_with_the_other_placement_changes_the_text_and_moves_the_note()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "old text", FootnotePlacement.Local, out Footnote? footnote);

            FootnoteEditor.Edit(book, footnote!, "new text", FootnotePlacement.Global, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            edited!.Placement.Should().Be(FootnotePlacement.Global);
            edited.NoteId.Should().Be("en-1");
            FootnoteEditor.FindGlobalFile(book)!.GetText().Should().Contain("<a href=\"" + html.Filename + "#en-1-ref\">1.</a> new text</p>");
            BodyOf(html).Should().NotContain("old text").And.NotContain("signet-footnotes");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void An_invalid_edit_changes_nothing()
    {
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "old text", FootnotePlacement.Local, out Footnote? footnote);
            string before = html.GetText();

            FootnoteEditor.Edit(book, footnote!, "a < b", FootnotePlacement.Global, out Footnote? edited)
                .Should().Be(FootnoteError.InvalidContent);
            FootnoteEditor.ValidateEdit(footnote!, " ", "3.0").Should().Be(FootnoteError.EmptyContent);

            edited.Should().BeNull();
            html.GetText().Should().Be(before);
            FootnoteEditor.FindGlobalFile(book).Should().BeNull();
        }
    }

    [Fact]
    public void The_content_of_another_note_is_its_whole_inside_which_must_keep_the_link_back()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup><a epub:type=\"noteref\" href=\"#note-7\" id=\"note-7-back\">[7]</a></sup>.</p>\n"
            + "<aside class=\"aside_note\" id=\"note-7\" epub:type=\"footnote\">\n"
            + "  <p class=\"p_note\"><a href=\"#note-7-back\">7.</a> First part.</p>\n  <p>Second part.</p>\n</aside>");
        using (book)
        {
            Footnote footnote = FootnoteScanner.Scan(book).Single();
            string inside = "<p class=\"p_note\"><a href=\"#note-7-back\">7.</a> First part.</p>\n  <p>Second part.</p>";

            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent(inside, true));
            FootnoteEditor.ValidateEdit(footnote, "<p>No way back.</p>", "3.0").Should().Be(FootnoteError.MissingBacklink);
            FootnoteEditor.Edit(book, footnote, "<p><a href=\"#note-7-back\">7.</a> Only part.</p>", FootnotePlacement.Local, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            html.GetText().Should().Contain(
                "epub:type=\"footnote\">\n  <p><a href=\"#note-7-back\">7.</a> Only part.</p>\n</aside>",
                "only the inside is replaced, the whitespace around it stays");
            edited!.NoteId.Should().Be("note-7");
            edited.IsSignet.Should().BeFalse();
        }
    }

    [Fact]
    public void The_reference_range_holds_the_sup_around_the_link()
    {
        const string reference = "<sup><a epub:type=\"noteref\" href=\"#n1\" id=\"n1-back\">1</a></sup>";
        (Book book, HtmlResource html) = NewBook(
            "<p>Text" + reference + ".</p>\n<aside epub:type=\"footnote\" id=\"n1\"><p><a href=\"#n1-back\">1</a> Note.</p></aside>");
        using (book)
        {
            (HtmlResource file, int start, int end) = FootnoteEditor.LocateReferenceRange(FootnoteScanner.Scan(book).Single())!.Value;

            file.Should().BeSameAs(html);
            html.GetText()[start..end].Should().Be(reference);
        }
    }

    // InDesign's footnotes as exported through calibre: the reference sits in a <span> its note links back to, or (in a
    // heading) next to an empty <span> the note links back to, inside the link before it.
    private const string InDesignBody =
        "<h1 id=\"ch\"><a href=\"#toc\">Chapter<sup><span id=\"footnote-9-backlink\"></span></sup></a>"
        + "<a href=\"#footnote-9\"><sup>[5]</sup></a></h1>\n"
        + "<p>Text<span id=\"footnote-8-backlink\"><a href=\"#footnote-8\">7</a></span> more.</p>\n"
        + "<p id=\"toc\">Contents</p>\n"
        + "<div id=\"footnote-9\"><p><a href=\"#footnote-9-backlink\">5</a> First note.</p></div>\n"
        + "<div id=\"footnote-8\"><p><a href=\"#footnote-8-backlink\">7</a> Second note.</p></div>";

    [Fact]
    public void A_link_back_to_the_wrapper_of_the_reference_or_to_an_empty_element_right_before_it_makes_a_footnote()
    {
        (Book book, HtmlResource _) = NewBook(InDesignBody);
        using (book)
        {
            IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

            footnotes.Select(f => (f.NoteId, f.ReferenceHref, f.ReferenceId)).Should().Equal(
                ("footnote-9", "#footnote-9", "footnote-9-backlink"),
                ("footnote-8", "#footnote-8", "footnote-8-backlink"));
            footnotes.Should().OnlyContain(f => !f.IsSignet);
            FootnoteValidator.Validate(book).Should().BeEmpty("the links back return to their references");
        }
    }

    [Fact]
    public void Text_between_the_empty_element_and_the_link_means_no_footnote()
    {
        (Book book, HtmlResource _) = NewBook(
            "<p><span id=\"x-back\"></span>Some words <a href=\"#x\">1</a></p>\n"
            + "<div id=\"x\"><p><a href=\"#x-back\">1</a> Not a note of that link.</p></div>");
        using (book)
        {
            FootnoteScanner.Scan(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Renumbering_also_renumbers_the_links_back_that_lead_to_the_wrapper()
    {
        (Book book, HtmlResource html) = NewBook(InDesignBody);
        using (book)
        {
            FootnoteEditor.Renumber(book).Should().BeTrue();

            string text = html.GetText();
            text.Should().Contain("<a href=\"#footnote-9\"><sup>[1]</sup></a>").And.Contain("<a href=\"#footnote-8\">2</a>");
            text.Should().Contain("<a href=\"#footnote-9-backlink\">1</a> First note.")
                .And.Contain("<a href=\"#footnote-8-backlink\">2</a> Second note.");
        }
    }

    [Fact]
    public void Removing_a_footnote_also_removes_the_wrapper_and_the_empty_element_its_link_back_led_to()
    {
        (Book book, HtmlResource html) = NewBook(InDesignBody);
        using (book)
        {
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single(f => f.NoteId == "footnote-8")).Should().BeTrue();
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            BodyOf(html).Should().Contain("<h1 id=\"ch\"><a href=\"#toc\">Chapter</a></h1>")
                .And.Contain("<p>Text more.</p>")
                .And.NotContain("backlink").And.NotContain("footnote-");
        }
    }

    [Fact]
    public void A_wrapper_another_link_leads_to_stays()
    {
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<span id=\"n-back\"><a href=\"#n\">1</a></span> more.</p>\n"
            + "<p><a href=\"#n-back\">see the reference</a></p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">1</a> Note.</p></div>");
        using (book)
        {
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            BodyOf(html).Should().Contain("<p>Text<span id=\"n-back\"></span> more.</p>").And.NotContain("Note.");
        }
    }

    [Theory]
    [InlineData("[7]", 2, "[2]")]
    [InlineData("<sup>12</sup>", 3, "<sup>3</sup>")]
    [InlineData("7.", 1, "1.")]
    [InlineData("*", 1, null)]
    [InlineData("1-2", 1, null)]
    public void Only_a_marker_with_a_single_number_is_renumbered(string marker, int number, string? expected)
    {
        FootnoteEditor.ReplaceSingleNumber(marker, number).Should().Be(expected);
    }
}
