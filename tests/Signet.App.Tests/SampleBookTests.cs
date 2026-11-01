using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The book the previews of the Preferences window are made over (<see cref="SampleBook"/>).</summary>
public sealed class SampleBookTests
{
    /// <summary>
    /// The chapter the Code Editor preview shows holds the characters that are drawn as nothing, so that the three
    /// settings of Code Editor › Appearance show in the preview what they do (a word hyphenated with soft hyphens, a
    /// non-breaking space and a zero-width space).
    /// </summary>
    [Fact]
    public void The_chapter_of_the_code_editor_preview_holds_the_characters_that_are_drawn_as_nothing()
    {
        // Arrange
        using Book book = SampleBook.Create();

        // Act
        string chapter = book.GetFolderKeeper().GetResourceTypeList<HtmlResource>()
            .First(h => h.Filename == "Chapter01.xhtml").GetText();

        // Assert
        chapter.Should().Contain("­", "a soft hyphen").And.Contain(" ", "a non-breaking space");
        chapter.Should().Contain("​", "a zero-width space");
        chapter.Should().NotContain("{SHY}", "the placeholders of the template are replaced");
    }
}
