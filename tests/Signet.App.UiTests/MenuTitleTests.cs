using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    public void The_title_goes_first_with_a_line_under_it_and_is_read_again_when_the_menu_opens()
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
}
