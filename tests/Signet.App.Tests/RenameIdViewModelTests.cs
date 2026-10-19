using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>View model of the "Rename Id" window: validation and the change counter.</summary>
public sealed class RenameIdViewModelTests
{
    private const string Chapter =
        "<html><body><p id=\"note1\">a</p><a href=\"#note1\">back</a><p id=\"other\">b</p></body></html>";

    private static IdRenamer Renamer() =>
        new([new ClassRenameSource("ch.xhtml", Chapter)], [new ClassRenameSource("s.css", "#note1 { }\n")]);

    private static RenameIdViewModel Sut() => new(Renamer(), "note1");

    [Fact]
    public void Starts_with_the_old_name_and_cannot_be_accepted_until_it_changes()
    {
        // Arrange
        RenameIdViewModel sut = Sut();

        // Act & Assert
        sut.NewName.Should().Be("note1");
        sut.CanAccept.Should().BeFalse();
        sut.ValidationMessages.Should().Equal(Strings.Get("RenameId_SameAsOld"));

        // Act
        sut.NewName = "note2";

        // Assert
        sut.CanAccept.Should().BeTrue();
        sut.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_an_invalid_name()
    {
        // Arrange
        RenameIdViewModel sut = Sut();

        // Act
        sut.NewName = "1bad";

        // Assert
        sut.CanAccept.Should().BeFalse();
        sut.ValidationMessages.Should().Equal(Strings.Get("RenameId_Invalid"));
    }

    [Fact]
    public void Rejects_an_id_that_already_exists()
    {
        // Arrange
        RenameIdViewModel sut = Sut();

        // Act
        sut.NewName = "other";

        // Assert
        sut.ValidationMessages.Should().Equal(Strings.Format("RenameId_AlreadyExists", "other"));
    }

    [Fact]
    public void Rejects_an_empty_name()
    {
        // Arrange
        RenameIdViewModel sut = Sut();

        // Act
        sut.NewName = string.Empty;

        // Assert
        sut.ValidationMessages.Should().Equal(Strings.Get("RenameId_Empty"));
    }

    [Fact]
    public void The_label_and_the_summary_report_the_scale_of_the_whole_book_rename()
    {
        // Arrange
        RenameIdViewModel sut = Sut();

        // Act & Assert
        sut.IdLabel.Should().Be(Strings.Format("RenameIdWindow_Id", "note1"));
        sut.Summary.Should().Be(Strings.Format("RenameIdWindow_Summary", 1, 1, 1, 2));
    }

    [Fact]
    public void Rename_computes_the_result_for_the_entered_name()
    {
        // Arrange
        RenameIdViewModel sut = Sut();
        sut.NewName = "note2";

        // Act
        IdRenameResult result = sut.Rename();

        // Assert
        result.ChangedTexts["ch.xhtml"].Should().Contain("id=\"note2\"").And.Contain("href=\"#note2\"");
        result.ChangedTexts["s.css"].Should().Contain("#note2");
    }
}
