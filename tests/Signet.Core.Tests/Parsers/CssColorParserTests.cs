using System;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>
/// Tests of <see cref="CssColorParser"/>: the colors of CSS Color 4 as 8-bit sRGB. The expected values are the ones of Calibre's tests
/// (<c>tinycss/tests/color4.py</c>), the 8-bit values that Chromium renders, within one step.
/// </summary>
public sealed class CssColorParserTests
{
    private static void Parses(string text, int r, int g, int b, int a)
    {
        // Act
        bool ok = CssColorParser.TryParse(text, out CssColor color);

        // Assert
        ok.Should().BeTrue(text);
        Math.Abs(color.R - r).Should().BeLessThanOrEqualTo(1, $"{text} red {color}");
        Math.Abs(color.G - g).Should().BeLessThanOrEqualTo(1, $"{text} green {color}");
        Math.Abs(color.B - b).Should().BeLessThanOrEqualTo(1, $"{text} blue {color}");
        Math.Abs(color.A - a).Should().BeLessThanOrEqualTo(1, $"{text} alpha {color}");
    }

    [Theory]
    [InlineData("rebeccapurple", 102, 51, 153, 255)]
    [InlineData("RebeccaPurple", 102, 51, 153, 255)]
    [InlineData("#f008", 255, 0, 0, 136)]
    [InlineData("#ff000080", 255, 0, 0, 128)]
    [InlineData("#AbCdEf12", 171, 205, 239, 18)]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("#CbA987", 203, 169, 135, 255)]
    [InlineData("rgb(255 0 0 / 50%)", 255, 0, 0, 128)]
    [InlineData("rgb(100% 0 0)", 255, 0, 0, 255)]
    [InlineData("rgb(none 128 none)", 0, 128, 0, 255)]
    [InlineData("rgb(255, 0, 0, 50%)", 255, 0, 0, 128)]
    [InlineData("rgb(1e2 0 0)", 100, 0, 0, 255)]
    [InlineData("rgb(2.5e1, 1, 1)", 25, 1, 1, 255)]
    [InlineData("rgb(255/**/0/**/0)", 255, 0, 0, 255)]
    [InlineData("rgb(-10 300 0)", 0, 255, 0, 255)]
    [InlineData("rgb(203, 169, 135)", 203, 169, 135, 255)]
    [InlineData("rgb(/* R */0, /* G */51, /* B */255)", 0, 51, 255, 255)]
    [InlineData("rgb(10%, 20%, 30%)", 26, 51, 77, 255)]
    [InlineData("rgba(1, 2, 3, 0.5)", 1, 2, 3, 128)]
    [InlineData("hsl(0.5turn 50% 50%)", 64, 191, 191, 255)]
    [InlineData("hsl(3.14159rad 100% 50%)", 0, 255, 255, 255)]
    [InlineData("hsl(200grad 50% 50%)", 64, 191, 191, 255)]
    [InlineData("hsl(120 50 50)", 64, 191, 64, 255)]
    [InlineData("hsl(none 50% 50%)", 191, 64, 64, 255)]
    [InlineData("hsla(120 50% 50%)", 64, 191, 64, 255)]
    [InlineData("hsl(120 -50% 50%)", 128, 128, 128, 255)]
    [InlineData("hsl(120 150% 50%)", 0, 255, 0, 255)]
    [InlineData("hsl(120, 50%, 50%)", 64, 191, 64, 255)]
    [InlineData("hsla(-300, 100%, 37.5%, 0.2)", 191, 191, 0, 51)]
    [InlineData("hwb(120 20% 30%)", 51, 179, 51, 255)]
    [InlineData("hwb(120deg 60% 60%)", 128, 128, 128, 255)]
    [InlineData("hwb(90 20 30)", 115, 179, 51, 255)]
    [InlineData("lab(50% 40 59.5)", 191, 87, 0, 255)]
    [InlineData("lab(54.29 80.82 69.91)", 255, 0, 0, 255)]
    [InlineData("lab(100 0 0)", 255, 255, 255, 255)]
    [InlineData("lab(50 40% -20%)", 183, 80, 163, 255)]
    [InlineData("lch(54.29 106.84 40.85)", 255, 0, 0, 255)]
    [InlineData("lch(50% 50% 120deg)", 70, 134, 0, 255)]
    [InlineData("lch(70 45 none)", 245, 137, 173, 255)]
    [InlineData("lch(70 -10 30)", 171, 171, 171, 255)]
    [InlineData("oklab(0.628 0.2249 0.1258)", 255, 0, 0, 255)]
    [InlineData("oklab(62.8% 56% 31%)", 254, 6, 8, 255)]
    [InlineData("oklab(1 0 0)", 255, 255, 255, 255)]
    [InlineData("oklch(0.628 0.2577 29.23)", 255, 0, 0, 255)]
    [InlineData("oklch(70% 0.1 200)", 64, 177, 183, 255)]
    [InlineData("oklch(0.7 40% 0.25turn)", 197, 152, 0, 255)]
    [InlineData("OKLCH(0.7 0.1 200)", 64, 177, 183, 255)]
    [InlineData("color(srgb 100% 50% 0%)", 255, 128, 0, 255)]
    [InlineData("color(srgb-linear 0.5 0.2 0.1)", 188, 124, 89, 255)]
    [InlineData("color(display-p3 0.9175 0.2003 0.1386)", 255, 0, 0, 255)]
    [InlineData("color(srgb none 0.5 none)", 0, 128, 0, 255)]
    [InlineData("COLOR(SRGB 1 0 0)", 255, 0, 0, 255)]
    [InlineData(" red ", 255, 0, 0, 255)]
    [InlineData(" /**/red/**/ ", 255, 0, 0, 255)]
    [InlineData("red /* unterminated", 255, 0, 0, 255)]
    [InlineData("rgb(255 0 0", 255, 0, 0, 255)]
    [InlineData("transparent", 0, 0, 0, 0)]
    public void A_color_is_read_as_8_bit_srgb(string text, int r, int g, int b, int a) => Parses(text, r, g, b, a);

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#ff0000f")]
    [InlineData("#ggg")]
    [InlineData("canvas")]
    [InlineData("currentColor")]
    [InlineData("red blue")]
    [InlineData("rgb(255 0 0) x")]
    [InlineData("rgb()")]
    [InlineData("rgb(255 0)")]
    [InlineData("rgb(255 0 0 0)")]
    [InlineData("rgb(255 0 0, 1)")]
    [InlineData("rgb(255 0 0 /)")]
    [InlineData("rgb(255 0 0 / 1 / 1)")]
    [InlineData("rgb(255, 0, 0, none)")]
    [InlineData("rgb(255, 0%, 0)")]
    [InlineData("rgb(var(--x))")]
    [InlineData("rgb(calc(1) 0 0)")]
    [InlineData("rgb(from red r g b)")]
    [InlineData("rgb(1px 0 0)")]
    [InlineData("hsl(120, 50, 50)")]
    [InlineData("hsl(120% 50% 50%)")]
    [InlineData("hsl(120px 50% 50%)")]
    [InlineData("hwb(120, 20%, 30%)")]
    [InlineData("lab(50, 40, 59)")]
    [InlineData("lab(50 40)")]
    [InlineData("color(foo 1 0 0)")]
    [InlineData("color(srgb 1 0)")]
    [InlineData("color(srgb, 1, 0, 0)")]
    [InlineData("color(1 0 0)")]
    [InlineData("cmyk(0 0 0 0)")]
    [InlineData("r\\65 d")]
    [InlineData("réd")]
    public void Text_that_is_not_a_color_is_refused(string text)
    {
        // Act & Assert
        CssColorParser.TryParse(text, out _).Should().BeFalse(text);
    }

    [Theory]
    [InlineData("color(a98-rgb 0.6 0.5 0.4)")]
    [InlineData("color(prophoto-rgb 0.5 0.4 0.3)")]
    [InlineData("color(rec2020 0.5 0.4 0.3)")]
    [InlineData("color(xyz 0.3 0.3 0.3)")]
    [InlineData("color(xyz-d65 0.2 0.3 0.4)")]
    [InlineData("color(xyz-d50 0.2 0.3 0.25)")]
    public void The_other_color_spaces_of_color_are_recognized_but_not_converted(string text)
    {
        // Act & Assert
        CssColorParser.IsColorFunction(text).Should().BeTrue();
        CssColorParser.TryParse(text, out _).Should().BeFalse("only srgb, srgb-linear and display-p3 are converted");
    }

    [Fact]
    public void The_hex_text_of_a_color_has_an_alpha_only_when_it_is_not_opaque()
    {
        // Assert
        new CssColor(255, 128, 0, 255).ToHex().Should().Be("#ff8000");
        new CssColor(255, 128, 0, 128).ToHex().Should().Be("#ff800080");
        new CssColor(255, 0, 0, 255).ToShortHex().Should().Be("#f00");
        new CssColor(255, 1, 0, 255).ToShortHex().Should().Be("#ff0100", "a color that has no short form");
    }
}
