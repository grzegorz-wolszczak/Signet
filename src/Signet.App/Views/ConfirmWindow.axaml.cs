using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>A simple confirmation window (OK / Cancel), optionally with "Don't ask again" or a check box of its own.</summary>
public partial class ConfirmWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ConfirmWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    /// <summary>
    /// Shows the window and returns <c>true</c> when the user confirmed. <paramref name="okText"/>
    /// replaces the default "OK" caption on the confirmation button; <paramref name="destructive"/> makes it red
    /// (it removes or deletes something).
    /// </summary>
    public static async Task<bool> AskAsync(
        Window owner, string title, string message, string? okText = null, bool destructive = false) =>
        await Build(title, message, okText, dontAskAgain: false, destructive).ShowDialog<bool>(owner);

    /// <summary>
    /// Shows the window with a "Don't ask again" check box; returns whether the user confirmed and whether the box was
    /// checked then (it counts only together with confirming).
    /// </summary>
    public static async Task<(bool Confirmed, bool DontAskAgain)> AskWithDontAskAgainAsync(
        Window owner, string title, string message, string? okText = null, bool destructive = false)
    {
        ConfirmWindow window = Build(title, message, okText, dontAskAgain: true, destructive);
        bool confirmed = await window.ShowDialog<bool>(owner);
        return (confirmed, confirmed && window.DontAskAgainBox.IsChecked == true);
    }

    /// <summary>
    /// Shows the window with a check box of its own (<paramref name="optionText"/>) under the message; returns whether
    /// the user confirmed and whether the box was checked then (it counts only together with confirming). The box starts
    /// unchecked — confirming without touching it does only what the message says.
    /// </summary>
    public static async Task<(bool Confirmed, bool Option)> AskWithOptionAsync(
        Window owner, string title, string message, string optionText, string? okText = null, bool destructive = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(optionText);
        ConfirmWindow window = Build(title, message, okText, dontAskAgain: false, destructive);
        window.OptionBox.Content = optionText;
        window.OptionBox.IsVisible = true;
        bool confirmed = await window.ShowDialog<bool>(owner);
        return (confirmed, confirmed && window.OptionBox.IsChecked == true);
    }

    private static ConfirmWindow Build(string title, string message, string? okText, bool dontAskAgain, bool destructive)
    {
        ConfirmWindow window = new() { Title = title };
        window.MessageText.Text = message;
        window.DontAskAgainBox.IsVisible = dontAskAgain;
        if (destructive)
        {
            window.OkButton.Classes.Add(ButtonClasses.Danger);
        }

        if (okText is not null)
        {
            window.OkButton.Content = okText;
        }

        return window;
    }
}
