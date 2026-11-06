using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Opf;

/// <summary>The checks of an EPUB 3 package document: properties, links, metadata, collections and item types.</summary>
public sealed class OPFChecker30Tests
{
    private const string Header = "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"uid\" xml:lang=\"en\">";
    private const string Metadata =
        "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>T</dc:title><dc:language>en</dc:language>"
        + "<dc:identifier id=\"uid\">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier>"
        + "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>";

    [Fact]
    public void Check_ValidPackage_ReportsNothing()
    {
        // Arrange
        string package = Package("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>", "<spine><itemref idref=\"nav\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<item id=\"i\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\" properties=\"cover-image\"/>", MessageId.OPF_012)]
    [InlineData("<item id=\"i\" href=\"a.png\" media-type=\"image/png\" properties=\"nav\"/>", MessageId.OPF_012)]
    [InlineData("<item id=\"i\" href=\"data:text/plain,x\" media-type=\"text/plain\"/>", MessageId.RSC_029)]
    [InlineData("<item id=\"i\" href=\"a.ttf\" media-type=\"application/x-font-ttf\"/>", MessageId.OPF_090)]
    [InlineData("<item id=\"i\" href=\"a.js\" media-type=\"text/javascript\"/>", MessageId.OPF_090)]
    [InlineData("<item id=\"i\" href=\"a.xhtml#frag\" media-type=\"application/xhtml+xml\"/>", MessageId.OPF_091)]
    public void Check_ItemProblem_IsReported(string item, MessageId expected)
    {
        // Act
        List<ReportedMessage> messages = Check(Package(item, "<spine/>"));

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(expected);
    }

    [Fact]
    public void Check_SpineItemThatIsNotAContentDocumentAndHasNoFallback_ReportsOpf043()
    {
        // Arrange
        string package = Package("<item id=\"i\" href=\"a.pdf\" media-type=\"application/pdf\"/>", "<spine><itemref idref=\"i\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_043);
    }

    [Fact]
    public void Check_SpineItemWithTheDataNavProperty_ReportsOpf077()
    {
        // Arrange
        string package = Package("<item id=\"i\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\" properties=\"data-nav\"/>", "<spine><itemref idref=\"i\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_077);
    }

    [Fact]
    public void Check_SpreadPropertyOnAnItemThatIsNotFixedLayout_ReportsOpf100()
    {
        // Arrange
        string package = Package("<item id=\"i\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/>",
            "<spine><itemref idref=\"i\" properties=\"page-spread-left\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_100).Which.Arguments[0].Should().Be("page-spread-left");
    }

    [Fact]
    public void Check_FixedLayoutPublication_IsReportedAsFacts()
    {
        // Arrange
        string metadata = Metadata.Replace("</metadata>", "<meta property=\"rendition:layout\">pre-paginated</meta></metadata>");
        string package = Header + metadata + "<manifest><item id=\"i\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"i\"/></spine></package>";

        // Act
        CollectingReport report = Run(package, out _);

        // Assert
        report.Features.Should().Contain(f => f.Feature == FeatureKind.RenditionLayout && f.Value == "pre-paginated");
        report.Features.Should().Contain(f => f.Feature == FeatureKind.HasFixedLayout && f.Value == "true");
    }

    [Fact]
    public void Check_RenditionSpreadPortrait_ReportsOpf086()
    {
        // Arrange
        string metadata = Metadata.Replace("</metadata>", "<meta property=\"rendition:spread\">portrait</meta></metadata>");
        string package = Header + metadata + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_086).Which.Arguments[0].Should().Be("rendition:spread portrait");
    }

    [Theory]
    [InlineData("<link rel=\"record\" href=\"rec.xml\"/>", MessageId.OPF_093)]
    [InlineData("<link rel=\"alternate record\" href=\"https://example.org/r.xml\"/>", MessageId.OPF_089)]
    [InlineData("<link rel=\"voicing\" href=\"https://example.org/r.xml\"/>", MessageId.OPF_094)]
    [InlineData("<link rel=\"voicing\" href=\"https://example.org/r.xml\" media-type=\"text/plain\"/>", MessageId.OPF_095)]
    [InlineData("<link rel=\"record\" href=\"package.opf#x\" media-type=\"application/xml\"/>", MessageId.OPF_098)]
    [InlineData("<link rel=\"record\" href=\"data:text/plain,x\" media-type=\"text/plain\"/>", MessageId.RSC_029)]
    [InlineData("<link rel=\"record\" href=\"https://example.org/r.xml\" media-type=\"text/plain\" hreflang=\"e\"/>", MessageId.OPF_092)]
    public void Check_LinkProblem_IsReported(string link, MessageId expected)
    {
        // Arrange
        string metadata = Metadata.Replace("</metadata>", link + "</metadata>");
        string package = Header + metadata + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(expected);
    }

    [Fact]
    public void Check_LinkToAnItemThatIsNotInTheSpine_ReportsOpf067()
    {
        // Arrange
        string metadata = Metadata.Replace("</metadata>", "<link rel=\"record\" href=\"rec.xml\" media-type=\"application/xml\"/></metadata>");
        string package = Header + metadata + "<manifest><item id=\"r\" href=\"rec.xml\" media-type=\"application/xml\"/></manifest><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_067).Which.Arguments[0].Should().Be("rec.xml");
    }

    [Theory]
    [InlineData("xml:lang=\"en\"", false)]
    [InlineData("xml:lang=\"e n\"", true)]
    [InlineData("xml:lang=\"en-\"", true)]
    public void Check_LanguageTagOfAnElement_MustBeWellFormed(string attribute, bool reported)
    {
        // Arrange
        string package = Header + Metadata.Replace("<dc:title>", "<dc:title " + attribute + ">") + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Any(m => m.Message.Id == MessageId.OPF_092).Should().Be(reported);
    }

    [Fact]
    public void Check_DcLanguageThatIsNotWellFormed_ReportsOpf092WithTheReason()
    {
        // Arrange
        string package = Header + Metadata.Replace("<dc:language>en</dc:language>", "<dc:language>en_US</dc:language>") + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_092).Which.Arguments.Should().Equal("en_US", "Invalid subtag: en_US [at index 0]");
    }

    [Fact]
    public void Check_RefinementCycle_ReportsOpf065()
    {
        // Arrange
        string metadata = Metadata.Replace("</metadata>", "<meta id=\"a\" property=\"role\" refines=\"#b\">x</meta><meta id=\"b\" property=\"role\" refines=\"#a\">y</meta></metadata>");
        string package = Header + metadata + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_065);
    }

    [Theory]
    [InlineData("<meta name=\"cover\" content=\"x\"/>", MessageId.OBS_001)]
    public void Check_Epub2Constructs_AreReportedAsObsolete(string element, MessageId expected)
    {
        // Arrange
        string package = Header + Metadata.Replace("</metadata>", element + "</metadata>") + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(expected);
    }

    [Fact]
    public void Check_GuideElement_IsReportedAsObsolete()
    {
        // Arrange
        string package = Package(string.Empty, "<spine/>") .Replace("</package>", "<guide/></package>");

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OBS_001);
    }

    [Fact]
    public void Check_CollectionsOfKnownRoles_AreCheckedAgainstTheManifest()
    {
        // Arrange
        string collections =
            "<collection role=\"dictionary\"><link href=\"gone.xhtml\"/><link href=\"a.xhtml\"/></collection>"
            + "<collection role=\"index\"><link href=\"gone.xhtml\"/><collection role=\"index-group\"><link href=\"gone2.xhtml\"/></collection></collection>"
            + "<collection role=\"preview\"><link href=\"gone.xhtml\"/><link href=\"a.xhtml#epubcfi(/6/2)\"/></collection>"
            + "<collection role=\"bad role\" ><link href=\"a.xhtml\"/></collection>";
        string package = Package("<item id=\"a\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/>", "<spine/>").Replace("</package>", collections + "</package>");

        // Act
        List<ReportedMessage> messages = Check(package);

        // Assert
        List<MessageId> ids = [.. messages.Select(m => m.Message.Id)];
        ids.Should().Contain([MessageId.OPF_081, MessageId.OPF_083, MessageId.OPF_071, MessageId.OPF_075, MessageId.OPF_076]);
        ids.Count(id => id == MessageId.OPF_071).Should().Be(2);
    }

    [Fact]
    public void Check_ItemsInAnIndexCollection_GetTheEpubCheckProperty()
    {
        // Arrange
        string collections = "<collection role=\"index\"><link href=\"a.xhtml\"/></collection>";
        string package = Package("<item id=\"a\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/>", "<spine/>").Replace("</package>", collections + "</package>");
        CollectingReport report = new();
        ValidationContext context = ContextFor(package, report);
        OPFChecker30 checker = new(context);

        // Act
        checker.Check();

        // Assert
        OPFHandler30 handler = (OPFHandler30)checker.OpfHandler!;
        handler.GetItemById("a")!.Properties.Select(p => p.PrefixedName).Should().Contain("epubcheck:in-index-collection");
    }

    [Fact]
    public void Check_ProfileOfThePublicationType_AddsItsSchema()
    {
        // Arrange: a dictionary publication needs a dictionary collection (OPF-078 comes with the content checks)
        string package = Package(string.Empty, "<spine/>");
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        ValidationContext context = ContextFor(package, report) with { Profile = EpubProfile.Dict };

        // Act
        new OPFChecker30(context).Check();

        // Assert
        report.Messages.Where(m => m.Message.Id == MessageId.RSC_005).Should().NotBeEmpty("the dictionary schema asks for a dc:type");
    }

    private static string Package(string items, string spine) => Header + Metadata + "<manifest>" + items + "</manifest>" + spine + "</package>";

    private static List<ReportedMessage> Check(string package) => [.. Run(package, out _).Messages.Where(m => m.Message.Id is not (MessageId.RSC_005 or MessageId.RSC_017))];

    private static CollectingReport Run(string package, out ValidationContext context)
    {
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        context = ContextFor(package, report);
        new OPFChecker30(context).Check();
        return report;
    }

    private static ValidationContext ContextFor(string package, CollectingReport report) => new()
    {
        Url = Url.Parse("file:///books/package.opf"),
        Report = report,
        Version = EpubVersion.Version3,
        MimeType = "application/oebps-package+xml",
        ResourceProvider = new Provider(package),
    };

    private sealed class Provider(string content) : IResourceProvider
    {
        public Stream OpenStream(Url url) => new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}
