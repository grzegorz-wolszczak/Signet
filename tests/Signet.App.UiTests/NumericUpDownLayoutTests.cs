using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;

namespace Signet.App.UiTests;

/// <summary>
/// The spinner buttons of <see cref="NumericUpDown"/> (e.g. the font size in the font picker):
/// "down" must be on the left and "up" on the right.
/// </summary>
public sealed class NumericUpDownLayoutTests
{
    [AvaloniaFact]
    public void Decrease_button_is_left_of_the_increase_button()
    {
        NumericUpDown box = new() { Value = 16, Minimum = 6, Maximum = 72, Width = 160 };
        var window = new Window { Width = 300, Height = 100, Content = box };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        RepeatButton increase = box.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_IncreaseButton");
        RepeatButton decrease = box.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_DecreaseButton");
        Point increaseX = increase.TranslatePoint(default, window)!.Value;
        Point decreaseX = decrease.TranslatePoint(default, window)!.Value;

        decreaseX.X.Should().BeLessThan(increaseX.X, "the down arrow is on the left, the up arrow on the right");
        window.Close();
    }
}
