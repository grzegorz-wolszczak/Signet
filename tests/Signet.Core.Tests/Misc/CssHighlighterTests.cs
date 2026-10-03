using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Misc;
using Xunit;
using static Signet.Core.Misc.SyntaxFormat;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// Tests of the <c>CSSHighlighter</c> automaton — the expected ranges computed by hand from
/// the transition table.
/// </summary>
public sealed class CssHighlighterTests
{
    private readonly CssHighlighter _sut = new();

    [Fact]
    public void Rule_is_split_into_selector_property_and_value()
    {
        List<SyntaxSpan> spans = new();

        int state = _sut.HighlightLine("p { color: red; }", _sut.InitialState, spans);

        spans.Should().Equal(
            new SyntaxSpan(0, 2, CssSelector),
            new SyntaxSpan(3, 6, CssProperty),
            new SyntaxSpan(10, 4, CssValue),
            new SyntaxSpan(15, 1, CssProperty));
        state.Should().Be(0 + (1 << 16), "the Selector state with a remembered Property, the encoding state + (save << 16)");
    }

    [Fact]
    public void Comment_is_highlighted_including_its_delimiters()
    {
        List<SyntaxSpan> spans = new();

        _sut.HighlightLine("/* x */ a {", _sut.InitialState, spans);

        spans.Should().Equal(
            new SyntaxSpan(0, 6, CssComment),
            new SyntaxSpan(6, 1, CssComment),
            new SyntaxSpan(7, 3, CssSelector));
    }

    [Fact]
    public void Unterminated_comment_continues_on_the_next_line()
    {
        List<SyntaxSpan> spans = new();
        int state = _sut.HighlightLine("/* start", _sut.InitialState, null);

        _sut.HighlightLine("still */ b", state, spans);

        spans.Should().Contain(new SyntaxSpan(0, 7, CssComment));
    }

    [Fact]
    public void Empty_first_line_keeps_the_state_undetermined()
    {
        _sut.HighlightLine(string.Empty, _sut.InitialState, null).Should().Be(_sut.InitialState);
    }
}
