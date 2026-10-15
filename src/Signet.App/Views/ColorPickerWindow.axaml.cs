using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Signet.App.Infrastructure;
using Signet.App.ScreenPicker;

namespace Signet.App.Views;

/// <summary>
/// Color picker window (the "…" button next to the color fields in Preferences). Returns the color
/// <c>#RRGGBB</c> (with the alpha channel: <c>#AARRGGBB</c> when not opaque) after OK, or <see langword="null"/>
/// after Cancel / closing the window. Besides the spectrum it offers basic and custom colors, a CSS color name field and
/// a screen color picker (a frozen desktop with a pixel magnifier).
/// </summary>
public partial class ColorPickerWindow : Window
{
    private const int CustomColorSlots = 24;
    private const double SwatchSize = 20;

    // Shared by all picker windows of the session (not persisted).
    private static readonly List<Color> s_customColors = [];

    private bool _syncingName;

    /// <summary>Initializes the window.</summary>
    public ColorPickerWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(Picker.IsAlphaEnabled ? ToHexWithAlpha(Picker.Color) : ToHex(Picker.Color));
        CancelButton.Click += (_, _) => Close(null);

        foreach (Color color in BasicColorPalette.Colors)
        {
            BasicHost.Children.Add(Swatch(color));
        }

        RebuildCustomColors();
        AddCustomButton.Click += (_, _) => AddCustomColor(Picker.Color);

        PickScreenButton.IsEnabled = ScreenColorPicker.IsSupported;
        PickScreenButton.Click += async (_, _) => await PickFromScreenAsync();

        Picker.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorView.ColorProperty)
            {
                ShowName();
            }
        };
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitName();
                e.Handled = true; // not the OK button
            }
        };
        NameBox.LostFocus += (_, _) => CommitName();
        ShowName();
    }

    // The picked RGB replaces the color, the alpha channel stays as it is.
    private void ApplyRgb(Color color) =>
        Picker.Color = Color.FromArgb(Picker.IsAlphaEnabled ? Picker.Color.A : (byte)255, color.R, color.G, color.B);

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

    private Button Swatch(Color color)
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
        ToolTip.SetTip(button, ToHex(color));
        button.Click += (_, _) => ApplyRgb(color);
        return button;
    }

    private void AddCustomColor(Color color)
    {
        Color opaque = Color.FromRgb(color.R, color.G, color.B);
        s_customColors.Remove(opaque);
        s_customColors.Insert(0, opaque);
        if (s_customColors.Count > CustomColorSlots)
        {
            s_customColors.RemoveRange(CustomColorSlots, s_customColors.Count - CustomColorSlots);
        }

        RebuildCustomColors();
    }

    private void RebuildCustomColors()
    {
        CustomHost.Children.Clear();
        for (int i = 0; i < CustomColorSlots; i++)
        {
            CustomHost.Children.Add(i < s_customColors.Count
                ? Swatch(s_customColors[i])
                : new Border
                {
                    Width = SwatchSize,
                    Height = SwatchSize,
                    Margin = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Opacity = 0.35,
                    CornerRadius = new CornerRadius(2),
                });
        }
    }

    // The field shows the CSS name of the current color, when it has one.
    private void ShowName()
    {
        _syncingName = true;
        NameBox.Text = CssColorNames.NameOf(Picker.Color) ?? string.Empty;
        _syncingName = false;
    }

    // A typed CSS name sets the color; anything else restores the name of the current color.
    private void CommitName()
    {
        if (_syncingName)
        {
            return;
        }

        if (CssColorNames.TryParse(NameBox.Text, out Color color))
        {
            ApplyRgb(color);
        }

        ShowName();
    }

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
        window.Picker.IsAlphaEnabled = alpha;
        window.Picker.IsAlphaVisible = alpha;
        window.Picker.Color = Color.TryParse(initialHex, out Color color) ? color : Colors.Black;
        return window;
    }

    /// <summary>The color as <c>#RRGGBB</c> when opaque, <c>#AARRGGBB</c> otherwise (upper case).</summary>
    public static string ToHexWithAlpha(Color color) => color.A == 255
        ? ToHex(color)
        : string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>The color as <c>#RRGGBB</c> (upper case, no alpha).</summary>
    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
