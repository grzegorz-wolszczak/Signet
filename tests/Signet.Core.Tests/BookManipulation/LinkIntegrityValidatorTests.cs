using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="LinkIntegrityValidator"/> (checking
/// links/references: broken/dangling/case-mismatch).
/// </summary>
public sealed class LinkIntegrityValidatorTests
{
    [Fact]
    public void Validate_returns_no_results_for_a_wellformed_book()
    {
        using Book book = OpenMutableBook();

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_broken_html_link()
    {
        using Book book = OpenMutableBook();
        HtmlResource nav = FindHtml(book, "nav.xhtml");
        nav.SetText(nav.GetText().Replace("text/chapter1.xhtml\">Chapter 1", "text/does-not-exist.xhtml\">Chapter 1"));

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error &&
            r.BookPath == nav.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_DeadLink"), StringComparison.Ordinal) &&
            r.Message.Contains("does-not-exist.xhtml"));
    }

    [Fact]
    public void Validate_reports_broken_css_url()
    {
        using Book book = OpenMutableBook();
        CssResource css = book.GetCssResources().First();
        css.SetText(css.GetText() + "\n.missing { background: url(\"../images/missing.png\"); }\n");

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error &&
            r.BookPath == css.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_CssDeadUrl"), StringComparison.Ordinal) &&
            r.Message.Contains("missing.png"));
    }

    [Fact]
    public void Validate_reports_case_mismatch()
    {
        using Book book = OpenMutableBook();
        HtmlResource nav = FindHtml(book, "nav.xhtml");
        nav.SetText(nav.GetText().Replace("text/chapter1.xhtml\">Chapter 1", "text/Chapter1.xhtml\">Chapter 1"));

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == nav.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_LinkCaseMismatch"), StringComparison.Ordinal) &&
            r.Message.Contains("Chapter1.xhtml"));
    }

    [Fact]
    public void Validate_reports_bad_fragment_in_other_document()
    {
        using Book book = OpenMutableBook();
        HtmlResource nav = FindHtml(book, "nav.xhtml");
        nav.SetText(nav.GetText().Replace(
            "text/chapter1.xhtml\">Chapter 1",
            "text/chapter1.xhtml#does-not-exist\">Chapter 1"));

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error &&
            r.BookPath == nav.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_BadFragment"), StringComparison.Ordinal) &&
            r.Message.Contains("does-not-exist"));
    }

    [Fact]
    public void Validate_reports_bad_fragment_within_same_document()
    {
        using Book book = OpenMutableBook();
        HtmlResource nav = FindHtml(book, "nav.xhtml");
        nav.SetText(nav.GetText().Replace(
            "<h1>Table of Contents</h1>",
            "<h1>Table of Contents</h1><p><a href=\"#does-not-exist\">skip</a></p>"));

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Error &&
            r.BookPath == nav.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_BadFragmentSameFile"), StringComparison.Ordinal) &&
            r.Message.Contains("#does-not-exist"));
    }

    [Fact]
    public void Validate_reports_resource_missing_from_manifest()
    {
        using Book book = OpenMutableBook();
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        document.Manifest.RemoveAll(e => e.Href.EndsWith("figure.png", StringComparison.Ordinal));
        opf.SetOpfDocument(document);

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.Message.Contains("figure.png") &&
            r.Message.Contains(LocalizedText.Fragment("Validation_LinkTargetNotInManifest"), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ignores_external_references()
    {
        using Book book = OpenMutableBook();
        HtmlResource nav = FindHtml(book, "nav.xhtml");
        nav.SetText(nav.GetText().Replace(
            "<h1>Table of Contents</h1>",
            "<h1>Table of Contents</h1><p><a href=\"https://example.com/x\">external</a> <a href=\"mailto:a@b.com\">mail</a></p>"));

        var results = LinkIntegrityValidator.Validate(book);

        results.Should().BeEmpty();
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
