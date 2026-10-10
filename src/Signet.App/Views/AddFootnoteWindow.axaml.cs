using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The "Add Footnote" window from the Code View context menu: the text around the caret, the text of the footnote
/// and its placement (local or global). OK is not the default button, so Enter makes a new line in the text.
/// </summary>
public partial class AddFootnoteWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public AddFootnoteWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => ContentBox.Focus();
    }

    /// <summary>Shows the window; returns the footnote to add once accepted, or <c>null</c>.</summary>
    public static Task<AddFootnoteRequest?> AskAsync(Window owner, AddFootnoteViewModel viewModel)
    {
        AddFootnoteWindow window = new() { DataContext = viewModel };
        return window.ShowDialog<AddFootnoteRequest?>(owner);
    }

    private void Accept()
    {
        if (DataContext is AddFootnoteViewModel { CanAccept: true } vm)
        {
            Close(vm.Request());
        }
    }
}
