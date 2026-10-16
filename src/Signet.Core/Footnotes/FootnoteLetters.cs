using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core.Footnotes;

/// <summary>
/// The letters of the references of a shared note (<see cref="FootnoteLetterStyle"/>): <c>[4a]</c>, <c>[4b]</c> and the
/// links back <c>4a, 4b</c> of the note.
/// </summary>
public static class FootnoteLetters
{
    private static readonly (int Value, string Numeral)[] Romans =
    {
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"),
        (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    };

    private static readonly Regex RomanNumeral = new(
        "^m{0,3}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3})$", RegexOptions.CultureInvariant);

    /// <summary>The letters of the reference at <paramref name="index"/> (from 0): a… z, aa, ab…; A… Z, AA…; i, ii, iii….</summary>
    public static string Of(FootnoteLetterStyle style, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (style == FootnoteLetterStyle.LowerRoman)
        {
            return Roman(index + 1);
        }

        // Bijective base 26: z is followed by aa.
        StringBuilder letters = new();
        for (int n = index + 1; n > 0; n = (n - 1) / 26)
        {
            letters.Insert(0, (char)('a' + ((n - 1) % 26)));
        }

        return style == FootnoteLetterStyle.UpperLatin ? letters.ToString().ToUpperInvariant() : letters.ToString();
    }

    /// <summary>
    /// The style the letters of the references of a shared note are written in; <c>null</c> when none of them has
    /// letters (the note is not shared yet). Upper case is <see cref="FootnoteLetterStyle.UpperLatin"/>; lower case is
    /// <see cref="FootnoteLetterStyle.LowerRoman"/> when every one of them is a roman numeral (the letters of a note are
    /// written anew after every change, so a latin note always has an <c>a</c> or a <c>b</c>).
    /// </summary>
    public static FootnoteLetterStyle? Detect(IEnumerable<string> letters)
    {
        List<string> written = letters.Where(l => l.Length > 0).ToList();
        if (written.Count == 0)
        {
            return null;
        }

        if (written.Any(l => l.Any(char.IsUpper)))
        {
            return FootnoteLetterStyle.UpperLatin;
        }

        return written.All(IsRoman) ? FootnoteLetterStyle.LowerRoman : FootnoteLetterStyle.LowerLatin;
    }

    private static bool IsRoman(string letters) => letters.Length > 0 && RomanNumeral.IsMatch(letters);

    private static string Roman(int number)
    {
        StringBuilder roman = new();
        foreach ((int value, string numeral) in Romans)
        {
            while (number >= value)
            {
                roman.Append(numeral);
                number -= value;
            }
        }

        return roman.ToString();
    }
}
