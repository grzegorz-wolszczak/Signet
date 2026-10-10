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
/// and lets the user take the shortcut away from the other actions. Whether two shortcuts collide depends on where
/// they work (<see cref="Overlap"/>).
/// </summary>
public sealed class KeyboardShortcutManager
{
    /// <summary>Name of the settings group in which overrides are stored.</summary>
    public const string SettingsGroup = "keyboard_shortcuts";

    /// <summary>The scope of the shortcuts handled only while the focus is in a code editor (Code View).</summary>
    public const string CodeViewScope = "CodeView";

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

    /// <summary>
    /// A working copy for an editor with Save / Cancel (the keymap in Preferences): the same actions with their current
    /// shortcuts, storing its changes in <paramref name="draftSettings"/> (a draft of the settings this manager uses,
    /// see <see cref="SettingsStore.CreateDraft"/>). Once the draft is committed, <see cref="ReloadFromSettings"/>
    /// brings the changes here.
    /// </summary>
    public KeyboardShortcutManager CreateDraft(SettingsStore draftSettings)
    {
        ArgumentNullException.ThrowIfNull(draftSettings);
        KeyboardShortcutManager draft = new(draftSettings);
        foreach (KeyboardShortcut entry in _shortcuts.Values)
        {
            draft._shortcuts[entry.Id] = new KeyboardShortcut(entry.Id, entry.Description, entry.Shortcuts, entry.DefaultShortcuts, entry.Scope);
        }

        return draft;
    }

    /// <summary>
    /// Reloads the saved overrides from the settings (after a committed draft) and raises
    /// <see cref="ShortcutChanged"/> for every action whose shortcuts changed.
    /// </summary>
    public void ReloadFromSettings()
    {
        _overrides.Clear();
        foreach (KeyValuePair<string, string> pair in _settings.GetStringMap(SettingsGroup))
        {
            _overrides[pair.Key] = pair.Value;
        }

        foreach (KeyboardShortcut entry in _shortcuts.Values)
        {
            IReadOnlyList<Shortcut> current = _overrides.TryGetValue(entry.Id, out string? stored)
                ? ShortcutConversion.ParseList(stored)
                : entry.DefaultShortcuts;
            if (!current.SequenceEqual(entry.Shortcuts))
            {
                entry.Shortcuts = current;
                ShortcutChanged?.Invoke(this, entry.Id);
            }
        }
    }

    /// <summary>Returns the shortcuts of the action or <see langword="null"/>.</summary>
    public KeyboardShortcut? Get(string id) => _shortcuts.GetValueOrDefault(id);

    /// <summary>
    /// The actions (other than <paramref name="actionId"/>) whose shortcuts conflict with <paramref name="shortcut"/>
    /// (<see cref="Conflict"/>) where both work at the same time (<see cref="ShortcutOverlap.Conflict"/>).
    /// </summary>
    public IReadOnlyList<string> FindConflicts(string actionId, Shortcut shortcut) =>
        FindOverlapping(actionId, shortcut, ShortcutOverlap.Conflict);

    /// <summary>
    /// The actions (other than <paramref name="actionId"/>) with the same shortcut that take it over in a part of the
    /// window (<see cref="ShortcutOverlap.Shadows"/>) — not a conflict, but the shortcut does something else there.
    /// </summary>
    public IReadOnlyList<string> FindShadowed(string actionId, Shortcut shortcut) =>
        FindOverlapping(actionId, shortcut, ShortcutOverlap.Shadows);

    /// <summary>
    /// How the shortcuts of two actions relate when they are the same keys:
    /// <list type="bullet">
    /// <item>the same scope — <see cref="ShortcutOverlap.Conflict"/>;</item>
    /// <item>a window-wide one and a code editor one — <see cref="ShortcutOverlap.Conflict"/> (the code editor is where
    /// window-wide shortcuts are mostly used, and its own one wins there);</item>
    /// <item>a window-wide one and a panel one — <see cref="ShortcutOverlap.Shadows"/> (the panel one wins while the
    /// panel has the focus, the window-wide one works everywhere else);</item>
    /// <item>two different scopes (e.g. the code editor and the Book Browser or the clips tree) —
    /// <see cref="ShortcutOverlap.None"/>: the focus is in one of them at a time.</item>
    /// </list>
    /// </summary>
    public static ShortcutOverlap Overlap(KeyboardShortcut first, KeyboardShortcut second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return OverlapOfScopes(first.Scope, second.Scope);
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

    private static ShortcutOverlap OverlapOfScopes(string? firstScope, string? secondScope)
    {
        if (string.Equals(firstScope, secondScope, StringComparison.Ordinal))
        {
            return ShortcutOverlap.Conflict;
        }

        if (firstScope is null || secondScope is null)
        {
            return string.Equals(firstScope ?? secondScope, CodeViewScope, StringComparison.Ordinal)
                ? ShortcutOverlap.Conflict
                : ShortcutOverlap.Shadows;
        }

        return ShortcutOverlap.None;
    }

    private List<string> FindOverlapping(string actionId, Shortcut shortcut, ShortcutOverlap overlap)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        KeyboardShortcut? own = _shortcuts.GetValueOrDefault(actionId);
        return _shortcuts.Values
            .Where(s => !string.Equals(s.Id, actionId, StringComparison.Ordinal)
                && OverlapOfScopes(own?.Scope, s.Scope) == overlap
                && s.Shortcuts.Any(other => Conflict(shortcut, other)))
            .Select(s => s.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

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
