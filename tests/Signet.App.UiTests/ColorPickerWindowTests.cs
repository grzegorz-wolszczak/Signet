using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>The color picker window in Preferences: without an alpha channel by default, returning #RRGGBB.</summary>
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

    [AvaloniaFact]
    public void ToHexWithAlpha_keeps_the_alpha_of_a_translucent_color_only()
    {
        ColorPickerWindow.ToHexWithAlpha(Color.FromArgb(0x80, 0x0A, 0xBC, 0xFF)).Should().Be("#800ABCFF");
        ColorPickerWindow.ToHexWithAlpha(Color.FromArgb(0xFF, 0x0A, 0xBC, 0xFF)).Should().Be("#0ABCFF");
    }

    private static ColorPickerWindow Shown(bool alpha = false, string initial = "#FFFFFF")
    {
        ColorPickerWindow window = ColorPickerWindow.Create("test", initial, alpha);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return window;
    }

    [AvaloniaFact]
    public void Offers_the_basic_palette_and_the_screen_picker()
    {
        ColorPickerWindow window = Shown();

        window.FindControl<WrapPanel>("BasicHost")!.Children.Should().HaveCount(BasicColorPalette.Colors.Count);
        window.FindControl<Button>("PickScreenButton")!.Should().NotBeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void A_palette_swatch_sets_the_color_and_the_name_follows()
    {
        ColorPickerWindow window = Shown();
        Button red = window.FindControl<WrapPanel>("BasicHost")!.Children.OfType<Button>()
            .First(b => ((Border)b.Content!).Background is Avalonia.Media.SolidColorBrush { Color: var c } && c == Colors.Red);

        red.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        window.FindControl<ColorView>("Picker")!.Color.Should().Be(Colors.Red);
        window.FindControl<TextBox>("NameBox")!.Text.Should().Be("red");
        window.Close();
    }

    [AvaloniaFact]
    public void A_typed_css_name_sets_the_color_but_keeps_the_alpha()
    {
        ColorPickerWindow window = Shown(alpha: true, initial: "#80FFFFFF");
        TextBox name = window.FindControl<TextBox>("NameBox")!;

        name.Text = "tomato";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        window.FindControl<ColorView>("Picker")!.Color.Should().Be(Color.FromArgb(0x80, 0xFF, 0x63, 0x47));
        window.Close();
    }

    [AvaloniaFact]
    public void An_unknown_name_is_reverted_to_the_current_color_name()
    {
        ColorPickerWindow window = Shown();
        TextBox name = window.FindControl<TextBox>("NameBox")!;

        name.Text = "definitely-not-a-color";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        name.Text.Should().Be("white");
        window.FindControl<ColorView>("Picker")!.Color.Should().Be(Colors.White);
        window.Close();
    }

    [AvaloniaFact]
    public void Adding_a_custom_color_fills_the_first_slot()
    {
        ColorPickerWindow window = Shown(initial: "#123456");

        window.FindControl<Button>("AddCustomButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        window.FindControl<WrapPanel>("CustomHost")!.Children[0].Should().BeOfType<Button>();
        window.Close();
    }
}
