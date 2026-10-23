using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>
/// The footnote pipeline over the markup of a real, published book
/// (<see cref="CorpusPaths.RealVerseChapters"/>). The hand-written fixtures of the other tests say what each step
/// does; this one says that the steps still work on markup nobody wrote for them — the wrappers a web-to-EPUB tool
/// emits, notes read from three places at once, hundreds of ids per file, a doubled folder name.
/// </summary>
public sealed class RealBookFootnoteTests
{
    private static readonly string[] KnownFaults = { "Validation_FootnoteOrphan", "Validation_FootnoteManyRefs" };

    // Six orphaned notes whose links back lead nowhere, and a leftover of the website the book was made from:
    // href="otworz.php?skrot=Wj 12,51". Both are how the book was published.
    private static readonly string[] KnownLinkFaults = { "Validation_BadFragmentSameFile", "Validation_DeadLink" };

    private static Book Open(TempDir temp) => new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.RealVerseChapters, temp)).GetBook();

    [Fact]
    public void The_book_loads_with_the_footnotes_of_other_origin_it_was_taken_with()
    {
        // Arrange
        using TempDir temp = new("real-");
        using Book book = Open(temp);

        // Act
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

        // Assert
        book.GetHtmlResourcesExcludingNav().Should().HaveCount(10);
        footnotes.Should().NotBeEmpty().And.OnlyContain(f => !f.IsSignet, "none of them is Signet's yet");
        footnotes.Should().OnlyContain(f => f.Placement == FootnotePlacement.Local, "each note sits in the file of its reference");

        // Assert — the book arrives with faults of its own, kept on purpose: a note nothing links to, and notes read
        // from two places without a link back to each. A real book is not clean, and the tests below have to live
        // with that rather than with a fixture tidied up for them.
        FootnoteValidator.Validate(book).Select(r => r.Code).Distinct().Should().BeEquivalentTo(KnownFaults);
        LinkIntegrityValidator.Validate(book).Select(r => r.Code).Distinct().Should()
            .BeEquivalentTo(KnownLinkFaults, "the orphaned notes link back to ids that are not in the book");
    }

    /// <summary>
    /// A note read from several places is one shared note, not several notes. The scanner gives a footnote per
    /// reference, so the count of footnotes is above the count of notes exactly where the book shares one.
    /// </summary>
    [Fact]
    public void A_note_read_from_several_places_is_found_as_one_shared_note()
    {
        // Arrange
        using TempDir temp = new("real-");
        using Book book = Open(temp);

        // Act
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

        // Assert
        List<IGrouping<(string, string), Footnote>> shared = footnotes
            .GroupBy(f => (f.NoteFile.BookPath, f.NoteId))
            .Where(g => g.Count() > 1)
            .ToList();
        shared.Should().NotBeEmpty("the book reads some of its notes from more than one place");
        shared.Should().Contain(g => g.Count() == 3, "two notes of 2kor-03 are read from three places each");
        foreach (IGrouping<(string, string), Footnote> note in shared)
        {
            note.Select(f => f.ReferenceOccurrence).Should().OnlyHaveUniqueItems();
            note.Should().OnlyContain(f => f.ReferenceCount == note.Count());
        }
    }

    [Fact]
    public void Converting_the_whole_book_leaves_Signets_footnotes_and_nothing_to_complain_about()
    {
        // Arrange
        using TempDir temp = new("real-");
        using Book book = Open(temp);
        int before = FootnoteScanner.Scan(book).Count;
        Dictionary<(string, string), int> complaintsBefore = Complaints(book);
        Dictionary<(string, string), int> linksBefore = Links(book);
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);

        // Assert
        plan.Items.Should().NotBeEmpty().And.OnlyContain(i => i.CanConvert, "the whole book can be converted");

        // Act
        ConversionResult result = FootnoteEditor.Convert(
            book, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, null, TestContext.Current.CancellationToken);

        // Assert
        result.Problems.Should().BeEmpty();
        result.Converted.Should().Be(plan.Items.Count);
        IReadOnlyList<Footnote> after = FootnoteScanner.Scan(book);
        after.Should().HaveCount(before).And.OnlyContain(f => f.IsSignet);

        // The book came with complaints of its own; what matters is that the conversion added none. This is the same
        // rule the conversion checks for itself (F7), held here against the validators directly.
        foreach (((string code, string path), int count) in Complaints(book))
        {
            count.Should().BeLessThanOrEqualTo(
                complaintsBefore.GetValueOrDefault((code, path)), "the conversion added no {0} in {1}", code, path);
        }

        foreach (((string code, string path), int count) in Links(book))
        {
            count.Should().BeLessThanOrEqualTo(
                linksBefore.GetValueOrDefault((code, path)), "the conversion added no {0} in {1}", code, path);
        }
    }

    /// <summary>
    /// The shared notes keep a letter per reference after the conversion, and the numbering is the same whether it
    /// runs once or twice — a renumbering that moved anything on the second run would be rewriting a settled book.
    /// </summary>
    [Fact]
    public void The_converted_book_is_numbered_the_same_way_twice()
    {
        // Arrange
        using TempDir temp = new("real-");
        using Book book = Open(temp);
        ConversionPlan plan = FootnoteEditor.PlanConversion(book);
        FootnoteEditor.Convert(
            book, new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false), null, null, TestContext.Current.CancellationToken)
            .Problems.Should().BeEmpty();
        List<string> markers = Markers(book);

        // Assert
        markers.Should().Contain(m => m.EndsWith("a-ref", StringComparison.Ordinal), "a shared note gets a letter per reference")
            .And.Contain(m => m.EndsWith("b-ref", StringComparison.Ordinal));

        // Act
        FootnoteEditor.Renumber(book, null, null, TestContext.Current.CancellationToken);

        // Assert
        Markers(book).Should().Equal(markers);
    }

    // How many of each kind of complaint a validator makes about each file.
    private static Dictionary<(string Code, string BookPath), int> Complaints(Book book) => Count(FootnoteValidator.Validate(book));

    private static Dictionary<(string Code, string BookPath), int> Links(Book book) => Count(LinkIntegrityValidator.Validate(book));

    private static Dictionary<(string Code, string BookPath), int> Count(IReadOnlyList<ValidationResult> results) =>
        results.GroupBy(r => (r.Code, r.BookPath)).ToDictionary(g => g.Key, g => g.Count());

    private static List<string> Markers(Book book) =>
        FootnoteScanner.Scan(book).Select(f => f.NoteId + "/" + f.ReferenceId).ToList();
}
