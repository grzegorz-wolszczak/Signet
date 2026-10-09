using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Avalonia.Headless;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>
/// The main window shortcuts must keep up with changes in Preferences → Keyboard Shortcuts without a restart:
/// the window rebuilds its <c>KeyBinding</c>s when <see cref="AppAction.Shortcuts"/> change. Two-stroke shortcuts wait
/// for their second stroke; mouse shortcuts run through <see cref="MouseShortcutRouter"/>.
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

        action.Shortcuts = new[] { new KeyStrokeShortcut(newGesture) };

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

        action.Shortcuts = new[] { new KeyStrokeShortcut(newGesture) };

        BindingFor(window, action)!.Gesture.Should().Be(newGesture);
        window.Close();
    }

    [AvaloniaFact]
    public void Cleared_shortcut_removes_the_window_key_binding()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        AppAction action = vm.ShortcutActions.First(a => a.Gesture is not null);

        action.Shortcuts = System.Array.Empty<Shortcut>();

        BindingFor(window, action).Should().BeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void Every_keyboard_shortcut_of_an_action_gets_a_binding()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        AppAction action = vm.ShortcutActions.First(a => a.Gesture is null);
        KeyGesture first = new(Key.F7, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        KeyGesture second = new(Key.F8, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);

        action.Shortcuts = new Shortcut[] { new KeyStrokeShortcut(first), new KeyStrokeShortcut(second) };

        window.KeyBindings.Where(b => ReferenceEquals(b.Command, action)).Select(b => b.Gesture).Should().Equal(first, second);
        window.Close();
    }

    [AvaloniaFact]
    public void A_two_stroke_shortcut_runs_its_action_after_the_second_stroke()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        AppAction action = vm.ShortcutActions.First(a => a.IsEnabled && a.Gesture is null);
        int invoked = 0;
        action.Invoked += (_, _) => invoked++;
        action.Shortcuts = new[]
        {
            new KeyStrokeShortcut(new KeyGesture(Key.F5, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift), new KeyGesture(Key.F6, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)),
        };

        window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        invoked.Should().Be(0, "the first stroke only waits for the second one");
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        invoked.Should().Be(1);

        window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        invoked.Should().Be(1, "another second stroke cancels the shortcut");
        window.Close();
    }

    [AvaloniaFact]
    public void Mouse_shortcuts_run_their_action_only_in_the_chosen_area()
    {
        Border area = new() { Width = 200, Height = 100, Background = Avalonia.Media.Brushes.White };
        Window window = new() { Width = 200, Height = 100, Content = area };
        MainWindowViewModel vm = new();
        AppAction action = vm.ShortcutActions.First(a => a.IsEnabled && a.Gesture is null);
        int invoked = 0;
        action.Invoked += (_, _) => invoked++;
        action.Shortcuts = new Shortcut[]
        {
            new MouseShortcut(MouseShortcutButton.Left, KeyModifiers.Control),
            new MouseShortcut(MouseShortcutButton.Back, KeyModifiers.None),
        };
        bool inArea = true;
        _ = new MouseShortcutRouter(window, () => new[] { action }, _ => inArea);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Point center = new(100, 50);

        window.MouseDown(center, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.Control);
        window.MouseDown(center, MouseButton.XButton1, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.XButton1, RawInputModifiers.None);
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        invoked.Should().Be(2, "a plain click is not one of the shortcuts");

        inArea = false;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.Control);
        invoked.Should().Be(2);
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
