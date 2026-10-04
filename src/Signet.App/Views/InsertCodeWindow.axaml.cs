using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;

namespace Signet.App.Views;

/// <summary>
/// "Insert Code" — the form in which to insert a character's code: a hexadecimal or decimal numeric
/// character reference (both valid in XHTML regardless of the DOCTYPE) or the plain text <c>U+XXXX</c>.
/// </summary>
public partial class InsertCodeWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public InsertCodeWindow()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>The three forms of the code of <paramref name="codePoint"/>: <c>&amp;#x2014;</c>, <c>&amp;#8212;</c>, <c>U+2014</c>.</summary>
    public static (string Hex, string Decimal, string Text) Forms(int codePoint) => (
        string.Create(CultureInfo.InvariantCulture, $"&#x{codePoint:X4};"),
        string.Create(CultureInfo.InvariantCulture, $"&#{codePoint};"),
        string.Create(CultureInfo.InvariantCulture, $"U+{codePoint:X4}"));

    /// <summary>Shows the window; returns the chosen form, or <c>null</c> when cancelled.</summary>
    public static Task<string?> AskAsync(Window owner, int codePoint) =>
        Build(codePoint).ShowDialog<string?>(owner);

    /// <summary>Configures the window without showing it (for render tests without a blocking <c>ShowDialog</c>).</summary>
    public static InsertCodeWindow Build(int codePoint)
    {
        (string hex, string dec, string text) = Forms(codePoint);
        InsertCodeWindow window = new();
        window.PromptText.Text = Strings.Format("InsertCodeWindow_Prompt", text);
        window.HexButton.Content = Strings.Format("InsertCodeWindow_Hex", hex);
        window.DecimalButton.Content = Strings.Format("InsertCodeWindow_Decimal", dec);
        window.TextButton.Content = Strings.Format("InsertCodeWindow_Text", text);
        window.HexButton.Click += (_, _) => window.Close(hex);
        window.DecimalButton.Click += (_, _) => window.Close(dec);
        window.TextButton.Click += (_, _) => window.Close(text);
        return window;
    }
}
