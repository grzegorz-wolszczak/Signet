using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>Tests for <see cref="SearchOperations"/> — counting and replacing across text and files.</summary>
public sealed class SearchOperationsTests
{
    private static string Normal(string text) =>
        SearchRegexBuilder.BuildSearchRegex(text, SearchMode.Normal, SearchOptions.None, false);

    [Fact]
    public void CountInText_CountsAllMatches()
    {
        SearchOperations.CountInText(Normal("ab"), "ab AB ab xy ab").Should().Be(4);
    }

    [Fact]
    public void CountInText_InvalidRegex_ReturnsZero()
    {
        SearchOperations.CountInText("(unclosed", "whatever").Should().Be(0);
    }

    [Fact]
    public void PerformGlobalReplace_ReplacesEveryMatchWithBackreferences()
    {
        (string text, int count) = SearchOperations.PerformGlobalReplace(
            "<b>x</b> <b>yy</b>", @"<b>(.*?)</b>", @"<i>\1</i>");

        count.Should().Be(2);
        text.Should().Be("<i>x</i> <i>yy</i>");
    }

    [Fact]
    public void PerformGlobalReplace_NoMatch_ReturnsInputUnchanged()
    {
        (string text, int count) = SearchOperations.PerformGlobalReplace("abc", Normal("zzz"), "q");

        count.Should().Be(0);
        text.Should().Be("abc");
    }

    [Fact]
    public void CountInFiles_ReturnsPerFileCounts()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();

        List<TextResource> chapters = book.GetHtmlResources()
            .Where(h => h.Filename.StartsWith("chapter", System.StringComparison.Ordinal))
            .Cast<TextResource>()
            .ToList();
        foreach (TextResource chapter in chapters)
        {
            chapter.SetText("<p>alpha</p><p>alpha</p>");
        }

        IReadOnlyList<SearchOperations.FileSearchResult> result =
            SearchOperations.CountInFiles(Normal("alpha"), chapters);

        result.Should().HaveCount(chapters.Count);
        result.Sum(r => r.Count).Should().Be(chapters.Count * 2);
    }

    [Fact]
    public void ReplaceInAllFiles_WritesBackAndReportsChangedFiles()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp)).GetBook();

        var first = (TextResource)book.GetHtmlResources()[0];
        var second = (TextResource)book.GetHtmlResources()[1];
        first.SetText("<p>needle needle</p>");
        second.SetText("<p>nothing</p>");

        IReadOnlyList<SearchOperations.FileSearchResult> result = SearchOperations.ReplaceInAllFiles(
            Normal("needle"), "pin", new[] { first, second });

        result.Should().ContainSingle();
        result[0].BookPath.Should().Be(first.BookPath);
        result[0].Count.Should().Be(2);
        first.GetText().Should().Be("<p>pin pin</p>");
        second.GetText().Should().Be("<p>nothing</p>");
    }
}
