using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.Core.MainUI;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// The "Bulk rename regex" window — a single window with a live preview
/// (instead of two consecutive dialogs: enter regex/replace → confirm the table →
/// go back to typing if rejected).
/// </summary>
public partial class RegexRenameWindow : Window
{
    private IReadOnlyList<Resource> _resources = System.Array.Empty<Resource>();
    private IReadOnlyList<string>? _newFilenames;

    /// <summary>Initializes the window.</summary>
    public RegexRenameWindow()
    {
        InitializeComponent();
        PatternBox.TextChanged += (_, _) => UpdatePreview();
        ReplacementBox.TextChanged += (_, _) => UpdatePreview();
        OkButton.Click += (_, _) => Close(_newFilenames is not null
            ? (PatternBox.Text ?? string.Empty, ReplacementBox.Text ?? string.Empty)
            : ((string, string)?)null);
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>
    /// Shows the window; returns the entered pattern/replacement and the resulting file names once accepted
    /// (when the preview was valid), otherwise <c>null</c>.
    /// </summary>
    public static async Task<(string Pattern, string Replacement, IReadOnlyList<string> NewFilenames)?> AskAsync(
        Window owner, IReadOnlyList<Resource> resources, string initialPattern = "", string initialReplacement = "")
    {
        RegexRenameWindow window = new() { _resources = resources };
        window.PatternBox.Text = initialPattern;
        window.ReplacementBox.Text = initialReplacement;
        window.UpdatePreview();

        (string Pattern, string Replacement)? result = await window.ShowDialog<(string, string)?>(owner);
        if (result is null || window._newFilenames is null)
        {
            return null;
        }

        return (result.Value.Pattern, result.Value.Replacement, window._newFilenames);
    }

    private void UpdatePreview()
    {
        string pattern = PatternBox.Text ?? string.Empty;
        string replacement = ReplacementBox.Text ?? string.Empty;

        if (pattern.Length == 0)
        {
            _newFilenames = null;
            ErrorText.IsVisible = false;
            PreviewList.ItemsSource = null;
            OkButton.IsEnabled = false;
            return;
        }

        bool ok = BulkRegexRenaming.TryPreviewNewFilenames(
            _resources, pattern, replacement, out IReadOnlyList<string>? newFilenames, out string? error);

        if (!ok || newFilenames is null)
        {
            _newFilenames = null;
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            PreviewList.ItemsSource = null;
            OkButton.IsEnabled = false;
            return;
        }

        _newFilenames = newFilenames;
        ErrorText.IsVisible = false;
        OkButton.IsEnabled = true;

        List<string> rows = new();
        for (int i = 0; i < _resources.Count; i++)
        {
            rows.Add($"{_resources[i].Filename}  →  {newFilenames[i]}");
        }

        PreviewList.ItemsSource = rows;
    }
}
