using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>
/// Tests of <see cref="SettingsStore"/> — default values, saving/loading, tolerance of an
/// empty/corrupted file, the <see cref="SettingsStore.SettingChanged"/> event, geometry,
/// appearance structures.
/// </summary>
public sealed class SettingsStoreTests
{
    private static SettingsStore NewStore(TempDir dir) =>
        new(dir.Combine("settings.json"));

    [Fact]
    public void Defaults_apply_when_file_is_missing()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);

        sut.UiLanguage.Should().Be("en", "the UI starts in English regardless of the system language");
        sut.DefaultMetadataLang.Should().Be("en");
        sut.DefaultVersion.Should().Be("3.0");
        sut.Dictionary.Should().Be("en_US");
        sut.SpellCheck.Should().BeFalse();
        sut.CleanOn.Should().Be(CleanOn.Open | CleanOn.Save);
        sut.ZoomText.Should().Be(1.0f);
        sut.MainMenuIconSize.Should().Be(1.8);
        sut.ClipboardHistoryLimit.Should().Be(20);
        sut.RecentFilesLimit.Should().Be(9);
        sut.CssEpub3ValidationSpec.Should().Be("css30");
        sut.HighlightOpenCloseTags.Should().BeTrue();
        sut.ThemePreference.Should().Be(ThemePreference.System);
        sut.PreserveEntityCodeNames.Should().ContainSingle()
            .Which.Should().Be(((ushort)160, "&#160;"));
    }

    [Fact]
    public void Values_survive_save_and_reload()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore writer = new(path);
        writer.DefaultMetadataLang = "pl";
        writer.SpellCheck = true;
        writer.CleanOn = CleanOn.Save;
        writer.ZoomPreview = 1.25f;
        writer.ThemePreference = ThemePreference.Dark;
        writer.RecentFiles = new[] { "a.epub", "b.epub" };
        writer.ClipboardHistory = new[] { "newest", "older" };
        writer.FavoriteSpecialCharacters = new[] { "—", "§" };
        writer.CleanupEnabledSteps = new[] { CleanupStep.UnusedSelectors, CleanupStep.UnusedMedia };
        writer.Save();

        File.Exists(path).Should().BeTrue();

        SettingsStore reader = new(path);
        reader.DefaultMetadataLang.Should().Be("pl");
        reader.SpellCheck.Should().BeTrue();
        reader.CleanOn.Should().Be(CleanOn.Save);
        reader.ZoomPreview.Should().Be(1.25f);
        reader.ThemePreference.Should().Be(ThemePreference.Dark);
        reader.RecentFiles.Should().Equal("a.epub", "b.epub");
        reader.ClipboardHistory.Should().Equal("newest", "older");
        reader.FavoriteSpecialCharacters.Should().Equal("—", "§");
        reader.CleanupEnabledSteps.Should().Equal(CleanupStep.UnusedSelectors, CleanupStep.UnusedMedia);
    }

    [Fact]
    public void Empty_file_yields_defaults()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");
        File.WriteAllText(path, string.Empty);

        SettingsStore sut = new(path);

        sut.DefaultVersion.Should().Be("3.0");
        sut.CodeViewWordWrap.Should().BeTrue("Code View wraps lines by default");
        sut.CleanupEnabledSteps.Should().BeEmpty("no Cleanup step is checked until the user confirms one");
    }

    [Fact]
    public void Corrupt_file_is_tolerated_and_yields_defaults()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");
        File.WriteAllText(path, "{ this is not valid json ][");

        SettingsStore sut = new(path);

        sut.DefaultVersion.Should().Be("3.0");
        sut.UiIconTheme.Should().Be("main");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 20)]
    [InlineData(-1, 9)]
    [InlineData(21, 9)]
    public void RecentFilesLimit_outside_0_to_20_falls_back_to_9(int stored, int expected)
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);

        sut.RecentFilesLimit = stored;

        sut.RecentFilesLimit.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(350, 350)]
    [InlineData(10000, 10000)]
    [InlineData(99, 1000)]
    [InlineData(10001, 1000)]
    public void UiPreviewTimeout_outside_100_to_10000_falls_back_to_1000(int stored, int expected)
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);

        sut.UiPreviewTimeout = stored;

        sut.UiPreviewTimeout.Should().Be(expected);
    }

    [Fact]
    public void UiPreviewTimeout_defaults_to_1000()
    {
        using TempDir dir = new();

        NewStore(dir).UiPreviewTimeout.Should().Be(1000);
    }

    [Fact]
    public void PreviewHighlight_defaults_to_yellow_background_45_percent_persistent()
    {
        using TempDir dir = new();

        PreviewHighlight sut = NewStore(dir).PreviewHighlight;

        sut.Should().Be(PreviewHighlight.Default);
        sut.Style.Should().Be(PreviewHighlightStyle.Background);
        sut.LightColor.Should().Be("#ffeb3b");
        sut.DarkColor.Should().Be("#ffeb3b");
        sut.OpacityPercent.Should().Be(45);
        sut.OutlineWidth.Should().Be(3);
        sut.AutoHide.Should().BeFalse();
        sut.AutoHideDelayMs.Should().Be(2000);
    }

    [Fact]
    public void PreviewHighlight_round_trips_and_raises_one_change()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);
        PreviewHighlight value = new(PreviewHighlightStyle.Outline, "#112233", "#AABBCC", 60, 7, true, 4500);
        List<string> changed = new();
        sut.SettingChanged += (_, e) => changed.Add(e.QualifiedKey);

        sut.PreviewHighlight = value;

        sut.PreviewHighlight.Should().Be(value);
        sut.Save();
        NewStore(dir).PreviewHighlight.Should().Be(value, "the value goes to the settings file");
        changed.Should().Equal("user_preferences/preview_highlight");
    }

    [Fact]
    public void PreviewHighlight_out_of_range_values_are_clamped_and_bad_colors_fall_back()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);

        sut.PreviewHighlight = new((PreviewHighlightStyle)42, "red", "#12345", 0, 99, true, 10);

        sut.PreviewHighlight.Should().Be(new PreviewHighlight(
            PreviewHighlightStyle.Background, "#ffeb3b", "#ffeb3b",
            PreviewHighlight.OpacityMin, PreviewHighlight.OutlineWidthMax, true, PreviewHighlight.AutoHideDelayMin));
    }

    [Fact]
    public void SettingChanged_fires_with_qualified_key()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);
        string? changed = null;
        sut.SettingChanged += (_, e) => changed = e.QualifiedKey;

        sut.DefaultMetadataLang = "de";

        changed.Should().Be("user_preferences/default_metadata_lang");
    }

    [Fact]
    public void Reload_discards_unsaved_changes()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore sut = new(path);
        sut.DefaultMetadataLang = "fr";
        sut.Save();
        sut.DefaultMetadataLang = "es";

        sut.Reload();

        sut.DefaultMetadataLang.Should().Be("fr");
    }

    [Fact]
    public void Window_geometry_roundtrips()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore writer = new(path);
        writer.GetWindowGeometry("MainWindow").Should().BeNull();
        writer.SetWindowGeometry("MainWindow", new WindowGeometry(10, 20, 1200, 800, Maximized: true));
        writer.Save();

        SettingsStore reader = new(path);
        reader.GetWindowGeometry("MainWindow").Should().Be(new WindowGeometry(10, 20, 1200, 800, Maximized: true));
    }

    [Fact]
    public void Window_geometry_can_be_read_back_from_the_same_store_after_setting_it()
    {
        // Regression: the values set in this session are int JSON nodes; reading them as double threw
        // (opening Preferences a second time crashed the app).
        using TempDir dir = new();
        SettingsStore store = new(dir.Combine("settings.json"));
        WindowGeometry geometry = new(0, 0, 900, 640, Maximized: false, FullScreen: false);

        store.SetWindowGeometry("PreferencesWindow", geometry);

        store.GetWindowGeometry("PreferencesWindow").Should().Be(geometry);
    }

    [Fact]
    public void Appearance_structs_roundtrip_and_reset()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore writer = new(path);
        writer.CodeViewAppearance.Should().Be(CodeViewAppearance.LightDefault);
        writer.CodeViewAppearance = CodeViewAppearance.LightDefault with { FontSize = 14, CssSelectorColor = "#123456" };
        writer.PreviewAppearance = PreviewAppearance.Default with { FontSize = 20 };
        writer.Save();

        SettingsStore reader = new(path);
        reader.CodeViewAppearance.FontSize.Should().Be(14);
        reader.CodeViewAppearance.CssSelectorColor.Should().Be("#123456");
        reader.PreviewAppearance.FontSize.Should().Be(20);
        reader.CodeViewDarkAppearance.Should().Be(CodeViewAppearance.DarkDefault);

        reader.ClearAppearanceSettings();
        reader.CodeViewAppearance.Should().Be(CodeViewAppearance.LightDefault);
        reader.PreviewAppearance.Should().Be(PreviewAppearance.Default);
    }

    [Fact]
    public void Extended_highlighting_is_on_by_default_and_its_colours_roundtrip()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore writer = new(path);
        writer.CodeViewExtendedHighlighting.Should().BeTrue();
        writer.CodeViewExtendedHighlighting = false;
        writer.CodeViewDarkAppearance = CodeViewAppearance.DarkDefault with { LinkColor = "#010203", ErrorUnderlineColor = "#040506" };
        writer.Save();

        SettingsStore reader = new(path);
        reader.CodeViewExtendedHighlighting.Should().BeFalse();
        reader.CodeViewDarkAppearance.LinkColor.Should().Be("#010203");
        reader.CodeViewDarkAppearance.ErrorUnderlineColor.Should().Be("#040506");
        reader.CodeViewDarkAppearance.CssConstantColor.Should().Be(CodeViewAppearance.DarkDefault.CssConstantColor);

        reader.ClearAppearanceSettings();
        reader.CodeViewDarkAppearance.Should().Be(CodeViewAppearance.DarkDefault);
    }

    [Fact]
    public void Search_match_and_selection_colors_roundtrip_per_theme()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore writer = new(path);
        writer.CodeViewAppearance = CodeViewAppearance.LightDefault with { SearchMatchBackgroundColor = "#111111", SelectionBackgroundColor = "#222222" };
        writer.CodeViewDarkAppearance = CodeViewAppearance.DarkDefault with { SearchMatchBackgroundColor = "#333333", SelectionBackgroundColor = "#444444" };
        writer.Save();

        SettingsStore reader = new(path);
        reader.CodeViewAppearance.SearchMatchBackgroundColor.Should().Be("#111111");
        reader.CodeViewAppearance.SelectionBackgroundColor.Should().Be("#222222");
        reader.CodeViewDarkAppearance.SearchMatchBackgroundColor.Should().Be("#333333");
        reader.CodeViewDarkAppearance.SelectionBackgroundColor.Should().Be("#444444");

        reader.ClearAppearanceSettings();
        reader.CodeViewAppearance.Should().Be(CodeViewAppearance.LightDefault);
        reader.CodeViewDarkAppearance.Should().Be(CodeViewAppearance.DarkDefault);
    }

    [Fact]
    public void String_map_group_roundtrips_and_clears()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.json");

        SettingsStore writer = new(path);
        writer.GetStringMap("keyboard_shortcuts").Should().BeEmpty();
        writer.SetStringMap("keyboard_shortcuts", new Dictionary<string, string>
        {
            ["MainWindow.Save"] = "Ctrl+S",
            ["MainWindow.Open"] = "Ctrl+O",
        });
        writer.Save();

        SettingsStore reader = new(path);
        reader.GetStringMap("keyboard_shortcuts").Should().ContainKey("MainWindow.Save")
            .WhoseValue.Should().Be("Ctrl+S");

        reader.SetStringMap("keyboard_shortcuts", new Dictionary<string, string>());
        reader.Save();
        new SettingsStore(path).GetStringMap("keyboard_shortcuts").Should().BeEmpty();
    }

    [Fact]
    public void TempFolderHome_returns_empty_when_stored_path_is_missing()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);

        sut.TempFolderHome = Path.Combine(dir.Path, "does-not-exist");
        sut.TempFolderHome.Should().BeEmpty();

        sut.TempFolderHome = dir.Path;
        sut.TempFolderHome.Should().Be(dir.Path);
    }
}
