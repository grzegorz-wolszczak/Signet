using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Opf;

/// <summary>The checks of an EPUB 2 package document: what the handler reports, items, fallbacks, spine and guide.</summary>
public sealed class OPFCheckerTests
{
    private const string Header = "<?xml version=\"1.0\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\" unique-identifier=\"uid\">";
    private const string Metadata =
        "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">"
        + "<dc:title>T</dc:title><dc:language>en</dc:language><dc:identifier id=\"uid\">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier></metadata>";

    [Fact]
    public void Check_ValidPackage_ReportsNothingButItsFacts()
    {
        // Arrange
        string package = Package(items: Item("ch1", "ch1.xhtml", "application/xhtml+xml") + Item("ncx", "toc.ncx", "application/x-dtbncx+xml"),
            spine: "<spine toc=\"ncx\"><itemref idref=\"ch1\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package, out CollectingReport report);

        // Assert
        messages.Should().BeEmpty();
        report.Features.Where(f => f.Feature == FeatureKind.SpineIndex).Select(f => f.Value).Should().Equal("0");
        report.Features.Should().Contain(f => f.Feature == FeatureKind.HasNcx && f.Value == "true");
    }

    [Fact]
    public void Check_SpineItemsThatAreNotLinear_ReportsOpf033()
    {
        // Arrange
        string package = Package(items: Item("ch1", "ch1.xhtml", "application/xhtml+xml"),
            spine: "<spine><itemref idref=\"ch1\" linear=\"no\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_033);
    }

    [Fact]
    public void Check_SpineItemRepeated_ReportsOpf034()
    {
        // Arrange
        string package = Package(items: Item("ch1", "ch1.xhtml", "application/xhtml+xml"),
            spine: "<spine><itemref idref=\"ch1\"/><itemref idref=\"ch1\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_034).Which.Arguments[0].Should().Be("ch1");
    }

    [Theory]
    [InlineData("text/css", MessageId.OPF_042)]
    [InlineData("image/png", MessageId.OPF_042)]
    [InlineData("application/pdf", MessageId.OPF_043)]
    public void Check_SpineItemOfAnUnsuitableType_IsReported(string mediaType, MessageId expected)
    {
        // Arrange
        string package = Package(items: Item("x", "x.bin", mediaType), spine: "<spine><itemref idref=\"x\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(expected);
    }

    [Fact]
    public void Check_SpineItemWithAFallbackThatIsNotAContentDocument_ReportsOpf044()
    {
        // Arrange
        string package = Package(
            items: Item("a", "a.pdf", "application/pdf", "fallback=\"b\"") + Item("b", "b.png", "image/png"),
            spine: "<spine><itemref idref=\"a\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_044).And.NotContain(MessageId.OPF_043);
    }

    [Fact]
    public void Check_SpineItemWithAChainEndingInAContentDocument_IsAccepted()
    {
        // Arrange
        string package = Package(
            items: Item("a", "a.pdf", "application/pdf", "fallback=\"b\"") + Item("b", "b.xhtml", "application/xhtml+xml"),
            spine: "<spine><itemref idref=\"a\"/></spine>");

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Check_FallbackThatDoesNotExist_ReportsOpf040()
    {
        // Arrange
        string package = Package(items: Item("a", "a.pdf", "application/pdf", "fallback=\"nothing\""));

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_040).Which.Arguments[0].Should().Be("nothing");
    }

    [Fact]
    public void Check_FallbackCycle_ReportsOpf045()
    {
        // Arrange
        string package = Package(items: Item("a", "a.pdf", "application/pdf", "fallback=\"b\"") + Item("b", "b.pdf", "application/pdf", "fallback=\"a\""));

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_045);
    }

    [Fact]
    public void Check_FallbackStyleThatDoesNotExist_ReportsOpf041()
    {
        // Arrange
        string package = Package(items: Item("a", "a.xml", "application/x-dtbook+xml", "fallback-style=\"gone\""));

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_041);
    }

    [Fact]
    public void Check_TwoItemsWithTheSameUrl_ReportsOpf074ForTheEarlierOne()
    {
        // Arrange
        string package = Package(items: Item("a", "same.xhtml", "application/xhtml+xml") + Item("b", "same.xhtml", "application/xhtml+xml"));

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_074).Which.Arguments[0].Should().Be("same.xhtml");
    }

    [Fact]
    public void Check_ItemThatIsThePackageDocumentItself_ReportsOpf099()
    {
        // Arrange
        string package = Package(items: Item("self", "package.opf", "application/oebps-package+xml"));

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_099);
    }

    [Fact]
    public void Check_GuideReferences_AreCheckedAgainstTheManifest()
    {
        // Arrange
        string package = Package(
            items: Item("ch1", "ch1.xhtml", "application/xhtml+xml") + Item("img", "c.png", "image/png"),
            guide: "<guide><reference type=\"text\" href=\"ch1.xhtml#a\"/><reference type=\"cover\" href=\"c.png\"/><reference type=\"x\" href=\"gone.xhtml\"/></guide>");

        // Act
        List<ReportedMessage> messages = CheckInContainer(package);

        // Assert
        messages.Select(m => m.Message.Id).Should().Equal(MessageId.OPF_032, MessageId.OPF_031);
    }

    [Fact]
    public void Check_DeprecatedItemTypes_AreReported()
    {
        // Arrange
        string package = Package(items: Item("a", "a.html", "text/html") + Item("b", "b.oeb", "text/x-oeb1-document"));

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Equal(MessageId.OPF_035, MessageId.OPF_037);
    }

    [Theory]
    [InlineData("creator", "<dc:creator opf:role=\"zzz\">A</dc:creator>", MessageId.OPF_052)]
    [InlineData("date", "<dc:date>2000-13-01</dc:date>", MessageId.OPF_054)]
    [InlineData("empty", "<dc:subject> </dc:subject>", MessageId.OPF_072)]
    [InlineData("uuid", "<dc:identifier opf:scheme=\"UUID\" id=\"x\">nope</dc:identifier>", MessageId.OPF_085)]
    public void Check_MetadataProblem_IsReported(string name, string element, MessageId expected)
    {
        // Arrange
        string metadata = Metadata.Replace("</metadata>", element + "</metadata>");
        if (name == "uuid")
        {
            metadata = Metadata.Replace("id=\"uid\"", "id=\"u2\"").Replace("</metadata>", element.Replace("id=\"x\"", "id=\"uid\"") + "</metadata>");
        }

        string package = Header + metadata + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(expected);
    }

    [Fact]
    public void Check_UniqueIdentifierThatIsNotThere_ReportsOpf030InAContainerButNotAlone()
    {
        // Arrange: the identifier element has another id
        string package = Header + Metadata.Replace("id=\"uid\"", "id=\"other\"") + "<manifest/><spine/></package>";

        // Act
        List<ReportedMessage> messages = CheckInContainer(package);

        // Assert
        messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_030).Which.Arguments[0].Should().Be("uid");
    }

    [Fact]
    public void Check_PackageDocumentInAContainer_ReportsItemsAndDeclaredTypesAsFacts()
    {
        // Arrange
        string package = Package(items: Item("ch1", "ch1.xhtml", "application/xhtml+xml"));

        // Act
        List<ReportedMessage> messages = CheckInContainer(package, out CollectingReport report);

        // Assert
        messages.Should().BeEmpty();
        report.Features.Should().Contain(f => f.Feature == FeatureKind.ItemsCount && f.Value == "1");
        report.Features.Should().Contain(f => f.Feature == FeatureKind.DeclaredMimetype && f.Resource == "ch1.xhtml" && f.Value == "application/xhtml+xml");
    }

    [Fact]
    public void Check_OebPackage_IsReportedAsOpf047AndItsDeprecatedItemsAreChecked()
    {
        // Arrange
        string package = "<package xmlns=\"http://openebook.org/namespaces/oeb-package/1.0/\" unique-identifier=\"uid\">"
            + "<metadata><dc-metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:Title>T</dc:Title><dc:Language>en</dc:Language>"
            + "<dc:Identifier id=\"uid\">x</dc:Identifier></dc-metadata></metadata>"
            + "<manifest><item id=\"a\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/></manifest><spine><itemref idref=\"a\"/></spine></package>";

        // Act
        List<ReportedMessage> messages = Check(package, out _);

        // Assert
        messages.Select(m => m.Message.Id).Should().Contain(MessageId.OPF_047).And.Contain(MessageId.OPF_038);
    }

    private static string Item(string id, string href, string mediaType, string extra = "") =>
        $"<item id=\"{id}\" href=\"{href}\" media-type=\"{mediaType}\" {extra}/>";

    private static string Package(string items = "", string spine = "<spine/>", string guide = "") =>
        Header + Metadata + "<manifest>" + items + "</manifest>" + spine + guide + "</package>";

    private static List<ReportedMessage> Check(string package, out CollectingReport report)
    {
        report = new CollectingReport { ReportingLevel = Severity.Usage };
        ValidationContext context = new()
        {
            Url = Url.Parse("file:///books/package.opf"),
            Report = report,
            Version = EpubVersion.Version2,
            MimeType = "application/oebps-package+xml",
            ResourceProvider = new Provider(package),
        };
        new OPFChecker(context).Check();

        // The schema tells about the required toc and the like; this class is about the checks that follow it.
        return [.. report.Messages.Where(m => m.Message.Id is not MessageId.RSC_005 and not MessageId.RSC_017)];
    }

    private static List<ReportedMessage> CheckInContainer(string package) => CheckInContainer(package, out _);

    private static List<ReportedMessage> CheckInContainer(string package, out CollectingReport report)
    {
        report = new CollectingReport { ReportingLevel = Severity.Usage };
        using MemoryEpubSource source = new(
        [
            new KeyValuePair<string, byte[]>("package.opf", Encoding.UTF8.GetBytes(package)),
            new KeyValuePair<string, byte[]>("ch1.xhtml", [.. "<a/>"u8]),
            new KeyValuePair<string, byte[]>("c.png", [0]),
        ]);
        OCFContainer.Builder builder = new(source);
        foreach (EpubSourceEntry entry in source.Entries)
        {
            builder.AddResource(entry);
        }

        OCFContainer container = builder.Build();
        ValidationContext context = new ValidationContext
        {
            Url = container.RootUrl.Resolve("package.opf"),
            Report = report,
            Version = EpubVersion.Version2,
            MimeType = "application/oebps-package+xml",
        }.WithContainer(container);
        new OPFChecker(context).Check();
        return [.. report.Messages.Where(m => m.Message.Id is not MessageId.RSC_005 and not MessageId.RSC_017)];
    }

    private sealed class Provider(string content) : IResourceProvider
    {
        public Stream OpenStream(Url url) => new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}
