using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Insert Link" dialog: an <c>href</c> field plus a list of targets ((X)HTML files, their
/// identifiers, media files). Picking an item from the list fills in the href.
/// Returns the entered href or <c>null</c>.
/// </summary>
public partial class SelectHyperlinkWindow : Window
{
    private IReadOnlyList<HyperlinkTargetItem> _all = Array.Empty<HyperlinkTargetItem>();

    /// <summary>Initializes the window.</summary>
    public SelectHyperlinkWindow()
    {
        InitializeComponent();
        Filter.TextChanged += (_, _) => ApplyFilter();
        Targets.SelectionChanged += (_, _) =>
        {
            if (Targets.SelectedItem is HyperlinkTargetItem item)
            {
                HrefInput.Text = item.Href;
            }
        };
        OkButton.Click += (_, _) => Close((HrefInput.Text ?? string.Empty).Trim());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window; returns the href or <c>null</c> when cancelled.</summary>
    public static async Task<string?> AskAsync(
        Window owner, IReadOnlyList<HyperlinkTargetItem> targets, string current)
    {
        SelectHyperlinkWindow window = new() { _all = targets };
        window.HrefInput.Text = current;
        window.ApplyFilter();
        return await window.ShowDialog<string?>(owner);
    }

    private void ApplyFilter()
    {
        string q = Filter.Text?.Trim() ?? string.Empty;
        Targets.ItemsSource = q.Length == 0
            ? _all
            : _all.Where(t => t.Display.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
