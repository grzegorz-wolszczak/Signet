using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Input;

namespace Signet.App.Input;

/// <summary>
/// Converts keyboard shortcuts between the portable key-sequence syntax (e.g.
/// <c>Ctrl+Shift+S</c>, <c>Ctrl+Return</c>, <c>Ctrl+=</c>) and Avalonia's <see cref="KeyGesture"/>.
/// Handles differences in key names (PgDown↔PageDown) and punctuation keys
/// that <see cref="KeyGesture.Parse(string)"/> does not recognize.
/// </summary>
public static class KeyGestureConversion
{
    private static readonly Dictionary<string, Key> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["return"] = Key.Return,
        ["enter"] = Key.Enter,
        ["space"] = Key.Space,
        ["tab"] = Key.Tab,
        ["backspace"] = Key.Back,
        ["del"] = Key.Delete,
        ["delete"] = Key.Delete,
        ["ins"] = Key.Insert,
        ["insert"] = Key.Insert,
        ["esc"] = Key.Escape,
        ["escape"] = Key.Escape,
        ["home"] = Key.Home,
        ["end"] = Key.End,
        ["pgup"] = Key.PageUp,
        ["pageup"] = Key.PageUp,
        ["pgdown"] = Key.PageDown,
        ["pagedown"] = Key.PageDown,
        ["up"] = Key.Up,
        ["down"] = Key.Down,
        ["left"] = Key.Left,
        ["right"] = Key.Right,
        ["="] = Key.OemPlus,
        ["+"] = Key.OemPlus,
        ["-"] = Key.OemMinus,
        ["."] = Key.OemPeriod,
        [","] = Key.OemComma,
        ["/"] = Key.OemQuestion,
        ["\\"] = Key.OemBackslash,
        ["["] = Key.OemOpenBrackets,
        ["]"] = Key.OemCloseBrackets,
        [";"] = Key.OemSemicolon,
        ["'"] = Key.OemQuotes,
        ["`"] = Key.OemTilde,
    };

    /// <summary>
    /// Parses a shortcut in the portable key-sequence syntax into a <see cref="KeyGesture"/>. An empty/whitespace
    /// string or an unrecognized key → <see langword="false"/> and <paramref name="gesture"/> = <see langword="null"/>.
    /// </summary>
    public static bool TryParse(string? shortcut, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        // The syntax allows several alternatives separated by ", " — take the first one.
        string first = shortcut.Split(',')[0].Trim();
        string[] parts = first.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            // A lone "+" as the key.
            parts = new[] { "+" };
        }

        KeyModifiers modifiers = KeyModifiers.None;
        string? keyToken = null;
        foreach (string raw in parts)
        {
            string p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "alt":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "meta":
                case "cmd":
                case "super":
                case "win":
                    modifiers |= KeyModifiers.Meta;
                    break;
                default:
                    keyToken = p;
                    break;
            }
        }

        if (keyToken is null || !TryResolveKey(keyToken, out Key key))
        {
            return false;
        }

        gesture = new KeyGesture(key, modifiers);
        return true;
    }

    /// <summary>Writes a <see cref="KeyGesture"/> in the portable key-sequence syntax (for menus / persistence).</summary>
    public static string ToPortableString(KeyGesture? gesture)
    {
        if (gesture is null)
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append("Ctrl+");
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            sb.Append("Meta+");
        }

        sb.Append(KeyToToken(gesture.Key));
        return sb.ToString();
    }

    private static bool TryResolveKey(string token, out Key key)
    {
        if (KeyNames.TryGetValue(token, out key))
        {
            return true;
        }

        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        if (token.Length == 1 && char.IsAsciiLetter(token[0]))
        {
            key = Key.A + (char.ToUpperInvariant(token[0]) - 'A');
            return true;
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }

    private static string KeyToToken(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.OemQuestion => "/",
        Key.OemBackslash => "\\",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDown",
        _ => key.ToString(),
    };
}
