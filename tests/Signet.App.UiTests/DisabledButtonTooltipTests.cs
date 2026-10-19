using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;

namespace Signet.App.UiTests;

/// <summary>A disabled button (e.g. in a toolbar) still shows its tooltip.</summary>
public sealed class DisabledButtonTooltipTests
{
    [AvaloniaFact]
    public void Disabled_buttons_show_their_tooltips()
    {
        // Arrange
        Button button = new() { Content = "a", IsEnabled = false };
        ToggleButton toggle = new() { Content = "b", IsEnabled = false };
        SplitButton split = new() { Content = "c", IsEnabled = false };
        TextBlock text = new() { Text = "d" };
        Window window = new() { Content = new StackPanel { Children = { button, toggle, split, text } } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Assert
        ToolTip.GetShowOnDisabled(button).Should().BeTrue();
        ToolTip.GetShowOnDisabled(toggle).Should().BeTrue();
        ToolTip.GetShowOnDisabled(split).Should().BeTrue();
        ToolTip.GetShowOnDisabled(text).Should().BeFalse("only buttons are affected");
        window.Close();
    }
}
