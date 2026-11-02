using System.Linq;
using AutoFixture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>The title of a context menu (<see cref="MenuTitle"/>).</summary>
public sealed class MenuTitleTests
{
    [Theory]
    [InlineData("ch_01.xhtml", "ch__01.xhtml")]
    [InlineData("plain.css", "plain.css")]
    public void A_title_shows_underscores_literally(string text, string header)
    {
        // Act & Assert
        MenuTitle.Escape(text).Should().Be(header);
    }

    [Theory]
    [InlineData("", "Book Browser")]
    [InlineData("ch_01.xhtml", "ch_01.xhtml")]
    public void A_title_on_a_selection_names_the_one_element_or_the_place(string names, string expected)
    {
        // Act & Assert
        MenuTitle.ForSelection(names.Split(',', System.StringSplitOptions.RemoveEmptyEntries), "Book Browser").Should().Be(expected);
    }

    [Fact]
    public void A_title_on_several_selected_elements_counts_them()
    {
        // Act & Assert
        MenuTitle.ForSelection("a,b,c".Split(','), "Book Browser").Should().Be(Strings.Format("MenuTitle_Selected", 3));
    }

    [AvaloniaFact]
    public void The_title_goes_first_with_a_separator_after_it_and_is_read_again_when_the_menu_opens()
    {
        // Arrange
        Window window = new();
        ContextMenu menu = new();
        menu.Items.Add(new MenuItem { Header = "Open" });
        window.ContextMenu = menu;
        window.Show();
        string text = "first";

        // Act
        MenuItem title = MenuTitle.AddTo(menu, () => text);

        // Assert
        menu.Items.Cast<object>().Should().HaveCount(3);
        menu.Items[0].Should().BeSameAs(title);
        menu.Items[1].Should().BeOfType<Separator>();
        title.Classes.Should().Contain(MenuTitle.TitleClass);
        title.IsHitTestVisible.Should().BeFalse();
        title.Focusable.Should().BeFalse();
        title.Header.Should().Be("first");

        // Act
        text = "second_one";
        menu.Open(window);

        // Assert
        title.Header.Should().Be("second__one");
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void An_open_context_menu_shows_its_title_as_a_title_bar_across_the_top_edge()
    {
        // Arrange
        Window window = new();
        ContextMenu menu = new();
        menu.Items.Add(new MenuItem { Header = new Fixture().Create<string>(), Icon = new Border { Width = 16, Height = 16 } });
        window.ContextMenu = menu;
        window.Show();
        MenuItem title = MenuTitle.AddTo(menu, () => new Fixture().Create<string>());

        // Act
        menu.Open(window);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Point topLeft = title.TranslatePoint(default, menu)!.Value;
        topLeft.Y.Should().BeLessThanOrEqualTo(menu.BorderThickness.Top, "the title bar starts at the menu's top edge");
        title.Bounds.Width.Should().BeGreaterThanOrEqualTo(menu.Bounds.Width - menu.BorderThickness.Left - menu.BorderThickness.Right - 1);
        title.CornerRadius.TopLeft.Should().BeGreaterThan(0, "it has the rounded top corners of the menu");
        Separator separator = menu.Items[1].Should().BeOfType<Separator>().Subject;
        separator.Classes.Should().Contain(MenuTitle.SeparatorClass);
        separator.IsVisible.Should().BeFalse("the title bar sets the title apart");
        ContentPresenter header = title.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_HeaderPresenter");
        header.HorizontalContentAlignment.Should().Be(HorizontalAlignment.Center);
        Grid.GetColumnSpan(header).Should().Be(5, "the text is centered over the whole width, not after the icon column");
        menu.Close();
        window.Close();
    }
}
