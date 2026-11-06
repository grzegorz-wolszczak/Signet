using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Unicode;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

/// <summary>
/// The character classes of XML Schema regular expressions as Jing's <c>Translator</c> makes them. Jing prints
/// <c>\p{Lu}</c> or <c>\p{InGreek}</c> for <c>java.util.regex</c>, so the categories and blocks are those of the JDK
/// that runs it; the port takes them from .NET's Unicode data (<see cref="CharUnicodeInfo"/>,
/// <see cref="UnicodeRanges"/>), with Jing's own corrections for the categories it considered wrong in old JDKs.
/// </summary>
internal static class XsdCharClasses
{
    private const int MinSupplementary = 0x10000;
    private const char Unicode31AddedLu = 'ϴ';
    private const char Unicode31AddedLl = 'ϵ';
    private const char Unicode31NoToNlMin = 'ᛮ';
    private const char Unicode31NoToNlMax = 'ᛰ';
    private const string CategoryPi = "«‘‛“‟‹";
    private const string CategoryPf = "»’”›";

    private static readonly Lazy<FrozenDictionary<string, CodePointSet>> s_generalCategories = new(ComputeGeneralCategories);
    private static readonly Lazy<FrozenDictionary<string, CodePointSet>> s_blocks = new(ComputeBlocks);
    private static readonly Lazy<CodePointSet> s_nameStart = new(() => NameSet(start: true));
    private static readonly Lazy<CodePointSet> s_nameChar = new(() => NameSet(start: false));

    /// <summary>The one-letter categories, in Jing's order.</summary>
    public const string Categories = "LMNPZSC";

    /// <summary>The two-letter categories, in Jing's order.</summary>
    public const string SubCategories = "LuLlLtLmLoMnMcMeNdNlNoPcPdPsPePiPfPoZsZlZpSmScSkSoCcCfCoCn";

    /// <summary>The blocks of <c>\p{IsX}</c> that Jing passes to Java as <c>\p{InX}</c> (Unicode 3.1 names, BMP only).</summary>
    private static readonly string[] s_blockNames =
    [
        "BasicLatin", "Latin-1Supplement", "LatinExtended-A", "LatinExtended-B", "IPAExtensions", "SpacingModifierLetters",
        "CombiningDiacriticalMarks", "Greek", "Cyrillic", "Armenian", "Hebrew", "Arabic", "Syriac", "Thaana", "Devanagari",
        "Bengali", "Gurmukhi", "Gujarati", "Oriya", "Tamil", "Telugu", "Kannada", "Malayalam", "Sinhala", "Thai", "Lao",
        "Tibetan", "Myanmar", "Georgian", "HangulJamo", "Ethiopic", "Cherokee", "UnifiedCanadianAboriginalSyllabics",
        "Ogham", "Runic", "Khmer", "Mongolian", "LatinExtendedAdditional", "GreekExtended", "GeneralPunctuation",
        "SuperscriptsandSubscripts", "CurrencySymbols", "CombiningMarksforSymbols", "LetterlikeSymbols", "NumberForms",
        "Arrows", "MathematicalOperators", "MiscellaneousTechnical", "ControlPictures", "OpticalCharacterRecognition",
        "EnclosedAlphanumerics", "BoxDrawing", "BlockElements", "GeometricShapes", "MiscellaneousSymbols", "Dingbats",
        "BraillePatterns", "CJKRadicalsSupplement", "KangxiRadicals", "IdeographicDescriptionCharacters",
        "CJKSymbolsandPunctuation", "Hiragana", "Katakana", "Bopomofo", "HangulCompatibilityJamo", "Kanbun",
        "BopomofoExtended", "EnclosedCJKLettersandMonths", "CJKCompatibility", "CJKUnifiedIdeographsExtensionA",
        "CJKUnifiedIdeographs", "YiSyllables", "YiRadicals", "HangulSyllables", "CJKCompatibilityIdeographs",
        "AlphabeticPresentationForms", "ArabicPresentationForms-A", "CombiningHalfMarks", "CJKCompatibilityForms",
        "SmallFormVariants", "ArabicPresentationForms-B", "Specials", "HalfwidthandFullwidthForms",
    ];

    // Where .NET names a block differently from the Unicode 3.1 name Jing uses.
    private static readonly FrozenDictionary<string, string> s_blockAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["greek"] = "greekandcoptic",
        ["combiningmarksforsymbols"] = "combiningdiacriticalmarksforsymbols",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // The blocks with ranges outside the BMP: Jing gives their ranges itself.
    private static readonly FrozenDictionary<string, CodePointSet> s_specialBlocks = new Dictionary<string, CodePointSet>(StringComparer.Ordinal)
    {
        ["OldItalic"] = CodePointSet.Range(0x10300, 0x1032F),
        ["Gothic"] = CodePointSet.Range(0x10330, 0x1034F),
        ["Deseret"] = CodePointSet.Range(0x10400, 0x1044F),
        ["ByzantineMusicalSymbols"] = CodePointSet.Range(0x1D000, 0x1D0FF),
        ["MusicalSymbols"] = CodePointSet.Range(0x1D100, 0x1D1FF),
        ["MathematicalAlphanumericSymbols"] = CodePointSet.Range(0x1D400, 0x1D7FF),
        ["CJKUnifiedIdeographsExtensionB"] = CodePointSet.Range(0x20000, 0x2A6D6),
        ["CJKCompatibilityIdeographsSupplement"] = CodePointSet.Range(0x2F800, 0x2FA1F),
        ["Tags"] = CodePointSet.Range(0xE0000, 0xE007F),
        ["PrivateUse"] = CodePointSet.FromRanges([(0xE000, 0xF8FF), (0xF0000, 0xFFFFD), (0x100000, 0x10FFFD)]),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary><c>\s</c>.</summary>
    public static CodePointSet Space { get; } = CodePointSet.FromRanges([(' ', ' '), ('\n', '\n'), ('\r', '\r'), ('\t', '\t')]);

    /// <summary><c>.</c>: anything but a line end.</summary>
    public static CodePointSet Dot { get; } = CodePointSet.FromRanges([('\n', '\n'), ('\r', '\r')]).Complement();

    /// <summary><c>\i</c>: the characters that start an XML 1.0 name (Jing: Unicode categories with exceptions computed
    /// from the XML 1.0 tables, BMP only).</summary>
    public static CodePointSet NameStart => s_nameStart.Value;

    /// <summary><c>\c</c>: the characters of an XML 1.0 name.</summary>
    public static CodePointSet NameChar => s_nameChar.Value;

    /// <summary><c>\d</c>.</summary>
    public static CodePointSet Digit => Property("Nd");

    /// <summary><c>\w</c>: anything but punctuation, separators and "other" characters.</summary>
    public static CodePointSet WordChar => CodePointSet.Union([Category('P'), Category('Z'), Category('C')]).Complement();

    /// <summary>The class of a one-letter category (<c>\p{L}</c>) with Jing's additions.</summary>
    public static CodePointSet Category(char code)
    {
        List<CodePointSet> classes = [Property(code.ToString())];
        switch (code)
        {
            case 'P':
                classes.Add(Chars(CategoryPi + CategoryPf));
                break;
            case 'L':
                classes.Add(CodePointSet.Of(Unicode31AddedLl));
                classes.Add(CodePointSet.Of(Unicode31AddedLu));
                break;
            case 'C':
                classes.Add(SubCategory("Cn"));
                break;
        }

        return CodePointSet.Union(classes);
    }

    /// <summary>The class of a two-letter category (<c>\p{Lu}</c>) with Jing's corrections.</summary>
    public static CodePointSet SubCategory(string name) => name switch
    {
        "Pi" => Chars(CategoryPi),
        "Pf" => Chars(CategoryPf),
        "Cn" => Property("Cn").Subtract(CodePointSet.FromRanges([(Unicode31AddedLu, Unicode31AddedLu), (Unicode31AddedLl, Unicode31AddedLl)])),
        "Lu" => Property(name).Union(CodePointSet.Of(Unicode31AddedLu)),
        "Ll" => Property(name).Union(CodePointSet.Of(Unicode31AddedLl)),
        "Nl" => Property(name).Union(CodePointSet.Range(Unicode31NoToNlMin, Unicode31NoToNlMax)),
        "No" => Property(name).Subtract(CodePointSet.Range(Unicode31NoToNlMin, Unicode31NoToNlMax)),
        _ => Property(name),
    };

    /// <summary>The class of <c>\p{IsX}</c>, or <c>null</c> when X is not a block Jing knows.</summary>
    public static CodePointSet? Block(string blockName)
    {
        if (s_specialBlocks.TryGetValue(blockName, out CodePointSet? special))
        {
            return special;
        }

        return Array.IndexOf(s_blockNames, blockName) >= 0 ? s_blocks.Value[blockName] : null;
    }

    /// <summary>All the block names Jing knows (for the tests).</summary>
    public static IEnumerable<string> BlockNames => s_blockNames.Concat(s_specialBlocks.Keys);

    // Java's \p{X}: a general category, or a group of them, of every code point.
    private static CodePointSet Property(string name) => s_generalCategories.Value[name];

    private static CodePointSet Chars(string chars) => CodePointSet.FromRanges(chars.Select(c => ((int)c, (int)c)));

    private static FrozenDictionary<string, CodePointSet> ComputeGeneralCategories()
    {
        Dictionary<string, List<(int, int)>> ranges = new(StringComparer.Ordinal);
        int start = 0;
        string current = Abbreviation(CharUnicodeInfo.GetUnicodeCategory(0));
        for (int cp = 1; cp <= CodePointSet.MaxCodePoint + 1; cp++)
        {
            string category = cp <= CodePointSet.MaxCodePoint ? Abbreviation(CharUnicodeInfo.GetUnicodeCategory(cp)) : string.Empty;
            if (category != current)
            {
                Add(ranges, current, start, cp - 1);
                Add(ranges, current[..1], start, cp - 1);
                start = cp;
                current = category;
            }
        }

        Dictionary<string, CodePointSet> result = new(StringComparer.Ordinal);
        for (int i = 0; i < SubCategories.Length; i += 2)
        {
            string name = SubCategories.Substring(i, 2);
            result[name] = CodePointSet.FromRanges(ranges.GetValueOrDefault(name) ?? []);
        }

        foreach (char c in Categories)
        {
            string name = c.ToString();
            result[name] = CodePointSet.FromRanges(ranges.GetValueOrDefault(name) ?? []);
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, List<(int, int)>> ranges, string name, int min, int max)
    {
        if (!ranges.TryGetValue(name, out List<(int, int)>? list))
        {
            list = [];
            ranges[name] = list;
        }

        list.Add((min, max));
    }

    private static string Abbreviation(UnicodeCategory category) => category switch
    {
        UnicodeCategory.UppercaseLetter => "Lu",
        UnicodeCategory.LowercaseLetter => "Ll",
        UnicodeCategory.TitlecaseLetter => "Lt",
        UnicodeCategory.ModifierLetter => "Lm",
        UnicodeCategory.OtherLetter => "Lo",
        UnicodeCategory.NonSpacingMark => "Mn",
        UnicodeCategory.SpacingCombiningMark => "Mc",
        UnicodeCategory.EnclosingMark => "Me",
        UnicodeCategory.DecimalDigitNumber => "Nd",
        UnicodeCategory.LetterNumber => "Nl",
        UnicodeCategory.OtherNumber => "No",
        UnicodeCategory.SpaceSeparator => "Zs",
        UnicodeCategory.LineSeparator => "Zl",
        UnicodeCategory.ParagraphSeparator => "Zp",
        UnicodeCategory.Control => "Cc",
        UnicodeCategory.Format => "Cf",
        UnicodeCategory.Surrogate => "Cs",
        UnicodeCategory.PrivateUse => "Co",
        UnicodeCategory.ConnectorPunctuation => "Pc",
        UnicodeCategory.DashPunctuation => "Pd",
        UnicodeCategory.OpenPunctuation => "Ps",
        UnicodeCategory.ClosePunctuation => "Pe",
        UnicodeCategory.InitialQuotePunctuation => "Pi",
        UnicodeCategory.FinalQuotePunctuation => "Pf",
        UnicodeCategory.OtherPunctuation => "Po",
        UnicodeCategory.MathSymbol => "Sm",
        UnicodeCategory.CurrencySymbol => "Sc",
        UnicodeCategory.ModifierSymbol => "Sk",
        UnicodeCategory.OtherSymbol => "So",
        _ => "Cn",
    };

    private static FrozenDictionary<string, CodePointSet> ComputeBlocks()
    {
        Dictionary<string, UnicodeRange> byName = typeof(UnicodeRanges)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(UnicodeRange))
            .ToDictionary(p => p.Name.ToLowerInvariant(), p => (UnicodeRange)p.GetValue(null)!, StringComparer.Ordinal);
        Dictionary<string, CodePointSet> result = new(StringComparer.Ordinal);
        foreach (string name in s_blockNames.Distinct(StringComparer.Ordinal))
        {
            string key = name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            UnicodeRange range = byName[s_blockAliases.GetValueOrDefault(key, key)];
            result[name] = CodePointSet.Range(range.FirstCodePoint, range.FirstCodePoint + range.Length - 1);
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static CodePointSet NameSet(bool start)
    {
        List<(int, int)> ranges = [];
        int? rangeStart = null;
        for (int cp = 0; cp <= MinSupplementary; cp++)
        {
            bool inSet = cp < MinSupplementary && (start ? Naming.IsName(((char)cp).ToString()) : Naming.IsNmtoken(((char)cp).ToString()));
            if (inSet && rangeStart is null)
            {
                rangeStart = cp;
            }
            else if (!inSet && rangeStart is { } s)
            {
                ranges.Add((s, cp - 1));
                rangeStart = null;
            }
        }

        return CodePointSet.FromRanges(ranges);
    }
}
