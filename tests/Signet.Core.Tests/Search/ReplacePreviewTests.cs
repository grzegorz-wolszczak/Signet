using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="ReplacePreview"/> — building the dry-run / replacement chooser table and
/// applying the selected replacements.
/// </summary>
public sealed class ReplacePreviewTests
{
    private static string Normal(string text) =>
        SearchRegexBuilder.BuildSearchRegex(text, SearchMode.Normal, SearchOptions.None, false);

    private static string Regex(string pattern) =>
        SearchRegexBuilder.BuildSearchRegex(pattern, SearchMode.Regex, SearchOptions.None, false);

    private static List<TextResource> Chapters(Book book, params string[] texts)
    {
        List<TextResource> chapters = book.GetHtmlResources().Cast<TextResource>().Take(texts.Length).ToList();
        for (int i = 0; i < texts.Length; i++)
        {
            chapters[i].SetText(texts[i]);
        }

        return chapters;
    }

    [Fact]
    public void BuildRows_ReturnsMatchWithReplacementAndTrimmedContext()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        List<TextResource> chapters = Chapters(book, "aa bb quick cc dd");

        IReadOnlyList<ReplacePreviewRow> rows =
            ReplacePreview.BuildRows(chapters, Normal("quick"), "slow", 20);

        rows.Should().ContainSingle();
        ReplacePreviewRow row = rows[0];
        row.BookPath.Should().Be(chapters[0].BookPath);
        row.MatchText.Should().Be("quick");
        row.ReplacementText.Should().Be("slow");
        row.CanReplace.Should().BeTrue();
        row.PriorContext.Should().Be(" bb ");
        row.PostContext.Should().Be(" cc ");
        row.BeforeSnippet.Should().Be(" bb quick cc ");
        row.AfterSnippet.Should().Be(" bb slow cc ");
    }

    [Fact]
    public void BuildRows_MultipleFiles_ReturnsRowsInDocumentOrder()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        List<TextResource> chapters = Chapters(book, "x cat x cat", "cat here");

        IReadOnlyList<ReplacePreviewRow> rows =
            ReplacePreview.BuildRows(chapters, Normal("cat"), "dog", 10);

        rows.Should().HaveCount(3);
        rows.Select(r => r.Offset).Should().Equal(2, 8, 0);
        rows.Select(r => r.BookPath)
            .Should().Equal(chapters[0].BookPath, chapters[0].BookPath, chapters[1].BookPath);
    }

    [Fact]
    public void BuildRows_Regex_ExpandsBackreferencePerMatch()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        List<TextResource> chapters = Chapters(book, "<b>x</b> <b>yy</b>");

        IReadOnlyList<ReplacePreviewRow> rows =
            ReplacePreview.BuildRows(chapters, Regex("<b>(.*?)</b>"), @"<i>\1</i>", 5);

        rows.Should().HaveCount(2);
        rows[0].ReplacementText.Should().Be("<i>x</i>");
        rows[1].ReplacementText.Should().Be("<i>yy</i>");
    }

    [Fact]
    public void BuildRows_InvalidRegex_ReturnsEmpty()
    {
        ReplacePreview.BuildRows(System.Array.Empty<TextResource>(), "(unclosed", "x", 20)
            .Should().BeEmpty();
    }

    [Fact]
    public void BuildRows_FunctionReplacement_MarksRowAsNotReplaceable()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();
        List<TextResource> chapters = Chapters(book, "one two one");

        IReadOnlyList<ReplacePreviewRow> rows =
            ReplacePreview.BuildRows(chapters, Normal("one"), @"\F<my_func>", 10);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => !r.CanReplace && r.ReplacementText == r.MatchText);
        rows.Should().OnlyContain(r => r.AfterSnippet == r.BeforeSnippet);
    }

    [Fact]
    public void GetPriorContext_TrimsLeadingPartialWord_AndReplacesNewlines()
    {
        ReplacePreview.GetPriorContext(6, "aa bb quick cc", 20).Should().Be(" bb ");
        ReplacePreview.GetPriorContext(4, "x\nyz", 10).Should().Be(" yz");
    }

    [Fact]
    public void GetPostContext_TrimsTrailingPartialWord()
    {
        ReplacePreview.GetPostContext(2, "ab cd efgh", 5).Should().Be(" cd ");
    }

    [Fact]
    public void ApplySelected_AppliesOnlySelectedRows_BottomToTop()
    {
        static ReplacePreviewRow Cat(int offset) =>
            new("x.xhtml", offset, 3, "cat", "dog", string.Empty, string.Empty, true);

        (string newText, int count) = ReplacePreview.ApplySelected(
            "cat cat cat", new[] { Cat(0), Cat(8) });

        count.Should().Be(2);
        newText.Should().Be("dog cat dog");
    }

    [Fact]
    public void ApplySelected_SkipsRowWhoseMatchTextNoLongerMatches()
    {
        var row = new ReplacePreviewRow("x.xhtml", 0, 3, "cat", "dog", string.Empty, string.Empty, true);

        (string newText, int count) = ReplacePreview.ApplySelected("dog here", new[] { row });

        count.Should().Be(0);
        newText.Should().Be("dog here");
    }
}
