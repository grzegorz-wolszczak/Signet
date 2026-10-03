using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>Simple modal window with a single text field (e.g. renaming a file).</summary>
public partial class TextPromptWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public TextPromptWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(Input.Text);
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the window and returns the entered text or <c>null</c> when cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string prompt, string initialValue)
    {
        TextPromptWindow window = new() { Title = title };
        window.PromptText.Text = prompt;
        window.Input.Text = initialValue;
        window.Input.SelectAll();
        return await window.ShowDialog<string?>(owner);
    }
}
