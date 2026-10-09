using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Input;
using Signet.App.Resources;

namespace Signet.App.Input;

/// <summary>The mouse "button" of a <see cref="MouseShortcut"/> (the wheel counts as two buttons, like in IntelliJ).</summary>
public enum MouseShortcutButton
{
    /// <summary>The left button.</summary>
    Left,

    /// <summary>The middle button (wheel click).</summary>
    Middle,

    /// <summary>The right button.</summary>
    Right,

    /// <summary>The side "Back" button (XButton1, IntelliJ's button 4).</summary>
    Back,

    /// <summary>The side "Forward" button (XButton2, IntelliJ's button 5).</summary>
    Forward,

    /// <summary>The wheel turned up.</summary>
    WheelUp,

    /// <summary>The wheel turned down.</summary>
    WheelDown,
}

/// <summary>
/// A shortcut of an action (modelled on IntelliJ's Shortcut): a keyboard shortcut of one or two strokes
/// (<see cref="KeyStrokeShortcut"/>) or a mouse shortcut (<see cref="MouseShortcut"/>). Two shortcuts are equal when
/// their <see cref="PortableText"/> is.
/// </summary>
public abstract class Shortcut : IEquatable<Shortcut>
{
    /// <summary>The text kept in the settings (see <see cref="ShortcutConversion"/>).</summary>
    public abstract string PortableText { get; }

    /// <summary>The text shown to the user (menus, the keymap, conflicts).</summary>
    public abstract string DisplayText { get; }

    /// <inheritdoc />
    public bool Equals(Shortcut? other) => other is not null && string.Equals(PortableText, other.PortableText, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Shortcut);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(PortableText);

    /// <inheritdoc />
    public override string ToString() => DisplayText;

    // "Ctrl+Alt+Shift+Meta+" — the modifier order of KeyGestureConversion.
    internal static string ModifierPrefix(KeyModifiers modifiers)
    {
        StringBuilder sb = new();
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append("Ctrl+");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            sb.Append("Meta+");
        }

        return sb.ToString();
    }
}

/// <summary>A keyboard shortcut: one stroke (<c>Ctrl+S</c>) or two (<c>Ctrl+K, Ctrl+C</c> — IntelliJ's "second stroke").</summary>
public sealed class KeyStrokeShortcut : Shortcut
{
    /// <summary>Creates the shortcut.</summary>
    /// <param name="first">The first (or only) stroke.</param>
    /// <param name="second">The second stroke, or <c>null</c> for a one-stroke shortcut.</param>
    public KeyStrokeShortcut(KeyGesture first, KeyGesture? second = null)
    {
        First = first ?? throw new ArgumentNullException(nameof(first));
        Second = second;
    }

    /// <summary>The first (or only) stroke.</summary>
    public KeyGesture First { get; }

    /// <summary>The second stroke, or <c>null</c>.</summary>
    public KeyGesture? Second { get; }

    /// <inheritdoc />
    public override string PortableText => Second is null
        ? KeyGestureConversion.ToPortableString(First)
        : $"{KeyGestureConversion.ToPortableString(First)}{ShortcutConversion.StrokeSeparator}{KeyGestureConversion.ToPortableString(Second)}";

    /// <inheritdoc />
    public override string DisplayText => PortableText;
}

/// <summary>
/// A mouse shortcut: a button with modifiers, a single or double click (IntelliJ's MouseShortcut), e.g.
/// <c>Ctrl+Click</c>, <c>Alt+Double Click</c>, <c>Back Button</c>, <c>Ctrl+Wheel Up</c>.
/// </summary>
public sealed class MouseShortcut : Shortcut
{
    /// <summary>The prefix of a mouse shortcut in the settings.</summary>
    internal const string PortablePrefix = "Mouse:";

    /// <summary>Creates the shortcut.</summary>
    /// <param name="button">The button.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    /// <param name="clickCount">1 or 2 (a double click) — always 1 for the side buttons and the wheel.</param>
    public MouseShortcut(MouseShortcutButton button, KeyModifiers modifiers, int clickCount = 1)
    {
        Button = button;
        Modifiers = modifiers;
        ClickCount = button is MouseShortcutButton.Left or MouseShortcutButton.Middle or MouseShortcutButton.Right
            ? Math.Clamp(clickCount, 1, 2)
            : 1;
    }

    /// <summary>The button.</summary>
    public MouseShortcutButton Button { get; }

    /// <summary>The modifier keys.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>1 or 2 (a double click).</summary>
    public int ClickCount { get; }

    /// <summary>
    /// Whether the shortcut would take over a plain left or right click (placing the caret, the context menu) — such
    /// a shortcut cannot be assigned.
    /// </summary>
    public bool IsReserved =>
        Modifiers == KeyModifiers.None && ClickCount == 1 && Button is MouseShortcutButton.Left or MouseShortcutButton.Right;

    /// <inheritdoc />
    public override string PortableText =>
        $"{PortablePrefix}{ModifierPrefix(Modifiers)}{Button}{(ClickCount == 2 ? "x2" : string.Empty)}";

    /// <inheritdoc />
    public override string DisplayText => ModifierPrefix(Modifiers) + Strings.Get((Button, ClickCount) switch
    {
        (MouseShortcutButton.Left, 1) => "Shortcut_MouseClick",
        (MouseShortcutButton.Left, _) => "Shortcut_MouseDoubleClick",
        (MouseShortcutButton.Middle, 1) => "Shortcut_MouseMiddleClick",
        (MouseShortcutButton.Middle, _) => "Shortcut_MouseMiddleDoubleClick",
        (MouseShortcutButton.Right, 1) => "Shortcut_MouseRightClick",
        (MouseShortcutButton.Right, _) => "Shortcut_MouseRightDoubleClick",
        (MouseShortcutButton.Back, _) => "Shortcut_MouseBack",
        (MouseShortcutButton.Forward, _) => "Shortcut_MouseForward",
        (MouseShortcutButton.WheelUp, _) => "Shortcut_MouseWheelUp",
        _ => "Shortcut_MouseWheelDown",
    });
}

/// <summary>
/// The settings syntax of shortcuts: a keyboard shortcut in the portable key-sequence syntax of
/// <see cref="KeyGestureConversion"/> with the strokes separated by <c>", "</c> (<c>Ctrl+K, Ctrl+C</c>); a mouse
/// shortcut as <c>Mouse:</c> + modifiers + button (+ <c>x2</c> for a double click), e.g. <c>Mouse:Ctrl+Leftx2</c>;
/// a list of shortcuts separated by <c>";"</c>. A single key sequence — the format of older settings — is a list
/// of one shortcut.
/// </summary>
public static class ShortcutConversion
{
    /// <summary>The separator of the two strokes of a keyboard shortcut.</summary>
    public const string StrokeSeparator = ", ";

    private const char ListSeparator = ';';

    /// <summary>Parses one shortcut; <c>false</c> for empty or unrecognized text.</summary>
    public static bool TryParse(string? text, out Shortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith(MouseShortcut.PortablePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseMouse(trimmed[MouseShortcut.PortablePrefix.Length..], out shortcut);
        }

        // "Ctrl+," is a single stroke (the comma key); two strokes are separated by a comma and a space.
        int separator = trimmed.IndexOf(StrokeSeparator, 1, StringComparison.Ordinal);
        string firstText = separator < 0 ? trimmed : trimmed[..separator];
        string? secondText = separator < 0 ? null : trimmed[(separator + StrokeSeparator.Length)..];
        if (!TryParseStroke(firstText, out KeyGesture? first))
        {
            return false;
        }

        KeyGesture? second = null;
        if (secondText is not null && !TryParseStroke(secondText, out second))
        {
            return false;
        }

        shortcut = new KeyStrokeShortcut(first!, second);
        return true;
    }

    /// <summary>Parses a list of shortcuts (unrecognized items are skipped, duplicates removed).</summary>
    public static IReadOnlyList<Shortcut> ParseList(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<Shortcut>()
            : text.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => TryParse(item, out Shortcut? s) ? s : null)
                .OfType<Shortcut>()
                .Distinct()
                .ToList();

    /// <summary>Writes a list of shortcuts in the settings syntax (empty for no shortcuts).</summary>
    public static string ToListString(IEnumerable<Shortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        return string.Join(ListSeparator + " ", shortcuts.Select(s => s.PortableText));
    }

    // A single stroke; unlike KeyGestureConversion.TryParse it keeps a trailing comma as the key ("Ctrl+,").
    private static bool TryParseStroke(string text, out KeyGesture? gesture)
    {
        gesture = null;
        string stroke = text.Trim();
        if (stroke.EndsWith(',') && (stroke.Length == 1 || stroke[^2] == '+'))
        {
            return KeyGestureConversion.TryParse(stroke[..^1] + "OemComma", out gesture);
        }

        return !stroke.Contains(',', StringComparison.Ordinal) && KeyGestureConversion.TryParse(stroke, out gesture);
    }

    private static bool TryParseMouse(string text, out Shortcut? shortcut)
    {
        shortcut = null;
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        KeyModifiers modifiers = KeyModifiers.None;
        foreach (string modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "meta" or "cmd" or "win":
                    modifiers |= KeyModifiers.Meta;
                    break;
                default:
                    return false;
            }
        }

        string button = parts[^1];
        int clickCount = 1;
        if (button.EndsWith("x2", StringComparison.OrdinalIgnoreCase))
        {
            clickCount = 2;
            button = button[..^2];
        }

        if (!Enum.TryParse(button, ignoreCase: true, out MouseShortcutButton parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        shortcut = new MouseShortcut(parsed, modifiers, clickCount);
        return true;
    }
}
