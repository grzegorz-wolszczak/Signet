using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Css;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Css;

/// <summary>What the checker of CSS reports and registers, for a style sheet and for the CSS inside a content document.</summary>
public sealed class CSSCheckerTests
{
    private const string Css = "text/css";

    [Fact]
    public void Check_ImportAndUrls_AreRegisteredAsReferences()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("@import url(other.css);\n@import 'more.css';\np { background: url(a.png) }").With("other.css", Css).With("more.css", Css).With("a.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => (System.IO.Path.GetFileName(reference.Url.Path), reference.Type)).Should().Equal(
            ("other.css", ReferenceType.Stylesheet), ("more.css", ReferenceType.Stylesheet), ("a.png", ReferenceType.Generic));
    }

    [Fact]
    public void Check_FragmentOnlyUrl_IsNotAReference()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p { fill: url(#paint) }");

        // Act
        Check(harness);

        // Assert
        harness.References.Should().BeEmpty();
        harness.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Check_EmptyUrl_ReportsCss002()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p { background: url('') }");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.CSS_002).Should().ContainSingle();
    }

    [Fact]
    public void Check_FontFace_RegistersTheFontAndReportsTheFeature()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("@font-face { font-family: 'My Font'; font-weight: bold; src: url(f.woff) }").With("f.woff", "font/woff");

        // Act
        Check(harness);

        // Assert
        harness.References.Should().ContainSingle().Which.Type.Should().Be(ReferenceType.Font);
        harness.Report.Features.Should().Contain(feature => feature.Feature == FeatureKind.FontEmbedded && feature.Value == "My Font,bold");
        harness.Ids.Should().Contain(MessageId.CSS_028);
    }

    [Fact]
    public void Check_FontFaceWithoutDeclarations_ReportsCss019()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("@font-face { }");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.CSS_019);
    }

    [Fact]
    public void Check_FontOfANonCoreType_ReportsCss007()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("@font-face { font-family: X; src: url(f.xyz) }").With("f.xyz", "application/x-unknown-font");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.CSS_007);
    }

    [Fact]
    public void Check_RemoteResource_NeedsTheRemoteResourcesProperty()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p { background: url(https://example.org/a.png) }");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_014).Should().ContainSingle().Which.Arguments.Should().Equal("remote-resources");
    }

    [Fact]
    public void Check_RemoteResourcesPropertyNotNeededByTheSheet_ReportsOpf018()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p { color: red }").WithItemProperty(PackageVocabs.ItemProperty.RemoteResources);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.OPF_018);
    }

    [Fact]
    public void Check_StyleElementWithTheRemoteResourcesProperty_ReportsNoOpf018()
    {
        // Arrange: the CSS of a style element is only a part of the document
        ContentDocumentHarness harness = new ContentDocumentHarness("chapter.xhtml", ContentDocumentHarness.Html("<p>x</p>")).WithItemProperty(PackageVocabs.ItemProperty.RemoteResources);
        harness.Build();

        // Act
        new CSSChecker(harness.Context, "p { color: red }", 3, isStyleAttribute: false).Check();

        // Assert
        harness.Ids.Should().NotContain(MessageId.OPF_018);
    }

    [Theory]
    [InlineData("p { position: fixed }", MessageId.CSS_006)]
    [InlineData("p { direction: rtl }", MessageId.CSS_001)]
    [InlineData("p { unicode-bidi: embed }", MessageId.CSS_001)]
    [InlineData("p { -epub-hyphens: auto }", MessageId.OBS_001)]
    [InlineData("p { text-transform: -epub-fullwidth }", MessageId.OBS_001)]
    public void Check_RulesOfEpub_AreReported(string css, MessageId expected)
    {
        // Arrange
        ContentDocumentHarness harness = Sheet(css);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(expected);
    }

    [Fact]
    public void Check_DirectionInEpub2_IsAllowed()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p { direction: rtl }");
        harness.Version = EpubVersion.Version2;

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain(MessageId.CSS_001);
    }

    [Fact]
    public void Check_SyntaxError_ReportsCss008AtTheLocation()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p {\n  color red;\n}");

        // Act
        Check(harness);

        // Assert
        ReportedMessage message = harness.Messages(MessageId.CSS_008).Should().ContainSingle().Subject;
        message.Location.Line.Should().Be(2);
    }

    [Fact]
    public void Check_KeyframePercentages_AreNotErrors()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("@keyframes k { 0% { top: 0 } 100% { top: 5px } }");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain(MessageId.CSS_008);
    }

    [Theory]
    [InlineData("@charset \"iso-8859-1\"; p { }", MessageId.CSS_004)]
    [InlineData("@charset \"utf-8\"; p { }", null)]
    public void Check_CharsetOtherThanUtf8_IsReported(string css, MessageId? expected)
    {
        // Arrange
        ContentDocumentHarness harness = Sheet(css);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Contains(MessageId.CSS_004).Should().Be(expected is not null);
    }

    [Fact]
    public void Check_Utf16Sheet_ReportsCss003()
    {
        // Arrange
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("p { color: red }")];
        ContentDocumentHarness harness = new("s.css", bytes, Css);

        // Act
        harness.Build();
        new CSSChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().Contain(MessageId.CSS_003);
    }

    [Fact]
    public void Check_StyleAttribute_ReportsDeclarationsWithTheLineOfTheHost()
    {
        // Arrange
        ContentDocumentHarness harness = new("chapter.xhtml", ContentDocumentHarness.Html("<p>x</p>"));
        harness.Build();

        // Act
        new CSSChecker(harness.Context, "position: fixed", 7, isStyleAttribute: true).Check();

        // Assert
        harness.Messages(MessageId.CSS_006).Should().ContainSingle().Which.Location.Line.Should().Be(7);
    }

    [Fact]
    public void Check_StyleElementAndAttributeInAContentDocument_AreChecked()
    {
        // Arrange
        ContentDocumentHarness harness = new("chapter.xhtml", ContentDocumentHarness.Html("<p style=\"position: fixed\">x</p>", "<style>p { direction: rtl }</style>"));

        // Act
        harness.Build();
        new Core.Ops.OPSChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().Contain([MessageId.CSS_006, MessageId.CSS_001]);
    }

    [Fact]
    public void Check_StyleSheetThatIsNotInTheContainer_ReportsRsc001()
    {
        // Arrange
        ContentDocumentHarness harness = Sheet("p { }");
        harness.Build();

        // Act
        new CSSChecker(harness.Context with { Url = harness.Local("missing.css") }).Check();

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_001);
    }

    private static ContentDocumentHarness Sheet(string css) => new("s.css", css, Css);

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new CSSChecker(harness.Context).Check();
    }
}
