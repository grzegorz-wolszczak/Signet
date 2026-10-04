using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="NestedDivCollapser"/> — finding chains of directly nested, identical <c>&lt;div&gt;</c>s and
/// collapsing them in the source text.
/// </summary>
public sealed class NestedDivCollapserTests
{
    private static string Page(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><title>t</title></head>\n<body>\n" + body + "\n</body>\n</html>\n";

    private static string CollapseAll(string text)
    {
        foreach (NestedDivChain chain in NestedDivCollapser.FindChains(text).OrderByDescending(c => c.OuterPos))
        {
            text = NestedDivCollapser.Collapse(text, chain.OuterPos)!;
        }

        return text;
    }

    [Fact]
    public void A_chain_of_three_collapses_into_one_div_and_the_content_moves_left()
    {
        string text = Page("<div class=\"a\">\n    <div class=\"a\">\n        <div class=\"a\">\n             some content\n        </div>\n    </div>\n</div>");

        NestedDivChain chain = NestedDivCollapser.FindChains(text).Should().ContainSingle().Subject;
        chain.Depth.Should().Be(3);
        chain.Separators.Should().BeEmpty();
        CollapseAll(text).Should().Be(Page("<div class=\"a\">\n     some content\n</div>"));
    }

    [Fact]
    public void Divs_on_one_line_collapse_without_reindenting()
    {
        string text = Page("<div class=\"a\"><div class=\"a\"> <div class=\"a\"><p>x</p></div></div>\n</div>");

        CollapseAll(text).Should().Be(Page("<div class=\"a\"><p>x</p>\n</div>"));
    }

    [Theory]
    [InlineData("<div class=\"a b\" id=\"x\"><div id=\"x\" class=\"b  a\"><p>x</p></div></div>")]
    [InlineData("<div><div><p>x</p></div></div>")]
    public void Identical_attributes_are_compared_as_a_set_with_classes_in_any_order(string body)
    {
        NestedDivCollapser.FindChains(Page(body)).Should().ContainSingle().Which.Depth.Should().Be(2);
    }

    [Theory]
    [InlineData("<div class=\"a\"><div class=\"b\"><p>x</p></div></div>")]
    [InlineData("<div class=\"a\"><div class=\"a\" id=\"y\"><p>x</p></div></div>")]
    [InlineData("<div class=\"a\">text<div class=\"a\"><p>x</p></div></div>")]
    [InlineData("<div class=\"a\"><div class=\"a\"><p>x</p></div>text</div>")]
    [InlineData("<div class=\"a\"><p>y</p><div class=\"a\"><p>x</p></div></div>")]
    [InlineData("<div class=\"a\"><div class=\"a\"><p>x</p></div><p>y</p></div>")]
    [InlineData("<section class=\"a\"><section class=\"a\"><p>x</p></section></section>")]
    public void Anything_but_whitespace_comments_and_cdata_between_the_tags_breaks_the_chain(string body)
    {
        NestedDivCollapser.FindChains(Page(body)).Should().BeEmpty();
    }

    [Fact]
    public void Comments_and_cdata_between_the_tags_are_reported_and_kept_inside_the_remaining_div()
    {
        string text = Page(
            "<div class=\"a\">\n    <div class=\"a\">\n    <![CDATA[ x < y ]]>\n        <div class=\"a\">\n            some content\n        </div>\n    </div>\n    <!-- some comment -->\n</div>");

        NestedDivChain chain = NestedDivCollapser.FindChains(text).Should().ContainSingle().Subject;
        chain.Depth.Should().Be(3);
        chain.Separators.Select(s => (s.Text, s.IsCData)).Should().Equal(("<![CDATA[ x < y ]]>", true), ("<!-- some comment -->", false));

        CollapseAll(text).Should().Be(Page("<div class=\"a\">\n<![CDATA[ x < y ]]>\n    some content\n<!-- some comment -->\n</div>"));
    }

    [Fact]
    public void The_content_is_not_reindented_when_it_contains_pre()
    {
        string text = Page("<div>\n  <div>\n    <pre>\n    code\n    </pre>\n  </div>\n</div>");

        CollapseAll(text).Should().Be(Page("<div>\n    <pre>\n    code\n    </pre>\n</div>"));
    }

    [Fact]
    public void The_content_is_not_reindented_when_a_line_is_indented_less_than_the_removed_levels()
    {
        string text = Page("<div>\n    <div>\n        <p>a\nb</p>\n    </div>\n</div>");

        CollapseAll(text).Should().Be(Page("<div>\n        <p>a\nb</p>\n</div>"));
    }

    [Fact]
    public void Nested_chains_inside_a_chain_are_separate_and_both_collapse()
    {
        string text = Page("<div class=\"a\">\n  <div class=\"a\">\n    <p>x</p>\n    <div class=\"b\">\n      <div class=\"b\">\n        <p>y</p>\n      </div>\n    </div>\n  </div>\n</div>");

        NestedDivCollapser.FindChains(text).Select(c => c.Depth).Should().Equal(2, 2);
        CollapseAll(text).Should().Be(Page("<div class=\"a\">\n  <p>x</p>\n  <div class=\"b\">\n    <p>y</p>\n  </div>\n</div>"));
    }

    [Fact]
    public void Collapse_returns_null_where_there_is_no_chain()
    {
        string text = Page("<div class=\"a\"><p>x</p></div>");

        NestedDivCollapser.Collapse(text, text.IndexOf("<div", System.StringComparison.Ordinal)).Should().BeNull();
    }
}
