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
}
