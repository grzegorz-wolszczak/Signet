using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Css;
using Signet.EpubCheck.Core.Dtbook;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Ops;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Opf;

/// <summary>Which checker a resource gets, and how the items of a package document reach it.</summary>
public sealed class CheckerFactoryTests
{
    [Theory]
    [InlineData("text/css", EpubVersion.Version3, typeof(CSSChecker))]
    [InlineData("text/css", EpubVersion.Version2, typeof(CSSChecker))]
    [InlineData("application/x-dtbook+xml", EpubVersion.Version2, typeof(DTBookChecker))]
    [InlineData("application/xhtml+xml", EpubVersion.Version3, typeof(OPSChecker))]
    [InlineData("application/XHTML+xml", EpubVersion.Version2, typeof(OPSChecker))]
    [InlineData("image/svg+xml", EpubVersion.Version3, typeof(OPSChecker))]
    [InlineData("image/svg+xml", EpubVersion.Version2, typeof(OPSChecker))]
    [InlineData("text/html", EpubVersion.Version2, typeof(OPSChecker))]
    [InlineData("text/x-oeb1-document", EpubVersion.Version2, typeof(OPSChecker))]
    [InlineData("text/html", EpubVersion.Version3, typeof(PublicationResourceChecker))]
    [InlineData("text/x-oeb1-document", EpubVersion.Version3, typeof(PublicationResourceChecker))]
    [InlineData("application/oebps-package+xml", EpubVersion.Version2, typeof(OPFChecker))]
    [InlineData("application/oebps-package+xml", EpubVersion.Version3, typeof(OPFChecker30))]
    [InlineData("image/png", EpubVersion.Version3, typeof(PublicationResourceChecker))]
    [InlineData("", EpubVersion.Version3, typeof(PublicationResourceChecker))]
    public void NewChecker_PicksTheCheckerOfTheMediaType(string mimeType, EpubVersion version, Type expected)
    {
        // Arrange
        ValidationContext context = new()
        {
            Url = Url.Parse("file:///doc"),
            Report = new CollectingReport(),
            MimeType = mimeType,
            Version = version,
        };

        // Act
        IChecker checker = CheckerFactory.NewChecker(context);

        // Assert
        checker.GetType().Should().Be(expected);
    }

    [Fact]
    public void GetMimeType_DataUrl_IsTheTypeItNames()
    {
        // Arrange
        ValidationContext context = Context(out _);

        // Act & Assert
        context.GetMimeType(Url.Parse("data:image/png;base64,AAAA")).Should().Be("image/png");
        context.GetMimeType(Url.Parse("data:text/plain,hello")).Should().Be("text/plain");
    }

    [Fact]
    public void GetMimeType_ResourceOfTheRegistry_IsItsDeclaredType_AndUnknownOnesAreNull()
    {
        // Arrange
        ValidationContext context = Context(out OCFContainer container);
        Url known = container.RootUrl.Resolve("a.png");
        context.ResourceRegistry!.RegisterResource(known, "image/png");

        // Act & Assert
        context.GetMimeType(known).Should().Be("image/png");
        context.GetMimeType(container.RootUrl.Resolve("b.png")).Should().BeNull();
        context.GetMimeType(null).Should().BeNull();
    }

    [Fact]
    public void GetMimeType_WithoutARegistry_IsNull()
    {
        // Arrange
        ValidationContext context = new() { Url = Url.Parse("file:///doc"), Report = new CollectingReport() };

        // Act & Assert
        context.GetMimeType(Url.Parse("file:///a.png")).Should().BeNull();
    }

    [Fact]
    public void Check_NavigationDocumentItem_IsCheckedAsANavigationDocument()
    {
        // Arrange
        string nav = "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>T</title></head>"
            + "<body><nav epub:type=\"toc\"><h1>T</h1><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav></body></html>";
        string chapter = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head><body><p>c</p></body></html>";

        // Act
        (CollectingReport report, ValidationContext context) = Run(nav, chapter);

        // Assert
        context.FeatureReport.HasFeature(FeatureKind.TocLinks).Should().BeTrue();
        context.ReferenceRegistry!.AsList().Should().Contain(reference => reference.Type == ReferenceType.NavTocLink);
        report.Messages.Where(message => message.Message.Id == MessageId.RSC_005).Should().BeEmpty();
    }

    [Fact]
    public void Check_NavigationDocumentWithoutAToc_ReportsTheSchemaOfNavigationDocuments()
    {
        // Arrange
        string nav = "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>T</title></head>"
            + "<body><nav epub:type=\"landmarks\"><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav></body></html>";
        string chapter = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head><body><p>c</p></body></html>";

        // Act
        (CollectingReport report, _) = Run(nav, chapter);

        // Assert
        report.Messages.Select(message => message.Message.Id).Should().Contain(MessageId.RSC_005);
    }

    private static (CollectingReport Report, ValidationContext Context) Run(string nav, string chapter)
    {
        string package = "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"uid\" xml:lang=\"en\">"
            + "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>T</dc:title><dc:language>en</dc:language>"
            + "<dc:identifier id=\"uid\">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier>"
            + "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>"
            + "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>"
            + "<item id=\"c\" href=\"c.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>"
            + "<spine><itemref idref=\"c\"/></spine></package>";
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        MemoryEpubSource source = new(
        [
            new KeyValuePair<string, byte[]>("package.opf", Encoding.UTF8.GetBytes(package)),
            new KeyValuePair<string, byte[]>("nav.xhtml", Encoding.UTF8.GetBytes(nav)),
            new KeyValuePair<string, byte[]>("c.xhtml", Encoding.UTF8.GetBytes(chapter)),
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
            Version = EpubVersion.Version3,
            MimeType = "application/oebps-package+xml",
        }.WithContainer(container);
        new OPFChecker30(context).Check();
        return (report, context);
    }

    private static ValidationContext Context(out OCFContainer container)
    {
        MemoryEpubSource source = new([new KeyValuePair<string, byte[]>("a.xhtml", [])]);
        OCFContainer.Builder builder = new(source);
        builder.AddResource(source.Entries[0]);
        container = builder.Build();
        return new ValidationContext { Url = container.RootUrl.Resolve("a.xhtml"), Report = new CollectingReport() }.WithContainer(container);
    }
}
