using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="OpfStructureValidator"/> (validating the
/// OPF/manifest/spine structure).
/// </summary>
public sealed class OpfStructureValidatorTests
{
    [Fact]
    public void Validate_returns_no_results_for_a_wellformed_book()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        var results = OpfStructureValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_missing_unique_identifier()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        document.Metadata.RemoveAll(m => m.Name == "dc:identifier");
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error && r.Message.Contains("dc:identifier"));
    }

    [Fact]
    public void Validate_reports_empty_manifest_id()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        document.Manifest[0].Id = string.Empty;
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error && r.Message.Contains(LocalizedText.Fragment("Validation_OpfManifestItemNoId"), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_duplicate_manifest_id()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        string duplicated = document.Manifest[1].Id;
        document.Manifest[0].Id = duplicated;
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error && r.Message == CoreStrings.Format("Validation_OpfDuplicateId", duplicated, 2));
    }

    [Fact]
    public void Validate_reports_duplicate_manifest_href()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        document.Manifest[0].Href = document.Manifest[1].Href;
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning && r.Message.Contains(LocalizedText.Fragment("Validation_OpfDuplicateHref"), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_media_type_mismatch()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        ManifestEntry htmlEntry = document.Manifest.First(e => e.Href.EndsWith(".xhtml", StringComparison.Ordinal));
        htmlEntry.MediaType = "text/plain";
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning && r.Message.Contains(htmlEntry.Href));
    }

    [Fact]
    public void Validate_reports_dangling_spine_idref()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        document.Spine[0].IdRef = "does-not-exist-in-manifest";
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error && r.Message.Contains("does-not-exist-in-manifest"));
    }

    [Fact]
    public void Validate_reports_empty_spine()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        document.Spine.Clear();
        opf.SetOpfDocument(document);

        var results = OpfStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error && r.Message.Contains("<spine>"));
    }

    private static Book OpenMutableBook()
    {
        TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }
}
