using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// "Revert to before / after …" that would remove, bring back or rename files — lists them (files that disappear
/// together with edits made in them as warnings) before the revert: "Revert" carries it out, Cancel (the default)
/// does not.
/// </summary>
public partial class CheckpointRevertWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public CheckpointRevertWindow()
    {
        InitializeComponent();
        RevertButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>Shows the files affected by <paramref name="request"/>; <c>true</c> when the user confirmed the revert.</summary>
    public static async Task<bool> AskAsync(Window owner, CheckpointRevertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CheckpointRevertWindow window = new() { Title = request.Title };
        window.Files.ItemsSource = request.Rows;
        return await window.ShowDialog<bool>(owner);
    }
}
