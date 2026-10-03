using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Insert Clip" dialog backing the <c>MainWindow.InsertClip</c> action. A flat, filterable list
/// of clips (leaves only, no groups); the chosen clip's text is pasted into the active document
/// (wrapping the selection via the <c>\1</c> placeholder). Returns the chosen text or
/// <see langword="null"/> when cancelled.
/// </summary>
public partial class SelectClipWindow : Window
{
    private IReadOnlyList<ClipPickerItem> _all = Array.Empty<ClipPickerItem>();

    /// <summary>Initializes the window.</summary>
    public SelectClipWindow()
    {
        InitializeComponent();
        Filter.TextChanged += (_, _) => ApplyFilter();
        OkButton.Click += (_, _) => Close((Clips.SelectedItem as ClipPickerItem)?.Text);
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window; returns the chosen clip's text or <see langword="null"/> when cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, IReadOnlyList<ClipPickerItem> clips)
    {
        SelectClipWindow window = new() { _all = clips };
        window.ApplyFilter();
        return await window.ShowDialog<string?>(owner);
    }

    private void ApplyFilter()
    {
        string q = Filter.Text?.Trim() ?? string.Empty;
        Clips.ItemsSource = q.Length == 0
            ? _all
            : _all.Where(c => c.Display.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
