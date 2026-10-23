using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "First"), "A <i>note</i>.", FootnotePlacement.Local)
                .Should().Be(FootnoteError.None);

            string text = html.GetText();

            // Assert
            text.Should().Contain(
                "<p>First<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a> sentence.</p>");
            text.Should().Contain("<p>Second sentence.</p>\n<hr class=\"signet-fn-separator\"/>\n"
                + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
                + "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> A <i>note</i>.</p></aside>\n"
                + "</section>\n</body>");
            text.Should().Contain(".signet-fn-ref {", "the reference is styled as a superscript");
            book.Modified.Should().BeTrue();
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Local_footnotes_are_numbered_in_text_order()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "Second"), "second note", FootnotePlacement.Local);
            FootnoteEditor.Add(book, html, After(html, "First"), "first note", FootnotePlacement.Local);

            string text = html.GetText();

            // Assert
            text.Should().Contain("First<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
                .And.Contain("Second<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-2\" id=\"fn-2-ref\">[2]</a>");
            text.IndexOf("first note", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("second note", StringComparison.Ordinal));
            text.Should().Contain("id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> first note");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Local_numbering_starts_from_one_in_every_file_and_global_numbering_runs_through_the_book()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));

            // Act
            FootnoteEditor.Add(book, second, After(second, "First"), "global in second", FootnotePlacement.Global);
            FootnoteEditor.Add(book, second, After(second, "Second"), "local in second", FootnotePlacement.Local);
            FootnoteEditor.Add(book, first, After(first, "First"), "global in first", FootnotePlacement.Global);
            FootnoteEditor.Add(book, first, After(first, "Second"), "local in first", FootnotePlacement.Local);

            // Assert
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
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "endnote", FootnotePlacement.Global);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;

            // Assert
            notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);
            book.GetHtmlResources()[^1].Should().BeSameAs(notes);
            notes.GetText().Should().Contain("<h1>Przypisy</h1>")
                .And.Contain("<aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\""
                    + Core.BookPath.Relative(notes.BookPath, html.BookPath) + "#en-1-ref\">1.</a> endnote</p></aside>");
            html.GetText().Should().Contain("href=\"" + Core.BookPath.Relative(html.BookPath, notes.BookPath) + "#en-1\"");
            ShouldBeConsistent(book);

            // Act
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            // Assert
            FootnoteEditor.FindGlobalFile(book).Should().BeNull();
            book.GetAllResources().Should().NotContain(notes);
        }
    }

    [Fact]
    public void Removing_the_last_local_footnote_leaves_the_file_as_it_was()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local);

            // Assert
            BodyOf(html).Should().Contain("signet-footnotes");

            // Act & Assert
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            BodyOf(html).Should().Be("<body>\n" + Body + "\n</body>\n</html>");

            // Assert
            html.GetText().Should().Contain(".signet-footnotes {", "the rules stay in the book");
        }
    }

    [Fact]
    public void A_footnote_moves_from_local_to_global_and_back()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "moving note", FootnotePlacement.Local);

            // Act & Assert
            FootnoteEditor.Move(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            // Arrange
            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;

            // Assert
            notes.GetText().Should().Contain("epub:type=\"endnote\" id=\"en-1\"").And.Contain("moving note");
            BodyOf(html).Should().NotContain("signet-footnotes").And.Contain("#en-1\" id=\"en-1-ref\">[1]</a>");
            FootnoteScanner.Scan(book).Single().Placement.Should().Be(FootnotePlacement.Global);
            ShouldBeConsistent(book);

            // Act & Assert
            FootnoteEditor.Move(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            // Assert
            FootnoteEditor.FindGlobalFile(book).Should().BeNull("the emptied notes file is removed");
            html.GetText().Should().Contain("href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
                .And.Contain("epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> moving note");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Add_and_Move_return_the_footnote_after_the_renumbering()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "Second"), "second note", FootnotePlacement.Local);

            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "First"), "first note", FootnotePlacement.Local, out Footnote? added)
                .Should().Be(FootnoteError.None);

            // Assert
            added!.NoteId.Should().Be("fn-1", "the new footnote comes first in the text");
            added.ReferenceId.Should().Be("fn-1-ref");

            FootnoteEditor.Locate(added, FootnoteSide.Reference)!.Value.Offset
                .Should().Be(html.GetText().IndexOf("<a class=\"signet-fn-ref\"", StringComparison.Ordinal));

            // Act
            FootnoteEditor.Move(book, added, out Footnote? moved).Should().BeTrue();

            // Assert
            moved!.Placement.Should().Be(FootnotePlacement.Global);
            moved.NoteId.Should().Be("en-1");
            moved.NoteFile.Should().BeSameAs(FootnoteEditor.FindGlobalFile(book));
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void NumberAt_is_the_number_the_renumbering_gives_a_new_footnote()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));

            // Act
            FootnoteEditor.Add(book, first, After(first, "Second"), "global in the first file", FootnotePlacement.Global);
            FootnoteEditor.Add(book, first, After(first, "First"), "local", FootnotePlacement.Local);
            FootnoteEditor.Add(book, second, After(second, "Second"), "global in the second file", FootnotePlacement.Global);

            // Between the two references of the first file: one local and no global footnote before it.
            int middle = After(first, " sentence.</p>");

            // Act & Assert
            FootnoteEditor.NumberAt(book, first, middle, FootnotePlacement.Local).Should().Be(2);

            // Act & Assert
            FootnoteEditor.NumberAt(book, first, middle, FootnotePlacement.Global).Should().Be(1);

            // At the start of the second file: no local footnote of that file, the global one of the first file before it.
            int start = After(second, "<p>");

            // Act & Assert
            FootnoteEditor.NumberAt(book, second, start, FootnotePlacement.Local).Should().Be(1);

            // Act & Assert
            FootnoteEditor.NumberAt(book, second, start, FootnotePlacement.Global).Should().Be(2);

            // Act
            FootnoteEditor.Add(book, second, start, "new", FootnotePlacement.Global, out Footnote? added);

            // Act & Assert
            FootnoteEditor.Locate(added!, FootnoteSide.Reference)!.Value.File.GetText().Should().Contain(">[2]</a>First");
        }
    }

    [Fact]
    public void Refresh_finds_a_footnote_of_an_earlier_scan_by_its_reference()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "first note", FootnotePlacement.Local, out Footnote? first);
            FootnoteEditor.Add(book, html, After(html, "Second"), "second note", FootnotePlacement.Local, out Footnote? second);

            // Act & Assert
            FootnoteEditor.Refresh(book, first!)!.NoteId.Should().Be("fn-1");

            // Act & Assert
            FootnoteEditor.Remove(book, FootnoteEditor.Refresh(book, second!)!).Should().BeTrue();

            // Act & Assert
            FootnoteEditor.Refresh(book, second!).Should().BeNull("its reference is gone");
        }
    }

    [Fact]
    public void FindAt_tells_the_reference_from_the_note()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "the note", FootnotePlacement.Local);

            // Arrange
            string text = html.GetText();

            // Act & Assert
            FootnoteEditor.FindAt(book, html, text.IndexOf("[1]", StringComparison.Ordinal))!.Side.Should().Be(FootnoteSide.Reference);

            // Act & Assert
            FootnoteEditor.FindAt(book, html, text.IndexOf("the note", StringComparison.Ordinal))!.Side.Should().Be(FootnoteSide.Note);

            // Act & Assert
            FootnoteEditor.FindAt(book, html, text.IndexOf("Second", StringComparison.Ordinal)).Should().BeNull();
        }
    }

    [Fact]
    public void A_footnote_cannot_be_added_where_a_reference_does_not_belong()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p>Some <a href=\"#x\">link</a> text.</p>\n<p id=\"x\">x</p>");
        using (book)
        {
            string text = html.GetText();

            // Act & Assert
            FootnoteEditor.CanAdd(book, html, text.IndexOf("<title>", StringComparison.Ordinal)).Should().Be(FootnoteError.NotInBody);

            // Act & Assert
            FootnoteEditor.CanAdd(book, html, text.IndexOf("href", StringComparison.Ordinal)).Should().Be(FootnoteError.InsideTag);

            // Act & Assert
            FootnoteEditor.CanAdd(book, html, After(html, ">li")).Should().Be(FootnoteError.InsideLink);

            // Act & Assert
            FootnoteEditor.CanAdd(book, html, After(html, "Some")).Should().Be(FootnoteError.None);

            // Act
            FootnoteEditor.Add(book, html, After(html, "Some"), "note", FootnotePlacement.Local);

            // Act & Assert
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
        // Act & Assert
        FootnoteEditor.ValidateContent(content, "3.0").Should().Be(expected);
    }

    [Fact]
    public void An_epub2_footnote_uses_divs_without_epub_type()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(version: "2.0");
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Arrange
            string text = html.GetText();

            // Assert
            text.Should().NotContain("epub:type").And.NotContain("<aside").And.NotContain("<section");
            text.Should().Contain("<div class=\"signet-footnotes\">").And.Contain("<div class=\"signet-fn\" id=\"fn-1\">");
            FootnoteScanner.Scan(book).Should().ContainSingle();
        }
    }

    [Fact]
    public void The_reference_style_goes_to_the_linked_stylesheet()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            css.SetText("p { margin: 0; }\n");
            html.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(html.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));

            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local);
            FootnoteEditor.Add(book, html, After(html, "Second"), "note 2", FootnotePlacement.Local);

            // Assert
            css.GetText().Should().Be("p { margin: 0; }\n\n" + FootnoteStyles.Default.ApplyTo(string.Empty));
            css.GetText().Should().Contain(".signet-fn-ref {\n  vertical-align: super;\n  font-size: smaller;\n  line-height: normal;\n}\n");
            html.GetText().Should().NotContain("<style");
        }
    }

    [Fact]
    public void A_stylesheet_shared_by_several_files_gets_the_rules_once()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            css.SetText("p { margin: 0; }\n");
            first.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(first.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(second.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));

            // Act
            FootnoteEditor.Add(book, first, After(first, "First"), "note", FootnotePlacement.Local);
            FootnoteEditor.Add(book, second, After(second, "First"), "note 2", FootnotePlacement.Local);
            FootnoteEditor.Renumber(book, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            css.GetText().Should().Be("p { margin: 0; }\n\n" + FootnoteStyles.Default.ApplyTo(string.Empty));
        }
    }

    [Fact]
    public void Every_footnote_operation_keeps_the_rules_equal_to_the_styles()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            html.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(html.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));
            FootnoteStyles first = FootnoteStyles.Default with { Note = "color: blue;" };

            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local, out _, new FootnoteOptions(first));

            // Assert
            css.GetText().Should().Contain(".signet-fn {\n  color: blue;\n}");

            // Act & Assert
            FootnoteEditor.Renumber(book, new FootnoteOptions(first), cancellationToken: TestContext.Current.CancellationToken)
                .Should().BeFalse("everything is up to date");

            // Act & Assert
            FootnoteEditor.Renumber(
                    book, new FootnoteOptions(first with { Note = "color: green;" }), cancellationToken: TestContext.Current.CancellationToken)
                .Should().BeTrue();

            // Assert
            css.GetText().Should().Contain(".signet-fn {\n  color: green;\n}").And.NotContain("blue");
        }
    }

    [Fact]
    public void The_notes_file_links_the_stylesheet_of_the_reference()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            CssResource css = book.CreateEmptyCssFile();
            html.SetText(Xhtml(Body, "\n<link href=\"" + Core.BookPath.Relative(html.BookPath, css.BookPath) + "\" rel=\"stylesheet\" type=\"text/css\"/>"));

            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Global);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;

            // Assert
            notes.GetLinkedStylesheets().Should().Equal(css.BookPath);
            notes.GetText().Should().NotContain("<style", "the rules are in the linked stylesheet");
            css.GetText().Should().Contain(".signet-endnotes {");
        }
    }

    [Fact]
    public void Renumbering_leaves_other_footnotes_as_they_are()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup class=\"sup_note\"><a class=\"a_note\" epub:type=\"noteref\" href=\"#footnote-07\" id=\"footnote-07-backlink\">[7]</a></sup>.</p>\n"
            + "<aside class=\"aside_footnote\" id=\"footnote-07\" epub:type=\"footnote\">"
            + "<p class=\"p_footnote\"><a class=\"p_a_footnote\" href=\"#footnote-07-backlink\">7.</a> B. Lapunow.</p></aside>");
        using (book)
        {
            string before = html.GetText();

            // Act & Assert
            FootnoteEditor.Renumber(book, cancellationToken: TestContext.Current.CancellationToken)
                .Should().BeFalse("Signet changes other footnotes only by converting them");

            string text = html.GetText();

            // Assert
            text.Should().Be(before);
            Footnote footnote = FootnoteScanner.Scan(book).Single();
            footnote.IsSignet.Should().BeFalse();

            // Act & Assert
            FootnoteEditor.FindAt(book, html, text.IndexOf("<sup", StringComparison.Ordinal) + 2)!.Side
                .Should().Be(FootnoteSide.Reference, "the <sup> around the link is part of the reference");

            // Act & Assert
            FootnoteEditor.Remove(book, footnote).Should().BeTrue();

            // Assert
            html.GetText().Should().Contain("<p>Text.</p>").And.NotContain("aside");
        }
    }

    [Fact]
    public void A_cancelled_token_stops_renumbering()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();

            // Act & Assert
            Action act = () => FootnoteEditor.Renumber(book, cancellationToken: cancelled.Token);
            act.Should().Throw<OperationCanceledException>();
        }
    }

    [Fact]
    public void Renumbering_reports_a_real_percent_while_scanning_the_books_files()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            book.CreateEmptyHtmlFile();
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Local);
            int totalFiles = book.GetHtmlResources().Count;
            List<RenumberProgress> reported = new();

            // Act
            FootnoteEditor.Renumber(book, progress: new RenumberSyncProgress(reported.Add), cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            List<int> fileScans = reported.Where(r => r.Stage == RenumberStage.Scanning && r.Total == totalFiles).Select(r => r.Done).ToList();
            fileScans.Should().Equal(Enumerable.Range(0, totalFiles + 1), "a report for each file parsed, not only the start and the end");
        }
    }

    private sealed class RenumberSyncProgress : IProgress<RenumberProgress>
    {
        private readonly Action<RenumberProgress> _report;

        public RenumberSyncProgress(Action<RenumberProgress> report) => _report = report;

        public void Report(RenumberProgress value) => _report(value);
    }

    [Fact]
    public void The_content_of_a_signet_note_is_the_text_after_its_link_back_and_edits_keep_the_template()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "A <i>note</i>.", FootnotePlacement.Local, out Footnote? footnote);

            // Assert
            FootnoteEditor.GetContent(footnote!).Should().Be(new FootnoteContent("A <i>note</i>.", false));

            // Act & Assert
            FootnoteEditor.Edit(book, footnote!, "  The <b>new</b> text.\n", FootnotePlacement.Local, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            // Assert
            html.GetText().Should().Contain("id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> The <b>new</b> text.</p></aside>")
                .And.NotContain("A <i>note</i>.");
            edited!.NoteId.Should().Be("fn-1");
            FootnoteEditor.GetContent(edited)!.Text.Should().Be("The <b>new</b> text.");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void An_edit_with_the_other_placement_changes_the_text_and_moves_the_note()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "old text", FootnotePlacement.Local, out Footnote? footnote);

            // Act & Assert
            FootnoteEditor.Edit(book, footnote!, "new text", FootnotePlacement.Global, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            // Assert
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
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "old text", FootnotePlacement.Local, out Footnote? footnote);

            // Arrange
            string before = html.GetText();

            // Act & Assert
            FootnoteEditor.Edit(book, footnote!, "a < b", FootnotePlacement.Global, out Footnote? edited)
                .Should().Be(FootnoteError.InvalidContent);

            // Act & Assert
            FootnoteEditor.ValidateEdit(footnote!, " ", "3.0").Should().Be(FootnoteError.EmptyContent);

            // Assert
            edited.Should().BeNull();
            html.GetText().Should().Be(before);
            FootnoteEditor.FindGlobalFile(book).Should().BeNull();
        }
    }

    [Fact]
    public void The_content_of_another_note_is_its_whole_inside_which_must_keep_the_link_back()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<sup><a epub:type=\"noteref\" href=\"#note-7\" id=\"note-7-back\">[7]</a></sup>.</p>\n"
            + "<aside class=\"aside_note\" id=\"note-7\" epub:type=\"footnote\">\n"
            + "  <p class=\"p_note\"><a href=\"#note-7-back\">7.</a> First part.</p>\n  <p>Second part.</p>\n</aside>");
        using (book)
        {
            // Act
            Footnote footnote = FootnoteScanner.Scan(book).Single();
            string inside = "<p class=\"p_note\"><a href=\"#note-7-back\">7.</a> First part.</p>\n  <p>Second part.</p>";

            // Assert
            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent(inside, true));

            // Act & Assert
            FootnoteEditor.ValidateEdit(footnote, "<p>No way back.</p>", "3.0").Should().Be(FootnoteError.MissingBacklink);

            // Act & Assert
            FootnoteEditor.Edit(book, footnote, "<p><a href=\"#note-7-back\">7.</a> Only part.</p>", FootnotePlacement.Local, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            // Assert
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
        // Arrange
        const string reference = "<sup><a epub:type=\"noteref\" href=\"#n1\" id=\"n1-back\">1</a></sup>";
        (Book book, HtmlResource html) = NewBook(
            "<p>Text" + reference + ".</p>\n<aside epub:type=\"footnote\" id=\"n1\"><p><a href=\"#n1-back\">1</a> Note.</p></aside>");
        using (book)
        {
            // Act
            (HtmlResource file, int start, int end) = FootnoteEditor.LocateReferenceRange(FootnoteScanner.Scan(book).Single())!.Value;

            // Assert
            file.Should().BeSameAs(html);
            html.GetText()[start..end].Should().Be(reference);
        }
    }

    [Theory]
    [InlineData("One.", new[] { "One." })]
    [InlineData("  One\nstill one.  ", new[] { "One\nstill one." })]
    [InlineData("One.\n\nTwo.", new[] { "One.", "Two." })]
    [InlineData("One.\r\n \t\r\n\r\nTwo.\n\n", new[] { "One.", "Two." })]
    [InlineData(" \n\n ", new string[0])]
    public void A_blank_line_separates_the_paragraphs_of_a_note(string content, string[] expected)
    {
        // Act & Assert
        FootnoteEditor.Paragraphs(content).Should().Equal(expected);
    }

    [Theory]
    [InlineData("One.\n\nTwo <i>parts</i>.", FootnoteError.None)]
    [InlineData("One.\n\n<ul><li>item</li></ul>", FootnoteError.BlockInParagraph)]
    [InlineData("Text <br/> with a break", FootnoteError.None)]
    [InlineData("One.\n\nan <i>unclosed", FootnoteError.InvalidContent)]
    public void Every_paragraph_of_a_note_is_checked(string content, FootnoteError expected)
    {
        // Act & Assert
        FootnoteEditor.ValidateContent(content, "3.0").Should().Be(expected);
    }

    [Fact]
    public void A_note_of_several_paragraphs_has_one_paragraph_per_line_with_the_link_back_in_the_first()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "First"), "One <i>part</i>.\n\nTwo.", FootnotePlacement.Local, out Footnote? footnote)
                .Should().Be(FootnoteError.None);

            // Assert
            html.GetText().Should().Contain(
                "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
                + "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\">\n"
                + "    <p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> One <i>part</i>.</p>\n"
                + "    <p class=\"signet-fn-p\">Two.</p>\n"
                + "  </aside>\n"
                + "</section>");

            FootnoteEditor.GetContent(footnote!).Should().Be(new FootnoteContent("One <i>part</i>.\n\nTwo.", false));
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Editing_the_paragraphs_rewrites_the_note_and_keeps_its_link_back()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "Only.", FootnotePlacement.Local, out Footnote? footnote);

            // Act & Assert
            FootnoteEditor.Edit(book, footnote!, "One.\n\nTwo.\n\nThree.", FootnotePlacement.Local, out Footnote? edited)
                .Should().Be(FootnoteError.None);

            // Assert
            html.GetText().Should().Contain(
                "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\">\n"
                + "    <p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> One.</p>\n    <p class=\"signet-fn-p\">Two.</p>\n    <p class=\"signet-fn-p\">Three.</p>\n  </aside>\n");

            // Act & Assert
            FootnoteEditor.Edit(book, edited!, "Back to one.", FootnotePlacement.Local, out Footnote? again)
                .Should().Be(FootnoteError.None);

            // Assert
            html.GetText().Should().Contain(
                "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\"><p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> Back to one.</p></aside>\n");
            FootnoteEditor.GetContent(again!)!.Text.Should().Be("Back to one.");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void A_note_written_before_the_paragraph_class_is_in_the_template_and_renumbering_gives_it_the_class()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>.</p>\n"
            + "<section class=\"signet-footnotes\" epub:type=\"footnotes\">\n"
            + "  <aside class=\"signet-fn\" epub:type=\"footnote\" id=\"fn-1\">\n"
            + "    <p><a href=\"#fn-1-ref\">1.</a> One.</p>\n    <p>Two.</p>\n  </aside>\n</section>");
        using (book)
        {
            // Act
            Footnote footnote = FootnoteScanner.Scan(book).Single();

            // Assert
            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent("One.\n\nTwo.", false));

            // Act
            FootnoteEditor.Renumber(book, cancellationToken: TestContext.Current.CancellationToken).Should().BeTrue();

            // Assert
            html.GetText().Should().Contain(
                "    <p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> One.</p>\n    <p class=\"signet-fn-p\">Two.</p>\n");

            // Act & Assert
            FootnoteEditor.Renumber(book, cancellationToken: TestContext.Current.CancellationToken).Should().BeFalse("the note is up to date now");
        }
    }

    [Fact]
    public void A_signet_note_changed_by_hand_is_edited_as_its_whole_inside_of_blocks()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "Text.", FootnotePlacement.Local, out Footnote? footnote);

            // Arrange
            html.SetText(html.GetText().Replace("<p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">", "<p class=\"x\"><a href=\"#fn-1-ref\">", StringComparison.Ordinal));

            // Act
            footnote = FootnoteEditor.Refresh(book, footnote!);

            // Assert
            FootnoteEditor.GetContent(footnote!).Should().Be(new FootnoteContent("<p class=\"x\"><a href=\"#fn-1-ref\">1.</a> Text.</p>", true));

            // Act & Assert
            FootnoteEditor.ValidateEdit(footnote!, "<a href=\"#fn-1-ref\">1.</a> Text.", "3.0").Should().Be(FootnoteError.NotBlocks);

            // Act & Assert
            FootnoteEditor.ValidateEdit(footnote!, "<p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> Text.</p><ul><li>A list.</li></ul>", "3.0")
                .Should().Be(FootnoteError.None);
        }
    }

    [Fact]
    public void The_preview_is_the_markup_the_footnote_gets()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            int offset = After(html, "Second");

            // Act
            FootnoteMarkup local = FootnoteEditor.Preview(book, html, offset, offset, "One.\n\nTwo.", FootnotePlacement.Local, 1)!;
            FootnoteEditor.Add(book, html, offset, "One.\n\nTwo.", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            string text = html.GetText();

            // Assert
            text.Should().Contain(local.Reference);
            text.Should().Contain(local.Note.Replace("\n", "\n  ", StringComparison.Ordinal), "the note is indented in its container");
            local.Page.Should().Contain("<p>Second" + local.Reference + " sentence.</p>")
                .And.Contain("<hr class=\"signet-fn-separator\"/>")
                .And.Contain("<section class=\"signet-footnotes\">\n" + local.Note)
                .And.Contain(".signet-fn-ref {");
        }
    }

    [Fact]
    public void The_preview_of_a_global_footnote_links_to_the_notes_file_to_be_created()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            int offset = After(html, "First");

            // Act
            FootnoteMarkup global = FootnoteEditor.Preview(book, html, offset, offset, "Note.", FootnotePlacement.Global, 1)!;

            // Assert
            global.Reference.Should().Be(
                "<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"Przypisy.xhtml#en-1\" id=\"en-1-ref\">[1]</a>");
            global.Note.Should().Be(
                "<aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-1\"><p class=\"signet-fn-p\"><a href=\"" + html.Filename + "#en-1-ref\">1.</a> Note.</p></aside>");

            // Act & Assert
            FootnoteEditor.FindGlobalFile(book).Should().BeNull("the preview changes nothing");

            // Act & Assert
            FootnoteEditor.Preview(book, html, offset, offset, "a <i>b", FootnotePlacement.Global, 1).Should().BeNull();
        }
    }

    [Fact]
    public void The_preview_of_an_edited_footnote_keeps_its_number_and_replaces_its_reference()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "one", FootnotePlacement.Local);
            FootnoteEditor.Add(book, html, After(html, "Second"), "two", FootnotePlacement.Local, out Footnote? second);
            (HtmlResource _, int start, int end) = FootnoteEditor.LocateReferenceRange(second!)!.Value;

            FootnoteMarkup preview = FootnoteEditor.Preview(book, html, start, end, "new two", FootnotePlacement.Local, 2, second)!;

            // Assert
            preview.Note.Should().Contain("id=\"fn-2\"><p class=\"signet-fn-p\"><a href=\"#fn-2-ref\">2.</a> new two</p>");
            preview.Page.Should().Contain("<p>Second" + preview.Reference + " sentence.</p>");
        }
    }

    [Fact]
    public void An_epub2_note_of_several_paragraphs_is_a_div()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(version: "2.0");
        using (book)
        {
            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "First"), "One.\n\nTwo.", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Assert
            html.GetText().Should().Contain(
                "  <div class=\"signet-fn\" id=\"fn-1\">\n    <p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> One.</p>\n    <p class=\"signet-fn-p\">Two.</p>\n  </div>\n");
        }
    }

    private static readonly FootnoteOptions ByChapter = new(FootnoteStyles.Default, GroupByChapter: true);

    // A book of two chapters with a heading each (the second one with a reference in its heading).
    private static (Book Book, HtmlResource One, HtmlResource Two) NewChapters()
    {
        (Book book, HtmlResource one) = NewBook("<h1>Chapter <i>One</i></h1>\n" + Body);
        HtmlResource two = book.CreateEmptyHtmlFile();
        two.SetText(Xhtml("<h2>Chapter Two</h2>\n" + Body));
        return (book, one, two);
    }

    // The global notes container of the book.
    private static string NotesContainer(Book book)
    {
        string text = FootnoteEditor.FindGlobalFile(book)!.GetText();
        int start = text.IndexOf("  <section class=\"signet-endnotes", StringComparison.Ordinal);
        return text[start..(text.LastIndexOf("</section>", StringComparison.Ordinal) + "</section>".Length)];
    }

    private static string Endnote(HtmlResource from, int n, string text) =>
        "<aside class=\"signet-fn\" epub:type=\"endnote\" id=\"en-" + n + "\"><p class=\"signet-fn-p\"><a href=\""
        + from.Filename + "#en-" + n + "-ref\">" + n + ".</a> " + text + "</p></aside>";

    [Fact]
    public void Grouped_by_chapter_every_chapter_has_a_group_titled_with_its_first_heading()
    {
        // Arrange
        (Book book, HtmlResource one, HtmlResource two) = NewChapters();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, two, After(two, "First"), "in two", FootnotePlacement.Global, out _, ByChapter);
            FootnoteEditor.Add(book, one, After(one, "Second"), "second in one", FootnotePlacement.Global, out _, ByChapter);
            FootnoteEditor.Add(book, one, After(one, "First"), "first in one", FootnotePlacement.Global, out _, ByChapter);

            // Assert
            NotesContainer(book).Should().Be(
                "  <section class=\"signet-endnotes signet-fn-by-chapter\" epub:type=\"endnotes\">\n"
                + "    <section class=\"signet-fn-group\">\n"
                + "      <h2 class=\"signet-fn-group-title\">Chapter One</h2>\n"
                + "      " + Endnote(one, 1, "first in one") + "\n"
                + "      " + Endnote(one, 2, "second in one") + "\n"
                + "    </section>\n"
                + "    <section class=\"signet-fn-group\">\n"
                + "      <h2 class=\"signet-fn-group-title\">Chapter Two</h2>\n"
                + "      " + Endnote(two, 3, "in two") + "\n"
                + "    </section>\n"
                + "  </section>");
            ShouldBeConsistent(book);

            // Act & Assert
            FootnoteEditor.Renumber(book, ByChapter, cancellationToken: TestContext.Current.CancellationToken).Should().BeFalse("the groups are up to date");
        }
    }

    [Fact]
    public void A_group_title_changed_by_hand_stays_and_an_emptied_group_goes_away()
    {
        // Arrange
        (Book book, HtmlResource one, HtmlResource two) = NewChapters();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, one, After(one, "First"), "in one", FootnotePlacement.Global, out _, ByChapter);
            FootnoteEditor.Add(book, two, After(two, "First"), "in two", FootnotePlacement.Global, out _, ByChapter);

            // Arrange
            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.SetText(notes.GetText().Replace(">Chapter One</h2>", ">The first chapter</h2>", StringComparison.Ordinal));

            // Act
            FootnoteEditor.Add(book, one, After(one, "Second"), "more in one", FootnotePlacement.Global, out _, ByChapter);

            // Act & Assert
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single(f => ReferenceEquals(f.ReferenceFile, two)), ByChapter).Should().BeTrue();

            // Assert
            string container = NotesContainer(book);
            container.Should().Contain("<h2 class=\"signet-fn-group-title\">The first chapter</h2>")
                .And.Contain("more in one").And.NotContain("Chapter Two").And.NotContain("in two");
            ShouldBeConsistent(book);

            foreach (Footnote footnote in FootnoteScanner.Scan(book).Reverse())
            {
                // Act & Assert
                FootnoteEditor.Remove(book, footnote, ByChapter).Should().BeTrue();
            }

            // Assert
            FootnoteEditor.FindGlobalFile(book).Should().BeNull("the notes file goes away with its last note");
        }
    }

    [Fact]
    public void A_book_grouped_by_chapter_stays_grouped_without_the_option_and_the_option_groups_older_notes()
    {
        // Arrange
        (Book book, HtmlResource one, HtmlResource two) = NewChapters();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, one, After(one, "First"), "older one", FootnotePlacement.Global);
            FootnoteEditor.Add(book, two, After(two, "First"), "older two", FootnotePlacement.Global);

            // Assert
            NotesContainer(book).Should().NotContain("signet-fn-group");

            // Act & Assert
            FootnoteEditor.Renumber(book, ByChapter, cancellationToken: TestContext.Current.CancellationToken).Should().BeTrue();

            // Assert
            NotesContainer(book).Should().Contain("signet-fn-by-chapter")
                .And.Contain("Chapter One</h2>\n      " + Endnote(one, 1, "older one"))
                .And.Contain("Chapter Two</h2>\n      " + Endnote(two, 2, "older two"));

            // Act & Assert
            FootnoteEditor.Add(book, two, After(two, "Second"), "newer two", FootnotePlacement.Global).Should().Be(FootnoteError.None);

            // Assert
            NotesContainer(book).Should().Contain(Endnote(two, 2, "older two") + "\n      " + Endnote(two, 3, "newer two") + "\n    </section>");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void Groups_without_the_chapter_class_stay_and_a_new_note_joins_the_group_of_the_note_before_it()
    {
        // Arrange
        (Book book, HtmlResource one, HtmlResource two) = NewChapters();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, one, After(one, "First"), "a", FootnotePlacement.Global);
            FootnoteEditor.Add(book, two, After(two, "First"), "b", FootnotePlacement.Global);

            // Arrange
            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            string text = notes.GetText();
            text = text.Replace("  " + Endnote(one, 1, "a") + "\n", "<div class=\"signet-fn-group\"><h3>Part I</h3>\n  " + Endnote(one, 1, "a") + "\n", StringComparison.Ordinal)
                .Replace("  " + Endnote(two, 2, "b") + "\n", "  " + Endnote(two, 2, "b") + "</div>\n", StringComparison.Ordinal);
            notes.SetText(text);

            // Act & Assert
            FootnoteEditor.Add(book, two, After(two, "Second"), "c", FootnotePlacement.Global).Should().Be(FootnoteError.None);

            // Act & Assert
            FootnoteEditor.Add(book, one, After(one, "Second"), "d", FootnotePlacement.Global).Should().Be(FootnoteError.None);

            // Assert
            NotesContainer(book).Should().Be(
                "  <section class=\"signet-endnotes\" epub:type=\"endnotes\">\n"
                + "    <div class=\"signet-fn-group\">\n"
                + "      <h3>Part I</h3>\n"
                + "      " + Endnote(one, 1, "a") + "\n"
                + "      " + Endnote(one, 2, "d") + "\n"
                + "      " + Endnote(two, 3, "b") + "\n"
                + "      " + Endnote(two, 4, "c") + "\n"
                + "    </div>\n"
                + "  </section>");
            ShouldBeConsistent(book);
        }
    }

    [Fact]
    public void A_group_title_comes_from_the_title_or_the_file_name_without_a_heading()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p>No heading<a class=\"signet-fn-ref\" href=\"#x\">[9]</a> here.</p>\n" + Body);
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Global, out _, ByChapter);

            // Assert
            NotesContainer(book).Should().Contain("<h2 class=\"signet-fn-group-title\">t</h2>", "the <title> of the file");
        }
    }

    [Fact]
    public void Signet_references_are_left_out_of_a_group_title()
    {
        // Arrange
        (Book book, HtmlResource one, HtmlResource _) = NewChapters();
        using (book)
        {
            // Act & Assert
            FootnoteEditor.Add(book, one, After(one, "Chapter <i>One"), "on the heading", FootnotePlacement.Global, out _, ByChapter)
                .Should().Be(FootnoteError.None);

            // Assert
            NotesContainer(book).Should().Contain("<h2 class=\"signet-fn-group-title\">Chapter One</h2>");
        }
    }

    // A chapter with references to notes of other origin in other files: one file for each item of `notes`, which holds
    // the ids of its notes separated by spaces.
    private static (Book Book, HtmlResource Chapter, HtmlResource[] NoteFiles) NewForeignGlobals(params string[] notes)
    {
        (Book book, HtmlResource chapter) = NewBook();
        HtmlResource[] files = notes.Select(_ => book.CreateEmptyHtmlFile()).ToArray();
        StringBuilder references = new("<p>Text");
        for (int f = 0; f < notes.Length; f++)
        {
            StringBuilder body = new();
            foreach (string id in notes[f].Split(' '))
            {
                references.Append("<a href=\"").Append(files[f].Filename).Append('#').Append(id).Append("\" id=\"").Append(id)
                    .Append("-back\">*</a>");
                body.Append("<div id=\"").Append(id).Append("\"><p><a href=\"").Append(chapter.Filename).Append('#').Append(id)
                    .Append("-back\">*</a> Note ").Append(id).Append(".</p></div>\n");
            }

            files[f].SetText(Xhtml(body.ToString()));
        }

        chapter.SetText(Xhtml(references.Append(" end.</p>").ToString()));
        return (book, chapter, files);
    }

    [Fact]
    public void The_only_dedicated_file_with_all_the_global_notes_is_taken_without_asking()
    {
        // Arrange
        (Book book, HtmlResource _, HtmlResource[] files) = NewForeignGlobals("n1 n2");
        using (book)
        {
            files[0].SetText(files[0].GetText().Replace("<body>\n", "<body>\n<h1>Notes</h1>\n<p>A short word on the notes.</p>\n", StringComparison.Ordinal));

            // Act
            IReadOnlyList<GlobalFileCandidate> candidates = FootnoteEditor.GlobalFileCandidates(book);

            // Assert
            candidates.Should().ContainSingle();
            candidates[0].Should().BeEquivalentTo(new { File = files[0], NoteCount = 2, IsDedicated = true, HoldsAll = true });
            candidates[0].Hints.Should().Equal(GlobalFileHint.NotesHeading);
            FootnoteEditor.AutomaticGlobalFile(candidates).Should().BeSameAs(candidates[0]);
        }
    }

    [Fact]
    public void With_several_candidates_or_a_file_that_is_not_only_notes_the_user_chooses()
    {
        // Arrange
        (Book book, HtmlResource _, HtmlResource[] files) = NewForeignGlobals("n1", "n2 n3");
        using (book)
        {
            // Act
            IReadOnlyList<GlobalFileCandidate> candidates = FootnoteEditor.GlobalFileCandidates(book);

            // Assert
            candidates.Select(c => (c.File, c.NoteCount, c.HoldsAll)).Should().Equal((files[0], 1, false), (files[1], 2, false));
            FootnoteEditor.AutomaticGlobalFile(candidates).Should().BeNull();
        }

        // Arrange
        (Book other, HtmlResource _, HtmlResource[] single) = NewForeignGlobals("n1");
        using (other)
        {
            single[0].SetText(single[0].GetText().Replace("<body>\n", "<body>\n<p>" + new string('x', FootnoteEditor.IntroLimit + 1) + "</p>\n", StringComparison.Ordinal));

            // Act
            IReadOnlyList<GlobalFileCandidate> candidates = FootnoteEditor.GlobalFileCandidates(other);

            // Assert
            candidates.Single().IsDedicated.Should().BeFalse();
            FootnoteEditor.AutomaticGlobalFile(candidates).Should().BeNull();
        }
    }

    [Fact]
    public void Notes_types_and_roles_hint_at_the_notes_file()
    {
        // Arrange
        (Book book, HtmlResource _, HtmlResource[] files) = NewForeignGlobals("n1");
        using (book)
        {
            files[0].SetText(files[0].GetText()
                .Replace("<body>\n", "<body>\n<section epub:type=\"endnotes\" role=\"doc-endnotes\">\n", StringComparison.Ordinal)
                .Replace("\n</body>", "</section>\n</body>", StringComparison.Ordinal));

            // Act & Assert
            FootnoteEditor.GlobalFileCandidates(book).Single().Hints.Should().Equal(GlobalFileHint.NotesType, GlobalFileHint.NotesRole);
        }
    }

    [Fact]
    public void A_book_without_global_notes_of_other_origin_has_no_candidates()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "Signet's own", FootnotePlacement.Global);

            // Act & Assert
            FootnoteEditor.GlobalFileCandidates(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_notes_file_with_something_else_in_it_stays_when_its_last_note_goes()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "note", FootnotePlacement.Global);

            // Arrange
            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.SetText(notes.GetText().Replace("<h1>Przypisy</h1>", "<h1>Przypisy</h1>\n  <p>Written by hand.</p>", StringComparison.Ordinal));

            // Act
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            // Assert
            book.GetHtmlResources().Should().Contain(notes);
            BodyOf(notes).Should().Contain("<p>Written by hand.</p>").And.NotContain("signet-endnotes");
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
        // Arrange
        (Book book, HtmlResource _) = NewBook(InDesignBody);
        using (book)
        {
            // Act
            IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

            // Assert
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
        // Arrange
        (Book book, HtmlResource _) = NewBook(
            "<p><span id=\"x-back\"></span>Some words <a href=\"#x\">1</a></p>\n"
            + "<div id=\"x\"><p><a href=\"#x-back\">1</a> Not a note of that link.</p></div>");
        using (book)
        {
            // Act & Assert
            FootnoteScanner.Scan(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Signet_footnotes_count_the_other_footnotes_before_them_but_leave_them_as_they_are()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p>First sentence.</p>\n" + InDesignBody);
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "First"), "before", FootnotePlacement.Local).Should().Be(FootnoteError.None);
            FootnoteEditor.Add(book, html, After(html, "more."), "after", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Arrange
            string text = html.GetText();

            // Assert
            text.Should().Contain("First<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
                .And.Contain("more.<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-4\" id=\"fn-4-ref\">[4]</a>",
                    "the two other footnotes between them count");
            text.Should().Contain("<a href=\"#footnote-9\"><sup>[5]</sup></a>").And.Contain("<a href=\"#footnote-8\">7</a>")
                .And.Contain("<a href=\"#footnote-9-backlink\">5</a> First note.")
                .And.Contain("<a href=\"#footnote-8-backlink\">7</a> Second note.", "other footnotes keep their markers");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Removing_a_footnote_also_removes_the_wrapper_and_the_empty_element_its_link_back_led_to()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(InDesignBody);
        using (book)
        {
            // Act & Assert
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single(f => f.NoteId == "footnote-8")).Should().BeTrue();

            // Act & Assert
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            // Assert
            BodyOf(html).Should().Contain("<h1 id=\"ch\"><a href=\"#toc\">Chapter</a></h1>")
                .And.Contain("<p>Text more.</p>")
                .And.NotContain("backlink").And.NotContain("footnote-");
        }
    }

    [Fact]
    public void A_wrapper_another_link_leads_to_stays()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Text<span id=\"n-back\"><a href=\"#n\">1</a></span> more.</p>\n"
            + "<p><a href=\"#n-back\">see the reference</a></p>\n"
            + "<div id=\"n\"><p><a href=\"#n-back\">1</a> Note.</p></div>");
        using (book)
        {
            // Act & Assert
            FootnoteEditor.Remove(book, FootnoteScanner.Scan(book).Single()).Should().BeTrue();

            // Assert
            BodyOf(html).Should().Contain("<p>Text<span id=\"n-back\"></span> more.</p>").And.NotContain("Note.");
        }
    }
}
