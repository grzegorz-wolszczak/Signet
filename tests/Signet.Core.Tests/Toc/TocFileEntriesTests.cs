using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>TOC entries pointing to whole files: <see cref="TocFileEntries"/>.</summary>
public sealed class TocFileEntriesTests
{
    [Fact]
    public void Removing_a_files_entries_keeps_their_children_pointing_to_other_files()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource first = book.GetHtmlResourcesExcludingNav()[0];
        HtmlResource second = book.CreateEmptyHtmlFile();

        // Act
        TocEditModel.Save(book, new TocEntry
        {
            IsRoot = true,
            Children =
            {
                new TocEntry
                {
                    Text = "Part",
                    Target = first.BookPath,
                    Children = { new TocEntry { Text = "Chapter", Target = second.BookPath } },
                },
                new TocEntry { Text = "Part note", Target = first.BookPath + "#note" },
            },
        });

        // Act & Assert
        TocFileEntries.RemoveEntriesForFiles(book, new[] { first.BookPath }).Should().BeTrue();

        TocEntry root = TocEditModel.GetRootTocEntry(book);

        // Assert
        root.Children.Select(e => e.Text).Should().Equal("Chapter");
        TocFileEntries.RemoveEntriesForFiles(book, new[] { first.BookPath }).Should().BeFalse("nothing is left to remove");
    }

    [Fact]
    public void Removing_from_an_epub2_ncx_leaves_a_start_entry_when_nothing_else_remains()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource only = book.GetHtmlResources()[0];
        HtmlResource other = book.CreateEmptyHtmlFile();

        // Act
        TocEditModel.Save(book, new TocEntry { IsRoot = true, Children = { new TocEntry { Text = "Gone", Target = other.BookPath } } });

        // Act & Assert
        TocFileEntries.RemoveEntriesForFiles(book, new[] { other.BookPath }).Should().BeTrue();

        // Assert
        NcxDocument ncx = book.GetNcx()!.GetNcxDocument();
        ncx.NavMap.Should().ContainSingle().Which.ContentSrc.Should().EndWith(only.Filename);
    }

    [Fact]
    public void Setting_a_file_entry_twice_keeps_a_single_entry()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource file = book.CreateEmptyHtmlFile();

        // Act & Assert
        TocFileEntries.SetFileEntry(book, file, "Notes").Should().BeTrue();

        // Act & Assert
        TocFileEntries.SetFileEntry(book, file, "Notes").Should().BeFalse();

        // Assert
        TocEditModel.GetRootTocEntry(book).Children.Count(e => e.Text == "Notes").Should().Be(1);
    }
}
