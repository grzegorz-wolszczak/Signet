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
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            string text = _fixture.Create<string>();

            // Act
            Footnote second = AddShared(book, html, text);

            string markup = html.GetText();

            // Assert
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
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "Third"), "note", FootnotePlacement.Local, out Footnote? footnote);
            FootnoteEditor.AddReference(book, html, After(html, "First"), footnote!, out Footnote? added).Should().Be(FootnoteError.None);

            // Assert
            added!.ReferenceIndex.Should().Be(0, "the new reference comes first in the text");
            html.GetText().Should().Contain("First" + Reference("fn-1a-ref", "#fn-1", "[1a]"))
                .And.Contain("Third" + Reference("fn-1b-ref", "#fn-1", "[1b]"));
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_shared_note_has_one_number_and_the_notes_between_its_references_go_on_counting()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            AddShared(book, html, "shared");

            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "Second"), "between", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Act & Assert
            FootnoteEditor.NumberAt(book, html, html.GetText().Length - 1, FootnotePlacement.Local).Should().Be(3);

            string markup = html.GetText();

            // Assert
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
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            Footnote footnote = AddShared(book, html, "note", FootnoteOptions.Default with { LetterStyle = style });

            // Act & Assert
            FootnoteEditor.AddReference(book, html, After(html, "Second"), footnote, out _).Should().Be(FootnoteError.None);

            // Assert
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
        // Arrange
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));

            // Act
            FootnoteEditor.Add(book, first, After(first, "First"), "endnote", FootnotePlacement.Global, out Footnote? footnote);

            // Act & Assert
            FootnoteEditor.AddReference(book, second, After(second, "Second"), footnote!, out Footnote? added).Should().Be(FootnoteError.None);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            string toNotes = Core.BookPath.Relative(first.BookPath, notes.BookPath);

            // Assert
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
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p>Plain sentence.</p>\n<p>Text<a epub:type=\"noteref\" href=\"#n1\" id=\"r1\">1</a> more.</p>\n"
            + "<aside epub:type=\"footnote\" id=\"n1\"><a href=\"#r1\">1</a> Other note.</aside>");
        using (book)
        {
            HtmlResource other = book.CreateEmptyHtmlFile();
            other.SetText(Xhtml(Body));

            // Act
            Footnote foreign = FootnoteScanner.Scan(book).Single();

            // Act & Assert
            FootnoteEditor.AddReference(book, html, After(html, "more."), foreign, out _).Should().Be(FootnoteError.NotSignet);

            // Act
            FootnoteEditor.Add(book, html, After(html, "Plain"), "local", FootnotePlacement.Local, out Footnote? local);

            // Act & Assert
            FootnoteEditor.AddReference(book, other, After(other, "First"), local!, out Footnote? added).Should().Be(FootnoteError.OtherFile);

            // Assert
            added.Should().BeNull();
        }
    }

    [Fact]
    public void Editing_a_shared_note_keeps_its_links_back()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            Footnote footnote = AddShared(book, html, "old text");

            // Assert
            FootnoteEditor.GetContent(footnote).Should().Be(new FootnoteContent("old text", false));

            // Act & Assert
            FootnoteEditor.Edit(book, footnote, "One.\n\nTwo.", FootnotePlacement.Local, out Footnote? edited).Should().Be(FootnoteError.None);

            // Assert
            html.GetText().Should().Contain("<p class=\"signet-fn-p\"><a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. One.</p>")
                .And.Contain("<p class=\"signet-fn-p\">Two.</p>");
            FootnoteEditor.GetContent(edited!).Should().Be(new FootnoteContent("One.\n\nTwo.", false));
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_note_of_other_origin_with_several_references_is_a_footnote_per_reference_and_one_item_of_the_plan()
    {
        // Arrange
        // The shape of a book where every reference repeats the number and the id of the first one.
        (Book book, _) = NewBook(
            "<p>One<sup><a href=\"#fn4\" id=\"ref4\" epub:type=\"noteref\">4</a></sup>, two<sup><a href=\"#fn4\" id=\"ref4\" "
            + "epub:type=\"noteref\">4</a></sup>.</p>\n<aside id=\"fn4\" epub:type=\"footnote\"><a href=\"#ref4\">[4]</a> Note.</aside>");
        using (book)
        {
            // Act
            FootnoteScanner.Scan(book).Select(f => (f.ReferenceOccurrence, f.ReferenceIndex, f.ReferenceCount))
                .Should().Equal((0, 0, 2), (1, 1, 2));

            // Act & Assert
            FootnoteEditor.PlanConversion(book).Items.Should().ContainSingle();

            // Assert
            FootnoteValidator.Validate(book).Select(r => r.Code).Should().Contain("Validation_FootnoteManyRefs",
                "the note has a link back to the first reference only");
        }
    }

    [Fact]
    public void Removing_a_reference_of_a_shared_note_letters_the_others_anew_and_the_last_one_makes_it_ordinary()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            Footnote second = AddShared(book, html, "note");

            // Act & Assert
            FootnoteEditor.AddReference(book, html, After(html, "Second"), second, out _).Should().Be(FootnoteError.None);

            // Act
            Footnote middle = FootnoteScanner.Scan(book)[1];

            // Act & Assert
            FootnoteEditor.RemoveReference(book, middle).Should().BeTrue();

            // Assert
            html.GetText().Should().Contain("<p>Second sentence.</p>")
                .And.Contain("Third" + Reference("fn-1b-ref", "#fn-1", "[1b]"))
                .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. note</p>");
            FootnoteValidator.Validate(book).Should().BeEmpty();

            // Act & Assert
            FootnoteEditor.RemoveReference(book, FootnoteScanner.Scan(book)[0]).Should().BeTrue();

            // Assert
            html.GetText().Should().Contain("<p>First sentence.</p>")
                .And.Contain("Third" + Reference("fn-1-ref", "#fn-1", "[1]"))
                .And.Contain("<p class=\"signet-fn-p\"><a href=\"#fn-1-ref\">1.</a> note</p>");

            // Act & Assert
            Footnote last = FootnoteScanner.Scan(book).Should().ContainSingle().Subject;

            // Assert
            last.IsShared.Should().BeFalse();

            // Act & Assert
            FootnoteEditor.RemoveReference(book, last).Should().BeFalse("the only reference goes with the whole footnote");

            // Assert
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void Removing_a_shared_footnote_removes_the_note_and_all_its_references()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            string before = html.GetText();

            // Act
            Footnote second = AddShared(book, html, "note");

            // Act & Assert
            FootnoteEditor.Remove(book, second).Should().BeTrue();

            // Assert
            html.GetText()[html.GetText().IndexOf("<body>", StringComparison.Ordinal)..]
                .Should().Be(before[before.IndexOf("<body>", StringComparison.Ordinal)..]);

            // Act & Assert
            FootnoteScanner.Scan(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void The_targets_of_a_new_reference_are_the_local_notes_of_the_file_and_the_global_ones_nearest_before_first()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            HtmlResource other = book.CreateEmptyHtmlFile();
            other.SetText(Xhtml(Body));

            // Act & Assert
            FootnoteEditor.Add(book, other, After(other, "First"), "elsewhere", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "Third"), "third <i>local</i>", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Act & Assert
            FootnoteEditor.Add(book, html, After(html, "First"), "first local", FootnotePlacement.Local).Should().Be(FootnoteError.None);

            // Act & Assert
            FootnoteEditor.Add(book, other, After(other, "Second"), "global", FootnotePlacement.Global).Should().Be(FootnoteError.None);

            // Act
            ReferenceTargetList list = FootnoteEditor.ReferenceTargets(book, html, After(html, "Second"));

            // Assert
            list.Targets.Select(t => (t.Number, t.Text, t.Footnote.Placement)).Should().Equal(
                (1, "first local", FootnotePlacement.Local),
                (2, "third local", FootnotePlacement.Local),
                (1, "global", FootnotePlacement.Global));
            list.Nearest.Should().Be(0, "the reference of the first local note is the nearest before the position");

            // Act & Assert
            FootnoteEditor.ReferenceTargets(book, other, After(other, "Third")).Nearest
                .Should().Be(1, "in the other file the global note's reference is the nearest");
        }
    }

    [Fact]
    public void A_shared_note_moves_with_all_its_references()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // Act
            Footnote second = AddShared(book, html, "note");

            FootnoteEditor.Move(book, second, out Footnote? moved).Should().BeTrue();

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            string toNotes = Core.BookPath.Relative(html.BookPath, notes.BookPath);

            // Assert
            html.GetText().Should().Contain("First" + Reference("en-1a-ref", toNotes + "#en-1", "[1a]"))
                .And.Contain("Third" + Reference("en-1b-ref", toNotes + "#en-1", "[1b]"))
                .And.NotContain("<section class=\"signet-footnotes\"");
            string toChapter = Core.BookPath.Relative(notes.BookPath, html.BookPath);
            notes.GetText().Should().Contain("<a href=\"" + toChapter + "#en-1a-ref\">1a</a>, <a href=\"" + toChapter + "#en-1b-ref\">1b</a>. note");
            moved!.ReferenceIndex.Should().Be(1, "the moved footnote is the same reference");
            FootnoteValidator.Validate(book).Should().BeEmpty();

            // Act & Assert
            FootnoteEditor.Move(book, moved).Should().BeTrue();

            FootnoteEditor.FindGlobalFile(book).Should().BeNull();

            // Assert
            html.GetText().Should().Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. note");
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_global_note_with_references_from_several_files_cannot_become_local()
    {
        // Arrange
        (Book book, HtmlResource first) = NewBook();
        using (book)
        {
            HtmlResource second = book.CreateEmptyHtmlFile();
            second.SetText(Xhtml(Body));

            // Act
            FootnoteEditor.Add(book, first, After(first, "First"), "endnote", FootnotePlacement.Global, out Footnote? footnote);

            // Act & Assert
            FootnoteEditor.AddReference(book, second, After(second, "Second"), footnote!, out Footnote? added).Should().Be(FootnoteError.None);

            // Arrange
            string before = first.GetText();

            // Act & Assert
            FootnoteEditor.CanMove(book, added!).Should().Be(FootnoteError.SeveralFiles);

            // Act & Assert
            FootnoteEditor.Move(book, added!).Should().BeFalse();

            // Act & Assert
            FootnoteEditor.Edit(book, added!, "changed", FootnotePlacement.Local, out Footnote? edited).Should().Be(FootnoteError.SeveralFiles);

            // Assert
            edited.Should().BeNull();
            first.GetText().Should().Be(before);
            FootnoteEditor.GetContent(FootnoteEditor.Refresh(book, added!)!)!.Text.Should().Be("endnote", "nothing changed");
        }
    }

    [Fact]
    public void The_letters_of_shared_notes_in_another_style_are_written_anew_and_links_to_their_ids_follow()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(Body + "\n<p>See <a href=\"#fn-1ii-ref\">the second place</a>.</p>");
        using (book)
        {
            // Act
            AddShared(book, html, "roman", FootnoteOptions.Default with { LetterStyle = FootnoteLetterStyle.LowerRoman });

            // Assert
            html.GetText().Should().Contain("id=\"fn-1ii-ref\">[1ii]</a>");

            // Act & Assert
            FootnoteEditor.PlanLetters(book, FootnoteLetterStyle.LowerRoman).Should().BeEmpty("the note is written in that style");

            // Act & Assert
            LetterItem item = FootnoteEditor.PlanLetters(book, FootnoteLetterStyle.LowerLatin).Should().ContainSingle().Subject;

            // Assert
            item.Current.Should().Be(FootnoteLetterStyle.LowerRoman);
            item.Number.Should().Be(1);
            item.References.Should().HaveCount(2);
            item.Links.Should().ContainSingle().Which.File.Should().BeSameAs(html);

            // Act & Assert
            FootnoteEditor.UnifyLetters(book, new[] { item }, FootnoteLetterStyle.LowerLatin).Should().Be(1);

            // Assert
            html.GetText().Should().Contain("First" + Reference("fn-1a-ref", "#fn-1", "[1a]"))
                .And.Contain("Third" + Reference("fn-1b-ref", "#fn-1", "[1b]"))
                .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. roman")
                .And.Contain("See <a href=\"#fn-1b-ref\">the second place</a>.");

            // Act & Assert
            FootnoteEditor.PlanLetters(book, FootnoteLetterStyle.LowerLatin).Should().BeEmpty();

            // Assert
            FootnoteValidator.Validate(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void A_new_id_equal_to_the_old_id_of_another_reference_does_not_mislead_the_links()
    {
        // Arrange
        // Nine references: in latin the ninth is "i", in roman the first one.
        string body = string.Join("\n", Enumerable.Range(1, 9).Select(i => "<p>Place" + i + ".</p>"))
            + "\n<p>Back to <a href=\"#fn-1i-ref\">the ninth</a>.</p>";
        (Book book, HtmlResource html) = NewBook(body);
        using (book)
        {
            // Act
            FootnoteEditor.Add(book, html, After(html, "Place1"), "note", FootnotePlacement.Local, out Footnote? footnote);
            for (int i = 2; i <= 9; i++)
            {
                // Act & Assert
                FootnoteEditor.AddReference(book, html, After(html, "Place" + i), footnote!, out _).Should().Be(FootnoteError.None);
            }

            // Assert
            html.GetText().Should().Contain("Place9" + Reference("fn-1i-ref", "#fn-1", "[1i]"));

            // Act
            LetterItem item = FootnoteEditor.PlanLetters(book, FootnoteLetterStyle.LowerRoman).Single();

            FootnoteEditor.UnifyLetters(book, new[] { item }, FootnoteLetterStyle.LowerRoman);

            string text = html.GetText();

            // Assert
            text.Should().Contain("Place1" + Reference("fn-1i-ref", "#fn-1", "[1i]"))
                .And.Contain("Place9" + Reference("fn-1ix-ref", "#fn-1", "[1ix]"))
                .And.Contain("Back to <a href=\"#fn-1ix-ref\">the ninth</a>.", "the link follows its reference")
                .And.Contain("<a href=\"#fn-1i-ref\">1i</a>, <a href=\"#fn-1ii-ref\">1ii</a>", "the links back are the note's own");
            FootnoteValidator.Validate(book).Should().BeEmpty();
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
    public void Letters_are_written_in_their_style(FootnoteLetterStyle style, int index, string expected)
    {
        // Act & Assert
        FootnoteLetters.Of(style, index).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "", "")]
    [InlineData(FootnoteLetterStyle.LowerLatin, "a", "b")]
    [InlineData(FootnoteLetterStyle.LowerLatin, "b", "c")]
    [InlineData(FootnoteLetterStyle.LowerLatin, "", "b")]
    [InlineData(FootnoteLetterStyle.UpperLatin, "B", "")]
    [InlineData(FootnoteLetterStyle.LowerRoman, "ii", "iii")]
    [InlineData(FootnoteLetterStyle.LowerRoman, "i", "")]
    public void The_style_of_written_letters_is_recognized(FootnoteLetterStyle? expected, string first, string second)
    {
        // Act & Assert
        FootnoteLetters.Detect(new[] { first, second }).Should().Be(expected);
    }
}
