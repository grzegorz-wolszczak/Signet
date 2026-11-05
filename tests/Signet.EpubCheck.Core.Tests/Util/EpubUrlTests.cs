using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class EpubUrlTests
{
    [Theory]
    [InlineData("Text/../chapter 1.xhtml", "chapter%201.xhtml")]
    [InlineData("../Text/./chapter.xhtml", "../Text/chapter.xhtml")]
    [InlineData("/Text/%65%CC%81.xhtml", "/Text/%C3%A9.xhtml")]
    public void NormalizePath_PathWithDotSegmentsOrUnicode_ReturnsCanonicalEncodedPath(string path, string expected)
    {
        // Act
        string actual = EpubUrl.NormalizePath(path);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void Parse_RelativeReferenceAndBase_ReturnsResolvedNormalizedUrl()
    {
        // Arrange
        EpubUrl baseUrl = EpubUrl.Parse("https://Example.COM/OPS/Text/chapter.xhtml?old=1");

        // Act
        EpubUrl actual = EpubUrl.Parse("../Images/cover image.svg#target", baseUrl);

        // Assert
        actual.Href.Should().Be("https://example.com/OPS/Images/cover%20image.svg#target");
        actual.DocumentUrl().Href.Should().Be("https://example.com/OPS/Images/cover%20image.svg");
    }

    [Theory]
    [InlineData("id:~:text=a,b", "text/html", "id", "", false, true)]
    [InlineData("epubcfi(/6/4)", "application/xhtml+xml", "", "epubcfi", false, true)]
    [InlineData("xywh=160,120,320,240", "image/svg+xml", "", "", true, true)]
    [InlineData("id&foo=bar", "image/svg+xml", "id", "", false, false)]
    [InlineData("foo=bar", "image/svg+xml", "", "", false, false)]
    public void Parse_FragmentForMimeType_ReturnsFragmentClassification(
        string fragment,
        string mimeType,
        string expectedId,
        string expectedScheme,
        bool expectedMedia,
        bool expectedValidity)
    {
        // Act
        EpubUrlFragment actual = EpubUrlFragment.Parse(fragment, mimeType);

        // Assert
        actual.Id.Should().Be(expectedId);
        actual.Scheme.Should().Be(expectedScheme);
        actual.IsMediaFragment.Should().Be(expectedMedia);
        actual.IsValid.Should().Be(expectedValidity);
    }
}
