using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="DivUnwrapper"/> — "Unwrap div" in the Code Editor.</summary>
public sealed class DivUnwrapperTests
{
    private const string Nested =
        "<div class=\"a\">\n"
        + "    <div class=\"b\">\n"
        + "        <p>1</p>\n"
        + "        <p>2</p>\n"
        + "    </div>\n"
        + "</div>\n";

    private static int At(string text, string marker, int delta = 0) => text.IndexOf(marker, StringComparison.Ordinal) + delta;

    [Fact]
    public void The_inner_div_is_removed_and_its_content_moves_one_level_up()
    {
        // Act
        FormatEdit edit = DivUnwrapper.Unwrap(Nested, At(Nested, "<p>1"), 0);

        // Assert
        edit.Changed.Should().BeTrue();
        edit.Text.Should().Be("<div class=\"a\">\n    <p>1</p>\n    <p>2</p>\n</div>\n");
    }

    [Fact]
    public void The_outer_div_is_removed_when_the_caret_is_in_its_own_tag()
    {
        // Act
        FormatEdit edit = DivUnwrapper.Unwrap(Nested, 3, 0);

        // Assert
        edit.Text.Should().Be("<div class=\"b\">\n    <p>1</p>\n    <p>2</p>\n</div>\n");
    }

    [Fact]
    public void Tabs_are_shifted_too()
    {
        // Arrange
        string text = "<div>\n\t<div>\n\t\t<p>x</p>\n\t</div>\n</div>";

        // Act & Assert
        DivUnwrapper.Unwrap(text, At(text, "<p>"), 0).Text.Should().Be("<div>\n\t<p>x</p>\n</div>");
    }

    [Fact]
    public void Crlf_line_breaks_are_kept()
    {
        // Arrange
        string text = "<div>\r\n  <div>\r\n    <p>x</p>\r\n  </div>\r\n</div>";

        // Act & Assert
        DivUnwrapper.Unwrap(text, At(text, "<p>"), 0).Text.Should().Be("<div>\r\n  <p>x</p>\r\n</div>");
    }

    [Fact]
    public void Inline_tags_are_removed_without_touching_the_indentation()
    {
        // Arrange
        string text = "<body>\n  <div>text <b>b</b></div>\n</body>";

        // Act & Assert
        DivUnwrapper.Unwrap(text, At(text, "text"), 0).Text.Should().Be("<body>\n  text <b>b</b>\n</body>");
    }

    [Fact]
    public void Only_the_innermost_div_is_removed()
    {
        // Arrange
        string text = "<div><div><div>x</div></div></div>";

        // Act & Assert
        DivUnwrapper.Unwrap(text, At(text, "x"), 0).Text.Should().Be("<div><div>x</div></div>");
    }

    [Fact]
    public void The_whole_range_from_the_opening_to_the_closing_tag_counts()
    {
        // Arrange
        string text = "<div id=\"o\"><div class=\"i\">a <!-- c --> b</div> tail</div>";

        // Act & Assert
        DivUnwrapper.Check(text, At(text, "<!--", 2)).Div!.ClassValue.Should().Be("i");

        // Act & Assert
        DivUnwrapper.Check(text, At(text, "</div> tail", 6)).Div!.ClassValue.Should().Be("i");

        // Act & Assert
        DivUnwrapper.Check(text, At(text, " tail", 3)).Div!.Id.Should().Be("o");
    }

    [Fact]
    public void Outside_any_div_there_is_nothing_to_unwrap()
    {
        // Arrange
        string text = "<p>a</p><div>b</div>";

        // Act & Assert
        DivUnwrapper.Check(text, 1).Problem.Should().Be(DivUnwrapProblem.NotInDiv);

        // Act & Assert
        DivUnwrapper.Unwrap(text, 1, 1).Changed.Should().BeFalse();
    }

    [Fact]
    public void A_div_without_a_closing_tag_is_reported()
    {
        // Arrange
        string text = "<p>a</p><div>b";

        // Act & Assert
        DivUnwrapper.Check(text, At(text, "<div>", 2)).Problem.Should().Be(DivUnwrapProblem.NoClosingTag);
    }

    [Fact]
    public void The_id_is_reported()
    {
        // Arrange
        string text = "<div id=\"x\">a</div>";

        // Act & Assert
        DivUnwrapper.Check(text, 3).Div!.Id.Should().Be("x");
    }

    [Fact]
    public void Preformatted_text_keeps_its_whitespace()
    {
        // Arrange
        string text = "<div>\n  <div>\n    <pre>\n      code\n    </pre>\n    <p>x</p>\n  </div>\n</div>";

        // Act & Assert
        DivUnwrapper.Unwrap(text, At(text, "<p>"), 0).Text
            .Should().Be("<div>\n  <pre>\n      code\n    </pre>\n  <p>x</p>\n</div>");
    }

    [Fact]
    public void The_caret_stays_on_the_same_text()
    {
        // Arrange
        int caret = At(Nested, "<p>2") + 3;

        // Act
        FormatEdit edit = DivUnwrapper.Unwrap(Nested, caret, caret);

        // Assert
        edit.Text.Substring(edit.SelectionStart - 3, 4).Should().Be("<p>2");
    }

    [Theory]
    [InlineData("<div>\n  <div class=\"b\">\n    <!-- keep me -->\n    <p a=\"1\">x &amp; y</p>\n    <![CDATA[ raw ]]>\n    <?pi data?>\n  </div>\n</div>", "<p a")]
    [InlineData("<div>\n\t<div>\n\t\t<pre>\n\t\t\tcode  <b>1</b>\n\t\t</pre>\n\t\t<!-- c -->text\n\n\t</div>\n</div>", "<pre>")]
    [InlineData("<body><div>a <!-- c --> <i>b</i> &nbsp; <div id=\"in\">z</div></div></body>", "a <!--")]
    [InlineData("<div>\r\n  <div>\r\n    <p>one</p>\r\n      <p>two</p>\r\n  </div>\r\n</div>\r\n", "<p>one")]
    public void Nothing_but_the_two_tags_and_one_indentation_level_is_lost(string text, string marker)
    {
        // Arrange
        int offset = At(text, marker);

        // Act
        DivUnwrapCheck check = DivUnwrapper.Check(text, offset);

        // Assert
        check.Problem.Should().Be(DivUnwrapProblem.None);

        // Act
        string result = DivUnwrapper.Unwrap(text, offset, offset).Text;

        // Assert
        // Without whitespace the result is the original minus exactly the opening and the closing tag of the div.
        static string Squash(string t) => System.Text.RegularExpressions.Regex.Replace(t, "\\s+", string.Empty);
        int open = check.Div!.OpenPos;
        int openEnd = text.IndexOf('>', open) + 1;
        int close = LastDivClose(text, open);
        string expected = text.Remove(close, "</div>".Length).Remove(open, openEnd - open);
        Squash(result).Should().Be(Squash(expected));

        // Line breaks inside the content are not touched either.
        result.Count(c => c == '\n').Should().BeGreaterThanOrEqualTo(text.Count(c => c == '\n') - 2);
    }

    // The closing tag matching the div at "open": the first </div> after it that balances the nesting.
    private static int LastDivClose(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (string.CompareOrdinal(text, i, "<div", 0, 4) == 0)
            {
                depth++;
            }
            else if (string.CompareOrdinal(text, i, "</div>", 0, 6) == 0 && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
