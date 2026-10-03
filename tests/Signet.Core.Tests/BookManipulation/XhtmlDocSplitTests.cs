using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="XhtmlDoc.GetSgfSectionSplits"/> — Split At Markers.</summary>
public sealed class XhtmlDocSplitTests
{
    private const string Marker = "<hr class=\"signet_split_marker\"/>";

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
        "<body>" + body + "</body></html>\n";

    [Fact]
    public void GetSgfSectionSplits_NoMarker_ReturnsSingleSectionWithSameContent()
    {
        string source = Xhtml("<p>only section</p>");

        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(source);

        sections.Should().ContainSingle();
        sections[0].Should().Contain("<p>only section</p>").And.Contain("<title>T</title>");
    }

    [Fact]
    public void GetSgfSectionSplits_NoBodyTag_ReturnsSourceUnchanged()
    {
        const string source = "<p>fragment without html/body</p>";

        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(source);

        sections.Should().Equal(source);
    }

    [Fact]
    public void GetSgfSectionSplits_OneMarker_ProducesTwoSectionsEachWithHeadAndBody()
    {
        string source = Xhtml($"<p>first</p>{Marker}<p>second</p>");

        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(source);

        sections.Should().HaveCount(2);
        sections[0].Should().Contain("<title>T</title>").And.Contain("<p>first</p>").And.NotContain("<p>second</p>");
        sections[1].Should().Contain("<title>T</title>").And.Contain("<p>second</p>").And.NotContain("<p>first</p>");
        foreach (string section in sections)
        {
            section.Should().Contain("<body>").And.Contain("</body>").And.Contain("</html>");
        }
    }

    [Fact]
    public void GetSgfSectionSplits_TwoMarkers_ProducesThreeSections()
    {
        string source = Xhtml($"<p>a</p>{Marker}<p>b</p>{Marker}<p>c</p>");

        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(source);

        sections.Should().HaveCount(3);
        sections[0].Should().Contain("<p>a</p>");
        sections[1].Should().Contain("<p>b</p>");
        sections[2].Should().Contain("<p>c</p>");
    }

    [Fact]
    public void GetSgfSectionSplits_MarkerInsideOpenDiv_ReinjectsOpenDivIntoSecondSection()
    {
        string source = Xhtml($"<div class=\"wrap\"><p>a</p>{Marker}<p>b</p></div>");

        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(source);

        sections.Should().HaveCount(2);
        sections[0].Should().Contain("<div class=\"wrap\">").And.Contain("<p>a</p>");
        sections[1].Should().Contain("<div class=\"wrap\">").And.Contain("<p>b</p>");
    }

    [Fact]
    public void GetSgfSectionSplits_MarkerWrappedInDiv_IsRecognized()
    {
        string source = Xhtml($"<p>a</p><div>{Marker}</div><p>b</p>");

        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(source);

        sections.Should().HaveCount(2);
        sections[0].Should().Contain("<p>a</p>");
        sections[1].Should().Contain("<p>b</p>");
    }
}
