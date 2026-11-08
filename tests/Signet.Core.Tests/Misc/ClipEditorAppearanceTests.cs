using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>The fonts of the Clip Editor window and the Clips panel (Preferences › Appearance › Clips › Fonts).</summary>
public sealed class ClipEditorAppearanceTests
{
    [Fact]
    public void By_default_only_the_text_of_a_clip_has_a_font_of_its_own()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        ClipEditorAppearance look = new SettingsStore(dir.Combine("settings.xml")).ClipEditorAppearance;

        // Assert
        look.Should().Be(ClipEditorAppearance.Default);
        look.TextFontFamily.Should().Be(ClipEditorAppearance.DefaultTextFontFamily);
        look.HeaderFontFamily.Should().BeEmpty();
        look.EntriesFontFamily.Should().BeEmpty();
        look.OtherFontFamily.Should().BeEmpty();
    }

    [Fact]
    public void The_fonts_are_kept_and_normalized()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        ClipEditorAppearance look = ClipEditorAppearance.Default with
        {
            HeaderFontFamily = "  Arial  ",
            HeaderFontSize = 400,
            EntriesFontFamily = "Consolas",
            EntriesFontSize = 14,
            OtherFontSize = -3,
        };

        // Act
        settings.ClipEditorAppearance = look;
        ClipEditorAppearance read = settings.ClipEditorAppearance;

        // Assert
        read.HeaderFontFamily.Should().Be("Arial");
        read.HeaderFontSize.Should().Be(ClipEditorAppearance.FontSizeMax);
        read.EntriesFontFamily.Should().Be("Consolas");
        read.EntriesFontSize.Should().Be(14);
        read.OtherFontSize.Should().Be(0);
    }

    [Fact]
    public void Appearance_defaults_restore_the_clip_editor_fonts()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        settings.ClipEditorAppearance = ClipEditorAppearance.Default with { EntriesFontFamily = "Consolas", TextFontFamily = "Courier New" };

        // Act
        settings.ClearAppearanceSettings();

        // Assert
        settings.ClipEditorAppearance.Should().Be(ClipEditorAppearance.Default);
    }
}
