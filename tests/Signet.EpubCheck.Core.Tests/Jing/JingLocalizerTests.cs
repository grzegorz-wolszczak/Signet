using System.Collections;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

public sealed class JingLocalizerTests
{
    private const int PatternMessageCount = 127;
    private const int ParseMessageCount = 38;

    private static readonly CultureInfo s_polish = CultureInfo.GetCultureInfo("pl");

    public static TheoryData<string, int> Bundles => new()
    {
        { nameof(JingLocalizer.PatternManager), PatternMessageCount },
        { nameof(JingLocalizer.ParseManager), ParseMessageCount },
    };

    [Theory]
    [MemberData(nameof(Bundles))]
    public void ThePolishTexts_HaveTheKeysAndPlaceholdersOfTheEnglishOnes(string bundle, int count)
    {
        // Arrange
        ResourceManager manager = bundle == nameof(JingLocalizer.PatternManager) ? JingLocalizer.PatternManager : JingLocalizer.ParseManager;
        Hashtable english = Resources(manager, CultureInfo.InvariantCulture);
        Hashtable polish = Resources(manager, s_polish);

        // Act
        string[] missing = english.Keys.Cast<string>().Except(polish.Keys.Cast<string>()).Order().ToArray();
        string[] extra = polish.Keys.Cast<string>().Except(english.Keys.Cast<string>()).Order().ToArray();
        string[] differentPlaceholders = english.Keys.Cast<string>()
            .Where(polish.ContainsKey)
            .Where(key => Placeholders((string)english[key]!) != Placeholders((string)polish[key]!))
            .Order().ToArray();

        // Assert
        missing.Should().BeEmpty();
        extra.Should().BeEmpty();
        differentPlaceholders.Should().BeEmpty();
        english.Count.Should().Be(count);
    }

    [Theory]
    [InlineData("element {0} not allowed here", "element \"a\" not allowed here")]
    [InlineData("{1} and {0}", "\"b\" and \"a\"")]
    [InlineData("it''s {0}", "it's \"a\"")]
    [InlineData("'{0}' is literal", "{0} is literal")]
    [InlineData("no argument {5}", "no argument {5}")]
    public void Format_JavaMessageFormatPattern_GivesWhatJavaGives(string pattern, string expected)
    {
        // Act
        string text = JingLocalizer.Format(pattern, ["\"a\"", "\"b\""]);

        // Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void Format_NullArgument_IsPrintedAsNull()
    {
        // Act
        string text = JingLocalizer.Format("value {0}", [null]);

        // Assert
        text.Should().Be("value null");
    }

    [Fact]
    public void Message_UnsupportedCulture_FallsBackToEnglish()
    {
        // Act
        string text = JingLocalizer.ForPattern(CultureInfo.GetCultureInfo("de")).Message("missing_start_element");

        // Assert
        text.Should().Be("missing \"start\" element");
    }

    private static Hashtable Resources(ResourceManager manager, CultureInfo culture)
    {
        ResourceSet set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        Hashtable table = [];
        foreach (DictionaryEntry e in set)
        {
            table.Add(e.Key, e.Value);
        }

        return table;
    }

    private static string Placeholders(string text) =>
        string.Join(',', Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
}

public sealed class NamingTests
{
    [Theory]
    [InlineData("a", true, true, true, true)]
    [InlineData("a:b", true, true, false, true)]
    [InlineData(":a", true, true, false, false)]
    [InlineData("a:", true, true, false, false)]
    [InlineData("1a", false, true, false, false)]
    [InlineData("-", false, true, false, false)]
    [InlineData("é_x.1", true, true, true, true)]
    [InlineData("", false, false, false, false)]
    [InlineData("a b", false, false, false, false)]
    public void Predicates_GiveWhatXml10Says(string value, bool name, bool nmtoken, bool ncname, bool qname)
    {
        // Act
        bool[] results = [Naming.IsName(value), Naming.IsNmtoken(value), Naming.IsNcname(value), Naming.IsQname(value)];

        // Assert
        results.Should().Equal(name, nmtoken, ncname, qname);
    }
}
