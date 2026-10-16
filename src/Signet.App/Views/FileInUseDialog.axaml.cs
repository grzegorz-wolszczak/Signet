using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.App.Services;

namespace Signet.App.Views;

/// <summary>
/// Modal dialog shown when the file to load is already open in another instance of the application: close this instance,
/// start with an empty book, pick another file, or load the file anyway (the instances can then overwrite each other's
/// changes). Closing the window with the X button or Esc is <see cref="FileInUseChoice.Close"/>.
/// </summary>
public partial class FileInUseDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public FileInUseDialog()
    {
        InitializeComponent();
        OpenEmptyButton.Click += (_, _) => Close(FileInUseChoice.OpenEmpty);
        ChooseAnotherButton.Click += (_, _) => Close(FileInUseChoice.ChooseAnother);
        OpenAnywayButton.Click += (_, _) => Close(FileInUseChoice.OpenAnyway);
        CloseButton.Click += (_, _) => Close(FileInUseChoice.Close);
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and returns the user's choice.</summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="fileName">Name of the file that is open in another instance.</param>
    public static async Task<FileInUseChoice> AskAsync(Window owner, string fileName)
    {
        FileInUseDialog dialog = Build(fileName);
        return await dialog.ShowDialog<FileInUseChoice>(owner);
    }

    /// <summary>Configures the dialog without showing it (for render tests).</summary>
    public static FileInUseDialog Build(string fileName)
    {
        FileInUseDialog dialog = new();
        dialog.MessageText.Text = Strings.Format("FileInUseDialog_Message", fileName);
        return dialog;
    }
}
