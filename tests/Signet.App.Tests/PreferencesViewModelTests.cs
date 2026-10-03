using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="PreferencesViewModel"/>: every change is written to <see cref="SettingsStore"/>
/// immediately, and the shortcut editor detects conflicts.
/// </summary>
public sealed class PreferencesViewModelTests
{
    private static (TestHost Host, PreferencesViewModel Vm) New()
    {
        TestHost host = new();
        return (host, NewOn(host));
    }

    private static PreferencesViewModel NewOn(TestHost host)
    {
        ThemeManager theme = new(host.Settings, NullLogger<ThemeManager>.Instance);
        LocalizationManager localization = new(host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(host.Settings, NullLogger<IconThemeManager>.Instance);
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-" + Guid.NewGuid().ToString("N"));
        SpellChecker spellChecker = new(host.Settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        UiDensityManager uiDensity = new(
            host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        return new PreferencesViewModel(host.Settings, spellChecker, theme, localization, iconTheme, host.Shortcuts, host.Registry, uiDensity);
    }

    [Fact]
    public void Preview_highlight_fields_start_from_settings()
    {
        using TestHost host = new();
        PreviewHighlight stored = new(PreviewHighlightStyle.Outline, "#102030", "#405060", 30, 4, true, 1500);
        host.Settings.PreviewHighlight = stored;

        PreferencesViewModel sut = NewOn(host);

        sut.PreviewHighlightStyle.Should().Be(PreviewHighlightStyle.Outline);
        sut.IsPreviewHighlightOutline.Should().BeTrue();
        sut.IsPreviewHighlightBackground.Should().BeFalse();
        sut.PreviewHighlightLightColor.Value.Should().Be("#102030");
        sut.PreviewHighlightDarkColor.Value.Should().Be("#405060");
        sut.PreviewHighlightOpacity.Should().Be(30);
        sut.PreviewHighlightOutlineWidth.Should().Be(4);
        sut.PreviewHighlightAutoHide.Should().BeTrue();
        sut.PreviewHighlightAutoHideDelay.Should().Be(1500);
    }

    [Fact]
    public void Preview_highlight_changes_persist_immediately_and_clamped()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.PreviewHighlightStyle = PreviewHighlightStyle.Outline;
        vm.PreviewHighlightOutlineWidth = 50;
        vm.PreviewHighlightOpacity = 70;
        vm.PreviewHighlightAutoHide = true;
        vm.PreviewHighlightAutoHideDelay = 100;
        vm.PreviewHighlightLightColor.Value = "#00ff00";
        vm.PreviewHighlightDarkColor.Value = "#0000ff";

        host.Settings.PreviewHighlight.Should().Be(new PreviewHighlight(
            PreviewHighlightStyle.Outline, "#00ff00", "#0000ff", 70,
            PreviewHighlight.OutlineWidthMax, true, PreviewHighlight.AutoHideDelayMin));
    }

    [Fact]
    public void Preview_highlight_color_rows_have_localized_labels()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.PreviewHighlightLightColor.Label.Should().Be(Strings.Get("PreferencesWindow_PreviewHighlightColorLight"));
        vm.PreviewHighlightDarkColor.Label.Should().Be(Strings.Get("PreferencesWindow_PreviewHighlightColorDark"));
    }

    [Theory]
    [InlineData(350, 350)]
    [InlineData(50, 100)]
    [InlineData(20000, 10000)]
    public void PreviewRefreshDelay_change_persists_clamped_to_100_10000(int entered, int expected)
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.PreviewRefreshDelay.Should().Be(host.Settings.UiPreviewTimeout);

        vm.PreviewRefreshDelay = entered;

        host.Settings.UiPreviewTimeout.Should().Be(expected);
    }

    [Fact]
    public void SelectedLanguage_change_persists_via_LocalizationManager_but_does_not_apply_live()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string other = vm.SelectedLanguage.Code == "en" ? "pl" : "en";

        vm.SelectedLanguage = PreferencesViewModel.AvailableLanguages.Single(l => l.Code == other);

        host.Settings.UiLanguage.Should().Be(other);
    }

    [Fact]
    public void SelectedIconTheme_change_persists_via_IconThemeManager_but_does_not_apply_live()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string other = vm.SelectedIconTheme.Code == "material" ? "fluent" : "material";

        vm.SelectedIconTheme = PreferencesViewModel.AvailableIconThemes.Single(t => t.Code == other);

        host.Settings.UiIconTheme.Should().Be(other);
    }

    [Fact]
    public void SelectedTheme_change_applies_immediately_via_ThemeManager()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.SelectedTheme = ThemePreference.Dark;

        host.Settings.ThemePreference.Should().Be(ThemePreference.Dark);
    }

    [Fact]
    public void Preview_font_changes_persist_immediately()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.PreviewFontStandard = "Georgia";
        vm.PreviewFontSize = 20;

        host.Settings.PreviewAppearance.FontFamilyStandard.Should().Be("Georgia");
        host.Settings.PreviewAppearance.FontSize.Should().Be(20);
    }

    [Fact]
    public async Task Choose_color_OK_sets_and_persists_the_picked_color_for_the_right_theme()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        (string Title, string Initial)? asked = null;
        vm.ColorPicker = (title, initial) =>
        {
            asked = (title, initial);
            return Task.FromResult<string?>("#123456");
        };
        ColorSettingRow row = vm.CodeViewDark.Colors.First(c => c.Key == "Search Match Background");

        await row.ChooseColorCommand.ExecuteAsync(null);

        asked.Should().Be((row.Label, CodeViewAppearance.DarkDefault.SearchMatchBackgroundColor));
        row.Value.Should().Be("#123456");
        host.Settings.CodeViewDarkAppearance.SearchMatchBackgroundColor.Should().Be("#123456");
        host.Settings.CodeViewAppearance.SearchMatchBackgroundColor.Should().Be(CodeViewAppearance.LightDefault.SearchMatchBackgroundColor);
    }

    [Fact]
    public async Task Choose_color_Cancel_keeps_the_current_color()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.ColorPicker = (_, _) => Task.FromResult<string?>(null);
        ColorSettingRow row = vm.CodeViewLight.Colors.First(c => c.Key == "Selection Background");

        await row.ChooseColorCommand.ExecuteAsync(null);

        row.Value.Should().Be(CodeViewAppearance.LightDefault.SelectionBackgroundColor);
        host.Settings.CodeViewAppearance.SelectionBackgroundColor.Should().Be(CodeViewAppearance.LightDefault.SelectionBackgroundColor);
    }

    [Fact]
    public void CodeViewLight_color_edit_persists_immediately()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        ColorSettingRow row = vm.CodeViewLight.Colors.First(c => c.Key == "CSS Comment");
        row.Value = "#123456";

        host.Settings.CodeViewAppearance.CssCommentColor.Should().Be("#123456");
    }

    [Fact]
    public void RestoreAppearanceDefaultsCommand_resets_fonts_and_colors()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.PreviewFontStandard = "Georgia";
        vm.CodeViewDark.Colors.First(c => c.Key == "CSS Comment").Value = "#000000";

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        vm.PreviewFontStandard.Should().Be(PreviewAppearance.Default.FontFamilyStandard);
        vm.CodeViewDark.Colors.First(c => c.Key == "CSS Comment").Value.Should().Be(CodeViewAppearance.DarkDefault.CssCommentColor);
    }

    [Fact]
    public void General_mend_checkboxes_persist_as_CleanOn_flags()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.MendOnOpen = true;
        vm.MendOnSave = false;

        host.Settings.CleanOn.Should().Be(CleanOn.Open);
    }

    [Fact]
    public void DefaultVersionIsEpub3_persists_as_version_string()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.DefaultVersionIsEpub3 = true;
        host.Settings.DefaultVersion.Should().Be("3.0");

        vm.DefaultVersionIsEpub3 = false;
        host.Settings.DefaultVersion.Should().Be("2.0");
    }

    [Fact]
    public void SpellCheckEnabled_toggle_persists_immediately()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.SpellCheckEnabled = true;

        host.Settings.SpellCheck.Should().BeTrue();
    }

    [Fact]
    public void PreserveEntities_add_edit_and_remove_persist_immediately()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        int before = vm.PreserveEntities.Count;

        vm.AddPreserveEntityCommand.Execute(null);
        PreserveEntityRow row = vm.PreserveEntities.Last();
        row.CodeText = "8212";
        row.Name = "&#8212;";

        host.Settings.PreserveEntityCodeNames.Should().Contain(p => p.Code == 8212 && p.Name == "&#8212;");

        row.RemoveCommand.Execute(null);

        vm.PreserveEntities.Should().HaveCount(before);
        host.Settings.PreserveEntityCodeNames.Should().NotContain(p => p.Code == 8212);
    }

    [Fact]
    public void TrySetGesture_rejects_a_shortcut_used_by_another_action_and_names_it()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        ShortcutRow open = vm.Shortcuts.Single(r => r.Id == AppActionIds.Open);
        string saveBefore = save.GestureText;

        bool accepted = save.TrySetGesture(host.Shortcuts.Get(AppActionIds.Open)!.KeyGesture);

        accepted.Should().BeFalse();
        save.GestureText.Should().Be(saveBefore);
        save.EditorMessage.Should().Contain(open.Label);
    }

    [Fact]
    public void TrySetGesture_applies_a_free_shortcut_and_switches_to_custom()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);

        save.TrySetGesture(new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Alt)).Should().BeTrue();

        save.GestureText.Should().Be("Ctrl+Alt+K");
        save.IsCustom.Should().BeTrue();
        host.Shortcuts.Get(AppActionIds.Save)!.IsOverridden.Should().BeTrue();
    }

    [Fact]
    public void Choosing_the_default_option_restores_the_default_shortcut()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        save.TrySetGesture(new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Alt));

        save.IsDefaultChoice = true;

        save.GestureText.Should().Be("Ctrl+S");
        host.Shortcuts.Get(AppActionIds.Save)!.IsOverridden.Should().BeFalse();
    }

    [Fact]
    public void ClearGesture_removes_the_shortcut()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        save.IsCustom = true;

        save.ClearGestureCommand.Execute(null);

        host.Shortcuts.Get(AppActionIds.Save)!.KeyGesture.Should().BeNull();
        save.GestureText.Should().BeEmpty();
    }

    [Fact]
    public void Capture_is_ignored_until_custom_is_chosen_and_completes_with_the_pressed_gesture()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);

        save.BeginCapture();
        save.IsCapturing.Should().BeFalse();

        save.IsCustom = true;
        save.BeginCapture();
        save.IsCapturing.Should().BeTrue();
        save.CompleteCapture(new KeyGesture(Key.F9, KeyModifiers.Shift));

        save.IsCapturing.Should().BeFalse();
        save.GestureText.Should().Be("Shift+F9");
    }

    [Fact]
    public void Opening_one_editor_closes_the_previously_open_one()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        ShortcutRow open = vm.Shortcuts.Single(r => r.Id == AppActionIds.Open);

        save.ToggleEditorCommand.Execute(null);
        open.ToggleEditorCommand.Execute(null);

        save.IsExpanded.Should().BeFalse();
        open.IsExpanded.Should().BeTrue();

        open.ToggleEditorCommand.Execute(null);
        open.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void ShortcutGroups_group_every_row_by_category()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.ShortcutGroups.SelectMany(g => g.Rows).Should().HaveCount(vm.Shortcuts.Count);
        vm.ShortcutGroups.Should().OnlyContain(g => g.Rows.All(r => r.Category == g.Category));
    }

    [Fact]
    public void ShortcutFilter_matches_by_label_and_by_key_combination_ignoring_case_and_spaces()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);

        vm.ShortcutFilter = "ctrl + s";

        save.IsVisible.Should().BeTrue();
        vm.Shortcuts.Where(r => r.IsVisible).Should().OnlyContain(r =>
            r.GestureText.Contains("Ctrl+S", StringComparison.OrdinalIgnoreCase)
            || r.Label.Contains("ctrl + s", StringComparison.OrdinalIgnoreCase)
            || r.Category.Contains("ctrl + s", StringComparison.OrdinalIgnoreCase));
        vm.ShortcutGroups.Single(g => g.Rows.Contains(save)).IsExpanded.Should().BeTrue();

        vm.ShortcutFilter = save.Label.ToUpperInvariant();
        save.IsVisible.Should().BeTrue();

        vm.ShortcutFilter = string.Empty;
        vm.Shortcuts.Should().OnlyContain(r => r.IsVisible);
        vm.ShortcutGroups.Should().OnlyContain(g => g.IsVisible);
    }

    [Fact]
    public void Filter_hides_groups_without_matching_rows()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.ShortcutFilter = "zzz-no-such-command-zzz";

        vm.Shortcuts.Should().OnlyContain(r => !r.IsVisible);
        vm.ShortcutGroups.Should().OnlyContain(g => !g.IsVisible);
    }

    [Fact]
    public void Conflicting_rows_are_marked_and_can_be_filtered()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        ShortcutRow open = vm.Shortcuts.Single(r => r.Id == AppActionIds.Open);
        // A conflict arises only by reverting to a default shortcut that another action has taken
        // in the meantime (manually assigning a shortcut that is in use is rejected).
        save.TrySetGesture(new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Alt));
        open.TrySetGesture(new KeyGesture(Key.S, KeyModifiers.Control));
        save.ResetToDefaultCommand.Execute(null);

        save.HasConflict.Should().BeTrue();
        save.ConflictText.Should().Contain(open.Label);
        open.ConflictText.Should().Contain(save.Label);

        vm.ShowOnlyShortcutConflicts = true;

        vm.Shortcuts.Where(r => r.IsVisible).Should().OnlyContain(r => r.HasConflict);
        save.IsVisible.Should().BeTrue();
        open.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void ResetToDefault_clears_a_previously_applied_override()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        save.TrySetGesture(new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Alt));
        host.Shortcuts.Get(AppActionIds.Save)!.IsOverridden.Should().BeTrue();

        save.ResetToDefaultCommand.Execute(null);

        host.Shortcuts.Get(AppActionIds.Save)!.IsOverridden.Should().BeFalse();
        save.IsCustom.Should().BeFalse();
    }

    [Fact]
    public void ResetAllShortcutsCommand_reverts_every_action_to_default()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        save.TrySetGesture(new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Alt));

        vm.ResetAllShortcutsCommand.Execute(null);

        host.Shortcuts.Get(AppActionIds.Save)!.IsOverridden.Should().BeFalse();
        save.GestureText.Should().Be("Ctrl+S");
    }

    /// <summary>
    /// Regression: Clip Bar slot rows must have distinguishable labels. The shortcut editor used to
    /// read the static Descriptor.Text (empty for slots), so all 60 rows looked identical and only
    /// the "Clip" category was visible.
    /// </summary>
    [Fact]
    public void Clip_slot_rows_are_labelled_with_their_slot_number()
    {
        using UiCultureScope culture = new("en");
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.Shortcuts.Single(r => r.Id == AppActionIds.Clip(1)).Label.Should().Be("Clip 1");
        vm.Shortcuts.Single(r => r.Id == AppActionIds.Clip(60)).Label.Should().Be("Clip 60");

        vm.Shortcuts.Where(r => r.Category == "Clip")
            .Select(r => r.Label)
            .Should().OnlyHaveUniqueItems("every slot must be distinguishable in the shortcut editor");
    }

    /// <summary>
    /// Regression: the conflict showed the shortcut via Avalonia's KeyGesture.ToString() ("Ctrl+D0"),
    /// while the editor uses Qt-style syntax ("Ctrl+0").
    /// </summary>
    [Fact]
    public void Conflict_message_shows_the_shortcut_in_qt_syntax()
    {
        using UiCultureScope culture = new("en");
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Known default conflict: Zoom Reset and Clip 60 both use Ctrl+0.
        vm.Conflicts.Should().Contain(c => c.EndsWith("Ctrl+0", StringComparison.Ordinal));
        vm.Conflicts.Should().NotContain(c => c.Contains("D0", StringComparison.Ordinal));
        ShortcutRow zoomReset = vm.Shortcuts.Single(r => r.Id == AppActionIds.ZoomReset);
        zoomReset.HasConflict.Should().BeTrue();
        zoomReset.ConflictText.Should().Contain("Clip 60");
    }

    [Fact]
    public async Task ChooseSpecialCharacterFont_applies_family_and_size_from_the_picker()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Georgia", 22));
        };

        await vm.ChooseSpecialCharacterFontCommand.ExecuteAsync(null);

        asked!.Size.Should().NotBeNull();
        asked.MonospaceOnly.Should().BeFalse();
        vm.SpecialCharacterFontFamily.Should().Be("Georgia");
        vm.SpecialCharacterFontSize.Should().Be(22);
        host.Settings.SpecialCharacterAppearance.Should().Be(new SpecialCharacterAppearance("Georgia", 22));
    }

    [Fact]
    public async Task ChoosePreviewFontSerif_changes_only_the_family_and_keeps_the_shared_size()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        int sizeBefore = vm.PreviewFontSize;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Georgia", 99));
        };

        await vm.ChoosePreviewFontSerifCommand.ExecuteAsync(null);

        asked!.Size.Should().BeNull();
        vm.PreviewFontSerif.Should().Be("Georgia");
        vm.PreviewFontSize.Should().Be(sizeBefore);
        host.Settings.PreviewAppearance.FontFamilySerif.Should().Be("Georgia");
    }

    [Fact]
    public async Task Code_view_font_picker_starts_monospace_only_and_applies_family_and_size()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Consolas", 14));
        };

        await vm.CodeViewDark.ChooseFontCommand.ExecuteAsync(null);

        asked!.MonospaceOnly.Should().BeTrue();
        host.Settings.CodeViewDarkAppearance.FontFamily.Should().Be("Consolas");
        host.Settings.CodeViewDarkAppearance.FontSize.Should().Be(14);
    }

    [Fact]
    public async Task Cancelled_font_picker_leaves_the_font_unchanged()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string before = vm.CodeViewLight.FontFamily;
        vm.FontPicker = _ => Task.FromResult<FontPickResult?>(null);

        await vm.CodeViewLight.ChooseFontCommand.ExecuteAsync(null);

        vm.CodeViewLight.FontFamily.Should().Be(before);
    }

    [Fact]
    public async Task ChooseUiFont_stores_family_and_size_and_ResetUiFont_restores_the_mode_default()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.CompactUi = true;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Tahoma", 13));
        };

        await vm.ChooseUiFontCommand.ExecuteAsync(null);

        asked!.Size.Should().Be(12, "the default size of the compact mode");
        asked.Family.Should().Be("Segoe UI", "the window starts with the font actually in use, so that OK is enabled");
        (host.Settings.UiFont, host.Settings.UiFontSize).Should().Be(("Tahoma", 13));
        vm.UiFontDescription.Should().Be("Tahoma, 13 px");

        vm.ResetUiFontCommand.Execute(null);

        (host.Settings.UiFont, host.Settings.UiFontSize).Should().Be((string.Empty, 0));
        vm.UiFontDescription.Should().Contain("Segoe UI").And.Contain("12");
    }

    [Fact]
    public void Standard_mode_default_UI_font_falls_back_to_the_system_font_when_Inter_is_not_installed()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.CompactUi = false;

        vm.EffectiveUiFontFamily.Should().Be("Segoe UI");
        vm.UiFontDescription.Should().Contain("14");
    }
}
