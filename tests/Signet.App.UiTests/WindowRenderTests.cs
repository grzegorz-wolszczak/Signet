using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;

namespace Signet.App.UiTests;

/// <summary>
/// Headless render tests of the main window. They check what the model tests cannot catch:
/// that the window frame actually renders and that the tab area (<c>DocumentDock</c>)
/// gets most of the center column height instead of collapsing to zero in favour of the
/// Validation Results panel.
/// </summary>
/// <remarks>
/// Rendering a document tab itself (adding a <c>Document</c> to <c>DocumentDock</c>) under
/// Avalonia.Headless hangs in this environment (Dock.Avalonia never finishes an internal
/// operation during <c>Dispatcher.RunJobs</c>). The tab open/activate/close logic is covered
/// without UI in <c>Signet.App.Tests</c> (<c>TabManagerTests</c>) and
/// <c>Signet.Core.Tests</c> (<c>TabManagerModelTests</c>).
/// </remarks>
public sealed class WindowRenderTests
{
    private static MainWindow ShowMainWindow()
    {
        MainWindow window = new() { DataContext = new MainWindowViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Main_window_renders_menu_toolbars_and_dock()
    {
        MainWindow window = ShowMainWindow();

        window.GetVisualDescendants().OfType<Avalonia.Controls.Menu>().Should().NotBeEmpty();
        window.GetVisualDescendants().Any(v => v.GetType().Name.Contains("DockControl"))
            .Should().BeTrue();
    }

    [AvaloniaFact]
    public void Toolbar_buttons_render_colour_icons_from_the_default_icon_theme()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), "Signet.Tests", $"uitests-{Guid.NewGuid():N}.json");
        new IconThemeManager(new SettingsStore(settingsPath), NullLogger<IconThemeManager>.Instance).ApplySaved();

        MainWindow window = ShowMainWindow();

        // Toolbar button icons (not the TreeView arrow PathIcons in the Book Browser).
        List<Image> icons = window.GetVisualDescendants().OfType<Image>()
            .Where(i => i.DataContext is ToolbarItemViewModel { IconKey: not null })
            .ToList();
        icons.Should().NotBeEmpty("the New/Open/Save toolbar should show icons");
        static bool IsColourPng(Image i) =>
            IconThemeManager.ColoredIconKeys.Contains(((ToolbarItemViewModel)i.DataContext!).IconKey!);
        icons.Where(i => !IsColourPng(i)).Should().OnlyContain(i => i.Source is DrawingImage,
            "the default \"main\" set consists of colour DrawingImages resolved by IconKeyToImageConverter");

        // Beautify / Fix code on the Tools toolbar use colour PNGs (beautify / html-fix).
        icons.Where(IsColourPng).Select(i => ((ToolbarItemViewModel)i.DataContext!).IconKey)
            .Should().BeEquivalentTo(IconThemeManager.ColoredIconKeys);
        icons.Where(IsColourPng).Should().OnlyContain(i => i.Source is Bitmap);
    }

    [AvaloniaFact]
    public void Documents_area_gets_the_bulk_of_the_center_column_height()
    {
        MainWindow window = ShowMainWindow();

        Control? documentControl = window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(v => v.GetType().Name is "DocumentControl" or "DocumentDockControl");

        documentControl.Should().NotBeNull(
            "the tab area must render even with no open documents");

        double windowHeight = window.Bounds.Height;
        documentControl!.Bounds.Height.Should().BeGreaterThan(windowHeight * 0.4,
            "an empty DocumentDock must not collapse to zero in favour of the Validation Results panel");
    }
}
