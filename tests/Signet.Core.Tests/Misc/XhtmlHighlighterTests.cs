using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Misc;
using Xunit;
using static Signet.Core.Misc.SyntaxFormat;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// Tests of the XHTML highlighter block highlighting — the expected ranges computed
/// by hand from the state machine (including its quirks).
/// </summary>
public sealed class XhtmlHighlighterTests
{
    private readonly XhtmlHighlighter _sut = new();

    private (List<SyntaxSpan> Spans, int State) Run(string line, int? state = null)
    {
        List<SyntaxSpan> spans = new();
        int end = _sut.HighlightLine(line, state ?? _sut.InitialState, spans);
        return (spans, end);
    }

    [Fact]
    public void Tag_attributes_and_entity_get_their_formats()
    {
        (List<SyntaxSpan> spans, int state) = Run("<p class=\"x\">A&amp;B</p>");

        spans.Should().Equal(
            new SyntaxSpan(0, 2, XhtmlTagName),          // "<p"
            new SyntaxSpan(3, 6, XhtmlAttributeName),    // "class="
            new SyntaxSpan(9, 1, XhtmlAttributeValue),   // the opening quote
            new SyntaxSpan(10, 2, XhtmlAttributeValue),  // x"
            new SyntaxSpan(12, 1, XhtmlTagName),         // ">"
            new SyntaxSpan(14, 4, XhtmlEntity),          // "&amp" (without ';')
            new SyntaxSpan(20, 4, XhtmlTagName));        // "</p>"
        state.Should().Be(_sut.InitialState);
    }

    [Fact]
    public void Doctype_line_is_one_span()
    {
        (List<SyntaxSpan> spans, _) = Run("<!DOCTYPE html>");

        spans.Should().Equal(new SyntaxSpan(0, 15, XhtmlDoctype));
    }

    [Fact]
    public void Comment_state_is_carried_to_the_next_line()
    {
        (List<SyntaxSpan> first, int state) = Run("<!-- a");
        (List<SyntaxSpan> second, int end) = Run("b -->x", state);

        first.Should().Equal(new SyntaxSpan(0, 6, XhtmlComment));
        second.Should().Equal(new SyntaxSpan(0, 5, XhtmlComment));
        end.Should().Be(_sut.InitialState);
    }

    [Fact]
    public void Empty_line_resets_the_state_like_the_original()
    {
        (_, int state) = Run("<!-- a");

        _sut.HighlightLine(string.Empty, state, null).Should().Be(_sut.InitialState);
    }

    [Fact]
    public void Inline_style_block_uses_css_formats()
    {
        (List<SyntaxSpan> spans, int state) = Run("<style>p{}</style>");

        spans.Should().Equal(
            new SyntaxSpan(0, 7, XhtmlTagName),
            new SyntaxSpan(7, 3, XhtmlCss),
            new SyntaxSpan(10, 8, XhtmlTagName));
        state.Should().Be(_sut.InitialState);
    }

    [Fact]
    public void Multi_line_style_block_highlights_css_and_css_comments()
    {
        (_, int state) = Run("<style>");
        (List<SyntaxSpan> spans, int end) = Run("p { color: red } /* c */", state);

        spans.Should().Equal(
            new SyntaxSpan(0, 19, XhtmlCss),
            new SyntaxSpan(19, 5, XhtmlCssComment));
        end.Should().Be(state, "after the comment we return to the CSS state");
    }

    [Fact]
    public void Special_space_is_marked()
    {
        (List<SyntaxSpan> spans, _) = Run("a b");

        spans.Should().Equal(new SyntaxSpan(1, 1, XhtmlSpecialSpace));
    }
}
