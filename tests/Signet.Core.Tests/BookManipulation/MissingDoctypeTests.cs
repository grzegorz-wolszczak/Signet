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
        string source = XmlDeclaration + "\n" + BodyWithoutDoctype;

        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "2.0");

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
        const string prefix = "\n";
        string source = prefix + BodyWithoutDoctype;

        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "3.0");

        result.IsWellFormed.Should().BeTrue();
        result.Warning!.Offset.Should().Be(prefix.Length);
        source.Substring(result.Warning.Offset, result.Warning.Length).Should().StartWith("<html");
    }

    [Fact]
    public void CheckXhtmlStructure_with_doctype_has_no_warning()
    {
        string source = XmlDeclaration + "\n<!DOCTYPE html>\n" + BodyWithoutDoctype;

        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "3.0");

        result.IsWellFormed.Should().BeTrue();
        result.Warning.Should().BeNull();
        WellFormedChecker.IsMissingDoctype(source, "3.0").Should().BeFalse();
    }

    [Fact]
    public void Missing_doctype_never_hides_a_real_well_formedness_error()
    {
        string source = XmlDeclaration + "\n<html><head><title>t</title></head><body><p>x</body></html>";

        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source, "2.0");

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
        string source = XmlDeclaration + "\n" + BodyWithoutDoctype;

        CleanSource.Mend(source, version, addMissingDoctype: false).Should().NotContain("<!DOCTYPE");
        CleanSource.PrettyPrint(source, false, version, null, null, null, addMissingDoctype: false)
            .Should().NotContain("<!DOCTYPE");

        CleanSource.Mend(source, version, addMissingDoctype: true).Should().Contain("<!DOCTYPE");
        CleanSource.PrettyPrint(source, false, version, null, null, null, addMissingDoctype: true)
            .Should().Contain("<!DOCTYPE");
    }

    [Fact]
    public void Mend_without_adding_doctype_still_produces_a_well_formed_document_with_the_xml_declaration()
    {
        string source = XmlDeclaration + "\n" + BodyWithoutDoctype;

        string mended = CleanSource.Mend(source, "2.0", addMissingDoctype: false);

        mended.Should().StartWith(XmlDeclaration);
        WellFormedChecker.CheckXhtmlStructure(mended, "2.0").IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void An_existing_doctype_is_kept_when_adding_is_disabled()
    {
        string source = XmlDeclaration + "\n<!DOCTYPE html>\n" + BodyWithoutDoctype;

        CleanSource.Mend(source, "3.0", addMissingDoctype: false).Should().Contain("<!DOCTYPE html>");
        CleanSource.PrettyPrint(source, false, "3.0", null, null, null, addMissingDoctype: false)
            .Should().Contain("<!DOCTYPE html>");
    }

    // --------------------------------------------------------------------------- Book --- //

    [Fact]
    public void FindHtmlMissingDoctype_lists_the_files_without_doctype()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        book.FindHtmlMissingDoctype().Should().ContainSingle().Which.Should().BeSameAs(stripped);
        book.FindHtmlMissingDoctype(book.GetHtmlResources().Skip(1)).Should().BeEmpty();
    }

    [Fact]
    public void FindHtmlMissingDoctype_is_empty_when_a_file_is_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadMalformed(temp);
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            RemoveDoctype(html);
        }

        book.FindHtmlMissingDoctype().Should().BeEmpty();
    }

    [Fact]
    public void Whole_book_operations_are_not_blocked_by_a_missing_doctype()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        book.AddSoftHyphens().Applied.Should().BeTrue();
        book.PrettyPrintAllHtml(addMissingDoctype: false).Applied.Should().BeTrue();
        book.RemoveSoftHyphens().Applied.Should().BeTrue();
        book.PrepareClassRename().Renamer.Should().NotBeNull();
        stripped.GetText().Should().NotContain("<!DOCTYPE");
    }

    [Fact]
    public void PrettyPrintAllHtml_adds_the_doctype_when_asked_to()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        book.PrettyPrintAllHtml(addMissingDoctype: true).Applied.Should().BeTrue();

        stripped.GetText().Should().Contain("<!DOCTYPE");
    }

    [Fact]
    public void MendAllHtml_adds_the_doctype_only_when_asked_to()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        book.MendAllHtml(addMissingDoctype: false);
        stripped.GetText().Should().NotContain("<!DOCTYPE");

        book.MendAllHtml(addMissingDoctype: true);
        stripped.GetText().Should().Contain("<!DOCTYPE");
    }

    [Fact]
    public void Operations_that_reserialize_html_never_add_a_doctype_silently()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        book.LinkStylesheetsToResources(new[] { stripped }, Array.Empty<string>()).Applied.Should().BeTrue();
        CleanSource.Mend(stripped.GetText(), "2.0").Should().NotContain("<!DOCTYPE");

        stripped.GetText().Should().NotContain("<!DOCTYPE");
    }

    // ------------------------------------------------------------------ BookValidator --- //

    [Fact]
    public void ValidateCurrentBook_reports_a_missing_doctype_as_a_warning()
    {
        using TempDir temp = new();
        using Book book = LoadDeepFolders(temp);
        HtmlResource stripped = RemoveDoctype(book.GetHtmlResources()[0]);

        var results = BookValidator.ValidateCurrentBook(book).Where(r => r.BookPath == stripped.BookPath).ToList();

        results.Should().ContainSingle(r => r.Message.Contains("DOCTYPE", StringComparison.Ordinal))
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
        results.Should().NotContain(r => r.Severity == ValidationSeverity.Error);
    }
}
