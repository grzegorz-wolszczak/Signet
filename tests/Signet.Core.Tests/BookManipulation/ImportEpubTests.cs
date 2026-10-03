using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of the full <see cref="ImportEpub"/> pipeline — from an <c>.epub</c> file to the <see cref="Book"/> model.</summary>
public sealed class ImportEpubTests
{
    public static TheoryData<string> ValidCorpus()
    {
        TheoryData<string> data = new();
        foreach (string dir in CorpusPaths.ValidEpubDirs)
        {
            data.Add(dir);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidCorpus))]
    public void GetBook_ValidCorpus_ImportsWithoutException(string corpusDir)
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(corpusDir, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.GetOpf().Should().NotBeNull();
        book.GetAllResources().Should().NotBeEmpty();
        book.EpubVersion.Should().MatchRegex("^[23]");
        book.GetHtmlResources().Should().NotBeEmpty();

        // Every resource has a file on disk in the working directory.
        foreach (Resource resource in book.GetAllResources())
        {
            File.Exists(resource.FullPath).Should().BeTrue($"the file of resource {resource.BookPath} should exist");
        }
    }

    [Fact]
    public void GetBook_CleanEpub_ImportsWithoutWarningsAndUnmodified()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.LoadWarnings.Should().BeEmpty();
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void GetBook_Epub2Minimal_LoadsRealNcxContent_NotDefaultTemplate()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp);

        using Book book = new ImportEpub(epub).GetBook();

        string ncxText = book.GetNcx()!.GetText();
        ncxText.Should().Contain("Minimal EPUB 2").And.Contain("Chapter 1");
        ncxText.Should().NotContain("ID_UNKNOWN").And.NotContain("Section0001.xhtml");
    }

    [Fact]
    public void GetBook_Epub2Minimal_HasNcxAndSpineHtml()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.EpubVersion.Should().Be("2.0");
        book.IsEpub3.Should().BeFalse();
        book.GetNcx().Should().NotBeNull();
        book.GetHtmlResources().Should().ContainSingle()
            .Which.BookPath.Should().Be("OEBPS/Text/chapter1.xhtml");
    }

    [Fact]
    public void GetBook_Epub3Minimal_IsEpub3_WithNavAndNoNcx()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.EpubVersion.Should().Be("3.0");
        book.IsEpub3.Should().BeTrue();
        book.GetNcx().Should().BeNull();
        book.GetAllResources().Should().Contain(r => r.BookPath == "EPUB/nav.xhtml");
    }

    [Fact]
    public void GetBook_Epub3WithNcx_LoadsNcxAndKeepsSpineOrder()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.GetNcx().Should().NotBeNull();
        book.GetHtmlResources().Select(h => h.Filename)
            .Should().ContainInOrder("chapter1.xhtml", "chapter2.xhtml");
    }

    [Fact]
    public void GetBook_Media_DetectsResourceTypes_AndFontsAreNotObfuscated()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.GetAllResources().OfType<ImageResource>().Should().HaveCountGreaterThanOrEqualTo(2);
        book.GetAllResources().OfType<AudioResource>().Should().ContainSingle();
        book.GetAllResources().OfType<FontResource>().Should().ContainSingle()
            .Which.ObfuscationAlgorithm.Should().BeEmpty();
    }

    [Fact]
    public void GetBook_DeepFolders_ResolvesRelativeHrefs_AndWarnsOnMissingManifestFile()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.GetAllResources().Should().Contain(r => r.BookPath == "content/pages/body/ch01.xhtml");
        book.GetAllResources().Should().Contain(r => r.BookPath == "content/resources/styles/main.css");
        // The manifest lists resources/img/pic.png, which is not in the tree.
        book.LoadWarnings.Should().Contain(w => w.Contains("pic.png", StringComparison.Ordinal));
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void GetBook_Epub3WithoutNavDocument_CreatesOneAndWarns()
    {
        using TempDir temp = new();
        string staged = TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, temp.Combine("staged"));

        File.Delete(Path.Combine(staged, "EPUB", "nav.xhtml"));
        string opfPath = Path.Combine(staged, "EPUB", "package.opf");
        string opf = File.ReadAllText(opfPath)
            .Replace("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n", string.Empty, StringComparison.Ordinal);
        File.WriteAllText(opfPath, opf);

        string epub = EpubBuilder.BuildInto(staged, temp, "no-nav.epub");

        using Book book = new ImportEpub(epub).GetBook();

        book.LoadWarnings.Should().Contain(CoreStrings.Get("LoadWarning_NavCreated"));
        book.GetAllResources().OfType<HtmlResource>()
            .Should().Contain(h => h.Filename == "nav.xhtml", "Signet creates the missing navigation document");
    }

    [Fact]
    public void GetBook_NotWellFormedXhtml_ImportsWithWarning()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.LoadWarnings.Should().Contain(w => w.Contains("well-formed", StringComparison.OrdinalIgnoreCase));
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void GetBook_MissingMimetype_ImportsWithWarning()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("missing-mimetype"), temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.LoadWarnings.Should().Contain(w => w.Contains("mimetype", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBook_NoRootfile_ThrowsEpubLoadException()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("no-rootfile"), temp);

        Action act = () => new ImportEpub(epub).GetBook();

        act.Should().Throw<EpubLoadException>();
    }

    [Fact]
    public void GetBook_BadOpfXml_EitherImportsWithWarningOrThrows()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("bad-opf-xml"), temp);

        try
        {
            using Book book = new ImportEpub(epub).GetBook();
            book.LoadWarnings.Should().Contain(w => w.Contains("OPF", StringComparison.Ordinal));
        }
        catch (EpubLoadException)
        {
            // also acceptable
        }
    }

    [Fact]
    public void GetBook_MissingFile_ThrowsEpubLoadException()
    {
        Action act = () => new ImportEpub(Path.Combine(Path.GetTempPath(), "signet-does-not-exist.epub")).GetBook();

        act.Should().Throw<EpubLoadException>();
    }

    [Fact]
    public void GetBook_ObfuscatedFonts_DeobfuscatesBothAlgorithmsOnImport()
    {
        const string identifier = "urn:uuid:5b2e8c1a-0000-4000-8000-0000000000ab";
        string fixtureDir = CorpusPaths.Epub3ObfuscatedFonts;

        byte[] idpfGolden = File.ReadAllBytes(Path.Combine(fixtureDir, "EPUB", "fonts", "idpf-font.ttf"));
        byte[] adobeGolden = File.ReadAllBytes(Path.Combine(fixtureDir, "EPUB", "fonts", "adobe-font.otf"));

        using TempDir temp = new();
        // A copy of the fixture in which the fonts are obfuscated, as in a real packed EPUB.
        string staged = TestFs.CopyDirectory(fixtureDir, temp.Combine("staged"));
        FontObfuscation.ObfuscateFile(
            Path.Combine(staged, "EPUB", "fonts", "idpf-font.ttf"), OcfReader.IdpfFontAlgorithmId, identifier);
        FontObfuscation.ObfuscateFile(
            Path.Combine(staged, "EPUB", "fonts", "adobe-font.otf"), OcfReader.AdobeFontAlgorithmId, identifier);

        string epub = EpubBuilder.BuildInto(staged, temp, "obfuscated-fonts.epub");

        using Book book = new ImportEpub(epub).GetBook();

        FontResource idpf = book.GetAllResources().OfType<FontResource>()
            .Single(f => f.Filename == "idpf-font.ttf");
        FontResource adobe = book.GetAllResources().OfType<FontResource>()
            .Single(f => f.Filename == "adobe-font.otf");

        idpf.ObfuscationAlgorithm.Should().Be(OcfReader.IdpfFontAlgorithmId);
        adobe.ObfuscationAlgorithm.Should().Be(OcfReader.AdobeFontAlgorithmId);

        File.ReadAllBytes(idpf.FullPath).Should().Equal(idpfGolden, "the import deobfuscates the IDPF font to its source form");
        File.ReadAllBytes(adobe.FullPath).Should().Equal(adobeGolden, "the import deobfuscates the Adobe font to its source form");
    }

    [Fact]
    public void GetBook_ExposesMetadata_FromOpf()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp);

        using Book book = new ImportEpub(epub).GetBook();

        book.GetMetadataValues("dc:title").Should().ContainSingle().Which.Should().Be("Minimal EPUB 2");
        book.GetMetadata().Should().Contain(m => m.Name == "dc:language" && m.Content == "en");
    }
}
