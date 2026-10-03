using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Signet.Core.Localization;
using Xunit;

namespace Signet.Core.Tests.Localization;

/// <summary>The text resources of the Core layer (<see cref="CoreStrings"/>): a complete set of PL/EN translations.</summary>
public sealed class CoreStringsTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    private static Dictionary<string, string> Entries(string culture)
    {
        ResourceSet? set = CoreStrings.RawManager.GetResourceSet(new CultureInfo(culture), true, false);
        set.Should().NotBeNull();
        return set!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void Polish_and_english_resources_have_the_same_non_empty_keys()
    {
        // Neutralny = polski (CultureInfo.InvariantCulture), satelita = angielski.
        Dictionary<string, string> polish = Entries(CultureInfo.InvariantCulture.Name);
        Dictionary<string, string> english = Entries("en");

        polish.Should().NotBeEmpty();
        english.Keys.Should().BeEquivalentTo(polish.Keys);
        polish.Values.Should().OnlyContain(v => v.Trim().Length > 0);
        english.Values.Should().OnlyContain(v => v.Trim().Length > 0);
    }

    [Fact]
    public void Translations_use_the_same_format_placeholders()
    {
        Dictionary<string, string> polish = Entries(CultureInfo.InvariantCulture.Name);
        Dictionary<string, string> english = Entries("en");

        List<string> mismatched = polish.Keys
            .Where(k => english.ContainsKey(k) && !Placeholders(polish[k]).SetEquals(Placeholders(english[k])))
            .ToList();

        mismatched.Should().BeEmpty("a translation must use the same format arguments");
    }

    [Fact]
    public void Text_follows_the_current_ui_culture()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            string english = CoreStrings.Get("LoadWarning_NcxCreated");
            CultureInfo.CurrentUICulture = new CultureInfo("pl");
            string polish = CoreStrings.Get("LoadWarning_NcxCreated");

            english.Should().Be("The OPF does not contain an NCX file. Signet created a new one.");
            polish.Should().Be("Plik OPF nie zawiera pliku NCX. Signet utworzył nowy.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static HashSet<string> Placeholders(string value) =>
        Placeholder.Matches(value).Select(m => m.Groups[1].Value).ToHashSet();
}
