using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Signet.App.Resources;

namespace Signet.App.Views;

/// <summary>
/// Warnings collected while loading a publication (<c>Book.LoadWarnings</c>) — all in a single
/// window (read-only, selectable text), with the ability to copy everything.
/// </summary>
public partial class LoadWarningsDialog : Window
{
    /// <summary>Initializes the window.</summary>
    public LoadWarningsDialog()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close();
        CopyButton.Click += async (_, _) => await CopyAsync().ConfigureAwait(true);
    }

    /// <summary>Shows the warnings and waits for the window to close.</summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="fileName">Name of the loaded file (for the header).</param>
    /// <param name="warnings">The warnings (already translated).</param>
    public static Task ShowAsync(Window owner, string fileName, IReadOnlyList<string> warnings) =>
        Build(fileName, warnings).ShowDialog(owner);

    /// <summary>Configures the window without showing it (for render tests without a blocking <c>ShowDialog</c>).</summary>
    public static LoadWarningsDialog Build(string fileName, IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        LoadWarningsDialog dialog = new();
        dialog.HeaderText.Text = Strings.Format("LoadWarningsDialog_Header", fileName, warnings.Count);
        dialog.WarningsText.Text = FormatList(warnings);
        return dialog;
    }

    /// <summary>The warning list as text: each starting with "• ", separated by a blank line.</summary>
    public static string FormatList(IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        return string.Join(Environment.NewLine + Environment.NewLine, warnings.Select(w => "• " + w.Trim()));
    }

    private async Task CopyAsync()
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(HeaderText.Text + Environment.NewLine + Environment.NewLine + WarningsText.Text)
                .ConfigureAwait(true);
        }
    }
}
