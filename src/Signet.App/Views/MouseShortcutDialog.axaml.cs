using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.Input;
using Signet.App.Resources;

namespace Signet.App.Views;

/// <summary>
/// Asks for a mouse shortcut of an action (IntelliJ's "Add Mouse Shortcut"): a click (single or double, any button)
/// or a wheel turn made in the pad with the modifier keys held. A plain left or right click cannot be a shortcut;
/// the actions the shortcut would conflict with are listed.
/// </summary>
public partial class MouseShortcutDialog : Window
{
    private Func<Shortcut, IReadOnlyList<string>> _conflicts = _ => Array.Empty<string>();

    /// <summary>Initializes the window.</summary>
    public MouseShortcutDialog()
    {
        InitializeComponent();
        Pad.Hint = Strings.Get("KeymapMouseDialog_Hint");
        Pad.ShortcutChanged += (_, _) => Refresh();
        OkButton.Click += (_, _) => Close(Pad.Shortcut);
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the dialog; the shortcut made, or <c>null</c> when cancelled.</summary>
    public static Task<Shortcut?> AskAsync(Window owner, string actionName, Func<Shortcut, IReadOnlyList<string>> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        MouseShortcutDialog dialog = new() { _conflicts = conflicts };
        dialog.ActionName.Text = actionName;
        return dialog.ShowDialog<Shortcut?>(owner);
    }

    private void Refresh()
    {
        MouseShortcut? shortcut = Pad.Shortcut;
        bool reserved = shortcut?.IsReserved == true;
        ReservedText.IsVisible = reserved;
        OkButton.IsEnabled = shortcut is not null && !reserved;
        IReadOnlyList<string> conflicts = shortcut is null || reserved ? Array.Empty<string>() : _conflicts(shortcut);
        Conflicts.ItemsSource = conflicts;
        ConflictsPanel.IsVisible = conflicts.Count > 0;
    }
}
