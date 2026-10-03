using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// <see cref="ResourceType"/> must keep stable bit values, because they are used to filter
/// sets of resources.
/// </summary>
public sealed class ResourceTypeTests
{
    [Theory]
    [InlineData(ResourceType.Generic, 1 << 0)]
    [InlineData(ResourceType.Text, 1 << 1)]
    [InlineData(ResourceType.Xml, 1 << 2)]
    [InlineData(ResourceType.Html, 1 << 3)]
    [InlineData(ResourceType.Css, 1 << 4)]
    [InlineData(ResourceType.Image, 1 << 5)]
    [InlineData(ResourceType.Svg, 1 << 6)]
    [InlineData(ResourceType.Font, 1 << 7)]
    [InlineData(ResourceType.Opf, 1 << 8)]
    [InlineData(ResourceType.Ncx, 1 << 9)]
    [InlineData(ResourceType.MiscText, 1 << 10)]
    [InlineData(ResourceType.Audio, 1 << 11)]
    [InlineData(ResourceType.Video, 1 << 12)]
    [InlineData(ResourceType.Pdf, 1 << 13)]
    public void Enum_values_match_the_expected_bit_flags(ResourceType value, int expected)
    {
        ((int)value).Should().Be(expected);
    }

    [Fact]
    public void Values_can_be_combined_and_tested_as_flags()
    {
        ResourceType textual = ResourceType.Html | ResourceType.Text | ResourceType.MiscText;

        textual.HasFlag(ResourceType.Html).Should().BeTrue();
        textual.HasFlag(ResourceType.Css).Should().BeFalse();
    }
}
