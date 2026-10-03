using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// <see cref="ImportEpub"/> + <see cref="ExportEpub"/> close the round trip.
/// For every valid EPUB from the corpus, the round trip produces a file that
/// <see cref="EpubComparer"/> considers equivalent to the input (with XML normalization and the allowed
/// differences: <c>dcterms:modified</c>, the generator meta, attribute order).
/// </summary>
public sealed class RoundTripTests
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
    public void RoundTrip_ValidCorpus_ProducesEquivalentEpub(string corpusDir)
    {
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(corpusDir, temp, "original.epub");

        string exported = temp.Combine("exported.epub");
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: false);
        }

        EpubComparisonResult result = EpubComparer.Compare(original, exported, EpubComparisonOptions.RoundTrip);
        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Fact]
    public void RoundTrip_WithMetadataStamping_StillEquivalentUnderRoundTripComparison()
    {
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp, "original.epub");

        string exported = temp.Combine("stamped.epub");
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: true);
        }

        EpubComparisonResult result = EpubComparer.Compare(original, exported, EpubComparisonOptions.RoundTrip);
        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Fact]
    public void RoundTrip_ExportedEpub_HasMimetypeFirstAndStored()
    {
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp, "original.epub");

        string exported = temp.Combine("exported.epub");
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: false);
        }

        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(exported);
        zip.Entries[0].FullName.Should().Be("mimetype");
        zip.Entries[0].CompressedLength.Should().Be(zip.Entries[0].Length, "mimetype must be STORED");
        zip.GetEntry(".signet-lock").Should().BeNull("the working directory lock file must not end up in the EPUB");
    }

    [Fact]
    public void RoundTrip_ReimportingExportedEpub_KeepsResourcesAndSpineOrder()
    {
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp, "original.epub");

        string exported = temp.Combine("exported.epub");
        using (Book first = new ImportEpub(original).GetBook())
        {
            new ExportEpub(first).WriteBook(exported, stampMetadata: false);
        }

        using Book second = new ImportEpub(exported).GetBook();
        second.GetNcx().Should().NotBeNull();
        second.GetHtmlResources().Select(h => h.Filename)
            .Should().ContainInOrder("chapter1.xhtml", "chapter2.xhtml");
    }

    [Fact]
    public void RoundTrip_ObfuscatedFonts_DeobfuscateOnImportThenReobfuscateOnExport()
    {
        const string identifier = "urn:uuid:5b2e8c1a-0000-4000-8000-0000000000ab";
        string fixtureDir = CorpusPaths.Epub3ObfuscatedFonts;

        // Fixture trzyma fonty ZDEOBFUSKOWANE (golden). Oczekiwana postac obfuskowana =
        // FontObfuscation(golden) — computed here, without committing the bytes.
        byte[] idpfExpected = ObfuscatedCopy(
            Path.Combine(fixtureDir, "EPUB", "fonts", "idpf-font.ttf"), OcfReader.IdpfFontAlgorithmId, identifier);
        byte[] adobeExpected = ObfuscatedCopy(
            Path.Combine(fixtureDir, "EPUB", "fonts", "adobe-font.otf"), OcfReader.AdobeFontAlgorithmId, identifier);

        using TempDir temp = new();

        // Build a "real" EPUB — the fonts on disk are obfuscated, encryption.xml is in the tree.
        string staged = TestFs.CopyDirectory(fixtureDir, temp.Combine("staged"));
        FontObfuscation.ObfuscateFile(
            Path.Combine(staged, "EPUB", "fonts", "idpf-font.ttf"), OcfReader.IdpfFontAlgorithmId, identifier);
        FontObfuscation.ObfuscateFile(
            Path.Combine(staged, "EPUB", "fonts", "adobe-font.otf"), OcfReader.AdobeFontAlgorithmId, identifier);
        string original = EpubBuilder.BuildInto(staged, temp, "original.epub");

        string exported = temp.Combine("exported.epub");
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: false);
        }

        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(exported);

        zip.GetEntry("META-INF/encryption.xml").Should().NotBeNull("the export regenerates encryption.xml for obfuscated fonts");
        ReadEntry(zip, "EPUB/fonts/idpf-font.ttf").Should().Equal(idpfExpected, "eksport reobfuskuje font IDPF");
        ReadEntry(zip, "EPUB/fonts/adobe-font.otf").Should().Equal(adobeExpected, "eksport reobfuskuje font Adobe");

        // Ponowny import znow deobfuskuje do postaci golden.
        using Book reimported = new ImportEpub(exported).GetBook();
        FontResource idpf = reimported.GetAllResources().OfType<FontResource>().Single(f => f.Filename == "idpf-font.ttf");
        idpf.ObfuscationAlgorithm.Should().Be(OcfReader.IdpfFontAlgorithmId);
        File.ReadAllBytes(idpf.FullPath).Should()
            .Equal(File.ReadAllBytes(Path.Combine(fixtureDir, "EPUB", "fonts", "idpf-font.ttf")));
    }

    private static byte[] ObfuscatedCopy(string sourceFile, string algorithm, string identifier)
    {
        string tempCopy = Path.Combine(Path.GetTempPath(), "signet-golden-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.Copy(sourceFile, tempCopy, overwrite: true);
            FontObfuscation.ObfuscateFile(tempCopy, algorithm, identifier);
            return File.ReadAllBytes(tempCopy);
        }
        finally
        {
            if (File.Exists(tempCopy))
            {
                File.Delete(tempCopy);
            }
        }
    }

    private static byte[] ReadEntry(System.IO.Compression.ZipArchive zip, string entryName)
    {
        System.IO.Compression.ZipArchiveEntry entry = zip.GetEntry(entryName)
            ?? throw new InvalidOperationException($"No entry {entryName}");
        using Stream stream = entry.Open();
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
