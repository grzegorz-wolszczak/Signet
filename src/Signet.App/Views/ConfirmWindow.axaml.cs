using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>A simple confirmation window (OK / Cancel).</summary>
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
    /// replaces the default "OK" caption on the confirmation button.
    /// </summary>
    public static async Task<bool> AskAsync(Window owner, string title, string message, string? okText = null)
    {
        ConfirmWindow window = new() { Title = title };
        window.MessageText.Text = message;
        if (okText is not null)
        {
            window.OkButton.Content = okText;
        }
        return await window.ShowDialog<bool>(owner);
    }
}
