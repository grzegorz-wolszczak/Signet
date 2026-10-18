using System;
using System.Runtime.InteropServices;
using Avalonia.Input;

// DllImport rather than LibraryImport: the latter needs AllowUnsafeBlocks for the whole project, for one call.
#pragma warning disable SYSLIB1054

namespace Signet.App.Input;

/// <summary>
/// "Enable AltGr on international keyboards" (Preferences → Keyboard Shortcuts). On Windows the AltGr key is reported as
/// Ctrl+Alt, so a shortcut made of Ctrl+Alt+&lt;letter&gt; takes the key before the editor can type the character that
/// AltGr+&lt;letter&gt; gives on the keyboard layout (e.g. ł, ą, ę on the Polish one). With the option on, a Ctrl+Alt stroke that
/// is typed with the AltGr key (the right Alt) is not a shortcut: it is left to the focused control, and the keyboard shortcut
/// editor does not record it. Ctrl+Alt made with the left Alt stays a shortcut, so Ctrl+Alt+S can open Preferences while
/// AltGr+S types ś. Off by default, as in Sigil, where the option is a restart-time switch of the Qt platform plugin.
/// </summary>
/// <remarks>
/// Windows treats Ctrl+Alt as AltGr when the layout has one, so the two cannot be told apart by the modifiers, nor by the
/// character the key produced (both type ś). What tells them apart is which Alt is down: the right one is AltGr
/// (<see cref="DetectAltGr"/>). Where that cannot be asked, the character the key produced decides
/// (<see cref="KeyEventArgs.KeySymbol"/>). An Avalonia <c>KeyBinding</c> is tried
/// before any handler of the key event and marks the key as handled, which would swallow the character; so with the option
/// on, <see cref="Infrastructure.ShortcutKeyBindings"/> does not bind such shortcuts as <c>KeyBinding</c>s but runs them from
/// its own key handler, after checking <see cref="IsTypedCharacter"/>.
/// </remarks>
public static class AltGrKeys
{
    private static bool _enabled;

    /// <summary>Whether the option is on (set at the start and when it is applied in Preferences).</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            EnabledChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Raised after <see cref="Enabled"/> changed (the shortcut bindings are rebuilt).</summary>
    public static event EventHandler? EnabledChanged;

    /// <summary>
    /// Tells whether the stroke is made with AltGr: <c>true</c> / <c>false</c> when known, <c>null</c> when it cannot be
    /// told (then the character the key produced decides). The default asks Windows whether the right Alt is down; tests
    /// replace it.
    /// </summary>
    public static Func<KeyEventArgs, bool?> DetectAltGr { get; set; } = WindowsRightAltDown;

    /// <summary>
    /// Whether <paramref name="e"/> is a character typed with AltGr: the option is on, Ctrl and Alt are both down, and the
    /// Alt is the right one (where that is not known: the key produced a printable character).
    /// </summary>
    public static bool IsTypedCharacter(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        const KeyModifiers altGr = KeyModifiers.Control | KeyModifiers.Alt;
        return Enabled
               && (e.KeyModifiers & altGr) == altGr
               && (DetectAltGr(e) ?? (!string.IsNullOrEmpty(e.KeySymbol) && !char.IsControl(e.KeySymbol[0])));
    }

    /// <summary>
    /// Whether a shortcut with this stroke could clash with AltGr: it has Ctrl and Alt and its key can type a character
    /// (a letter, a digit or a punctuation key). Function keys, arrows and the like never do.
    /// </summary>
    public static bool CanBeTypedCharacter(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        const KeyModifiers altGr = KeyModifiers.Control | KeyModifiers.Alt;
        Key key = gesture.Key;
        return (gesture.KeyModifiers & altGr) == altGr
               && (key is >= Key.A and <= Key.Z
                   || key is >= Key.D0 and <= Key.D9
                   || key.ToString().StartsWith("Oem", StringComparison.Ordinal));
    }

    private const int VkRightMenu = 0xA5;

    // AltGr is the right Alt key (Windows sends it as a Ctrl that is not pressed plus the right Alt); the left Alt with the
    // left Ctrl is the plain Ctrl+Alt of a shortcut. The state is the one of the key event being handled.
    private static bool? WindowsRightAltDown(KeyEventArgs e) =>
        OperatingSystem.IsWindows() ? (GetKeyState(VkRightMenu) & 0x8000) != 0 : null;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
