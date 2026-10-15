using System;
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
        FormatEdit edit = DivUnwrapper.Unwrap(Nested, At(Nested, "<p>1"), 0);

        edit.Changed.Should().BeTrue();
        edit.Text.Should().Be("<div class=\"a\">\n    <p>1</p>\n    <p>2</p>\n</div>\n");
    }

    [Fact]
    public void The_outer_div_is_removed_when_the_caret_is_in_its_own_tag()
    {
        FormatEdit edit = DivUnwrapper.Unwrap(Nested, 3, 0);

        edit.Text.Should().Be("<div class=\"b\">\n    <p>1</p>\n    <p>2</p>\n</div>\n");
    }

    [Fact]
    public void Tabs_are_shifted_too()
    {
        string text = "<div>\n\t<div>\n\t\t<p>x</p>\n\t</div>\n</div>";

        DivUnwrapper.Unwrap(text, At(text, "<p>"), 0).Text.Should().Be("<div>\n\t<p>x</p>\n</div>");
    }

    [Fact]
    public void Crlf_line_breaks_are_kept()
    {
        string text = "<div>\r\n  <div>\r\n    <p>x</p>\r\n  </div>\r\n</div>";

        DivUnwrapper.Unwrap(text, At(text, "<p>"), 0).Text.Should().Be("<div>\r\n  <p>x</p>\r\n</div>");
    }

    [Fact]
    public void Inline_tags_are_removed_without_touching_the_indentation()
    {
        string text = "<body>\n  <div>text <b>b</b></div>\n</body>";

        DivUnwrapper.Unwrap(text, At(text, "text"), 0).Text.Should().Be("<body>\n  text <b>b</b>\n</body>");
    }

    [Fact]
    public void Only_the_innermost_div_is_removed()
    {
        string text = "<div><div><div>x</div></div></div>";

        DivUnwrapper.Unwrap(text, At(text, "x"), 0).Text.Should().Be("<div><div>x</div></div>");
    }

    [Fact]
    public void The_whole_range_from_the_opening_to_the_closing_tag_counts()
    {
        string text = "<div id=\"o\"><div class=\"i\">a <!-- c --> b</div> tail</div>";

        DivUnwrapper.Check(text, At(text, "<!--", 2)).Div!.ClassValue.Should().Be("i");
        DivUnwrapper.Check(text, At(text, "</div> tail", 6)).Div!.ClassValue.Should().Be("i");
        DivUnwrapper.Check(text, At(text, " tail", 3)).Div!.Id.Should().Be("o");
    }

    [Fact]
    public void Outside_any_div_there_is_nothing_to_unwrap()
    {
        string text = "<p>a</p><div>b</div>";

        DivUnwrapper.Check(text, 1).Problem.Should().Be(DivUnwrapProblem.NotInDiv);
        DivUnwrapper.Unwrap(text, 1, 1).Changed.Should().BeFalse();
    }

    [Fact]
    public void A_div_without_a_closing_tag_is_reported()
    {
        string text = "<p>a</p><div>b";

        DivUnwrapper.Check(text, At(text, "<div>", 2)).Problem.Should().Be(DivUnwrapProblem.NoClosingTag);
    }

    [Fact]
    public void The_id_is_reported()
    {
        string text = "<div id=\"x\">a</div>";

        DivUnwrapper.Check(text, 3).Div!.Id.Should().Be("x");
    }

    [Fact]
    public void Preformatted_text_keeps_its_whitespace()
    {
        string text = "<div>\n  <div>\n    <pre>\n      code\n    </pre>\n    <p>x</p>\n  </div>\n</div>";

        DivUnwrapper.Unwrap(text, At(text, "<p>"), 0).Text
            .Should().Be("<div>\n  <pre>\n      code\n    </pre>\n  <p>x</p>\n</div>");
    }

    [Fact]
    public void The_caret_stays_on_the_same_text()
    {
        int caret = At(Nested, "<p>2") + 3;
        FormatEdit edit = DivUnwrapper.Unwrap(Nested, caret, caret);

        edit.Text.Substring(edit.SelectionStart - 3, 4).Should().Be("<p>2");
    }
}
