using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// "Insert ID" dialog: an editable field with suggestions of the identifiers already present
/// in the file. Returns the entered identifier or <c>null</c>.
/// </summary>
public partial class SelectIdWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public SelectIdWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close((IdInput.Text ?? string.Empty).Trim());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window; returns the identifier or <c>null</c> when cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, IReadOnlyList<string> existingIds, string current)
    {
        SelectIdWindow window = new();
        window.IdInput.ItemsSource = existingIds.Distinct().OrderBy(s => s, System.StringComparer.Ordinal).ToList();
        window.IdInput.Text = current;
        return await window.ShowDialog<string?>(owner);
    }
}
