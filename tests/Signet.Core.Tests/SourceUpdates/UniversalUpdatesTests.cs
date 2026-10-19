using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.SourceUpdates;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>
/// Tests for <see cref="UniversalUpdates"/> — orchestrating reference updates across the whole book
/// after a resource is renamed.
/// </summary>
public sealed class UniversalUpdatesTests
{
    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xD9 };

    [Fact]
    public void Perform_ImageRenamed_UpdatesReferencingHtmlResource()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");

        string imageSourcePath = temp.Combine("cover.jpg");
        File.WriteAllBytes(imageSourcePath, JpegBytes);
        Resource image = book.GetFolderKeeper().AddContentFileToFolder(
            imageSourcePath, bookPath: "OEBPS/Images/cover.jpg");

        HtmlResource html = book.GetHtmlResources()[0];
        html.SetText(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
            "<body><img src=\"../Images/cover.jpg\" alt=\"c\"/></body></html>\n");

        string oldImageBookPath = image.BookPath;
        Resource[] toRename = { image };
        string[] newNames = { "cover-renamed.jpg" };

        // Act
        book.GetFolderKeeper().BulkRenameResources(toRename, newNames);
        image.CurrentBookRelPath = oldImageBookPath;

        // Arrange
        Dictionary<string, string> updates = new() { [oldImageBookPath] = image.BookPath };

        // Act
        UniversalUpdates.Perform(book, updates);

        // Assert
        html.GetText().Should().Contain("src=\"../Images/cover-renamed.jpg\"");
        html.CurrentBookRelPath.Should().Be(html.BookPath);
    }

    [Fact]
    public void Perform_CssRenamed_UpdatesLinkInHtmlAndImageUrlInCss()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");

        string cssSourcePath = temp.Combine("style.css");
        File.WriteAllText(cssSourcePath, "body { background: url(../Images/cover.jpg); }");
        Resource css = book.GetFolderKeeper().AddContentFileToFolder(
            cssSourcePath, bookPath: "OEBPS/Styles/style.css");
        ((CssResource)css).SetText("body { background: url(../Images/cover.jpg); }");

        string imageSourcePath = temp.Combine("cover.jpg");
        File.WriteAllBytes(imageSourcePath, JpegBytes);
        Resource image = book.GetFolderKeeper().AddContentFileToFolder(
            imageSourcePath, bookPath: "OEBPS/Images/cover.jpg");

        HtmlResource html = book.GetHtmlResources()[0];
        html.SetText(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title>" +
            "<link rel=\"stylesheet\" href=\"../Styles/style.css\" type=\"text/css\"/></head>" +
            "<body><p>x</p></body></html>\n");

        string oldCssBookPath = css.BookPath;
        string oldImageBookPath = image.BookPath;
        Resource[] toRename = { css, image };
        string[] newNames = { "style-new.css", "cover-new.jpg" };

        // Act
        book.GetFolderKeeper().BulkRenameResources(toRename, newNames);
        css.CurrentBookRelPath = oldCssBookPath;
        image.CurrentBookRelPath = oldImageBookPath;

        Dictionary<string, string> updates = new()
        {
            [oldCssBookPath] = css.BookPath,
            [oldImageBookPath] = image.BookPath,
        };

        // Act
        UniversalUpdates.Perform(book, updates);

        // Assert
        html.GetText().Should().Contain("href=\"../Styles/style-new.css\"");
        ((CssResource)css).GetText().Should().Contain("url(../Images/cover-new.jpg)");
    }

    [Fact]
    public void Perform_EmptyUpdates_DoesNothing()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource html = book.GetHtmlResources()[0];
        string original = html.GetText();

        // Act
        UniversalUpdates.Perform(book, new Dictionary<string, string>());

        // Assert
        html.GetText().Should().Be(original);
    }
}
