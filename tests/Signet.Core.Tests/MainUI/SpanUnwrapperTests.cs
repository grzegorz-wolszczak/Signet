using System;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="SpanUnwrapper"/> — "Remove span" in the Code View context menu.</summary>
public sealed class SpanUnwrapperTests
{
    private const string Text =
        "<body><p>A <span class=\"b i\">one</span> B <span class=\"i b\">two</span> C <span class=\"b i\" style=\"color:red\">three</span>"
        + " D <span class=\"b i\" id=\"x\">four</span> E <span class=\"b\">five</span></p></body>";

    private static int At(string text, string marker, int delta = 0) => text.IndexOf(marker, StringComparison.Ordinal) + delta;

    [Theory]
    [InlineData(0)] // on "<"
    [InlineData(5)] // inside the opening tag
    [InlineData(18)] // right after ">"
    public void The_span_is_found_anywhere_in_its_opening_tag(int delta)
    {
        SpanAtCaret? span = SpanUnwrapper.Find(Text, At(Text, "<span class=\"b i\">one", delta));

        span.Should().NotBeNull();
        span!.ClassValue.Should().Be("b i");
        span.HasId.Should().BeFalse();
    }

    [Fact]
    public void The_span_is_found_from_its_closing_tag_too()
    {
        SpanUnwrapper.Find(Text, At(Text, "</span> B", 3))!.OpenPos.Should().Be(At(Text, "<span class=\"b i\">one"));
    }

    [Fact]
    public void Outside_a_span_tag_there_is_nothing()
    {
        SpanUnwrapper.Find(Text, At(Text, "one", 1)).Should().BeNull();
        SpanUnwrapper.Find(Text, At(Text, "<p>", 1)).Should().BeNull();
    }

    [Fact]
    public void Same_spans_have_the_same_classes_in_any_order_and_no_other_differences()
    {
        // "b i" and "i b" — yes; with style, with id, or only "b" — no.
        SpanUnwrapper.Find(Text, At(Text, "<span class=\"b i\">one", 1))!.SameCount.Should().Be(2);
    }

    [Fact]
    public void A_span_with_an_id_is_reported_and_not_removed()
    {
        int offset = At(Text, "<span class=\"b i\" id", 1);

        SpanUnwrapper.Find(Text, offset)!.HasId.Should().BeTrue();
        SpanUnwrapper.Unwrap(Text, offset, all: false, caret: offset).Changed.Should().BeFalse();
    }

    [Fact]
    public void Removing_a_single_span_keeps_its_content()
    {
        int offset = At(Text, "<span class=\"i b\">two", 1);

        FormatEdit edit = SpanUnwrapper.Unwrap(Text, offset, all: false, caret: offset);

        edit.Changed.Should().BeTrue();
        edit.Text.Should().Contain(" B two C ").And.Contain("<span class=\"b i\">one</span>");
    }

    [Fact]
    public void Removing_all_same_spans_leaves_the_different_ones()
    {
        int offset = At(Text, "<span class=\"b i\">one", 1);

        FormatEdit edit = SpanUnwrapper.Unwrap(Text, offset, all: true, caret: At(Text, "five"));

        edit.Text.Should().Be(
            "<body><p>A one B two C <span class=\"b i\" style=\"color:red\">three</span>"
            + " D <span class=\"b i\" id=\"x\">four</span> E <span class=\"b\">five</span></p></body>");
        edit.Text.Substring(edit.SelectionStart, 4).Should().Be("five");
    }

    [Fact]
    public void A_span_without_a_class_has_no_class_value()
    {
        const string text = "<p><span>x</span> <span>y</span> <span lang=\"en\">z</span></p>";

        SpanAtCaret span = SpanUnwrapper.Find(text, 4)!;

        span.ClassValue.Should().BeNull();
        span.SameCount.Should().Be(2);
    }
}
