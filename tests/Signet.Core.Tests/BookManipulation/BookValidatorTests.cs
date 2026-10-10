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
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        using Book book = new ImportEpub(epub).GetBook();

        var results = BookValidator.ValidateCurrentBook(book);

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.Severity == ValidationSeverity.Error);
        results.Should().OnlyContain(r => r.Line > 0);
        results.Should().Contain(r => r.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateCurrentBook_returns_no_results_for_a_wellformed_book()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        var results = BookValidator.ValidateCurrentBook(book, SkipDummyFont);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCurrentBook_orders_results_like_GetHtmlResources()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        using Book book = new ImportEpub(epub).GetBook();

        var results = BookValidator.ValidateCurrentBook(book);
        var expectedOrder = book.GetHtmlResources().Select(r => r.BookPath).ToList();

        results.Select(r => r.BookPath).Should().BeSubsetOf(expectedOrder);
    }
}
