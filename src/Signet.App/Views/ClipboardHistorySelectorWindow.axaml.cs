using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// The "Paste From Clipboard History" dialog (just the picker window; history tracking is done by
/// <c>ClipboardHistoryService</c>). Returns the full (untruncated) text of the chosen entry,
/// or <c>null</c> after cancelling.
/// </summary>
public partial class ClipboardHistorySelectorWindow : Window
{
    private const int MaxDisplayLength = 500;

    private IReadOnlyList<string> _items = System.Array.Empty<string>();

    /// <summary>Initializes the window.</summary>
    public ClipboardHistorySelectorWindow()
    {
        InitializeComponent();
        ItemsList.DoubleTapped += (_, _) => AcceptSelection();
        OkButton.Click += (_, _) => AcceptSelection();
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window; returns the chosen history entry, or <c>null</c> after cancelling.</summary>
    public static async Task<string?> AskAsync(Window owner, IReadOnlyList<string> history)
    {
        ClipboardHistorySelectorWindow window = Build(history);
        return await window.ShowDialog<string?>(owner);
    }

    /// <summary>
    /// Configures the window without showing it (extracted from <see cref="AskAsync"/> for render tests
    /// without a blocking <c>ShowDialog</c>).
    /// </summary>
    public static ClipboardHistorySelectorWindow Build(IReadOnlyList<string> history)
    {
        ClipboardHistorySelectorWindow window = new() { _items = history };
        window.ItemsList.ItemsSource = history.Select(DisplayTextFor).ToList();
        if (history.Count > 0)
        {
            window.ItemsList.SelectedIndex = 0;
        }

        return window;
    }

    private void AcceptSelection()
    {
        int index = ItemsList.SelectedIndex;
        if (index >= 0 && index < _items.Count)
        {
            Close(_items[index]);
        }
    }

    private static string DisplayTextFor(string text)
    {
        string singleLine = text.Replace("\r\n", " ↵ ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', '→');
        return singleLine.Length > MaxDisplayLength
            ? singleLine[..MaxDisplayLength] + "…"
            : singleLine;
    }
}
