using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Signet.Core.Misc;

namespace Signet.App.Input;

/// <summary>
/// Registry of the application's keyboard shortcuts, provided as a DI service. Holds the current and
/// default sequences, detects conflicts and persists overrides in <see cref="SettingsStore"/>
/// (group <c>keyboard_shortcuts</c>).
/// </summary>
/// <remarks>
/// Only entries that differ from the defaults are persisted (an empty string = the shortcut was
/// deliberately removed). The shortcut editor in Preferences builds on this mechanism.
/// </remarks>
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

    /// <summary>Raised after every sequence change (the argument is the action id).</summary>
    public event EventHandler<string>? ShortcutChanged;

    /// <summary>All registered shortcuts.</summary>
    public IReadOnlyCollection<KeyboardShortcut> AllShortcuts => _shortcuts.Values;

    /// <summary>
    /// Registers an action with its default sequence. Registering the same id again is
    /// ignored. If a saved override exists, it is applied.
    /// </summary>
    public KeyboardShortcut RegisterAction(string id, string defaultShortcut, string description, string? scope = null)
    {
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? existing))
        {
            return existing;
        }

        _ = KeyGestureConversion.TryParse(defaultShortcut, out KeyGesture? defaultGesture);
        KeyGesture? current = defaultGesture;

        if (_overrides.TryGetValue(id, out string? stored))
        {
            current = stored.Length == 0 ? null
                : KeyGestureConversion.TryParse(stored, out KeyGesture? g) ? g : defaultGesture;
        }

        KeyboardShortcut shortcut = new(id, description, current, defaultGesture, scope);
        _shortcuts[id] = shortcut;
        return shortcut;
    }

    /// <summary>Returns the shortcut for the id or <see langword="null"/>.</summary>
    public KeyboardShortcut? Get(string id) => _shortcuts.GetValueOrDefault(id);

    /// <summary>
    /// Whether the sequence is already used by another action (other than <paramref name="exceptId"/>).
    /// </summary>
    public bool IsKeyGestureInUse(KeyGesture gesture, string exceptId = "") =>
        FindKeyGestureOwner(gesture, exceptId) is not null;

    /// <summary>
    /// Id of the action that already has the sequence <paramref name="gesture"/> assigned (other than
    /// <paramref name="exceptId"/>), or <see langword="null"/> when the sequence is free.
    /// </summary>
    public string? FindKeyGestureOwner(KeyGesture gesture, string exceptId = "")
    {
        ArgumentNullException.ThrowIfNull(gesture);
        string key = gesture.ToString();
        string? scope = _shortcuts.TryGetValue(exceptId, out KeyboardShortcut? except) ? except.Scope : null;
        foreach (KeyValuePair<string, KeyboardShortcut> kv in _shortcuts)
        {
            if (!string.Equals(kv.Key, exceptId, StringComparison.Ordinal)
                && ScopesOverlap(scope, kv.Value.Scope)
                && kv.Value.KeyGesture is { } cur
                && string.Equals(cur.ToString(), key, StringComparison.Ordinal))
            {
                return kv.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets the sequence for an action. Returns <see langword="false"/> (no change) if the
    /// sequence is already taken by another action. <paramref name="gesture"/> =
    /// <see langword="null"/> removes the shortcut and always succeeds.
    /// </summary>
    public bool SetKeyGesture(string id, KeyGesture? gesture)
    {
        if (!_shortcuts.TryGetValue(id, out KeyboardShortcut? shortcut))
        {
            return false;
        }

        if (gesture is not null && IsKeyGestureInUse(gesture, id))
        {
            return false;
        }

        shortcut.KeyGesture = gesture;
        SyncOverride(shortcut);
        ShortcutChanged?.Invoke(this, id);
        return true;
    }

    /// <summary>Restores the default sequence for an action.</summary>
    public void ResetToDefault(string id)
    {
        if (_shortcuts.TryGetValue(id, out KeyboardShortcut? shortcut))
        {
            shortcut.KeyGesture = shortcut.DefaultKeyGesture;
            SyncOverride(shortcut);
            ShortcutChanged?.Invoke(this, id);
        }
    }

    /// <summary>Restores all shortcuts to their defaults and clears the saved overrides.</summary>
    public void ResetAll()
    {
        foreach (KeyboardShortcut shortcut in _shortcuts.Values)
        {
            shortcut.KeyGesture = shortcut.DefaultKeyGesture;
        }

        _overrides.Clear();
        Persist();

        foreach (string id in _shortcuts.Keys.ToList())
        {
            ShortcutChanged?.Invoke(this, id);
        }
    }

    /// <summary>Pairs of actions sharing the same sequence (diagnostics; in practice it should be empty).</summary>
    public IReadOnlyList<(string First, string Second, string Gesture)> FindConflicts()
    {
        List<(string, string, string)> conflicts = new();
        List<KeyboardShortcut> withGesture = _shortcuts.Values
            .Where(s => s.KeyGesture is not null)
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < withGesture.Count; i++)
        {
            for (int j = i + 1; j < withGesture.Count; j++)
            {
                if (ScopesOverlap(withGesture[i].Scope, withGesture[j].Scope)
                    && string.Equals(withGesture[i].KeyGesture!.ToString(), withGesture[j].KeyGesture!.ToString(), StringComparison.Ordinal))
                {
                    // The shortcut is shown in the portable syntax ("Ctrl+0"), as in the shortcut edit fields —
                    // Avalonia's KeyGesture.ToString() would give the raw key name ("Ctrl+D0").
                    conflicts.Add((
                        withGesture[i].Id,
                        withGesture[j].Id,
                        KeyGestureConversion.ToPortableString(withGesture[i].KeyGesture)));
                }
            }
        }

        return conflicts;
    }

    // A window-wide shortcut (no scope) overlaps with everything; two panel shortcuts only within the same panel.
    private static bool ScopesOverlap(string? first, string? second) =>
        first is null || second is null || string.Equals(first, second, StringComparison.Ordinal);

    private void SyncOverride(KeyboardShortcut shortcut)
    {
        if (shortcut.IsOverridden)
        {
            _overrides[shortcut.Id] = KeyGestureConversion.ToPortableString(shortcut.KeyGesture);
        }
        else
        {
            _overrides.Remove(shortcut.Id);
        }

        Persist();
    }

    private void Persist()
    {
        _settings.SetStringMap(SettingsGroup, _overrides);
        _settings.Save();
    }
}
