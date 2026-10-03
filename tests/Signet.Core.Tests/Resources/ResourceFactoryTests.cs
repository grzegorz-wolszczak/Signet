using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="ResourceFactory"/> — choosing the subtype by MIME type and extension.</summary>
public sealed class ResourceFactoryTests
{
    [Theory]
    [InlineData("application/oebps-package+xml", typeof(OpfResource))]
    [InlineData("application/x-dtbncx+xml", typeof(NcxResource))]
    [InlineData("application/xhtml+xml", typeof(HtmlResource))]
    [InlineData("text/css", typeof(CssResource))]
    [InlineData("image/svg+xml", typeof(SvgResource))]
    [InlineData("image/png", typeof(ImageResource))]
    [InlineData("image/jpeg", typeof(ImageResource))]
    [InlineData("font/otf", typeof(FontResource))]
    [InlineData("application/font-woff", typeof(FontResource))]
    [InlineData("audio/mpeg", typeof(AudioResource))]
    [InlineData("video/mp4", typeof(VideoResource))]
    [InlineData("application/pdf", typeof(PdfResource))]
    [InlineData("application/javascript", typeof(MiscTextResource))]
    [InlineData("application/xml", typeof(XmlResource))]
    [InlineData("application/octet-stream", typeof(Resource))]
    public void Selects_subtype_from_media_type(string mediaType, System.Type expected)
    {
        using TempDir root = new();

        Resource resource = ResourceFactory.Create(root.Path, root.Combine("f.bin"), mediaType);

        resource.Should().BeOfType(expected);
        resource.MediaType.Should().Be(mediaType);
    }

    [Fact]
    public void Falls_back_to_extension_when_media_type_is_missing()
    {
        using TempDir root = new();

        Resource css = ResourceFactory.Create(root.Path, root.Combine("Styles", "main.css"));
        Resource html = ResourceFactory.Create(root.Path, root.Combine("Text", "c.xhtml"));

        css.Should().BeOfType<CssResource>();
        css.MediaType.Should().Be("text/css");
        html.Should().BeOfType<HtmlResource>();
    }
}
