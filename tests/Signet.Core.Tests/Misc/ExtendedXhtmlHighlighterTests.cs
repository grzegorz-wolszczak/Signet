using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Misc;
using Xunit;
using static Signet.Core.Misc.SyntaxFormat;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// Tests of the extended (X)HTML highlighting — the
/// automaton.
/// </summary>
public sealed class ExtendedXhtmlHighlighterTests
{
    private static SyntaxFormat? FormatAt(List<SyntaxSpan> spans, string text, string fragment, int offset = 0) =>
        ExtendedCssHighlighterTests.FormatAt(spans, text.IndexOf(fragment, StringComparison.Ordinal) + offset);

    // Highlights successive lines; returns the fragments of the last line.
    private static List<SyntaxSpan> Highlight(ExtendedXhtmlHighlighter sut, params string[] lines)
    {
        ExtendedXhtmlState state = sut.InitialState;
        List<SyntaxSpan> spans = new();
        foreach (string line in lines)
        {
            spans = new List<SyntaxSpan>();
            state = sut.HighlightLine(line, state, spans);
        }

        return spans;
    }

    private static List<SyntaxSpan> Highlight(params string[] lines) => Highlight(new ExtendedXhtmlHighlighter(), lines);

    [Fact]
    public void Tag_attribute_and_value_use_the_basic_formats()
    {
        const string text = "<p class=\"x\">Hi</p>";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text, "<p").Should().Be(XhtmlTagName);
        FormatAt(spans, text, "p ").Should().Be(XhtmlTagName);
        FormatAt(spans, text, "class").Should().Be(XhtmlAttributeName);
        FormatAt(spans, text, "=").Should().Be(XhtmlAttributeName);
        FormatAt(spans, text, "\"x\"", 1).Should().Be(XhtmlAttributeValue);
        FormatAt(spans, text, ">Hi").Should().Be(XhtmlTagName);
        FormatAt(spans, text, "Hi").Should().BeNull();
        FormatAt(spans, text, "</p", 2).Should().Be(XhtmlTagName);
    }

    [Fact]
    public void Namespace_prefixes_have_their_own_format()
    {
        const string text = "<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\" epub:type=\"x\"/>";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text, "svg:svg").Should().Be(XhtmlNamespacePrefix);
        FormatAt(spans, text, "svg:svg", 4).Should().Be(XhtmlTagName);
        FormatAt(spans, text, "xmlns:").Should().Be(XhtmlNamespacePrefix);
        FormatAt(spans, text, "svg=").Should().Be(XhtmlAttributeName);
        FormatAt(spans, text, "epub:").Should().Be(XhtmlNamespacePrefix);
    }

    [Fact]
    public void Text_inside_bold_and_italic_elements_is_styled()
    {
        const string text = "<p><b>bold <i>both</i></b> plain <em>it</em></p>";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text, "bold").Should().Be(XhtmlBoldText);
        FormatAt(spans, text, "both").Should().Be(XhtmlBoldItalicText);
        FormatAt(spans, text, "plain").Should().BeNull();
        FormatAt(spans, text, "it<").Should().Be(XhtmlItalicText);
    }

    [Fact]
    public void Heading_style_continues_on_the_next_line_until_the_closing_tag()
    {
        List<SyntaxSpan> spans = Highlight("<h1>Title", "continues</h1> after");

        FormatAt(spans, "continues</h1> after", "continues").Should().Be(XhtmlBoldText);
        FormatAt(spans, "continues</h1> after", "after").Should().BeNull();
    }

    [Fact]
    public void Title_content_is_bold()
    {
        const string text = "<title>Book</title>";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text, "Book").Should().Be(XhtmlBoldText);
        FormatAt(spans, text, "</title", 3).Should().Be(XhtmlTagName);
    }

    [Fact]
    public void Style_content_is_highlighted_as_css()
    {
        const string text = "<style>p { color: red }</style><p>x</p>";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text, "p {").Should().Be(CssSelector);
        FormatAt(spans, text, "color").Should().Be(CssProperty);
        FormatAt(spans, text, "red").Should().Be(CssConstant);
        FormatAt(spans, text, "</style", 3).Should().Be(XhtmlTagName);
        FormatAt(spans, text, "<p>", 1).Should().Be(XhtmlTagName);
    }

    [Fact]
    public void Css_state_is_carried_across_lines_inside_style()
    {
        List<SyntaxSpan> spans = Highlight("<style type=\"text/css\">", ".a {", "  margin: 0 /* x", "   y */ }", "</style>");

        FormatAt(spans, "</style>", "style").Should().Be(XhtmlTagName);

        List<SyntaxSpan> comment = Highlight("<style>", ".a {", "  margin: 0 /* x", "   y */ }");
        FormatAt(comment, "   y */ }", "y").Should().Be(CssComment);

        List<SyntaxSpan> selector = Highlight("<style>", "", ".a {");
        FormatAt(selector, ".a {", ".a").Should().Be(CssSpecialSelector);
    }

    [Theory]
    [InlineData("a < b", "<", SyntaxIssue.UnescapedLessThan)]
    [InlineData("a & b", "&", SyntaxIssue.UnescapedAmpersand)]
    [InlineData("a > b", ">", SyntaxIssue.UnescapedGreaterThan)]
    [InlineData("<p>x</p y>", "y", SyntaxIssue.BadClosingTag)]
    [InlineData("<svg:>", "<svg:", SyntaxIssue.PrefixOnlyTagName)]
    [InlineData("<a href=>x</a>", "=", SyntaxIssue.MissingAttributeValue)]
    [InlineData("<br / x>", "/", SyntaxIssue.MisplacedSlash)]
    public void Syntax_errors_are_marked_with_their_kind(string text, string fragment, SyntaxIssue issue)
    {
        List<SyntaxSpan> spans = Highlight(text);

        int index = text.IndexOf(fragment, StringComparison.Ordinal);
        spans.Should().Contain(s => s.Format == SyntaxError && s.Issue == issue && s.Start == index);
    }

    [Theory]
    [InlineData("<p>&amp; &#160; &#x2014;</p>")]
    [InlineData("<input disabled><input disabled=\"disabled\" />")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<![CDATA[ a < b ]]>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?>")]
    public void Valid_markup_has_no_errors(string text)
    {
        Highlight(text).Should().NotContain(s => s.Format == SyntaxError);
    }

    [Fact]
    public void Missing_attribute_value_does_not_swallow_the_end_of_the_tag()
    {
        List<SyntaxSpan> spans = Highlight("<a href=>x</a>");

        FormatAt(spans, "<a href=>x</a>", ">x").Should().Be(XhtmlTagName);
        FormatAt(spans, "<a href=>x</a>", "x<").Should().BeNull();
    }

    [Theory]
    [InlineData(true, Link, SyntaxIssue.Link)]
    [InlineData(false, BadLink, SyntaxIssue.BrokenLink)]
    public void Href_value_is_a_link_checked_against_the_book(bool exists, SyntaxFormat format, SyntaxIssue issue)
    {
        const string text = "<a href=\"ch2.xhtml#x\">next</a>";
        List<string> asked = new();
        var sut = new ExtendedXhtmlHighlighter(linkExists: reference =>
        {
            asked.Add(reference);
            return exists;
        });

        List<SyntaxSpan> spans = Highlight(sut, text);

        asked.Should().Equal("ch2.xhtml#x");
        spans.Should().Contain(new SyntaxSpan(9, "ch2.xhtml#x".Length, format, issue));
        FormatAt(spans, text, "\">").Should().Be(XhtmlAttributeValue);
    }

    [Fact]
    public void Data_uri_and_other_attributes_are_not_links()
    {
        List<SyntaxSpan> spans = Highlight(
            new ExtendedXhtmlHighlighter(linkExists: _ => false),
            "<img src=\"data:image/png;base64,AA\" alt=\"a.png\"/>");

        spans.Should().NotContain(s => s.Format == Link || s.Format == BadLink);
    }

    [Fact]
    public void Xml_mode_checks_links_but_does_not_style_text_or_treat_style_as_css()
    {
        var sut = new ExtendedXhtmlHighlighter(isXml: true, linkExists: _ => false);

        List<SyntaxSpan> item = Highlight(sut, "<item id=\"a\" href=\"Text/a.xhtml\" media-type=\"application/xhtml+xml\"/>");
        List<SyntaxSpan> bold = Highlight(sut, "<b>x</b>");
        List<SyntaxSpan> style = Highlight(sut, "<style>.a { }</style>");

        item.Should().Contain(s => s.Format == BadLink);
        FormatAt(bold, "<b>x</b>", "x").Should().BeNull();
        FormatAt(style, "<style>.a { }</style>", ".a").Should().BeNull();
    }

    [Fact]
    public void Unclosed_element_changes_the_end_state_and_identical_lines_give_equal_states()
    {
        var sut = new ExtendedXhtmlHighlighter();

        ExtendedXhtmlState plain = sut.HighlightLine("<p>text</p>", sut.InitialState, null);
        ExtendedXhtmlState open = sut.HighlightLine("<p><b>text", sut.InitialState, null);
        ExtendedXhtmlState again = sut.HighlightLine("<p><b>text", sut.InitialState, null);
        ExtendedXhtmlState closedByParent = sut.HighlightLine("<p><b>text</p>", sut.InitialState, null);

        plain.Should().Be(sut.InitialState);
        open.Should().NotBe(plain);
        open.Should().Be(again, "the state has value equality, so subsequent lines are not recomputed needlessly");
        closedByParent.Should().Be(plain, "closing the parent also pops unclosed children from the stack (close_tag)");
    }

    [Fact]
    public void Comment_continues_across_lines()
    {
        List<SyntaxSpan> spans = Highlight("<!-- start", "middle", "end --> <p>");

        FormatAt(spans, "end --> <p>", "end").Should().Be(XhtmlComment);
        FormatAt(spans, "end --> <p>", "<p").Should().Be(XhtmlTagName);
    }

    [Fact]
    public void Special_spaces_are_marked()
    {
        const string text = "<p>a b</p>";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text, " ").Should().Be(XhtmlSpecialSpace);
    }
}
