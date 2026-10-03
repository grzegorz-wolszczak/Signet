using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.SourceUpdates;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>Tests for <see cref="PerformHtmlUpdates"/> — recomputing references in (X)HTML source.</summary>
public sealed class PerformHtmlUpdatesTests
{
    private const string Source =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
        "<head>\n" +
        "  <title>T</title>\n" +
        "  <link rel=\"stylesheet\" href=\"../Styles/old.css\" type=\"text/css\"/>\n" +
        "</head>\n" +
        "<body>\n" +
        "  <img src=\"../Images/old.jpg\" alt=\"x\"/>\n" +
        "  <a href=\"ch2.xhtml#note\">next</a>\n" +
        "</body>\n" +
        "</html>\n";

    [Fact]
    public void Apply_HrefAndSrcAttributes_AreRewritten()
    {
        Dictionary<string, string> htmlUpdates = new()
        {
            ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg",
            ["OEBPS/Text/ch2.xhtml"] = "OEBPS/Text/ch2-new.xhtml",
        };
        Dictionary<string, string> cssUpdates = new();

        string result = PerformHtmlUpdates.Apply(
            Source, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml", htmlUpdates, cssUpdates, "2.0");

        result.Should().Contain("src=\"../Images/new.jpg\"");
        result.Should().Contain("href=\"ch2-new.xhtml#note\"");
    }

    [Fact]
    public void Apply_StylesheetLink_IsRewrittenWhenTargetIsCssType()
    {
        Dictionary<string, string> htmlUpdates = new() { ["OEBPS/Styles/old.css"] = "OEBPS/Styles/new.css" };
        Dictionary<string, string> cssUpdates = new();

        string result = PerformHtmlUpdates.Apply(
            Source, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml", htmlUpdates, cssUpdates, "2.0");

        result.Should().Contain("href=\"../Styles/new.css\"");
    }

    [Fact]
    public void Apply_InlineStyleUrl_IsRewritten()
    {
        string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
            "<body><div style=\"background: url(../Images/old.jpg);\">x</div></body></html>\n";
        Dictionary<string, string> htmlUpdates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };
        Dictionary<string, string> cssUpdates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };

        string result = PerformHtmlUpdates.Apply(
            source, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml", htmlUpdates, cssUpdates, "2.0");

        result.Should().Contain("url(../Images/new.jpg)");
    }

    [Fact]
    public void Apply_StyleElementContent_IsRewritten()
    {
        string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title>" +
            "<style>body { background: url(../Images/old.jpg); }</style></head>" +
            "<body><p>x</p></body></html>\n";
        Dictionary<string, string> htmlUpdates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };
        Dictionary<string, string> cssUpdates = new() { ["OEBPS/Images/old.jpg"] = "OEBPS/Images/new.jpg" };

        string result = PerformHtmlUpdates.Apply(
            source, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml", htmlUpdates, cssUpdates, "2.0");

        result.Should().Contain("url(../Images/new.jpg)");
    }

    [Fact]
    public void Apply_SrcsetCandidates_AreEachRewritten()
    {
        string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
            "<body><img src=\"../Images/a.jpg\" srcset=\"../Images/a.jpg 1x, ../Images/b.jpg 2x\"/></body></html>\n";
        Dictionary<string, string> htmlUpdates = new()
        {
            ["OEBPS/Images/a.jpg"] = "OEBPS/Images/a2.jpg",
            ["OEBPS/Images/b.jpg"] = "OEBPS/Images/b2.jpg",
        };

        string result = PerformHtmlUpdates.Apply(
            source, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml", htmlUpdates, new Dictionary<string, string>(), "2.0");

        result.Should().Contain("srcset=\"../Images/a2.jpg 1x, ../Images/b2.jpg 2x\"");
    }

    [Fact]
    public void Apply_NoMatchingUpdates_PreservesDocumentStructure()
    {
        string result = PerformHtmlUpdates.Apply(
            Source, "OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml",
            new Dictionary<string, string>(), new Dictionary<string, string>(), "2.0");

        result.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        result.Should().Contain("href=\"../Styles/old.css\"");
        result.Should().Contain("src=\"../Images/old.jpg\"");
    }
}
