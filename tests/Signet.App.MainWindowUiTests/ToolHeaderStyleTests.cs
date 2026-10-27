using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.Core.Misc;

namespace Signet.App.MainWindowUiTests;

/// <summary>The font of a tool panel's title bar (Book Browser, Preview, …), from Preferences › Appearance › General › Fonts.</summary>
public sealed class ToolHeaderStyleTests
{
    private sealed record Host(Window Window, MainDockFactory Factory, IRootDock Root);

    private static Host Show()
    {
        MainDockFactory factory = new();
        IRootDock root = factory.CreateLayout();
        factory.InitLayout(root);
        Window window = new()
        {
            Width = 1000,
            Height = 700,
            Content = new DockControl { Layout = root, Factory = factory, InitializeLayout = false },
        };
        window.Show();
        Settle(window);
        return new Host(window, factory, root);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Tool_panel_headers_take_the_font_of_the_setting_and_return_to_the_default_font()
    {
        // Arrange
        Host host = Show();
        ToolChromeControl[] headers = host.Window.GetVisualDescendants().OfType<ToolChromeControl>().ToArray();
        headers.Should().NotBeEmpty("the default layout docks tool panels such as the Book Browser");
        ToolChromeControl header = headers[0];
        // The rendered title TextBlock itself, not only ToolChromeControl's own (inherited) property: Dock.Avalonia's
        // template sets FontSize directly on "PART_Title", which otherwise wins over it.
        TextBlock title = header.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
        double normal = title.FontSize;
        try
        {
            // Act
            UiDensityManager.ApplyToolHeader(Application.Current!, new ToolHeaderFontAppearance("Courier New", 23));
            Settle(host.Window);

            // Assert
            header.FontSize.Should().Be(23);
            header.FontFamily.Name.Should().Be("Courier New");
            title.FontSize.Should().Be(23);
            title.FontFamily.Name.Should().Be("Courier New");
        }
        finally
        {
            // Act
            UiDensityManager.ApplyToolHeader(Application.Current!, ToolHeaderFontAppearance.Default);
        }

        Settle(host.Window);

        // Assert
        header.FontSize.Should().Be(normal);
        title.FontSize.Should().Be(normal);
    }
}
