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

    // What every step of the Cleanup dialog needs a string for.
    private static readonly string[] CleanupStepParts = { "Title", "Description", "Summary" };

    // Contains at least one Unicode letter: tells text apart from bare symbols/arrows (e.g. "▲", "★").
    private static readonly Regex ContainsLetter = new(@"\p{L}", RegexOptions.Compiled);

    internal static string AppSourceRoot { get; } = ResolveAppSourceRoot();

    [Fact]
    public void Every_polish_resource_key_has_an_english_translation()
    {
        // Arrange
        ResourceManager manager = Strings.RawManager;

        // Act
        ResourceSet? polish = manager.GetResourceSet(new System.Globalization.CultureInfo("pl"), true, true);
        ResourceSet? english = manager.GetResourceSet(new System.Globalization.CultureInfo("en"), true, true);

        // Assert
        polish.Should().NotBeNull();
        english.Should().NotBeNull();

        List<string> polishKeys = polish!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();
        List<string> englishKeys = english!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();

        polishKeys.Should().NotBeEmpty();
        englishKeys.Should().BeEquivalentTo(polishKeys, "every key of the neutral (Polish) language must have an English translation");
    }

    /// <summary>
    /// Every step of the Cleanup dialog shows a title, a description and a summary of what it found. The summary is
    /// built from the step's name at run time and only once the step is checked, so a missing one is not a blank
    /// label but an exception — it crashed the window in October 2026.
    /// </summary>
    [Fact]
    public void Every_cleanup_step_has_a_title_a_description_and_a_summary()
    {
        // Arrange
        ResourceSet keys = Strings.RawManager.GetResourceSet(new System.Globalization.CultureInfo("pl"), true, true)!;
        HashSet<string> known = keys
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Act
        string[] missing = Enum.GetValues<Signet.Core.BookManipulation.CleanupStep>()
            .SelectMany(step => CleanupStepParts.Select(part => $"Cleanup_{step}_{part}"))
            .Where(key => !known.Contains(key))
            .ToArray();

        // Assert
        missing.Should().BeEmpty("every step of the Cleanup dialog names itself, explains itself and reports what it found");
    }

    [Fact]
    public void Every_loc_key_used_in_axaml_exists_in_the_resources()
    {
        // A misspelled key renders as empty text (e.g. a blank OK button), with no build error.
        // Arrange
        ResourceSet? polish = Strings.RawManager.GetResourceSet(new System.Globalization.CultureInfo("pl"), true, true);
        HashSet<string> keys = polish!.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
        Regex locKey = new(@"\{loc:Loc\s+(?:Key=)?(?<key>[\w.]+)\s*\}");

        List<string> missing = new();

        // Act
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

        // Assert
        missing.Should().BeEmpty("every loc:Loc key used in a view must exist in Strings.resx");
    }

    [Fact]
    public void No_axaml_view_has_a_hardcoded_user_facing_string_literal()
    {
        // Arrange
        List<string> violations = new();

        // Act
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

        // Assert
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
