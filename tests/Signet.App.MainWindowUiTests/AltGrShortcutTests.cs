using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.ViewModels;
using Signet.App.Views;
using Xunit;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// A Ctrl+Alt shortcut against a character typed with AltGr (Windows reports AltGr as Ctrl+Alt): with "Enable AltGr" on,
/// a stroke that types a character is not a shortcut and stays unhandled for the focused control.
/// </summary>
[Collection("AltGrKeys")]
public sealed class AltGrShortcutTests
{
    private static (MainWindow Window, AppAction Action, int[] Runs) Setup()
    {
        MainWindowViewModel vm = new();
        MainWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The shortcut a user may assign: Ctrl+Alt+L, which is also AltGr+L (ł) on the Polish keyboard.
        AppAction action = vm.Actions.Require(AppActionIds.ToggleNotifications);
        action.Shortcuts = new[] { new KeyStrokeShortcut(new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Alt)) };
        action.CanExecute(null).Should().BeTrue("the premise: the action is available without a book");
        int[] runs = new int[1];
        action.Invoked += (_, _) => runs[0]++;
        return (window, action, runs);
    }

    // The headless platform has no physical keys, so a stroke counts as made with AltGr when it typed a character.
    static AltGrShortcutTests() => AltGrKeys.DetectAltGr = e => !string.IsNullOrEmpty(e.KeySymbol);

    private static void PressCtrlAltL(MainWindow window, string? symbol)
    {
        window.KeyPress(Key.L, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.L, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void With_the_option_off_the_shortcut_wins_over_the_character()
    {
        // Arrange
        AltGrKeys.Enabled = false;
        (MainWindow window, _, int[] runs) = Setup();

        // Act
        PressCtrlAltL(window, "ł");

        // Assert
        runs[0].Should().Be(1);
        window.Close();
    }

    [AvaloniaFact]
    public void With_the_option_on_a_stroke_that_types_a_character_is_not_a_shortcut()
    {
        // Arrange
        AltGrKeys.Enabled = true;
        try
        {
            (MainWindow window, _, int[] runs) = Setup();

            // Act
            PressCtrlAltL(window, "ł");

            // Assert
            runs[0].Should().Be(0);
            window.Close();
        }
        finally
        {
            AltGrKeys.Enabled = false;
        }
    }

    [AvaloniaFact]
    public void With_the_option_on_Ctrl_Alt_that_types_nothing_is_still_a_shortcut_and_so_is_the_next_one()
    {
        // Arrange
        AltGrKeys.Enabled = true;
        try
        {
            (MainWindow window, _, int[] runs) = Setup();

            // Act
            PressCtrlAltL(window, null);
            PressCtrlAltL(window, "ł");
            PressCtrlAltL(window, null);

            // Assert
            runs[0].Should().Be(2, "only the stroke that typed a character was left to the focused control");
            window.Close();
        }
        finally
        {
            AltGrKeys.Enabled = false;
        }
    }

    [AvaloniaFact]
    public void Switching_the_option_changes_how_an_open_window_treats_the_stroke()
    {
        // Arrange
        AltGrKeys.Enabled = false;
        try
        {
            (MainWindow window, _, int[] runs) = Setup();

            // Act
            PressCtrlAltL(window, "ł");

            // Assert
            runs[0].Should().Be(1, "off: the shortcut wins");

            // Act
            AltGrKeys.Enabled = true;
            PressCtrlAltL(window, "ł");

            // Assert
            runs[0].Should().Be(1, "on: the character is left to the focused control");

            // Act
            AltGrKeys.Enabled = false;
            PressCtrlAltL(window, "ł");

            // Assert
            runs[0].Should().Be(2, "off again: the shortcut wins again");
            window.Close();
        }
        finally
        {
            AltGrKeys.Enabled = false;
        }
    }
}
