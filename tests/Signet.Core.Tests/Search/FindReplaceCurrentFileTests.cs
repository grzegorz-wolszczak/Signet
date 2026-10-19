using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="CodeEditorSearch"/> — Code Editor search behaviour in "current file" mode.
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
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "one two one two";

        // Act
        FindResult result = search.FindNext(text, 0, 0, 0, Normal("one"), SearchDirection.Down, wrap: false);

        // Assert
        result.Found.Should().BeTrue();
        (result.Start, result.End).Should().Be((0, 3));

        // The next Find starts at the end of the previous match.
        // Act
        FindResult second = search.FindNext(text, result.Start, result.End, result.End, Normal("one"), SearchDirection.Down, false);

        // Assert
        (second.Start, second.End).Should().Be((8, 11));
    }

    [Fact]
    public void FindNext_Up_SelectsPreviousMatch()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "aa bb aa bb aa";

        // Act
        FindResult result = search.FindNext(text, 12, 14, 12, Normal("aa"), SearchDirection.Up, wrap: false);

        // Assert
        (result.Start, result.End).Should().Be((6, 8));
    }

    [Fact]
    public void FindNext_NoWrap_ReturnsNotFoundAtEnd()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "abc abc";

        // Act
        FindResult result = search.FindNext(text, 4, 7, 7, Normal("abc"), SearchDirection.Down, wrap: false);

        // Assert
        result.Found.Should().BeFalse();
    }

    [Fact]
    public void FindNext_Wrap_FindsFromStartAndReportsWrapped()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "abc abc";

        // Act
        FindResult result = search.FindNext(text, 4, 7, 7, Normal("abc"), SearchDirection.Down, wrap: true);

        // Assert
        result.Found.Should().BeTrue();
        result.Wrapped.Should().BeTrue();
        (result.Start, result.End).Should().Be((0, 3));
    }

    [Fact]
    public void FindNext_Normal_IsCaseInsensitive_CaseSensitiveIsNot()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "Hello HELLO hello";

        // Act & Assert
        search.FindNext(text, 0, 0, 0, Normal("hello"), SearchDirection.Down, false)
            .Start.Should().Be(0);

        // Act & Assert
        search.FindNext(text, 0, 0, 0, CaseSensitive("hello"), SearchDirection.Down, false)
            .Start.Should().Be(12);
    }

    [Fact]
    public void FindNext_Regex_Matches()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "id=12, id=345, id=6";

        // Act
        FindResult result = search.FindNext(text, 0, 0, 0, @"id=(\d+)", SearchDirection.Down, false);

        // Assert
        (result.Start, result.End).Should().Be((0, 5));
    }

    // ------------------------------------------------------------- Count ----- //

    [Fact]
    public void Count_Wrap_CountsAllMatches()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act & Assert
        search.Count("a a a a", 3, Normal("a"), SearchDirection.Down, wrap: true).Should().Be(4);
    }

    [Fact]
    public void Count_NoWrap_Down_CountsFromCaret()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act & Assert
        // "a a a a" — caret at offset 3 (after the second 'a'); 2 remain going down.
        search.Count("a a a a", 3, Normal("a"), SearchDirection.Down, wrap: false).Should().Be(2);
    }

    // ------------------------------------------------------- RememberMatch -- //

    [Fact]
    public void RememberMatch_enables_ReplaceSelected_with_backreferences()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "x <b>cat</b> y";
        string pattern = SearchRegexBuilder.BuildSearchRegex(
            "<b>(\\w+)</b>", SearchMode.Regex, SearchOptions.None, false);

        // Act
        search.RememberMatch(text, pattern, 2, 12).Should().BeTrue();
        ReplaceResult result = search.ReplaceSelected(
            text, 2, 12, 12, pattern, "<i>\\1</i>", SearchDirection.Down, replaceCurrent: true);

        // Assert
        result.Replaced.Should().BeTrue();
        result.NewText.Should().Be("x <i>cat</i> y");
    }

    [Fact]
    public void RememberMatch_rejects_a_range_that_is_not_exactly_a_match()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act
        search.FindNext("cat cat", 0, 0, 0, Normal("cat"), SearchDirection.Down, false);

        // Act & Assert
        search.RememberMatch("cat cat", Normal("cat"), 1, 4).Should().BeFalse();

        // Assert
        search.HasLastMatch.Should().BeFalse("a stale match must not be replaced");
    }

    // ------------------------------------------------------- ReplaceSelected - //

    [Fact]
    public void ReplaceSelected_RequiresPriorFindWithSamePattern()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "cat cat";

        // Without a prior Find — no replacement.
        // Act & Assert
        search.ReplaceSelected(text, 0, 3, 3, Normal("cat"), "dog", SearchDirection.Down, replaceCurrent: true)
            .Replaced.Should().BeFalse();

        // Act
        FindResult found = search.FindNext(text, 0, 0, 0, Normal("cat"), SearchDirection.Down, false);
        ReplaceResult replaced = search.ReplaceSelected(
            text, found.Start, found.End, found.End, Normal("cat"), "dog", SearchDirection.Down, replaceCurrent: true);

        // Assert
        replaced.Replaced.Should().BeTrue();
        replaced.NewText.Should().Be("dog cat");
        (replaced.SelectionStart, replaced.SelectionEnd).Should().Be((0, 3));
    }

    [Fact]
    public void ReplaceSelected_Regex_ExpandsBackreferences()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "name: Smith, John";
        const string pattern = @"(\w+), (\w+)";

        // Act
        FindResult found = search.FindNext(text, 0, 0, 0, pattern, SearchDirection.Down, false);
        ReplaceResult replaced = search.ReplaceSelected(
            text, found.Start, found.End, found.End, pattern, @"\2 \1", SearchDirection.Down, replaceCurrent: true);

        // Assert
        replaced.NewText.Should().Be("name: John Smith");
    }

    [Fact]
    public void ReplaceSelected_SecondCallWithoutFind_Fails()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "x x x";

        // Act
        FindResult found = search.FindNext(text, 0, 0, 0, Normal("x"), SearchDirection.Down, false);
        ReplaceResult first = search.ReplaceSelected(text, found.Start, found.End, found.End, Normal("x"), "y", SearchDirection.Down, true);

        // Assert
        first.Replaced.Should().BeTrue();

        // Act & Assert
        // The remembered match has been invalidated.
        search.ReplaceSelected(first.NewText, 0, 1, 1, Normal("x"), "y", SearchDirection.Down, true)
            .Replaced.Should().BeFalse();
    }

    // ---------------------------------------------------------- ReplaceAll --- //

    [Fact]
    public void ReplaceAll_Wrap_ReplacesEveryMatch()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act
        ReplaceAllResult result = search.ReplaceAll(
            "a-a-a-a", 0, Normal("a"), "b", SearchDirection.Down, wrap: true);

        // Assert
        result.Count.Should().Be(4);
        result.NewText.Should().Be("b-b-b-b");
    }

    [Fact]
    public void ReplaceAll_Regex_WithBackreferences()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "<b>x</b> <b>y</b>";

        // Act
        ReplaceAllResult result = search.ReplaceAll(
            text, 0, @"<b>(.*?)</b>", @"<i>\1</i>", SearchDirection.Down, wrap: true);

        // Assert
        result.Count.Should().Be(2);
        result.NewText.Should().Be("<i>x</i> <i>y</i>");
    }

    [Fact]
    public void ReplaceAll_NoWrap_Down_OnlyFromCaret()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "a a a a";

        // Act
        // Caret at offset 4 — matches ending before it (0-1, 2-3) are skipped.
        ReplaceAllResult result = search.ReplaceAll(text, 4, Normal("a"), "b", SearchDirection.Down, wrap: false);

        // Assert
        result.Count.Should().Be(2);
        result.NewText.Should().Be("a a b b");
    }

    // -------------------------------------------------------- marked text --- //

    [Fact]
    public void MarkSelection_RestrictsFindToRegion()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "cat cat cat cat";

        // Act
        search.MarkSelection(4, 11).Should().BeTrue(); // covers the second and third "cat"

        // Assert
        search.Marked.IsMarked.Should().BeTrue();

        // Act
        FindResult first = search.FindNext(text, 0, 0, 0, Normal("cat"), SearchDirection.Down, wrap: true);

        // Assert
        (first.Start, first.End).Should().Be((4, 7));

        // Act
        FindResult second = search.FindNext(text, first.Start, first.End, first.End, Normal("cat"), SearchDirection.Down, wrap: true);

        // Assert
        (second.Start, second.End).Should().Be((8, 11));

        // There are no matches outside the region — wrapping returns to the start of the region.
        // Act
        FindResult third = search.FindNext(text, second.Start, second.End, second.End, Normal("cat"), SearchDirection.Down, wrap: true);

        // Assert
        (third.Start, third.End).Should().Be((4, 7));
        third.Wrapped.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAll_RestrictedToMarkedRegion()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "cat cat cat cat";

        // Act
        search.MarkSelection(4, 11);
        ReplaceAllResult result = search.ReplaceAll(text, 4, Normal("cat"), "dog", SearchDirection.Down, wrap: true);

        // Assert
        result.Count.Should().Be(2);
        result.NewText.Should().Be("cat dog dog cat");
        // The region is adjusted by the length delta of the replacements (dog == cat, so unchanged here).
        search.Marked.End.Should().Be(11);
    }

    [Fact]
    public void Count_RestrictedToMarkedRegion()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "cat cat cat cat";

        // Act
        search.MarkSelection(4, 11);

        // Act & Assert
        search.Count(text, 4, Normal("cat"), SearchDirection.Down, wrap: true).Should().Be(2);
    }

    [Fact]
    public void FindAll_Wrap_ListsEveryMatchWithItsOffsets()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act & Assert
        search.FindAll("a ab a", 3, Normal("a"), SearchDirection.Down, wrap: true)
            .Should().Equal((0, 1), (2, 3), (5, 6));
    }

    [Fact]
    public void FindAll_NoWrap_Down_StartsAtTheCaretButKeepsOffsetsInTheWholeText()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act & Assert
        search.FindAll("a a a a", 3, Normal("a"), SearchDirection.Down, wrap: false)
            .Should().Equal((4, 5), (6, 7));
    }

    [Fact]
    public void FindAll_NoWrap_Up_ListsTheMatchesBeforeTheCaret()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act & Assert
        search.FindAll("a a a a", 4, Normal("a"), SearchDirection.Up, wrap: false)
            .Should().Equal((0, 1), (2, 3));
    }

    [Fact]
    public void FindAll_RestrictedToMarkedRegion_OffsetsAreInTheWholeText()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "cat cat cat cat";

        // Act
        search.MarkSelection(4, 11);

        // Act & Assert
        search.FindAll(text, 4, Normal("cat"), SearchDirection.Down, wrap: true)
            .Should().Equal((4, 7), (8, 11));
    }

    [Fact]
    public void FindAll_InvalidRegex_IsEmpty()
    {
        // Act & Assert
        new CodeEditorSearch().FindAll("whatever", 0, "(unclosed", SearchDirection.Down, wrap: true).Should().BeEmpty();
    }

    [Fact]
    public void NotifyTextChanged_ClearsMarkedRegionAndLastMatch()
    {
        // Arrange
        var search = new CodeEditorSearch();
        const string text = "x x x";

        // Act
        search.MarkSelection(0, 3);
        search.FindNext(text, 0, 0, 0, Normal("x"), SearchDirection.Down, true);

        // Assert
        search.HasLastMatch.Should().BeTrue();

        // Act
        search.NotifyTextChanged();

        // Assert
        search.Marked.IsMarked.Should().BeFalse();
        search.HasLastMatch.Should().BeFalse();
    }

    [Fact]
    public void MarkSelection_EmptySelection_ClearsRegion()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act
        search.MarkSelection(2, 8);

        // Act & Assert
        search.MarkSelection(5, 5).Should().BeFalse();

        // Assert
        search.Marked.IsMarked.Should().BeFalse();
    }

    [Fact]
    public void InvalidRegex_FindReturnsNotFound_CountReturnsZero()
    {
        // Arrange
        var search = new CodeEditorSearch();

        // Act & Assert
        search.FindNext("abc", 0, 0, 0, "(unclosed", SearchDirection.Down, true).Found.Should().BeFalse();

        // Act & Assert
        search.Count("abc", 0, "(unclosed", SearchDirection.Down, true).Should().Be(0);
    }
}
