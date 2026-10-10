namespace Signet.App.Input;

/// <summary>How the same shortcut of two actions behaves (<see cref="KeyboardShortcutManager.Overlap"/>).</summary>
public enum ShortcutOverlap
{
    /// <summary>The two never get the same key press (they work in different parts of the window).</summary>
    None,

    /// <summary>One of them takes the shortcut over in a part of the window (a panel); the other works elsewhere.</summary>
    Shadows,

    /// <summary>Both are meant to work in the same place: a real conflict.</summary>
    Conflict,
}
