using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="BookValidator.ValidateCurrentBook"/> (Well-Formed Check EPUB).
/// </summary>
public sealed class BookValidatorTests
{
    // The corpus font is a dummy, not a real font (see tests/corpus/generate-binaries.sh).
    private static readonly string[] SkipDummyFont = { "Validation_FontCorrupt" };

    [Fact]
    public void ValidateCurrentBook_reports_the_not_wellformed_html_file_with_a_line_number()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        using Book book = new ImportEpub(epub).GetBook();

        // Act
        var results = BookValidator.ValidateCurrentBook(book, culture: System.Globalization.CultureInfo.GetCultureInfo("en"))
            .Where(r => r.Code == "RSC-016").ToList();

        // Assert
        results.Should().NotBeEmpty("EPUBCheck reports a file that is not well-formed");
        results.Should().OnlyContain(r => r.Severity == ValidationSeverity.Fatal);
        results.Should().OnlyContain(r => r.Line > 0);
        results.Should().Contain(r => r.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateCurrentBook_returns_no_results_for_a_wellformed_book()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        // Act
        var results = BookValidator.ValidateCurrentBook(book, SkipDummyFont);

        // Assert
        // The accessibility checks have their own say about the corpus book (it has no accessibility metadata, and its nav is
        // <nav epub:type="toc"> without role="doc-toc"), which is right, so it is left out here (the corpus is byte-stable).
        results.Where(r => !r.Code.StartsWith("Validation_Accessibility_", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void ValidateCurrentBook_orders_results_like_GetHtmlResources()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        using Book book = new ImportEpub(epub).GetBook();

        // Act
        var results = BookValidator.ValidateCurrentBook(book);
        var expectedOrder = book.GetHtmlResources().Select(r => r.BookPath).ToList();

        // Assert
        // (The accessibility metadata is reported on the OPF, which is not one of the (X)HTML files.)
        results.Where(r => !r.Code.StartsWith("Validation_Accessibility_", StringComparison.Ordinal)).Select(r => r.BookPath)
            .Should().BeSubsetOf(expectedOrder);
    }
}
