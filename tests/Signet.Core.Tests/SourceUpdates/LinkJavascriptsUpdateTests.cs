using System;
using AwesomeAssertions;
using Signet.Core.SourceUpdates;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>Tests for <see cref="LinkJavascriptsUpdate"/> — relinking <c>&lt;script src&gt;</c>.</summary>
public sealed class LinkJavascriptsUpdateTests
{
    private const string Source =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
        "<head>\n" +
        "  <title>Ch</title>\n" +
        "  <link href=\"../Styles/s.css\" type=\"text/css\" rel=\"stylesheet\"/>\n" +
        "  <script type=\"text/javascript\" src=\"../Scripts/old.js\"></script>\n" +
        "</head>\n" +
        "<body><p>hi</p></body>\n" +
        "</html>\n";

    [Fact]
    public void Apply_replaces_js_links_but_keeps_stylesheet_links()
    {
        string[] scripts = { "OEBPS/Scripts/a.js" };
        string result = LinkJavascriptsUpdate.Apply(Source, "OEBPS/Text/ch1.xhtml", scripts, "3.0");

        result.Should().NotContain("old.js");
        result.Should().Contain("src=\"../Scripts/a.js\"");
        result.Should().Contain("href=\"../Styles/s.css\"");
    }

    [Fact]
    public void Apply_with_empty_list_removes_all_js_links()
    {
        string result = LinkJavascriptsUpdate.Apply(
            Source, "OEBPS/Text/ch1.xhtml", Array.Empty<string>(), "3.0");

        result.Should().NotContain("<script");
        result.Should().Contain("href=\"../Styles/s.css\"");
    }

    [Fact]
    public void Apply_emits_non_self_closing_script_element()
    {
        string[] scripts = { "OEBPS/Scripts/a.js" };
        string result = LinkJavascriptsUpdate.Apply(Source, "OEBPS/Text/ch1.xhtml", scripts, "3.0");

        result.Should().Contain("</script>");
    }
}
