using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Signet.Core.Fonts;

/// <summary>The descriptors of an <c>@font-face</c> rule for one font file.</summary>
/// <param name="Family">The family name (without quotes).</param>
/// <param name="Weight">The <c>font-weight</c> value: <c>normal</c>, <c>bold</c>, a number, or a range (<c>100 900</c>).</param>
/// <param name="Style">The <c>font-style</c> value: <c>normal</c>, <c>italic</c> or <c>oblique</c>.</param>
public sealed record FontFaceDescriptor(string Family, string Weight, string Style);

/// <summary>A <c>font-weight</c> range (a static face has <see cref="Min"/> = <see cref="Max"/>).</summary>
/// <param name="Min">The lightest weight covered.</param>
/// <param name="Max">The heaviest weight covered.</param>
public readonly record struct FontWeightRange(int Min, int Max)
{
    /// <summary>The weight of <c>normal</c>.</summary>
    public static FontWeightRange Normal { get; } = new(400, 400);

    /// <inheritdoc/>
    public override string ToString() =>
        Min == Max
            ? Min.ToString(CultureInfo.InvariantCulture)
            : string.Concat(Min.ToString(CultureInfo.InvariantCulture), " ", Max.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// Derives the <c>@font-face</c> descriptors of a font file from what the file says about itself
/// (<see cref="FontFileInfo"/>) and normalizes descriptor values, so faces can be compared.
/// </summary>
public static class FontFaceDescriptors
{
    // Style words in font names, longest first (so "SemiBold" is not read as "Bold"), with their weights.
    private static readonly (string Word, int Weight)[] WeightWords =
    {
        ("extralight", 200), ("ultralight", 200), ("semibold", 600), ("demibold", 600), ("extrabold", 800),
        ("ultrabold", 800), ("hairline", 100), ("thin", 100), ("light", 300), ("medium", 500), ("regular", 400),
        ("normal", 400), ("book", 400), ("bold", 700), ("black", 900), ("heavy", 900),
    };

    private static readonly char[] NameSeparators = { '-', '_', ' ' };

    private static readonly string[] StyleWords = { "italic", "oblique", "slanted", "it" };

    /// <summary>
    /// The descriptors for a font file: the family from <see cref="FontFileInfo.PreferredFamily"/> (or from the
    /// file name when the file has no names), the weight from <c>OS/2</c> (or the <c>wght</c> axis of a variable font,
    /// or the style name), the style from <c>fsSelection</c> (or the style name).
    /// </summary>
    public static FontFaceDescriptor FromFont(FontFileInfo info, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string family = info.PreferredFamily.Length > 0 ? info.PreferredFamily : FamilyFromFileName(fileName);
        string styleName = info.PreferredSubfamily.Length > 0 ? info.PreferredSubfamily : StyleNameFromFileName(fileName);
        return new FontFaceDescriptor(family, WeightOf(info, styleName), StyleOf(info, styleName));
    }

    /// <summary>
    /// A family name guessed from a file name: the extension and the trailing style words are dropped and the
    /// remaining parts joined with spaces (<c>Acme-BoldItalic.ttf</c> → <c>Acme</c>).
    /// </summary>
    public static string FamilyFromFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        List<string> parts = Path.GetFileNameWithoutExtension(fileName)
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        while (parts.Count > 1 && IsStyleName(parts[^1]))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return parts.Count == 0 ? Path.GetFileNameWithoutExtension(fileName) : string.Join(" ", parts);
    }

    /// <summary>The CSS keyword for a weight when there is one (400 → <c>normal</c>, 700 → <c>bold</c>), otherwise the number.</summary>
    public static string CssWeight(int weight) => weight switch
    {
        400 => "normal",
        700 => "bold",
        _ => weight.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The generic family to fall back to after this font: <c>monospace</c>, <c>cursive</c> or <c>sans-serif</c> when
    /// PANOSE or the IBM font class says so, otherwise <c>serif</c>.
    /// </summary>
    public static string GenericFallback(FontFileInfo info)
    {
        if (info.PanoseFamilyType == 2 && info.PanoseProportion == 9)
        {
            return "monospace";
        }

        if (info.PanoseFamilyType == 3)
        {
            return "cursive";
        }

        if (info.PanoseFamilyType == 2 && info.PanoseSerifStyle is >= 11 and <= 13)
        {
            return "sans-serif";
        }

        return ((info.FamilyClass ?? 0) >> 8) switch
        {
            8 => "sans-serif",
            10 => "cursive",
            _ => "serif",
        };
    }

    /// <summary>
    /// Parses a <c>font-weight</c> descriptor value: <c>normal</c>, <c>bold</c>, a number or a range of two numbers.
    /// Anything else (also an empty value, the default of <c>@font-face</c>) is <c>normal</c>.
    /// </summary>
    public static FontWeightRange ParseWeight(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] parts = StripImportant(value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return FontWeightRange.Normal;
        }

        int? first = WeightValue(parts[0]);
        if (first is null)
        {
            return FontWeightRange.Normal;
        }

        int? second = parts.Length > 1 ? WeightValue(parts[1]) : null;
        return second is { } max ? new FontWeightRange(Math.Min(first.Value, max), Math.Max(first.Value, max)) : new FontWeightRange(first.Value, first.Value);
    }

    /// <summary>Normalizes a <c>font-style</c> value to <c>normal</c>, <c>italic</c> or <c>oblique</c>.</summary>
    public static string NormalizeStyle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string v = StripImportant(value).Trim().ToLowerInvariant();
        return v.StartsWith("italic", StringComparison.Ordinal) ? "italic"
            : v.StartsWith("oblique", StringComparison.Ordinal) ? "oblique"
            : "normal";
    }

    /// <summary>
    /// A family name as CSS compares it: surrounding quotes removed, escapes resolved, inner whitespace collapsed and
    /// lower-cased (family names are matched case-insensitively).
    /// </summary>
    public static string FamilyKey(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return NormalizeFamily(family).ToLowerInvariant();
    }

    /// <summary>A family name without surrounding quotes, with escapes resolved and inner whitespace collapsed.</summary>
    public static string NormalizeFamily(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        string v = family.Trim();
        if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0])
        {
            v = v[1..^1];
        }

        StringBuilder sb = new(v.Length);
        bool space = false;
        for (int i = 0; i < v.Length; i++)
        {
            char c = v[i];
            if (c == '\\' && i + 1 < v.Length)
            {
                c = v[++i];
            }
            else if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>A description of a face for messages, e.g. <c>bold italic</c>, <c>300</c> or <c>normal</c>.</summary>
    public static string Describe(FontWeightRange weight, string style)
    {
        ArgumentNullException.ThrowIfNull(style);
        string w = weight.Min == weight.Max ? CssWeight(weight.Min) : weight.ToString();
        if (style == "normal")
        {
            return w;
        }

        return w == "normal" ? style : string.Concat(w, " ", style);
    }

    private static string WeightOf(FontFileInfo info, string styleName)
    {
        if (info.HasWeightAxis)
        {
            int min = (int)Math.Round(info.WeightAxisMin!.Value);
            int max = (int)Math.Round(info.WeightAxisMax!.Value);
            return min == max ? CssWeight(min) : new FontWeightRange(min, max).ToString();
        }

        if (info.WeightClass is { } weightClass and > 0)
        {
            int weight = weightClass < 10 ? weightClass * 100 : Math.Clamp((int)weightClass, 1, 1000);

            // Some fonts keep usWeightClass 400 for their bold face; fsSelection and the style name know better.
            if (weight == 400 && (info.IsBold || WeightFromName(styleName) == 700))
            {
                weight = 700;
            }

            return CssWeight(weight);
        }

        return CssWeight(WeightFromName(styleName));
    }

    private static string StyleOf(FontFileInfo info, string styleName)
    {
        if (info.IsItalic)
        {
            return "italic";
        }

        if (info.IsOblique)
        {
            return "oblique";
        }

        string compact = Compact(styleName);
        return compact.Contains("italic", StringComparison.Ordinal) ? "italic"
            : compact.Contains("oblique", StringComparison.Ordinal) || compact.Contains("slanted", StringComparison.Ordinal) ? "oblique"
            : "normal";
    }

    private static int WeightFromName(string styleName)
    {
        string compact = Compact(styleName);
        foreach ((string word, int weight) in WeightWords)
        {
            if (compact.Contains(word, StringComparison.Ordinal))
            {
                return weight;
            }
        }

        return 400;
    }

    // The style words at the end of a file name, e.g. "Acme-SemiBoldItalic" → "SemiBoldItalic".
    private static string StyleNameFromFileName(string fileName)
    {
        string[] parts = Path.GetFileNameWithoutExtension(fileName)
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Skip(1).Reverse().TakeWhile(IsStyleName).Reverse());
    }

    // Whether a name part consists only of style words ("Bold", "BoldItalic", "SemiBold", "It").
    private static bool IsStyleName(string part)
    {
        string rest = part.ToLowerInvariant();
        while (rest.Length > 0)
        {
            string? word = WeightWords.Select(w => w.Word).Concat(StyleWords)
                .Where(w => rest.StartsWith(w, StringComparison.Ordinal))
                .OrderByDescending(w => w.Length)
                .FirstOrDefault();
            if (word is null)
            {
                return false;
            }

            rest = rest[word.Length..];
        }

        return part.Length > 0;
    }

    private static string Compact(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static int? WeightValue(string token) => token.ToLowerInvariant() switch
    {
        "normal" => 400,
        "bold" => 700,
        _ => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n is >= 1 and <= 1000
            ? (int)Math.Round(n)
            : null,
    };

    private static string StripImportant(string value)
    {
        int bang = value.IndexOf('!', StringComparison.Ordinal);
        return bang >= 0 ? value[..bang] : value;
    }
}
