using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Services;

namespace Signet.App.Views;

/// <summary>
/// Modal "document modified — save changes?" dialog with three outcomes (Save / Discard / Cancel).
/// </summary>
public partial class SaveChangesDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public SaveChangesDialog()
    {
        InitializeComponent();
        SaveButton.Click += (_, _) => Close(SaveChangesChoice.Save);
        DiscardButton.Click += (_, _) => Close(SaveChangesChoice.Discard);
        CancelButton.Click += (_, _) => Close(SaveChangesChoice.Cancel);
    }

    /// <summary>
    /// Shows the dialog and returns the user's choice (closing via the title bar = <see cref="SaveChangesChoice.Cancel"/>).
    /// <paramref name="message"/> replaces the default "document modified" text.
    /// </summary>
    public static async Task<SaveChangesChoice> AskAsync(Window owner, string? message = null)
    {
        SaveChangesDialog dialog = new();
        if (message is not null)
        {
            dialog.MessageText.Text = message;
        }

        SaveChangesChoice result = await dialog.ShowDialog<SaveChangesChoice>(owner);
        return result;
    }
}
