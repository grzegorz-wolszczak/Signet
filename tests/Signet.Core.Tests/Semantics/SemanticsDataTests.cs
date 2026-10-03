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
        GuideItems.GetCodeMap().Should().HaveCount(36);
        GuideItems.GetName("text").Should().Be(CoreStrings.Get("Guide_text_Name"));
        GuideItems.GetCode("Table of Contents").Should().Be("toc");
        GuideItems.IsGuideItemsCode("other.afterword").Should().BeTrue();
        GuideItems.IsGuideItemsName("Cover").Should().BeTrue();
        GuideItems.GetName("unknown-code").Should().Be("unknown-code");
        GuideItems.GetDescriptionByCode("cover").Should().Be(CoreStrings.Get("Guide_cover_Desc"));
    }

    [Fact]
    public void Landmarks_has_expected_cardinality_and_bidirectional_lookup()
    {
        Landmarks.GetCodeMap().Should().HaveCount(49);
        Landmarks.GetName("bodymatter").Should().Be(CoreStrings.Get("Landmark_bodymatter_Name"));
        Landmarks.GetCode("Title Page").Should().Be("titlepage");
        Landmarks.IsLandmarksCode("toc").Should().BeTrue();
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
        Landmarks.GuideLandMapping(input).Should().Be(expected);
    }

    [Fact]
    public void Landmarks_GuideLandMapping_returns_empty_for_unmapped()
    {
        Landmarks.GuideLandMapping("chapter").Should().BeEmpty();
    }
}
