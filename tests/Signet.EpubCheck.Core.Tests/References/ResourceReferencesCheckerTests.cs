using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.References;

/// <summary>The checks of the references between the resources of a publication.</summary>
public sealed class ResourceReferencesCheckerTests
{
    private static readonly EpubLocation s_from = new("chapter.xhtml", 3, 4);

    [Fact]
    public void Constructor_WithoutAContainer_Fails()
    {
        // Arrange
        ValidationContext context = new() { Url = Url.Parse("file:///a.xhtml"), Report = new CollectingReport() };

        // Act & Assert
        FluentActions.Invoking(() => new ResourceReferencesChecker(context)).Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void Check_ReferenceToAMissingLocalResource_ReportsRsc007()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();

        // Act
        Check(harness, harness.Local("missing.png"), ReferenceType.Image);

        // Assert
        harness.Messages(MessageId.RSC_007).Should().ContainSingle().Which.Location.Should().Be(s_from);
    }

    [Theory]
    [InlineData(EpubVersion.Version3, MessageId.RSC_007w)]
    [InlineData(EpubVersion.Version2, MessageId.RSC_007)]
    public void Check_LinkToAMissingResource_IsAWarningInEpub3Only(EpubVersion version, MessageId expected)
    {
        // Arrange
        ContentDocumentHarness harness = Harness();
        harness.Version = version;

        // Act
        Check(harness, harness.Local("missing.xml"), ReferenceType.Link);

        // Assert
        harness.Ids.Should().Equal(expected);
    }

    [Fact]
    public void Check_ResourceInTheContainerThatTheManifestDoesNotDeclare_IsReportedOnce()
    {
        // Arrange: the file is in the container but not registered
        ContentDocumentHarness harness = Harness();
        harness.UndeclaredFile("loose.png");

        // Act
        Check(harness, harness.Local("loose.png"), ReferenceType.Image, ReferenceType.Image);

        // Assert
        harness.Messages(MessageId.RSC_008).Should().ContainSingle();
    }

    [Fact]
    public void Check_RemoteResourceThatIsNotAllowed_ReportsRsc006()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();

        // Act
        Check(harness, Url.Parse("https://example.org/a.png"), ReferenceType.Image);

        // Assert
        harness.Ids.Should().Equal(MessageId.RSC_006);
    }

    [Theory]
    [InlineData(ReferenceType.Hyperlink)]
    [InlineData(ReferenceType.Cite)]
    [InlineData(ReferenceType.Link)]
    [InlineData(ReferenceType.Font)]
    [InlineData(ReferenceType.Audio)]
    [InlineData(ReferenceType.Video)]
    public void Check_RemoteReferenceOfAnAllowedKind_ReportsNoRsc006(ReferenceType type)
    {
        // Arrange
        ContentDocumentHarness harness = Harness();

        // Act
        Check(harness, Url.Parse("https://example.org/a"), type);

        // Assert
        harness.Ids.Should().NotContain(MessageId.RSC_006);
    }

    [Fact]
    public void Check_RemoteFontInEpub2_ReportsRsc006()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();
        harness.Version = EpubVersion.Version2;

        // Act
        Check(harness, Url.Parse("https://example.org/a.otf"), ReferenceType.Font);

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_006);
    }

    [Fact]
    public void Check_RemoteAudioWithoutHttps_ReportsRsc031()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();

        // Act
        Check(harness, Url.Parse("http://example.org/a.mp3"), ReferenceType.Audio);

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_031);
    }

    [Fact]
    public void Check_HyperlinkToAResourceThatIsNotAContentDocument_ReportsRsc010()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("a.png", "image/png");

        // Act
        Check(harness, harness.Local("a.png"), ReferenceType.Hyperlink);

        // Assert
        harness.Ids.Should().Equal(MessageId.RSC_010);
    }

    [Fact]
    public void Check_HyperlinkToAContentDocumentOutsideTheSpine_ReportsRsc011()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("other.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness, harness.Local("other.xhtml"), ReferenceType.Hyperlink);

        // Assert
        harness.Ids.Should().Equal(MessageId.RSC_011);
    }

    [Fact]
    public void Check_HyperlinkToASpineItem_IsFine()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();

        // Act
        Check(harness, harness.Local("chapter.xhtml"), ReferenceType.Hyperlink);

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Check_HyperlinkToAnEpubCfi_IsNotChecked()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("a.png", "image/png");

        // Act
        Check(harness, harness.Local("a.png#epubcfi(/6/4)"), ReferenceType.Hyperlink);

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Check_ImageFragmentOutsideSvg_ReportsRsc009()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("a.png", "image/png").With("b.svg", ContentDocumentHarness.Svg);

        // Act
        Run(harness, (harness.Local("a.png#frag"), ReferenceType.Image), (harness.Local("b.svg#svgView(viewBox(0,0,1,1))"), ReferenceType.Image));

        // Assert
        harness.Messages(MessageId.RSC_009).Should().ContainSingle();
    }

    [Fact]
    public void Check_StyleSheetWithAFragment_ReportsRsc013()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("s.css", "text/css");

        // Act
        Check(harness, harness.Local("s.css#x"), ReferenceType.Stylesheet);

        // Assert
        harness.Ids.Should().Equal(MessageId.RSC_013);
    }

    [Theory]
    [InlineData("text/css", MessageId.RSC_034)]
    [InlineData("text/x-javascript", MessageId.RSC_035)]
    public void Check_ScriptOfAWrongOrLegacyType_ReportsIt(string mimeType, MessageId expected)
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("s.js", mimeType);

        // Act
        Check(harness, harness.Local("s.js"), ReferenceType.Script);

        // Assert
        harness.Ids.Should().Contain(expected);
    }

    [Theory]
    [InlineData(ReferenceType.SvgPaint)]
    [InlineData(ReferenceType.SvgClipPath)]
    [InlineData(ReferenceType.SvgSymbol)]
    public void Check_SvgReferenceWithoutAFragment_ReportsRsc015(ReferenceType type)
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("b.svg", ContentDocumentHarness.Svg);

        // Act
        Check(harness, harness.Local("b.svg"), type);

        // Assert
        harness.Ids.Should().Equal(MessageId.RSC_015);
    }

    [Fact]
    public void Check_FragmentThatNamesNoId_ReportsRsc012()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();

        // Act
        Check(harness, harness.Local("chapter.xhtml#nowhere"), ReferenceType.Hyperlink);

        // Assert
        harness.Ids.Should().Equal(MessageId.RSC_012);
    }

    [Fact]
    public void Check_FragmentThatNamesAnId_IsFine()
    {
        // Arrange
        ContentDocumentHarness harness = Harness();
        harness.Context.ResourceRegistry!.RegisterId("here", ReferenceType.Generic, harness.Context.Url);

        // Act
        Check(harness, harness.Local("chapter.xhtml#here"), ReferenceType.Hyperlink);

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ReferenceType.SvgPaint, ReferenceType.SvgClipPath, true)]
    [InlineData(ReferenceType.SvgPaint, ReferenceType.SvgPaint, false)]
    [InlineData(ReferenceType.SvgClipPath, ReferenceType.Generic, true)]
    [InlineData(ReferenceType.SvgSymbol, ReferenceType.Generic, false)]
    [InlineData(ReferenceType.SvgSymbol, ReferenceType.SvgSymbol, false)]
    [InlineData(ReferenceType.SvgSymbol, ReferenceType.SvgPaint, true)]
    [InlineData(ReferenceType.Hyperlink, ReferenceType.SvgPaint, true)]
    [InlineData(ReferenceType.Cite, ReferenceType.Generic, false)]
    public void Check_ReferenceToAnIdOfAnotherKind_ReportsRsc014(ReferenceType reference, ReferenceType idKind, bool reported)
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("b.svg", ContentDocumentHarness.Svg).WithItem("b.svg", item => item.SpinePosition = 1);
        harness.Context.ResourceRegistry!.RegisterId("x", idKind, harness.Local("b.svg").WithFragment(null));

        // Act
        Check(harness, harness.Local("b.svg#x"), reference);

        // Assert
        harness.Ids.Contains(MessageId.RSC_014).Should().Be(reported);
    }

    [Theory]
    [InlineData("image/bmp", false, false, true)]
    [InlineData("image/bmp", true, false, false)]
    [InlineData("image/bmp", false, true, false)]
    [InlineData("image/png", false, false, false)]
    public void Check_ForeignResource_NeedsAFallback(string mimeType, bool intrinsic, bool coreFallback, bool reported)
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("a.img", mimeType).WithItem("a.img", item => item.HasCoreMediaTypeFallback = coreFallback);

        // Act
        Check(harness, harness.Local("a.img"), ReferenceType.Image, hasIntrinsicFallback: intrinsic);

        // Assert
        harness.Ids.Contains(MessageId.RSC_032).Should().Be(reported);
    }

    [Fact]
    public void Check_PublicationResourceWithAManifestFallback_ReportsObs001()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("a.png", "image/png").WithItem("a.png", item => item.Fallback = "other");

        // Act
        Check(harness, harness.Local("a.png"), ReferenceType.Image);

        // Assert
        harness.Messages(MessageId.OBS_001).Should().ContainSingle().Which.Arguments.Should().Equal("content-fallback", "a.png");
    }

    [Fact]
    public void Check_RegionBasedLink_NeedsAFixedLayoutItem()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("fxl.xhtml", ContentDocumentHarness.Xhtml).WithItem("fxl.xhtml", item => item.FixedLayout = true)
            .With("flow.xhtml", ContentDocumentHarness.Xhtml).WithItem("flow.xhtml", item => item.SpinePosition = 2);

        // Act
        Check(harness, harness.Local("fxl.xhtml"), ReferenceType.RegionBasedNav);
        Check(harness, harness.Local("flow.xhtml"), ReferenceType.RegionBasedNav);

        // Assert
        harness.Messages(MessageId.NAV_009).Should().ContainSingle();
    }

    [Fact]
    public void Check_TocLinksOutOfSpineOrder_ReportNav011()
    {
        // Arrange
        ContentDocumentHarness harness = Harness()
            .With("c1.xhtml", ContentDocumentHarness.Xhtml).WithItem("c1.xhtml", item => item.SpinePosition = 1)
            .With("c2.xhtml", ContentDocumentHarness.Xhtml).WithItem("c2.xhtml", item => item.SpinePosition = 2);

        // Act
        Run(harness, (harness.Local("c2.xhtml"), ReferenceType.NavTocLink), (harness.Local("c1.xhtml"), ReferenceType.NavTocLink));

        // Assert
        harness.Messages(MessageId.NAV_011).Should().ContainSingle().Which.Arguments[0].Should().Be("toc");
    }

    [Fact]
    public void Check_TocLinksOutOfDocumentOrder_ReportNav011()
    {
        // Arrange
        ContentDocumentHarness harness = Harness()
            .With("c1.xhtml", ContentDocumentHarness.Xhtml).WithItem("c1.xhtml", item => item.SpinePosition = 1);
        Url c1 = harness.Local("c1.xhtml");
        harness.Context.ResourceRegistry!.RegisterId("a", ReferenceType.Generic, c1);
        harness.Context.ResourceRegistry.RegisterId("b", ReferenceType.Generic, c1);

        // Act
        Run(harness, (c1.WithFragment("b"), ReferenceType.NavTocLink), (c1.WithFragment("a"), ReferenceType.NavTocLink));

        // Assert
        harness.Messages(MessageId.NAV_011).Should().ContainSingle();
    }

    [Fact]
    public void Check_TocLinksInOrder_ReportNothing()
    {
        // Arrange
        ContentDocumentHarness harness = Harness()
            .With("c1.xhtml", ContentDocumentHarness.Xhtml).WithItem("c1.xhtml", item => item.SpinePosition = 1)
            .With("c2.xhtml", ContentDocumentHarness.Xhtml).WithItem("c2.xhtml", item => item.SpinePosition = 2);

        // Act
        Run(harness, (harness.Local("c1.xhtml"), ReferenceType.NavTocLink), (harness.Local("c2.xhtml"), ReferenceType.NavTocLink));

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Check_OverlayLinks_AreCheckedAsReferencesAndForOrder()
    {
        // Arrange
        ContentDocumentHarness harness = Harness()
            .With("c1.xhtml", ContentDocumentHarness.Xhtml).WithItem("c1.xhtml", item => item.SpinePosition = 1)
            .With("c2.xhtml", ContentDocumentHarness.Xhtml).WithItem("c2.xhtml", item => item.SpinePosition = 2)
            .With("img.png", "image/png");

        // Act
        Run(
            harness,
            (harness.Local("c2.xhtml#a"), ReferenceType.OverlayTextLink),
            (harness.Local("c1.xhtml"), ReferenceType.OverlayTextLink),
            (harness.Local("img.png"), ReferenceType.OverlayTextLink));

        // Assert
        harness.Ids.Should().Contain([MessageId.MED_015, MessageId.RSC_010, MessageId.RSC_012]);
    }

    [Fact]
    public void Check_EveryReference_IsReportedAsAResource()
    {
        // Arrange
        ContentDocumentHarness harness = Harness().With("a.png", "image/png");

        // Act
        Check(harness, harness.Local("a.png"), ReferenceType.Image);

        // Assert
        harness.Report.Features.Should().ContainSingle(feature => feature.Feature == FeatureKind.Resource).Which.Value.Should().Be("a.png");
    }

    private static ContentDocumentHarness Harness() => new("chapter.xhtml", ContentDocumentHarness.Html("<p>x</p>"));

    private static void Check(ContentDocumentHarness harness, Url url, ReferenceType type, ReferenceType? second = null, bool hasIntrinsicFallback = false)
    {
        Register(harness, url, type, hasIntrinsicFallback);
        if (second is { } other)
        {
            Register(harness, url, other, hasIntrinsicFallback);
        }

        new ResourceReferencesChecker(harness.Context).Check();
    }

    private static void Run(ContentDocumentHarness harness, params (Url Url, ReferenceType Type)[] references)
    {
        foreach ((Url url, ReferenceType type) in references)
        {
            Register(harness, url, type);
        }

        new ResourceReferencesChecker(harness.Context).Check();
    }

    private static void Register(ContentDocumentHarness harness, Url url, ReferenceType type, bool hasIntrinsicFallback = false) =>
        harness.Context.ReferenceRegistry!.RegisterReference(url, type, s_from, hasIntrinsicFallback);
}
