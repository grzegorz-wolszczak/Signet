using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The footnote window from the Code View context menu, for adding a footnote at the caret or editing the footnote
/// under it: the text around the reference, the text of the footnote and its placement (local or global). An edited
/// footnote can be removed ("Remove", after a confirmation unless it was turned off). OK is not the default button, so
/// Enter makes a new line in the text.
/// </summary>
public partial class FootnoteWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public FootnoteWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Accept();
        RemoveButton.Click += async (_, _) => await RemoveAsync().ConfigureAwait(true);
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => ContentBox.Focus();
    }

    /// <summary>Shows the window; returns what to do once accepted (add, save or remove), or <c>null</c>.</summary>
    public static Task<FootnoteRequest?> AskAsync(Window owner, FootnoteViewModel viewModel)
    {
        FootnoteWindow window = new() { DataContext = viewModel };
        return window.ShowDialog<FootnoteRequest?>(owner);
    }

    private void Accept()
    {
        if (DataContext is FootnoteViewModel { CanAccept: true } vm)
        {
            Close(vm.Request());
        }
    }

    private async Task RemoveAsync()
    {
        if (DataContext is not FootnoteViewModel { IsEditing: true } vm)
        {
            return;
        }

        if (vm.ConfirmRemoval)
        {
            (bool confirmed, bool dontAskAgain) = await ConfirmWindow.AskWithDontAskAgainAsync(
                this, Strings.Get("FootnoteWindow_RemoveConfirmTitle"), Strings.Get("FootnoteWindow_RemoveConfirm"),
                Strings.Get("FootnoteWindow_RemoveButton"), destructive: true).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }

            if (dontAskAgain)
            {
                vm.StopConfirmingRemoval?.Invoke();
            }
        }

        Close(vm.RemoveRequest());
    }
}
