using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Signet.App.Infrastructure;
using Signet.App.ScreenPicker;

namespace Signet.App.Views;

/// <summary>
/// Color picker window (the "…" button next to the color fields in Preferences), laid out like KColorChooser. Returns
/// the color <c>#RRGGBB</c> (with the alpha channel: <c>#AARRGGBB</c> when not opaque) after OK, or
/// <see langword="null"/> after Cancel / closing the window. Besides the spectrum and sliders it offers switchable
/// palettes (basic, CSS, custom), editable HSV / RGB / alpha / HTML fields, a CSS color name field and a screen color
/// picker (a frozen desktop with a pixel magnifier).
/// </summary>
public partial class ColorPickerWindow : Window
{
    // As many as fit the palette area without scrolling: 12 columns x 11 rows of 22 px cells (asserted by a UI test).
    private const int CustomColorSlots = 132;
    private const double SwatchSize = 20;
    private const int BasicPalette = 0;
    private const int CssPalette = 1;
    private const int CustomPalette = 2;

    // Shared by all picker windows of the session (not persisted).
    private static readonly List<Color> s_customColors = [];

    // The single source of truth (alpha included); every control is a view of it.
    private HsvColor _hsv = new(1, 0, 0, 0);
    private bool _syncing;

    /// <summary>Initializes the window.</summary>
    public ColorPickerWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(IsAlphaEnabled ? ToHexWithAlpha(Color) : ToHex(Color));
        CancelButton.Click += (_, _) => Close(null);

        Spectrum.PropertyChanged += (_, e) => OnPickerHsvChanged(e, Spectrum.HsvColor, alphaToo: false);
        ThirdSlider.PropertyChanged += (_, e) => OnPickerHsvChanged(e, ThirdSlider.HsvColor, alphaToo: false);
        AlphaSlider.PropertyChanged += (_, e) =>
        {
            if (!_syncing && e.Property == ColorSlider.HsvColorProperty)
            {
                SetHsv(new HsvColor(AlphaSlider.HsvColor.A, _hsv.H, _hsv.S, _hsv.V));
            }
        };
        foreach (NumericUpDown box in new[] { HueBox, SaturationBox, BrightnessBox })
        {
            box.ValueChanged += (_, _) => ApplyHsvFields();
        }

        foreach (NumericUpDown box in new[] { RedBox, GreenBox, BlueBox })
        {
            box.ValueChanged += (_, _) => ApplyRgbFields();
        }

        AlphaBox.ValueChanged += (_, _) => ApplyAlphaField();
        HueRadio.IsCheckedChanged += (_, _) => OnAxisChanged();
        SaturationRadio.IsCheckedChanged += (_, _) => OnAxisChanged();
        BrightnessRadio.IsCheckedChanged += (_, _) => OnAxisChanged();

        PaletteCombo.SelectionChanged += (_, _) => RebuildPalette();
        AddCustomButton.Click += (_, _) => AddCustomColor(Color);
        PickScreenButton.IsEnabled = ScreenColorPicker.IsSupported;
        PickScreenButton.Click += async (_, _) => await PickFromScreenAsync();

        NameBox.KeyDown += (_, e) => CommitOnEnter(e, CommitName);
        NameBox.LostFocus += (_, _) => CommitName();
        HexBox.KeyDown += (_, e) => CommitOnEnter(e, CommitHex);
        HexBox.LostFocus += (_, _) => CommitHex();

        OnAxisChanged();
        RebuildPalette();
        Sync();
    }

    /// <summary>The selected color (with its alpha; opaque unless <see cref="IsAlphaEnabled"/>).</summary>
    public Color Color
    {
        get => _hsv.ToRgb();
        set => SetHsv(KeepHue(value.ToHsv()));
    }

    /// <summary>Whether the alpha channel can be set (the alpha slider and field are shown).</summary>
    public bool IsAlphaEnabled { get; private set; }

    /// <summary>Shows the window with the initial color <paramref name="initialHex"/> (unparseable → black).</summary>
    /// <param name="owner">The window the picker is modal to.</param>
    /// <param name="title">The title.</param>
    /// <param name="initialHex">The initial color (<c>#RRGGBB</c>, or <c>#AARRGGBB</c> with <paramref name="alpha"/>).</param>
    /// <param name="alpha">Whether the alpha channel can be set.</param>
    public static Task<string?> AskAsync(Window owner, string title, string initialHex, bool alpha = false) =>
        Create(title, initialHex, alpha).ShowDialog<string?>(owner);

    /// <summary>Creates the window (not shown) with the initial color <paramref name="initialHex"/>.</summary>
    /// <param name="title">The title.</param>
    /// <param name="initialHex">The initial color (unparseable → black).</param>
    /// <param name="alpha">Whether the alpha channel can be set.</param>
    public static ColorPickerWindow Create(string title, string initialHex, bool alpha = false)
    {
        ColorPickerWindow window = new() { Title = title };
        window.IsAlphaEnabled = alpha;
        window.AlphaRow.IsVisible = alpha;
        Color initial = Color.TryParse(initialHex, out Color color) ? color : Colors.Black;
        window.Color = alpha ? initial : Color.FromRgb(initial.R, initial.G, initial.B);
        window.PreviousSwatch.Background = new SolidColorBrush(window.Color);
        return window;
    }

    /// <summary>The color as <c>#RRGGBB</c> when opaque, <c>#AARRGGBB</c> otherwise (upper case).</summary>
    public static string ToHexWithAlpha(Color color) => color.A == 255
        ? ToHex(color)
        : string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>The color as <c>#RRGGBB</c> (upper case, no alpha).</summary>
    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    // A grey or black color has no meaningful hue / saturation: keep the current ones so that the spectrum and the
    // fields do not jump to 0 while the user edits.
    private HsvColor KeepHue(HsvColor hsv) => hsv.V <= 0
        ? new HsvColor(hsv.A, _hsv.H, _hsv.S, 0)
        : hsv.S <= 0 ? new HsvColor(hsv.A, _hsv.H, 0, hsv.V) : hsv;

    private void SetHsv(HsvColor hsv)
    {
        _hsv = IsAlphaEnabled ? hsv : new HsvColor(1, hsv.H, hsv.S, hsv.V);
        Sync();
    }

    // The color the user changed with the spectrum or the vertical slider (a drag), as opposed to our own updates.
    private void OnPickerHsvChanged(AvaloniaPropertyChangedEventArgs e, HsvColor picked, bool alphaToo)
    {
        if (!_syncing && (e.Property == ColorSpectrum.HsvColorProperty || e.Property == ColorSlider.HsvColorProperty))
        {
            SetHsv(new HsvColor(alphaToo ? picked.A : _hsv.A, picked.H, picked.S, picked.V));
        }
    }

    // The radio button chooses the HSV component edited by the vertical slider; the spectrum shows the other two.
    private void OnAxisChanged()
    {
        (ColorSpectrumComponents components, ColorComponent slider) =
            HueRadio.IsChecked == true ? (ColorSpectrumComponents.SaturationValue, ColorComponent.Component1)
            : SaturationRadio.IsChecked == true ? (ColorSpectrumComponents.HueValue, ColorComponent.Component2)
            : (ColorSpectrumComponents.HueSaturation, ColorComponent.Component3);
        Spectrum.Components = components;
        ThirdSlider.ColorComponent = slider;
        Sync();
    }

    // Writes the current color into every control (the one the user edited gets the same value back).
    private void Sync()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            Color color = Color;
            HsvColor opaque = new(1, _hsv.H, _hsv.S, _hsv.V);
            Spectrum.HsvColor = opaque;
            ThirdSlider.HsvColor = opaque;
            AlphaSlider.HsvColor = _hsv;
            HueBox.Value = (decimal)Math.Round(_hsv.H);
            SaturationBox.Value = (decimal)Math.Round(_hsv.S * 100);
            BrightnessBox.Value = (decimal)Math.Round(_hsv.V * 100);
            RedBox.Value = color.R;
            GreenBox.Value = color.G;
            BlueBox.Value = color.B;
            AlphaBox.Value = (decimal)Math.Round(_hsv.A * 100);
            HexBox.Text = ToHex(color);
            NameBox.Text = CssColorNames.NameOf(color) ?? string.Empty;
            NewSwatch.Background = new SolidColorBrush(color);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ApplyHsvFields()
    {
        if (_syncing || HueBox.Value is not { } hue || SaturationBox.Value is not { } saturation || BrightnessBox.Value is not { } brightness)
        {
            return;
        }

        SetHsv(new HsvColor(_hsv.A, (double)hue, (double)saturation / 100, (double)brightness / 100));
    }

    private void ApplyRgbFields()
    {
        if (_syncing || RedBox.Value is not { } red || GreenBox.Value is not { } green || BlueBox.Value is not { } blue)
        {
            return;
        }

        Color = Color.FromArgb(Color.A, (byte)Math.Round(red), (byte)Math.Round(green), (byte)Math.Round(blue));
    }

    private void ApplyAlphaField()
    {
        if (!_syncing && IsAlphaEnabled && AlphaBox.Value is { } percent)
        {
            SetHsv(new HsvColor((double)percent / 100, _hsv.H, _hsv.S, _hsv.V));
        }
    }

    // The picked RGB replaces the color, the alpha channel stays as it is.
    private void ApplyRgb(Color color) => Color = Color.FromArgb(Color.A, color.R, color.G, color.B);

    private static void CommitOnEnter(KeyEventArgs e, Action commit)
    {
        if (e.Key == Key.Enter)
        {
            commit();
            e.Handled = true; // not the OK button
        }
    }

    // A typed CSS name sets the color; anything else restores the name of the current color.
    private void CommitName()
    {
        if (_syncing)
        {
            return;
        }

        if (CssColorNames.TryParse(NameBox.Text, out Color color))
        {
            ApplyRgb(color);
        }

        Sync();
    }

    // "#RRGGBB" / "RRGGBB" / "#RGB" / (with alpha) "#AARRGGBB" in the HTML field; anything else restores the current color.
    private void CommitHex()
    {
        if (_syncing)
        {
            return;
        }

        string text = (HexBox.Text ?? string.Empty).Trim().TrimStart('#');
        if (text.Length is 3 or 6 or 8 && text.All(Uri.IsHexDigit) && Color.TryParse("#" + text, out Color color))
        {
            if (text.Length == 8 && IsAlphaEnabled)
            {
                Color = color;
            }
            else
            {
                ApplyRgb(color);
            }
        }

        Sync();
    }

    private async Task PickFromScreenAsync()
    {
        PickFailedText.IsVisible = false;
        ScreenPickResult result = await ScreenColorPicker.PickAsync(this);
        if (result.Color is { } color)
        {
            ApplyRgb(color);
        }

        PickFailedText.IsVisible = result.Failed;
    }

    private void RebuildPalette()
    {
        PaletteHost.Children.Clear();
        switch (PaletteCombo.SelectedIndex)
        {
            case BasicPalette:
                foreach (Color color in BasicColorPalette.Colors)
                {
                    PaletteHost.Children.Add(Swatch(color, ToHex(color)));
                }

                break;
            case CssPalette:
                foreach ((string name, Color color) in CssColorNames.All)
                {
                    PaletteHost.Children.Add(Swatch(color, $"{name}  {ToHex(color)}"));
                }

                break;
            default:
                for (int i = 0; i < CustomColorSlots; i++)
                {
                    PaletteHost.Children.Add(i < s_customColors.Count
                        ? Swatch(s_customColors[i], ToHex(s_customColors[i]))
                        : EmptySlot());
                }

                break;
        }
    }

    private Button Swatch(Color color, string tip)
    {
        Button button = new()
        {
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            Width = SwatchSize,
            Height = SwatchSize,
            Background = Brushes.Transparent,
            Content = new Border
            {
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
            },
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => ApplyRgb(color);
        return button;
    }

    private static Border EmptySlot() => new()
    {
        Width = SwatchSize,
        Height = SwatchSize,
        Margin = new Thickness(1),
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        Opacity = 0.35,
        CornerRadius = new CornerRadius(2),
    };

    private void AddCustomColor(Color color)
    {
        Color opaque = Color.FromRgb(color.R, color.G, color.B);
        s_customColors.Remove(opaque);
        s_customColors.Insert(0, opaque);
        if (s_customColors.Count > CustomColorSlots)
        {
            s_customColors.RemoveRange(CustomColorSlots, s_customColors.Count - CustomColorSlots);
        }

        if (PaletteCombo.SelectedIndex == CustomPalette)
        {
            RebuildPalette();
        }
        else
        {
            PaletteCombo.SelectedIndex = CustomPalette; // rebuilds
        }
    }
}
