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

        // Shortcut capture in the tunneling phase: the key combination reaches the shortcut editor before
        // a button (Enter/Space = click) or the window (Enter = the default "Close" button) sees it.
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
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

    private void OnShortcutRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ShortcutRow row })
        {
            row.ToggleEditorCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCaptureClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ShortcutRow row } button)
        {
            row.BeginCapture();
            button.Focus();
        }
    }

    private void OnCaptureLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ShortcutRow row })
        {
            row.CancelCapture();
        }
    }

    private static void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Control { DataContext: ShortcutRow { IsCapturing: true } row })
        {
            return;
        }

        switch (e.Key)
        {
            // Modifiers only — wait for the actual key.
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None:
                e.Handled = true;
                return;

            // Esc without modifiers aborts the capture without changing the shortcut.
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                row.CancelCapture();
                e.Handled = true;
                return;

            default:
                row.CompleteCapture(new KeyGesture(e.Key, e.KeyModifiers));
                e.Handled = true;
                return;
        }
    }
}
