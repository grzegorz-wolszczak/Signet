using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// <see cref="UsageContextSegments"/>: the context of a usage cut into pieces by syntax (the highlighters of Code Editor)
/// and by the searched text.
/// </summary>
public sealed class UsageContextSegmentsTests
{
    private static ClassUsage Usage(string path, ClassUsageKind kind, string context, int matchStart, int matchLength) =>
        new(path, 0, 1, 1, kind, context, 0, matchStart, matchLength);

    [Fact]
    public void The_pieces_put_together_are_the_context()
    {
        // Arrange
        const string context = "<p class=\"calibre4\" id=\"x\">Text &amp; more</p>";

        // Act
        IReadOnlyList<UsageContextSegment> segments = UsageContextSegments.Of(
            Usage("Text/a.xhtml", ClassUsageKind.ClassAttribute, context, 12, 8));

        // Assert
        string.Concat(segments.Select(s => s.Text)).Should().Be(context);
    }

    [Fact]
    public void Markup_is_colored_like_Code_Editor_colors_it()
    {
        // Act
        IReadOnlyList<UsageContextSegment> segments = UsageContextSegments.Of(
            Usage("Text/a.xhtml", ClassUsageKind.ClassAttribute, "<p class=\"note\">", 10, 4));

        // Assert
        segments.Should().Contain(s => s.Text == "<p" && s.Format == SyntaxFormat.XhtmlTagName);
        segments.Should().Contain(s => s.Text.StartsWith("class") && s.Format == SyntaxFormat.XhtmlAttributeName);
        segments.Should().Contain(s => s.Format == SyntaxFormat.XhtmlAttributeValue);
    }

    [Fact]
    public void The_searched_text_is_a_piece_of_its_own_that_keeps_its_color()
    {
        // Act
        IReadOnlyList<UsageContextSegment> segments = UsageContextSegments.Of(
            Usage("Text/a.xhtml", ClassUsageKind.ClassAttribute, "<p class=\"note\">", 10, 4));

        UsageContextSegment match = segments.Single(s => s.IsMatch);

        // Assert
        match.Text.Should().Be("note");
        match.Format.Should().Be(SyntaxFormat.XhtmlAttributeValue, "a match is marked on top of the colors");
        segments.Where(s => !s.IsMatch).Should().OnlyContain(s => s.Text != "note");
    }

    [Fact]
    public void A_stylesheet_and_the_selector_of_a_style_block_are_colored_as_CSS()
    {
        // Arrange
        const string rule = ".note { color: red }";

        // Act
        IReadOnlyList<UsageContextSegment> inCss = UsageContextSegments.Of(Usage("Styles/s.css", ClassUsageKind.Selector, rule, 1, 4));
        IReadOnlyList<UsageContextSegment> inHtml = UsageContextSegments.Of(Usage("Text/a.xhtml", ClassUsageKind.Selector, rule, 1, 4));

        // Assert
        inCss.Should().Contain(s => s.Format == SyntaxFormat.CssProperty);
        inHtml.Select(s => s.Format).Should().Equal(inCss.Select(s => s.Format));
    }

    [Fact]
    public void A_file_of_another_kind_is_plain_text_with_the_match_marked()
    {
        // Act
        IReadOnlyList<UsageContextSegment> segments = UsageContextSegments.Of(
            Usage("Misc/data.txt", ClassUsageKind.Text, "one cat here", 4, 3));

        // Assert
        segments.Should().Equal(
            new UsageContextSegment("one ", null, false),
            new UsageContextSegment("cat", null, true),
            new UsageContextSegment(" here", null, false));
    }

    [Fact]
    public void Without_a_known_match_nothing_is_marked_and_an_empty_context_has_no_pieces()
    {
        // Act & Assert
        UsageContextSegments.Of(Usage("Text/a.xhtml", ClassUsageKind.ClassAttribute, "<p>x</p>", -1, 0))
            .Should().OnlyContain(s => !s.IsMatch);

        // Act & Assert
        UsageContextSegments.Of(Usage("Text/a.xhtml", ClassUsageKind.ClassAttribute, string.Empty, -1, 0)).Should().BeEmpty();
    }

    [Fact]
    public void A_mark_that_reaches_past_the_end_is_cut_to_the_text()
    {
        // Act
        IReadOnlyList<UsageContextSegment> segments = UsageContextSegments.Of(
            Usage("Misc/data.txt", ClassUsageKind.Text, "abc", 1, 50));

        // Assert
        segments.Single(s => s.IsMatch).Text.Should().Be("bc");
    }
}
