using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

/// <summary>What <see cref="OCFChecker"/> does with <c>META-INF</c>: the container file, the version and profile of the publication, encryption, signatures.</summary>
public sealed class OCFMetaFileTests
{
    private const string ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";
    private const string Opf = "http://www.idpf.org/2007/opf";
    private static readonly EpubLocation s_publication = new("book.epub");

    [Theory]
    [InlineData("<rootfile media-type=\"application/oebps-package+xml\"/>", MessageId.OPF_016)]
    [InlineData("<rootfile full-path=\"  \" media-type=\"application/oebps-package+xml\"/>", MessageId.OPF_017)]
    public void Check_RootfileWithoutAPath_IsReported(string rootfile, MessageId expected)
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(rootfile));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().StartWith(expected).And.Contain(MessageId.RSC_003);
    }

    [Fact]
    public void Check_NoPackageDocumentDeclared_ReportsRsc003AtTheContainerFile()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(string.Empty));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.RSC_003).Which.Location.Path.Should().Be("META-INF/container.xml");
    }

    [Fact]
    public void Check_PackageDocumentThatIsNotInTheContainer_ReportsOpf002()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("missing/package.opf")));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_002).Which.Arguments[0].Should().Be("missing/package.opf");
    }

    [Theory]
    [InlineData("<package xmlns=\"" + Opf + "\"/>", "Version attribute not found")]
    [InlineData("<package xmlns=\"" + Opf + "\" version=\"9\"/>", "Version not supported")]
    [InlineData("<other/>", "Package element must be the first element in the package file")]
    public void Check_PackageDocumentWithoutAUsableVersion_ReportsOpf001AndStops(string package, string reason)
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), package);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_001).Which.Arguments[0].Should().Be(reason);
        report.Messages.Should().NotContain(m => m.Message.Id == MessageId.PKG_001);
    }

    [Fact]
    public void Check_PackageDocumentNotWellFormed_ReportsRsc016()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), "<package version=\"3.0\"");
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.RSC_016);
    }

    [Fact]
    public void Check_VersionAskedForDiffersFromTheDeclaredOne_ReportsPkg001()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("3.0"));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version2, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.PKG_001).Which.Arguments.Should().Equal("2.0.1", "3.4");
    }

    [Fact]
    public void Check_DetectedVersion_IsReportedAsAFact()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("2.0"));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Unknown, report, s_publication);

        // Assert
        report.Features.Should().ContainSingle(f => f.Feature == FeatureKind.FormatVersion).Which.Value.Should().Be("2.0.1");
    }

    [Fact]
    public void Check_ProfileForAnEpub2_ReportsPkg023AndIsIgnored()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("2.0"));
        CollectingReport report = new() { ReportingLevel = Severity.Usage };

        // Act
        OCFChecker.Check(source, EpubVersion.Unknown, report, s_publication, EpubProfile.Dict);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.PKG_023);
    }

    [Fact]
    public void Check_PublicationTypeThatAsksForAProfile_ReportsOpf064()
    {
        // Arrange
        string package = "<package xmlns=\"" + Opf + "\" version=\"3.0\" unique-identifier=\"u\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
            + "<metadata><dc:type>dictionary</dc:type></metadata></package>";
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), package);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.OPF_064).Which.Arguments.Should().Equal("dictionary", "DICT");
    }

    [Fact]
    public void Check_SeveralRenditions_AreCountedAndNeedAMetadataFile()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("a.opf") + Rootfile("b.opf")), null, "a.opf", Package("3.0"), "b.opf", Package("3.0"));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Features.Should().ContainSingle(f => f.Feature == FeatureKind.EpubRenditionsCount).Which.Value.Should().Be("2");
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.RSC_019);
    }

    [Fact]
    public void Check_SeveralRenditionsOfAnEpub2_ReportsPkg013()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("a.opf") + Rootfile("b.opf")), null, "a.opf", Package("2.0"), "b.opf", Package("2.0"));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version2, report, s_publication);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.PKG_013).And.NotContain(MessageId.RSC_019);
    }

    [Fact]
    public void Check_ContainerFileWithAnUnknownElement_ReportsRsc005WithTheSchemasMessage()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf") + "<foo/>"), Package("3.0"));
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.RSC_005).Which.Text.Should().Contain("element \"foo\" not allowed");
    }

    [Fact]
    public void Check_EncryptionFile_IsReportedAsAFactAndValidated()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("3.0"), "META-INF/encryption.xml", "<foo/>");
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Features.Should().ContainSingle(f => f.Feature == FeatureKind.HasEncryption).Which.Value.Should().Be("META-INF/encryption.xml");
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.RSC_005).Which.Text.Should().Contain("expected element \"encryption\"");
    }

    [Fact]
    public void Check_FontObfuscation_IsReportedAsOutdatedUsage()
    {
        // Arrange
        string encryption = "<encryption xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" xmlns:enc=\"http://www.w3.org/2001/04/xmlenc#\">"
            + "<enc:EncryptedData><enc:EncryptionMethod Algorithm=\"http://www.idpf.org/2008/embedding\"/>"
            + "<enc:CipherData><enc:CipherReference URI=\"font.otf\"/></enc:CipherData></enc:EncryptedData></encryption>";
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("3.0"), "META-INF/encryption.xml", encryption, "font.otf", "x");
        CollectingReport report = new() { ReportingLevel = Severity.Usage };

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.OBS_001);
        report.Messages.Select(m => m.Message.Id).Should().NotContain(MessageId.RSC_007);
    }

    [Fact]
    public void Check_EncryptedResourceThatIsNotInTheContainer_ReportsRsc007()
    {
        // Arrange
        string encryption = "<encryption xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" xmlns:enc=\"http://www.w3.org/2001/04/xmlenc#\">"
            + "<enc:EncryptedData><enc:EncryptionMethod Algorithm=\"urn:x\"/>"
            + "<enc:CipherData><enc:CipherReference URI=\"nothing.otf\"/></enc:CipherData></enc:EncryptedData></encryption>";
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("3.0"), "META-INF/encryption.xml", encryption);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.RSC_007).Which.Arguments[0].Should().Be("nothing.otf");
    }

    [Fact]
    public void Check_SignaturesFile_IsReportedAsAFactAndValidated()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("3.0"), "META-INF/signatures.xml", "<foo/>");
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Features.Should().ContainSingle(f => f.Feature == FeatureKind.HasSignatures);
        report.Messages.Should().ContainSingle(m => m.Message.Id == MessageId.RSC_005).Which.Text.Should().Contain("expected element \"signatures\"");
    }

    [Fact]
    public void Check_ValidPublicationOfTheOcfLayer_ReportsNothing()
    {
        // Arrange
        using MemoryEpubSource source = Source(Container(Rootfile("package.opf")), Package("3.0"));
        CollectingReport report = new() { ReportingLevel = Severity.Usage };

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().BeEmpty();
    }

    private static string Container(string rootfiles) =>
        "<container version=\"1.0\" xmlns=\"" + ContainerNamespace + "\"><rootfiles>" + rootfiles + "</rootfiles></container>";

    private static string Rootfile(string path) => "<rootfile full-path=\"" + path + "\" media-type=\"application/oebps-package+xml\"/>";

    private static string Package(string version) => "<package xmlns=\"" + Opf + "\" version=\"" + version + "\" unique-identifier=\"u\"/>";

    // An expanded publication: mimetype, the container file, the package document "package.opf" when given, and the
    // other files (names and contents in pairs).
    private static MemoryEpubSource Source(string container, string? package = null, params string[] files)
    {
        List<KeyValuePair<string, byte[]>> entries =
        [
            new("mimetype", Encoding.UTF8.GetBytes(OCFChecker.EpubMimeType)),
            new(OCFChecker.ContainerPath, Encoding.UTF8.GetBytes(container)),
        ];
        if (package is not null)
        {
            entries.Add(new("package.opf", Encoding.UTF8.GetBytes(package)));
        }

        for (int i = 0; i < files.Length; i += 2)
        {
            entries.Add(new(files[i], Encoding.UTF8.GetBytes(files[i + 1])));
        }

        return new MemoryEpubSource(entries);
    }
}
