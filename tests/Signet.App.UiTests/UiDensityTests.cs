using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;

namespace Signet.App.UiTests;

/// <summary>
/// Compact ("Windows-like") UI look — the Fluent resource overrides must actually reach the
/// controls. Resources are attached locally to the window (not globally to the application) so
/// they do not affect other tests sharing the headless application instance.
/// </summary>
public sealed class UiDensityTests
{
    [AvaloniaFact]
    public void Compact_resources_shrink_the_ui_font_and_menu_item_padding()
    {
        TextBlock text = new() { Text = "Wycinki" };
        MenuItem item = new() { Header = "Wycinki" };
        Avalonia.Controls.Menu menu = new() { ItemsSource = new[] { item } };
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { menu, text } } };
        window.Resources.MergedDictionaries.Add(UiDensityManager.CreateCompactResources());

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        window.FontSize.Should().Be(UiDensityManager.CompactFontSize);
        text.FontSize.Should().Be(UiDensityManager.CompactFontSize);
        text.FontFamily.Should().Be(FontFamily.Default, "the system font is used instead of Inter");
        menu.Bounds.Height.Should().BeLessThanOrEqualTo(24);
    }

    [AvaloniaFact]
    public void Compact_context_menu_items_are_shorter_than_the_default_fluent_ones()
    {
        static double Height(bool compact)
        {
            MenuItem item = new() { Header = "Przełącz tryb zawijania linii" };
            ContextMenu menu = new() { ItemsSource = new[] { item } };
            Border target = new() { Width = 100, Height = 100, ContextMenu = menu };
            var window = new Window { Width = 400, Height = 400, Content = target };
            if (compact)
            {
                window.Resources.MergedDictionaries.Add(UiDensityManager.CreateCompactResources());
            }

            window.Show();
            Dispatcher.UIThread.RunJobs();
            menu.Open(target);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
            double height = item.Bounds.Height;
            menu.Close();
            window.Close();
            return height;
        }

        double normal = Height(compact: false);
        double compact = Height(compact: true);

        compact.Should().BeGreaterThan(0);
        compact.Should().BeLessThan(normal);
    }

    [AvaloniaFact]
    public void Chosen_ui_font_overrides_the_compact_default()
    {
        TextBlock text = new() { Text = "Wycinki" };
        var window = new Window { Width = 300, Height = 200, Content = text };
        window.Resources.MergedDictionaries.Add(UiDensityManager.CreateCompactResources());
        window.Resources.MergedDictionaries.Add(UiDensityManager.CreateFontResources("Tahoma", 13)!);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        text.FontSize.Should().Be(13);
        text.FontFamily.Name.Should().Be("Tahoma");
        UiDensityManager.CreateFontResources(string.Empty, 0).Should().BeNull();
    }
}
