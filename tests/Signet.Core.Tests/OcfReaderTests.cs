using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;
using Xunit;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests;

/// <summary>
/// Tests of <see cref="OcfReader"/> — unpacking an EPUB and reading the OCF container.
/// </summary>
public sealed class OcfReaderTests
{
    // --- rozpakowanie z korpusu ---

    [Fact]
    public void Extracts_a_minimal_epub3_and_resolves_the_opf()
    {
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        container.OpfBookPath.Should().Be("EPUB/package.opf");
        container.Rootfiles.Should().ContainSingle().Which.Should().Be("EPUB/package.opf");
        container.OpfDirectory.Should().Be("EPUB");
        container.Warnings.Should().BeEmpty();
        container.EncryptedFiles.Should().BeEmpty();

        File.Exists(SysPath.Combine(work.Path, "EPUB", "package.opf")).Should().BeTrue();
        File.Exists(SysPath.Combine(work.Path, "EPUB", "text", "chapter1.xhtml")).Should().BeTrue();
        container.ToAbsolutePath("EPUB/nav.xhtml")
            .Should().Be(SysPath.Combine(work.Path, "EPUB", "nav.xhtml"));
    }

    [Fact]
    public void Extracts_epub2_with_its_ncx()
    {
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        container.OpfBookPath.Should().Be("OEBPS/content.opf");
        File.Exists(SysPath.Combine(work.Path, "OEBPS", "toc.ncx")).Should().BeTrue();
    }

    [Fact]
    public void Resolves_the_opf_for_a_deep_folder_layout()
    {
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        container.OpfBookPath.Should().Be("content/book.opf");
        container.OpfDirectory.Should().Be("content");
    }

    [Theory]
    [MemberData(nameof(ValidEpubDirs))]
    public void Extraction_is_lossless_for_every_valid_corpus_epub(string corpusDir)
    {
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(corpusDir, epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        byte[] original = EpubBuilder.BuildBytes(corpusDir);
        byte[] afterExtraction = EpubBuilder.BuildBytes(container.RootDirectory);
        EpubComparisonResult result = EpubComparer.CompareBytes(original, afterExtraction);

        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Fact]
    public void Works_through_the_TempFolder_overload()
    {
        using TempDir epubDir = new();
        using TempFolder work = new(epubDir.Path);
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work);

        container.OpfBookPath.Should().Be("EPUB/package.opf");
    }

    // --- warnings and errors ---

    [Fact]
    public void A_missing_mimetype_is_a_warning_not_an_error()
    {
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("missing-mimetype"), epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        container.OpfBookPath.Should().Be("EPUB/package.opf");
        container.Warnings.Should().Contain(w => w.Contains("mimetype", StringComparison.Ordinal));
    }

    [Fact]
    public void A_compressed_mimetype_entry_produces_a_warning()
    {
        using TempDir dir = new();
        string epub = SysPath.Combine(dir.Path, "compressed-mimetype.epub");
        WriteZip(epub, zip =>
        {
            WriteText(zip.CreateEntry("mimetype", CompressionLevel.Optimal), OcfReader.OcfMimetype);
            WriteText(
                zip.CreateEntry("META-INF/container.xml"),
                MinimalContainer("OEBPS/content.opf"));
            WriteText(zip.CreateEntry("OEBPS/content.opf"), "<package/>");
        });

        OcfContainer container = new OcfReader(epub).Extract(dir.Combine("out"));

        container.Warnings.Should().Contain(CoreStrings.Get("LoadWarning_MimetypeCompressed"));
    }

    [Fact]
    public void An_empty_rootfiles_list_throws()
    {
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("no-rootfile"), epubDir);

        FluentActions.Invoking(() => new OcfReader(epub).Extract(work.Path))
            .Should().Throw<EpubLoadException>()
            .WithMessage("*rootfile*");
    }

    [Fact]
    public void A_bad_opf_still_extracts_at_the_ocf_stage()
    {
        // The validity of the OPF itself is checked elsewhere — OcfReader only locates the file.
        using TempDir epubDir = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("bad-opf-xml"), epubDir);

        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        container.OpfBookPath.Should().Be("EPUB/package.opf");
    }

    [Fact]
    public void A_corrupt_zip_throws_EpubLoadException()
    {
        using TempDir dir = new();
        string fake = SysPath.Combine(dir.Path, "broken.epub");
        File.WriteAllBytes(fake, Encoding.ASCII.GetBytes(new string('x', 200)));

        FluentActions.Invoking(() => new OcfReader(fake).Extract(dir.Combine("out")))
            .Should().Throw<EpubLoadException>()
            .WithMessage("*" + LocalizedText.Fragment("LoadError_CorruptZip") + "*");
    }

    [Fact]
    public void A_missing_epub_file_throws_EpubLoadException()
    {
        using TempDir dir = new();

        FluentActions.Invoking(() => new OcfReader(dir.Combine("nope.epub")).Extract(dir.Combine("out")))
            .Should().Throw<EpubLoadException>()
            .WithMessage("*" + LocalizedText.Fragment("LoadError_EpubNotFound") + "*");
    }

    [Fact]
    public void An_entry_that_escapes_the_target_directory_is_rejected()
    {
        using TempDir dir = new();
        string evil = SysPath.Combine(dir.Path, "evil.epub");
        WriteZip(evil, zip =>
        {
            WriteText(zip.CreateEntry("mimetype", CompressionLevel.NoCompression), OcfReader.OcfMimetype);
            WriteText(zip.CreateEntry("../escape.txt"), "pwned");
        });

        FluentActions.Invoking(() => new OcfReader(evil).Extract(dir.Combine("out")))
            .Should().Throw<EpubLoadException>()
            .WithMessage("*" + LocalizedText.Fragment("LoadError_SuspiciousEntryName") + "*");
    }

    // --- ParseContainerXml ---

    [Fact]
    public void ParseContainerXml_returns_the_rootfile_bookpath()
    {
        OcfReader.ParseContainerXml(MinimalContainer("EPUB/package.opf"))
            .Should().ContainSingle().Which.Should().Be("EPUB/package.opf");
    }

    [Fact]
    public void ParseContainerXml_returns_every_rendition_in_document_order()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="EPUB/main.opf" media-type="application/oebps-package+xml"/>
                <rootfile full-path="EPUB/alt.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """;

        OcfReader.ParseContainerXml(xml).Should().Equal("EPUB/main.opf", "EPUB/alt.opf");
    }

    [Fact]
    public void ParseContainerXml_ignores_rootfiles_with_the_wrong_or_missing_media_type()
    {
        const string xml = """
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="EPUB/no-type.opf"/>
                <rootfile full-path="EPUB/wrong.opf" media-type="text/plain"/>
                <rootfile full-path="EPUB/right.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """;

        OcfReader.ParseContainerXml(xml).Should().Equal("EPUB/right.opf");
    }

    [Theory]
    [InlineData("EPUB\\package.opf")]
    [InlineData("./EPUB/package.opf")]
    [InlineData("/EPUB/package.opf")]
    public void ParseContainerXml_normalizes_the_full_path(string fullPath)
    {
        OcfReader.ParseContainerXml(MinimalContainer(fullPath))
            .Should().ContainSingle().Which.Should().Be("EPUB/package.opf");
    }

    [Fact]
    public void ParseContainerXml_throws_on_malformed_xml()
    {
        FluentActions.Invoking(() => OcfReader.ParseContainerXml("<container><rootfiles></container>"))
            .Should().Throw<EpubLoadException>()
            .WithMessage("*container.xml*");
    }

    // --- ParseEncryptionXml ---

    [Fact]
    public void ParseEncryptionXml_maps_font_paths_to_their_algorithm()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container"
                        xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
              <enc:EncryptedData>
                <enc:EncryptionMethod Algorithm="http://www.idpf.org/2008/embedding"/>
                <enc:CipherData><enc:CipherReference URI="EPUB/fonts/OpenSans.otf"/></enc:CipherData>
              </enc:EncryptedData>
              <enc:EncryptedData>
                <enc:EncryptionMethod Algorithm="http://ns.adobe.com/pdf/enc#RC"/>
                <enc:CipherData><enc:CipherReference URI="/EPUB/fonts/My%20Font.otf"/></enc:CipherData>
              </enc:EncryptedData>
            </encryption>
            """;

        var map = OcfReader.ParseEncryptionXml(xml);

        map.Should().HaveCount(2);
        map["EPUB/fonts/OpenSans.otf"].Should().Be(OcfReader.IdpfFontAlgorithmId);
        map["EPUB/fonts/My Font.otf"].Should().Be(OcfReader.AdobeFontAlgorithmId);
    }

    [Fact]
    public void ParseEncryptionXml_of_an_empty_document_is_empty()
    {
        OcfReader.ParseEncryptionXml("<encryption/>").Should().BeEmpty();
    }

    [Fact]
    public void ParseEncryptionXml_throws_on_malformed_xml()
    {
        FluentActions.Invoking(() => OcfReader.ParseEncryptionXml("<encryption>"))
            .Should().Throw<EpubLoadException>()
            .WithMessage("*encryption.xml*");
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        FluentActions.Invoking(() => new OcfReader(null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => OcfReader.ParseContainerXml(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OcfReader.ParseEncryptionXml(null!)).Should().Throw<ArgumentNullException>();
    }

    public static TheoryData<string> ValidEpubDirs()
    {
        TheoryData<string> data = new();
        foreach (string dir in CorpusPaths.ValidEpubDirs)
        {
            data.Add(dir);
        }

        return data;
    }

    private static string MinimalContainer(string opfFullPath) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="{opfFullPath}" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private static void WriteZip(string path, Action<ZipArchive> build)
    {
        using FileStream fs = File.Create(path);
        using ZipArchive zip = new(fs, ZipArchiveMode.Create);
        build(zip);
    }

    private static void WriteText(ZipArchiveEntry entry, string content)
    {
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    [Fact]
    public void ReadExtracted_reads_an_unpacked_publication_without_modifying_it()
    {
        using TempDir tree = new();
        TestFs.CopyDirectory(CorpusPaths.Epub3ObfuscatedFonts, tree.Path);
        string[] before = Directory.GetFiles(tree.Path, "*", SearchOption.AllDirectories);

        OcfContainer container = OcfReader.ReadExtracted(tree.Path);

        container.OpfBookPath.Should().Be("EPUB/package.opf");
        container.EncryptedFiles.Should().NotBeEmpty();
        Directory.GetFiles(tree.Path, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void ReadExtracted_rejects_a_missing_directory()
    {
        using TempDir tree = new();

        Action act = () => OcfReader.ReadExtracted(tree.Combine("missing"));

        act.Should().Throw<EpubLoadException>();
    }
}
