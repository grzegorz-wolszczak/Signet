using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>A modal question with several answers, one button each (e.g. the keymap's Remove / Leave / Cancel).</summary>
public partial class ChoiceWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public ChoiceWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the question; the index of the chosen answer, or -1 when the window was closed without one. The last
    /// answer is the cancel button (Esc).
    /// </summary>
    public static async Task<int> AskAsync(Window owner, string title, string message, params string[] answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ChoiceWindow window = new() { Title = title };
        window.MessageText.Text = message;
        for (int i = 0; i < answers.Length; i++)
        {
            int index = i;
            Button button = new() { Content = answers[i], MinWidth = 80, IsDefault = i == 0, IsCancel = i == answers.Length - 1 };
            button.Click += (_, _) => window.Close(index);
            window.Buttons.Children.Add(button);
        }

        return await window.ShowDialog<int?>(owner) ?? -1;
    }
}
