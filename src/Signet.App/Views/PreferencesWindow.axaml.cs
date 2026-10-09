using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>The modal "Preferences" window — changes are saved immediately, with no OK/Cancel.</summary>
public partial class PreferencesWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PreferencesViewModel vm)
        {
            // The "Choose…" buttons next to the fonts open a modal picker window over this window.
            vm.FontPicker = request => FontPickerWindow.AskAsync(this, request);
            vm.ColorPicker = (title, initial) => ColorPickerWindow.AskAsync(this, title, initial);
            vm.TextPrompt = (title, prompt, initial) => TextPromptWindow.AskAsync(this, title, prompt, initial);
            vm.ErrorMessage = (title, message) => MessageDialog.ShowAsync(this, title, message);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>"Open settings folder".</summary>
    private async void OnOpenPreferencesLocationClicked(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppDirectories.EnsurePrefsDirectory()));
    }

    private void OnUserWordSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm && sender is ListBox list)
        {
            vm.SetSelectedUserWords(list.SelectedItems?.OfType<string>() ?? Enumerable.Empty<string>());
        }
    }

    private void OnUserWordDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PreferencesViewModel vm && vm.EditUserWordCommand.CanExecute(null))
        {
            vm.EditUserWordCommand.Execute(null);
        }
    }
}
