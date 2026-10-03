using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;
using Signet.Core.Localization;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="CrossFileStructureValidator"/> (checking parsing/structure at the level of the
/// whole book).
/// </summary>
public sealed class CrossFileStructureValidatorTests
{
    [Fact]
    public void Validate_returns_no_results_for_a_wellformed_book()
    {
        using Book book = OpenMutableBook();

        var results = CrossFileStructureValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_duplicate_id_across_files()
    {
        using Book book = OpenMutableBook();
        HtmlResource nav = FindHtml(book, "nav.xhtml");
        HtmlResource chapter1 = FindHtml(book, "chapter1.xhtml");

        nav.SetText(nav.GetText().Replace("<h1>", "<h1 id=\"shared-anchor\">"));
        chapter1.SetText(chapter1.GetText().Replace("<h1>", "<h1 id=\"shared-anchor\">"));

        var results = CrossFileStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == nav.BookPath &&
            r.Message.Contains("shared-anchor") &&
            r.Message.Contains(chapter1.BookPath));
        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == chapter1.BookPath &&
            r.Message.Contains("shared-anchor") &&
            r.Message.Contains(nav.BookPath));
    }

    [Fact]
    public void Validate_reports_oversized_html_file()
    {
        using Book book = OpenMutableBook();
        HtmlResource chapter1 = FindHtml(book, "chapter1.xhtml");
        string padding = new string('a', CrossFileStructureValidator.MaxHtmlFileSizeBytes + 1);
        chapter1.SetText(chapter1.GetText().Replace("</body>", $"<p>{padding}</p></body>"));

        var results = CrossFileStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == chapter1.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_FileTooLarge"), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_bare_text_directly_in_body()
    {
        using Book book = OpenMutableBook();
        HtmlResource chapter1 = FindHtml(book, "chapter1.xhtml");
        chapter1.SetText(chapter1.GetText().Replace("<body>", "<body>Goly tekst bez elementu blokowego."));

        var results = CrossFileStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == chapter1.BookPath &&
            r.Message == CoreStrings.Get("Validation_BareBodyText"));
    }

    [Fact]
    public void Validate_reports_invalid_utf8_bytes_on_disk()
    {
        using Book book = OpenMutableBook();
        HtmlResource chapter1 = FindHtml(book, "chapter1.xhtml");

        byte[] invalidUtf8 = Encoding.UTF8.GetBytes(chapter1.GetText());
        // We insert an invalid UTF-8 sequence (a lone continuation byte) in the middle of the file.
        byte[] corrupted = invalidUtf8.Take(invalidUtf8.Length / 2)
            .Append((byte)0x80)
            .Concat(invalidUtf8.Skip(invalidUtf8.Length / 2))
            .ToArray();
        File.WriteAllBytes(chapter1.FullPath, corrupted);

        var results = CrossFileStructureValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == chapter1.BookPath &&
            r.Message.Contains("UTF-8"));
    }

    private static HtmlResource FindHtml(Book book, string fileName) =>
        book.GetAllResources()
            .OfType<HtmlResource>()
            .First(r => r.BookPath.EndsWith(fileName, StringComparison.Ordinal));

    private static Book OpenMutableBook()
    {
        TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }
}
