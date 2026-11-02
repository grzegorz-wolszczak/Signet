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

    // A shown preview whose engine is ready (the panel open).
    private static PreviewNavigationGate Ready()
    {
        PreviewNavigationGate gate = new();
        gate.Attached();
        gate.Initialized();
        return gate;
    }

    [Fact]
    public void A_reload_while_a_page_is_loading_loads_that_page_again()
    {
        // Arrange: the shown file was merged into another (found in November 2026) - the tab of the target becomes active
        // and its page is asked for; the reload after the merge comes before the engine has loaded it
        PreviewNavigationGate sut = Ready();
        Uri merged = Page();
        Uri target = Page();
        sut.Request(merged);
        sut.Completed(merged);

        // Act
        sut.Request(target);

        // Assert
        sut.ForReload().Should().Be(target, "the engine would reload the merged file, gone from the mirror");

        // Act
        sut.Completed(target);

        // Assert
        sut.ForReload().Should().BeNull("the page is shown: the engine's own reload is right");
    }

    [Fact]
    public void The_completion_of_an_older_load_leaves_the_page_still_loading()
    {
        // Arrange
        PreviewNavigationGate sut = Ready();
        Uri older = Page();
        Uri newer = Page();
        sut.Request(older);

        // Act
        sut.Request(newer);
        sut.Completed(older);

        // Assert
        sut.ForReload().Should().Be(newer);
    }

    [Fact]
    public void Another_place_in_the_page_shown_is_not_a_load()
    {
        // Arrange: the engine scrolls to a fragment of the page it shows without reporting a completion
        PreviewNavigationGate sut = Ready();
        Uri page = Page();
        sut.Request(page);
        sut.Completed(page);

        // Act
        sut.Request(new UriBuilder(page) { Fragment = Any.Create<string>() }.Uri);

        // Assert
        sut.ForReload().Should().BeNull("a reload must still refresh the page");
    }
}
