using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace Signet.App.Views;

/// <summary>
/// Color picker window (the "…" button next to the color fields in Preferences). Returns the color
/// <c>#RRGGBB</c> after OK, or <see langword="null"/> after Cancel / closing the window.
/// </summary>
public partial class ColorPickerWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ColorPickerWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(ToHex(Picker.Color));
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window with the initial color <paramref name="initialHex"/> (unparseable → black).</summary>
    public static Task<string?> AskAsync(Window owner, string title, string initialHex)
    {
        ColorPickerWindow window = new() { Title = title };
        window.Picker.Color = Color.TryParse(initialHex, out Color color) ? color : Colors.Black;
        return window.ShowDialog<string?>(owner);
    }

    /// <summary>The color as <c>#RRGGBB</c> (upper case, no alpha).</summary>
    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
