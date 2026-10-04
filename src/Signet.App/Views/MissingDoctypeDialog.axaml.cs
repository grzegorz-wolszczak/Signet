using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Resources;
using Signet.App.Services;

namespace Signet.App.Views;

/// <summary>
/// Warning before an operation on (X)HTML files without a DOCTYPE: the list of files (scrollable),
/// Yes / No and "Don't ask again". While "Don't ask again" is checked the No button is disabled —
/// the choice can be remembered only together with continuing.
/// </summary>
public partial class MissingDoctypeDialog : Window
{
    /// <summary>Initializes the window.</summary>
    public MissingDoctypeDialog()
    {
        InitializeComponent();
        YesButton.Click += (_, _) => Close(Answer);
        NoButton.Click += (_, _) => Close(MissingDoctypeAnswer.Cancel);
        DontAskAgainBox.IsCheckedChanged += (_, _) => NoButton.IsEnabled = DontAskAgainBox.IsChecked != true;
    }

    /// <summary>The answer the Yes button gives (depends on "Don't ask again").</summary>
    public MissingDoctypeAnswer Answer =>
        DontAskAgainBox.IsChecked == true ? MissingDoctypeAnswer.ContinueAndDontAskAgain : MissingDoctypeAnswer.Continue;

    /// <summary>
    /// Shows the dialog and returns the answer; closing the window without an answer (Esc, the close
    /// button) is <see cref="MissingDoctypeAnswer.Cancel"/>.
    /// </summary>
    public static async Task<MissingDoctypeAnswer> AskAsync(Window owner, string operationName, IReadOnlyList<string> fileNames) =>
        await Build(operationName, fileNames).ShowDialog<MissingDoctypeAnswer?>(owner).ConfigureAwait(true)
            ?? MissingDoctypeAnswer.Cancel;

    /// <summary>Configures the window without showing it (for render tests without a blocking <c>ShowDialog</c>).</summary>
    public static MissingDoctypeDialog Build(string operationName, IReadOnlyList<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(operationName);
        ArgumentNullException.ThrowIfNull(fileNames);
        MissingDoctypeDialog dialog = new();
        dialog.HeaderText.Text = Strings.Format("MissingDoctypeDialog_Header", operationName, fileNames.Count);
        dialog.FileList.ItemsSource = fileNames;
        return dialog;
    }
}
