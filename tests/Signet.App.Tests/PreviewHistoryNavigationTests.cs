using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Back and forward in the preview: what is recorded and what a step does (<see cref="PreviewViewModel"/>).</summary>
public sealed class PreviewHistoryNavigationTests
{
    private static readonly string[] Blocks = { "p", "h1", "h6", "li", "blockquote", "td", "figcaption" };
    private static readonly string[] NotBlocks = { "div", "section", "ul", "table", "span", "a", "body" };

    private static PreviewViewModel New() => new(new StatusBarService()) { DebounceInterval = TimeSpan.Zero };

    private static Uri UrlOf(PreviewViewModel vm, HtmlResource resource, string? fragment = null) =>
        new UriBuilder(new Uri(Path.Combine(vm.MirrorRootPath!, resource.BookPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            Fragment = fragment ?? string.Empty,
        }.Uri;

    [Fact]
    public void A_new_preview_has_nowhere_to_go_back_or_forward()
    {
        // Arrange
        using PreviewViewModel sut = New();

        // Act & Assert
        sut.CanGoBack.Should().BeFalse();
        sut.CanGoForward.Should().BeFalse();
        sut.GoBackCommand.CanExecute(null).Should().BeFalse();
        sut.GoForwardCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Showing_another_file_can_be_undone_and_redone()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource first = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource second = book.CreateEmptyHtmlFile();
        using PreviewViewModel sut = New();
        List<Uri> navigated = new();
        List<int> activated = new();
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.CodeViewActivationRequested += (_, offset) => activated.Add(offset);
        sut.SetBook(book);
        sut.ShowResource(first);
        sut.ShowResource(second);
        navigated.Clear();

        // Act
        sut.GoBackCommand.Execute(null);

        // Assert
        navigated.Should().ContainSingle().Which.Should().Be(UrlOf(sut, first));
        activated.Should().Equal(-1);
        sut.CurrentBookPath.Should().Be(first.BookPath);
        sut.CanGoBack.Should().BeFalse();
        sut.CanGoForward.Should().BeTrue();

        // Act
        sut.GoForwardCommand.Execute(null);

        // Assert
        navigated.Last().Should().Be(UrlOf(sut, second));
        sut.CurrentBookPath.Should().Be(second.BookPath);
        sut.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void The_tab_a_step_activates_does_not_load_the_page_a_second_time()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource first = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource second = book.CreateEmptyHtmlFile();
        using PreviewViewModel sut = New();
        List<Uri> navigated = new();
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.CodeViewActivationRequested += (_, _) => sut.ShowResource(first);
        sut.SetBook(book);
        sut.ShowResource(first);
        sut.ShowResource(second);
        navigated.Clear();

        // Act
        sut.GoBackCommand.Execute(null);

        // Assert
        navigated.Should().ContainSingle();
        sut.CanGoForward.Should().BeTrue("activating the tab is not a new place");
    }

    [Fact]
    public void A_link_followed_in_the_page_is_a_place_to_come_back_from()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource other = book.CreateEmptyHtmlFile();
        chapter.SetText("<html><body><p><a href=\"" + other.Filename + "#part\">See part</a></p></body></html>");
        other.SetText("<html><body>\n<h2 id=\"part\">Part</h2>\n</body></html>");
        using PreviewViewModel sut = New();
        List<Uri> navigated = new();
        sut.NavigateRequested += (_, url) => navigated.Add(url);
        sut.SetBook(book);
        sut.ShowResource(chapter);
        sut.HandlePreviewMessage("signet-link:-1 " + UrlOf(sut, other, "part").AbsoluteUri);
        navigated.Clear();

        // Act
        sut.GoBackCommand.Execute(null);
        sut.GoForwardCommand.Execute(null);

        // Assert
        navigated.Should().Equal(UrlOf(sut, chapter), UrlOf(sut, other, "part"));
        sut.CurrentBookPath.Should().Be(other.BookPath);
    }

    [Fact]
    public void A_big_jump_of_the_caret_within_a_file_is_a_place_but_a_small_one_is_not()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        using PreviewViewModel sut = New();
        List<int> activated = new();
        sut.CodeViewActivationRequested += (_, offset) => activated.Add(offset);
        sut.SetBook(book);
        sut.ShowResource(chapter);
        sut.SyncCaretToPreview(10);

        // Act
        sut.SyncCaretToPreview(10 + PreviewHistoryConstants.LargeCaretJumpChars - 1);

        // Assert
        sut.CanGoBack.Should().BeFalse();

        // Act
        const int farAway = 20_000;
        sut.SyncCaretToPreview(farAway);

        // Assert
        sut.CanGoBack.Should().BeTrue();

        // Act
        sut.GoBackCommand.Execute(null);

        // Assert
        activated.Should().Equal(10 + PreviewHistoryConstants.LargeCaretJumpChars - 1);
        sut.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public void The_caret_move_caused_by_a_step_is_not_recorded()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(chapter);
        sut.SyncCaretToPreview(10);
        sut.SyncCaretToPreview(20_000);
        sut.CodeViewActivationRequested += (_, _) => sut.SyncCaretToPreview(90_000);

        // Act
        sut.GoBackCommand.Execute(null);

        // Assert
        sut.CanGoForward.Should().BeTrue();
        sut.CanGoBack.Should().BeFalse("nothing was added after the place the step went to");
    }

    [Fact]
    public void A_click_in_the_preview_far_from_the_caret_is_not_a_place()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(chapter);
        sut.SyncCaretToPreview(10);

        // Act — the click moves Code View, whose caret comes back
        sut.HandlePreviewMessage("signet-loc:30000");
        sut.SyncCaretToPreview(30_000);

        // Assert
        sut.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void Closing_everything_or_changing_the_book_forgets_the_history()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource first = book.GetHtmlResourcesExcludingNav().First();
        HtmlResource second = book.CreateEmptyHtmlFile();
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(first);
        sut.ShowResource(second);

        // Act
        sut.Clear();

        // Assert
        sut.CanGoBack.Should().BeFalse();

        // Arrange
        sut.ShowResource(first);
        sut.ShowResource(second);

        // Act
        sut.SetBook(null);

        // Assert
        sut.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void A_click_in_a_block_of_text_is_a_place_once_per_block()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        using PreviewViewModel sut = New();
        List<int> activated = new();
        sut.CodeViewActivationRequested += (_, offset) => activated.Add(offset);
        sut.SetBook(book);
        sut.ShowResource(chapter);
        sut.SyncCaretToPreview(5);

        // Act
        sut.HandlePreviewMessage("signet-block:100");
        sut.HandlePreviewMessage("signet-loc:110");
        sut.SyncCaretToPreview(110);
        sut.HandlePreviewMessage("signet-block:100");

        // Assert
        sut.CanGoBack.Should().BeTrue();
        sut.CanGoForward.Should().BeFalse();

        // Act
        sut.HandlePreviewMessage("signet-block:300");
        sut.GoBackCommand.Execute(null);

        // Assert
        activated.Should().Equal(110);
        sut.CanGoBack.Should().BeTrue("the first block was a place of its own, after the start");
        sut.CanGoForward.Should().BeTrue();

        // Act
        sut.GoBackCommand.Execute(null);

        // Assert
        activated.Should().Equal(110, 5);
        sut.CanGoBack.Should().BeFalse();
    }

    [Theory]
    [InlineData("signet-block:")]
    [InlineData("signet-block:abc")]
    [InlineData("signet-block:-1")]
    public void A_block_click_without_a_usable_offset_is_ignored(string message)
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        using PreviewViewModel sut = New();
        sut.SetBook(book);
        sut.ShowResource(chapter);

        // Act
        sut.HandlePreviewMessage(message);

        // Assert
        sut.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void The_click_bridge_reports_the_blocks_of_text_and_not_the_containers()
    {
        // Arrange
        string script = PreviewClickBridge.Script;

        // Act & Assert
        foreach (string tag in Blocks)
        {
            PreviewClickBridge.BlockTags.Should().Contain(tag);
        }

        foreach (string tag in NotBlocks)
        {
            PreviewClickBridge.BlockTags.Should().NotContain(tag);
        }

        script.Should().Contain("'p':1").And.Contain("footnote|endnote|rearnote").And.Contain("signet-block:");
        script.IndexOf("signet-block:", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("'signet-loc:'", StringComparison.Ordinal), "the block is reported before the caret moves");
    }
}
