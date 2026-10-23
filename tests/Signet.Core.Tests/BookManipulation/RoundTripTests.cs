using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Importers;
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
    [Fact]
    public void WriteBook_reports_the_files_copied_and_the_entries_written()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();
        List<BookSaveProgress> reports = new();

        // Act
        new ExportEpub(book).WriteBook(temp.Combine("out.epub"), progress: new SyncProgress(reports.Add));

        // Assert
        reports.Select(r => r.Stage).Distinct().Should().Equal(BookSaveStage.Preparing, BookSaveStage.Copying, BookSaveStage.Archiving);
        reports.Single(r => r.Stage == BookSaveStage.Preparing).Total.Should().Be(0, "the preparation has no count");
        reports.Where(r => r.Stage == BookSaveStage.Copying).Select(r => r.Done).Should().BeInAscendingOrder().And.StartWith(0);
        reports.Where(r => r.Stage == BookSaveStage.Archiving).Select(r => r.Total).Distinct().Should().ContainSingle().Which.Should().BeGreaterThan(0);
    }

    private sealed class SyncProgress : IProgress<BookSaveProgress>
    {
        private readonly Action<BookSaveProgress> _report;

        public SyncProgress(Action<BookSaveProgress> report) => _report = report;

        public void Report(BookSaveProgress value) => _report(value);
    }

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
        // Arrange
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(corpusDir, temp, "original.epub");

        string exported = temp.Combine("exported.epub");

        // Act
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: false);
        }

        EpubComparisonResult result = EpubComparer.Compare(original, exported, EpubComparisonOptions.RoundTrip);

        // Assert
        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Fact]
    public void RoundTrip_WithMetadataStamping_StillEquivalentUnderRoundTripComparison()
    {
        // Arrange
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp, "original.epub");

        string exported = temp.Combine("stamped.epub");

        // Act
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: true);
        }

        // Assert
        EpubComparisonResult result = EpubComparer.Compare(original, exported, EpubComparisonOptions.RoundTrip);
        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Fact]
    public void RoundTrip_ExportedEpub_HasMimetypeFirstAndStored()
    {
        // Arrange
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp, "original.epub");

        string exported = temp.Combine("exported.epub");

        // Act
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: false);
        }

        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(exported);

        // Assert
        zip.Entries[0].FullName.Should().Be("mimetype");
        zip.Entries[0].CompressedLength.Should().Be(zip.Entries[0].Length, "mimetype must be STORED");
        zip.GetEntry(".signet-lock").Should().BeNull("the working directory lock file must not end up in the EPUB");
    }

    [Fact]
    public void RoundTrip_ReimportingExportedEpub_KeepsResourcesAndSpineOrder()
    {
        // Arrange
        using TempDir temp = new();
        string original = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp, "original.epub");

        string exported = temp.Combine("exported.epub");

        // Act
        using (Book first = new ImportEpub(original).GetBook())
        {
            new ExportEpub(first).WriteBook(exported, stampMetadata: false);
        }

        using Book second = new ImportEpub(exported).GetBook();

        // Assert
        second.GetNcx().Should().NotBeNull();
        second.GetHtmlResources().Select(h => h.Filename)
            .Should().ContainInOrder("chapter1.xhtml", "chapter2.xhtml");
    }

    [Fact]
    public void RoundTrip_ObfuscatedFonts_DeobfuscateOnImportThenReobfuscateOnExport()
    {
        // Arrange
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

        // Act
        using (Book book = new ImportEpub(original).GetBook())
        {
            new ExportEpub(book).WriteBook(exported, stampMetadata: false);
        }

        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(exported);

        // Assert
        zip.GetEntry("META-INF/encryption.xml").Should().NotBeNull("the export regenerates encryption.xml for obfuscated fonts");
        ReadEntry(zip, "EPUB/fonts/idpf-font.ttf").Should().Equal(idpfExpected, "eksport reobfuskuje font IDPF");
        ReadEntry(zip, "EPUB/fonts/adobe-font.otf").Should().Equal(adobeExpected, "eksport reobfuskuje font Adobe");

        // Ponowny import znow deobfuskuje do postaci golden.
        // Act
        using Book reimported = new ImportEpub(exported).GetBook();

        // Assert
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
