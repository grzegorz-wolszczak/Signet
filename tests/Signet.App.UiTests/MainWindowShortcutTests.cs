using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>
/// The main window shortcuts must keep up with changes in Preferences → Keyboard Shortcuts without a restart:
/// the window rebuilds its <c>KeyBinding</c>s when <see cref="AppAction.Gesture"/> changes.
/// </summary>
public sealed class MainWindowShortcutTests
{
    private static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow()
    {
        MainWindowViewModel vm = new();
        MainWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static KeyBinding? BindingFor(MainWindow window, AppAction action) =>
        window.KeyBindings.FirstOrDefault(b => ReferenceEquals(b.Command, action));

    [AvaloniaFact]
    public void Changed_shortcut_replaces_the_window_key_binding_immediately()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        AppAction action = vm.ShortcutActions.First(a => a.Gesture is not null);
        KeyGesture newGesture = new(Key.F12, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);

        action.Gesture = newGesture;

        BindingFor(window, action).Should().NotBeNull();
        BindingFor(window, action)!.Gesture.Should().Be(newGesture);
        window.Close();
    }

    [AvaloniaFact]
    public void Shortcut_assigned_to_an_action_without_default_starts_working_immediately()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        AppAction action = vm.ShortcutActions.First(a => a.Gesture is null);
        BindingFor(window, action).Should().BeNull();
        KeyGesture newGesture = new(Key.F11, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);

        action.Gesture = newGesture;

        BindingFor(window, action)!.Gesture.Should().Be(newGesture);
        window.Close();
    }

    [AvaloniaFact]
    public void Cleared_shortcut_removes_the_window_key_binding()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        AppAction action = vm.ShortcutActions.First(a => a.Gesture is not null);

        action.Gesture = null;

        BindingFor(window, action).Should().BeNull();
        window.Close();
    }

    /// <summary>The menu of the toolbar button with the action list is built once — the shortcut text must keep up.</summary>
    [AvaloniaFact]
    public void Toolbar_drop_down_menu_shows_the_changed_shortcut()
    {
        (MainWindow window, _) = ShowMainWindow();
        MenuItem item = window.GetVisualDescendants().OfType<SplitButton>()
            .Select(b => b.Flyout).OfType<MenuFlyout>()
            .SelectMany(f => f.Items.OfType<MenuItem>())
            .First();
        AppAction action = (AppAction)item.CommandParameter!;
        KeyGesture newGesture = new(Key.F10, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);

        action.Gesture = newGesture;

        item.InputGesture.Should().Be(newGesture);
        window.Close();
    }
}
