using AwesomeAssertions;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

/// <summary>
/// Tests for <see cref="ColorContrast"/> — which of black and white is readable on a color. The color fields of
/// Preferences write the hex of a color on the color itself, so the text has to follow it.
/// </summary>
public sealed class ColorContrastTests
{
    [Theory]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#102030", "#FFFFFF")]
    [InlineData("#FFF000", "#000000")]
    [InlineData("#C50500", "#FFFFFF")]
    // A mid gray: black wins, though not by much (contrast 4.69 against 4.47) — the threshold is ~0.179, not 0.5.
    [InlineData("#767676", "#000000")]
    [InlineData("#6E6E6E", "#FFFFFF")]
    [InlineData("#CCCCCC", "#000000")]
    public void The_readable_text_follows_the_color_it_is_written_on(string background, string expected)
    {
        // Act
        ThemeColor text = ColorContrast.ReadableOn(ThemeColor.Parse(background), darkSurface: false);

        // Assert
        text.ToString().Should().Be(expected);
    }

    /// <summary>
    /// A color that is not opaque is read as what is seen: half-transparent black is a mid gray on a white panel
    /// (black text) and nearly black on a dark one (white text). The stored color is the same in both.
    /// </summary>
    [Fact]
    public void A_transparent_color_is_judged_over_the_surface_it_is_drawn_on()
    {
        // Arrange
        ThemeColor halfBlack = ThemeColor.Parse("#33000000");

        // Act & Assert
        ColorContrast.ReadableOn(halfBlack, darkSurface: false).ToString().Should().Be("#000000");

        // Act & Assert
        ColorContrast.ReadableOn(halfBlack, darkSurface: true).ToString().Should().Be("#FFFFFF");
    }

    [Fact]
    public void Compositing_mixes_the_color_into_the_surface_by_its_alpha()
    {
        // Act & Assert
        ColorContrast.Composite(ThemeColor.Parse("#80000000"), darkSurface: false).ToString().Should().Be("#7F7F7F");

        // Act & Assert
        ColorContrast.Composite(ThemeColor.Parse("#FF336699"), darkSurface: true).ToString()
            .Should().Be("#336699", "an opaque color is not touched");
    }

    [Fact]
    public void Luminance_runs_from_black_to_white()
    {
        // Act & Assert
        ColorContrast.Luminance(ThemeColor.Parse("#000000")).Should().Be(0);

        // Act & Assert
        ColorContrast.Luminance(ThemeColor.Parse("#FFFFFF")).Should().BeApproximately(1, 1e-9);

        // Act & Assert
        ColorContrast.Luminance(ThemeColor.Parse("#808080")).Should().BeInRange(0.2, 0.3);
    }
}
