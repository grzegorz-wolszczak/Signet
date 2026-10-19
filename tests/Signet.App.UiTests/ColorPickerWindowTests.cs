using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>
/// The color picker window (KColorChooser-style): without an alpha channel by default, returning #RRGGBB; editable
/// HSV / RGB / alpha / HTML / CSS name fields kept in sync with the spectrum, the sliders, the palettes and the preview.
/// </summary>
public sealed class ColorPickerWindowTests
{
    private static ColorPickerWindow Shown(bool alpha = false, string initial = "#FFFFFF")
    {
        ColorPickerWindow window = ColorPickerWindow.Create("test", initial, alpha);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return window;
    }

    private static T Find<T>(ColorPickerWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    private static void PressEnter(TextBox box) =>
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Renders_without_the_alpha_row_by_default()
    {
        // Act
        ColorPickerWindow window = Shown();

        // Assert
        window.IsAlphaEnabled.Should().BeFalse();
        Find<Grid>(window, "AlphaRow").IsVisible.Should().BeFalse();
        Find<ColorSpectrum>(window, "Spectrum").Should().NotBeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void ToHex_formats_rrggbb_without_alpha()
    {
        // Act & Assert
        ColorPickerWindow.ToHex(Color.FromArgb(0x80, 0x0A, 0xBC, 0xFF)).Should().Be("#0ABCFF");
    }

    [AvaloniaFact]
    public void ToHexWithAlpha_keeps_the_alpha_of_a_translucent_color_only()
    {
        // Act & Assert
        ColorPickerWindow.ToHexWithAlpha(Color.FromArgb(0x80, 0x0A, 0xBC, 0xFF)).Should().Be("#800ABCFF");
        ColorPickerWindow.ToHexWithAlpha(Color.FromArgb(0xFF, 0x0A, 0xBC, 0xFF)).Should().Be("#0ABCFF");
    }

    [AvaloniaFact]
    public void An_opaque_window_drops_the_alpha_of_the_initial_color()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#80102030");

        // Assert
        window.Color.Should().Be(Color.FromRgb(0x10, 0x20, 0x30));
        window.Close();
    }

    [AvaloniaFact]
    public void Offers_the_basic_palette_the_screen_picker_and_the_other_palettes()
    {
        // Act
        ColorPickerWindow window = Shown();

        // Assert
        Find<WrapPanel>(window, "PaletteHost").Children.Should().HaveCount(BasicColorPalette.Colors.Count);
        Find<Button>(window, "PickScreenButton").Should().NotBeNull();

        // Act
        Find<ComboBox>(window, "PaletteCombo").SelectedIndex = 1;

        // Assert
        Find<WrapPanel>(window, "PaletteHost").Children.Should().HaveCount(CssColorNames.All.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void A_palette_swatch_sets_the_color_and_the_name_follows()
    {
        // Act
        ColorPickerWindow window = Shown();
        Button red = Find<WrapPanel>(window, "PaletteHost").Children.OfType<Button>()
            .First(b => ((Border)b.Content!).Background is SolidColorBrush { Color: var c } && c == Colors.Red);

        Click(red);

        // Assert
        window.Color.Should().Be(Colors.Red);
        Find<TextBox>(window, "NameBox").Text.Should().Be("red");
        window.Close();
    }

    [AvaloniaFact]
    public void A_typed_css_name_sets_the_color_but_keeps_the_alpha()
    {
        // Act
        ColorPickerWindow window = Shown(alpha: true, initial: "#80FFFFFF");
        TextBox name = Find<TextBox>(window, "NameBox");

        name.Text = "tomato";
        PressEnter(name);

        // Assert
        window.Color.Should().Be(Color.FromArgb(0x80, 0xFF, 0x63, 0x47));
        window.Close();
    }

    [AvaloniaFact]
    public void An_unknown_name_is_reverted_to_the_current_color_name()
    {
        // Act
        ColorPickerWindow window = Shown();
        TextBox name = Find<TextBox>(window, "NameBox");

        name.Text = "definitely-not-a-color";
        PressEnter(name);

        // Assert
        name.Text.Should().Be("white");
        window.Color.Should().Be(Colors.White);
        window.Close();
    }

    [AvaloniaFact]
    public void Adding_a_custom_color_fills_the_first_custom_slot()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#123456");

        Click(Find<Button>(window, "AddCustomButton"));

        // Assert
        Find<ComboBox>(window, "PaletteCombo").SelectedIndex.Should().Be(2);
        Find<WrapPanel>(window, "PaletteHost").Children[0].Should().BeOfType<Button>();
        window.Close();
    }

    [AvaloniaFact]
    public void The_numeric_fields_show_the_color_in_hsb_rgb_and_html()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#FAB41C");

        // Assert
        Find<NumericUpDown>(window, "RedBox").Value.Should().Be(250);
        Find<NumericUpDown>(window, "GreenBox").Value.Should().Be(180);
        Find<NumericUpDown>(window, "BlueBox").Value.Should().Be(28);
        Find<NumericUpDown>(window, "HueBox").Value.Should().Be(41);
        Find<NumericUpDown>(window, "SaturationBox").Value.Should().Be(89);
        Find<NumericUpDown>(window, "BrightnessBox").Value.Should().Be(98);
        Find<TextBox>(window, "HexBox").Text.Should().Be("#FAB41C");
        window.Close();
    }

    [AvaloniaFact]
    public void Editing_an_rgb_field_changes_the_color_and_the_other_fields()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#000000");

        Find<NumericUpDown>(window, "RedBox").Value = 255;

        // Assert
        window.Color.Should().Be(Color.FromRgb(255, 0, 0));
        Find<TextBox>(window, "HexBox").Text.Should().Be("#FF0000");
        Find<NumericUpDown>(window, "SaturationBox").Value.Should().Be(100);
        Find<NumericUpDown>(window, "BrightnessBox").Value.Should().Be(100);
        window.Close();
    }

    [AvaloniaFact]
    public void Editing_an_hsb_field_changes_the_color_and_the_rgb_fields()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#FF0000");

        Find<NumericUpDown>(window, "HueBox").Value = 120;

        // Assert
        window.Color.Should().Be(Color.FromRgb(0, 255, 0));
        Find<NumericUpDown>(window, "GreenBox").Value.Should().Be(255);
        Find<NumericUpDown>(window, "RedBox").Value.Should().Be(0);
        window.Close();
    }

    [AvaloniaFact]
    public void The_hue_survives_a_trip_through_black()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#00FF00");

        Find<NumericUpDown>(window, "BrightnessBox").Value = 0;

        // Assert
        Find<NumericUpDown>(window, "HueBox").Value.Should().Be(120);
        window.Color.Should().Be(Colors.Black);
        window.Close();
    }

    [AvaloniaFact]
    public void A_typed_html_code_sets_the_color_and_a_bad_one_is_reverted()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#FFFFFF");
        TextBox hex = Find<TextBox>(window, "HexBox");

        hex.Text = "#1a2b3c";
        PressEnter(hex);

        // Assert
        window.Color.Should().Be(Color.FromRgb(0x1A, 0x2B, 0x3C));
        hex.Text.Should().Be("#1A2B3C");

        // Act
        hex.Text = "zzz";
        PressEnter(hex);

        // Assert
        hex.Text.Should().Be("#1A2B3C");
        window.Close();
    }

    [AvaloniaFact]
    public void The_alpha_field_is_shown_only_with_alpha_and_edits_the_alpha()
    {
        // Act
        ColorPickerWindow opaque = Shown();

        // Assert
        Find<Grid>(opaque, "AlphaRow").IsVisible.Should().BeFalse();
        opaque.Close();

        // Act
        ColorPickerWindow window = Shown(alpha: true, initial: "#FFFFFFFF");

        // Assert
        Find<Grid>(window, "AlphaRow").IsVisible.Should().BeTrue();

        // Act
        Find<NumericUpDown>(window, "AlphaBox").Value = 50;

        // Assert
        window.Color.A.Should().Be(128);
        window.Close();
    }

    [AvaloniaFact]
    public void The_radio_buttons_choose_which_component_the_vertical_slider_edits()
    {
        // Act
        ColorPickerWindow window = Shown();
        ColorSlider slider = Find<ColorSlider>(window, "ThirdSlider");

        // Assert
        slider.ColorComponent.Should().Be(ColorComponent.Component3); // brightness by default

        // Act
        Find<RadioButton>(window, "HueRadio").IsChecked = true;

        // Assert
        slider.ColorComponent.Should().Be(ColorComponent.Component1);
        Find<ColorSpectrum>(window, "Spectrum").Components.Should().Be(ColorSpectrumComponents.SaturationValue);
        window.Close();
    }

    [AvaloniaFact]
    public void New_follows_the_edits_and_Previous_keeps_the_initial_color()
    {
        // Act
        ColorPickerWindow window = Shown(initial: "#112233");

        Find<NumericUpDown>(window, "RedBox").Value = 200;

        // Assert
        ((SolidColorBrush)Find<Border>(window, "NewSwatch").Background!).Color.R.Should().Be(200);
        ((SolidColorBrush)Find<Border>(window, "PreviousSwatch").Background!).Color.Should().Be(Color.FromRgb(0x11, 0x22, 0x33));
        window.Close();
    }

    [AvaloniaFact]
    public void The_custom_palette_fills_the_palette_area_without_scrolling()
    {
        // Act
        ColorPickerWindow window = Shown();
        Find<ComboBox>(window, "PaletteCombo").SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        WrapPanel host = Find<WrapPanel>(window, "PaletteHost");
        ScrollViewer scroll = host.FindAncestorOfType<ScrollViewer>()!;

        // Assert
        host.Children.Should().HaveCount(132);
        scroll.Extent.Height.Should().BeLessThanOrEqualTo(scroll.Viewport.Height);
        // One more row would no longer fit: the area is used to the full.
        (scroll.Extent.Height + 22).Should().BeGreaterThan(scroll.Viewport.Height);
        window.Close();
    }
}
