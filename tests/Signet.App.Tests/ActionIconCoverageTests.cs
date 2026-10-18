using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The icon keys of the action catalog against the three built-in icon sets. An action pointing at a key a set does not
/// define would show no icon (or a bare label on a toolbar) in that set.
/// </summary>
public sealed partial class ActionIconCoverageTests
{
    private static readonly string[] IconSets = { "MainIcons.axaml", "FluentIcons.axaml", "MaterialIcons.axaml" };

    [GeneratedRegex("x:Key=\"(?<key>[^\"]+)\"")]
    private static partial Regex KeyRegex();

    private static HashSet<string> KeysOf(string file) =>
        KeyRegex().Matches(File.ReadAllText(Path.Combine(LocalizationTests.AppSourceRoot, "Assets", "Icons", file)))
            .Select(m => m.Groups["key"].Value)
            .ToHashSet();

    [Fact]
    public void Every_icon_key_of_the_catalog_is_defined_in_all_three_icon_sets()
    {
        // The colored PNGs (ColoredIconKeys) are shared by all sets and are not in the dictionaries.
        List<string> keys = AppActionCatalog.All
            .Select(a => a.IconKey)
            .OfType<string>()
            .Where(k => !IconThemeManager.ColoredIconKeys.Contains(k))
            .Distinct()
            .ToList();
        keys.Should().NotBeEmpty();

        foreach (string file in IconSets)
        {
            HashSet<string> defined = KeysOf(file);
            keys.Where(k => !defined.Contains(k)).Should().BeEmpty($"{file} must define every icon key the catalog uses");
        }
    }

    [Fact]
    public void The_three_icon_sets_define_the_same_keys()
    {
        HashSet<string> main = KeysOf("MainIcons.axaml");

        KeysOf("FluentIcons.axaml").Should().BeEquivalentTo(main);
        KeysOf("MaterialIcons.axaml").Should().BeEquivalentTo(main);
    }

    [Fact]
    public void Every_colored_icon_key_has_its_png()
    {
        foreach (string key in IconThemeManager.ColoredIconKeys)
        {
            File.Exists(Path.Combine(LocalizationTests.AppSourceRoot, "Assets", "Icons", "Calibre", key + ".png"))
                .Should().BeTrue($"{key}.png must exist next to the other colored icons");
        }
    }
}
