using System.Linq;
using AwesomeAssertions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for the context menu commands of <see cref="ToolbarViewModel"/>.</summary>
public sealed class ToolbarViewModelTests
{
    [Fact]
    public void Hide_hides_the_toolbar_like_the_View_menu_does()
    {
        // Arrange
        using TestHost host = new();
        ToolbarViewModel sut = new(ToolbarId.Format, host.Toolbars, host.Registry);

        // Act
        sut.HideCommand.Execute(null);

        // Assert
        sut.IsVisible.Should().BeFalse();
        host.Toolbars.IsVisible(ToolbarId.Format).Should().BeFalse();
        host.ReopenSettings().GetStringMap(ToolbarManager.VisibilityGroup)
            .Should().ContainKey(nameof(ToolbarId.Format)).WhoseValue.Should().Be("0");
    }

    [Fact]
    public void Every_shown_toolbar_of_a_row_but_the_first_starts_with_a_line()
    {
        // Arrange
        using TestHost host = new();
        ToolbarViewModel first = new(ToolbarId.New, host.Toolbars, host.Registry);
        ToolbarViewModel second = new(ToolbarId.File, host.Toolbars, host.Registry);
        ToolbarViewModel third = new(ToolbarId.Edit, host.Toolbars, host.Registry);

        // Act
        using ToolbarRowViewModel row = new(new[] { first, second, third });

        // Assert
        new[] { first, second, third }.Select(t => t.FollowsAnotherToolbar).Should().Equal(false, true, true);

        // Act
        first.IsVisible = false;

        // Assert
        new[] { second, third }.Select(t => t.FollowsAnotherToolbar).Should().Equal(false, true);
    }

    [Fact]
    public void Configure_raises_ConfigureRequested()
    {
        // Arrange
        using TestHost host = new();
        ToolbarViewModel sut = new(ToolbarId.Align, host.Toolbars, host.Registry);
        object? raisedBy = null;
        sut.ConfigureRequested += (s, _) => raisedBy = s;

        // Act
        sut.ConfigureCommand.Execute(null);

        // Assert
        raisedBy.Should().BeSameAs(sut);
    }

    [Fact]
    public void The_settings_of_the_menu_are_shared_by_every_toolbar_and_saved()
    {
        // Arrange
        using TestHost host = new();
        ToolbarViewModel sut = new(ToolbarId.Align, host.Toolbars, host.Registry);
        ToolbarViewModel other = new(ToolbarId.Format, host.Toolbars, host.Registry);
        host.Toolbars.AppearanceChanged += (_, _) => other.RefreshAppearance();

        // Assert
        other.IsIconsOnly.Should().BeTrue();
        other.ShowsText.Should().BeFalse();
        other.IsMediumIcons.Should().BeTrue();
        other.ArePositionsLocked.Should().BeFalse();

        // Act
        sut.SetTextPositionCommand.Execute(ToolbarTextPosition.TextUnderIcons);
        sut.SetIconSizeCommand.Execute(ToolbarIconSize.Small);
        sut.TogglePositionsLockedCommand.Execute(null);

        // Assert
        other.IsTextUnderIcons.Should().BeTrue();
        other.IsIconsOnly.Should().BeFalse();
        other.ShowsText.Should().BeTrue();
        other.IsSmallIcons.Should().BeTrue();
        other.IsMediumIcons.Should().BeFalse();
        other.IsBigIcons.Should().BeFalse();
        other.ArePositionsLocked.Should().BeTrue();
        host.ReopenSettings().ToolbarAppearance.Should().Be(
            ToolbarAppearance.Default with
            {
                TextPosition = ToolbarTextPosition.TextUnderIcons,
                IconSize = ToolbarIconSize.Small,
                PositionsLocked = true,
            });

        // Act
        sut.TogglePositionsLockedCommand.Execute(null);

        // Assert
        other.ArePositionsLocked.Should().BeFalse();
    }
}
