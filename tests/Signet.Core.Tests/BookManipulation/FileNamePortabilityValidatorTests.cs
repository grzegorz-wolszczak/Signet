using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;
using Signet.Core.Localization;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="FileNamePortabilityValidator"/> (checking file
/// names for portability between file systems).
/// </summary>
/// <remarks>
/// The name rules are checked with the pure function <see cref="FileNamePortabilityValidator.CheckFileName"/>,
/// not by renaming a real file: names containing <c>:</c> or reserved ones
/// (<c>CON</c>) cannot be created on Windows, so <c>MoveTo</c> returned <c>false</c> there,
/// the bookpath stayed the old one, and the test failed on an empty result list instead of on the rule.
/// </remarks>
public sealed class FileNamePortabilityValidatorTests
{
    [Fact]
    public void Validate_returns_no_results_for_a_wellformed_book()
    {
        using Book book = OpenMutableBook();

        var results = FileNamePortabilityValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void CheckFileName_reports_windows_forbidden_character()
    {
        FileNamePortabilityValidator.CheckFileName("chapter1:bad.xhtml")
            .Should().ContainSingle().Which.Should().Contain(LocalizedText.Fragment("Validation_FileNameIllegalChar")).And.Contain(":");
    }

    [Fact]
    public void CheckFileName_reports_windows_reserved_name()
    {
        FileNamePortabilityValidator.CheckFileName("CON.xhtml")
            .Should().ContainSingle().Which.Should().Contain(LocalizedText.Fragment("Validation_FileNameReserved")).And.Contain("CON");
    }

    [Fact]
    public void CheckFileName_reports_trailing_dot_and_trailing_space()
    {
        FileNamePortabilityValidator.CheckFileName("chapter1.xhtml.")
            .Should().ContainSingle().Which.Should().Be(CoreStrings.Format("Validation_FileNameTrailingDot", "chapter1.xhtml."));

        FileNamePortabilityValidator.CheckFileName("chapter1.xhtml ")
            .Should().ContainSingle().Which.Should().Be(CoreStrings.Format("Validation_FileNameTrailingSpace", "chapter1.xhtml "));
    }

    [Fact]
    public void CheckFileName_reports_non_nfc_normalized_filename()
    {
        // "e" + combining acute (U+0065 U+0301) instead of the single "é" — NFD form.
        string nfd = "chapteér1.xhtml";

        FileNamePortabilityValidator.CheckFileName(nfd)
            .Should().ContainSingle().Which.Should().Contain("NFC");
    }

    [Fact]
    public void CheckFileName_accepts_a_portable_name()
    {
        FileNamePortabilityValidator.CheckFileName("chapter1.xhtml").Should().BeEmpty();
    }

    [Fact]
    public void CheckFileName_rejects_null()
    {
        FluentActions.Invoking(() => FileNamePortabilityValidator.CheckFileName(null!))
            .Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Wiring the rules to <see cref="Book"/>: the message from <c>CheckFileName</c> must reach the
    /// <see cref="ValidationResult"/> with the resource bookpath. A name with a trailing dot is legal
    /// on every system, so <c>MoveTo</c> succeeds here.
    /// </summary>
    [Fact]
    public void Validate_maps_a_bad_filename_onto_the_resource_bookpath()
    {
        using Book book = OpenMutableBook();
        HtmlResource chapter1 = FindHtml(book, "chapter1.xhtml");

        chapter1.MoveTo("Text/chapter1.xhtml.")
            .Should().BeTrue("without a successful rename the test would not check the rule");

        var results = FileNamePortabilityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == chapter1.BookPath &&
            r.Message.Contains(LocalizedText.Fragment("Validation_FileNameTrailingDot"), StringComparison.Ordinal));
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
