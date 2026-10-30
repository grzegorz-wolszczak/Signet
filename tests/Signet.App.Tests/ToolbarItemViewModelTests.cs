using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for toolbar buttons with a drop-down menu (headings / change case): choosing an item
/// from the menu makes it the button's default action.
/// </summary>
public sealed class ToolbarItemViewModelTests
{
    private static ToolbarItemViewModel HeadingDropDown(TestHost host)
    {
        ToolbarViewModel heading = new(ToolbarId.Heading, host.Toolbars, host.Registry);
        return heading.Items.Single();
    }

    [Fact]
    public void Heading_toolbar_holds_a_drop_down_with_the_seven_heading_actions()
    {
        // Arrange
        using TestHost host = new();

        // Act
        ToolbarItemViewModel sut = HeadingDropDown(host);

        // Assert
        sut.IsDropDown.Should().BeTrue();
        sut.MenuActions.Select(a => a.Id).Should().Equal(
            AppActionIds.Heading1, AppActionIds.Heading2, AppActionIds.Heading3, AppActionIds.Heading4,
            AppActionIds.Heading5, AppActionIds.Heading6, AppActionIds.HeadingNormal);
        sut.IconKey.Should().Be("heading-all");
    }

    [Fact]
    public void Choosing_an_action_makes_it_the_default_and_takes_over_its_icon()
    {
        // Arrange
        using TestHost host = new();
        ToolbarItemViewModel sut = HeadingDropDown(host);
        AppAction heading2 = sut.MenuActions[1];

        // Act
        sut.ChooseCommand.Execute(heading2);

        // Assert
        sut.DefaultAction.Should().BeSameAs(heading2);
        sut.IconKey.Should().Be(heading2.IconKey);
    }

    [Fact]
    public void Main_part_without_a_default_action_asks_for_the_menu()
    {
        // Arrange
        using TestHost host = new();
        ToolbarItemViewModel sut = HeadingDropDown(host);
        bool requested = false;
        sut.MenuRequested += (_, _) => requested = true;

        // Act
        sut.Command!.Execute(null);

        // Assert
        requested.Should().BeTrue();
    }

    [Theory]
    [InlineData("Mend Code in Current File", "Mend Code in\nCurrent File")]
    [InlineData("Generate Table Of Contents", "Generate Table\nOf Contents")]
    [InlineData("Bookmark Location", "Bookmark\nLocation")]
    [InlineData("Metadata Editor", "Metadata Editor")]
    [InlineData("Spellcheck", "Spellcheck")]
    [InlineData("Averyveryverylongword", "Averyveryverylongword")]
    public void A_long_label_is_shown_in_two_even_lines(string label, string expected)
    {
        // Act
        string shown = ToolbarItemViewModel.WrapLabel(label);

        // Assert
        shown.Should().Be(expected);
    }
}
