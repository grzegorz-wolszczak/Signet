using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

public sealed class OCFCheckerTests
{
    private const string PackagePath = "package.opf";

    // A container file that declares one package document, and a package document the checks of the OCF layer accept.
    private const string ValidContainer = "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">"
        + "<rootfiles><rootfile full-path=\"package.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>";

    private const string ValidPackage = "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"uid\" xml:lang=\"en\">"
        + "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"uid\">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier>"
        + "<dc:title>T</dc:title><dc:language>en</dc:language><meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>"
        + "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest>"
        + "<spine><itemref idref=\"nav\"/></spine></package>";

    private static readonly EpubLocation s_publication = new("book.epub");
    private readonly Fixture _fixture = new();

    [Fact]
    public void Check_DuplicateCanonicalNames_ReportsDuplicateEntry()
    {
        // Arrange
        using MemoryEpubSource source = Expanded("münchen.xhtml", "münchen.xhtml");
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Where(message => message.Message.Id == MessageId.OPF_060).Should().ContainSingle()
            .Which.Location.Should().Be(s_publication);
    }

    [Fact]
    public void Check_MissingContainerFile_ReportsIt()
    {
        // Arrange
        using MemoryEpubSource source = new([File("mimetype", OCFChecker.EpubMimeType)]);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.RSC_002);
    }

    [Fact]
    public void Check_NonAsciiDirectoryAndFile_ReportsPkg012OncePerFile()
    {
        // Arrange (directories are not checked: their names are part of the paths of their files)
        using MemoryEpubSource source = Expanded("EPUB/café/", "EPUB/café/a.xhtml");
        CollectingReport report = new() { ReportingLevel = Severity.Usage }; // PKG-012 is a usage

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.PKG_012)
            .Which.Text.Should().Contain("EPUB/café/a.xhtml");
    }

    [Fact]
    public void Check_DirectoryHoldingOnlyAnEmptyDirectory_ReportsBoth()
    {
        // Arrange
        using MemoryEpubSource source = Expanded("EPUB/", "EPUB/empty/");
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Where(message => message.Message.Id == MessageId.PKG_014).Select(message => message.Arguments[0])
            .Should().Equal("EPUB/", "EPUB/empty/");
    }

    [Fact]
    public void Check_DirectoryWhoseNameStartsAFileName_IsEmpty()
    {
        // Arrange ("a/" is not the directory of "ab.xhtml")
        using MemoryEpubSource source = Expanded("a/", "ab.xhtml");
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.PKG_014);
    }

    [Fact]
    public void Check_ExpandedWithoutMimetype_ReportsPkg006()
    {
        // Arrange
        using MemoryEpubSource source = new([File(OCFChecker.ContainerPath, _fixture.Create<string>())]);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.PKG_006);
    }

    [Theory]
    [InlineData("application/epub+zip\n", "The content of the mimetype file must be the string \"application/epub+zip\" with no whitespace or newline.")]
    [InlineData("application/zip", "The content of the mimetype file must be the string \"application/epub+zip\"")]
    public void Check_WrongMimetype_ReportsPkg007(string content, string expected)
    {
        // Arrange
        using MemoryEpubSource source = new([File("mimetype", content), File(OCFChecker.ContainerPath, ValidContainer), File(PackagePath, ValidPackage)]);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle().Which.Text.Should().Be(expected);
    }

    [Fact]
    public void Check_MimetypeWithWhitespaceInPolish_LocalizesTheWholeText()
    {
        // Arrange
        using MemoryEpubSource source = new([File("mimetype", " application/epub+zip"), File(OCFChecker.ContainerPath, ValidContainer), File(PackagePath, ValidPackage)]);
        CollectingReport report = new(new LocalizedMessageDictionary(CultureInfo.GetCultureInfo("pl")));
        string english = new CollectingReport().Dictionary.Format(new CollectingReport().Dictionary.GetMessage(MessageId.PKG_007), "whitespace");

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Messages.Should().ContainSingle().Which.Text.Should().NotBe(english).And.NotContain("with no whitespace");
    }

    [Fact]
    public void Check_File_ReportsItsFeatures()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes(_fixture.Create<string>());
        using MemoryEpubSource source = new([File("mimetype", OCFChecker.EpubMimeType), new("EPUB/a.xhtml", content)]);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(source, EpubVersion.Version3, report, s_publication);

        // Assert
        report.Features.Where(feature => feature.Resource == "EPUB/a.xhtml").Select(feature => (feature.Feature, feature.Value))
            .Should().Equal(
                (FeatureKind.Size, content.Length.ToString(CultureInfo.InvariantCulture)),
                (FeatureKind.CompressedSize, content.Length.ToString(CultureInfo.InvariantCulture)),
                (FeatureKind.CompressionMethod, "Stored"),
                (FeatureKind.Sha256, System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content))));
        report.Features.Should().Contain(new ReportedFeature(null, FeatureKind.FormatName, OCFChecker.EpubMimeType));
    }

    [Fact]
    public void CheckPath_EmptyFile_ReportsPkg003AndUnreadableZip()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        System.IO.File.WriteAllBytes(path, []);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(path, EpubVersion.Version3, report);

        // Assert (no RSC-002 after the fatal error)
        report.Messages.Select(message => message.Message.Id).Should().Equal(MessageId.PKG_003, MessageId.PKG_008);
        report.Messages[1].Text.Should().Contain(ZipEpubSource.EmptyZipMessage);
    }

    [Fact]
    public void CheckPath_NotAZip_ReportsPkg004AndUnreadableZip()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        System.IO.File.WriteAllBytes(path, _fixture.CreateMany<byte>(100).Select(b => (byte)(b | 0x80)).ToArray());
        CollectingReport report = new();

        // Act
        OCFChecker.Check(path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Select(message => message.Message.Id).Should().Equal(MessageId.PKG_004, MessageId.PKG_008);
    }

    [Fact]
    public void CheckPath_ValidZipWithUtf8NamesWithoutTheFlag_IsAccepted()
    {
        // Arrange (Java reads every name as UTF-8; many tools do not set the flag)
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        TestZip.WriteEpub(path, TestZip.Entry.Of(OCFChecker.ContainerPath, ValidContainer, utf8Flag: false), TestZip.Entry.Of(PackagePath, ValidPackage, utf8Flag: false),
            TestZip.Entry.Of("EPUB/café.xhtml", string.Empty, utf8Flag: false));
        CollectingReport report = new() { ReportingLevel = Severity.Usage }; // PKG-012 is a usage

        // Act
        OCFChecker.Check(path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Select(message => message.Message.Id).Should().Equal(MessageId.PKG_012, MessageId.OPF_003); // the file is not declared
    }

    [Fact]
    public void CheckPath_NameThatIsNotUtf8_ReportsPkg027()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        TestZip.WriteEpub(path, new TestZip.Entry([0x63, 0x61, 0x66, 0xE9], [])); // "caf\xE9" in Latin-1
        CollectingReport report = new();

        // Act
        OCFChecker.Check(path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Select(message => message.Message.Id).Should().Equal(MessageId.PKG_027);
    }

    [Fact]
    public void CheckPath_MimetypeWithExtraField_ReportsPkg005()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        TestZip.Write(path, [
            new TestZip.Entry(Encoding.ASCII.GetBytes("mimetype"), Encoding.ASCII.GetBytes(OCFChecker.EpubMimeType), Extra: [0xFE, 0xCA, 0, 0]),
            TestZip.Entry.Of(OCFChecker.ContainerPath, ValidContainer),
            TestZip.Entry.Of(PackagePath, ValidPackage),
        ]);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.PKG_005);
    }

    [Fact]
    public void CheckPath_MimetypeNotFirst_ReportsPkg006Once()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        TestZip.Write(path, [
            TestZip.Entry.Of(OCFChecker.ContainerPath, ValidContainer),
            TestZip.Entry.Of(PackagePath, ValidPackage),
            TestZip.Entry.Of("mimetype", OCFChecker.EpubMimeType),
        ]);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.PKG_006);
    }

    [Fact]
    public void CheckPath_Directory_ChecksItAsAnExpandedPublication()
    {
        // Arrange
        using TempDir dir = new();
        Directory.CreateDirectory(dir.Combine("META-INF"));
        System.IO.File.WriteAllText(dir.Combine("META-INF", "container.xml"), ValidContainer);
        System.IO.File.WriteAllText(dir.Combine(PackagePath), ValidPackage);
        CollectingReport report = new();

        // Act
        OCFChecker.Check(dir.Path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.PKG_006);
    }

    private static KeyValuePair<string, byte[]> File(string name, string content) => new(name, Encoding.UTF8.GetBytes(content));

    // A valid expanded publication (mimetype, container file) with the given extra entries.
    private static MemoryEpubSource Expanded(params string[] names) =>
        new(new[] { File("mimetype", OCFChecker.EpubMimeType), File(OCFChecker.ContainerPath, ValidContainer), File(PackagePath, ValidPackage) }
            .Concat(names.Select(name => File(name, string.Empty))));
}
