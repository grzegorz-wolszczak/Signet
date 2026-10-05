using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;

namespace Signet.App.UiTests;

/// <summary>
/// Menu popups must not clip long labels — Fluent caps flyout/menu popups at
/// <c>FlyoutThemeMaxWidth</c> (456), which <c>App.axaml</c> lifts.
/// </summary>
public sealed class MenuWidthTests
{
    /// <summary>The width cap of the stock Fluent theme.</summary>
    private const double FluentFlyoutMaxWidth = 456;

    [AvaloniaFact]
    public void Context_menu_grows_to_fit_a_label_wider_than_the_fluent_flyout_cap()
    {
        const string label = "Rebase OPF Manifest IDs on Current Filenames, then update every reference in the book";
        TextBlock probe = new() { Text = label };
        probe.Measure(Size.Infinity);
        double labelWidth = probe.DesiredSize.Width;
        labelWidth.Should().BeGreaterThan(FluentFlyoutMaxWidth, "the label must be long enough to hit the Fluent cap");

        MenuItem item = new() { Header = label };
        ContextMenu menu = new() { ItemsSource = new[] { item } };
        Border target = new() { Width = 100, Height = 100, ContextMenu = menu };
        var window = new Window { Width = 1600, Height = 400, Content = target };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        menu.Open(target);
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        try
        {
            menu.Bounds.Width.Should().BeGreaterThan(labelWidth);
        }
        finally
        {
            menu.Close();
            window.Close();
        }
    }
}
