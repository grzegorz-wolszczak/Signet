using System;
using AwesomeAssertions;
using Signet.Core.SourceUpdates;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>Tests for <see cref="LinkStylesheetsUpdate"/> — relinking <c>&lt;link rel="stylesheet"&gt;</c>.</summary>
public sealed class LinkStylesheetsUpdateTests
{
    private const string Source =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
        "<head>\n" +
        "  <title>Ch</title>\n" +
        "  <link href=\"../Styles/old.css\" type=\"text/css\" rel=\"stylesheet\"/>\n" +
        "</head>\n" +
        "<body><p>hi</p></body>\n" +
        "</html>\n";

    [Fact]
    public void Apply_replaces_existing_links_with_selected_stylesheets_in_order()
    {
        string[] sheets = { "OEBPS/Styles/a.css", "OEBPS/Styles/b.css" };
        string result = LinkStylesheetsUpdate.Apply(Source, "OEBPS/Text/ch1.xhtml", sheets, "3.0");

        result.Should().NotContain("old.css");
        result.Should().Contain("href=\"../Styles/a.css\"");
        result.Should().Contain("href=\"../Styles/b.css\"");
        result.Should().Contain("rel=\"stylesheet\"");
        result.Should().Contain("type=\"text/css\"");
        result.IndexOf("a.css", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("b.css", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_with_empty_list_removes_all_head_links()
    {
        string result = LinkStylesheetsUpdate.Apply(
            Source, "OEBPS/Text/ch1.xhtml", Array.Empty<string>(), "3.0");

        result.Should().NotContain("<link");
        result.Should().Contain("<title>Ch</title>");
    }

    [Fact]
    public void Apply_url_encodes_href()
    {
        string[] sheets = { "OEBPS/Styles/my style.css" };
        string result = LinkStylesheetsUpdate.Apply(Source, "OEBPS/Text/ch1.xhtml", sheets, "3.0");

        result.Should().Contain("../Styles/my%20style.css");
    }

    [Fact]
    public void Apply_returns_empty_source_unchanged()
    {
        string[] sheets = { "OEBPS/Styles/a.css" };
        LinkStylesheetsUpdate.Apply(string.Empty, "OEBPS/Text/ch1.xhtml", sheets, "3.0")
            .Should().BeEmpty();
    }
}
