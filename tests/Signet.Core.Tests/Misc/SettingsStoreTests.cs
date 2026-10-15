using System.Collections.Generic;
using System.IO;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Search;
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
        new(dir.Combine("settings.xml"));

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
        sut.PreserveEntityCodeNames.Should().ContainSingle()
            .Which.Should().Be(((ushort)160, "&#160;"));
    }

    [Fact]
    public void Values_survive_save_and_reload()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");

        SettingsStore writer = new(path);
        writer.DefaultMetadataLang = "pl";
        writer.SpellCheck = true;
        writer.CleanOn = CleanOn.Save;
        writer.ZoomPreview = 1.25f;
        writer.RecentFiles = new[] { "a.epub", "b.epub" };
        writer.ClipboardHistory = new[] { "newest", "older" };
        writer.FavoriteSpecialCharacters = new[] { "—", "§" };
        writer.CleanupEnabledSteps = new[] { CleanupStep.UnusedSelectors, CleanupStep.UnusedMedia };
        writer.StandardizationEnabledSteps = new[] { StandardizationStep.RebaseManifestIds };
        writer.WarningAppearance = new WarningAppearance(16);
        writer.EditionPage = new EditionPageSettings(
            true, EditionPagePosition.Second, new[] { new EditionPageField("Edited by", "GreatWorksPublishing", true), new EditionPageField("Note", "x", false) },
            false, true, false, true, false, "Colophon", 3);
        writer.OpenTagHintAppearance = new OpenTagHintAppearance("Consolas", 15);
        writer.Save();

        File.Exists(path).Should().BeTrue();

        SettingsStore reader = new(path);
        reader.DefaultMetadataLang.Should().Be("pl");
        reader.SpellCheck.Should().BeTrue();
        reader.CleanOn.Should().Be(CleanOn.Save);
        reader.ZoomPreview.Should().Be(1.25f);
        reader.RecentFiles.Should().Equal("a.epub", "b.epub");
        reader.ClipboardHistory.Should().Equal("newest", "older");
        reader.FavoriteSpecialCharacters.Should().Equal("—", "§");
        reader.CleanupEnabledSteps.Should().Equal(CleanupStep.UnusedSelectors, CleanupStep.UnusedMedia);
        reader.StandardizationEnabledSteps.Should().Equal(StandardizationStep.RebaseManifestIds);
        reader.WarningAppearance.Should().Be(new WarningAppearance(16));
        reader.EditionPage.Should().Be(new EditionPageSettings(
            true, EditionPagePosition.Second, new[] { new EditionPageField("Edited by", "GreatWorksPublishing", true), new EditionPageField("Note", "x", false) },
            false, true, false, true, false, "Colophon", 3));
        reader.OpenTagHintAppearance.Should().Be(new OpenTagHintAppearance("Consolas", 15));
    }

    [Fact]
    public void Empty_file_yields_defaults()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        File.WriteAllText(path, string.Empty);

        SettingsStore sut = new(path);

        sut.DefaultVersion.Should().Be("3.0");
        sut.CodeEditorWordWrap.Should().BeTrue("Code Editor wraps lines by default");
        sut.CleanupEnabledSteps.Should().BeEmpty("no Cleanup step is checked until the user confirms one");
        sut.StandardizationEnabledSteps.Should().Equal(EpubStandardization.AllSteps, "every step is checked until the dialog is applied");
        sut.WarningAppearance.Should().Be(WarningAppearance.Default, "warnings are as large as the interface text by default");
        sut.EditionPage.Should().Be(EditionPageSettings.Default, "the edition page is off until the user turns it on");
        sut.CodeEditorOpenTagHint.Should().BeTrue("the opening tag hint is on by default");
        sut.CodeEditorOpenTagHintDelayMs.Should().Be(500);
        sut.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default, "the hint uses the editor font");
    }

    [Fact]
    public void Open_tag_hint_appearance_is_normalized()
    {
        using TempDir dir = new();
        SettingsStore sut = new(dir.Combine("settings.xml"));

        sut.OpenTagHintAppearance = new OpenTagHintAppearance(" Consolas ", 500);

        sut.OpenTagHintAppearance.Should().Be(new OpenTagHintAppearance("Consolas", OpenTagHintAppearance.FontSizeMax));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(750, 750)]
    [InlineData(99999, SettingsStore.OpenTagHintDelayMaxMs)]
    public void Open_tag_hint_delay_is_clamped(int stored, int expected)
    {
        using TempDir dir = new();
        SettingsStore sut = new(dir.Combine("settings.xml"));

        sut.CodeEditorOpenTagHintDelayMs = stored;

        sut.CodeEditorOpenTagHintDelayMs.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(3, WarningAppearance.FontSizeMin)]
    [InlineData(99, WarningAppearance.FontSizeMax)]
    [InlineData(18, 18)]
    public void Warning_font_size_is_normalized_and_0_means_the_interface_text_size(int stored, int expected)
    {
        using TempDir dir = new();
        SettingsStore sut = new(dir.Combine("settings.xml"));

        sut.WarningAppearance = new WarningAppearance(stored);

        sut.WarningAppearance.Should().Be(new WarningAppearance(expected));
    }

    [Fact]
    public void The_last_preferences_page_survives_a_restart_and_is_never_negative()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore writer = new(path);
        writer.PreferencesPageIndex.Should().Be(0);

        writer.PreferencesPageIndex = 5;
        writer.Save();
        new SettingsStore(path).PreferencesPageIndex.Should().Be(5);

        writer.PreferencesPageIndex = -3;
        writer.PreferencesPageIndex.Should().Be(0);
    }

    [Fact]
    public void The_find_scope_is_all_html_files_without_settings_or_with_an_invalid_value_and_survives_a_restart()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore writer = new(path);
        writer.GetFindReplaceSettings().LookWhere.Should().Be(LookWhere.AllHtmlFiles, "a fresh installation");

        writer.SaveFindReplaceSettings(writer.GetFindReplaceSettings() with { LookWhere = (LookWhere)99 });
        writer.GetFindReplaceSettings().LookWhere.Should().Be(LookWhere.AllHtmlFiles, "a value that is no scope would leave the list empty");

        writer.SaveFindReplaceSettings(writer.GetFindReplaceSettings() with { LookWhere = LookWhere.OpfFile });
        writer.Save();
        new SettingsStore(path).GetFindReplaceSettings().LookWhere.Should().Be(LookWhere.OpfFile);
    }

    [Fact]
    public void The_collapsed_preferences_sections_survive_a_restart_and_start_empty()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore writer = new(path);
        writer.PreferencesCollapsedSections.Should().BeEmpty("every section is expanded by default");

        writer.PreferencesCollapsedSections = new[] { "AppearancePage", "ColorSchemePage", "AppearancePage" };
        writer.Save();

        new SettingsStore(path).PreferencesCollapsedSections.Should().Equal("AppearancePage", "ColorSchemePage");
    }

    [Fact]
    public void The_font_of_the_color_scheme_previews_is_monospaced_by_default_and_survives_a_restart()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore writer = new(path);
        writer.ColorSchemePreviewAppearance.Should().Be(new ColorSchemePreviewAppearance("Consolas", 13));
        Fixture any = new();
        ColorSchemePreviewAppearance font = new(any.Create<string>(), any.Create<int>());

        writer.ColorSchemePreviewAppearance = font;
        writer.Save();

        new SettingsStore(path).ColorSchemePreviewAppearance.Should().Be(font);
    }

    [Fact]
    public void The_preferences_splitter_sizes_survive_a_restart_without_the_ones_that_are_not_positive()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore writer = new(path);
        writer.PreferencesSplitterSizes.Should().BeEmpty();

        writer.PreferencesSplitterSizes = new Dictionary<string, int> { ["ThemeCssView.tree"] = 280, ["ThemeCssView.preview"] = 0 };
        writer.Save();

        new SettingsStore(path).PreferencesSplitterSizes.Should().BeEquivalentTo(new Dictionary<string, int> { ["ThemeCssView.tree"] = 280 });
        new SettingsStore(path).PreferencesFieldHeights.Should().BeEmpty("the field heights are a separate map");
    }

    [Fact]
    public void The_keys_of_the_removed_dark_theme_are_dropped_on_load_and_not_saved_again()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        File.WriteAllText(path, """
            <settings>
              <group name="user_preferences">
                <string key="ui_theme">Dark</string>
                <bool key="ui_custom_signet_dark_theme">true</bool>
                <number key="preview_dark_in_dm">1</number>
                <string key="warning_color_light">#102030</string>
                <string key="warning_color_dark">#405060</string>
                <string key="error_color_dark">#405060</string>
                <string key="preview_highlight_color_dark">#405060</string>
                <string key="open_tag_hint_dark_background">#405060</string>
                <string key="open_tag_hint_dark_foreground">#405060</string>
                <string key="cv_dark_css_comment_color">#405060</string>
                <number key="cv_dark_font_size">14</number>
              </group>
            </settings>
            """);

        SettingsStore sut = new(path);
        sut.LegacyColors.Should().ContainKey("warning_color_light", "the light color goes into a theme");
        sut.Save();

        string saved = File.ReadAllText(path);
        saved.Should().NotContain("ui_theme").And.NotContain("dark").And.NotContain("warning_color");
    }

    [Fact]
    public void The_unused_code_editor_colors_are_dropped_on_load()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        File.WriteAllText(path, """
            <settings>
              <group name="user_preferences">
                <string key="code_editor_css_comment_color">#102030</string>
                <string key="code_editor_line_number_background_color">#405060</string>
                <string key="code_editor_line_number_foreground_color">#405060</string>
                <string key="code_editor_spelling_underline_color">#405060</string>
              </group>
            </settings>
            """);

        SettingsStore sut = new(path);
        sut.LegacyColors.Keys.Should().Equal("code_editor_css_comment_color");
        sut.Save();

        string saved = File.ReadAllText(path);
        saved.Should().NotContain("line_number").And.NotContain("spelling_underline").And.NotContain("css_comment");
    }

    [Fact]
    public void A_tooltip_font_is_stored_per_area_normalized_and_reset_with_the_appearance()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        SettingsStore sut = new(path);
        string family = new Fixture().Create<string>();

        sut.GetTooltipFont(TooltipArea.FindReplace).Should().Be(new TooltipFont(string.Empty, 13), "the interface font at 13 px");

        sut.SetTooltipFont(TooltipArea.FindReplace, new TooltipFont(" " + family + " ", 99));
        sut.Save();

        sut.GetTooltipFont(TooltipArea.FindReplace).Should().Be(new TooltipFont(family, TooltipFont.FontSizeMax));
        sut.GetTooltipFont(TooltipArea.Footnotes).Should().Be(TooltipFont.Default, "another area");
        File.ReadAllText(path).Should().Contain("tooltip_find_replace_font_family").And.Contain("tooltip_find_replace_font_size");

        sut.ClearAppearanceSettings();

        sut.GetTooltipFont(TooltipArea.FindReplace).Should().Be(TooltipFont.Default);
    }

    [Fact]
    public void Corrupt_file_is_tolerated_and_yields_defaults()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        File.WriteAllText(path, "<settings><group name=\"user_preferences\"><string key=");

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
    public void PreviewHighlight_defaults_to_a_background_45_percent_persistent()
    {
        using TempDir dir = new();

        PreviewHighlight sut = NewStore(dir).PreviewHighlight;

        sut.Should().Be(PreviewHighlight.Default);
        sut.Style.Should().Be(PreviewHighlightStyle.Background);
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
        PreviewHighlight value = new(PreviewHighlightStyle.Outline, 60, 7, true, 4500);
        List<string> changed = new();
        sut.SettingChanged += (_, e) => changed.Add(e.QualifiedKey);

        sut.PreviewHighlight = value;

        sut.PreviewHighlight.Should().Be(value);
        sut.Save();
        NewStore(dir).PreviewHighlight.Should().Be(value, "the value goes to the settings file");
        changed.Should().Equal("user_preferences/preview_highlight");
    }

    [Fact]
    public void PreviewHighlight_out_of_range_values_are_clamped()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);

        sut.PreviewHighlight = new((PreviewHighlightStyle)42, 0, 99, true, 10);

        sut.PreviewHighlight.Should().Be(new PreviewHighlight(
            PreviewHighlightStyle.Background,
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
        string path = dir.Combine("settings.xml");

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
        string path = dir.Combine("settings.xml");

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
        SettingsStore store = new(dir.Combine("settings.xml"));
        WindowGeometry geometry = new(0, 0, 900, 640, Maximized: false, FullScreen: false);

        store.SetWindowGeometry("PreferencesWindow", geometry);

        store.GetWindowGeometry("PreferencesWindow").Should().Be(geometry);
    }

    [Fact]
    public void Appearance_structs_roundtrip_and_reset()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");

        SettingsStore writer = new(path);
        writer.CodeEditorAppearance.Should().Be(CodeEditorAppearance.Default);
        writer.CodeEditorAppearance = CodeEditorAppearance.Default with { FontSize = 14, FontFamily = "Consolas" };
        writer.PreviewAppearance = PreviewAppearance.Default with { FontSize = 20 };
        writer.Save();

        SettingsStore reader = new(path);
        reader.CodeEditorAppearance.FontSize.Should().Be(14);
        reader.CodeEditorAppearance.FontFamily.Should().Be("Consolas");
        reader.PreviewAppearance.FontSize.Should().Be(20);

        reader.ClearAppearanceSettings();
        reader.CodeEditorAppearance.Should().Be(CodeEditorAppearance.Default);
        reader.PreviewAppearance.Should().Be(PreviewAppearance.Default);
    }

    [Fact]
    public void Extended_highlighting_is_on_by_default_and_roundtrips()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");

        SettingsStore writer = new(path);
        writer.CodeEditorExtendedHighlighting.Should().BeTrue();
        writer.CodeEditorExtendedHighlighting = false;
        writer.Save();

        SettingsStore reader = new(path);
        reader.CodeEditorExtendedHighlighting.Should().BeFalse();
    }

    [Fact]
    public void String_map_group_roundtrips_and_clears()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");

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

    [Fact]
    public void A_draft_reads_the_current_values_and_keeps_its_changes_to_itself()
    {
        using TempDir dir = new();
        string path = dir.Combine("settings.xml");
        Fixture fixture = new();
        string original = fixture.Create<string>();
        string changed = fixture.Create<string>();
        SettingsStore sut = new(path);
        sut.DefaultMetadataLang = original;
        sut.Save();

        SettingsStore draft = sut.CreateDraft();
        draft.DefaultMetadataLang.Should().Be(original);
        draft.DefaultMetadataLang = changed;
        draft.Save();

        draft.IsDraft.Should().BeTrue();
        sut.DefaultMetadataLang.Should().Be(original);
        new SettingsStore(path).DefaultMetadataLang.Should().Be(original, "a draft never writes the file");
        sut.HasDraftChanges(draft).Should().BeTrue();
    }

    [Fact]
    public void Committing_a_draft_copies_only_its_changes_and_raises_SettingChanged()
    {
        using TempDir dir = new();
        Fixture fixture = new();
        string meanwhile = fixture.Create<string>();
        SettingsStore sut = NewStore(dir);
        SettingsStore draft = sut.CreateDraft();
        List<string> raised = new();
        sut.SettingChanged += (_, e) => raised.Add(e.QualifiedKey);

        draft.SpellCheck = true;
        draft.UiIconTheme = "fluent";
        sut.DefaultMetadataLang = meanwhile;
        raised.Clear();

        IReadOnlyList<string> committed = sut.CommitDraft(draft);

        committed.Should().BeEquivalentTo("user_preferences/spell_check", "user_preferences/ui_icon_theme");
        raised.Should().BeEquivalentTo(committed);
        sut.SpellCheck.Should().BeTrue();
        sut.UiIconTheme.Should().Be("fluent");
        sut.DefaultMetadataLang.Should().Be(meanwhile, "the draft did not change it");
        sut.HasDraftChanges(draft).Should().BeFalse();
    }

    [Fact]
    public void Committing_a_composite_setting_raises_its_own_key_too()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);
        SettingsStore draft = sut.CreateDraft();
        List<string> raised = new();
        sut.SettingChanged += (_, e) => raised.Add(e.QualifiedKey);

        draft.PreviewAppearance = draft.PreviewAppearance with { FontSize = draft.PreviewAppearance.FontSize + 1 };
        sut.CommitDraft(draft);

        raised.Should().Contain("user_preferences/preview_appearance");
    }

    [Fact]
    public void A_change_reverted_in_the_draft_is_not_a_change()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);
        sut.UiIconTheme = "main";
        SettingsStore draft = sut.CreateDraft();
        string original = draft.UiIconTheme;

        draft.UiIconTheme = "fluent";
        draft.UiIconTheme = original;

        sut.HasDraftChanges(draft).Should().BeFalse();
        sut.CommitDraft(draft).Should().BeEmpty();
    }

    [Fact]
    public void A_draft_can_be_committed_again_with_later_changes_only()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);
        SettingsStore draft = sut.CreateDraft();
        draft.SpellCheck = true;
        sut.CommitDraft(draft);

        draft.SpellCheckNumbers = !draft.SpellCheckNumbers;

        sut.CommitDraft(draft).Should().Equal("user_preferences/spell_check_numbers");
    }

    [Fact]
    public void After_a_rebase_earlier_draft_writes_are_not_changes()
    {
        using TempDir dir = new();
        SettingsStore sut = NewStore(dir);
        SettingsStore draft = sut.CreateDraft();
        draft.SpellCheck = !draft.SpellCheck;

        draft.RebaseDraft();

        sut.HasDraftChanges(draft).Should().BeFalse();
        sut.CommitDraft(draft).Should().BeEmpty();
    }

    [Fact]
    public void A_draft_cannot_make_a_draft()
    {
        using TempDir dir = new();
        SettingsStore draft = NewStore(dir).CreateDraft();

        FluentActions.Invoking(() => draft.CreateDraft()).Should().Throw<System.InvalidOperationException>();
    }
}
