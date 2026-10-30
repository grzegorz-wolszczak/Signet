using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;

namespace Signet.App.UiTests;

/// <summary>The fonts of the context menus and the background of their title (<see cref="ContextMenuLook"/>, App.axaml).</summary>
public sealed class ContextMenuLookTests
{
    private static (Window Window, ContextMenu Menu, MenuItem Title, MenuItem Item) Open()
    {
        ContextMenu menu = new();
        MenuItem item = new() { Header = "Open" };
        menu.Items.Add(item);
        MenuItem title = MenuTitle.AddTo(menu, () => "chapter_01.xhtml");
        Window window = new() { ContextMenu = menu };
        window.Show();
        menu.Open(window);
        Dispatcher.UIThread.RunJobs();
        return (window, menu, title, item);
    }

    [AvaloniaFact]
    public void Without_settings_the_items_have_the_interface_font_and_the_title_is_bold()
    {
        // Arrange
        Application app = Application.Current!;
        app.Resources["ControlContentThemeFontSize"] = 17.0;

        // Act
        ContextMenuLook.Apply(app, ContextMenuAppearance.Default);
        (Window window, ContextMenu menu, MenuItem title, MenuItem item) = Open();

        // Assert
        item.FontSize.Should().Be(17, "the items keep the interface size");
        item.FontWeight.Should().Be(FontWeight.Normal);
        title.FontSize.Should().Be(17, "the title takes the size of the items");
        title.FontWeight.Should().Be(FontWeight.Bold);
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void The_fonts_set_for_the_title_and_the_items_reach_the_menu()
    {
        // Arrange
        Application app = Application.Current!;

        // Act
        ContextMenuLook.Apply(app, new ContextMenuAppearance(string.Empty, 19, 300, string.Empty, 13, 600));
        (Window window, ContextMenu menu, MenuItem title, MenuItem item) = Open();

        // Assert
        item.FontSize.Should().Be(13);
        item.FontWeight.Should().Be(FontWeight.SemiBold);
        title.FontSize.Should().Be(19);
        title.FontWeight.Should().Be(FontWeight.Light);
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Without_a_theme_color_the_title_has_a_shade_of_the_accent_color_as_background()
    {
        // Arrange
        Application app = Application.Current!;

        // Act
        AppTheme.Apply(app, AppTheme.Current);
        (Window window, ContextMenu menu, MenuItem title, _) = Open();

        // Assert
        title.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be((Color)app.FindResource("SystemAccentColorLight2")!);
        menu.Close();
        window.Close();
    }
}
