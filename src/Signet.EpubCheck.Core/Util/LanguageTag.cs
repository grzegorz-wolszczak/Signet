using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Signet.EpubCheck.Core.Util;

/// <summary>
/// Checks that a language tag is well-formed as BCP 47 says (what Java's <c>Locale.Builder.setLanguageTag</c> checks, and
/// words its error as <c>IllformedLocaleException</c> does): <c>language [extlangs] [script] [region] [variants] [extensions]
/// [privateuse]</c>, or one of the grandfathered tags.
/// </summary>
public static class LanguageTag
{
    private const char Separator = '-';
    private const int MaxExtlangs = 3;

    // The irregular grandfathered tags map to their modern equivalent, which is what is parsed.
    private static readonly Dictionary<string, string> s_grandfathered = new(StringComparer.Ordinal)
    {
        ["art-lojban"] = "jbo", ["i-ami"] = "ami", ["i-bnn"] = "bnn", ["i-hak"] = "hak", ["i-klingon"] = "tlh",
        ["i-lux"] = "lb", ["i-navajo"] = "nv", ["i-pwn"] = "pwn", ["i-tao"] = "tao", ["i-tay"] = "tay", ["i-tsu"] = "tsu",
        ["no-bok"] = "nb", ["no-nyn"] = "nn", ["sgn-be-fr"] = "sfb", ["sgn-be-nl"] = "vgt", ["sgn-ch-de"] = "sgg",
        ["zh-guoyu"] = "cmn", ["zh-hakka"] = "hak", ["zh-min-nan"] = "nan", ["zh-xiang"] = "hsn",
        ["en-gb-oed"] = "en-GB-oxendict", ["i-default"] = "en-x-i-default", ["i-enochian"] = "und-x-i-enochian",
        ["i-mingo"] = "see-x-i-mingo", ["zh-min"] = "nan-x-zh-min", ["cel-gaulish"] = "xtg-x-cel-gaulish",
    };

    /// <summary>Why <paramref name="tag"/> is not well-formed (with the place, as Java words it), or <c>null</c> when it is.</summary>
    public static string? Validate(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        string text = s_grandfathered.GetValueOrDefault(tag.ToLower(CultureInfo.InvariantCulture), tag);
        Tokens tokens = new(text);
        (string Message, int Index)? error = null;

        if (ParseLanguage(tokens))
        {
            ParseExtlangs(tokens);
            ParseScript(tokens);
            ParseRegion(tokens);
            ParseVariants(tokens);
            error = ParseExtensions(tokens);
        }

        error ??= ParsePrivateUse(tokens);
        if (error is null && !tokens.IsDone)
        {
            error = (tokens.Current.Length == 0 ? "Empty subtag" : "Invalid subtag: " + tokens.Current, tokens.Start);
        }

        return error is { } found ? found.Message + " [at index " + found.Index.ToString(CultureInfo.InvariantCulture) + "]" : null;
    }

    private static bool ParseLanguage(Tokens tokens)
    {
        if (tokens.IsDone || !IsAlpha(tokens.Current, 2, 8))
        {
            return false;
        }

        tokens.Next();
        return true;
    }

    private static void ParseExtlangs(Tokens tokens)
    {
        int count = 0;
        while (!tokens.IsDone && IsAlpha(tokens.Current, 3, 3))
        {
            tokens.Next();
            if (++count == MaxExtlangs)
            {
                break;
            }
        }
    }

    private static void ParseScript(Tokens tokens)
    {
        if (!tokens.IsDone && IsAlpha(tokens.Current, 4, 4))
        {
            tokens.Next();
        }
    }

    private static void ParseRegion(Tokens tokens)
    {
        if (!tokens.IsDone && (IsAlpha(tokens.Current, 2, 2) || (tokens.Current.Length == 3 && tokens.Current.All(char.IsAsciiDigit))))
        {
            tokens.Next();
        }
    }

    private static void ParseVariants(Tokens tokens)
    {
        while (!tokens.IsDone && IsVariant(tokens.Current))
        {
            tokens.Next();
        }
    }

    private static (string, int)? ParseExtensions(Tokens tokens)
    {
        while (!tokens.IsDone && tokens.Current.Length == 1 && char.IsAsciiLetterOrDigit(tokens.Current[0]) && !IsPrivateUsePrefix(tokens.Current))
        {
            string singleton = tokens.Current;
            int start = tokens.Start;
            tokens.Next();
            int subtags = 0;
            while (!tokens.IsDone && IsAlphaNumeric(tokens.Current, 2, 8))
            {
                subtags++;
                tokens.Next();
            }

            if (subtags == 0)
            {
                return ("Incomplete extension '" + singleton + "'", start);
            }
        }

        return null;
    }

    private static (string, int)? ParsePrivateUse(Tokens tokens)
    {
        if (tokens.IsDone || !IsPrivateUsePrefix(tokens.Current))
        {
            return null;
        }

        int start = tokens.Start;
        tokens.Next();
        int subtags = 0;
        while (!tokens.IsDone && IsAlphaNumeric(tokens.Current, 1, 8))
        {
            subtags++;
            tokens.Next();
        }

        return subtags == 0 ? ("Incomplete privateuse", start) : null;
    }

    private static bool IsPrivateUsePrefix(string subtag) => subtag.Length == 1 && subtag[0] is 'x' or 'X';

    private static bool IsVariant(string subtag) =>
        (subtag.Length is >= 5 and <= 8 && subtag.All(char.IsAsciiLetterOrDigit))
        || (subtag.Length == 4 && char.IsAsciiDigit(subtag[0]) && subtag.Skip(1).All(char.IsAsciiLetterOrDigit));

    private static bool IsAlpha(string subtag, int min, int max) => subtag.Length >= min && subtag.Length <= max && subtag.All(char.IsAsciiLetter);

    private static bool IsAlphaNumeric(string subtag, int min, int max) =>
        subtag.Length >= min && subtag.Length <= max && subtag.All(char.IsAsciiLetterOrDigit);

    // Java's StringTokenIterator over "-": an empty string is one empty token; "a--b" has an empty token between.
    private sealed class Tokens
    {
        private readonly string _text;
        private int _start;
        private int _end;

        public Tokens(string text)
        {
            _text = text;
            _end = text.IndexOf(Separator, StringComparison.Ordinal);
            if (_end < 0)
            {
                _end = text.Length;
            }
        }

        public bool IsDone { get; private set; }

        public string Current => IsDone ? string.Empty : _text[_start.._end];

        public int Start => _start;

        public void Next()
        {
            if (_end >= _text.Length)
            {
                IsDone = true;
                return;
            }

            _start = _end + 1;
            _end = _text.IndexOf(Separator, _start);
            if (_end < 0)
            {
                _end = _text.Length;
            }
        }
    }
}
