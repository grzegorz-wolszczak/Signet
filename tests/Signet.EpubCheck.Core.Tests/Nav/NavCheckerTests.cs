using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Nav;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Nav;

/// <summary>The navigation document: the kinds of nav, their links, and the features they stand for.</summary>
public sealed class NavCheckerTests
{
    [Fact]
    public void Constructor_ResourceThatIsNotXhtml_Fails()
    {
        // Arrange
        ContentDocumentHarness harness = new("nav.svg", "<svg/>", ContentDocumentHarness.Svg);
        harness.Build();

        // Act & Assert
        FluentActions.Invoking(() => new NavChecker(harness.Context)).Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Constructor_NavigationDocumentOfAnEpub2_ReportsNav001()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"toc\"><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav>");
        harness.Version = EpubVersion.Version2;
        harness.Build();

        // Act
        _ = new NavChecker(harness.Context);

        // Assert
        harness.Ids.Should().Equal(MessageId.NAV_001);
    }

    [Fact]
    public void Check_TocLinks_AreRegisteredAsTocLinksAndReportedAsAFeature()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"toc\"><ol><li><a href=\"c.xhtml#a\">c</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(ReferenceType.Hyperlink, ReferenceType.NavTocLink);
        harness.Context.FeatureReport.HasFeature(FeatureKind.TocLinks).Should().BeTrue();
    }

    [Fact]
    public void Check_PageListLinks_AreRegisteredAsPageListLinks()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"page-list\"><ol><li><a href=\"c.xhtml#p1\">1</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(ReferenceType.Hyperlink, ReferenceType.NavPagelistLink);
        harness.Context.FeatureReport.HasFeature(FeatureKind.PageList).Should().BeTrue();
        harness.Context.FeatureReport.HasFeature(FeatureKind.TocLinks).Should().BeFalse();
    }

    [Theory]
    [InlineData("toc", "toc")]
    [InlineData("page-list", "page-list")]
    [InlineData("landmarks", "landmarks")]
    public void Check_RemoteLinkOfAReadingNav_ReportsNav010(string type, string text)
    {
        // Arrange
        ContentDocumentHarness harness = Navigation($"<nav epub:type=\"{type}\"><ol><li><a href=\"https://example.org/c\">c</a></li></ol></nav>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.NAV_010).Should().ContainSingle().Which.Arguments.Should().Equal(text, "https://example.org/c");
        harness.References.Should().NotContain(reference => reference.Type == ReferenceType.NavTocLink || reference.Type == ReferenceType.NavPagelistLink);
    }

    [Fact]
    public void Check_LandmarksLinks_AreNotRegisteredForReadingOrder()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"landmarks\"><ol><li><a epub:type=\"bodymatter\" href=\"c.xhtml\">c</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(ReferenceType.Hyperlink);
    }

    [Fact]
    public void Check_LinksOutsideANavElement_AreOnlyHyperlinks()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"toc\"><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav><p><a href=\"c.xhtml\">again</a></p>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(ReferenceType.Hyperlink, ReferenceType.NavTocLink, ReferenceType.Hyperlink);
    }

    [Theory]
    [InlineData("loi", FeatureKind.Loi)]
    [InlineData("lot", FeatureKind.Lot)]
    [InlineData("loa", FeatureKind.Loa)]
    [InlineData("lov", FeatureKind.Lov)]
    public void Check_ListsOfFiguresTablesAudioAndVideo_AreReportedAsFeatures(string type, FeatureKind expected)
    {
        // Arrange
        ContentDocumentHarness harness = Navigation($"<nav epub:type=\"toc\"><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav><nav epub:type=\"{type}\"><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.Context.FeatureReport.HasFeature(expected).Should().BeTrue();
    }

    [Fact]
    public void Check_EpubTypeOutsideANav_DoesNotChangeTheKindOfNav()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"toc\"><ol><li><a epub:type=\"loi\" href=\"c.xhtml\">c</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.Context.FeatureReport.HasFeature(FeatureKind.Loi).Should().BeFalse();
    }

    [Fact]
    public void Check_NavigationDocumentWithoutAToc_ReportsASchemaError()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"landmarks\"><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_005);
    }

    [Fact]
    public void Check_ValidNavigationDocument_ReportsNothing()
    {
        // Arrange
        ContentDocumentHarness harness = Navigation("<nav epub:type=\"toc\"><h1>Contents</h1><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav>").With("c.xhtml", ContentDocumentHarness.Xhtml);
        harness.WithItemProperty(PackageVocabs.ItemProperty.Nav);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new NavChecker(harness.Context).Check();
    }

    private static ContentDocumentHarness Navigation(string body) => new("nav.xhtml", ContentDocumentHarness.Html(body));
}
