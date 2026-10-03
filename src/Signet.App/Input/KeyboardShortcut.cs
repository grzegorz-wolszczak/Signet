using Avalonia.Input;

namespace Signet.App.Input;

/// <summary>
/// Registration of a single keyboard shortcut.
/// Holds the current and default key sequence and a description. <see langword="null"/> = no shortcut.
/// </summary>
public sealed class KeyboardShortcut
{
    internal KeyboardShortcut(string id, string description, KeyGesture? current, KeyGesture? @default)
    {
        Id = id;
        Description = description;
        KeyGesture = current;
        DefaultKeyGesture = @default;
    }

    /// <summary>Action identifier (see <see cref="Signet.App.Actions.AppActionIds"/>).</summary>
    public string Id { get; }

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
