using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Footnotes;

namespace Signet.App.Views;

/// <summary>
/// The modal "Convert Footnotes of Other Origin" dialog (<see cref="FootnoteConversionViewModel"/>): the footnotes of
/// other origin with checkboxes, the options and the notes file of the global ones. "Convert" converts the checked ones
/// and the dialog stays open with the footnotes left; "Close" closes it. A double click on the reference or the note of
/// an item opens it in the main window behind the dialog.
/// </summary>
public partial class FootnoteConversionWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public FootnoteConversionWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>
    /// Asks whether to convert although the checkpoint before the conversion could not be created (then a failed
    /// conversion cannot be undone).
    /// </summary>
    public static async Task<bool> ConfirmWithoutCheckpointAsync(Window owner) =>
        await ChoiceWindow.AskAsync(
            owner,
            Strings.Get("FootnoteConversionWindow_Title"),
            Strings.Get("FootnoteConversion_NoCheckpoint"),
            Strings.Get("FootnoteConversion_ConvertAnyway"),
            Strings.Get("Common_Cancel")) == 0;

    private void OnReferenceDoubleTapped(object? sender, TappedEventArgs e) => Navigate(sender, FootnoteSide.Reference, e);

    private void OnNoteDoubleTapped(object? sender, TappedEventArgs e) => Navigate(sender, FootnoteSide.Note, e);

    private void Navigate(object? sender, FootnoteSide side, TappedEventArgs e)
    {
        if (DataContext is FootnoteConversionViewModel vm && sender is Control { DataContext: FootnoteConversionItemViewModel item })
        {
            vm.Navigate(item, side);
            e.Handled = true;
        }
    }
}
