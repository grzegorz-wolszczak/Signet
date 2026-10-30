using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using Signet.App.Toolbars;

namespace Signet.App.UiTests;

/// <summary>
/// Toolbar layout tests: overflow into the "»" menu and splitting the row width.
/// </summary>
public sealed class ToolbarLayoutTests
{
    private static ToolbarOverflowPanel Strip(int buttons, double buttonWidth)
    {
        ToolbarOverflowPanel panel = new() { ChevronWidth = 16 };
        for (int i = 0; i < buttons; i++)
        {
            panel.Children.Add(new Border { Width = buttonWidth, Height = 20, DataContext = i });
        }

        return panel;
    }

    [AvaloniaFact]
    public void All_buttons_fit_without_overflow()
    {
        // Arrange
        ToolbarOverflowPanel sut = Strip(4, 20);

        // Act
        sut.Measure(new Size(200, 30));

        // Assert
        sut.HasOverflow.Should().BeFalse("full={0}", sut.FullContentWidth);
        sut.DesiredSize.Width.Should().Be(80);
        sut.OverflowedItems.Should().BeEmpty();
    }

    // Found in October 2026: with the display scaled (125 %, 150 %) the sizes are rounded to device pixels, and the
    // width a toolbar gets can come out a fraction of a pixel short of the sum of its buttons — the last button went
    // to the "»" menu although it fitted.
    [AvaloniaFact]
    public void A_shortfall_of_a_fraction_of_a_pixel_from_rounding_is_no_overflow()
    {
        // Arrange
        ToolbarOverflowPanel sut = Strip(8, 30);

        // Act
        sut.Measure(new Size((8 * 30) - 0.5, 30));

        // Assert
        sut.HasOverflow.Should().BeFalse();
        sut.OverflowedItems.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Buttons_that_do_not_fit_go_to_the_chevron_menu()
    {
        // Arrange
        ToolbarOverflowPanel sut = Strip(4, 20);

        // Act
        sut.Measure(new Size(60, 30));

        // Assert
        sut.HasOverflow.Should().BeTrue();
        sut.OverflowedItems.Should().Equal(2, 3); // 60 − 16 (») = 44 → 2 buttons fit
    }

    [AvaloniaFact]
    public void First_button_is_always_shown()
    {
        // Arrange
        ToolbarOverflowPanel sut = Strip(3, 20);

        // Act
        sut.Measure(new Size(5, 30));

        // Assert
        sut.OverflowedItems.Should().Equal(1, 2);
        sut.MinContentWidth.Should().Be(36);
    }

    [AvaloniaFact]
    public void Row_gives_each_toolbar_its_minimum_and_the_rest_to_the_leftmost()
    {
        // Arrange
        ToolbarRowPanel sut = new();
        ToolbarOverflowPanel first = Strip(4, 20);
        ToolbarOverflowPanel second = Strip(4, 20);
        sut.Children.Add(first);
        sut.Children.Add(second);

        // full: 80 + 80; minimum: 36 + 36; available 120 → the first gets 84 (full 80), the second 40
        // Act
        sut.Measure(new Size(120, 30));

        // Assert
        first.HasOverflow.Should().BeFalse();
        second.HasOverflow.Should().BeTrue();
        second.OverflowedItems.Should().Equal(1, 2, 3);
    }
}
