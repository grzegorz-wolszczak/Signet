using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>A web or mail address clicked in the preview: asked about, then opened in the system's application.</summary>
public sealed class PreviewExternalLinkTests
{
    // A preview with a page shown (only then does it know which addresses are inside the book).
    private static PreviewViewModel New(Book book, out List<Uri> opened, out List<Uri> asked) =>
        Show(new PreviewViewModel(new StatusBarService()) { DebounceInterval = TimeSpan.Zero }, book, out opened, out asked);

    private static PreviewViewModel Show(PreviewViewModel vm, Book book, out List<Uri> opened, out List<Uri> asked)
    {
        vm.SetBook(book);
        vm.ShowResource(book.GetHtmlResourcesExcludingNav().First());
        List<Uri> openedList = new();
        List<Uri> askedList = new();
        vm.AttachExternalUrlOpener(address =>
        {
            openedList.Add(address);
            return null;
        });
        vm.ExternalLinkConfirmationRequested += (_, address) => askedList.Add(address);
        opened = openedList;
        asked = askedList;
        return vm;
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com")]
    [InlineData("mailto:someone@example.com")]
    public void A_web_or_mail_address_is_asked_about_and_the_page_does_not_go_there(string address)
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using PreviewViewModel sut = New(book, out List<Uri> opened, out List<Uri> asked);
        Uri target = new(address);

        // Act
        bool allowed = sut.AllowNavigation(target);

        // Assert
        allowed.Should().BeFalse();
        asked.Should().Equal(target);
        opened.Should().BeEmpty("nothing is opened before the user agrees");
    }

    [Fact]
    public void Agreeing_opens_the_address_and_keeps_asking()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using PreviewViewModel sut = New(book, out List<Uri> opened, out List<Uri> asked);
        Uri target = new("https://example.com/a");
        sut.AllowNavigation(target);

        // Act
        sut.AnswerExternalLink(target, open: true, dontAskAgain: false);

        // Assert
        opened.Should().Equal(target);
        sut.ConfirmExternalLinks.Should().BeTrue();

        // Act
        sut.AllowNavigation(target);

        // Assert
        asked.Should().HaveCount(2);
    }

    [Fact]
    public void Refusing_opens_nothing()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using PreviewViewModel sut = New(book, out List<Uri> opened, out _);
        Uri target = new("https://example.com/a");
        sut.AllowNavigation(target);

        // Act
        sut.AnswerExternalLink(target, open: false, dontAskAgain: true);

        // Assert
        opened.Should().BeEmpty();
        sut.ConfirmExternalLinks.Should().BeTrue("a refusal does not turn the question off");
    }

    [Fact]
    public void Only_one_question_is_open_at_a_time()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using PreviewViewModel sut = New(book, out _, out List<Uri> asked);
        Uri target = new("https://example.com/a");

        // Act
        sut.AllowNavigation(target);
        sut.AllowNavigation(target);

        // Assert
        asked.Should().ContainSingle();
    }

    [Fact]
    public void Turning_the_question_off_opens_later_addresses_at_once_and_tells_the_host()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using PreviewViewModel sut = New(book, out List<Uri> opened, out List<Uri> asked);
        int disabled = 0;
        sut.ExternalLinkConfirmationDisabled += (_, _) => disabled++;
        Uri first = new("https://example.com/a");
        Uri second = new("https://example.com/b");
        sut.AllowNavigation(first);
        sut.AnswerExternalLink(first, open: true, dontAskAgain: true);

        // Act
        sut.AllowNavigation(second);

        // Assert
        disabled.Should().Be(1);
        asked.Should().ContainSingle();
        opened.Should().Equal(first, second);
    }

    [Fact]
    public void A_failure_to_open_is_reported_on_the_status_bar()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using StatusBarService statusBar = new();
        using PreviewViewModel sut = Show(
            new PreviewViewModel(statusBar) { ConfirmExternalLinks = false }, book, out _, out _);
        sut.AttachExternalUrlOpener(_ => "no browser");
        string? message = null;
        statusBar.MessageShown += (_, notification) => message = notification.Message;

        // Act
        sut.AllowNavigation(new Uri("https://example.com/a"));

        // Assert
        message.Should().Contain("no browser");
    }

    [Fact]
    public void Other_addresses_outside_the_book_stay_blocked()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        using PreviewViewModel sut = New(book, out List<Uri> opened, out List<Uri> asked);

        // Act
        bool allowed = sut.AllowNavigation(new Uri("ftp://example.com/file"));

        // Assert
        allowed.Should().BeFalse();
        asked.Should().BeEmpty();
        opened.Should().BeEmpty();
    }
}
