using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// Font picker window for Preferences: a list of system fonts (names rendered in their own
/// font), a name filter, a "fixed-width only" filter, size and preview.
/// </summary>
public partial class FontPickerWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public FontPickerWindow()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close(null);
        FontList.DoubleTapped += (_, _) => Accept();
        Opened += (_, _) =>
        {
            if (FontList.SelectedItem is { } selected)
            {
                FontList.ScrollIntoView(selected);
            }

            FilterBox.Focus();
        };

        // The down arrow in the filter field moves to the list (as in typical pickers).
        FilterBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && FontList.ItemCount > 0)
            {
                FontList.SelectedIndex = FontList.SelectedIndex < 0 ? 0 : FontList.SelectedIndex;
                (FontList.ContainerFromIndex(FontList.SelectedIndex) as Control)?.Focus();
                e.Handled = true;
            }
        };
    }

    /// <summary>Shows the window; returns the chosen font, or <see langword="null"/> after cancelling.</summary>
    public static async Task<FontPickResult?> AskAsync(Window owner, FontPickRequest request)
    {
        FontPickerWindow window = new() { DataContext = new FontPickerViewModel(FontCatalog.SystemFonts, request) };
        return await window.ShowDialog<FontPickResult?>(owner);
    }

    private void Accept()
    {
        if (DataContext is FontPickerViewModel { CanAccept: true } vm)
        {
            Close(vm.Result());
        }
    }
}
