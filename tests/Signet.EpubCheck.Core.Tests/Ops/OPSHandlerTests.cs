using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ops;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ops;

/// <summary>What the handler of the content documents of EPUB 2 (and the base of that of EPUB 3) registers and reports.</summary>
public sealed class OPSHandlerTests
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string XlinkNamespace = "http://www.w3.org/1999/xlink";

    [Fact]
    public void Check_Hyperlink_IsRegisteredAsHyperlink()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p><a href=\"other.xhtml#top\">go</a></p>").With("other.xhtml", ContentDocumentHarness.Xhtml);
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Should().ContainSingle().Which.Should().Match<Reference>(reference =>
            reference.Type == ReferenceType.Hyperlink && reference.Url.ToString().EndsWith("other.xhtml#top", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Check_RemoteHyperlink_IsReportedAsReferenceFeatureAndNotRegistered()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p><a href=\"https://example.org/page\">go</a></p>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Should().BeEmpty();
        harness.Report.Features.Should().Contain(feature => feature.Feature == FeatureKind.Reference && feature.Value == "https://example.org/page");
    }

    [Fact]
    public void Check_EmptyHref_ReportsHtm045ButADotDoesNot()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p><a href=\"\">a</a><a href=\".\">b</a></p>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Messages(MessageId.HTM_045).Should().ContainSingle();
    }

    [Fact]
    public void Check_UnregisteredScheme_ReportsHtm025()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p><a href=\"zzz://example.org/page\">go</a></p>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Messages(MessageId.HTM_025).Should().ContainSingle().Which.Location.Context.Should().Be("zzz://example.org/page");
    }

    [Fact]
    public void Check_ImageAndObjectAndLinkedStyleSheet_AreRegisteredWithTheirTypes()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document(
            "<p><img src=\"a.png\" alt=\"\"/><object data=\"b.swf\" type=\"application/x-shockwave-flash\"></object></p>",
            "<link rel=\"stylesheet\" type=\"text/css\" href=\"s.css\"/>")
            .With("a.png", "image/png").With("b.swf", "application/x-shockwave-flash").With("s.css", "text/css");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Select(reference => (reference.Type, System.IO.Path.GetFileName(reference.Url.Path))).Should().BeEquivalentTo(
        [
            (ReferenceType.Stylesheet, "s.css"),
            (ReferenceType.Image, "a.png"),
            (ReferenceType.Generic, "b.swf"),
        ]);
    }

    [Fact]
    public void Check_Ids_AreRegisteredInTheResourceRegistry()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p id=\"first\">a</p><p id=\"second\">b</p>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        Resource resource = harness.Context.ResourceRegistry!.GetResource(harness.Context.Url)!;
        harness.Context.ResourceRegistry.GetIdPosition("first", resource).Should().Be(1);
        harness.Context.ResourceRegistry.GetIdPosition("second", resource).Should().Be(2);
        harness.Context.ResourceRegistry.GetIdType("first", resource).Should().Be(ReferenceType.Generic);
    }

    [Fact]
    public void Check_TableWithoutHeadersHeadAndCaption_ReportsTheAccessibilityHints()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<table><tr><td>x</td></tr></table>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().Contain([MessageId.ACC_005, MessageId.ACC_006, MessageId.ACC_012]);
    }

    [Fact]
    public void Check_TableWithHeadersHeadAndCaption_ReportsNoAccessibilityHint()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<table><caption>c</caption><thead><tr><th>h</th></tr></thead><tbody><tr><td>x</td></tr></tbody></table>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().NotContain([MessageId.ACC_005, MessageId.ACC_006, MessageId.ACC_012]);
    }

    [Fact]
    public void Check_NestedTables_AreReportedForTheOutermostOnly()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<table><tr><td><table><tr><td>x</td></tr></table></td></tr></table>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Messages(MessageId.ACC_005).Should().ContainSingle();
    }

    [Fact]
    public void Check_Script_ReportsTheFeatureWhatEverTheVersion()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p>x</p>", "<script type=\"text/javascript\">var a;</script>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Context.FeatureReport.HasFeature(FeatureKind.HasScripts).Should().BeTrue();
    }

    [Fact]
    public void Check_ScriptOfAnotherType_IsNotScripting()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p>x</p>", "<script type=\"application/json\">{}</script>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Context.FeatureReport.HasFeature(FeatureKind.HasScripts).Should().BeFalse();
    }

    [Fact]
    public void Check_CharactersCount_IsReportedAtTheEndOfTheDocument()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p>abc</p>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Report.Features.Should().Contain(feature => feature.Feature == FeatureKind.CharsCount);
    }

    [Fact]
    public void Check_Epub2Document_IsNotAskedForEpubTypes()
    {
        // Arrange
        ContentDocumentHarness harness = Epub2Document("<p>x</p>");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().NotContain(MessageId.ACC_007);
    }

    [Fact]
    public void Check_Svg_RegistersSymbolsPaintsAndImages()
    {
        // Arrange
        string svg = $"<svg xmlns=\"{SvgNamespace}\" xmlns:xlink=\"{XlinkNamespace}\" version=\"1.1\" width=\"10\" height=\"10\">"
            + "<defs><symbol id=\"sym\"><rect width=\"1\" height=\"1\"/></symbol><linearGradient id=\"grad\"/></defs>"
            + "<use xlink:href=\"#sym\"/><image xlink:href=\"a.png\" width=\"1\" height=\"1\"/><rect fill=\"url(#grad)\" width=\"1\" height=\"1\"/></svg>";
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", svg, ContentDocumentHarness.Svg) { Version = EpubVersion.Version2 }.With("a.png", "image/png");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(ReferenceType.SvgSymbol, ReferenceType.Image, ReferenceType.SvgPaint);
        Resource resource = harness.Context.ResourceRegistry!.GetResource(harness.Context.Url)!;
        harness.Context.ResourceRegistry.GetIdType("sym", resource).Should().Be(ReferenceType.SvgSymbol);
        harness.Context.ResourceRegistry.GetIdType("grad", resource).Should().Be(ReferenceType.SvgPaint);
    }

    [Theory]
    [InlineData("url()")]
    [InlineData("url(')")]
    [InlineData("url('')")]
    public void Check_SvgPaintWithAnEmptyUrl_DoesNotFail(string paint)
    {
        // Arrange
        string svg = $"<svg xmlns=\"{SvgNamespace}\" version=\"1.1\" width=\"10\" height=\"10\"><rect fill=\"{paint}\" width=\"1\" height=\"1\"/></svg>";
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", svg, ContentDocumentHarness.Svg) { Version = EpubVersion.Version2 };
        harness.Build();

        // Act
        System.Action act = () => new OPSChecker(harness.Context).Check();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Check_SvgPaintWithQuotedUrl_RegistersTheUrlWithoutTheQuotes()
    {
        // Arrange
        string svg = $"<svg xmlns=\"{SvgNamespace}\" version=\"1.1\" width=\"10\" height=\"10\"><rect fill=\"url('#grad')\" width=\"1\" height=\"1\"/><rect clip-path=\"url(&quot;#clip&quot;)\" width=\"1\" height=\"1\"/></svg>";
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", svg, ContentDocumentHarness.Svg) { Version = EpubVersion.Version2 };
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Select(reference => (reference.Type, reference.Url.Fragment)).Should().Equal(
            (ReferenceType.SvgPaint, "grad"), (ReferenceType.SvgClipPath, "clip"));
    }

    [Fact]
    public void Check_XmlStylesheetInSvg_RegistersTheStyleSheet()
    {
        // Arrange
        string svg = $"<?xml-stylesheet type=\"text/css\" href=\"s.css\"?><svg xmlns=\"{SvgNamespace}\" version=\"1.1\" width=\"10\" height=\"10\"/>";
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", svg, ContentDocumentHarness.Svg) { Version = EpubVersion.Version2 }.With("s.css", "text/css");
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Should().ContainSingle().Which.Type.Should().Be(ReferenceType.Stylesheet);
    }

    [Fact]
    public void Check_XmlStylesheetOfAnotherType_IsIgnored()
    {
        // Arrange
        string svg = $"<?xml-stylesheet type=\"text/xsl\" href=\"s.xsl\"?><svg xmlns=\"{SvgNamespace}\" version=\"1.1\" width=\"10\" height=\"10\"/>";
        ContentDocumentHarness harness = new ContentDocumentHarness("image.svg", svg, ContentDocumentHarness.Svg) { Version = EpubVersion.Version2 };
        harness.Build();

        // Act
        new OPSChecker(harness.Context).Check();

        // Assert
        harness.References.Should().BeEmpty();
    }

    private static ContentDocumentHarness Epub2Document(string body, string head = "") =>
        new(
            "chapter.xhtml",
            "<?xml version=\"1.0\"?><!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">"
            + $"<html xmlns=\"{ContentDocumentHarness.XhtmlNamespace}\" xml:lang=\"en\"><head><title>T</title>{head}</head><body>{body}</body></html>")
        {
            Version = EpubVersion.Version2,
        };
}
