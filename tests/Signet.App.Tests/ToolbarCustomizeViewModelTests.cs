using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The "Customize Toolbars" window with several items selected at once (Ctrl / Shift in its lists): Add, Remove, Up and
/// Down act on all of them.
/// </summary>
public sealed class ToolbarCustomizeViewModelTests
{
    private static ToolbarCustomizeViewModel NewOn(TestHost host, ToolbarId toolbar)
    {
        ToolbarCustomizeViewModel sut = new(host.Toolbars, host.Registry) { SelectedToolbar = toolbar };
        return sut;
    }

    private static ToolbarEntry Available(ToolbarCustomizeViewModel sut, string id) =>
        sut.AvailableActions.Single(e => e.Id == id);

    [Fact]
    public void Add_adds_every_selected_action_in_the_order_of_the_list_and_selects_them()
    {
        // Arrange
        using TestHost host = new();
        ToolbarCustomizeViewModel sut = NewOn(host, ToolbarId.List);
        ToolbarEntry cut = Available(sut, AppActionIds.Cut);
        ToolbarEntry bold = Available(sut, AppActionIds.Bold);
        sut.SelectedAvailableItems.Add(bold);
        sut.SelectedAvailableItems.Add(cut);
        int before = sut.CurrentItems.Count;
        bool cutFirst = sut.AvailableActions.IndexOf(cut) < sut.AvailableActions.IndexOf(bold);

        // Act
        sut.AddCommand.Execute(null);

        // Assert
        string[] expected = cutFirst ? new[] { AppActionIds.Cut, AppActionIds.Bold } : new[] { AppActionIds.Bold, AppActionIds.Cut };
        sut.CurrentItems.Skip(before).Select(e => e.Id).Should().Equal(expected);
        host.Toolbars.GetItems(ToolbarId.List).TakeLast(2).Should().Equal(expected);
        sut.SelectedCurrentItems.Should().Equal(sut.CurrentItems.Skip(before), "the added items are selected in the layout");
        sut.CurrentItems.Skip(before).Should().NotContain(new[] { cut, bold }, "each added item is an item of its own");
    }

    [Fact]
    public void Remove_removes_every_selected_item()
    {
        // Arrange
        using TestHost host = new();
        ToolbarCustomizeViewModel sut = NewOn(host, ToolbarId.Format);
        ToolbarEntry[] items = sut.CurrentItems.ToArray();
        sut.SelectedCurrentItems.Add(items[0]);
        sut.SelectedCurrentItems.Add(items[2]);

        // Act
        sut.RemoveCommand.Execute(null);

        // Assert
        sut.CurrentItems.Should().Equal(items.Where((_, i) => i is not 0 and not 2));
        host.Toolbars.GetItems(ToolbarId.Format).Should().Equal(items.Where((_, i) => i is not 0 and not 2).Select(e => e.Id));
        sut.SelectedCurrentItems.Should().BeEmpty();
    }

    [Fact]
    public void Up_and_down_move_the_selected_items_as_a_block_and_keep_them_selected()
    {
        // Arrange
        using TestHost host = new();
        ToolbarCustomizeViewModel sut = NewOn(host, ToolbarId.Format);
        ToolbarEntry[] items = sut.CurrentItems.ToArray(); // B, I, U, S, sub, sup
        sut.SelectedCurrentItems.Add(items[2]);
        sut.SelectedCurrentItems.Add(items[3]);

        // Act
        sut.MoveUpCommand.Execute(null);

        // Assert
        sut.CurrentItems.Should().Equal(items[0], items[2], items[3], items[1], items[4], items[5]);
        sut.SelectedCurrentItems.Should().BeEquivalentTo(new[] { items[2], items[3] });

        // Act
        sut.MoveUpCommand.Execute(null);
        sut.MoveUpCommand.Execute(null);

        // Assert
        sut.CurrentItems.Should().Equal(items[2], items[3], items[0], items[1], items[4], items[5]);

        // Act
        sut.MoveDownCommand.Execute(null);

        // Assert
        sut.CurrentItems.Should().Equal(items[0], items[2], items[3], items[1], items[4], items[5]);
        host.Toolbars.GetItems(ToolbarId.Format).Should().Equal(sut.CurrentItems.Select(e => e.Id));
    }
}
