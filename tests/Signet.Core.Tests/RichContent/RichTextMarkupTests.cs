using AwesomeAssertions;
using Signet.Core.RichContent;
using Xunit;

namespace Signet.Core.Tests.RichContent;

/// <summary>The light markup of formatted text (<see cref="RichTextMarkup"/>) and what it builds.</summary>
public sealed class RichTextMarkupTests
{
    [Fact]
    public void Marks_give_bold_italic_and_code_fragments()
    {
        // Act
        RichText text = RichTextMarkup.Parse("Sets the direction. **Keywords:** `ltr`, *or* `rtl`");

        // Assert
        text.Should().Be(new RichTextBuilder()
            .Text("Sets the direction. ").Bold("Keywords:").Text(" ").Code("ltr").Text(", ").Italic("or").Text(" ").Code("rtl")
            .Build());
    }

    [Fact]
    public void A_line_break_stays_in_the_paragraph_and_a_blank_line_starts_a_new_one()
    {
        // Act
        RichText text = RichTextMarkup.Parse("first\nsecond\n\nthird");

        // Assert
        text.Should().Be(new RichTextBuilder().Text("first").LineBreak().Text("second").Paragraph().Text("third").Build());
        text.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void A_dashes_line_is_a_separator_and_a_fence_a_highlighted_code_block()
    {
        // Act
        RichText text = RichTextMarkup.Parse("Before\n---\n```css\np { color: red; }\n/* *not* a mark */\n```\nAfter");

        // Assert
        text.Blocks.Should().HaveCount(4);
        text.Blocks[1].Should().BeOfType<RichSeparator>();
        text.Blocks[2].Should().Be(new RichCodeBlock("p { color: red; }\n/* *not* a mark */", "css"));
        text.ToPlainText().Should().Be("Before\n\n---\n\np { color: red; }\n/* *not* a mark */\n\nAfter");
    }

    [Theory]
    [InlineData("a \\*literal\\* star", "a *literal* star")]
    [InlineData("an *unclosed mark", "an *unclosed mark")]
    [InlineData("a back\\\\slash", "a back\\slash")]
    public void Escapes_and_unclosed_marks_are_taken_literally(string markup, string plain)
    {
        // Act
        RichText text = RichTextMarkup.Parse(markup);

        // Assert
        text.Should().Be(RichText.Plain(plain));
    }

    [Theory]
    [InlineData("2 * 3 = `six`")]
    [InlineData("back\\slash and *stars*")]
    public void An_escaped_text_reads_back_as_the_same_plain_text(string value)
    {
        // Act
        RichText text = RichTextMarkup.Parse(RichTextMarkup.Escape(value));

        // Assert
        text.Should().Be(RichText.Plain(value));
    }

    [Fact]
    public void A_value_escaped_for_code_keeps_its_backticks_and_backslashes()
    {
        // Act
        RichText text = RichTextMarkup.Parse("`" + RichTextMarkup.EscapeCode("a`b\\c*d") + "`");

        // Assert
        text.Should().Be(new RichTextBuilder().Code("a`b\\c*d").Build());
    }
}
