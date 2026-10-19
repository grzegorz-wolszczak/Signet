using AwesomeAssertions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
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
    public void Edit_raises_EditRequested()
    {
        // Arrange
        using TestHost host = new();
        ToolbarViewModel sut = new(ToolbarId.Align, host.Toolbars, host.Registry);
        object? raisedBy = null;
        sut.EditRequested += (s, _) => raisedBy = s;

        // Act
        sut.EditCommand.Execute(null);

        // Assert
        raisedBy.Should().BeSameAs(sut);
    }
}
