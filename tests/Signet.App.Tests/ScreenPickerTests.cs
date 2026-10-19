using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.ScreenPicker;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The testable parts of the screen color picker: the snapshot, the magnifier placement, color names, palette.</summary>
public sealed class ScreenPickerTests
{
    private readonly Fixture _fixture = new();

    // A 4 × 3 snapshot whose pixel (x, y) has the color (x, y, x + y) and sits at the desktop origin (ox, oy).
    private static ScreenSnapshot Gradient(int ox, int oy)
    {
        byte[] bgra = new byte[4 * 3 * 4];
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int i = ((y * 4) + x) * 4;
                bgra[i] = (byte)(x + y);
                bgra[i + 1] = (byte)y;
                bgra[i + 2] = (byte)x;
                bgra[i + 3] = 0;
            }
        }

        return new ScreenSnapshot(bgra, 4, 3, ox, oy);
    }

    [Fact]
    public void A_pixel_is_read_in_desktop_coordinates_with_the_origin_applied()
    {
        // Arrange
        int ox = _fixture.Create<short>();
        int oy = -Math.Abs(_fixture.Create<short>());
        ScreenSnapshot snapshot = Gradient(ox, oy);

        // Act & Assert
        snapshot.GetPixel(ox + 2, oy + 1).Should().Be(Color.FromRgb(2, 1, 3));
        snapshot.GetPixel(ox, oy).Should().Be(Color.FromRgb(0, 0, 0));
    }

    [Fact]
    public void Pixels_outside_the_snapshot_are_transparent()
    {
        // Arrange
        ScreenSnapshot snapshot = Gradient(10, 20);

        // Act & Assert
        snapshot.Contains(9, 20).Should().BeFalse();
        snapshot.Contains(14, 20).Should().BeFalse();
        snapshot.GetPixel(9, 20).A.Should().Be(0);
        snapshot.GetPixel(10, 23).A.Should().Be(0);
    }

    [Fact]
    public void A_region_is_centred_on_the_pixel_and_pads_the_edges_with_transparent()
    {
        // Arrange
        ScreenSnapshot snapshot = Gradient(0, 0);

        // Act
        Color[] region = snapshot.GetRegion(0, 0, 3);

        // Assert
        region.Should().HaveCount(9);
        region[4].Should().Be(Color.FromRgb(0, 0, 0)); // the centre
        region[5].Should().Be(Color.FromRgb(1, 0, 1)); // right of the centre
        region[7].Should().Be(Color.FromRgb(0, 1, 1)); // below the centre
        region[0].A.Should().Be(0);                    // above-left is outside
    }

    [Fact]
    public void A_buffer_smaller_than_the_image_is_rejected()
    {
        // Arrange
        Action act = () => _ = new ScreenSnapshot(new byte[15], 2, 2, 0, 0);

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_magnifier_goes_below_right_of_the_pointer_when_it_fits()
    {
        // Act
        Point at = MagnifierLayout.Place(new Point(100, 100), new Size(150, 200), new Size(1000, 800), gap: 20);

        // Assert
        at.Should().Be(new Point(120, 120));
    }

    [Fact]
    public void The_magnifier_flips_to_the_other_side_near_the_right_and_bottom_edges()
    {
        // Act
        Point at = MagnifierLayout.Place(new Point(950, 750), new Size(150, 200), new Size(1000, 800), gap: 20);

        // Assert
        at.Should().Be(new Point(950 - 20 - 150, 750 - 20 - 200));
    }

    [Fact]
    public void The_magnifier_stays_inside_a_window_smaller_than_itself()
    {
        // Act
        Point at = MagnifierLayout.Place(new Point(10, 10), new Size(150, 200), new Size(100, 100));

        // Assert
        at.Should().Be(new Point(0, 0));
    }

    [Theory]
    [InlineData("tomato", 0xFF, 0x63, 0x47)]
    [InlineData("  CornflowerBlue ", 0x64, 0x95, 0xED)]
    public void A_css_name_parses_to_its_color(string text, int r, int g, int b)
    {
        // Act & Assert
        CssColorNames.TryParse(text, out Color color).Should().BeTrue();

        // Assert
        color.Should().Be(Color.FromRgb((byte)r, (byte)g, (byte)b));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#FF0000")]
    [InlineData("notacolor")]
    public void Anything_that_is_not_a_name_is_not_parsed(string? text)
    {
        // Act & Assert
        CssColorNames.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void An_exact_match_has_a_name_and_alpha_is_ignored()
    {
        // Act & Assert
        CssColorNames.NameOf(Color.FromRgb(0xFF, 0x63, 0x47)).Should().Be("tomato");
        CssColorNames.NameOf(Color.FromArgb(0x40, 0xFF, 0x63, 0x47)).Should().Be("tomato");
        CssColorNames.NameOf(Color.FromRgb(1, 2, 3)).Should().BeNull();
    }

    [Fact]
    public void The_palette_is_a_grid_of_distinct_colors_from_white_to_black()
    {
        // Arrange
        IReadOnlyList<Color> colors = BasicColorPalette.Colors;

        // Act & Assert
        colors.Should().HaveCount(BasicColorPalette.Columns * 4);
        colors.Distinct().Should().HaveCount(colors.Count);
        colors[0].Should().Be(Color.FromRgb(255, 255, 255));
        colors[BasicColorPalette.Columns - 1].Should().Be(Color.FromRgb(0, 0, 0));
        colors[BasicColorPalette.Columns + BasicColorPalette.Columns].Should().Be(Color.FromRgb(255, 0, 0)); // pure red, hue 0
    }
}
