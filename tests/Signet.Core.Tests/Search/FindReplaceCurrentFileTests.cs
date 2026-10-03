using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="CodeViewSearch"/> — Code View search behaviour in "current file" mode.
/// </summary>
public sealed class FindReplaceCurrentFileTests
{
    private static string Normal(string text) =>
        SearchRegexBuilder.BuildSearchRegex(text, SearchMode.Normal, SearchOptions.None, false);

    private static string CaseSensitive(string text) =>
        SearchRegexBuilder.BuildSearchRegex(text, SearchMode.CaseSensitive, SearchOptions.None, false);

    // ---------------------------------------------------------------- Find --- //

    [Fact]
    public void FindNext_Down_SelectsFirstMatchAfterCaret()
    {
        var search = new CodeViewSearch();
        const string text = "one two one two";

        FindResult result = search.FindNext(text, 0, 0, 0, Normal("one"), SearchDirection.Down, wrap: false);

        result.Found.Should().BeTrue();
        (result.Start, result.End).Should().Be((0, 3));

        // The next Find starts at the end of the previous match.
        FindResult second = search.FindNext(text, result.Start, result.End, result.End, Normal("one"), SearchDirection.Down, false);
        (second.Start, second.End).Should().Be((8, 11));
    }

    [Fact]
    public void FindNext_Up_SelectsPreviousMatch()
    {
        var search = new CodeViewSearch();
        const string text = "aa bb aa bb aa";

        FindResult result = search.FindNext(text, 12, 14, 12, Normal("aa"), SearchDirection.Up, wrap: false);

        (result.Start, result.End).Should().Be((6, 8));
    }

    [Fact]
    public void FindNext_NoWrap_ReturnsNotFoundAtEnd()
    {
        var search = new CodeViewSearch();
        const string text = "abc abc";

        FindResult result = search.FindNext(text, 4, 7, 7, Normal("abc"), SearchDirection.Down, wrap: false);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_Wrap_FindsFromStartAndReportsWrapped()
    {
        var search = new CodeViewSearch();
        const string text = "abc abc";

        FindResult result = search.FindNext(text, 4, 7, 7, Normal("abc"), SearchDirection.Down, wrap: true);

        result.Found.Should().BeTrue();
        result.Wrapped.Should().BeTrue();
        (result.Start, result.End).Should().Be((0, 3));
    }

    [Fact]
    public void FindNext_Normal_IsCaseInsensitive_CaseSensitiveIsNot()
    {
        var search = new CodeViewSearch();
        const string text = "Hello HELLO hello";

        search.FindNext(text, 0, 0, 0, Normal("hello"), SearchDirection.Down, false)
            .Start.Should().Be(0);

        search.FindNext(text, 0, 0, 0, CaseSensitive("hello"), SearchDirection.Down, false)
            .Start.Should().Be(12);
    }

    [Fact]
    public void FindNext_Regex_Matches()
    {
        var search = new CodeViewSearch();
        const string text = "id=12, id=345, id=6";

        FindResult result = search.FindNext(text, 0, 0, 0, @"id=(\d+)", SearchDirection.Down, false);

        (result.Start, result.End).Should().Be((0, 5));
    }

    // ------------------------------------------------------------- Count ----- //

    [Fact]
    public void Count_Wrap_CountsAllMatches()
    {
        var search = new CodeViewSearch();
        search.Count("a a a a", 3, Normal("a"), SearchDirection.Down, wrap: true).Should().Be(4);
    }

    [Fact]
    public void Count_NoWrap_Down_CountsFromCaret()
    {
        var search = new CodeViewSearch();
        // "a a a a" — caret at offset 3 (after the second 'a'); 2 remain going down.
        search.Count("a a a a", 3, Normal("a"), SearchDirection.Down, wrap: false).Should().Be(2);
    }

    // ------------------------------------------------------- RememberMatch -- //

    [Fact]
    public void RememberMatch_enables_ReplaceSelected_with_backreferences()
    {
        var search = new CodeViewSearch();
        const string text = "x <b>cat</b> y";
        string pattern = SearchRegexBuilder.BuildSearchRegex(
            "<b>(\\w+)</b>", SearchMode.Regex, SearchOptions.None, false);

        search.RememberMatch(text, pattern, 2, 12).Should().BeTrue();
        ReplaceResult result = search.ReplaceSelected(
            text, 2, 12, 12, pattern, "<i>\\1</i>", SearchDirection.Down, replaceCurrent: true);

        result.Replaced.Should().BeTrue();
        result.NewText.Should().Be("x <i>cat</i> y");
    }

    [Fact]
    public void RememberMatch_rejects_a_range_that_is_not_exactly_a_match()
    {
        var search = new CodeViewSearch();
        search.FindNext("cat cat", 0, 0, 0, Normal("cat"), SearchDirection.Down, false);

        search.RememberMatch("cat cat", Normal("cat"), 1, 4).Should().BeFalse();

        search.HasLastMatch.Should().BeFalse("a stale match must not be replaced");
    }

    // ------------------------------------------------------- ReplaceSelected - //

    [Fact]
    public void ReplaceSelected_RequiresPriorFindWithSamePattern()
    {
        var search = new CodeViewSearch();
        const string text = "cat cat";

        // Without a prior Find — no replacement.
        search.ReplaceSelected(text, 0, 3, 3, Normal("cat"), "dog", SearchDirection.Down, replaceCurrent: true)
            .Replaced.Should().BeFalse();

        FindResult found = search.FindNext(text, 0, 0, 0, Normal("cat"), SearchDirection.Down, false);
        ReplaceResult replaced = search.ReplaceSelected(
            text, found.Start, found.End, found.End, Normal("cat"), "dog", SearchDirection.Down, replaceCurrent: true);

        replaced.Replaced.Should().BeTrue();
        replaced.NewText.Should().Be("dog cat");
        (replaced.SelectionStart, replaced.SelectionEnd).Should().Be((0, 3));
    }

    [Fact]
    public void ReplaceSelected_Regex_ExpandsBackreferences()
    {
        var search = new CodeViewSearch();
        const string text = "name: Smith, John";
        const string pattern = @"(\w+), (\w+)";

        FindResult found = search.FindNext(text, 0, 0, 0, pattern, SearchDirection.Down, false);
        ReplaceResult replaced = search.ReplaceSelected(
            text, found.Start, found.End, found.End, pattern, @"\2 \1", SearchDirection.Down, replaceCurrent: true);

        replaced.NewText.Should().Be("name: John Smith");
    }

    [Fact]
    public void ReplaceSelected_SecondCallWithoutFind_Fails()
    {
        var search = new CodeViewSearch();
        const string text = "x x x";

        FindResult found = search.FindNext(text, 0, 0, 0, Normal("x"), SearchDirection.Down, false);
        ReplaceResult first = search.ReplaceSelected(text, found.Start, found.End, found.End, Normal("x"), "y", SearchDirection.Down, true);
        first.Replaced.Should().BeTrue();

        // The remembered match has been invalidated.
        search.ReplaceSelected(first.NewText, 0, 1, 1, Normal("x"), "y", SearchDirection.Down, true)
            .Replaced.Should().BeFalse();
    }

    // ---------------------------------------------------------- ReplaceAll --- //

    [Fact]
    public void ReplaceAll_Wrap_ReplacesEveryMatch()
    {
        var search = new CodeViewSearch();

        ReplaceAllResult result = search.ReplaceAll(
            "a-a-a-a", 0, Normal("a"), "b", SearchDirection.Down, wrap: true);

        result.Count.Should().Be(4);
        result.NewText.Should().Be("b-b-b-b");
    }

    [Fact]
    public void ReplaceAll_Regex_WithBackreferences()
    {
        var search = new CodeViewSearch();
        const string text = "<b>x</b> <b>y</b>";

        ReplaceAllResult result = search.ReplaceAll(
            text, 0, @"<b>(.*?)</b>", @"<i>\1</i>", SearchDirection.Down, wrap: true);

        result.Count.Should().Be(2);
        result.NewText.Should().Be("<i>x</i> <i>y</i>");
    }

    [Fact]
    public void ReplaceAll_NoWrap_Down_OnlyFromCaret()
    {
        var search = new CodeViewSearch();
        const string text = "a a a a";

        // Caret at offset 4 — matches ending before it (0-1, 2-3) are skipped.
        ReplaceAllResult result = search.ReplaceAll(text, 4, Normal("a"), "b", SearchDirection.Down, wrap: false);

        result.Count.Should().Be(2);
        result.NewText.Should().Be("a a b b");
    }

    // -------------------------------------------------------- marked text --- //

    [Fact]
    public void MarkSelection_RestrictsFindToRegion()
    {
        var search = new CodeViewSearch();
        const string text = "cat cat cat cat";

        search.MarkSelection(4, 11).Should().BeTrue(); // covers the second and third "cat"
        search.Marked.IsMarked.Should().BeTrue();

        FindResult first = search.FindNext(text, 0, 0, 0, Normal("cat"), SearchDirection.Down, wrap: true);
        (first.Start, first.End).Should().Be((4, 7));

        FindResult second = search.FindNext(text, first.Start, first.End, first.End, Normal("cat"), SearchDirection.Down, wrap: true);
        (second.Start, second.End).Should().Be((8, 11));

        // There are no matches outside the region — wrapping returns to the start of the region.
        FindResult third = search.FindNext(text, second.Start, second.End, second.End, Normal("cat"), SearchDirection.Down, wrap: true);
        (third.Start, third.End).Should().Be((4, 7));
        third.Wrapped.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAll_RestrictedToMarkedRegion()
    {
        var search = new CodeViewSearch();
        const string text = "cat cat cat cat";

        search.MarkSelection(4, 11);
        ReplaceAllResult result = search.ReplaceAll(text, 4, Normal("cat"), "dog", SearchDirection.Down, wrap: true);

        result.Count.Should().Be(2);
        result.NewText.Should().Be("cat dog dog cat");
        // The region is adjusted by the length delta of the replacements (dog == cat, so unchanged here).
        search.Marked.End.Should().Be(11);
    }

    [Fact]
    public void Count_RestrictedToMarkedRegion()
    {
        var search = new CodeViewSearch();
        const string text = "cat cat cat cat";

        search.MarkSelection(4, 11);
        search.Count(text, 4, Normal("cat"), SearchDirection.Down, wrap: true).Should().Be(2);
    }

    [Fact]
    public void NotifyTextChanged_ClearsMarkedRegionAndLastMatch()
    {
        var search = new CodeViewSearch();
        const string text = "x x x";

        search.MarkSelection(0, 3);
        search.FindNext(text, 0, 0, 0, Normal("x"), SearchDirection.Down, true);
        search.HasLastMatch.Should().BeTrue();

        search.NotifyTextChanged();

        search.Marked.IsMarked.Should().BeFalse();
        search.HasLastMatch.Should().BeFalse();
    }

    [Fact]
    public void MarkSelection_EmptySelection_ClearsRegion()
    {
        var search = new CodeViewSearch();
        search.MarkSelection(2, 8);
        search.MarkSelection(5, 5).Should().BeFalse();
        search.Marked.IsMarked.Should().BeFalse();
    }

    [Fact]
    public void InvalidRegex_FindReturnsNotFound_CountReturnsZero()
    {
        var search = new CodeViewSearch();
        search.FindNext("abc", 0, 0, 0, "(unclosed", SearchDirection.Down, true).Found.Should().BeFalse();
        search.Count("abc", 0, "(unclosed", SearchDirection.Down, true).Should().Be(0);
    }
}
