using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Misc;
using Xunit;
using static Signet.Core.Misc.SyntaxFormat;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// Tests of the extended CSS highlighting — refinements of
/// parts of the automaton.
/// </summary>
public sealed class ExtendedCssHighlighterTests
{
    // The format in effect for a character (the last matching fragment wins).
    internal static SyntaxFormat? FormatAt(List<SyntaxSpan> spans, int index) =>
        spans.LastOrDefault(s => s.Format != SyntaxError && index >= s.Start && index < s.Start + s.Length) is { Length: > 0 } span
            ? span.Format
            : null;

    private static List<SyntaxSpan> Highlight(string text, System.Func<string, bool>? linkExists = null)
    {
        var sut = new ExtendedCssHighlighter(linkExists);
        List<SyntaxSpan> spans = new();
        sut.HighlightLine(text, sut.InitialState, spans);
        return spans;
    }

    [Fact]
    public void Class_id_and_pseudo_selectors_are_distinguished_from_the_tag_selector()
    {
        const string text = ".intro a:hover, #main {";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text.IndexOf(".intro", System.StringComparison.Ordinal)).Should().Be(CssSpecialSelector);
        FormatAt(spans, text.IndexOf("a:", System.StringComparison.Ordinal)).Should().Be(CssSelector);
        FormatAt(spans, text.IndexOf(":hover", System.StringComparison.Ordinal)).Should().Be(CssSpecialSelector);
        FormatAt(spans, text.IndexOf("#main", System.StringComparison.Ordinal) + 2).Should().Be(CssSpecialSelector);
    }

    [Fact]
    public void At_rule_is_highlighted()
    {
        List<SyntaxSpan> spans = Highlight("@media screen {");

        FormatAt(spans, 0).Should().Be(CssAtRule);
        FormatAt(spans, 5).Should().Be(CssAtRule);
        FormatAt(spans, 7).Should().Be(CssSelector);
    }

    [Fact]
    public void Numbers_colours_and_important_are_highlighted_inside_values_only()
    {
        const string text = "p { margin: -1.5em 10px; color: #fff; background: red !important; }";

        List<SyntaxSpan> spans = Highlight(text);

        FormatAt(spans, text.IndexOf("-1.5em", System.StringComparison.Ordinal)).Should().Be(CssConstant);
        FormatAt(spans, text.IndexOf("10px", System.StringComparison.Ordinal) + 3).Should().Be(CssConstant);
        FormatAt(spans, text.IndexOf("#fff", System.StringComparison.Ordinal)).Should().Be(CssConstant);
        FormatAt(spans, text.IndexOf("red", System.StringComparison.Ordinal)).Should().Be(CssConstant);
        FormatAt(spans, text.IndexOf("!important", System.StringComparison.Ordinal) + 3).Should().Be(CssAtRule);
        FormatAt(spans, text.IndexOf("color", System.StringComparison.Ordinal)).Should().Be(CssProperty);
    }

    [Fact]
    public void Nothing_is_refined_inside_a_comment()
    {
        List<SyntaxSpan> spans = Highlight("/* .x #y 12px red */");

        spans.Should().OnlyContain(s => s.Format == CssComment);
    }

    [Theory]
    [InlineData(true, Link, SyntaxIssue.Link)]
    [InlineData(false, BadLink, SyntaxIssue.BrokenLink)]
    public void Url_target_is_a_link_checked_against_the_book(bool exists, SyntaxFormat format, SyntaxIssue issue)
    {
        const string text = "p { background: url(\"../Images/a.png\"); }";
        List<string> asked = new();

        List<SyntaxSpan> spans = Highlight(text, reference =>
        {
            asked.Add(reference);
            return exists;
        });

        asked.Should().Equal("../Images/a.png");
        spans.Should().Contain(new SyntaxSpan(text.IndexOf("../", System.StringComparison.Ordinal), "../Images/a.png".Length, format, issue));
    }

    [Fact]
    public void Data_url_is_not_a_link()
    {
        List<SyntaxSpan> spans = Highlight("p { background: url(data:image/png;base64,AAAA); }", _ => false);

        spans.Should().NotContain(s => s.Format == Link || s.Format == BadLink);
    }

    [Fact]
    public void Unterminated_string_is_marked_as_an_error()
    {
        const string text = "p { font-family: \"Foo";

        List<SyntaxSpan> spans = Highlight(text);

        spans.Should().Contain(s => s.Format == SyntaxError && s.Issue == SyntaxIssue.UnterminatedString &&
                                    s.Start == text.IndexOf('"', System.StringComparison.Ordinal));
    }

    [Fact]
    public void Line_states_are_the_same_as_in_the_basic_automaton()
    {
        string[] lines = { "body {", "  color: red; /* x", " y */ margin: 0", "}", "a:hover { x: \"q", "\" }" };
        var basic = new CssHighlighter();
        var sut = new ExtendedCssHighlighter();
        int expected = basic.InitialState;
        int actual = sut.InitialState;

        foreach (string line in lines)
        {
            expected = basic.HighlightLine(line, expected, new List<SyntaxSpan>());
            actual = sut.HighlightLine(line, actual, new List<SyntaxSpan>());
            actual.Should().Be(expected, line);
        }
    }
}
