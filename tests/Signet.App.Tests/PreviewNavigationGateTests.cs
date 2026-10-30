using System;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Views;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// When the preview may load a page (<see cref="PreviewNavigationGate"/>): only in the visual tree with its engine
/// initialized; a page asked for while the Preview panel is closed is loaded when it is opened again.
/// </summary>
public sealed class PreviewNavigationGateTests
{
    private static readonly Fixture Any = new();

    private static Uri Page() => new("file:///book/" + Any.Create<string>() + ".xhtml");

    [Fact]
    public void A_page_asked_for_while_the_panel_is_closed_is_loaded_when_it_is_opened_again()
    {
        // Arrange
        PreviewNavigationGate sut = new();
        Uri first = Page();
        Uri second = Page();
        sut.Attached();
        sut.Initialized();

        // Act & Assert
        sut.Request(first).Should().Be(first, "a shown preview loads at once");

        // Act: the panel is closed, then the active tab changes twice
        sut.Detached();

        // Assert
        sut.Request(Page()).Should().BeNull("a closed panel loads nothing");
        sut.Request(second).Should().BeNull();

        // Act & Assert: opened again, it loads the page of the tab active now
        sut.Attached().Should().Be(second);
        sut.Pending.Should().BeNull();
        sut.Attached().Should().BeNull("nothing waits any more");
    }

    [Fact]
    public void A_page_asked_for_before_the_engine_is_ready_is_loaded_once_it_is()
    {
        // Arrange
        PreviewNavigationGate sut = new();
        Uri page = Page();

        // Act
        sut.Attached().Should().BeNull();
        sut.Request(page).Should().BeNull();

        // Act & Assert
        sut.Initialized().Should().Be(page);
    }

    [Fact]
    public void A_forgotten_page_is_not_loaded()
    {
        // Arrange
        PreviewNavigationGate sut = new();
        sut.Request(Page());

        // Act
        sut.Forget();

        // Assert
        sut.Attached().Should().BeNull();
        sut.Initialized().Should().BeNull();
    }
}
