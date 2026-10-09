using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace Signet.App.Input;

/// <summary>
/// The shortcuts of one action: the current ones (keyboard and mouse, possibly several — as in IntelliJ's keymap) and
/// the default ones, plus a description.
/// </summary>
public sealed class KeyboardShortcut
{
    internal KeyboardShortcut(string id, string description, IReadOnlyList<Shortcut> current, IReadOnlyList<Shortcut> defaults, string? scope = null)
    {
        Id = id;
        Scope = scope;
        Description = description;
        Shortcuts = current;
        DefaultShortcuts = defaults;
    }

    /// <summary>Action identifier (see <see cref="Signet.App.Actions.AppActionIds"/>).</summary>
    public string Id { get; }

    /// <summary>
    /// Name of the panel the shortcut works in (it is handled only while the focus is in that panel), or
    /// <see langword="null"/> for a window-wide shortcut. Shortcuts of two different panels never collide.
    /// </summary>
    public string? Scope { get; }

    /// <summary>Human-readable action description.</summary>
    public string Description { get; internal set; }

    /// <summary>The current shortcuts (may be changed by the user), in the order they were added.</summary>
    public IReadOnlyList<Shortcut> Shortcuts { get; internal set; }

    /// <summary>The default (factory) shortcuts.</summary>
    public IReadOnlyList<Shortcut> DefaultShortcuts { get; }

    /// <summary>The first one-stroke keyboard shortcut (what a menu item shows), or <see langword="null"/>.</summary>
    public KeyGesture? KeyGesture =>
        Shortcuts.OfType<KeyStrokeShortcut>().FirstOrDefault(s => s.Second is null)?.First;

    /// <summary>Whether the current shortcuts differ from the defaults.</summary>
    public bool IsOverridden => !Shortcuts.SequenceEqual(DefaultShortcuts);
}
