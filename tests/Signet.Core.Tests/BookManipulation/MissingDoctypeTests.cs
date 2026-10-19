using System;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// A missing DOCTYPE (typical calibre output) is a non-blocking warning: <see cref="WellFormedChecker"/>
/// reports it in <see cref="WellFormedResult.Warning"/>, whole-book operations run on such files,
/// the validator reports a warning, and Mend / Prettify add a DOCTYPE only on request.
/// </summary>
public sealed class MissingDoctypeTests
{
    private const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

    private const string BodyWithoutDoctype =
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
        "<head>\n  <title>t</title>\n</head>\n" +
        "<body>\n  <p>x</p>\n</body>\n" +
        "</html>\n";

    private static readonly Regex Doctype = new("<!DOCTYPE[^>]*>", RegexOptions.IgnoreCase);

    private static Book LoadDeepFolders(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.EdgeDeepFolders, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadMalformed(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("not-wellformed-xhtml"), temp);
        return new ImportEpub(epub).GetBook();
    }

    private static HtmlResource RemoveDoctype(HtmlResource html)
    {
        html.SetText(Doctype.Replace(html.GetText(), string.Empty));
        return html;
    }

    // ------------------------------------------------------------- WellFormedChecker --- //

    [Fact]
    public void CheckXhtmlStructure_without_doctype_is_well_formed_with_a_warning_on_the_xml_declaration()
    {
        // Arrange
        string source = XmlDeclaration + "\n" + BodyWithoutDoctype;

        // Act
        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "2.0");

        // Assert
        result.IsWellFormed.Should().BeTrue();
        result.Warning.Should().NotBeNull();
        result.Warning!.Kind.Should().Be(WellFormedWarningKind.MissingDoctype);
        result.Warning.Offset.Should().Be(0);
        result.Warning.Length.Should().Be(XmlDeclaration.Length);
        result.Warning.Message.Should().Contain("DOCTYPE");
    }

    [Fact]
    public void CheckXhtmlStructure_without_doctype_and_xml_declaration_points_the_warning_at_html()
    {
        // Arrange
        const string prefix = "\n";
        string source = prefix + BodyWithoutDoctype;

        // Act
        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "3.0");

        // Assert
        result.IsWellFormed.Should().BeTrue();
        result.Warning!.Offset.Should().Be(prefix.Length);
        source.Substring(result.Warning.Offset, result.Warning.Length).Should().StartWith("<html");
    }

    [Fact]
    public void CheckXhtmlStructure_with_doctype_has_no_warning()
    {
        // Arrange
        string source = XmlDeclaration + "\n<!DOCTYPE html>\n" + BodyWithoutDoctype;

        // Act
        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "3.0");

        // Assert
        result.IsWellFormed.Should().BeTrue();
        result.Warning.Should().BeNull();
        WellFormedChecker.IsMissingDoctype(source, "3.0").Should().BeFalse();
    }

    [Fact]
    public void Missing_doctype_never_hides_a_real_well_formedness_error()
    {
        // Arrange
        string source = XmlDeclaration + "\n<html><head><title>t</title></head><body><p>x</body></html>";

        // Act
        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "2.0");

        // Assert
        result.IsWellFormed.Should().BeFalse();
        result.Warning.Should().BeNull();
        WellFormedChecker.IsMissingDoctype(source, "2.0").Should().BeFalse();
    }

    // -------------------------------------------------------------------- CleanSource --- //

    [Theory]
    [InlineData("2.0")]
    [InlineData("3.0")]
    public void Mend_and_PrettyPrint_keep_a_missing_doctype_missing_unless_asked_to_add_it(string version)
    {
        // Arrange
        string source = XmlDeclaration + "\n" + BodyWithoutDoctype;

        // Act & Assert
        CleanSource.Mend(source, version, addMissingDoctype: false).Should().NotContain("<!DOCTYPE");

        // Act & Assert
        CleanSource.PrettyPrint(source, false, version, null, null, null, addMissingDoctype: false)
            .Should().NotContain("<!DOCTYPE");

        // Act & Assert
        CleanSource.Mend(source, version, addMissingDoctype: true).Should().Contain("<!DOCTYPE");

        // Act & Assert
        CleanSource.PrettyPrint(source, false, version, null, null, null, addMissingDoctype: true)
            .Should().Contain("<!DOCTYPE");
    }

    [Fact]
    public void Mend_without_adding_doctype_still_produces_a_well_formed_document_with_the_xml_declaration()
    {
        // Arrange
        string source = XmlDeclaration + "\n" + BodyWithoutDoctype;

        // Act
        string mended = CleanSource.Mend(source, "2.0", addMissingDoctype: false);

        // Assert
        mended.Should().StartWith(XmlDeclaration);
        WellFormedChecker.CheckXhtmlStructure(mended, "2.0").IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void An_existing_doctype_is_kept_when_adding_is_disabled()
    {
        // Arrange
        string source = XmlDeclaration + "\n<!DOCTYPE html>\n" + BodyWithoutDoctype;

        // Act & Assert
        CleanSource.Mend(source, "3.0", addMissingDoctype: false).Should().Contain("<!DOCTYPE html>");

        // Act & Assert
        CleanSource.PrettyPrint(source, false, "3.0", null, null, null, addMissingDoctype: false)
            .Should().Contain("<!DOCTYPE html>");
    }

    // --------------------------------------------------------------------------- Book --- //

    [Fact]
    public void FindHtmlMissingDoctype_lists_the_files_without_doctype()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        // Act & Assert
        book.FindHtmlMissingDoctype().Should().ContainSingle().Which.Should().BeSameAs(stripped);

        // Act & Assert
        book.FindHtmlMissingDoctype(book.GetHtmlResources().Skip(1)).Should().BeEmpty();
    }

    [Fact]
    public void FindHtmlMissingDoctype_is_empty_when_a_file_is_not_well_formed()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            RemoveDoctype(html);
        }

        // Act & Assert
        book.FindHtmlMissingDoctype().Should().BeEmpty();
    }

    [Fact]
    public void Whole_book_operations_are_not_blocked_by_a_missing_doctype()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        // Act & Assert
        book.AddSoftHyphens().Skipped.Should().BeEmpty();

        // Act & Assert
        book.PrettyPrintAllHtml(addMissingDoctype: false).Skipped.Should().BeEmpty();

        // Act & Assert
        book.RemoveSoftHyphens().Skipped.Should().BeEmpty();

        // Act & Assert
        book.PrepareClassRename().Renamer.Should().NotBeNull();

        // Assert
        stripped.GetText().Should().NotContain("<!DOCTYPE");
    }

    [Fact]
    public void PrettyPrintAllHtml_adds_the_doctype_when_asked_to()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        // Act
        book.PrettyPrintAllHtml(addMissingDoctype: true).Skipped.Should().BeEmpty();

        // Assert
        stripped.GetText().Should().Contain("<!DOCTYPE");
    }

    [Fact]
    public void MendAllHtml_adds_the_doctype_only_when_asked_to()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        // Act
        book.MendAllHtml(addMissingDoctype: false);

        // Assert
        stripped.GetText().Should().NotContain("<!DOCTYPE");

        // Act
        book.MendAllHtml(addMissingDoctype: true);

        // Assert
        stripped.GetText().Should().Contain("<!DOCTYPE");
    }

    [Fact]
    public void Operations_that_reserialize_html_never_add_a_doctype_silently()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        // Act & Assert
        book.LinkStylesheetsToResources(new[] { stripped }, Array.Empty<string>()).Skipped.Should().BeEmpty();

        // Act & Assert
        CleanSource.Mend(stripped.GetText(), "2.0").Should().NotContain("<!DOCTYPE");

        // Assert
        stripped.GetText().Should().NotContain("<!DOCTYPE");
    }

    // ------------------------------------------------------------------ BookValidator --- //

    [Fact]
    public void ValidateCurrentBook_reports_a_missing_doctype_as_a_warning()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        // Act
        var results = BookValidator.ValidateCurrentBook(book).Where(r => r.BookPath == stripped.BookPath).ToList();

        // Assert
        results.Should().ContainSingle(r => r.Message.Contains("DOCTYPE", StringComparison.Ordinal))
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
        results.Should().NotContain(r => r.Severity == ValidationSeverity.Error);
    }
}
