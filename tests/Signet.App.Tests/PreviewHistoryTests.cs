using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The places the preview has shown, and the way back and forward through them (<see cref="PreviewHistory"/>).</summary>
public sealed class PreviewHistoryTests
{
    private static List<HtmlResource> Files(Book book, int count)
    {
        List<HtmlResource> files = new() { book.GetHtmlResourcesExcludingNav().First() };
        while (files.Count < count)
        {
            files.Add(book.CreateEmptyHtmlFile());
        }

        return files;
    }

    private static PreviewHistoryEntry At(HtmlResource resource, string? fragment = null, int? offset = null) =>
        new(resource, fragment, offset);

    [Fact]
    public void A_new_history_has_no_place_to_go_to()
    {
        // Arrange
        PreviewHistory sut = new();

        // Act & Assert
        sut.Current.Should().BeNull();
        sut.CanGoBack.Should().BeFalse();
        sut.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void Visiting_places_allows_going_back_and_then_forward()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        List<HtmlResource> files = Files(book, 3);
        PreviewHistory sut = new();
        sut.Visit(At(files[0]), null);
        sut.Visit(At(files[1]), null);
        sut.Visit(At(files[2]), null);

        // Act
        PreviewHistoryEntry? back = sut.Move(-1, null, _ => true);
        PreviewHistoryEntry? backAgain = sut.Move(-1, null, _ => true);
        PreviewHistoryEntry? tooFar = sut.Move(-1, null, _ => true);
        PreviewHistoryEntry? forward = sut.Move(1, null, _ => true);

        // Assert
        back!.Resource.Should().BeSameAs(files[1]);
        backAgain!.Resource.Should().BeSameAs(files[0]);
        tooFar.Should().BeNull();
        forward!.Resource.Should().BeSameAs(files[1]);
        sut.CanGoBack.Should().BeTrue();
        sut.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public void Visiting_the_place_that_is_shown_already_changes_nothing()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource file = Files(book, 1)[0];
        PreviewHistory sut = new();
        sut.Visit(At(file, "#a"), null);

        // Act
        bool changed = sut.Visit(At(file, "#a"), null);

        // Assert
        changed.Should().BeFalse();
        sut.Count.Should().Be(1);
    }

    [Fact]
    public void Visiting_a_place_after_going_back_forgets_the_places_ahead()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        List<HtmlResource> files = Files(book, 3);
        PreviewHistory sut = new();
        sut.Visit(At(files[0]), null);
        sut.Visit(At(files[1]), null);
        sut.Move(-1, null, _ => true);

        // Act
        sut.Visit(At(files[2]), null);

        // Assert
        sut.CanGoForward.Should().BeFalse();
        sut.Count.Should().Be(2);
        sut.Current!.Resource.Should().BeSameAs(files[2]);
    }

    [Fact]
    public void The_offset_the_reader_was_at_is_kept_for_the_way_back()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        List<HtmlResource> files = Files(book, 2);
        PreviewHistory sut = new();
        sut.Visit(At(files[0]), null);
        sut.Visit(At(files[1]), leavingOffset: 123);

        // Act
        PreviewHistoryEntry? back = sut.Move(-1, null, _ => true);

        // Assert
        back!.Offset.Should().Be(123);
    }

    [Fact]
    public void Places_that_cannot_be_shown_any_more_are_skipped_and_dropped()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        List<HtmlResource> files = Files(book, 4);
        PreviewHistory sut = new();
        foreach (HtmlResource file in files)
        {
            sut.Visit(At(file), null);
        }

        // Act
        PreviewHistoryEntry? back = sut.Move(-1, null, e => !ReferenceEquals(e.Resource, files[2]) && !ReferenceEquals(e.Resource, files[1]));

        // Assert
        back!.Resource.Should().BeSameAs(files[0]);
        sut.Count.Should().Be(2);
        sut.CanGoBack.Should().BeFalse();
        sut.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public void The_history_is_limited_and_forgets_the_oldest_place_first()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource file = Files(book, 1)[0];
        PreviewHistory sut = new();

        // Act
        for (int i = 0; i < PreviewHistoryConstants.MaxEntries + 10; i++)
        {
            sut.Visit(At(file, offset: i), null);
        }

        // Assert
        sut.Count.Should().Be(PreviewHistoryConstants.MaxEntries);
        sut.Current!.Offset.Should().Be(PreviewHistoryConstants.MaxEntries + 9);
    }
}
