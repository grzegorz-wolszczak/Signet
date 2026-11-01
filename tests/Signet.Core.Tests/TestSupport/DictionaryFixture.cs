using System.Globalization;
using System.IO;
using System.Linq;

namespace Signet.Core.Tests.TestSupport;

/// <summary>Writes a minimal, synthetic <c>.aff</c>/<c>.dic</c> pair for the SpellChecker/HtmlSpellCheck tests.</summary>
public static class DictionaryFixture
{
    private const string TryChars = "esianrtolcdugmphbyfvkwjqxzESIANRTOLCDUGMPHBYFVKWJQXZ";

    /// <summary>Writes the dictionary <paramref name="name"/> (an <c>.aff</c>/<c>.dic</c> pair) in <paramref name="directory"/>.</summary>
    public static void Write(string directory, string name, params string[] words)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, name + ".aff"),
            $"SET UTF-8\nTRY {TryChars}\n");
        File.WriteAllLines(
            Path.Combine(directory, name + ".dic"),
            new[] { words.Length.ToString(CultureInfo.InvariantCulture) }.Concat(words));
    }

    /// <summary>
    /// Writes the dictionary <paramref name="name"/> the way a non-Unicode one is written: the <c>.aff</c> says
    /// <c>SET ISO8859-2</c> and both files are in that encoding (so is the Polish Hunspell dictionary).
    /// </summary>
    /// <remarks>
    /// The bytes are made here, letter by letter, and not with <c>Encoding.GetEncoding("ISO-8859-2")</c>: asking .NET
    /// for that encoding registers the code page provider for the whole test process and would hide the very thing the
    /// test is about (<c>Signet.Core.LegacyEncodings</c>).
    /// </remarks>
    public static void WriteIso88592(string directory, string name, params string[] words)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, name + ".aff"),
            ToIso88592("SET ISO8859-2\nTRY " + TryChars + PolishLetters + "\n"));
        File.WriteAllBytes(
            Path.Combine(directory, name + ".dic"),
            ToIso88592(string.Join('\n', new[] { words.Length.ToString(CultureInfo.InvariantCulture) }.Concat(words)) + "\n"));
    }

    // The Polish letters, in the order of PolishLettersIso88592 below.
    private const string PolishLetters = "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ";

    // Their ISO-8859-2 (Latin-2) bytes.
    private static readonly byte[] PolishLettersIso88592 =
    {
        0xB1, 0xE6, 0xEA, 0xB3, 0xF1, 0xF3, 0xB6, 0xBC, 0xBF,
        0xA1, 0xC6, 0xCA, 0xA3, 0xD1, 0xD3, 0xA6, 0xAC, 0xAF,
    };

    private static byte[] ToIso88592(string text)
    {
        byte[] bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            int polish = PolishLetters.IndexOf(text[i], System.StringComparison.Ordinal);
            bytes[i] = polish >= 0
                ? PolishLettersIso88592[polish]
                : text[i] < 128
                    ? (byte)text[i]
                    : throw new System.ArgumentException($"Not an ASCII or Polish letter: {text[i]}", nameof(text));
        }

        return bytes;
    }
}
