using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of the "Link Stylesheets" / "Link Javascripts" operations at the <see cref="Book"/> level:
/// maps, relinking in <c>&lt;head&gt;</c>, the well-formed guard.
/// </summary>
public sealed class BookLinkResourcesTests
{
    private static Book LoadWithNcx(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static List<HtmlResource> Chapters(Book book) =>
        book.GetHtmlResources().Where(h => h.Filename.StartsWith("chapter", StringComparison.Ordinal)).ToList();

    [Fact]
    public void GetStylesheetsMap_marks_shared_stylesheet_as_included()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);

        // Act
        IReadOnlyList<LinkableResourceEntry> map = book.GetStylesheetsMap(Chapters(book));

        // Assert
        map.Should().ContainSingle();
        map[0].Included.Should().BeTrue();
        map[0].BookPath.Should().EndWith("style.css");
    }

    [Fact]
    public void LinkStylesheetsToResources_with_empty_list_removes_links_from_head()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        List<HtmlResource> chapters = Chapters(book);

        // Act
        FileBatchResult result = book.LinkStylesheetsToResources(chapters, Array.Empty<string>());

        // Assert
        result.Skipped.Should().BeEmpty();
        result.Processed.Should().Be(chapters.Count);
        foreach (HtmlResource chapter in chapters)
        {
            chapter.GetText().Should().NotContain("stylesheet");
        }

        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void LinkStylesheetsToResources_links_multiple_stylesheets_in_order()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        HtmlResource chapter1 = Chapters(book).First();

        CssResource extra = book.CreateEmptyCssFile();
        string existing = book.GetCssResources().Single(c => c.Filename == "style.css").BookPath;

        // Act
        FileBatchResult result = book.LinkStylesheetsToResources(
            new[] { chapter1 }, new[] { extra.BookPath, existing });

        // Assert
        result.Skipped.Should().BeEmpty();
        string text = chapter1.GetText();
        text.IndexOf(extra.Filename, StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("style.css", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkStylesheetsToResources_skips_a_file_that_is_not_well_formed_and_links_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        List<HtmlResource> chapters = Chapters(book);

        chapters.Should().HaveCountGreaterThan(1);

        chapters[0].SetText("<html><head><body><p>broken");

        // Act
        FileBatchResult result = book.LinkStylesheetsToResources(chapters, Array.Empty<string>());

        // Assert
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].BookPath.Should().Be(chapters[0].BookPath);
        result.Processed.Should().Be(chapters.Count - 1);
        result.NothingProcessed.Should().BeFalse();
        chapters[0].GetText().Should().Be("<html><head><body><p>broken");
        chapters[1].GetText().Should().NotContain("stylesheet");
    }

    [Fact]
    public void LinkStylesheetsToResources_skips_a_file_that_fails_while_being_processed()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        List<HtmlResource> chapters = Chapters(book);

        chapters.Should().HaveCountGreaterThan(1);

        ThrowingHtmlResource failing = new(temp.Path, temp.Combine("failing.xhtml"));
        List<HtmlResource> resources = new() { failing, chapters[0] };

        // Act
        FileBatchResult result = book.LinkStylesheetsToResources(resources, Array.Empty<string>());

        // Assert
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].BookPath.Should().Be(failing.BookPath);
        result.Skipped[0].Reason.Should().Be(ThrowingHtmlResource.Message);
        result.Processed.Should().Be(1);
        chapters[0].GetText().Should().NotContain("stylesheet");
    }

    [Fact]
    public void LinkJavascriptsToResources_skips_a_file_that_is_not_well_formed_and_links_the_rest()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        List<HtmlResource> chapters = Chapters(book);

        chapters.Should().HaveCountGreaterThan(1);

        chapters[0].SetText("<html><head><body><p>broken");
        MiscTextResource js = book.CreateEmptyJsFile();

        // Act
        FileBatchResult result = book.LinkJavascriptsToResources(chapters, new[] { js.BookPath });

        // Assert
        result.Skipped.Should().ContainSingle().Which.BookPath.Should().Be(chapters[0].BookPath);
        chapters[0].GetText().Should().Be("<html><head><body><p>broken");
        chapters[1].GetText().Should().Contain(js.Filename);
    }

    private sealed class ThrowingHtmlResource : HtmlResource
    {
        public const string Message = "cannot read";

        public ThrowingHtmlResource(string mainFolder, string fullFilePath)
            : base(mainFolder, fullFilePath)
        {
        }

        public override string GetText() => throw new InvalidOperationException(Message);
    }

    [Fact]
    public void LinkJavascriptsToResources_adds_script_and_marks_scripted_property()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        HtmlResource chapter1 = Chapters(book).First();
        MiscTextResource js = book.CreateEmptyJsFile();

        // Act
        FileBatchResult result = book.LinkJavascriptsToResources(new[] { chapter1 }, new[] { js.BookPath });

        // Assert
        result.Skipped.Should().BeEmpty();
        chapter1.GetText().Should().Contain(js.Filename);
        chapter1.GetText().Should().Contain("</script>");
    }
}
