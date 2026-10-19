using AwesomeAssertions;
using Signet.Core.Semantics;
using Signet.Core.Localization;
using Xunit;

namespace Signet.Core.Tests.Semantics;

/// <summary>Spot checks of the <see cref="GuideItems"/> and <see cref="Landmarks"/> tables.</summary>
public sealed class SemanticsDataTests
{
    [Fact]
    public void GuideItems_has_expected_cardinality_and_bidirectional_lookup()
    {
        // Act & Assert
        GuideItems.GetCodeMap().Should().HaveCount(36);

        // Act & Assert
        GuideItems.GetName("text").Should().Be(CoreStrings.Get("Guide_text_Name"));

        // Act & Assert
        GuideItems.GetCode("Table of Contents").Should().Be("toc");

        // Act & Assert
        GuideItems.IsGuideItemsCode("other.afterword").Should().BeTrue();

        // Act & Assert
        GuideItems.IsGuideItemsName("Cover").Should().BeTrue();

        // Act & Assert
        GuideItems.GetName("unknown-code").Should().Be("unknown-code");

        // Act & Assert
        GuideItems.GetDescriptionByCode("cover").Should().Be(CoreStrings.Get("Guide_cover_Desc"));
    }

    [Fact]
    public void Landmarks_has_expected_cardinality_and_bidirectional_lookup()
    {
        // Act & Assert
        Landmarks.GetCodeMap().Should().HaveCount(49);

        // Act & Assert
        Landmarks.GetName("bodymatter").Should().Be(CoreStrings.Get("Landmark_bodymatter_Name"));

        // Act & Assert
        Landmarks.GetCode("Title Page").Should().Be("titlepage");

        // Act & Assert
        Landmarks.IsLandmarksCode("toc").Should().BeTrue();

        // Act & Assert
        Landmarks.GetName("not-a-code").Should().BeEmpty();
    }

    [Theory]
    [InlineData("text", "bodymatter")]
    [InlineData("bodymatter", "text")]
    [InlineData("title-page", "titlepage")]
    [InlineData("titlepage", "title-page")]
    [InlineData("other.afterword", "afterword")]
    [InlineData("afterword", "other.afterword")]
    [InlineData("toc", "toc")]
    public void Landmarks_GuideLandMapping_maps_both_directions(string input, string expected)
    {
        // Act & Assert
        Landmarks.GuideLandMapping(input).Should().Be(expected);
    }

    [Fact]
    public void Landmarks_GuideLandMapping_returns_empty_for_unmapped()
    {
        // Act & Assert
        Landmarks.GuideLandMapping("chapter").Should().BeEmpty();
    }
}
