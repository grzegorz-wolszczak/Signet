using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Overlay;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Overlay;

/// <summary>The media overlay documents, the overlay text checker, and the checks of the package document about overlays.</summary>
public sealed class OverlayCheckerTests
{
    private const string Smil = "application/smil+xml";

    [Fact]
    public void Constructor_ResourceThatIsNotAnOverlay_Fails()
    {
        // Arrange
        ContentDocumentHarness harness = new("o.smil", "<smil/>", ContentDocumentHarness.Xhtml);
        harness.Build();

        // Act & Assert
        FluentActions.Invoking(() => new OverlayChecker(harness.Context)).Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Check_TextAndSeqAndBody_RegisterTheLinksToTheContentDocuments()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay(
            "<body epub:textref=\"c.xhtml\"><seq epub:textref=\"c.xhtml#s\"><par><text src=\"c.xhtml#p1\"/><audio src=\"a.mp3\"/></par></seq></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml).With("a.mp3", "audio/mpeg");

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => reference.Type).Should().Equal(
            ReferenceType.OverlayTextLink, ReferenceType.OverlayTextLink, ReferenceType.OverlayTextLink, ReferenceType.Audio);
        harness.References[3].HasIntrinsicFallback.Should().BeTrue();
    }

    [Fact]
    public void Check_ContentDocumentReferredToByTwoOverlays_ReportsMed011()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body><par><text src=\"c.xhtml#p1\"/></par></body>").With("c.xhtml", ContentDocumentHarness.Xhtml);
        harness.Build();
        harness.Context.OverlayTextChecker!.RegisterOverlay(harness.Local("c.xhtml"), "another-overlay");

        // Act
        new OverlayChecker(harness.Context).Check();

        // Assert
        harness.Messages(MessageId.MED_011).Should().ContainSingle();
    }

    [Fact]
    public void Check_AudioOfANonCoreType_ReportsMed005()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body><par><text src=\"c.xhtml#p1\"/><audio src=\"a.wav\"/></par></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml).With("a.wav", "audio/wav");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.MED_005).Should().ContainSingle().Which.Arguments.Should().Equal("a.wav", "audio/wav");
    }

    [Fact]
    public void Check_AudioWithAFragment_ReportsMed014AndRegistersTheDocument()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body><par><text src=\"c.xhtml#p1\"/><audio src=\"a.mp3#t=1\"/></par></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml).With("a.mp3", "audio/mpeg");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.MED_014).Should().ContainSingle().Which.Arguments.Should().Equal("t=1");
        harness.References.Last().Url.Fragment.Should().BeNull();
    }

    [Fact]
    public void Check_RemoteAudio_NeedsTheRemoteResourcesProperty()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body><par><text src=\"c.xhtml#p1\"/><audio src=\"https://example.org/a.mp3\"/></par></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_014).Should().ContainSingle().Which.Arguments.Should().Equal("remote-resources");
    }

    [Theory]
    [InlineData("5s", "3s", MessageId.MED_008)]
    [InlineData("3s", "3s", MessageId.MED_009)]
    [InlineData("0:00:03", "3000ms", MessageId.MED_009)]
    public void Check_ClipTimes_AreCompared(string begin, string end, MessageId expected)
    {
        // Arrange
        ContentDocumentHarness harness = Overlay($"<body><par><text src=\"c.xhtml#p1\"/><audio src=\"a.mp3\" clipBegin=\"{begin}\" clipEnd=\"{end}\"/></par></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml).With("a.mp3", "audio/mpeg");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(expected);
    }

    [Fact]
    public void Check_ClipEndAfterTheBeginning_AndWithoutAnEnd_ReportNothing()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay(
            "<body><par><text src=\"c.xhtml#p1\"/><audio src=\"a.mp3\" clipEnd=\"3s\"/></par><par><text src=\"c.xhtml#p2\"/><audio src=\"a.mp3\" clipBegin=\"4s\"/></par></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml).With("a.mp3", "audio/mpeg");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().NotContain([MessageId.MED_008, MessageId.MED_009]);
    }

    [Fact]
    public void Check_UnknownSemantic_ReportsOpf088()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body><seq epub:type=\"zzz\" epub:textref=\"c.xhtml\"><par><text src=\"c.xhtml#p1\"/></par></seq></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_088).Should().ContainSingle().Which.Arguments.Should().Equal("zzz");
    }

    [Fact]
    public void Check_ValidOverlay_ReportsNothing()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body><par><text src=\"c.xhtml#p1\"/><audio src=\"a.mp3\" clipBegin=\"0s\" clipEnd=\"2s\"/></par></body>")
            .With("c.xhtml", ContentDocumentHarness.Xhtml).With("a.mp3", "audio/mpeg");

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Fact]
    public void OverlayTextChecker_RemembersTheOverlayOfEachDocument()
    {
        // Arrange
        OverlayTextChecker checker = new();
        Url a = Url.Parse("https://x.example/a.xhtml");
        Url b = Url.Parse("https://x.example/b.xhtml");

        // Act & Assert
        checker.RegisterOverlay(a, "o1").Should().BeTrue();
        checker.RegisterOverlay(a, "o1").Should().BeTrue();
        checker.RegisterOverlay(a, "o2").Should().BeFalse();
        checker.IsReferencedByOverlay(a).Should().BeTrue();
        checker.IsReferencedByOverlay(b).Should().BeFalse();
        checker.IsReferencedByOverlay(null).Should().BeFalse();
        checker.IsCorrectOverlay(a, "o1").Should().BeTrue();
        checker.IsCorrectOverlay(a, "o2").Should().BeFalse();
        checker.IsCorrectOverlay(b, "o1").Should().BeFalse();
    }

    [Fact]
    public void CheckerFactory_GivesTheOverlayCheckerToSmilInEpub3Only()
    {
        // Arrange
        ContentDocumentHarness harness = Overlay("<body/>");
        harness.Build();

        // Act & Assert
        CheckerFactory.NewChecker(harness.Context).Should().BeOfType<OverlayChecker>();
        CheckerFactory.NewChecker(harness.Context with { Version = EpubVersion.Version2 }).Should().BeOfType<PublicationResourceChecker>();
    }

    [Fact]
    public void Package_ContentDocumentAnOverlayRefersToWithoutTheAttribute_ReportsMed010()
    {
        // Arrange & Act
        CollectingReport report = RunPackage(contentAttribute: string.Empty);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.MED_010);
    }

    [Fact]
    public void Package_ContentDocumentNamingAnotherOverlay_ReportsMed012()
    {
        // Arrange & Act
        CollectingReport report = RunPackage(contentAttribute: " media-overlay=\"other\"", extraItems: "<item id=\"other\" href=\"other.smil\" media-type=\"application/smil+xml\"/>");

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.MED_012);
    }

    [Fact]
    public void Package_ContentDocumentNamingTheRightOverlay_ReportsNoMedMessage()
    {
        // Arrange & Act
        CollectingReport report = RunPackage(contentAttribute: " media-overlay=\"o\"");

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().NotContain([MessageId.MED_010, MessageId.MED_012, MessageId.MED_013]);
    }

    [Fact]
    public void Package_ContentDocumentNamingAnOverlayThatDoesNotReferToIt_ReportsMed013()
    {
        // Arrange & Act
        CollectingReport report = RunPackage(contentAttribute: " media-overlay=\"o\"", overlayText: "d.xhtml#p");

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.MED_013);
    }

    [Theory]
    [InlineData("PT0H0M10S", "00:00:04", "00:00:06", false)]
    [InlineData("0:00:10", "4s", "4s", true)]
    [InlineData("0:00:10", "4s", "5.5s", false)]
    [InlineData("10s", "xyz", "5s", false)]
    public void Package_TotalDurationDifferingFromTheSum_ReportsMed016(string total, string first, string second, bool reported)
    {
        // Arrange
        string metadata = $"<meta property=\"media:duration\">{total}</meta>"
            + $"<meta property=\"media:duration\" refines=\"#o\">{first}</meta><meta property=\"media:duration\" refines=\"#o2\">{second}</meta>";

        // Act
        CollectingReport report = RunPackage(contentAttribute: " media-overlay=\"o\"", metadata: metadata);

        // Assert
        report.Messages.Any(m => m.Message.Id == MessageId.MED_016).Should().Be(reported);
    }

    private static ContentDocumentHarness Overlay(string body) =>
        new(
            "o.smil",
            "<smil xmlns=\"http://www.w3.org/ns/SMIL\" xmlns:epub=\"http://www.idpf.org/2007/ops\" version=\"3.0\">" + body + "</smil>",
            Smil);

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new OverlayChecker(harness.Context).Check();
    }

    // A package with one overlay "o" (o.smil) that refers to c.xhtml, or to the document named in overlayText.
    private static CollectingReport RunPackage(string contentAttribute, string overlayText = "c.xhtml#p", string extraItems = "", string metadata = "")
    {
        string package = "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"uid\" xml:lang=\"en\">"
            + "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>T</dc:title><dc:language>en</dc:language>"
            + "<dc:identifier id=\"uid\">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier>"
            + "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>" + metadata + "</metadata>"
            + "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>"
            + $"<item id=\"c\" href=\"c.xhtml\" media-type=\"application/xhtml+xml\"{contentAttribute}/>"
            + $"<item id=\"d\" href=\"d.xhtml\" media-type=\"application/xhtml+xml\"/>"
            + "<item id=\"o\" href=\"o.smil\" media-type=\"application/smil+xml\"/>" + extraItems + "</manifest>"
            + "<spine><itemref idref=\"c\"/><itemref idref=\"d\"/></spine></package>";
        string html = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head><body><p id=\"p\">x</p></body></html>";
        string nav = "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>T</title></head>"
            + "<body><nav epub:type=\"toc\"><h1>T</h1><ol><li><a href=\"c.xhtml\">c</a></li></ol></nav></body></html>";
        string overlay = "<smil xmlns=\"http://www.w3.org/ns/SMIL\" xmlns:epub=\"http://www.idpf.org/2007/ops\" version=\"3.0\"><body>"
            + $"<par><text src=\"{overlayText}\"/></par></body></smil>";
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        MemoryEpubSource source = new(
        [
            new KeyValuePair<string, byte[]>("package.opf", Encoding.UTF8.GetBytes(package)),
            new KeyValuePair<string, byte[]>("nav.xhtml", Encoding.UTF8.GetBytes(nav)),
            new KeyValuePair<string, byte[]>("c.xhtml", Encoding.UTF8.GetBytes(html)),
            new KeyValuePair<string, byte[]>("d.xhtml", Encoding.UTF8.GetBytes(html)),
            new KeyValuePair<string, byte[]>("o.smil", Encoding.UTF8.GetBytes(overlay)),
            new KeyValuePair<string, byte[]>("other.smil", Encoding.UTF8.GetBytes(overlay)),
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
        return report;
    }
}
