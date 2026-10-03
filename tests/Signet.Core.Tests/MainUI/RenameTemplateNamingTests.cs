using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="RenameTemplateNaming"/> — the "Rename with a template" logic.</summary>
public sealed class RenameTemplateNamingTests
{
    [Theory]
    [InlineData("Section", "0001", new string[0], "Section0001")]
    [InlineData("Section", "0001", new[] { "Section0001.xhtml" }, "Section0002")]
    [InlineData("Section", "0001", new[] { "Section0001.xhtml", "Section0002.xhtml" }, "Section0003")]
    public void GetFirstAvailableTemplateName_SkipsUsedNames(
        string basePart, string numberString, string[] existing, string expected)
    {
        string result = RenameTemplateNaming.GetFirstAvailableTemplateName(existing, basePart, numberString);
        result.Should().Be(expected);
    }

    [Fact]
    public void BuildSequentialFilenames_GeneratesIncrementingNamesWithPadding()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        HtmlResource b = book.CreateEmptyHtmlFile();
        HtmlResource c = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a, b, c };
        List<string> allFilenames = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? result = RenameTemplateNaming.BuildSequentialFilenames(
            resources, "Chapter0001.xhtml", allFilenames, out string? error);

        error.Should().BeNull();
        result.Should().Equal("Chapter0001.xhtml", "Chapter0002.xhtml", "Chapter0003.xhtml");
    }

    [Fact]
    public void BuildSequentialFilenames_TemplateWithoutExtension_KeepsOldExtension()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a };
        List<string> allFilenames = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? result = RenameTemplateNaming.BuildSequentialFilenames(
            resources, "Chapter0001", allFilenames, out string? error);

        error.Should().BeNull();
        result.Should().Equal("Chapter0001.xhtml");
    }

    [Fact]
    public void BuildSequentialFilenames_TemplateWithoutTrailingDigits_ReusesOldNameWithNewExtension()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        string oldNameNoExt = a.Filename[..a.Filename.LastIndexOf('.')];
        List<Resource> resources = new() { a };
        List<string> allFilenames = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? result = RenameTemplateNaming.BuildSequentialFilenames(
            resources, ".html", allFilenames, out string? error);

        error.Should().BeNull();
        result.Should().Equal(oldNameNoExt + ".html");
    }

    [Fact]
    public void BuildSequentialFilenames_CollisionWithExistingFile_ReturnsNullAndError()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        HtmlResource existing = book.CreateEmptyHtmlFile();
        // Rename "existing" so its name collides with the template output for "a".
        Resource[] toRename = { existing };
        string[] toRenameNames = { "Chapter0002.xhtml" };
        book.GetFolderKeeper().BulkRenameResources(toRename, toRenameNames);
        HtmlResource b = book.CreateEmptyHtmlFile();
        List<Resource> resources = new() { a, b };
        List<string> allFilenames = new(book.GetFolderKeeper().GetAllFilenames());

        IReadOnlyList<string>? result = RenameTemplateNaming.BuildSequentialFilenames(
            resources, "Chapter0001.xhtml", allFilenames, out string? error);

        result.Should().BeNull();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Section0001.xhtml", true)]
    [InlineData("bad:name.xhtml", false)]
    [InlineData("bad/name.xhtml", false)]
    public void IsTemplateNameValid_RejectsForbiddenCharacters(string template, bool expectedValid)
    {
        bool valid = RenameTemplateNaming.IsTemplateNameValid(template, out string? error);

        valid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            error.Should().NotBeNull();
        }
    }
}
