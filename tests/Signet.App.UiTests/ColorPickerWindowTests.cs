using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>The color picker window in Preferences: renders without an alpha channel and returns #RRGGBB.</summary>
public sealed class ColorPickerWindowTests
{
    [AvaloniaFact]
    public void Renders_a_color_view_without_alpha()
    {
        ColorPickerWindow window = new();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        ColorView picker = window.FindControl<ColorView>("Picker")!;
        picker.IsAlphaEnabled.Should().BeFalse();
        picker.IsAlphaVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void ToHex_formats_rrggbb_without_alpha()
    {
        ColorPickerWindow.ToHex(Color.FromArgb(0x80, 0x0A, 0xBC, 0xFF)).Should().Be("#0ABCFF");
    }
}
