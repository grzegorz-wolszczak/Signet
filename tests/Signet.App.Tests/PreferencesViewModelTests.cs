using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoFixture;
using AwesomeAssertions;
using Avalonia.Input;
using Signet.App.Actions;
using Signet.App.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="PreferencesViewModel"/>: changes are written to <see cref="SettingsStore"/> only when
/// applied (Save / Apply), and the keyboard shortcuts page is the keymap of all actions.
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
    public void Changes_stay_in_the_window_until_applied()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        ThemePreference themeBefore = host.Settings.ThemePreference;
        bool spellCheckBefore = host.Settings.SpellCheck;
        int applied = 0;
        vm.Applied += (_, _) => applied++;
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();

        vm.SelectedTheme = themeBefore == ThemePreference.Dark ? ThemePreference.Light : ThemePreference.Dark;
        vm.SpellCheckEnabled = !spellCheckBefore;

        host.Settings.ThemePreference.Should().Be(themeBefore, "nothing is written before Save / Apply");
        host.Settings.SpellCheck.Should().Be(spellCheckBefore);
        vm.HasChanges.Should().BeTrue();
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();

        vm.ApplyCommand.Execute(null);

        host.Settings.ThemePreference.Should().Be(vm.SelectedTheme);
        host.Settings.SpellCheck.Should().Be(!spellCheckBefore);
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        applied.Should().Be(1);
    }

    [Fact]
    public void A_window_closed_without_applying_changes_nothing()
    {
        using TestHost host = new();
        string folderBefore = host.Settings.UiCustomIconFolder;
        PreferencesViewModel cancelled = NewOn(host);

        cancelled.CustomIconFolder = new Fixture().Create<string>();
        cancelled.PreviewRefreshDelay = SettingsStore.UiPreviewTimeoutMax;

        host.Settings.UiCustomIconFolder.Should().Be(folderBefore);
        NewOn(host).CustomIconFolder.Should().Be(folderBefore, "the next window starts from the saved settings");
    }

    [Fact]
    public void Keymap_changes_wait_for_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        KeymapNode open = vm.Keymap.ActionNodes.Single(n => n.ActionId == AppActionIds.Open);
        KeyStrokeShortcut added = new(new KeyGesture(Key.F12, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift));
        List<string> changed = new();
        host.Shortcuts.ShortcutChanged += (_, id) => changed.Add(id);

        vm.Keymap.AddShortcut(open, added, removeConflicts: false);

        open.Shortcuts.Should().Contain(added, "the keymap page shows the change at once");
        host.Shortcuts.Get(AppActionIds.Open)!.Shortcuts.Should().NotContain(added);
        changed.Should().BeEmpty();
        vm.HasChanges.Should().BeTrue();

        vm.ApplyCommand.Execute(null);

        host.Shortcuts.Get(AppActionIds.Open)!.Shortcuts.Should().Contain(added);
        changed.Should().Equal(AppActionIds.Open);
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
    public void Warning_fields_start_from_settings_and_changes_persist_on_apply()
    {
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16, "#102030", "#405060");
        PreferencesViewModel sut = NewOn(host);

        sut.WarningFontSize.Should().Be(16);
        sut.WarningLightColor.Value.Should().Be("#102030");
        sut.WarningDarkColor.Value.Should().Be("#405060");

        sut.WarningFontSize = 0;
        sut.WarningLightColor.Value = "#00ff00";
        sut.WarningDarkColor.Value = "#0000ff";
        sut.ApplyCommand.Execute(null);

        host.Settings.WarningAppearance.Should().Be(new WarningAppearance(0, "#00ff00", "#0000ff"));
    }

    [Fact]
    public void Open_tag_hint_fields_start_from_settings_and_changes_persist_on_apply()
    {
        using TestHost host = new();
        host.Settings.CodeViewOpenTagHint = false;
        host.Settings.CodeViewOpenTagHintDelayMs = 800;
        PreferencesViewModel sut = NewOn(host);

        sut.OpenTagHint.Should().BeFalse();
        sut.OpenTagHintDelay.Should().Be(800);

        sut.OpenTagHint = true;
        sut.OpenTagHintDelay = 250;
        sut.ApplyCommand.Execute(null);

        host.Settings.CodeViewOpenTagHint.Should().BeTrue();
        host.Settings.CodeViewOpenTagHintDelayMs.Should().Be(250);
    }

    [Fact]
    public void Open_tag_hint_look_starts_from_settings_persists_and_resets()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        host.Settings.OpenTagHintAppearance = new OpenTagHintAppearance("Consolas", 15, "#111111", "#222222", "#333333", "#444444");
        PreferencesViewModel sut = NewOn(host);

        sut.OpenTagHintFontDescription.Should().Be("Consolas, 15 px");
        sut.OpenTagHintLightBackground.Value.Should().Be("#111111");
        sut.OpenTagHintDarkForeground.Value.Should().Be("#444444");

        sut.OpenTagHintDarkBackground.Value = "#abcdef";
        sut.ResetOpenTagHintFontCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        host.Settings.OpenTagHintAppearance.Should().Be(new OpenTagHintAppearance(string.Empty, 0, "#111111", "#222222", "#abcdef", "#444444"));
        sut.OpenTagHintFontDescription.Should().Be(Strings.Get("PreferencesWindow_OpenTagHintFontDefault"));

        sut.RestoreAppearanceDefaultsCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        host.Settings.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default);
        sut.OpenTagHintLightBackground.Value.Should().Be(OpenTagHintAppearance.Default.LightBackground);
    }

    [Fact]
    public void Restoring_the_appearance_defaults_resets_the_warnings()
    {
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16, "#102030", "#405060");
        PreferencesViewModel sut = NewOn(host);

        sut.RestoreAppearanceDefaultsCommand.Execute(null);

        sut.WarningFontSize.Should().Be(0);
        sut.WarningLightColor.Value.Should().Be(WarningAppearance.Default.LightColor);
        sut.WarningDarkColor.Value.Should().Be(WarningAppearance.Default.DarkColor);
        sut.ApplyCommand.Execute(null);
        host.Settings.WarningAppearance.Should().Be(WarningAppearance.Default);
    }

    [Fact]
    public void Preview_highlight_changes_persist_on_apply_and_clamped()
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
        vm.ApplyCommand.Execute(null);

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
        vm.ApplyCommand.Execute(null);

        host.Settings.UiPreviewTimeout.Should().Be(expected);
    }

    [Fact]
    public void SelectedLanguage_change_persists_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string other = vm.SelectedLanguage.Code == "en" ? "pl" : "en";

        vm.SelectedLanguage = PreferencesViewModel.AvailableLanguages.Single(l => l.Code == other);
        vm.ApplyCommand.Execute(null);

        host.Settings.UiLanguage.Should().Be(other);
    }

    [Fact]
    public void SelectedIconTheme_change_persists_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string other = vm.SelectedIconTheme.Code == "material" ? "fluent" : "material";

        vm.SelectedIconTheme = PreferencesViewModel.AvailableIconThemes.Single(t => t.Code == other);
        vm.ApplyCommand.Execute(null);

        host.Settings.UiIconTheme.Should().Be(other);
    }

    [Fact]
    public void SelectedTheme_change_persists_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.SelectedTheme = ThemePreference.Dark;
        vm.ApplyCommand.Execute(null);

        host.Settings.ThemePreference.Should().Be(ThemePreference.Dark);
    }

    [Fact]
    public void Preview_font_changes_persist_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.PreviewFontStandard = "Georgia";
        vm.PreviewFontSize = 20;
        vm.ApplyCommand.Execute(null);

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
        vm.ApplyCommand.Execute(null);
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
    public void CodeViewLight_color_edit_persists_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        ColorSettingRow row = vm.CodeViewLight.Colors.First(c => c.Key == "CSS Comment");
        row.Value = "#123456";
        vm.ApplyCommand.Execute(null);

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
        vm.ApplyCommand.Execute(null);

        host.Settings.CleanOn.Should().Be(CleanOn.Open);
    }

    [Fact]
    public void DefaultVersionIsEpub3_persists_as_version_string()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.DefaultVersionIsEpub3 = true;
        vm.ApplyCommand.Execute(null);
        host.Settings.DefaultVersion.Should().Be("3.0");

        vm.DefaultVersionIsEpub3 = false;
        vm.ApplyCommand.Execute(null);
        host.Settings.DefaultVersion.Should().Be("2.0");
    }

    [Fact]
    public void SpellCheckEnabled_toggle_persists_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.SpellCheckEnabled = true;
        vm.ApplyCommand.Execute(null);

        host.Settings.SpellCheck.Should().BeTrue();
    }

    [Fact]
    public void PreserveEntities_add_edit_and_remove_persist_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        int before = vm.PreserveEntities.Count;

        vm.AddPreserveEntityCommand.Execute(null);
        PreserveEntityRow row = vm.PreserveEntities.Last();
        row.CodeText = "8212";
        row.Name = "&#8212;";
        vm.ApplyCommand.Execute(null);

        host.Settings.PreserveEntityCodeNames.Should().Contain(p => p.Code == 8212 && p.Name == "&#8212;");

        row.RemoveCommand.Execute(null);

        vm.PreserveEntities.Should().HaveCount(before);
        vm.ApplyCommand.Execute(null);
        host.Settings.PreserveEntityCodeNames.Should().NotContain(p => p.Code == 8212);
    }

    [Fact]
    public void The_keyboard_shortcuts_page_is_the_keymap_of_all_actions()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.Keymap.ActionNodes.Select(n => n.ActionId).Should().BeEquivalentTo(host.Registry.Actions.Select(a => a.Id));
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
        vm.ApplyCommand.Execute(null);
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
        vm.ApplyCommand.Execute(null);
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
        vm.ApplyCommand.Execute(null);
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
        vm.ApplyCommand.Execute(null);
        (host.Settings.UiFont, host.Settings.UiFontSize).Should().Be(("Tahoma", 13));
        vm.UiFontDescription.Should().Be("Tahoma, 13 px");

        vm.ResetUiFontCommand.Execute(null);
        vm.ApplyCommand.Execute(null);

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
