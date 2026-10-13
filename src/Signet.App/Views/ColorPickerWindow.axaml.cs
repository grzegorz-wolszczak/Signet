using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace Signet.App.Views;

/// <summary>
/// Color picker window (the "…" button next to the color fields in Preferences). Returns the color
/// <c>#RRGGBB</c> (with the alpha channel: <c>#AARRGGBB</c> when not opaque) after OK, or <see langword="null"/>
/// after Cancel / closing the window.
/// </summary>
public partial class ColorPickerWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ColorPickerWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(Picker.IsAlphaEnabled ? ToHexWithAlpha(Picker.Color) : ToHex(Picker.Color));
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window with the initial color <paramref name="initialHex"/> (unparseable → black).</summary>
    /// <param name="owner">The window the picker is modal to.</param>
    /// <param name="title">The title.</param>
    /// <param name="initialHex">The initial color (<c>#RRGGBB</c>, or <c>#AARRGGBB</c> with <paramref name="alpha"/>).</param>
    /// <param name="alpha">Whether the alpha channel can be set.</param>
    public static Task<string?> AskAsync(Window owner, string title, string initialHex, bool alpha = false)
    {
        ColorPickerWindow window = new() { Title = title };
        window.Picker.IsAlphaEnabled = alpha;
        window.Picker.IsAlphaVisible = alpha;
        window.Picker.Color = Color.TryParse(initialHex, out Color color) ? color : Colors.Black;
        return window.ShowDialog<string?>(owner);
    }

    /// <summary>The color as <c>#RRGGBB</c> when opaque, <c>#AARRGGBB</c> otherwise (upper case).</summary>
    public static string ToHexWithAlpha(Color color) => color.A == 255
        ? ToHex(color)
        : string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>The color as <c>#RRGGBB</c> (upper case, no alpha).</summary>
    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
