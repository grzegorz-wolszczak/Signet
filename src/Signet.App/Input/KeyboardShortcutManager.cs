using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Signet.Core.Misc;

namespace Signet.App.Input;

/// <summary>
/// The shortcuts of all actions (one keymap, as in IntelliJ): every action may have several keyboard shortcuts (one or
/// two strokes) and mouse shortcuts. Defaults come from the action catalog; the actions whose shortcuts differ from
/// the defaults are saved in the settings. Conflicting assignments are allowed — the keymap editor warns about them
/// and lets the user take the shortcut away from the other actions.
/// </summary>
public sealed class KeyboardShortcutManager
{
    /// <summary>Name of the settings group in which overrides are stored.</summary>
    public const string SettingsGroup = "keyboard_shortcuts";

    private readonly SettingsStore _settings;
    private readonly Dictionary<string, KeyboardShortcut> _shortcuts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _overrides;

    /// <summary>Creates the manager and loads the saved overrides.</summary>
    public KeyboardShortcutManager(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _overrides = new Dictionary<string, string>(_settings.GetStringMap(SettingsGroup), StringComparer.Ordinal);
    }

    /// <summary>Raised after every change of an action's shortcuts (the argument is the action id).</summary>
    public event EventHandler<string>? ShortcutChanged;

    /// <summary>All registered actions with their shortcuts.</summary>
    public IReadOnlyCollection<KeyboardShortcut> AllShortcuts => _shortcuts.Values;

    /// <summary>
    /// Registers an action with its default shortcuts (the settings syntax of <see cref="ShortcutConversion"/>).
    /// Registering the same id again is ignored. A saved override replaces the defaults.
    /// </summary>
    public KeyboardShortcut RegisterAction(string id, string defaultShortcut, string description, string? scope = null)
    {
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? existing))
        {
            return existing;
        }

        IReadOnlyList<Shortcut> defaults = ShortcutConversion.ParseList(defaultShortcut);
        IReadOnlyList<Shortcut> current = _overrides.TryGetValue(id, out string? stored)
            ? ShortcutConversion.ParseList(stored)
            : defaults;

        KeyboardShortcut shortcut = new(id, description, current, defaults, scope);
        _shortcuts[id] = shortcut;
        return shortcut;
    }

    /// <summary>Returns the shortcuts of the action or <see langword="null"/>.</summary>
    public KeyboardShortcut? Get(string id) => _shortcuts.GetValueOrDefault(id);

    /// <summary>
    /// The actions (other than <paramref name="actionId"/>, in an overlapping scope) whose shortcuts conflict with
    /// <paramref name="shortcut"/> (<see cref="Conflict"/>).
    /// </summary>
    public IReadOnlyList<string> FindConflicts(string actionId, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        string? scope = _shortcuts.TryGetValue(actionId, out KeyboardShortcut? own) ? own.Scope : null;
        return _shortcuts.Values
            .Where(s => !string.Equals(s.Id, actionId, StringComparison.Ordinal)
                && ScopesOverlap(scope, s.Scope)
                && s.Shortcuts.Any(other => Conflict(shortcut, other)))
            .Select(s => s.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The actions having a shortcut that matches <paramref name="filter"/> — the keymap's "Find Shortcut": a keyboard
    /// filter matches shortcuts with the same first stroke (and the same second one when the filter has it); a mouse
    /// filter matches the equal mouse shortcut.
    /// </summary>
    public IReadOnlyList<string> FindActions(Shortcut filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return _shortcuts.Values.Where(s => s.Shortcuts.Any(own => Matches(filter, own))).Select(s => s.Id).ToList();
    }

    /// <summary>Adds a shortcut to the action (nothing when it already has it). Conflicts are not checked.</summary>
    public void AddShortcut(string id, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? entry) && !entry.Shortcuts.Contains(shortcut))
        {
            Set(entry, entry.Shortcuts.Append(shortcut).ToList());
        }
    }

    /// <summary>Removes a shortcut from the action.</summary>
    public void RemoveShortcut(string id, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? entry) && entry.Shortcuts.Contains(shortcut))
        {
            Set(entry, entry.Shortcuts.Where(s => !s.Equals(shortcut)).ToList());
        }
    }

    /// <summary>Removes all shortcuts of the action.</summary>
    public void RemoveAllShortcuts(string id)
    {
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? entry) && entry.Shortcuts.Count > 0)
        {
            Set(entry, Array.Empty<Shortcut>());
        }
    }

    /// <summary>
    /// Takes <paramref name="shortcut"/> away from the actions it conflicts with (<see cref="FindConflicts"/>) — the
    /// "Remove" answer of the conflict question.
    /// </summary>
    public void RemoveConflicts(string actionId, Shortcut shortcut)
    {
        foreach (string other in FindConflicts(actionId, shortcut))
        {
            KeyboardShortcut entry = _shortcuts[other];
            Set(entry, entry.Shortcuts.Where(s => !Conflict(shortcut, s)).ToList());
        }
    }

    /// <summary>Restores the default shortcuts of an action.</summary>
    public void ResetToDefault(string id)
    {
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? entry))
        {
            Set(entry, entry.DefaultShortcuts);
        }
    }

    /// <summary>Restores all shortcuts to their defaults and clears the saved overrides.</summary>
    public void ResetAll()
    {
        foreach (KeyboardShortcut shortcut in _shortcuts.Values)
        {
            shortcut.Shortcuts = shortcut.DefaultShortcuts;
        }

        _overrides.Clear();
        Persist();

        foreach (string id in _shortcuts.Keys.ToList())
        {
            ShortcutChanged?.Invoke(this, id);
        }
    }

    /// <summary>
    /// Whether two shortcuts conflict (as IntelliJ's <c>Keymap.getConflicts</c>): equal mouse shortcuts, or keyboard
    /// shortcuts with the same first stroke where one has no second stroke or both have the same one.
    /// </summary>
    public static bool Conflict(Shortcut first, Shortcut second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return (first, second) switch
        {
            (KeyStrokeShortcut a, KeyStrokeShortcut b) => SameStroke(a.First, b.First)
                && (a.Second is null || b.Second is null || SameStroke(a.Second, b.Second)),
            (MouseShortcut a, MouseShortcut b) => a.Equals(b),
            _ => false,
        };
    }

    private static bool Matches(Shortcut filter, Shortcut shortcut) => (filter, shortcut) switch
    {
        (KeyStrokeShortcut f, KeyStrokeShortcut s) =>
            SameStroke(f.First, s.First) && (f.Second is null || (s.Second is not null && SameStroke(f.Second, s.Second))),
        (MouseShortcut f, MouseShortcut s) => f.Equals(s),
        _ => false,
    };

    private static bool SameStroke(KeyGesture a, KeyGesture b) => a.Key == b.Key && a.KeyModifiers == b.KeyModifiers;

    // A window-wide shortcut (no scope) overlaps with everything; two panel shortcuts only within the same panel.
    private static bool ScopesOverlap(string? first, string? second) =>
        first is null || second is null || string.Equals(first, second, StringComparison.Ordinal);

    private void Set(KeyboardShortcut entry, IReadOnlyList<Shortcut> shortcuts)
    {
        entry.Shortcuts = shortcuts;
        if (entry.IsOverridden)
        {
            _overrides[entry.Id] = ShortcutConversion.ToListString(shortcuts);
        }
        else
        {
            _overrides.Remove(entry.Id);
        }

        Persist();
        ShortcutChanged?.Invoke(this, entry.Id);
    }

    private void Persist()
    {
        _settings.SetStringMap(SettingsGroup, _overrides);
        _settings.Save();
    }
}
