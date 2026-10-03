using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Input;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of the action catalog: uniqueness, the absence of excluded features, parseability of the shortcuts.</summary>
public sealed class AppActionCatalogTests
{
    [Fact]
    public void Action_ids_are_unique()
    {
        AppActionCatalog.All.Select(a => a.Id).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("Plugin")]
    [InlineData("Automate")]
    // Checkpoints (git: ManageRepo/RepoLog/CPCompare) remain out of scope; the checkpoints
    // are a deliberate addition.
    [InlineData("ManageRepo")]
    [InlineData("Index")]
    [InlineData("XEditor")]
    public void Excluded_features_are_absent(string fragment)
    {
        AppActionCatalog.All.Should().NotContain(a => a.Id.Contains(fragment));
    }

    [Fact]
    public void Every_default_shortcut_parses_or_is_empty()
    {
        foreach (AppActionDescriptor descriptor in AppActionCatalog.All)
        {
            if (descriptor.DefaultShortcut.Length == 0)
            {
                continue;
            }

            KeyGestureConversion.TryParse(descriptor.DefaultShortcut, out _)
                .Should().BeTrue($"the shortcut '{descriptor.DefaultShortcut}' of action {descriptor.Id} should parse");
        }
    }

    [Fact]
    public void Registry_builds_all_catalog_actions_and_wires_gestures()
    {
        TestHost host = new();

        host.Registry.Actions.Should().HaveCount(AppActionCatalog.All.Count);
        host.Registry.Require(AppActionIds.Save).Gesture!.ToString().Should().Contain("Ctrl");
        host.Registry.Require(AppActionIds.SaveACopy).Gesture.Should().BeNull();
    }

    /// <summary>
    /// Regression: on Windows AltGr is reported as Ctrl+Alt, so a Ctrl+Alt+&lt;letter&gt; shortcut
    /// intercepts the character typed with AltGr+&lt;letter&gt; and it cannot be
    /// typed in Code View or in text fields. Ctrl+Alt+A/C/L/N blocked the
    /// Polish ą ć ł ń (uppercase worked, because AltGr+Shift is a different combination).
    /// </summary>
    [Fact]
    public void No_default_shortcut_blocks_a_polish_AltGr_character()
    {
        // Letters that produce a character through AltGr in the "Polish (programmer)" layout.
        const string altGrLetters = "ACELNOSXZ";

        string[] offending = AppActionCatalog.All
            .Where(a => a.DefaultShortcut.StartsWith("Ctrl+Alt+", System.StringComparison.Ordinal))
            .Where(a =>
            {
                string key = a.DefaultShortcut["Ctrl+Alt+".Length..];
                return key.Length == 1 && altGrLetters.Contains(key[0], System.StringComparison.Ordinal);
            })
            .Select(a => $"{a.Id} = {a.DefaultShortcut}")
            .ToArray();

        offending.Should().BeEmpty("a Ctrl+Alt+<letter> shortcut from this list blocks typing a Polish character");
    }
}
