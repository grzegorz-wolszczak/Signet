using System.Collections.Generic;
using AwesomeAssertions;
using Signet.App.Actions;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The "needs a book" block of the actions (<see cref="AppAction.IsBlocked"/>, <see cref="AppActionRegistry.SetBookOpen"/>).</summary>
public sealed class AppActionBlockTests
{
    [Fact]
    public void A_blocked_action_is_disabled_and_gets_its_own_state_back_when_unblocked()
    {
        // Arrange
        using TestHost host = new();
        AppActionRegistry registry = host.Registry;
        AppAction save = registry.Require(AppActionIds.Save);
        save.IsEnabled = true;
        List<string?> changed = new();
        int canExecuteChanged = 0;
        save.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        save.CanExecuteChanged += (_, _) => canExecuteChanged++;

        // Act
        registry.SetBookOpen(false);

        // Assert
        save.IsBlocked.Should().BeTrue();
        save.IsEnabled.Should().BeFalse();
        save.CanExecute(null).Should().BeFalse();
        changed.Should().Contain(nameof(AppAction.IsEnabled));
        canExecuteChanged.Should().Be(1);

        // Act
        save.IsEnabled = false;
        save.IsEnabled = true;

        // Assert
        canExecuteChanged.Should().Be(1, "the effective state did not change while blocked");

        // Act
        registry.SetBookOpen(true);

        // Assert
        save.IsEnabled.Should().BeTrue();
        canExecuteChanged.Should().Be(2);
    }

    [Fact]
    public void An_action_disabled_by_its_owner_stays_disabled_when_a_book_opens()
    {
        // Arrange
        using TestHost host = new();
        AppActionRegistry registry = host.Registry;
        AppAction revert = registry.Require(AppActionIds.RevertToBefore);
        revert.IsEnabled = false;

        // Act
        registry.SetBookOpen(false);

        registry.SetBookOpen(true);

        // Assert
        revert.IsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(AppActionIds.Open, true)]
    [InlineData(AppActionIds.Preferences, true)]
    [InlineData(AppActionIds.FocusPreview, true)]
    [InlineData(AppActionIds.ClipEditorRename, true)]
    [InlineData(AppActionIds.Save, false)]
    [InlineData(AppActionIds.BookBrowserRename, false)]
    [InlineData(AppActionIds.LiveCssPanel, false)]
    public void Only_some_actions_work_without_a_book(string id, bool expected)
    {
        // Arrange
        using TestHost host = new();
        AppActionRegistry registry = host.Registry;
        AppAction action = registry.Require(id);

        // Act & Assert
        AppActionIds.WorksWithoutBook(action.Id, action.Category).Should().Be(expected);
    }
}
