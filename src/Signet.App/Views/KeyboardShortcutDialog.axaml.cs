using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Input;

namespace Signet.App.Views;

/// <summary>
/// Asks for a keyboard shortcut of an action (IntelliJ's "Add Keyboard Shortcut"): the first stroke and, with
/// "Second stroke", the second one; the actions the shortcut would conflict with are listed as it is typed.
/// </summary>
public partial class KeyboardShortcutDialog : Window
{
    private Func<Shortcut, IReadOnlyList<string>> _conflicts = _ => Array.Empty<string>();

    /// <summary>Initializes the window.</summary>
    public KeyboardShortcutDialog()
    {
        InitializeComponent();
        FirstStroke.GestureChanged += (_, _) => Refresh();
        SecondStroke.GestureChanged += (_, _) => Refresh();
        SecondEnabled.IsCheckedChanged += (_, _) => Refresh();
        OkButton.Click += (_, _) => Close(CurrentShortcut());
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => FirstStroke.Focus();
    }

    /// <summary>Shows the dialog; the shortcut entered, or <c>null</c> when cancelled.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="actionName">The action the shortcut is for.</param>
    /// <param name="conflicts">The actions a shortcut would conflict with.</param>
    public static Task<Shortcut?> AskAsync(Window owner, string actionName, Func<Shortcut, IReadOnlyList<string>> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        KeyboardShortcutDialog dialog = new() { _conflicts = conflicts };
        dialog.ActionName.Text = actionName;
        return dialog.ShowDialog<Shortcut?>(owner);
    }

    private KeyStrokeShortcut? CurrentShortcut()
    {
        if (FirstStroke.Gesture is not { } first)
        {
            return null;
        }

        return SecondEnabled.IsChecked == true
            ? SecondStroke.Gesture is { } second ? new KeyStrokeShortcut(first, second) : null
            : new KeyStrokeShortcut(first);
    }

    private void Refresh()
    {
        KeyStrokeShortcut? shortcut = CurrentShortcut();
        OkButton.IsEnabled = shortcut is not null;
        IReadOnlyList<string> conflicts = shortcut is null ? Array.Empty<string>() : _conflicts(shortcut);
        Conflicts.ItemsSource = conflicts;
        ConflictsPanel.IsVisible = conflicts.Count > 0;
    }
}
