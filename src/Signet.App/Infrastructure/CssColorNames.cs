using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>The CSS / X11 color names Avalonia knows (<c>rebeccapurple</c>, <c>tomato</c>, …), both directions.</summary>
public static class CssColorNames
{
    private static readonly Lazy<Dictionary<uint, string>> s_byRgb = new(Build);

    /// <summary>
    /// Parses a color name (case-insensitive, letters only — a hex code is not a name). Returns the opaque color.
    /// </summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        string name = text?.Trim() ?? string.Empty;
        if (name.Length == 0 || !name.All(char.IsLetter) || !Color.TryParse(name, out Color parsed))
        {
            return false;
        }

        color = parsed;
        return true;
    }

    /// <summary>The name of an exactly matching opaque color (ignores alpha); <see langword="null"/> when it has none.</summary>
    public static string? NameOf(Color color) =>
        s_byRgb.Value.TryGetValue(Key(color), out string? name) ? name : null;

    /// <summary>Every distinct named color (aliases such as grey / gray collapsed), ordered by hue then brightness.</summary>
    public static IReadOnlyList<(string Name, Color Color)> All => s_all.Value;

    private static readonly Lazy<IReadOnlyList<(string Name, Color Color)>> s_all = new(() =>
        s_byRgb.Value
            .Select(pair => (Name: pair.Value, Color: Color.FromRgb((byte)(pair.Key >> 16), (byte)(pair.Key >> 8), (byte)pair.Key)))
            .OrderBy(entry => entry.Color.ToHsv() is { S: >= 0.1 } hsv ? Math.Floor(hsv.H / 15) : -1)
            .ThenByDescending(entry => entry.Color.ToHsv().V)
            .ToList());

    private static uint Key(Color color) => ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    // Alphabetically first name wins for aliases (aqua / cyan, fuchsia / magenta, gray / grey, …).
    private static Dictionary<uint, string> Build()
    {
        Dictionary<uint, string> result = [];
        foreach (PropertyInfo property in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static)
                     .Where(p => p.PropertyType == typeof(Color))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            Color color = (Color)property.GetValue(null)!;
            if (color.A == 255)
            {
                result.TryAdd(Key(color), property.Name.ToLowerInvariant());
            }
        }

        return result;
    }
}
