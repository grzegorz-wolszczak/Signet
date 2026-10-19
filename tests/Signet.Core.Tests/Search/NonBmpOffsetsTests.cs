using System;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Characters outside the Basic Multilingual Plane (emoji, mathematical letters) take two UTF-16 code units. Search
/// results and formatting must use the same UTF-16 offsets as the editor (calibre 6.10, 6.26 and 7.17 fixed shifted
/// selections in this case).
/// </summary>
public sealed class NonBmpOffsetsTests
{
    private const string Text = "<p>𝔸𝔹 one 😀 two 😀 one</p>";

    private static string Normal(string text) =>
        SearchRegexBuilder.BuildSearchRegex(text, SearchMode.Normal, SearchOptions.None, false);

    private static string Regex(string pattern) =>
        SearchRegexBuilder.BuildSearchRegex(pattern, SearchMode.Regex, SearchOptions.None, false);

    private static int IndexOf(string value, int from = 0) => Text.IndexOf(value, from, StringComparison.Ordinal);

    [Fact]
    public void FindNext_down_and_up_select_the_utf16_range_of_the_match()
    {
        // Arrange
        CodeEditorSearch search = new();
        int second = IndexOf("one", IndexOf("one") + 1);

        // Act
        FindResult down = search.FindNext(Text, 0, 0, 0, Normal("one"), SearchDirection.Down, wrap: false);
        FindResult next = search.FindNext(Text, down.Start, down.End, down.End, Normal("one"), SearchDirection.Down, wrap: false);
        FindResult up = new CodeEditorSearch().FindNext(Text, Text.Length, Text.Length, Text.Length, Normal("one"), SearchDirection.Up, wrap: false);

        // Assert
        (down.Start, down.End).Should().Be((IndexOf("one"), IndexOf("one") + 3));
        (next.Start, next.End).Should().Be((second, second + 3));
        (up.Start, up.End).Should().Be((second, second + 3));
    }

    [Fact]
    public void A_regex_match_that_spans_non_bmp_characters_covers_whole_surrogate_pairs()
    {
        // Act
        FindResult result = new CodeEditorSearch().FindNext(Text, 0, 0, 0, Regex("😀 two 😀"), SearchDirection.Down, wrap: false);

        // Assert
        (result.Start, result.End).Should().Be((IndexOf("😀"), IndexOf("😀") + "😀 two 😀".Length));
        Text[result.Start..result.End].Should().Be("😀 two 😀");
    }

    [Fact]
    public void A_dot_matches_a_whole_non_bmp_character()
    {
        // Act
        FindResult result = new CodeEditorSearch().FindNext(Text, 0, 0, 0, Regex("o. t"), SearchDirection.Down, wrap: false);

        // Assert
        result.Found.Should().BeFalse("'.' is one character, the emoji between 'one ' and ' two' is two code units only in UTF-16");

        // Act & Assert
        new CodeEditorSearch().FindNext(Text, 0, 0, 0, Regex(" . two"), SearchDirection.Down, wrap: false)
            .Start.Should().Be(IndexOf("😀") - 1);
    }

    [Fact]
    public void ReplaceAll_and_ReplaceSelected_keep_the_text_around_non_bmp_characters()
    {
        // Act
        ReplaceAllResult all = new CodeEditorSearch().ReplaceAll(Text, 0, Normal("one"), "1", SearchDirection.Down, wrap: true);

        // Assert
        all.Count.Should().Be(2);
        all.NewText.Should().Be("<p>𝔸𝔹 1 😀 two 😀 1</p>");

        int two = IndexOf("two");

        // Arrange
        CodeEditorSearch search = new();

        // Act
        search.FindNext(Text, two, two, two, Normal("two"), SearchDirection.Down, wrap: false);
        ReplaceResult selected = search.ReplaceSelected(Text, two, two + 3, two + 3, Normal("two"), "2", SearchDirection.Down, true);

        // Assert
        selected.NewText.Should().Be("<p>𝔸𝔹 one 😀 2 😀 one</p>");
    }

    [Fact]
    public void Count_counts_matches_after_non_bmp_characters()
    {
        // Act & Assert
        new CodeEditorSearch().Count(Text, 0, Normal("one"), SearchDirection.Down, wrap: true).Should().Be(2);
    }

    [Fact]
    public void ToggleInline_wraps_exactly_the_selection_after_non_bmp_characters()
    {
        // Arrange
        string html = "<html><body><p>𝔸 😀 word 😀</p></body></html>";
        int start = html.IndexOf("word", StringComparison.Ordinal);

        // Act
        FormatEdit edit = CodeFormatOperations.ToggleInline(html, start, start + 4, "b");

        // Assert
        edit.Text.Should().Be("<html><body><p>𝔸 😀 <b>word</b> 😀</p></body></html>");
        edit.Text[edit.SelectionStart..edit.SelectionEnd].Should().Contain("word");
    }
}
