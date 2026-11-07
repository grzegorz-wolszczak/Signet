using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="ManifestMediaTypeValidator"/>: the media types of the manifest against the file extensions.</summary>
public sealed class ManifestMediaTypeValidatorTests
{
    private static Book Open(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }

    [Fact]
    public void Validate_returns_no_results_for_a_wellformed_book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Open(temp);

        // Act
        IReadOnlyList<ValidationResult> results = ManifestMediaTypeValidator.Validate(book);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_media_type_mismatch_with_a_fix_at_the_opf()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Open(temp);
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        ManifestEntry htmlEntry = document.Manifest.First(e => e.Href.EndsWith(".xhtml", StringComparison.Ordinal));
        htmlEntry.MediaType = "text/plain";
        opf.SetOpfDocument(document);

        // Act
        IReadOnlyList<ValidationResult> results = ManifestMediaTypeValidator.Validate(book);

        // Assert
        ValidationResult result = results.Should().ContainSingle(r => r.Message.Contains(htmlEntry.Href, StringComparison.Ordinal)).Subject;
        result.Severity.Should().Be(ValidationSeverity.Warning);
        result.BookPath.Should().Be(opf.BookPath);
        result.Code.Should().Be("Validation_OpfMediaTypeMismatch");
        result.Fix.Should().BeOfType<ManifestMediaTypeFix>();
    }
}
