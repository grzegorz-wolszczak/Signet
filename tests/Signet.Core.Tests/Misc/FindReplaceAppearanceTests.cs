using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>The font of the Find and Replace fields (Preferences › Appearance › Find and Replace › Fonts).</summary>
public sealed class FindReplaceAppearanceTests
{
    [Fact]
    public void By_default_the_fields_use_the_interface_font()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        FindReplaceAppearance look = new SettingsStore(dir.Combine("settings.xml")).FindReplaceAppearance;

        // Assert
        look.Should().Be(FindReplaceAppearance.Default);
        look.FieldFontFamily.Should().BeEmpty();
    }

    [Fact]
    public void The_font_is_kept_normalized_and_announced_and_cleared_with_the_appearance()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        int changes = 0;
        settings.SettingChanged += (_, e) =>
        {
            if (e.QualifiedKey.Contains("find_replace_appearance", System.StringComparison.Ordinal))
            {
                changes++;
            }
        };

        // Act
        settings.FindReplaceAppearance = new FindReplaceAppearance("  Consolas ", 400, 0);

        // Assert
        settings.FindReplaceAppearance.Should().Be(new FindReplaceAppearance("Consolas", FindReplaceAppearance.FontSizeMax, 0));
        changes.Should().Be(1);

        // Act
        settings.ClearAppearanceSettings();

        // Assert
        settings.FindReplaceAppearance.Should().Be(FindReplaceAppearance.Default);
    }
}
