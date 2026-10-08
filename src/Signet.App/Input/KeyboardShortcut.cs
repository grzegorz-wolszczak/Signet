using Avalonia.Input;

namespace Signet.App.Input;

/// <summary>
/// Registration of a single keyboard shortcut.
/// Holds the current and default key sequence and a description. <see langword="null"/> = no shortcut.
/// </summary>
public sealed class KeyboardShortcut
{
    internal KeyboardShortcut(string id, string description, KeyGesture? current, KeyGesture? @default, string? scope = null)
    {
        Id = id;
        Scope = scope;
        Description = description;
        KeyGesture = current;
        DefaultKeyGesture = @default;
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

    /// <summary>Current key sequence (may be overridden by the user).</summary>
    public KeyGesture? KeyGesture { get; internal set; }

    /// <summary>Default (factory) key sequence.</summary>
    public KeyGesture? DefaultKeyGesture { get; }

    /// <summary>Whether the current sequence differs from the default.</summary>
    public bool IsOverridden =>
        !Equals(KeyGesture?.ToString(), DefaultKeyGesture?.ToString());
}
