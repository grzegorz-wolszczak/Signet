using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="AdjacentDivMerger"/> — finding runs of consecutive sibling <c>&lt;div&gt;</c>s with identical
/// attributes (with their nesting level) and merging them in the source text.
/// </summary>
public sealed class AdjacentDivMergerTests
{
    private static string Page(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><title>t</title></head>\n<body>\n" + body + "\n</body>\n</html>\n";

    private static string MergeAll(string text)
    {
        foreach (AdjacentDivGroup group in AdjacentDivMerger.FindGroups(text).OrderByDescending(g => g.FirstPos))
        {
            text = AdjacentDivMerger.Merge(text, group.FirstPos)!;
        }

        return text;
    }

    [Fact]
    public void Two_divs_merge_into_the_first_one_and_the_contents_keep_their_lines()
    {
        string text = Page("<div class=\"a\">\n  <p>x</p>\n</div>\n<div class=\"a\">\n  <p>y</p>\n</div>");

        AdjacentDivGroup group = AdjacentDivMerger.FindGroups(text).Should().ContainSingle().Subject;
        (group.Count, group.Level, group.OpenTag).Should().Be((2, 1, "<div class=\"a\">"));
        group.Separators.Should().BeEmpty();
        MergeAll(text).Should().Be(Page("<div class=\"a\">\n  <p>x</p>\n  <p>y</p>\n</div>"));
    }

    [Fact]
    public void A_run_of_three_merges_into_one_div()
    {
        string text = Page("<div><p>x</p></div>\n<div><p>y</p></div>\n<div><p>z</p></div>");

        AdjacentDivMerger.FindGroups(text).Should().ContainSingle().Which.Count.Should().Be(3);
        MergeAll(text).Should().Be(Page("<div><p>x</p>\n<p>y</p>\n<p>z</p></div>"));
    }

    [Theory]
    [InlineData("<div class=\"a\"><p>x</p></div><div class=\"a\"><p>y</p></div>", 1)]
    [InlineData("<section><div class=\"a\"><p>x</p></div><div class=\"a\"><p>y</p></div></section>", 2)]
    [InlineData("<section><div class=\"o\"><div class=\"a\"><p>x</p></div><div class=\"a\"><p>y</p></div></div></section>", 3)]
    public void The_level_counts_every_element_between_the_run_and_body(string body, int level)
    {
        AdjacentDivMerger.FindGroups(Page(body)).Should().ContainSingle().Which.Level.Should().Be(level);
    }

    [Fact]
    public void Runs_on_different_levels_are_found_in_one_pass()
    {
        string text = Page(
            "<div class=\"o\"><div class=\"i\"><p>a</p></div><div class=\"i\"><p>b</p></div></div>"
            + "<div class=\"o\"><p>c</p></div>");

        AdjacentDivMerger.FindGroups(text).Select(g => (g.Level, g.Count)).Should().Equal((1, 2), (2, 2));
        MergeAll(text).Should().Be(Page("<div class=\"o\"><div class=\"i\"><p>a</p><p>b</p></div><p>c</p></div>"));
    }

    [Theory]
    [InlineData("<div class=\"a b\"><p>x</p></div><div class=\"b  a\"><p>y</p></div>")]
    [InlineData("<div><p>x</p></div>\n  <div><p>y</p></div>")]
    public void Identical_attributes_are_compared_as_a_set_with_classes_in_any_order(string body)
    {
        AdjacentDivMerger.FindGroups(Page(body)).Should().ContainSingle().Which.Count.Should().Be(2);
    }

    [Theory]
    [InlineData("<div class=\"a\"><p>x</p></div><div class=\"b\"><p>y</p></div>")]
    [InlineData("<div class=\"a\"><p>x</p></div><div class=\"a\" id=\"y\"><p>y</p></div>")]
    [InlineData("<div class=\"a\"><p>x</p></div>text<div class=\"a\"><p>y</p></div>")]
    [InlineData("<div class=\"a\"><p>x</p></div><p>z</p><div class=\"a\"><p>y</p></div>")]
    [InlineData("<div class=\"a\"><div class=\"a\"><p>x</p></div></div>")]
    [InlineData("<section class=\"a\"><p>x</p></section><section class=\"a\"><p>y</p></section>")]
    public void Anything_but_whitespace_comments_and_cdata_between_identical_divs_prevents_merging(string body)
    {
        AdjacentDivMerger.FindGroups(Page(body)).Should().BeEmpty();
    }

    [Fact]
    public void Comments_between_the_divs_are_reported_and_kept_inside_the_merged_div()
    {
        string text = Page("<div class=\"a\">\n  <p>x</p>\n</div>\n<!-- c -->\n<div class=\"a\">\n  <p>y</p>\n</div>");

        AdjacentDivGroup group = AdjacentDivMerger.FindGroups(text).Should().ContainSingle().Subject;
        group.Separators.Select(s => (s.Text, s.IsCData)).Should().Equal(("<!-- c -->", false));
        MergeAll(text).Should().Be(Page("<div class=\"a\">\n  <p>x</p>\n<!-- c -->\n  <p>y</p>\n</div>"));
    }

    [Theory]
    [InlineData("<div>one</div> <div>two</div>", "<div>one two</div>")]
    [InlineData("<div>one </div><div>two</div>", "<div>one two</div>")]
    [InlineData("<div>one</div><div>two</div>", "<div>onetwo</div>")]
    public void The_whitespace_between_the_divs_is_kept_when_the_contents_have_none_at_the_joint(string body, string expected)
    {
        MergeAll(Page(body)).Should().Be(Page(expected));
    }

    [Fact]
    public void Merge_returns_null_when_there_is_no_run_at_the_position()
    {
        string text = Page("<div><p>x</p></div><p>y</p>");

        AdjacentDivMerger.Merge(text, text.IndexOf("<div", System.StringComparison.Ordinal)).Should().BeNull();
    }
}
