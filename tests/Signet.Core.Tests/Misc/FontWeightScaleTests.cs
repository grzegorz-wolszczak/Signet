using System.Linq;
using AwesomeAssertions;
using Signet.Core.Misc;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>The weights of a font as the settings store them.</summary>
public sealed class FontWeightScaleTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(300, 300)]
    [InlineData(5000, 950)]
    public void A_weight_is_clamped_and_zero_stays_not_set(int stored, int expected)
    {
        // Act & Assert
        FontWeightScale.Normalize(stored).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(100, "Thin")]
    [InlineData(200, "ExtraLight")]
    [InlineData(300, "Light")]
    [InlineData(350, "SemiLight")]
    [InlineData(400, "Regular")]
    [InlineData(500, "Medium")]
    [InlineData(600, "SemiBold")]
    [InlineData(700, "Bold")]
    [InlineData(800, "ExtraBold")]
    [InlineData(900, "Black")]
    [InlineData(950, "ExtraBlack")]
    [InlineData(450, "450")]
    public void A_weight_has_the_name_fonts_give_it(int weight, string? name)
    {
        // Act & Assert
        FontWeightScale.NameOf(weight).Should().Be(name);
    }

    [Fact]
    public void The_standard_weights_are_ordered_and_all_named()
    {
        // Act & Assert
        FontWeightScale.Standard.Should().BeInAscendingOrder().And.OnlyContain(w => FontWeightScale.NameOf(w)!.Any(char.IsLetter));
    }
}
