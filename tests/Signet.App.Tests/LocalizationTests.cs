using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Signet.App.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// i18n tests: every key in <c>Strings.resx</c> (Polish, neutral) has a value in the
/// <c>Strings.en.resx</c> satellite, and no <c>.axaml</c> file in the view layer contains
/// hardcoded text in user-facing attributes (a heuristic scanner).
/// </summary>
public sealed class LocalizationTests
{
    // Literals deliberately allowed: the product name (not "text to translate").
    private static readonly HashSet<string> ExemptLiteralValues = new(StringComparer.Ordinal)
    {
        "Signet",
    };

    private static readonly Regex AttributeRegex = new(
        @"(?<![\w.])(?<attr>Header|Content|Text|PlaceholderText|Watermark|ToolTip\.Tip|Title)=""(?<val>[^""]*)""",
        RegexOptions.Compiled);

    // Contains at least one Unicode letter: tells text apart from bare symbols/arrows (e.g. "▲", "★").
    private static readonly Regex ContainsLetter = new(@"\p{L}", RegexOptions.Compiled);

    private static string AppSourceRoot { get; } = ResolveAppSourceRoot();

    [Fact]
    public void Every_polish_resource_key_has_an_english_translation()
    {
        ResourceManager manager = Strings.RawManager;
        ResourceSet? polish = manager.GetResourceSet(new System.Globalization.CultureInfo("pl"), true, true);
        ResourceSet? english = manager.GetResourceSet(new System.Globalization.CultureInfo("en"), true, true);
        polish.Should().NotBeNull();
        english.Should().NotBeNull();

        List<string> polishKeys = polish!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();
        List<string> englishKeys = english!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();

        polishKeys.Should().NotBeEmpty();
        englishKeys.Should().BeEquivalentTo(polishKeys, "every key of the neutral (Polish) language must have an English translation");
    }

    [Fact]
    public void Every_loc_key_used_in_axaml_exists_in_the_resources()
    {
        // A misspelled key renders as empty text (e.g. a blank OK button), with no build error.
        ResourceSet? polish = Strings.RawManager.GetResourceSet(new System.Globalization.CultureInfo("pl"), true, true);
        HashSet<string> keys = polish!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
        Regex locKey = new(@"\{loc:Loc\s+(?:Key=)?(?<key>[\w.]+)\s*\}");

        List<string> missing = new();
        foreach (string path in Directory.EnumerateFiles(AppSourceRoot, "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match match in locKey.Matches(File.ReadAllText(path)))
            {
                if (!keys.Contains(match.Groups["key"].Value))
                {
                    missing.Add($"{Path.GetRelativePath(AppSourceRoot, path)}: {match.Groups["key"].Value}");
                }
            }
        }

        missing.Should().BeEmpty("every loc:Loc key used in a view must exist in Strings.resx");
    }

    [Fact]
    public void No_axaml_view_has_a_hardcoded_user_facing_string_literal()
    {
        List<string> violations = new();

        foreach (string path in Directory.EnumerateFiles(AppSourceRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(path);
            string relative = Path.GetRelativePath(AppSourceRoot, path);

            foreach (Match match in AttributeRegex.Matches(content))
            {
                string value = match.Groups["val"].Value;
                if (value.Length == 0 || value.StartsWith('{'))
                {
                    continue;
                }

                if (!ContainsLetter.IsMatch(value) || ExemptLiteralValues.Contains(value))
                {
                    continue;
                }

                violations.Add($"{relative}: {match.Groups["attr"].Value}=\"{value}\"");
            }
        }

        violations.Should().BeEmpty(
            "all user-facing strings in the view layer (.axaml) must go through {{loc:Loc ...}}");
    }

    private static string ResolveAppSourceRoot()
    {
        string? raw = typeof(LocalizationTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "AppSourceRoot", StringComparison.Ordinal))
            ?.Value;

        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException(
                "The AssemblyMetadata(\"AppSourceRoot\") assembly attribute is not set; check Signet.App.Tests.csproj.");
        }

        string full = Path.GetFullPath(raw);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"AppSourceRoot does not exist: {full}");
        }

        return full;
    }
}
