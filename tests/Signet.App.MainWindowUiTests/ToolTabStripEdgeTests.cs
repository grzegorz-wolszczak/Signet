using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Signet.App.Docking;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// The bottom edge of a group of docked tool panels, which the row of tabs draws. Found in November 2026: with
/// Book Browser and Clips in one group, closing Clips left the bottom line of Book Browser with a gap where the tab of
/// Clips had been (and bits of the line after a resize) — Dock kept sizing the borders of the line by the hidden tab.
/// </summary>
public sealed class ToolTabStripEdgeTests
{
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Part(ToolTabStrip strip, string name) =>
        strip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);

    [AvaloniaFact]
    public void A_group_left_with_one_panel_has_its_bottom_line_whole()
    {
        // Arrange
        using MainWindowViewModel vm = new();
        MainWindow window = new() { DataContext = vm, Width = 1200, Height = 800 };
        window.Show();
        Settle(window);
        MainDockFactory factory = (MainDockFactory)vm.DockFactory;
        if (!factory.IsToolVisible(DockableIds.BookBrowser))
        {
            factory.ToggleTool(DockableIds.BookBrowser);
        }

        factory.ToggleTool(DockableIds.Clips);
        Settle(window);
        ToolTabStrip strip = window.GetVisualDescendants().OfType<ToolTabStrip>()
            .Single(s => s.Items.OfType<Dock.Model.Core.IDockable>().Any(d => d.Id == DockableIds.BookBrowser));
        strip.ItemCount.Should().Be(2, "Book Browser and Clips share the group");
        IBrush? line = Part(strip, "PART_BorderLeftFill").BorderBrush;

        // Act
        factory.ToggleTool(DockableIds.Clips);
        Settle(window);

        // Assert
        strip.Classes.Should().Contain(":singleitem");
        Part(strip, "PART_BorderLeftFill").IsVisible.Should().BeFalse("its size is that of the hidden tab");
        Part(strip, "PART_BorderRightFill").IsVisible.Should().BeFalse();
        Border whole = Part(strip, "PART_BorderFill");
        whole.BorderThickness.Top.Should().Be(1);
        whole.Bounds.Width.Should().Be(strip.Bounds.Width, "the line runs the whole width of the panel");
        ((ISolidColorBrush)whole.BorderBrush!).Color.Should().Be(((ISolidColorBrush)line!).Color, "the same line as with several tabs");

        // Act
        factory.ToggleTool(DockableIds.Clips);
        Settle(window);

        // Assert
        Part(strip, "PART_BorderLeftFill").IsVisible.Should().BeTrue("with two tabs the line opens under the selected one again");
        Part(strip, "PART_BorderFill").BorderThickness.Top.Should().Be(0);
        window.Close();
    }
}
