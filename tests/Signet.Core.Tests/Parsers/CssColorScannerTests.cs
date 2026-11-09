using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests of <see cref="CssColorScanner"/>: where the colors are in a stylesheet or in (X)HTML, and how a new color is written.</summary>
public sealed class CssColorScannerTests
{
    private static string[] Texts(string text, IReadOnlyList<CssColorMatch> matches) =>
        matches.Select(m => text.Substring(m.Start, m.Length)).ToArray();

    [Fact]
    public void The_colors_in_the_values_of_the_declarations_are_found_with_their_place_and_notation()
    {
        // Arrange
        const string css = "p { color: #ff0000; background: rgb(0 128 0) url(x.png) no-repeat; border-color: Tomato hsl(120 50% 50%) }";

        // Act
        IReadOnlyList<CssColorMatch> matches = CssColorScanner.InStylesheet(css);

        // Assert
        Texts(css, matches).Should().Equal("#ff0000", "rgb(0 128 0)", "Tomato", "hsl(120 50% 50%)");
        matches.Select(m => m.Notation).Should().Equal(CssColorNotation.Hex, CssColorNotation.Rgb, CssColorNotation.Named, CssColorNotation.Hsl);
        matches[0].Color.Should().Be(new CssColor(255, 0, 0, 255));
        matches[1].Color.Should().Be(new CssColor(0, 128, 0, 255));
        matches[2].Color.Should().Be(new CssColor(255, 99, 71, 255));
    }

    [Fact]
    public void Selectors_strings_urls_and_comments_are_not_scanned()
    {
        // Arrange
        const string css = "#fff, a#abc:hover, .red { content: \"#fff\"; background: url(#abc); /* color: #123 */ margin: 0 }";

        // Act & Assert
        CssColorScanner.InStylesheet(css).Should().BeEmpty();
    }

    [Fact]
    public void A_named_color_counts_only_in_the_properties_that_take_colors()
    {
        // Arrange
        const string css = "p { font-family: Orange, tomato; border: 1px solid red; --accent: tomato; text-decoration: underline blue; list-style: square }";

        // Act
        IReadOnlyList<CssColorMatch> matches = CssColorScanner.InStylesheet(css);

        // Assert
        Texts(css, matches).Should().Equal("red", "tomato", "blue");
    }

    [Fact]
    public void Colors_inside_media_rules_and_inside_other_functions_are_found()
    {
        // Arrange
        const string css = "@media print { div { background: linear-gradient(red, #00f 50%, rgba(0,0,0,.5)); } }";

        // Act
        IReadOnlyList<CssColorMatch> matches = CssColorScanner.InStylesheet(css);

        // Assert
        Texts(css, matches).Should().Equal("red", "#00f", "rgba(0,0,0,.5)");
    }

    [Fact]
    public void Colors_that_cannot_be_converted_are_recognized_with_no_color_and_what_is_not_a_color_is_left_out()
    {
        // Arrange
        const string css = "p { a: color(a98-rgb 0.6 0.5 0.4); b: var(--x); c: #12; d: #abcdefg; e: oklch(0.7 0.1 200); f: rgb(var(--r) 0 0); g: calc(1px + 2px) }";

        // Act
        IReadOnlyList<CssColorMatch> matches = CssColorScanner.InStylesheet(css);

        // Assert
        Texts(css, matches).Should().Equal("color(a98-rgb 0.6 0.5 0.4)", "oklch(0.7 0.1 200)");
        matches[0].Notation.Should().Be(CssColorNotation.Other);
        matches[0].Color.Should().BeNull();
        matches[1].Color.Should().NotBeNull();
    }

    [Fact]
    public void An_unclosed_block_and_empty_text_do_not_throw()
    {
        // Act & Assert
        CssColorScanner.InStylesheet("p { color: #123456").Should().ContainSingle();
        CssColorScanner.InStylesheet(string.Empty).Should().BeEmpty();
        CssColorScanner.InStylesheet("p { color: rgb(1 2").Should().BeEmpty();
    }

    [Fact]
    public void In_html_the_style_elements_and_the_style_attributes_are_scanned_with_offsets_into_the_file()
    {
        // Arrange
        const string html = "<html><head><style>p { color: #f00 }</style></head><body>\n<p style=\"background:rgb(1,2,3);margin:0\">x</p><span style='color: blue'>y</span>"
            + "<p class=\"#fff\" title=\"red\">z</p></body></html>";

        // Act
        IReadOnlyList<CssColorMatch> matches = CssColorScanner.InHtml(html);

        // Assert
        Texts(html, matches).Should().Equal("#f00", "rgb(1,2,3)", "blue");
    }

    [Theory]
    [InlineData("#f00", 255, 0, 0, 255, "#f00")]
    [InlineData("#f00", 255, 0, 1, 255, "#ff0001")]
    [InlineData("#FF0000", 0, 128, 255, 255, "#0080ff")]
    [InlineData("#ff0000", 0, 128, 255, 128, "#0080ff80")]
    [InlineData("rgb(255, 0, 0)", 0, 128, 255, 255, "rgb(0, 128, 255)")]
    [InlineData("rgb(255, 0, 0)", 0, 128, 255, 128, "rgba(0, 128, 255, 0.5)")]
    [InlineData("rgba(255,0,0,1)", 0, 128, 255, 255, "rgb(0,128,255)")]
    [InlineData("rgb(255 0 0)", 0, 128, 255, 255, "rgb(0 128 255)")]
    [InlineData("rgb(255 0 0 / 50%)", 0, 128, 255, 255, "rgb(0 128 255)")]
    [InlineData("rgb(255 0 0)", 0, 128, 255, 128, "rgb(0 128 255 / 0.5)")]
    [InlineData("hsl(0, 100%, 50%)", 255, 0, 0, 255, "hsl(0, 100%, 50%)")]
    [InlineData("hsl(0 100% 50%)", 0, 255, 0, 255, "hsl(120 100% 50%)")]
    [InlineData("hsl(0, 100%, 50%)", 0, 255, 0, 128, "hsla(120, 100%, 50%, 0.5)")]
    [InlineData("red", 0, 0, 255, 255, "blue")]
    [InlineData("red", 1, 2, 3, 255, "#010203")]
    [InlineData("Red", 255, 255, 255, 255, "white")]
    [InlineData("oklch(0.7 0.1 200)", 255, 0, 0, 255, "#ff0000")]
    [InlineData("hwb(120 20% 30%)", 255, 0, 0, 255, "#ff0000")]
    public void A_new_color_is_written_in_the_notation_of_the_old_one_when_it_can_be(string original, int r, int g, int b, int a, string expected)
    {
        // Arrange
        string css = "p { color: " + original + " }";
        CssColorMatch match = CssColorScanner.InStylesheet(css).Single();

        // Act
        string text = CssColorScanner.Format(new CssColor((byte)r, (byte)g, (byte)b, (byte)a), match, css);

        // Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void The_written_color_is_read_back_as_the_same_color()
    {
        // Arrange
        CssColor color = new(18, 52, 86, 200);

        // Act & Assert
        foreach (string original in new[] { "#abc", "#abcdef", "rgb(1, 2, 3)", "rgb(1 2 3)", "hsl(10, 20%, 30%)", "hsl(10 20% 30%)", "red", "lab(50 40 20)" })
        {
            string css = "p { color: " + original + " }";
            string written = CssColorScanner.Format(color, CssColorScanner.InStylesheet(css).Single(), css);
            CssColorParser.TryParse(written, out CssColor back).Should().BeTrue(written);
            ((int)back.R).Should().BeCloseTo(color.R, 3, written);
            ((int)back.G).Should().BeCloseTo(color.G, 3, written);
            ((int)back.B).Should().BeCloseTo(color.B, 3, written);
            ((int)back.A).Should().BeCloseTo(color.A, 3, written);
        }
    }
}
