using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The "Accessibility" dialog (<see cref="AccessibilityViewModel"/>).</summary>
public sealed class AccessibilityViewModelTests
{
    private static Book NewBook(string version, string body)
    {
        Book book = BookCreator.CreateNewBook(version);
        book.GetHtmlResourcesExcludingNav()[0].SetText(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
            + body + "\n</body>\n</html>");
        return book;
    }

    private static AccessibilityViewModel Open(Book book, List<AccessibilitySaveRequest>? saved = null, bool saveResult = true, List<(string, int)>? opened = null) =>
        new(book, (path, offset) => opened?.Add((path, offset)), request =>
        {
            saved?.Add(request);
            return Task.FromResult(saveResult);
        });

    [Fact]
    public void The_dialog_opens_with_what_the_book_has_and_nothing_to_save()
    {
        // Arrange
        using Book book = NewBook("3.0", "<img src=\"a.png\" alt=\"A cat\"/><img src=\"b.png\"/>");
        AccessibilityLanguage.SetLanguages(book, ["pl", "en"]);
        book.GetOpf().AddMetaProperty("schema:accessMode", "textual");
        book.GetOpf().AddMetaProperty("schema:accessMode", "mine");
        book.GetOpf().AddMetaProperty("schema:accessibilitySummary", "Summary");

        // Act
        using AccessibilityViewModel sut = Open(book);

        // Assert
        sut.IsEpub3.Should().BeTrue();
        sut.Languages.Select(l => l.Code).Should().Equal("pl", "en");
        sut.FilesWithoutLanguage.Should().BeGreaterThanOrEqualTo(1);
        sut.Images.Should().HaveCount(2);
        sut.ImagesSummary.Should().Contain("1").And.Contain("2");
        sut.Summary.Should().Be("Summary");
        sut.AccessModes.Where(m => m.IsChecked).Select(m => m.Value).Should().Equal("textual", "mine");
        sut.AccessModes.Select(m => m.Value).Should().Contain("visual").And.EndWith("mine", "a value the vocabulary does not know is kept after the known ones");
        sut.HasUnsavedChanges.Should().BeFalse();
        sut.CanSave.Should().BeFalse();
        sut.BuildRequest().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Languages_are_added_by_code_or_by_name_and_removed_and_reordered_but_never_all_removed()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p>");
        AccessibilityLanguage.SetLanguages(book, ["pl"]);
        using AccessibilityViewModel sut = Open(book);

        // Act
        sut.NewLanguage = "en-GB";
        bool canAdd = sut.CanAddLanguage;
        sut.AddLanguageCommand.Execute(null);
        sut.NewLanguage = "pl";

        // Assert
        canAdd.Should().BeTrue();
        sut.CanAddLanguage.Should().BeFalse("it is there already");
        sut.Languages.Select(l => l.Code).Should().Equal("pl", "en-GB");
        sut.NewLanguage.Should().Be("pl", "the box is cleared only after a language was added");

        // Act
        sut.MakeMainLanguageCommand.Execute(sut.Languages[1]);

        // Assert
        sut.Languages.Select(l => l.Code).Should().Equal("en-GB", "pl");
        sut.BuildRequest().Languages.Should().Equal("en-GB", "pl");
        sut.HasUnsavedChanges.Should().BeTrue();

        // Act
        sut.RemoveLanguageCommand.Execute(sut.Languages[0]);
        sut.RemoveLanguageCommand.Execute(sut.Languages[0]);

        // Assert
        sut.Languages.Should().ContainSingle().Which.Code.Should().Be("pl");
        sut.BuildRequest().Languages.Should().BeNull("the language is what the book had");
    }

    [Fact]
    public void A_language_typed_as_a_name_or_as_nonsense_is_resolved_or_refused()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p>");
        using AccessibilityViewModel sut = Open(book);

        // Act & Assert
        sut.NewLanguage = "not a language!";
        sut.CanAddLanguage.Should().BeFalse();
        sut.NewLanguage = string.Empty;
        sut.CanAddLanguage.Should().BeFalse();
        sut.NewLanguage = sut.KnownLanguageNames.First();
        sut.CanAddLanguage.Should().BeTrue("the name of a language the application knows is a language");
    }

    [Fact]
    public void Choosing_what_to_do_with_the_language_of_the_files_is_a_change()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p>");
        using AccessibilityViewModel sut = Open(book);

        // Act
        sut.HtmlLanguage = HtmlLanguageChoice.OnlyMissing;

        // Assert
        sut.HasUnsavedChanges.Should().BeTrue();
        sut.BuildRequest().HtmlLanguage.Should().Be(HtmlLanguageChoice.OnlyMissing);
    }

    [Fact]
    public void Editing_the_description_of_an_image_or_marking_it_as_decoration_is_an_edit_and_the_filter_follows()
    {
        // Arrange
        using Book book = NewBook("3.0", "<img src=\"a.png\" alt=\"A cat\"/><img src=\"b.png\"/>\n<img src=\"c.png\"/>");
        using AccessibilityViewModel sut = Open(book);
        AccessibilityImageItem second = sut.Images[1];
        AccessibilityImageItem third = sut.Images[2];

        // Act
        second.Description = "  A dog ";
        third.IsDecorative = true;

        // Assert
        second.IsChanged.Should().BeTrue();
        third.IsChanged.Should().BeTrue();
        sut.Images[0].IsChanged.Should().BeFalse();
        sut.BuildRequest().ImageEdits.Select(e => (e.Description, e.Decorative)).Should().Equal(("A dog", false), (string.Empty, true));
        sut.ImagesSummary.Should().StartWith(string.Empty).And.Contain("0");

        // Act
        second.Description = string.Empty;
        sut.ShowOnlyUndescribed = true;

        // Assert
        sut.Images.Should().Equal(second);

        // Act
        second.Description = "A cat";
        second.Description = string.Empty;
        sut.ShowOnlyUndescribed = false;

        // Assert
        sut.Images.Should().HaveCount(3);
    }

    [Fact]
    public void The_place_of_an_image_is_opened_in_the_code_editor()
    {
        // Arrange
        using Book book = NewBook("3.0", "<img src=\"a.png\"/>");
        List<(string, int)> opened = new();
        using AccessibilityViewModel sut = Open(book, opened: opened);

        // Act
        sut.OpenImage(sut.Images[0]);

        // Assert
        opened.Should().Equal((sut.Images[0].Image.BookPath, sut.Images[0].Image.Offset));
        sut.Images[0].Place.Should().Contain(":8").And.Contain("img");
    }

    [Fact]
    public void The_metadata_checklists_and_the_summary_make_a_request()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p>");
        using AccessibilityViewModel sut = Open(book);

        // Act
        sut.AccessModes.Single(m => m.Value == "textual").IsChecked = true;
        sut.Hazards.Single(m => m.Value == "none").IsChecked = true;
        sut.Features.Single(m => m.Value == "tableOfContents").IsChecked = true;
        sut.Summary = "  Text only. ";

        // Assert
        AccessibilityMetadataValues metadata = sut.BuildRequest().Metadata!;
        metadata.AccessModes.Should().Equal("textual");
        metadata.Hazards.Should().Equal("none");
        metadata.Features.Should().Equal("tableOfContents");
        metadata.Summary.Should().Be("  Text only. ");
        sut.AccessModes.Single(m => m.Value == "textual").DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_epub2_book_has_no_metadata_to_edit()
    {
        // Arrange
        using Book book = NewBook("2.0", "<p>x</p>");

        // Act
        using AccessibilityViewModel sut = Open(book);

        // Assert
        sut.IsEpub3.Should().BeFalse();
        sut.AccessModes.Should().BeEmpty();
        sut.BuildRequest().Metadata.Should().BeNull();
    }

    [Fact]
    public async Task Save_hands_the_request_over_and_closes_the_dialog_only_when_it_was_written()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p>");
        List<AccessibilitySaveRequest> saved = new();
        using AccessibilityViewModel sut = Open(book, saved, saveResult: false);
        bool done = false;
        sut.Done += (_, _) => done = true;
        sut.Summary = "Hello";

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        saved.Should().ContainSingle().Which.Metadata!.Summary.Should().Be("Hello");
        done.Should().BeFalse("the save did not happen");
    }

    [Fact]
    public async Task Save_closes_the_dialog_when_it_was_written_and_is_off_without_a_change_or_while_the_book_is_busy()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p>");
        using AccessibilityViewModel sut = Open(book);
        bool done = false;
        sut.Done += (_, _) => done = true;

        // Act & Assert
        sut.SaveCommand.CanExecute(null).Should().BeFalse("nothing changed");
        sut.Summary = "Hello";
        sut.SaveCommand.CanExecute(null).Should().BeTrue();
        sut.IsBookOperationBusy = true;
        sut.SaveCommand.CanExecute(null).Should().BeFalse();
        sut.IsBookOperationBusy = false;

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        done.Should().BeTrue();
    }
}
