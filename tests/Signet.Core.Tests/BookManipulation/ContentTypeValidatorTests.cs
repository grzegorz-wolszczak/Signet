using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="ContentTypeValidator"/> (checking the mimetype against the actual
/// content of the file).
/// </summary>
public sealed class ContentTypeValidatorTests
{
    [Fact]
    public void Validate_returns_no_results_for_a_wellformed_book()
    {
        using Book book = OpenMutableBook();

        var results = ContentTypeValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_png_declared_as_jpeg()
    {
        using Book book = OpenMutableBook();
        ImageResource cover = FindImage(book, "cover.png");
        cover.MediaType = "image/jpeg";

        var results = ContentTypeValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath == cover.BookPath &&
            r.Message.Contains("image/jpeg") &&
            r.Message.Contains("image/png"));
    }

    [Fact]
    public void Validate_ignores_svg_resources()
    {
        using Book book = OpenMutableBook();
        ImageResource cover = FindImage(book, "cover.png");
        cover.MediaType = "image/svg+xml";

        var results = ContentTypeValidator.Validate(book);

        results.Should().BeEmpty();
    }

    private static ImageResource FindImage(Book book, string fileName) =>
        book.GetAllResources()
            .OfType<ImageResource>()
            .First(r => r.BookPath.EndsWith(fileName, System.StringComparison.Ordinal));

    private static Book OpenMutableBook()
    {
        TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }
}
