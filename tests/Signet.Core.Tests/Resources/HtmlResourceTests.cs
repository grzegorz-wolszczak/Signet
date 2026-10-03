using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="HtmlResource"/> — the DOM cache and queries for linked resources / properties.</summary>
public sealed class HtmlResourceTests
{
    private static readonly string[] ExpectedLinkedResources = { "styles/main.css", "images/pic.png" };

    private const string Sample =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"pl\">\n" +
        "<head>\n" +
        "  <title>T</title>\n" +
        "  <link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/main.css\"/>\n" +
        "  <script src=\"../js/app.js\"></script>\n" +
        "</head>\n" +
        "<body>\n" +
        "  <img src=\"../images/pic.png\" alt=\"\"/>\n" +
        "  <p>See <a href=\"https://example.com/\">external</a> and <a href=\"other.xhtml#x\">local</a>.</p>\n" +
        "  <svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"5\"/></svg>\n" +
        "  <script>var x = 1;</script>\n" +
        "</body>\n" +
        "</html>\n";

    private static HtmlResource Make(string text = Sample)
    {
        TempDir root = new();
        HtmlResource resource = new(root.Path, root.Combine("Text", "chapter1.xhtml"));
        resource.SetText(text);
        return resource;
    }

    [Fact]
    public void Reports_its_type_and_language()
    {
        HtmlResource resource = Make();

        resource.Type.Should().Be(ResourceType.Html);
        resource.GetLanguageAttribute().Should().Be("pl");
    }

    [Fact]
    public void Lists_linked_stylesheets_and_javascripts_as_bookpaths()
    {
        HtmlResource resource = Make();

        resource.GetLinkedStylesheets().Should().ContainSingle().Which.Should().Be("styles/main.css");
        resource.GetLinkedJavascripts().Should().ContainSingle().Which.Should().Be("js/app.js");
    }

    [Fact]
    public void Lists_paths_to_linked_resources_ignoring_absolute_urls_and_fragments()
    {
        HtmlResource resource = Make();

        resource.GetPathsToLinkedResources().Should().BeEquivalentTo(ExpectedLinkedResources);
    }

    [Fact]
    public void Derives_manifest_properties_from_content()
    {
        HtmlResource resource = Make();

        resource.GetManifestProperties().Should().Equal("svg", "scripted");
    }

    [Fact]
    public void Detects_remote_resources_property()
    {
        HtmlResource resource = Make(
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>" +
            "<audio src=\"https://cdn.example.com/a.mp3\"></audio></body></html>");

        resource.GetManifestProperties().Should().Contain("remote-resources");
    }

    [Fact]
    public void Rebuilds_the_dom_cache_after_the_text_changes()
    {
        HtmlResource resource = Make();
        resource.GetManifestProperties().Should().Contain("svg");

        resource.SetText("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>plain</p></body></html>");

        resource.GetManifestProperties().Should().BeEmpty();
    }
}
