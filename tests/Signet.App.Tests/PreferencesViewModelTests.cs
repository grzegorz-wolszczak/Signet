using Signet.Core.MainUI;
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
using Signet.Core.Footnotes;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Themes;
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
        ThemeManager themes = new(host.Settings, ThemeStores.In(root), NullLogger<ThemeManager>.Instance);
        return new PreferencesViewModel(host.Settings, spellChecker, localization, iconTheme, host.Shortcuts, host.Registry, uiDensity, themes);
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
        PreviewHighlight stored = new(PreviewHighlightStyle.Outline, 30, 4, true, 1500);
        host.Settings.PreviewHighlight = stored;

        PreferencesViewModel sut = NewOn(host);

        sut.PreviewHighlightStyle.Should().Be(PreviewHighlightStyle.Outline);
        sut.IsPreviewHighlightOutline.Should().BeTrue();
        sut.IsPreviewHighlightBackground.Should().BeFalse();
        sut.PreviewHighlightOpacity.Should().Be(30);
        sut.PreviewHighlightOutlineWidth.Should().Be(4);
        sut.PreviewHighlightAutoHide.Should().BeTrue();
        sut.PreviewHighlightAutoHideDelay.Should().Be(1500);
    }

    [Fact]
    public void Warning_fields_start_from_settings_and_changes_persist_on_apply()
    {
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16);
        PreferencesViewModel sut = NewOn(host);

        sut.WarningFontSize.Should().Be(16);

        sut.WarningFontSize = 0;
        sut.ApplyCommand.Execute(null);

        host.Settings.WarningAppearance.Should().Be(new WarningAppearance(0));
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
    public void The_footnote_letter_style_starts_from_settings_and_persists_on_apply()
    {
        using TestHost host = new();
        PreferencesViewModel sut = NewOn(host);
        sut.FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.LowerLatin, "a, b is the default");
        PreferencesViewModel.AvailableFootnoteLetterStyles.Should().Equal(
            FootnoteLetterStyle.LowerLatin, FootnoteLetterStyle.UpperLatin, FootnoteLetterStyle.LowerRoman);

        sut.FootnoteLetterStyle = FootnoteLetterStyle.LowerRoman;
        sut.ApplyCommand.Execute(null);

        host.Settings.FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.LowerRoman);
        NewOn(host).FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.LowerRoman);
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
        host.Settings.CodeEditorOpenTagHint = false;
        host.Settings.CodeEditorOpenTagHintDelayMs = 800;
        PreferencesViewModel sut = NewOn(host);

        sut.OpenTagHint.Should().BeFalse();
        sut.OpenTagHintDelay.Should().Be(800);

        sut.OpenTagHint = true;
        sut.OpenTagHintDelay = 250;
        sut.ApplyCommand.Execute(null);

        host.Settings.CodeEditorOpenTagHint.Should().BeTrue();
        host.Settings.CodeEditorOpenTagHintDelayMs.Should().Be(250);
    }

    [Fact]
    public void Open_tag_hint_look_starts_from_settings_persists_and_resets()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        host.Settings.OpenTagHintAppearance = new OpenTagHintAppearance("Consolas", 15);
        PreferencesViewModel sut = NewOn(host);

        sut.OpenTagHintFontFamily.Should().Be("Consolas");
        sut.OpenTagHintFontSize.Should().Be(15);

        sut.ResetOpenTagHintFontCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        host.Settings.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default);
        sut.OpenTagHintFontFamily.Should().BeEmpty("\"Default\" is the editor font again");

        sut.OpenTagHintFontSize = 20;
        sut.RestoreAppearanceDefaultsCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        host.Settings.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default);
    }

    [Fact]
    public void Restoring_the_appearance_defaults_resets_the_warnings()
    {
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16);
        PreferencesViewModel sut = NewOn(host);

        sut.RestoreAppearanceDefaultsCommand.Execute(null);

        sut.WarningFontSize.Should().Be(0);
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
        vm.ApplyCommand.Execute(null);

        host.Settings.PreviewHighlight.Should().Be(new PreviewHighlight(
            PreviewHighlightStyle.Outline, 70,
            PreviewHighlight.OutlineWidthMax, true, PreviewHighlight.AutoHideDelayMin));
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
    public void The_found_usages_column_fit_is_a_draft_until_saved_and_a_default_to_restore()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.FindUsagesFitUsageColumn.Should().BeTrue();

        vm.FindUsagesFitUsageColumn = false;

        host.Settings.FindUsagesFitUsageColumn.Should().BeTrue("not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.FindUsagesFitUsageColumn.Should().BeFalse();

        vm.RestoreAppearanceDefaultsCommand.Execute(null);
        vm.FindUsagesFitUsageColumn.Should().BeTrue();
    }

    [Fact]
    public void The_found_usages_row_spacing_is_a_draft_until_saved_and_a_default_to_restore()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.FindUsagesRowSpacing.Should().Be(0);

        vm.FindUsagesRowSpacing = 3;

        host.Settings.FindUsagesRowSpacing.Should().Be(0, "not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.FindUsagesRowSpacing.Should().Be(3);

        vm.RestoreAppearanceDefaultsCommand.Execute(null);
        vm.FindUsagesRowSpacing.Should().Be(0);
    }

    [Fact]
    public void The_fonts_with_a_weight_are_a_draft_until_saved_and_a_default_to_restore()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel context = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesContext");
        FontSettingViewModel editor = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "CodeEditorText");
        FontSettingViewModel interfaceFont = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");
        context.HasWeight.Should().BeTrue();

        context.Weight = 300;
        editor.Weight = 600;
        interfaceFont.Weight = 350;

        host.Settings.FindUsagesAppearance.ContextFontWeight.Should().Be(0, "not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.FindUsagesAppearance.ContextFontWeight.Should().Be(300);
        host.Settings.CodeEditorAppearance.FontWeight.Should().Be(600);
        host.Settings.UiFontWeight.Should().Be(350);

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        context.Weight.Should().Be(0);
        editor.Weight.Should().Be(0);
    }

    [Fact]
    public void The_weight_list_of_a_font_row_offers_the_default_and_keeps_a_weight_the_font_lacks()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel row = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesCells");

        row.Weights.First().Value.Should().Be(0);
        row.SelectedWeight!.Value.Should().Be(0);

        row.Weight = 450; // a weight in no font here: it stays listed, so it can be seen and changed
        row.Weights.Select(w => w.Value).Should().Contain(450);
        row.SelectedWeight!.Value.Should().Be(450);

        row.SelectedWeight = row.Weights.First();

        row.Weight.Should().Be(0);
    }

    [Fact]
    public void The_found_usages_fonts_page_has_the_headers_the_context_and_the_other_cells()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.FindUsagesFonts.Fonts.Select(f => f.Key).Should().Equal("FindUsagesHeaders", "FindUsagesContext", "FindUsagesCells");
    }

    [Fact]
    public void The_found_usages_fonts_are_a_draft_until_saved_and_a_default_to_restore()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.FindUsagesContextFontFamily = "Consolas";
        vm.FindUsagesContextFontSize = 14;
        vm.FindUsagesHeaderFontSize = 11;
        vm.FindUsagesCellsFontFamily = "Arial";

        host.Settings.FindUsagesAppearance.Should().Be(FindUsagesAppearance.Default, "not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.FindUsagesAppearance.Should().Be(new FindUsagesAppearance(string.Empty, 11, "Consolas", 14, "Arial", 0));

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        vm.FindUsagesContextFontFamily.Should().BeEmpty();
        vm.FindUsagesContextFontSize.Should().Be(0);
        vm.FindUsagesHeaderFontSize.Should().Be(0);
        vm.FindUsagesCellsFontFamily.Should().BeEmpty();
    }

    [Fact]
    public void RestoreAppearanceDefaultsCommand_resets_the_fonts()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.PreviewFontStandard = "Georgia";
        vm.CodeEditor.FontSize = 31;

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        vm.PreviewFontStandard.Should().Be(PreviewAppearance.Default.FontFamilyStandard);
        vm.CodeEditor.FontSize.Should().Be(CodeEditorAppearance.Default.FontSize);
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
    public async Task Code_editor_font_picker_starts_monospace_only_and_applies_family_and_size()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Consolas", 14));
        };

        await vm.CodeEditor.ChooseFontCommand.ExecuteAsync(null);

        asked!.MonospaceOnly.Should().BeTrue();
        vm.ApplyCommand.Execute(null);
        host.Settings.CodeEditorAppearance.FontFamily.Should().Be("Consolas");
        host.Settings.CodeEditorAppearance.FontSize.Should().Be(14);
    }

    [Fact]
    public async Task Cancelled_font_picker_leaves_the_font_unchanged()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string before = vm.CodeEditor.FontFamily;
        vm.FontPicker = _ => Task.FromResult<FontPickResult?>(null);

        await vm.CodeEditor.ChooseFontCommand.ExecuteAsync(null);

        vm.CodeEditor.FontFamily.Should().Be(before);
    }

    [Fact]
    public void A_font_page_edits_the_setting_and_previews_the_one_changed_last()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPageViewModel page = vm.CodeEditorFonts;
        FontSettingViewModel hint = page.Fonts.Single(f => f.Key == "OpenTagHint");
        page.Active!.Key.Should().Be("CodeEditorText", "the first setting is previewed at the start");

        hint.Family.Should().BeEmpty();
        hint.Placeholder.Should().Be(vm.CodeEditor.FontFamily, "the real name of the editor font, not a description of the default");
        hint.EffectiveFamily.Should().Be(vm.CodeEditor.FontFamily, "an empty hint font is the editor font");
        hint.PreviewBackground.Should().Be(AppTheme.Current.Background(ThemeKeys.HintsOpenTag).ToString(), "the colors of the theme");
        hint.PreviewForeground.Should().Be(AppTheme.Current.Foreground(ThemeKeys.HintsOpenTag).ToString());

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
        FontSettingViewModel editor = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "CodeEditorText");
        string family = new Fixture().Create<string>();
        List<string?> changed = new();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CodeEditor.FontFamily = family;

        editor.Family.Should().Be(family);
        changed.Should().NotBeEmpty("the row and the preview are refreshed");
    }

    [Fact]
    public void The_editor_font_is_previewed_as_code_with_the_highlighting_being_edited()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel editor = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "CodeEditorText");
        FontSettingViewModel hint = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "OpenTagHint");

        vm.ExtendedHighlighting = !vm.ExtendedHighlighting;

        editor.IsCodePreview.Should().BeTrue();
        editor.CodePreviewExtended.Should().Be(vm.ExtendedHighlighting, "the preview shows the switch not applied yet");
        hint.IsCodePreview.Should().BeFalse();
    }

    // The tooltip fonts of the parts without a section of their own, on Appearance › General › Fonts.
    private static readonly string[] AppearanceTooltipRows =
    {
        "TooltipMainWindow", "TooltipDocumentTabs", "TooltipPreferencesWindow", "TooltipOtherWindows",
    };

    [Fact]
    public void Tooltip_fonts_are_set_per_area_on_the_fonts_pages_and_applied_on_save()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string family = new Fixture().Create<string>();

        vm.AppearanceFonts.Fonts.Select(f => f.Key).Should().Contain(AppearanceTooltipRows);
        vm.PreviewFonts.Fonts.Select(f => f.Key).Should().Contain("TooltipPreview");
        vm.SpecialCharacterFonts.Fonts.Select(f => f.Key).Should().Contain("TooltipSpecialCharacters");
        FontSettingViewModel footnotes = vm.FootnotesFonts.Fonts.Should().ContainSingle().Subject;
        footnotes.Key.Should().Be("TooltipFootnotes");
        footnotes.Family.Should().BeEmpty();
        footnotes.Placeholder.Should().Be(vm.EffectiveUiFontFamily, "the real name of the interface font");
        footnotes.EffectiveFamily.Should().Be(vm.EffectiveUiFontFamily, "an empty typeface is the interface font");
        footnotes.Size.Should().Be(13);
        footnotes.PreviewText.Should().Be(Strings.Get("FontPreview_TooltipFootnotes"));

        footnotes.Family = family;
        footnotes.Size = 18;

        host.Settings.GetTooltipFont(TooltipArea.Footnotes).Should().Be(TooltipFont.Default, "not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.GetTooltipFont(TooltipArea.Footnotes).Should().Be(new TooltipFont(family, 18));
        host.Settings.GetTooltipFont(TooltipArea.Validation).Should().Be(TooltipFont.Default);
    }

    [Fact]
    public void Every_tooltip_area_but_the_other_windows_inherits_their_font_by_default()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string family = new Fixture().Create<string>();
        FontSettingViewModel other = vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipOtherWindows");
        other.HasInheritance.Should().BeFalse("the other windows have no parent");
        other.Family = family;
        other.Size = 17;

        FontSettingViewModel footnotes = vm.FootnotesFonts.Fonts.Single();
        FontSettingViewModel main = vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipMainWindow");

        foreach (FontSettingViewModel row in new[] { footnotes, main, vm.PreviewFonts.Fonts.Single(f => f.Key == "TooltipPreview") })
        {
            row.HasInheritance.Should().BeTrue();
            row.InheritFromParent.Should().BeTrue("every window inherits by default");
            row.IsOwnFont.Should().BeFalse();
            row.Parent.Should().BeSameAs(other);
            row.ParentFont.Should().Be($"{family}, 17");
            row.EffectiveFamily.Should().Be(family);
            row.EffectiveSize.Should().Be(17);
        }

        footnotes.ParentPath.Should().Be(string.Join(
            " › ",
            Strings.Get("PreferencesWindow_Appearance"),
            Strings.Get("PreferencesWindow_General"),
            Strings.Get("PreferencesWindow_Fonts"),
            Strings.Get("PreferencesWindow_TooltipFontOtherWindows")));
    }

    [Fact]
    public void Turning_the_inheritance_off_uses_the_own_font_and_restore_defaults_turns_it_on_again()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string own = new Fixture().Create<string>();
        vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipOtherWindows").Size = 17;
        FontSettingViewModel footnotes = vm.FootnotesFonts.Fonts.Single();
        footnotes.Family = own;
        footnotes.Size = 20;
        footnotes.EffectiveSize.Should().Be(17, "the own font is stored, but not used while the setting inherits");

        footnotes.InheritFromParent = false;

        footnotes.IsOwnFont.Should().BeTrue();
        footnotes.EffectiveFamily.Should().Be(own);
        footnotes.EffectiveSize.Should().Be(20);
        vm.ApplyCommand.Execute(null);
        host.Settings.GetTooltipInherit(TooltipArea.Footnotes).Should().BeFalse();
        host.Settings.GetEffectiveTooltipFont(TooltipArea.Footnotes).Should().Be(new TooltipFont(own, 20));

        footnotes.RestoreDefault();

        footnotes.InheritFromParent.Should().BeTrue();
        footnotes.Size.Should().Be(TooltipFont.Default.FontSize);
    }

    [Fact]
    public void Book_Browser_has_a_checkbox_per_folder_for_expanding_it_on_load_saved_with_the_others()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.BookBrowserFolders.Select(o => o.Label).Should().Equal("Text", "Styles", "Images", "Fonts", "Audio", "Video", "Misc");
        vm.BookBrowserFolders.Where(o => o.IsChecked).Select(o => o.Label).Should().Equal("Text");

        vm.BookBrowserFolders.Single(o => o.Label == "Video").IsChecked = true;
        vm.BookBrowserFolders.Single(o => o.Label == "Text").IsChecked = false;

        host.Settings.GetBookBrowserExpandOnLoad(OpfModelGroupKind.Video).Should().BeFalse("not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.GetBookBrowserExpandOnLoad(OpfModelGroupKind.Video).Should().BeTrue();
        host.Settings.GetBookBrowserExpandOnLoad(OpfModelGroupKind.Text).Should().BeFalse();

        vm.RestoreAppearanceDefaultsCommand.Execute(null);
        vm.BookBrowserFolders.Where(o => o.IsChecked).Select(o => o.Label).Should().Equal("Text");
    }

    [Fact]
    public void Book_Browser_look_is_saved_with_the_others_and_restored_with_the_appearance_defaults()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.BookBrowserIndent.Should().Be(20);
        vm.BookBrowserShowFolders.Should().BeFalse();

        vm.BookBrowserIndent = 35;
        vm.BookBrowserShowFolders = true;
        vm.BookBrowserCellMinHeight.Should().Be(16);
        vm.BookBrowserCellMinHeight = 24;
        vm.BookBrowserCellPadding = 2;
        vm.BookBrowserItemsFontFamily = "Consolas";
        vm.BookBrowserItemsFontSize = 14;
        vm.BookBrowserHeaderFontFamily = "Arial";
        vm.BookBrowserHeaderFontSize = 11;

        host.Settings.BookBrowserAppearance.Should().Be(BookBrowserAppearance.Default, "not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.BookBrowserAppearance.Should().Be(new BookBrowserAppearance("Consolas", 14, "Arial", 11, 35, 24, 2, true));

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        (vm.BookBrowserIndent, vm.BookBrowserShowFolders, vm.BookBrowserItemsFontFamily, vm.BookBrowserHeaderFontSize,
                vm.BookBrowserCellMinHeight, vm.BookBrowserCellPadding)
            .Should().Be((20, false, string.Empty, 0, 16, 0));
    }

    [Fact]
    public void Line_numbers_font_inherits_the_editor_font_until_it_is_unchecked_and_is_saved_with_the_others()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel row = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "LineNumbers");

        vm.CodeEditorFonts.Fonts.Select(f => f.Key).Should().Equal("CodeEditorText", "LineNumbers", "OpenTagHint", "DocumentTabs");
        row.HasInheritance.Should().BeTrue();
        row.InheritFromParent.Should().BeTrue("by default the numbers have the font of the editor");
        row.EffectiveFamily.Should().Be(vm.CodeEditor.FontFamily);
        row.EffectiveSize.Should().Be(vm.CodeEditor.FontSize);
        row.Parent.Should().BeSameAs(vm.CodeEditorFonts.Fonts[0]);

        row.InheritFromParent = false;

        row.IsOwnFont.Should().BeTrue();
        (vm.LineNumbersFontFamily, vm.LineNumbersFontSize).Should().Be((vm.CodeEditor.FontFamily, vm.CodeEditor.FontSize),
            "unchecking starts from the editor font");
        vm.LineNumbersFontSize = 17;
        host.Settings.LineNumberFontAppearance.Should().Be(LineNumberFontAppearance.Default, "not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.LineNumberFontAppearance.Should().Be(new LineNumberFontAppearance(vm.CodeEditor.FontFamily, 17));

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        row.InheritFromParent.Should().BeTrue();
        (vm.LineNumbersFontFamily, vm.LineNumbersFontSize).Should().Be((string.Empty, 0));
    }

    [Fact]
    public void Book_Browser_Fonts_page_has_rows_for_the_names_and_for_the_headers_before_the_tooltip()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        vm.BookBrowserFonts.Fonts.Select(f => f.Key).Should().Equal(
            "BookBrowserItems", "BookBrowserHeaders", "Tooltip" + TooltipArea.BookBrowser);
        FontSettingViewModel items = vm.BookBrowserFonts.Fonts[0];
        items.Placeholder.Should().Be(vm.EffectiveUiFontFamily, "an empty typeface is the interface font");

        items.Size = 15;
        items.Family = "Consolas";

        vm.BookBrowserItemsFontSize.Should().Be(15);
        vm.BookBrowserItemsFontFamily.Should().Be("Consolas");
        vm.BookBrowserFonts.Fonts[1].Size.Should().Be(0, "the headers have a font of their own");
    }

    [Fact]
    public void The_preferences_window_is_modal_by_default_and_it_is_a_setting_saved_with_the_others()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        vm.PreferencesWindowModal.Should().BeTrue();

        vm.PreferencesWindowModal = false;

        host.Settings.PreferencesWindowModal.Should().BeTrue("not saved yet");
        vm.ApplyCommand.Execute(null);
        host.Settings.PreferencesWindowModal.Should().BeFalse();

        vm.RestoreAppearanceDefaultsCommand.Execute(null);
        vm.PreferencesWindowModal.Should().BeTrue();
    }

    [Fact]
    public void Restore_appearance_defaults_resets_the_tooltip_fonts()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel main = vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipMainWindow");
        main.Size = 20;

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        main.Size.Should().Be(TooltipFont.Default.FontSize);
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
    public void The_warning_size_0_is_previewed_at_the_interface_size_in_the_warning_color_of_the_theme()
    {
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel ui = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");
        FontSettingViewModel warnings = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Warnings");

        warnings.HasFamily.Should().BeFalse();
        warnings.Size.Should().Be(0);
        warnings.EffectiveSize.Should().Be(ui.EffectiveSize);
        warnings.EffectiveFamily.Should().Be(ui.EffectiveFamily);

        warnings.PreviewForeground.Should().Be(AppTheme.Current.Color(ThemeKeys.UiWarningText).ToString());
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
