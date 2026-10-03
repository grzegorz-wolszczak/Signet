using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;

namespace Signet.App.Views;

/// <summary>
/// "Move Files" dialog: an editable field with suggestions of the existing folders for the
/// media-type group. An empty field / the placeholder means the EPUB root.
/// </summary>
public partial class SelectFolderWindow : Window
{
    // Localized "<epub root>" placeholder.
    private static string RootPlaceholder => Strings.Get("SelectFolderWindow_Root");

    /// <summary>Initializes the window.</summary>
    public SelectFolderWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(Normalize(FolderInput.Text));
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>
    /// Shows the window; returns the target folder's book path (empty string = EPUB root) or
    /// <c>null</c> when cancelled.
    /// </summary>
    public static async Task<string?> AskAsync(Window owner, IReadOnlyList<string> existingFolders, string current)
    {
        SelectFolderWindow window = Build(existingFolders, current);
        return await window.ShowDialog<string?>(owner);
    }

    /// <summary>
    /// Configures the window without showing it (split out of <see cref="AskAsync"/> so rendering
    /// can be tested without a blocking <c>ShowDialog</c>, see <c>CommonDialogsTests</c>).
    /// </summary>
    public static SelectFolderWindow Build(IReadOnlyList<string> existingFolders, string current)
    {
        SelectFolderWindow window = new();
        window.FolderInput.ItemsSource = existingFolders
            .Select(f => f.Length == 0 ? RootPlaceholder : f)
            .Distinct()
            .OrderBy(s => s, System.StringComparer.Ordinal)
            .ToList();
        window.FolderInput.Text = current.Length == 0 ? RootPlaceholder : current;
        return window;
    }

    private static string Normalize(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (string.Equals(trimmed, RootPlaceholder, System.StringComparison.Ordinal))
        {
            trimmed = string.Empty;
        }

        return trimmed.TrimEnd('/');
    }
}
