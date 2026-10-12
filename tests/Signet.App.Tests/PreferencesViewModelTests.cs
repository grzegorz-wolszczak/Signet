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
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="PreferencesViewModel"/>: changes are written to <see cref="SettingsStore"/> only when
/// saved (Save), and the keyboard shortcuts page is the keymap of all actions.
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
        LocalizationManager localization = new(host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(host.Settings, NullLogger<IconThemeManager>.Instance);
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-" + Guid.NewGuid().ToString("N"));
        SpellChecker spellChecker = new(host.Settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        UiDensityManager uiDensity = new(
            host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        return new PreferencesViewModel(host.Settings, spellChecker, localization, iconTheme, host.Shortcuts, host.Registry, uiDensity);
    }

    [Fact]
    public void Changes_stay_in_the_window_until_applied()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string iconThemeBefore = host.Settings.UiIconTheme;
        bool spellCheckBefore = host.Settings.SpellCheck;
        int applied = 0;
        vm.Applied += (_, _) => applied++;
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();

        vm.SelectedIconTheme = PreferencesViewModel.AvailableIconThemes.First(t => t.Code != iconThemeBefore);
        vm.SpellCheckEnabled = !spellCheckBefore;

        host.Settings.UiIconTheme.Should().Be(iconThemeBefore, "nothing is written before Save");
        host.Settings.SpellCheck.Should().Be(spellCheckBefore);
        vm.HasChanges.Should().BeTrue();
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();

        vm.ApplyCommand.Execute(null);

        host.Settings.UiIconTheme.Should().Be(vm.SelectedIconTheme.Code);
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
        PreviewHighlight stored = new(PreviewHighlightStyle.Outline, "#102030", 30, 4, true, 1500);
        host.Settings.PreviewHighlight = stored;

        PreferencesViewModel sut = NewOn(host);

        sut.PreviewHighlightStyle.Should().Be(PreviewHighlightStyle.Outline);
        sut.IsPreviewHighlightOutline.Should().BeTrue();
        sut.IsPreviewHighlightBackground.Should().BeFalse();
        sut.PreviewHighlightColor.Value.Should().Be("#102030");
        sut.PreviewHighlightOpacity.Should().Be(30);
        sut.PreviewHighlightOutlineWidth.Should().Be(4);
        sut.PreviewHighlightAutoHide.Should().BeTrue();
        sut.PreviewHighlightAutoHideDelay.Should().Be(1500);
    }

    [Fact]
    public void Warning_fields_start_from_settings_and_changes_persist_on_apply()
    {
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16, "#102030");
        PreferencesViewModel sut = NewOn(host);

        sut.WarningFontSize.Should().Be(16);
        sut.WarningColor.Value.Should().Be("#102030");

        sut.WarningFontSize = 0;
        sut.WarningColor.Value = "#00ff00";
        sut.ApplyCommand.Execute(null);

        host.Settings.WarningAppearance.Should().Be(new WarningAppearance(0, "#00ff00"));
    }

    [Fact]
    public void Error_color_starts_from_settings_persists_on_apply_and_resets()
    {
        using TestHost host = new();
        host.Settings.ErrorAppearance = new ErrorAppearance("#102030");
        PreferencesViewModel sut = NewOn(host);

        sut.ErrorColor.Value.Should().Be("#102030");

        sut.ErrorColor.Value = "#00ff00";
        sut.ApplyCommand.Execute(null);
        host.Settings.ErrorAppearance.Should().Be(new ErrorAppearance("#00ff00"));

        sut.RestoreAppearanceDefaultsCommand.Execute(null);
        sut.ApplyCommand.Execute(null);
        host.Settings.ErrorAppearance.Should().Be(ErrorAppearance.Default);
        sut.ErrorColor.Value.Should().Be(ErrorAppearance.Default.Color);
    }

    private sealed class FakeEditionPageHost : IEditionPageHost
    {
        public bool HasOpenBook { get; set; } = true;

        public string? EditionPageBookPath { get; set; }

        public int RemoveCalls { get; private set; }

        public bool RemoveEditionPage()
        {
            RemoveCalls++;
            EditionPageBookPath = null;
            return true;
        }
    }

    [Fact]
    public void The_footnote_removal_confirmation_starts_from_settings_and_persists_on_apply()
    {
        using TestHost host = new();
        host.Settings.ConfirmFootnoteRemoval = false;
        PreferencesViewModel sut = NewOn(host);
        sut.ConfirmFootnoteRemoval.Should().BeFalse();

        sut.ConfirmFootnoteRemoval = true;
        sut.ApplyCommand.Execute(null);

        host.Settings.ConfirmFootnoteRemoval.Should().BeTrue();
    }

    [Fact]
    public void Footnote_styles_start_from_settings_and_changes_persist_on_apply()
    {
        using TestHost host = new();
        host.Settings.FootnoteStyles = Signet.Core.Footnotes.FootnoteStyles.Default with { Note = "color: red;" };
        PreferencesViewModel sut = NewOn(host);
        sut.FootnoteNoteStyle.Should().Be("color: red;");

        sut.FootnoteParagraphStyle.Should().Be("margin: 0;\ntext-indent: 0;");
        sut.FootnoteSeparatorStyle = "border: none;";
        sut.FootnoteReferenceStyle = "vertical-align: super;";
        sut.FootnoteParagraphStyle = "margin: 0 0 0.3em 0;";
        sut.FootnoteGroupTitleStyle = "font-weight: bold;";
        sut.FootnoteGroupByChapter.Should().BeFalse("grouping is off by default");
        sut.FootnoteGroupByChapter = true;
        sut.FootnoteReferenceStyleError.Should().BeEmpty();
        sut.ApplyCommand.Execute(null);

        host.Settings.FootnoteStyles.Should().Be(Signet.Core.Footnotes.FootnoteStyles.Default with
        {
            Note = "color: red;",
            Separator = "border: none;",
            Reference = "vertical-align: super;",
            Paragraph = "margin: 0 0 0.3em 0;",
            GroupTitle = "font-weight: bold;",
        });
        host.Settings.FootnoteGroupByChapter.Should().BeTrue();

        sut.ResetFootnoteStylesCommand.Execute(null);
        sut.ApplyCommand.Execute(null);
        host.Settings.FootnoteStyles.Should().Be(Signet.Core.Footnotes.FootnoteStyles.Default);
    }

    [Fact]
    public void An_invalid_footnote_style_shows_why_and_keeps_its_last_valid_definition()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        PreferencesViewModel sut = NewOn(host);

        sut.FootnoteNoteStyle = "margin: 0;";
        sut.FootnoteNoteStyle = "fnot-size: 1em;";

        sut.FootnoteNoteStyleError.Should().Be("Invalid style (it will not be saved): unknown property “fnot-size”");
        sut.FootnoteSeparatorStyleError.Should().BeEmpty();
        sut.FootnoteSeparatorStyle = "border: none;";
        sut.ApplyCommand.Execute(null);

        host.Settings.FootnoteStyles.Note.Should().Be("margin: 0;", "the last valid definition is saved");
        host.Settings.FootnoteStyles.Separator.Should().Be("border: none;", "the other fields are saved as usual");
        sut.FootnoteNoteStyle.Should().Be("fnot-size: 1em;", "the field keeps what was typed");
    }

    [Fact]
    public void Edition_page_fields_start_from_settings_and_changes_persist_on_apply()
    {
        using TestHost host = new();
        string value = new Fixture().Create<string>();
        host.Settings.EditionPage = EditionPageSettings.Default with
        {
            Enabled = true,
            Position = EditionPagePosition.Second,
            Fields = new[] { new EditionPageField("Edited by", value, true) },
            HeadingLevel = 2,
        };
        PreferencesViewModel sut = NewOn(host);

        sut.EditionPageEnabled.Should().BeTrue();
        sut.EditionPagePosition.Should().Be(EditionPagePosition.Second);
        sut.EditionPageHeadingLevel.Level.Should().Be(2);
        sut.EditionPageFields.Should().ContainSingle().Which.Value.Should().Be(value);

        sut.EditionPageFields[0].Show = false;
        sut.AddEditionPageFieldCommand.Execute(null);
        sut.EditionPageFields[1].Key = "Source";
        sut.EditionPageShowDate = false;
        sut.EditionPageShowVersion = false;
        sut.EditionPagePosition = EditionPagePosition.Last;
        host.Settings.EditionPage.Position.Should().Be(EditionPagePosition.Second, "nothing is written before Save");
        sut.ApplyCommand.Execute(null);

        EditionPageSettings saved = host.Settings.EditionPage;
        saved.Position.Should().Be(EditionPagePosition.Last);
        saved.ShowDate.Should().BeFalse();
        (saved.ShowProgram, saved.ShowVersion).Should().Be((true, false));
        saved.Fields.Should().Equal(new EditionPageField("Edited by", value, false), new EditionPageField("Source", string.Empty, true));

        sut.EditionPageFields[0].RemoveCommand.Execute(null);
        sut.ApplyCommand.Execute(null);
        host.Settings.EditionPage.Fields.Select(f => f.Key).Should().Equal("Source");
    }

    [Fact]
    public void Removing_the_edition_page_is_possible_only_when_the_open_book_has_one()
    {
        using TestHost host = new();
        PreferencesViewModel sut = NewOn(host);
        sut.RemoveEditionPageCommand.CanExecute(null).Should().BeFalse("no book is attached");
        sut.EditionPageStatus.Should().Be(Strings.Get("PreferencesWindow_EditionPageNoBook"));

        FakeEditionPageHost book = new() { EditionPageBookPath = "OEBPS/Text/signet_edition.xhtml" };
        sut.AttachEditionPageHost(book);

        sut.RemoveEditionPageCommand.CanExecute(null).Should().BeTrue();
        sut.EditionPageStatus.Should().Contain("signet_edition.xhtml");

        sut.RemoveEditionPageCommand.Execute(null);

        book.RemoveCalls.Should().Be(1);
        sut.RemoveEditionPageCommand.CanExecute(null).Should().BeFalse();
        sut.EditionPageStatus.Should().Be(Strings.Get("PreferencesWindow_EditionPageNotFound"));
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
        host.Settings.OpenTagHintAppearance = new OpenTagHintAppearance("Consolas", 15, "#111111", "#222222");
        PreferencesViewModel sut = NewOn(host);

        sut.OpenTagHintFontFamily.Should().Be("Consolas");
        sut.OpenTagHintFontSize.Should().Be(15);
        sut.OpenTagHintBackground.Value.Should().Be("#111111");
        sut.OpenTagHintForeground.Value.Should().Be("#222222");

        sut.OpenTagHintBackground.Value = "#abcdef";
        sut.ResetOpenTagHintFontCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        host.Settings.OpenTagHintAppearance.Should().Be(new OpenTagHintAppearance(string.Empty, 0, "#abcdef", "#222222"));
        sut.OpenTagHintFontFamily.Should().BeEmpty("\"Default\" is the editor font again");

        sut.RestoreAppearanceDefaultsCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        host.Settings.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default);
        sut.OpenTagHintBackground.Value.Should().Be(OpenTagHintAppearance.Default.Background);
    }

    [Fact]
    public void Restoring_the_appearance_defaults_resets_the_warnings()
    {
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16, "#102030");
        PreferencesViewModel sut = NewOn(host);

        sut.RestoreAppearanceDefaultsCommand.Execute(null);

        sut.WarningFontSize.Should().Be(0);
        sut.WarningColor.Value.Should().Be(WarningAppearance.Default.Color);
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
        vm.PreviewHighlightColor.Value = "#00ff00";
        vm.ApplyCommand.Execute(null);

        host.Settings.PreviewHighlight.Should().Be(new PreviewHighlight(
            PreviewHighlightStyle.Outline, "#00ff00", 70,
            PreviewHighlight.OutlineWidthMax, true, PreviewHighlight.AutoHideDelayMin));
    }

    [Fact]
    public void Preview_highlight_color_row_has_a_localized_label()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.PreviewHighlightColor.Label.Should().Be(Strings.Get("PreferencesWindow_PreviewHighlightColor"));
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
    public async Task Choose_color_OK_sets_and_persists_the_picked_color()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        (string Title, string Initial)? asked = null;
        vm.ColorPicker = (title, initial) =>
        {
            asked = (title, initial);
            return Task.FromResult<string?>("#123456");
        };
        ColorSettingRow row = vm.CodeView.Colors.First(c => c.Key == "Search Match Background");

        await row.ChooseColorCommand.ExecuteAsync(null);

        asked.Should().Be((row.Label, CodeViewAppearance.Default.SearchMatchBackgroundColor));
        row.Value.Should().Be("#123456");
        vm.ApplyCommand.Execute(null);
        host.Settings.CodeViewAppearance.SearchMatchBackgroundColor.Should().Be("#123456");
    }

    [Fact]
    public async Task Choose_color_Cancel_keeps_the_current_color()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.ColorPicker = (_, _) => Task.FromResult<string?>(null);
        ColorSettingRow row = vm.CodeView.Colors.First(c => c.Key == "Selection Background");

        await row.ChooseColorCommand.ExecuteAsync(null);

        row.Value.Should().Be(CodeViewAppearance.Default.SelectionBackgroundColor);
        host.Settings.CodeViewAppearance.SelectionBackgroundColor.Should().Be(CodeViewAppearance.Default.SelectionBackgroundColor);
    }

    [Fact]
    public void CodeView_color_edit_persists_on_apply()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        ColorSettingRow row = vm.CodeView.Colors.First(c => c.Key == "CSS Comment");
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
        vm.CodeView.Colors.First(c => c.Key == "CSS Comment").Value = "#000000";

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        vm.PreviewFontStandard.Should().Be(PreviewAppearance.Default.FontFamilyStandard);
        vm.CodeView.Colors.First(c => c.Key == "CSS Comment").Value.Should().Be(CodeViewAppearance.Default.CssCommentColor);
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

        await vm.CodeView.ChooseFontCommand.ExecuteAsync(null);

        asked!.MonospaceOnly.Should().BeTrue();
        vm.ApplyCommand.Execute(null);
        host.Settings.CodeViewAppearance.FontFamily.Should().Be("Consolas");
        host.Settings.CodeViewAppearance.FontSize.Should().Be(14);
    }

    [Fact]
    public async Task Cancelled_font_picker_leaves_the_font_unchanged()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string before = vm.CodeView.FontFamily;
        vm.FontPicker = _ => Task.FromResult<FontPickResult?>(null);

        await vm.CodeView.ChooseFontCommand.ExecuteAsync(null);

        vm.CodeView.FontFamily.Should().Be(before);
    }

    [Fact]
    public void A_font_page_edits_the_setting_and_previews_the_one_changed_last()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPageViewModel page = vm.CodeViewFonts;
        FontSettingViewModel hint = page.Fonts.Single(f => f.Key == "OpenTagHint");
        page.Active!.Key.Should().Be("CodeViewEditor", "the first setting is previewed at the start");

        hint.Family.Should().BeEmpty();
        hint.Placeholder.Should().Be(Strings.Get("PreferencesWindow_OpenTagHintFontDefault"));
        hint.EffectiveFamily.Should().Be(vm.CodeView.FontFamily, "an empty hint font is the editor font");
        hint.PreviewBackground.Should().Be(vm.OpenTagHintBackground.Value);
        hint.PreviewForeground.Should().Be(vm.OpenTagHintForeground.Value);

        hint.Size = 19;

        page.Active.Should().BeSameAs(hint);
        hint.EffectiveSize.Should().Be(19);
        vm.ApplyCommand.Execute(null);
        host.Settings.OpenTagHintAppearance.FontSize.Should().Be(19);
    }

    [Fact]
    public void A_font_setting_follows_changes_made_elsewhere()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel editor = vm.CodeViewFonts.Fonts.Single(f => f.Key == "CodeViewEditor");
        string family = new Fixture().Create<string>();
        List<string?> changed = new();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CodeView.FontFamily = family;

        editor.Family.Should().Be(family);
        changed.Should().NotBeEmpty("the row and the preview are refreshed");
    }

    [Fact]
    public void The_editor_font_is_previewed_as_code_in_the_colors_being_edited()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel editor = vm.CodeViewFonts.Fonts.Single(f => f.Key == "CodeViewEditor");
        FontSettingViewModel hint = vm.CodeViewFonts.Fonts.Single(f => f.Key == "OpenTagHint");

        vm.CodeView.Colors.Single(c => c.Key == "XHTML HTML").Value = "#123456";
        vm.ExtendedHighlighting = !vm.ExtendedHighlighting;

        editor.IsCodePreview.Should().BeTrue();
        editor.CodePreviewAppearance!.XhtmlHtmlColor.Should().Be("#123456", "the preview shows the colors not applied yet");
        editor.CodePreviewExtended.Should().Be(vm.ExtendedHighlighting);
        hint.IsCodePreview.Should().BeFalse();
        hint.CodePreviewAppearance.Should().BeNull();
    }

    [Fact]
    public void Restore_defaults_of_a_font_page_resets_only_its_fonts()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.PreviewFontStandard = new Fixture().Create<string>();
        vm.PreviewFontSize = 22;
        vm.SpecialCharacterFontSize = 40;

        vm.PreviewFonts.RestoreDefaultsCommand.Execute(null);

        vm.PreviewFontStandard.Should().Be(PreviewAppearance.Default.FontFamilyStandard);
        vm.PreviewFontSize.Should().Be(PreviewAppearance.Default.FontSize);
        vm.SpecialCharacterFontSize.Should().Be(40, "another page");
    }

    [Fact]
    public void The_warning_size_0_is_previewed_at_the_interface_size_in_the_warning_color()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel ui = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");
        FontSettingViewModel warnings = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Warnings");

        warnings.HasFamily.Should().BeFalse();
        warnings.Size.Should().Be(0);
        warnings.EffectiveSize.Should().Be(ui.EffectiveSize);
        warnings.EffectiveFamily.Should().Be(ui.EffectiveFamily);

        vm.WarningColor.Value = "#123456";

        warnings.PreviewForeground.Should().Be("#123456");
    }

    [Fact]
    public void The_interface_font_row_describes_the_default_and_stores_a_chosen_family()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel ui = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");
        string family = new Fixture().Create<string>();

        ui.Family.Should().BeEmpty();
        ui.Placeholder.Should().Be(vm.UiFontDescription);

        ui.Family = family;

        vm.UiFontFamily.Should().Be(family);
        ui.EffectiveFamily.Should().Be(family);
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
