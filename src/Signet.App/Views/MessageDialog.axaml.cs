using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>A simple modal window with a message and a single OK button.</summary>
public partial class MessageDialog : Window
{
    /// <summary>Initializes the window.</summary>
    public MessageDialog()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close();
    }

    /// <summary>Shows the message and waits for it to be acknowledged.</summary>
    public static Task ShowAsync(Window owner, string title, string message)
    {
        MessageDialog dialog = new() { Title = title };
        dialog.MessageText.Text = message;
        return dialog.ShowDialog(owner);
    }
}
