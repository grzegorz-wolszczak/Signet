using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Tests of <see cref="MediaTypes"/> - the extension / media-type / group / <see cref="ResourceType"/> maps.
/// </summary>
public sealed class MediaTypesTests
{
    [Theory]
    [InlineData("xhtml", "application/xhtml+xml")]
    [InlineData("html", "application/xhtml+xml")]
    [InlineData("htm", "application/xhtml+xml")]
    [InlineData("css", "text/css")]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("svg", "image/svg+xml")]
    [InlineData("ncx", "application/x-dtbncx+xml")]
    [InlineData("opf", "application/oebps-package+xml")]
    [InlineData("ttf", "font/ttf")]
    [InlineData("otf", "font/otf")]
    [InlineData("woff2", "font/woff2")]
    [InlineData("mp3", "audio/mpeg")]
    [InlineData("mp4", "video/mp4")]
    [InlineData("js", "application/javascript")]
    [InlineData("avif", "image/avif")]
    [InlineData("jxl", "image/jxl")]
    public void GetMediaTypeFromExtension_maps_known_extensions(string extension, string expected)
    {
        MediaTypes.GetMediaTypeFromExtension(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".PNG", "image/png")]
    [InlineData("XHTML", "application/xhtml+xml")]
    [InlineData("  .Css  ", "text/css")]
    public void GetMediaTypeFromExtension_ignores_leading_dot_and_case(string extension, string expected)
    {
        MediaTypes.GetMediaTypeFromExtension(extension).Should().Be(expected);
    }

    [Fact]
    public void GetMediaTypeFromExtension_returns_fallback_for_unknown()
    {
        MediaTypes.GetMediaTypeFromExtension("qwerty").Should().BeEmpty();
        MediaTypes.GetMediaTypeFromExtension("qwerty", "application/octet-stream")
            .Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("application/xhtml+xml", "xhtml")]
    [InlineData("text/css", "css")]
    [InlineData("font/ttf", "ttf")]
    [InlineData("video/mp4", "m4v")]
    [InlineData("application/x-dtbncx+xml", "ncx")]
    public void GetExtensionFromMediaType_maps_known_media_types(string mediaType, string expected)
    {
        MediaTypes.GetExtensionFromMediaType(mediaType).Should().Be(expected);
    }

    [Theory]
    [InlineData("application/xhtml+xml", "Text")]
    [InlineData("image/png", "Images")]
    [InlineData("image/svg+xml", "Images")]
    [InlineData("text/css", "Styles")]
    [InlineData("font/ttf", "Fonts")]
    [InlineData("audio/mpeg", "Audio")]
    [InlineData("video/mp4", "Video")]
    [InlineData("application/oebps-package+xml", "opf")]
    [InlineData("application/x-dtbncx+xml", "ncx")]
    [InlineData("application/pdf", "Misc")]
    public void GetGroupFromMediaType_maps_known_media_types(string mediaType, string expected)
    {
        MediaTypes.GetGroupFromMediaType(mediaType).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/x-unknown-format", "Images")]
    [InlineData("font/some-future-format", "Fonts")]
    [InlineData("application/x-font-weird", "Fonts")]
    [InlineData("audio/strange", "Audio")]
    [InlineData("video/strange", "Video")]
    public void GetGroupFromMediaType_falls_back_to_prefix_heuristics(string mediaType, string expected)
    {
        MediaTypes.GetGroupFromMediaType(mediaType).Should().Be(expected);
    }

    [Fact]
    public void GetGroupFromMediaType_returns_fallback_when_nothing_matches()
    {
        MediaTypes.GetGroupFromMediaType("chemical/x-pdb", "Misc").Should().Be("Misc");
    }

    [Theory]
    [InlineData("application/xhtml+xml", ResourceType.Html)]
    [InlineData("application/x-dtbook+xml", ResourceType.Html)]
    [InlineData("text/css", ResourceType.Css)]
    [InlineData("image/png", ResourceType.Image)]
    [InlineData("image/svg+xml", ResourceType.Svg)]
    [InlineData("font/ttf", ResourceType.Font)]
    [InlineData("application/vnd.ms-opentype", ResourceType.Font)]
    [InlineData("application/oebps-package+xml", ResourceType.Opf)]
    [InlineData("application/x-dtbncx+xml", ResourceType.Ncx)]
    [InlineData("audio/mpeg", ResourceType.Audio)]
    [InlineData("video/mp4", ResourceType.Video)]
    [InlineData("application/smil+xml", ResourceType.Xml)]
    [InlineData("application/xml", ResourceType.Xml)]
    [InlineData("application/javascript", ResourceType.MiscText)]
    [InlineData("text/plain", ResourceType.MiscText)]
    [InlineData("application/pdf", ResourceType.Pdf)]
    public void GetResourceType_maps_media_types_to_the_resource_enum(string mediaType, ResourceType expected)
    {
        MediaTypes.GetResourceType(mediaType).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/jxl", ResourceType.Image)]
    [InlineData("image/avif", ResourceType.Image)]
    public void GetResourceType_treats_epub33_image_types_as_images(string mediaType, ResourceType expected)
    {
        MediaTypes.GetResourceType(mediaType).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/x-unknown-format", ResourceType.Image)]
    [InlineData("font/some-future-format", ResourceType.Font)]
    [InlineData("audio/strange", ResourceType.Audio)]
    public void GetResourceType_falls_back_to_prefix_heuristics(string mediaType, ResourceType expected)
    {
        MediaTypes.GetResourceType(mediaType).Should().Be(expected);
    }

    [Fact]
    public void GetResourceType_returns_fallback_when_nothing_matches()
    {
        MediaTypes.GetResourceType("chemical/x-pdb").Should().Be(ResourceType.Generic);
        MediaTypes.GetResourceType("chemical/x-pdb", ResourceType.Text).Should().Be(ResourceType.Text);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("css")]
    [InlineData("xhtml")]
    [InlineData("ttf")]
    [InlineData("svg")]
    [InlineData("ncx")]
    [InlineData("opf")]
    public void Extension_media_type_round_trips_for_canonical_extensions(string extension)
    {
        string mediaType = MediaTypes.GetMediaTypeFromExtension(extension);
        string backExtension = MediaTypes.GetExtensionFromMediaType(mediaType);

        MediaTypes.GetMediaTypeFromExtension(backExtension).Should().Be(mediaType);
    }
}
