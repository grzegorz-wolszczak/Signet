using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of the action catalog: uniqueness, the absence of excluded features, parseability of the shortcuts.</summary>
public sealed class AppActionCatalogTests
{
    [Fact]
    public void Action_ids_are_unique()
    {
        // Act & Assert
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
        // Act & Assert
        AppActionCatalog.All.Should().NotContain(a => a.Id.Contains(fragment));
    }

    [Theory]
    [InlineData("")]
    [InlineData("en")]
    public void Every_action_has_a_tooltip_in_both_languages(string culture)
    {
        // Arrange
        CultureInfo language = CultureInfo.GetCultureInfo(culture);
        System.Collections.Generic.List<string> withoutTooltip = new();

        // Act
        foreach (AppActionDescriptor descriptor in AppActionCatalog.All)
        {
            string key = AppActionIds.TryGetClipSlot(descriptor.Id, out _)
                ? "ActionTip_ClipSlot"
                : "ActionTip_" + descriptor.Id.Replace('.', '_');
            if (string.IsNullOrWhiteSpace(Strings.RawManager.GetString(key, language)))
            {
                withoutTooltip.Add(descriptor.Id);
            }
        }

        // Assert
        withoutTooltip.Should().BeEmpty("every action needs a tooltip explaining what it does");
    }

    [Fact]
    public void Every_tooltip_belongs_to_an_action()
    {
        // Arrange
        System.Collections.Generic.HashSet<string> keys = AppActionCatalog.All
            .Select(a => "ActionTip_" + a.Id.Replace('.', '_'))
            .ToHashSet(System.StringComparer.Ordinal);
        keys.Add("ActionTip_ClipSlot");
        System.Collections.Generic.List<string> orphans = new();

        // Act
        foreach (System.Collections.DictionaryEntry entry in Strings.RawManager
                     .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
                     .Cast<System.Collections.DictionaryEntry>())
        {
            if (entry.Key is string key && key.StartsWith("ActionTip_", System.StringComparison.Ordinal) && !keys.Contains(key))
            {
                orphans.Add(key);
            }
        }

        // Assert
        orphans.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AppActionIds.SplitSection)]
    [InlineData(AppActionIds.SplitTag)]
    [InlineData(AppActionIds.BookBrowserSplit)]
    public void Split_actions_have_no_default_shortcut(string id)
    {
        // Act & Assert
        AppActionCatalog.All.Single(a => a.Id == id).DefaultShortcut.Should().BeEmpty();
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

            // Act & Assert
            KeyGestureConversion.TryParse(descriptor.DefaultShortcut, out _)
                .Should().BeTrue($"the shortcut '{descriptor.DefaultShortcut}' of action {descriptor.Id} should parse");
        }
    }

    [Fact]
    public void Registry_builds_all_catalog_actions_and_wires_gestures()
    {
        // Arrange
        using TestHost host = new();

        // Act & Assert
        host.Registry.Actions.Should().HaveCount(AppActionCatalog.All.Count);

        // Act & Assert
        host.Registry.Require(AppActionIds.Save).Gesture!.ToString().Should().Contain("Ctrl");

        // Act & Assert
        host.Registry.Require(AppActionIds.SaveACopy).Gesture.Should().BeNull();
    }

    /// <summary>
    /// Regression: on Windows AltGr is reported as Ctrl+Alt, so a Ctrl+Alt+&lt;letter&gt; shortcut
    /// intercepts the character typed with AltGr+&lt;letter&gt; and it cannot be
    /// typed in Code Editor or in text fields. Ctrl+Alt+A/C/L/N blocked the
    /// Polish ą ć ł ń (uppercase worked, because AltGr+Shift is a different combination).
    /// </summary>
    [Fact]
    public void No_default_shortcut_blocks_a_polish_AltGr_character()
    {
        // Arrange
        // Letters that produce a character through AltGr in the "Polish (programmer)" layout.
        const string altGrLetters = "ACELNOSXZ";

        // Act
        string[] offending = AppActionCatalog.All
            .Where(a => a.DefaultShortcut.StartsWith("Ctrl+Alt+", System.StringComparison.Ordinal))
            .Where(a =>
            {
                string key = a.DefaultShortcut["Ctrl+Alt+".Length..];
                return key.Length == 1 && altGrLetters.Contains(key[0], System.StringComparison.Ordinal);
            })
            .Select(a => $"{a.Id} = {a.DefaultShortcut}")
            .ToArray();

        // Assert
        offending.Should().BeEmpty("a Ctrl+Alt+<letter> shortcut from this list blocks typing a Polish character");
    }
}
