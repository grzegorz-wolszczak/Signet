using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>
/// The books of <c>tests/corpus/broken</c>: valid EPUBs written on purpose around the cases that caught Signet out.
/// Each case went in because a session found it missing from the tests, which is the rule those books exist for — see
/// their <c>README.md</c>. A case kept as a book is one every later session runs, instead of one that lived in a
/// commit message.
/// </summary>
public sealed class BrokenBookFootnoteTests
{
    private static readonly string[] OnlyRealNotes = { "real", "note" };

    private static Book Open(TempDir temp, string name) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Broken(name), temp)).GetBook();

    /// <summary>
    /// Every awkward footnote of the book is offered for conversion, with the warnings that say why it is awkward:
    /// a note holding another note, and the one held.
    /// </summary>
    [Fact]
    public void The_plan_offers_every_footnote_of_the_broken_book_and_names_what_is_odd()
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "footnotes");

        // Act
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);

        // Assert
        plan.Items.Should().HaveCount(8).And.OnlyContain(i => i.CanConvert);
        ConversionItem outer = plan.Items.Single(i => i.Footnote.NoteId == "outer");
        outer.Warnings.Should().Contain(ConversionWarning.ContainsNote);
        outer.NestedWith.Should().Be("inner");
        ConversionItem inner = plan.Items.Single(i => i.Footnote.NoteId == "inner");
        inner.Warnings.Should().Contain(ConversionWarning.InsideNote);
        inner.NestedWith.Should().Be("outer");
    }

    /// <summary>
    /// The whole book converts: a note cited from inside another note, two notes citing each other, a note holding
    /// another through a comment that swallowed a closing tag, and a shared note with one link back for two
    /// references. The first two were broken until October 2026 — the citing note's reference was dropped and the
    /// mutual pair came out with two edits written over each other.
    /// </summary>
    [Fact]
    public void The_whole_broken_book_converts_without_a_problem()
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "footnotes");
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);

        // Act
        ConversionResult result = FootnoteEditor.Convert(
            book, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, null, TestContext.Current.CancellationToken);

        // Assert
        result.Problems.Should().BeEmpty();
        result.Converted.Should().Be(8);
        IReadOnlyList<Footnote> after = FootnoteScanner.Scan(book);
        after.Should().HaveCount(10).And.OnlyContain(f => f.IsSignet, "eight notes, two of them read from two places");

        // The only complaints left are the two the book makes on purpose, both about the note it leaves with nothing
        // pointing at it and with a link back to an id nobody wrote. The conversion neither touches nor hides them.
        FootnoteValidator.Validate(book).Select(r => r.Code).Should().Equal("Validation_FootnoteOrphan");
        LinkIntegrityValidator.Validate(book).Select(r => r.Code).Should().Equal("Validation_BadFragmentSameFile");
    }

    /// <summary>
    /// A note cited from inside another note ends up as Signet's reference inside Signet's note, and the pair that
    /// cite each other end up pointing at each other. Nothing keeps a temporary <c>fn-new-</c> id.
    /// </summary>
    [Fact]
    public void The_notes_that_cite_each_other_point_at_each_other_afterwards()
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "footnotes");
        HtmlResource citing = book.GetHtmlResources().Single(h => h.Filename == "citing.xhtml");
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);

        // Act
        FootnoteEditor.Convert(
            book, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, null, TestContext.Current.CancellationToken)
            .Problems.Should().BeEmpty();

        // Assert
        string text = citing.GetText();
        text.Should().NotContain("fn-new-", "no temporary id is left behind")
            .And.NotContain("id=\"one\"").And.NotContain("id=\"left\"").And.NotContain("id=\"right\"");

        // Every note of the file now holds Signet's references, and every reference leads to a note of the file.
        List<Footnote> notes = FootnoteScanner.Scan(book).Where(f => ReferenceEquals(f.NoteFile, citing)).ToList();
        notes.Should().HaveCount(6, "five notes, one of them read twice");
        foreach (Footnote note in notes)
        {
            text.Should().Contain("id=\"" + note.NoteId + "\"").And.Contain("id=\"" + note.ReferenceId + "\"");
        }
    }

    /// <summary>
    /// A footnote written inside a comment is not a footnote, and neither is one written inside a script or a style.
    /// A scanner that read the file as plain text would find three notes in that file instead of one.
    /// </summary>
    [Fact]
    public void A_footnote_inside_a_comment_or_a_script_is_not_found()
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "markup");

        // Act
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

        // Assert
        footnotes.Select(f => f.NoteId).Should().BeEquivalentTo(OnlyRealNotes);
        footnotes.Should().NotContain(f => f.NoteId == "ghost", "that one is commented out");
        footnotes.Should().NotContain(f => f.NoteId == "not-a-note", "that one is a string in a script");
    }

    /// <summary>
    /// An id written twice does not mislead the pairing: the file repeats a reference's id on an unrelated element
    /// further down, and the footnote still converts and still links both ways.
    /// </summary>
    [Fact]
    public void A_repeated_id_does_not_mislead_the_conversion()
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "markup");
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);

        // Act
        ConversionResult result = FootnoteEditor.Convert(
            book, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, null, TestContext.Current.CancellationToken);

        // Assert
        result.Problems.Should().BeEmpty();
        result.Converted.Should().Be(2);
        FootnoteScanner.Scan(book).Should().HaveCount(2).And.OnlyContain(f => f.IsSignet);
        FootnoteValidator.Validate(book).Should().BeEmpty();
        LinkIntegrityValidator.Validate(book).Should().BeEmpty();
    }

    /// <summary>
    /// Added in the review of October 2026, for the preview following footnotes: a link is told a footnote link by the
    /// scanned footnotes, not by its look — including a reference written inside another note and two notes citing
    /// each other.
    /// </summary>
    [Theory]
    [InlineData("one", FootnoteLinkKind.Reference)]
    [InlineData("aside", FootnoteLinkKind.Reference)]
    [InlineData("left", FootnoteLinkKind.Reference)]
    [InlineData("right", FootnoteLinkKind.Reference)]
    [InlineData("one-ref", FootnoteLinkKind.Backlink)]
    [InlineData("aside-ref", FootnoteLinkKind.Backlink)]
    [InlineData("right-ref", FootnoteLinkKind.Backlink)]
    [InlineData("no-such-id", FootnoteLinkKind.None)]
    [InlineData("", FootnoteLinkKind.None)]
    public void Links_of_the_broken_book_are_told_apart_by_its_footnotes(string fragment, FootnoteLinkKind expected)
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "footnotes");
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        string citing = book.GetHtmlResources().Single(h => h.Filename == "citing.xhtml").BookPath;

        // Act
        FootnoteLinkKind kind = FootnoteNavigation.Classify(footnotes, citing, citing, fragment);

        // Assert
        kind.Should().Be(expected);
    }

    [Fact]
    public void A_link_to_a_note_from_a_file_without_its_reference_is_no_footnote_link()
    {
        // Arrange
        using TempDir temp = new("broken-");
        using Book book = Open(temp, "footnotes");
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        string citing = book.GetHtmlResources().Single(h => h.Filename == "citing.xhtml").BookPath;
        string awkward = book.GetHtmlResources().Single(h => h.Filename == "awkward.xhtml").BookPath;

        // Act
        FootnoteLinkKind kind = FootnoteNavigation.Classify(footnotes, awkward, citing, "one");

        // Assert
        kind.Should().Be(FootnoteLinkKind.None);
    }
}
