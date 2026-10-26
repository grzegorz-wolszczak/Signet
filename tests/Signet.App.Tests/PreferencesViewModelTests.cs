using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using Avalonia.Input;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.Footnotes;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Themes;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="PreferencesViewModel"/>: changes are written to <see cref="SettingsStore"/> only when
/// saved (Save), and the keyboard shortcuts page is the keymap of all actions.
/// </summary>
public sealed class PreferencesViewModelTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (TestHost Host, PreferencesViewModel Vm) New()
    {
        TestHost host = _disposables.Add(new TestHost());
        return (host, NewOn(host));
    }

    private PreferencesViewModel NewOn(TestHost host)
    {
        LocalizationManager localization = new(host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(host.Settings, NullLogger<IconThemeManager>.Instance);
        string root = _disposables.Add(new TempDir("prefs-")).Path;
        SpellChecker spellChecker = new(host.Settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        UiDensityManager uiDensity = new(
            host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        ThemeManager themes = new(host.Settings, ThemeStores.In(root), NullLogger<ThemeManager>.Instance);
        return _disposables.Add(
            new PreferencesViewModel(host.Settings, spellChecker, localization, iconTheme, host.Shortcuts, host.Registry, uiDensity, themes));
    }

    [Fact]
    public void Changes_stay_in_the_window_until_applied()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string iconThemeBefore = host.Settings.UiIconTheme;
        bool spellCheckBefore = host.Settings.SpellCheck;
        int applied = 0;
        vm.Applied += (_, _) => applied++;

        // Act & Assert
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();

        // Act
        vm.SelectedIconTheme = PreferencesViewModel.AvailableIconThemes.First(t => t.Code != iconThemeBefore);
        vm.SpellCheckEnabled = !spellCheckBefore;

        // Assert
        host.Settings.UiIconTheme.Should().Be(iconThemeBefore, "nothing is written before Save");
        host.Settings.SpellCheck.Should().Be(spellCheckBefore);
        vm.HasChanges.Should().BeTrue();
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.UiIconTheme.Should().Be(vm.SelectedIconTheme.Code);
        host.Settings.SpellCheck.Should().Be(!spellCheckBefore);
        vm.HasChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        applied.Should().Be(1);
    }

    /// <summary>
    /// The temp folder is picked in a folder dialog, so the page shows the setting: the folder itself, or a note that
    /// the system default is used. "Clear" goes back to that default and has nothing to undo while it is in use.
    /// </summary>
    [Fact]
    public void The_temp_folder_is_shown_and_cleared_back_to_the_default()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string folder = _disposables.Add(new TempDir("temp-home-")).Path;

        // Act & Assert
        vm.TempFolderDisplay.Should().Be(Strings.Get("PreferencesWindow_TempFolderDefault"));
        vm.HasCustomTempFolder.Should().BeFalse();

        // Act
        vm.TempFolderHome = folder;

        // Assert
        vm.TempFolderDisplay.Should().Be(folder);
        vm.HasCustomTempFolder.Should().BeTrue();

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.TempFolderHome.Should().Be(folder);

        // Act
        vm.ClearTempFolderCommand.Execute(null);
        vm.ApplyCommand.Execute(null);

        // Assert
        vm.TempFolderHome.Should().BeEmpty();
        vm.TempFolderDisplay.Should().Be(Strings.Get("PreferencesWindow_TempFolderDefault"));
        vm.HasCustomTempFolder.Should().BeFalse();
        host.Settings.TempFolderHome.Should().BeEmpty();
    }

    [Fact]
    public void A_window_closed_without_applying_changes_nothing()
    {
        // Arrange
        using TestHost host = new();
        string folderBefore = host.Settings.UiCustomIconFolder;
        PreferencesViewModel cancelled = NewOn(host);

        // Act
        cancelled.CustomIconFolder = new Fixture().Create<string>();
        cancelled.PreviewRefreshDelay = SettingsStore.UiPreviewTimeoutMax;

        // Assert
        host.Settings.UiCustomIconFolder.Should().Be(folderBefore);
        NewOn(host).CustomIconFolder.Should().Be(folderBefore, "the next window starts from the saved settings");
    }

    [Fact]
    public void Keymap_changes_wait_for_apply()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        KeymapNode open = vm.Keymap.ActionNodes.Single(n => n.ActionId == AppActionIds.Open);
        KeyStrokeShortcut added = new(new KeyGesture(Key.F12, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift));
        List<string> changed = new();
        host.Shortcuts.ShortcutChanged += (_, id) => changed.Add(id);

        // Act
        vm.Keymap.AddShortcut(open, added, removeConflicts: false);

        // Assert
        open.Shortcuts.Should().Contain(added, "the keymap page shows the change at once");
        host.Shortcuts.Get(AppActionIds.Open)!.Shortcuts.Should().NotContain(added);
        changed.Should().BeEmpty();
        vm.HasChanges.Should().BeTrue();

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Shortcuts.Get(AppActionIds.Open)!.Shortcuts.Should().Contain(added);
        changed.Should().Equal(AppActionIds.Open);
    }

    [Fact]
    public void Preview_highlight_fields_start_from_settings()
    {
        // Arrange
        using TestHost host = new();
        PreviewHighlight stored = new(PreviewHighlightStyle.Outline, 30, 4, true, 1500);
        host.Settings.PreviewHighlight = stored;

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Assert
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
        // Arrange
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16);

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.WarningFontSize.Should().Be(16);

        // Act
        sut.WarningFontSize = 0;
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.WarningAppearance.Should().Be(new WarningAppearance(0));
    }

    [Fact]
    public void Tool_header_font_starts_from_settings_and_changes_persist_on_apply()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.ToolHeaderFontAppearance = new ToolHeaderFontAppearance("Arial", 16, 600);

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.ToolHeaderFontFamily.Should().Be("Arial");
        sut.ToolHeaderFontSize.Should().Be(16);
        sut.ToolHeaderFontWeight.Should().Be(600);
        sut.AppearanceFonts.Fonts.Should().Contain(f => f.Key == "ToolHeader", "the panel-header font is on the Appearance Fonts page");

        // Act
        sut.ToolHeaderFontFamily = string.Empty;
        sut.ToolHeaderFontSize = 0;
        sut.ToolHeaderFontWeight = 0;
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.ToolHeaderFontAppearance.Should().Be(ToolHeaderFontAppearance.Default);
    }

    [Fact]
    public void Find_action_button_display_starts_from_settings_and_changes_persist_on_apply()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.FindActionButtonDisplay = FindActionButtonDisplay.IconOnly;

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.FindActionButtonDisplay.Should().Be(FindActionButtonDisplay.IconOnly);

        // Act
        sut.FindActionButtonDisplay = FindActionButtonDisplay.TextOnly;
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FindActionButtonDisplay.Should().Be(FindActionButtonDisplay.TextOnly);
    }

    private sealed class FakeEditionPageHost : IEditionPageHost
    {
        public bool HasOpenBook { get; set; } = true;

        public string? EditionPageBookPath { get; set; }

        public int RemoveCalls { get; private set; }

        public Task<bool> RemoveEditionPageAsync()
        {
            RemoveCalls++;
            EditionPageBookPath = null;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public void The_footnote_removal_confirmation_starts_from_settings_and_persists_on_apply()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.ConfirmFootnoteRemoval = false;

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.ConfirmFootnoteRemoval.Should().BeFalse();

        // Act
        sut.ConfirmFootnoteRemoval = true;
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.ConfirmFootnoteRemoval.Should().BeTrue();
    }

    [Fact]
    public void The_footnote_letter_style_starts_from_settings_and_persists_on_apply()
    {
        // Arrange
        using TestHost host = new();

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.LowerLatin, "a, b is the default");
        PreferencesViewModel.AvailableFootnoteLetterStyles.Should().Equal(
            FootnoteLetterStyle.LowerLatin, FootnoteLetterStyle.UpperLatin, FootnoteLetterStyle.LowerRoman);

        // Act
        sut.FootnoteLetterStyle = FootnoteLetterStyle.LowerRoman;
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.LowerRoman);
        NewOn(host).FootnoteLetterStyle.Should().Be(FootnoteLetterStyle.LowerRoman);
    }

    [Fact]
    public void Footnote_styles_start_from_settings_and_changes_persist_on_apply()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.FootnoteStyles = Signet.Core.Footnotes.FootnoteStyles.Default with { Note = "color: red;" };

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.FootnoteNoteStyle.Should().Be("color: red;");

        sut.FootnoteParagraphStyle.Should().Be("margin: 0;\ntext-indent: 0;");

        // Act
        sut.FootnoteSeparatorStyle = "border: none;";
        sut.FootnoteReferenceStyle = "vertical-align: super;";
        sut.FootnoteParagraphStyle = "margin: 0 0 0.3em 0;";
        sut.FootnoteGroupTitleStyle = "font-weight: bold;";

        // Act & Assert
        sut.FootnoteGroupByChapter.Should().BeFalse("grouping is off by default");

        // Act
        sut.FootnoteGroupByChapter = true;

        // Act & Assert
        sut.FootnoteReferenceStyleError.Should().BeEmpty();

        // Act
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FootnoteStyles.Should().Be(Signet.Core.Footnotes.FootnoteStyles.Default with
        {
            Note = "color: red;",
            Separator = "border: none;",
            Reference = "vertical-align: super;",
            Paragraph = "margin: 0 0 0.3em 0;",
            GroupTitle = "font-weight: bold;",
        });
        host.Settings.FootnoteGroupByChapter.Should().BeTrue();

        // Act
        sut.ResetFootnoteStylesCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FootnoteStyles.Should().Be(Signet.Core.Footnotes.FootnoteStyles.Default);
    }

    [Fact]
    public void An_invalid_footnote_style_shows_why_and_keeps_its_last_valid_definition()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        PreferencesViewModel sut = NewOn(host);

        // Act
        sut.FootnoteNoteStyle = "margin: 0;";
        sut.FootnoteNoteStyle = "fnot-size: 1em;";

        // Act & Assert
        sut.FootnoteNoteStyleError.Should().Be("Invalid style (it will not be saved): unknown property “fnot-size”");
        sut.FootnoteSeparatorStyleError.Should().BeEmpty();

        // Act
        sut.FootnoteSeparatorStyle = "border: none;";
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FootnoteStyles.Note.Should().Be("margin: 0;", "the last valid definition is saved");
        host.Settings.FootnoteStyles.Separator.Should().Be("border: none;", "the other fields are saved as usual");
        sut.FootnoteNoteStyle.Should().Be("fnot-size: 1em;", "the field keeps what was typed");
    }

    [Fact]
    public void Edition_page_fields_start_from_settings_and_changes_persist_on_apply()
    {
        // Arrange
        using TestHost host = new();
        string value = new Fixture().Create<string>();
        host.Settings.EditionPage = EditionPageSettings.Default with
        {
            Enabled = true,
            Position = EditionPagePosition.Second,
            Fields = new[] { new EditionPageField("Edited by", value, true) },
            HeadingLevel = 2,
        };

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.EditionPageEnabled.Should().BeTrue();
        sut.EditionPagePosition.Should().Be(EditionPagePosition.Second);
        sut.EditionPageHeadingLevel.Level.Should().Be(2);
        sut.EditionPageFields.Should().ContainSingle().Which.Value.Should().Be(value);

        // Act
        sut.EditionPageFields[0].Show = false;
        sut.AddEditionPageFieldCommand.Execute(null);
        sut.EditionPageFields[1].Key = "Source";
        sut.EditionPageShowDate = false;
        sut.EditionPageShowVersion = false;
        sut.EditionPagePosition = EditionPagePosition.Last;

        // Assert
        host.Settings.EditionPage.Position.Should().Be(EditionPagePosition.Second, "nothing is written before Save");

        // Act
        sut.ApplyCommand.Execute(null);

        // Assert
        EditionPageSettings saved = host.Settings.EditionPage;
        saved.Position.Should().Be(EditionPagePosition.Last);
        saved.ShowDate.Should().BeFalse();
        (saved.ShowProgram, saved.ShowVersion).Should().Be((true, false));
        saved.Fields.Should().Equal(new EditionPageField("Edited by", value, false), new EditionPageField("Source", string.Empty, true));

        // Act
        sut.EditionPageFields[0].RemoveCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.EditionPage.Fields.Select(f => f.Key).Should().Equal("Source");
    }

    [Fact]
    public void Removing_the_edition_page_is_possible_only_when_the_open_book_has_one()
    {
        // Arrange
        using TestHost host = new();

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.RemoveEditionPageCommand.CanExecute(null).Should().BeFalse("no book is attached");
        sut.EditionPageStatus.Should().Be(Strings.Get("PreferencesWindow_EditionPageNoBook"));

        FakeEditionPageHost book = new() { EditionPageBookPath = "OEBPS/Text/signet_edition.xhtml" };

        // Act
        sut.AttachEditionPageHost(book);

        // Act & Assert
        sut.RemoveEditionPageCommand.CanExecute(null).Should().BeTrue();
        sut.EditionPageStatus.Should().Contain("signet_edition.xhtml");

        // Act
        sut.RemoveEditionPageCommand.Execute(null);

        // Assert
        book.RemoveCalls.Should().Be(1);
        sut.RemoveEditionPageCommand.CanExecute(null).Should().BeFalse();
        sut.EditionPageStatus.Should().Be(Strings.Get("PreferencesWindow_EditionPageNotFound"));
    }

    [Fact]
    public void Open_tag_hint_fields_start_from_settings_and_changes_persist_on_apply()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.CodeEditorOpenTagHint = false;
        host.Settings.CodeEditorOpenTagHintDelayMs = 800;

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.OpenTagHint.Should().BeFalse();
        sut.OpenTagHintDelay.Should().Be(800);

        // Act
        sut.OpenTagHint = true;
        sut.OpenTagHintDelay = 250;
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.CodeEditorOpenTagHint.Should().BeTrue();
        host.Settings.CodeEditorOpenTagHintDelayMs.Should().Be(250);
    }

    [Fact]
    public void Open_tag_hint_look_starts_from_settings_persists_and_resets()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        host.Settings.OpenTagHintAppearance = new OpenTagHintAppearance("Consolas", 15);

        // Act
        PreferencesViewModel sut = NewOn(host);

        // Act & Assert
        sut.OpenTagHintFontFamily.Should().Be("Consolas");
        sut.OpenTagHintFontSize.Should().Be(15);

        // Act
        sut.ResetOpenTagHintFontCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default);

        // Act & Assert
        sut.OpenTagHintFontFamily.Should().BeEmpty("\"Default\" is the editor font again");

        // Act
        sut.OpenTagHintFontSize = 20;
        sut.RestoreAppearanceDefaultsCommand.Execute(null);
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.OpenTagHintAppearance.Should().Be(OpenTagHintAppearance.Default);
    }

    [Fact]
    public void Restoring_the_appearance_defaults_resets_the_warnings()
    {
        // Arrange
        using TestHost host = new();
        host.Settings.WarningAppearance = new WarningAppearance(16);

        // Act
        PreferencesViewModel sut = NewOn(host);

        sut.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        sut.WarningFontSize.Should().Be(0);

        // Act
        sut.ApplyCommand.Execute(null);

        // Assert
        host.Settings.WarningAppearance.Should().Be(WarningAppearance.Default);
    }

    [Fact]
    public void Preview_highlight_changes_persist_on_apply_and_clamped()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.PreviewHighlightStyle = PreviewHighlightStyle.Outline;
        vm.PreviewHighlightOutlineWidth = 50;
        vm.PreviewHighlightOpacity = 70;
        vm.PreviewHighlightAutoHide = true;
        vm.PreviewHighlightAutoHideDelay = 100;
        vm.ApplyCommand.Execute(null);

        // Assert
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
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.PreviewRefreshDelay.Should().Be(host.Settings.UiPreviewTimeout);

        // Act
        vm.PreviewRefreshDelay = entered;
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.UiPreviewTimeout.Should().Be(expected);
    }

    [Fact]
    public void SelectedLanguage_change_persists_on_apply()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string other = vm.SelectedLanguage.Code == "en" ? "pl" : "en";

        // Act
        vm.SelectedLanguage = PreferencesViewModel.AvailableLanguages.Single(l => l.Code == other);
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.UiLanguage.Should().Be(other);
    }

    [Fact]
    public void SelectedIconTheme_change_persists_on_apply()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string other = vm.SelectedIconTheme.Code == "material" ? "fluent" : "material";

        // Act
        vm.SelectedIconTheme = PreferencesViewModel.AvailableIconThemes.Single(t => t.Code == other);
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.UiIconTheme.Should().Be(other);
    }

    [Fact]
    public void Preview_font_changes_persist_on_apply()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.PreviewFontStandard = "Georgia";
        vm.PreviewFontSize = 20;
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.PreviewAppearance.FontFamilyStandard.Should().Be("Georgia");
        host.Settings.PreviewAppearance.FontSize.Should().Be(20);
    }

    [Fact]
    public void The_found_usages_column_fit_is_a_draft_until_saved_and_a_default_to_restore()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.FindUsagesFitUsageColumn.Should().BeTrue();

        // Act
        vm.FindUsagesFitUsageColumn = false;

        // Assert
        host.Settings.FindUsagesFitUsageColumn.Should().BeTrue("not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FindUsagesFitUsageColumn.Should().BeFalse();

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        vm.FindUsagesFitUsageColumn.Should().BeTrue();
    }

    [Fact]
    public void The_found_usages_row_spacing_is_a_draft_until_saved_and_a_default_to_restore()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.FindUsagesRowSpacing.Should().Be(0);

        // Act
        vm.FindUsagesRowSpacing = 3;

        // Assert
        host.Settings.FindUsagesRowSpacing.Should().Be(0, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FindUsagesRowSpacing.Should().Be(3);

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        vm.FindUsagesRowSpacing.Should().Be(0);
    }

    [Fact]
    public void The_fonts_with_a_weight_are_a_draft_until_saved_and_a_default_to_restore()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel context = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesContext");
        FontSettingViewModel editor = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "CodeEditorText");
        FontSettingViewModel interfaceFont = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");

        // Act & Assert
        context.HasWeight.Should().BeTrue();

        // Act
        context.Weight = 300;
        editor.Weight = 600;
        interfaceFont.Weight = 350;

        // Assert
        host.Settings.FindUsagesAppearance.ContextFontWeight.Should().Be(0, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FindUsagesAppearance.ContextFontWeight.Should().Be(300);
        host.Settings.CodeEditorAppearance.FontWeight.Should().Be(600);
        host.Settings.UiFontWeight.Should().Be(350);

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        context.Weight.Should().Be(0);
        editor.Weight.Should().Be(0);
    }

    [Fact]
    public void A_font_row_shows_its_typeface_size_and_weight_as_one_line()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel row = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesCells");

        string notSet = Strings.Get("FontWeight_Default");

        // Act & Assert
        row.Summary.Should().StartWith(vm.EffectiveUiFontFamily + " " + notSet, "nothing is set: the font in use is described");

        // Act
        vm.FindUsagesCellsFontSize = 14;

        // Assert
        row.Summary.Should().Be(vm.EffectiveUiFontFamily + " " + notSet + ", 14");

        // Act
        vm.FindUsagesCellsFontFamily = "Segoe UI";
        vm.FindUsagesCellsFontWeight = 300;

        // Assert
        row.Summary.Should().Be("Segoe UI, 14, Light");
    }

    [Fact]
    public void A_font_row_that_is_only_a_size_has_a_size_field_and_no_typeface()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel warnings = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Warnings");

        // Act & Assert
        warnings.IsSizeOnly.Should().BeTrue();
        vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface").IsSizeOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Changes_in_the_font_picker_are_applied_at_once_and_OK_keeps_them()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.FindUsagesCellsFontSize = 11;
        FontSettingViewModel row = vm.FindUsagesFonts.Fonts.Single(f => f.Key == "FindUsagesCells");
        string? summaryWhileOpen = null;
        vm.FontPicker = request =>
        {
            request.Changed!(new FontPickResult("Georgia", 20, 300));
            summaryWhileOpen = row.Summary;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Georgia", 22, 300));
        };

        await vm.ChooseFindUsagesCellsFontCommand.ExecuteAsync(null);

        // Assert
        summaryWhileOpen.Should().Be("Georgia, 20, Light", "the row (and so the preview) follows the window");
        row.Summary.Should().Be("Georgia, 22, Light");
        vm.FindUsagesCellsFontSize.Should().Be(22);
    }

    [Fact]
    public async Task Cancelling_the_font_picker_puts_back_the_font_from_before_it_was_opened()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.FindUsagesCellsFontFamily = "Arial";
        vm.FindUsagesCellsFontSize = 11;
        vm.FindUsagesCellsFontWeight = 700;
        vm.FontPicker = request =>
        {
            request.Changed!(new FontPickResult("Georgia", 20, 300));
            vm.FindUsagesCellsFontFamily.Should().Be("Georgia");
            return Task.FromResult<FontPickResult?>(null);
        };

        await vm.ChooseFindUsagesCellsFontCommand.ExecuteAsync(null);

        // Assert
        vm.FindUsagesCellsFontFamily.Should().Be("Arial");
        vm.FindUsagesCellsFontSize.Should().Be(11);
        vm.FindUsagesCellsFontWeight.Should().Be(700);
    }

    [Fact]
    public async Task The_picker_of_a_font_that_is_not_set_starts_with_the_font_in_use_selected()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.DocumentTabFontFamily = string.Empty;
        FontPickRequest? asked = null;
        vm.FontPicker = request =>
        {
            asked = request;
            return Task.FromResult<FontPickResult?>(null);
        };

        await vm.ChooseDocumentTabFontCommand.ExecuteAsync(null);

        // Assert
        asked!.Family.Should().Be(vm.EffectiveUiFontFamily, "an empty name would leave nothing selected, and OK off");
        vm.CodeEditorFonts.Fonts.Single(f => f.Key == "DocumentTabs").EffectiveFamily.Should().Be(vm.EffectiveUiFontFamily);
    }

    [Fact]
    public async Task Cancelling_the_picker_of_a_font_that_was_not_set_leaves_it_not_set()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.FontPicker = request =>
        {
            request.Changed!(new FontPickResult("Georgia", 20, 300));
            return Task.FromResult<FontPickResult?>(null);
        };

        await vm.ChooseFindUsagesContextFontCommand.ExecuteAsync(null);

        // Assert
        vm.FindUsagesContextFontFamily.Should().BeEmpty();
        vm.FindUsagesContextFontSize.Should().Be(0);
        vm.FindUsagesContextFontWeight.Should().Be(0);
    }

    [Fact]
    public void The_found_usages_fonts_page_has_the_headers_the_context_and_the_other_cells()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.FindUsagesFonts.Fonts.Select(f => f.Key).Should().Equal("FindUsagesHeaders", "FindUsagesContext", "FindUsagesCells");
    }

    [Fact]
    public void The_found_usages_fonts_are_a_draft_until_saved_and_a_default_to_restore()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.FindUsagesContextFontFamily = "Consolas";
        vm.FindUsagesContextFontSize = 14;
        vm.FindUsagesHeaderFontSize = 11;
        vm.FindUsagesCellsFontFamily = "Arial";

        // Assert
        host.Settings.FindUsagesAppearance.Should().Be(FindUsagesAppearance.Default, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.FindUsagesAppearance.Should().Be(new FindUsagesAppearance(string.Empty, 11, "Consolas", 14, "Arial", 0));

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        vm.FindUsagesContextFontFamily.Should().BeEmpty();
        vm.FindUsagesContextFontSize.Should().Be(0);
        vm.FindUsagesHeaderFontSize.Should().Be(0);
        vm.FindUsagesCellsFontFamily.Should().BeEmpty();
    }

    [Fact]
    public void RestoreAppearanceDefaultsCommand_resets_the_fonts()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.PreviewFontStandard = "Georgia";
        vm.CodeEditor.FontSize = 31;

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        vm.PreviewFontStandard.Should().Be(PreviewAppearance.Default.FontFamilyStandard);
        vm.CodeEditor.FontSize.Should().Be(CodeEditorAppearance.Default.FontSize);
    }

    [Fact]
    public void General_mend_checkboxes_persist_as_CleanOn_flags()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.MendOnOpen = true;
        vm.MendOnSave = false;
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.CleanOn.Should().Be(CleanOn.Open);
    }

    [Fact]
    public void DefaultVersionIsEpub3_persists_as_version_string()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.DefaultVersionIsEpub3 = true;
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.DefaultVersion.Should().Be("3.0");

        // Act
        vm.DefaultVersionIsEpub3 = false;
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.DefaultVersion.Should().Be("2.0");
    }

    [Fact]
    public void SpellCheckEnabled_toggle_persists_on_apply()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.SpellCheckEnabled = true;
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.SpellCheck.Should().BeTrue();
    }

    [Fact]
    public void PreserveEntities_add_edit_and_remove_persist_on_apply()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        int before = vm.PreserveEntities.Count;

        // Act
        vm.AddPreserveEntityCommand.Execute(null);
        PreserveEntityRow row = vm.PreserveEntities.Last();
        row.CodeText = "8212";
        row.Name = "&#8212;";
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.PreserveEntityCodeNames.Should().Contain(p => p.Code == 8212 && p.Name == "&#8212;");

        // Act
        row.RemoveCommand.Execute(null);

        // Assert
        vm.PreserveEntities.Should().HaveCount(before);

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.PreserveEntityCodeNames.Should().NotContain(p => p.Code == 8212);
    }

    [Fact]
    public void The_keyboard_shortcuts_page_is_the_keymap_of_all_actions()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.Keymap.ActionNodes.Select(n => n.ActionId).Should().BeEquivalentTo(host.Registry.Actions.Select(a => a.Id));
    }

    [Fact]
    public async Task ChooseSpecialCharacterFont_applies_family_and_size_from_the_picker()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPickRequest? asked = null;

        // Act
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Georgia", 22));
        };

        await vm.ChooseSpecialCharacterFontCommand.ExecuteAsync(null);

        // Assert
        asked!.Size.Should().NotBeNull();
        asked.MonospaceOnly.Should().BeFalse();
        vm.SpecialCharacterFontFamily.Should().Be("Georgia");
        vm.SpecialCharacterFontSize.Should().Be(22);

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.SpecialCharacterAppearance.Should().Be(new SpecialCharacterAppearance("Georgia", 22));
    }

    [Fact]
    public async Task ChoosePreviewFontSerif_changes_only_the_family_and_keeps_the_shared_size()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        int sizeBefore = vm.PreviewFontSize;
        FontPickRequest? asked = null;

        // Act
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Georgia", 99));
        };

        await vm.ChoosePreviewFontSerifCommand.ExecuteAsync(null);

        // Assert
        asked!.Size.Should().BeNull();
        vm.PreviewFontSerif.Should().Be("Georgia");
        vm.PreviewFontSize.Should().Be(sizeBefore);

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.PreviewAppearance.FontFamilySerif.Should().Be("Georgia");
    }

    [Fact]
    public async Task Code_editor_font_picker_starts_monospace_only_and_applies_family_and_size()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPickRequest? asked = null;

        // Act
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Consolas", 14));
        };

        await vm.CodeEditor.ChooseFontCommand.ExecuteAsync(null);

        // Assert
        asked!.MonospaceOnly.Should().BeTrue();

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.CodeEditorAppearance.FontFamily.Should().Be("Consolas");
        host.Settings.CodeEditorAppearance.FontSize.Should().Be(14);
    }

    [Fact]
    public async Task Cancelled_font_picker_leaves_the_font_unchanged()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string before = vm.CodeEditor.FontFamily;

        // Act
        vm.FontPicker = _ => Task.FromResult<FontPickResult?>(null);

        await vm.CodeEditor.ChooseFontCommand.ExecuteAsync(null);

        // Assert
        vm.CodeEditor.FontFamily.Should().Be(before);
    }

    [Fact]
    public void A_font_page_edits_its_settings_and_describes_their_previews()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontPageViewModel page = vm.CodeEditorFonts;
        FontSettingViewModel hint = page.Fonts.Single(f => f.Key == "OpenTagHint");

        // Act & Assert
        hint.Family.Should().BeEmpty();
        hint.Placeholder.Should().Be(vm.CodeEditor.FontFamily, "the real name of the editor font, not a description of the default");
        hint.EffectiveFamily.Should().Be(vm.CodeEditor.FontFamily, "an empty hint font is the editor font");
        hint.PreviewBackground.Should().Be(AppTheme.Current.Background(ThemeKeys.HintsOpenTag).ToString(), "the colors of the theme");
        hint.PreviewForeground.Should().Be(AppTheme.Current.Foreground(ThemeKeys.HintsOpenTag).ToString());

        // Act
        hint.Size = 19;

        // Assert
        hint.EffectiveSize.Should().Be(19);

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.OpenTagHintAppearance.FontSize.Should().Be(19);
    }

    [Fact]
    public void A_font_setting_follows_changes_made_elsewhere()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel editor = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "CodeEditorText");
        string family = new Fixture().Create<string>();
        List<string?> changed = new();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        vm.CodeEditor.FontFamily = family;

        // Assert
        editor.Family.Should().Be(family);
        changed.Should().NotBeEmpty("the row and the preview are refreshed");
    }

    [Fact]
    public void The_editor_font_is_previewed_as_code_with_the_highlighting_being_edited()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel editor = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "CodeEditorText");
        FontSettingViewModel hint = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "OpenTagHint");

        // Act
        vm.ExtendedHighlighting = !vm.ExtendedHighlighting;

        // Assert
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
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string family = new Fixture().Create<string>();

        // Act & Assert
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

        // Act
        footnotes.Family = family;
        footnotes.Size = 18;

        // Assert
        host.Settings.GetTooltipFont(TooltipArea.Footnotes).Should().Be(TooltipFont.Default, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.GetTooltipFont(TooltipArea.Footnotes).Should().Be(new TooltipFont(family, 18));
        host.Settings.GetTooltipFont(TooltipArea.Validation).Should().Be(TooltipFont.Default);
    }

    [Fact]
    public void Every_tooltip_area_but_the_other_windows_inherits_their_font_by_default()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string family = new Fixture().Create<string>();
        FontSettingViewModel other = vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipOtherWindows");

        // Act & Assert
        other.HasInheritance.Should().BeFalse("the other windows have no parent");

        // Act
        other.Family = family;
        other.Size = 17;

        FontSettingViewModel footnotes = vm.FootnotesFonts.Fonts.Single();
        FontSettingViewModel main = vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipMainWindow");

        foreach (FontSettingViewModel row in new[] { footnotes, main, vm.PreviewFonts.Fonts.Single(f => f.Key == "TooltipPreview") })
        {
            // Assert
            row.HasInheritance.Should().BeTrue();
            row.InheritFromParent.Should().BeTrue("every window inherits by default");
            row.IsOwnFont.Should().BeFalse();
            row.Parent.Should().BeSameAs(other);
            row.ParentFont.Should().Be($"{family}, 17");
            row.EffectiveFamily.Should().Be(family);
            row.EffectiveSize.Should().Be(17);
        }

        footnotes.ParentPath.Should().Be(
            Strings.Get("PreferencesWindow_Fonts") + " › " + Strings.Get("PreferencesWindow_TooltipFontOtherWindows"),
            "the path starts at the fonts page, the window has one place to set a font");
    }

    [Fact]
    public void Turning_the_inheritance_off_uses_the_own_font_and_restore_defaults_turns_it_on_again()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        string own = new Fixture().Create<string>();

        // Act
        vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipOtherWindows").Size = 17;
        FontSettingViewModel footnotes = vm.FootnotesFonts.Fonts.Single();
        footnotes.Family = own;
        footnotes.Size = 20;

        // Assert
        footnotes.EffectiveSize.Should().Be(17, "the own font is stored, but not used while the setting inherits");

        // Act
        footnotes.InheritFromParent = false;

        // Assert
        footnotes.IsOwnFont.Should().BeTrue();
        footnotes.EffectiveFamily.Should().Be(own);
        footnotes.EffectiveSize.Should().Be(20);

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.GetTooltipInherit(TooltipArea.Footnotes).Should().BeFalse();
        host.Settings.GetEffectiveTooltipFont(TooltipArea.Footnotes).Should().Be(new TooltipFont(own, 20));

        // Act
        footnotes.RestoreDefault();

        // Assert
        footnotes.InheritFromParent.Should().BeTrue();
        footnotes.Size.Should().Be(TooltipFont.Default.FontSize);
    }

    [Fact]
    public void Book_Browser_has_a_checkbox_per_folder_for_expanding_it_on_load_saved_with_the_others()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.BookBrowserFolders.Select(o => o.Label).Should().Equal("Text", "Styles", "Images", "Fonts", "Audio", "Video", "Misc");
        vm.BookBrowserFolders.Where(o => o.IsChecked).Select(o => o.Label).Should().Equal("Text");

        // Act
        vm.BookBrowserFolders.Single(o => o.Label == "Video").IsChecked = true;
        vm.BookBrowserFolders.Single(o => o.Label == "Text").IsChecked = false;

        // Assert
        host.Settings.GetBookBrowserExpandOnLoad(OpfModelGroupKind.Video).Should().BeFalse("not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.GetBookBrowserExpandOnLoad(OpfModelGroupKind.Video).Should().BeTrue();
        host.Settings.GetBookBrowserExpandOnLoad(OpfModelGroupKind.Text).Should().BeFalse();

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        vm.BookBrowserFolders.Where(o => o.IsChecked).Select(o => o.Label).Should().Equal("Text");
    }

    [Fact]
    public void Book_Browser_look_is_saved_with_the_others_and_restored_with_the_appearance_defaults()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.BookBrowserIndent.Should().Be(20);
        vm.BookBrowserShowFolders.Should().BeFalse();

        // Act
        vm.BookBrowserIndent = 35;
        vm.BookBrowserShowFolders = true;

        // Assert
        vm.BookBrowserCellMinHeight.Should().Be(16);

        // Act
        vm.BookBrowserCellMinHeight = 24;
        vm.BookBrowserCellPadding = 2;
        vm.BookBrowserItemsFontFamily = "Consolas";
        vm.BookBrowserItemsFontSize = 14;
        vm.BookBrowserHeaderFontFamily = "Arial";
        vm.BookBrowserHeaderFontSize = 11;

        // Assert
        host.Settings.BookBrowserAppearance.Should().Be(BookBrowserAppearance.Default, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.BookBrowserAppearance.Should().Be(new BookBrowserAppearance("Consolas", 14, "Arial", 11, 35, 24, 2, true));

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        (vm.BookBrowserIndent, vm.BookBrowserShowFolders, vm.BookBrowserItemsFontFamily, vm.BookBrowserHeaderFontSize,
                vm.BookBrowserCellMinHeight, vm.BookBrowserCellPadding)
            .Should().Be((20, false, string.Empty, 0, 16, 0));
    }

    [Fact]
    public void The_sample_Book_Browser_of_the_preview_shows_a_whole_small_book_in_every_column()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;

        BookBrowserViewModel sample = vm.BookBrowserSample;

        sample.IsSample.Should().BeTrue();
        sample.HasBook.Should().BeTrue();
        sample.OptionalColumns.Should().Be("ReadingOrder,Semantics,Properties", "the preview shows every column, whatever the user chose");
        sample.Nodes.Where(n => n.IsFolder).Should().OnlyContain(n => n.IsExpanded, "the folders are open, so the fonts show on many rows");
        BookBrowserNode[] files = sample.Nodes.SelectMany(n => n.Children).ToArray();
        files.Select(n => n.Header).Should().Contain("Chapter01.xhtml").And.Contain("Stylesheet.css").And.Contain("cover.png")
            .And.Contain("Serif-Regular.ttf");
        files.Should().Contain(n => n.SemanticsText.Length > 0, "the cover has a semantics");
        files.Select(n => n.PropertiesText).Should().Contain(p => p.Contains("nav")).And.Contain(p => p.Contains("cover-image"));
        files.Where(n => n.ReadingOrderNumber is not null).Should().HaveCountGreaterThan(3, "the chapters are in the reading order");

        // Act
        sample.OptionalColumns = "Semantics";

        // Assert
        sample.OptionalColumns.Should().Be("ReadingOrder,Semantics,Properties");
        host.Settings.BookBrowserColumns.Should().NotBe("Semantics", "the columns of the real Book Browser are not touched");
    }

    [Fact]
    public void The_look_of_the_start_screen_is_saved_with_the_others_and_restored_with_the_appearance_defaults()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;

        // Act & Assert
        vm.StartScreenIndent.Should().Be(StartScreenAppearance.DefaultIndent);

        // Act
        vm.StartScreenIndent = 24;
        vm.StartScreenTitleFontFamily = "Georgia";
        vm.StartScreenTitleFontSize = 16;
        vm.StartScreenLocationFontSize = 9;
        vm.StartScreenSectionFontWeight = 700;

        // Assert
        host.Settings.StartScreenAppearance.Should().Be(StartScreenAppearance.Default, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.StartScreenAppearance.Should().Be(
            new StartScreenAppearance("Georgia", 16, string.Empty, 9, string.Empty, 0, 24, 0, 0, 700));

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        (vm.StartScreenIndent, vm.StartScreenTitleFontFamily, vm.StartScreenTitleFontSize, vm.StartScreenLocationFontSize,
                vm.StartScreenSectionFontWeight)
            .Should().Be((StartScreenAppearance.DefaultIndent, string.Empty, 0, 0, 0));
    }

    [Fact]
    public void The_start_screen_fonts_page_has_the_three_fonts_of_the_list_and_the_tooltip_with_their_defaults()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;

        // Act & Assert
        vm.StartScreenFonts.Fonts.Select(f => f.Key).Should().Equal(
            "StartScreenTitles", "StartScreenLocations", "StartScreenSection", "Tooltip" + TooltipArea.StartScreen);
        FontSettingViewModel titles = vm.StartScreenFonts.Fonts[0];
        FontSettingViewModel locations = vm.StartScreenFonts.Fonts[1];
        FontSettingViewModel section = vm.StartScreenFonts.Fonts[2];

        titles.EffectiveSize.Should().Be(vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface").EffectiveSize, "the names have the interface size");
        locations.EffectiveSize.Should().Be(11, "the folder is a little smaller while its size is not set");
        section.EffectiveWeight.Should().Be(600, "the heading is semi-bold while its weight is not set");

        // Act
        vm.StartScreenLocationFontSize = 13;

        // Assert
        locations.EffectiveSize.Should().Be(13);
        locations.Summary.Should().Contain("13");
    }

    [Fact]
    public void The_sample_start_screen_shows_the_number_of_files_of_the_draft_with_one_entry_highlighted()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;
        StartScreenViewModel sample = vm.StartScreenSample;

        sample.IsSample.Should().BeTrue();
        sample.IsVisible.Should().BeTrue();
        sample.RecentFileItems.Should().NotBeEmpty();
        sample.RecentFileItems.Should().HaveCountLessThanOrEqualTo(vm.StartScreenRecentFilesLimit);
        sample.RecentFileItems.Where(f => f.IsHighlighted).Should().ContainSingle();

        // Act
        vm.StartScreenRecentFilesLimit = 4;

        // Assert
        sample.RecentFileItems.Should().HaveCount(4, "the preview follows the draft");
        host.Settings.StartScreenRecentFilesLimit.Should().NotBe(4, "the draft is not saved yet");

        // Act
        vm.RecentFilesLimit = 2;

        // Assert
        sample.RecentFileItems.Should().HaveCount(4, "the number of the entries of the File menu does not change the start screen");

        // Act
        vm.StartScreenRecentFilesLimit = 0;

        // Assert
        sample.HasRecentFiles.Should().BeFalse();

        int changes = 0;
        sample.AppearanceChanged += (_, _) => changes++;

        // Act
        vm.StartScreenIndent = 30;
        vm.StartScreenTitleFontSize = 18;

        // Assert
        sample.Appearance.Indent.Should().Be(30);
        sample.Appearance.TitleFontSize.Should().Be(18);
        changes.Should().BeGreaterThan(0);
        host.Settings.StartScreenAppearance.Should().Be(StartScreenAppearance.Default, "the preview shows what is not saved yet");
    }

    [Fact]
    public void The_sample_Code_Editor_tab_shows_a_chapter_and_follows_the_unsaved_look()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;
        Signet.App.ViewModels.Tabs.CodeTabViewModel sample = vm.CodeEditorSample;
        int changes = 0;
        sample.AppearanceChanged += (_, _) => changes++;

        sample.IsSample.Should().BeTrue();
        sample.Document.Text.Should().Contain("<h1 id=\"start\">").And.Contain("Missing.xhtml");
        sample.LinkTargetExists("Missing.xhtml").Should().BeFalse("the sample has one broken link, so its highlighting shows");
        sample.LinkTargetExists("Chapter02.xhtml").Should().BeTrue();
        sample.SearchHighlights.Should().ContainSingle();
        sample.ViewState.Should().NotBeNull("the caret rests in a tag when the view is shown");

        // Act
        vm.CodeEditor.FontSize = 19;
        vm.OpenTagHintFontSize = 14;
        vm.ExtendedHighlighting = !vm.ExtendedHighlighting;

        // Assert
        sample.Appearance.FontSize.Should().Be(19);
        sample.OpenTagHintAppearance.FontSize.Should().Be(14);
        sample.ExtendedHighlighting.Should().Be(vm.ExtendedHighlighting);
        changes.Should().BeGreaterThan(2);
        host.Settings.CodeEditorAppearance.Should().Be(CodeEditorAppearance.Default, "the preview shows what is not saved yet");
    }

    [Fact]
    public void The_sample_Find_Usages_panel_shows_fabricated_usages_and_follows_the_unsaved_look()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;
        FindUsagesViewModel sample = vm.FindUsagesSample;
        int changes = 0;
        sample.AppearanceChanged += (_, _) => changes++;

        sample.IsSample.Should().BeTrue();
        sample.HasSearch.Should().BeTrue();
        sample.GroupByFile.Should().BeTrue("the sample shows the usages under their files");
        sample.Roots.Should().ContainSingle().Which.Children.Select(c => c.Text)
            .Should().Equal("Styles/Stylesheet.css", "Text/Chapter01.xhtml", "Text/Chapter02.xhtml", "Text/Chapter03.xhtml");

        // Act
        vm.FindUsagesCellsFontSize = 17;
        vm.FindUsagesRowSpacing = 7;
        vm.FindUsagesFitUsageColumn = !vm.FindUsagesFitUsageColumn;

        // Assert
        sample.Appearance.CellsFontSize.Should().Be(17);
        changes.Should().BeGreaterThan(0);
        sample.RowSpacing.Should().Be(7, "the sample shows the spacing as it is edited");
        sample.FitUsageColumn.Should().Be(vm.FindUsagesFitUsageColumn);

        // What is done in the sample is not written to the settings.
        // Act
        sample.WrapRows = !sample.WrapRows;
        sample.GroupByFile = false;
        sample.UsageColumnWidth = 321;

        // Assert
        host.Settings.FindUsagesWrapRows.Should().BeFalse();
        host.Settings.FindUsagesGroupByFile.Should().Be(new SettingsStore(_disposables.Add(new TempDir()).Combine("defaults.xml")).FindUsagesGroupByFile, "the default");
        host.Settings.FindUsagesUsageColumnWidth.Should().NotBe(321);
        host.Settings.FindUsagesAppearance.CellsFontSize.Should().Be(0, "the preview shows what is not saved yet");
    }

    [Fact]
    public void The_sample_Find_and_Replace_panel_has_no_book_and_keeps_what_is_typed_in_it_to_itself()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;
        FindReplaceViewModel sample = vm.FindReplaceSample;

        sample.FindText.Should().NotBeEmpty("the sample has something to find");

        // Act
        sample.FindText = "typed in the preview";
        sample.FindNextCommand.Execute(null);

        // Assert
        host.Settings.GetFindReplaceSettings().FindHistory.Should().NotContain("typed in the preview");
        host.Settings.GetFindReplaceSettings().FindHistory.Should().NotContain(sample.FindText);
    }

    [Fact]
    public void The_sample_Cleanup_dialog_has_something_to_clean_in_every_tab()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;

        CleanupViewModel sample = vm.CleanupSample;

        sample.Tabs.Should().NotBeEmpty();
        sample.Tabs.SelectMany(t => t.Sections).Should().Contain(s => s.HasItems, "the sample book has rules to merge and elements to remove");

        // Act
        sample.Tabs.First().CleanCommand.Execute(null);

        // Assert
        sample.Tabs.SelectMany(t => t.Sections).Should().Contain(s => s.HasItems, "Clean changes nothing in the sample");
    }

    [Fact]
    public void The_sample_Book_Browser_follows_the_unsaved_look_and_leaves_the_saved_one_alone()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        using PreferencesViewModel __ = vm;
        BookBrowserViewModel sample = vm.BookBrowserSample;
        int changes = 0;
        sample.AppearanceChanged += (_, _) => changes++;

        // Act
        vm.BookBrowserItemsFontSize = 19;
        vm.BookBrowserIndent = 33;

        // Assert
        sample.Appearance.ItemsFontSize.Should().Be(19);
        sample.Appearance.Indent.Should().Be(33);
        changes.Should().BeGreaterThan(0);
        host.Settings.BookBrowserAppearance.Should().Be(BookBrowserAppearance.Default, "the preview shows what is not saved yet");
    }

    [Fact]
    public void Line_numbers_font_inherits_the_editor_font_until_it_is_unchecked_and_is_saved_with_the_others()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel row = vm.CodeEditorFonts.Fonts.Single(f => f.Key == "LineNumbers");

        // Act & Assert
        vm.CodeEditorFonts.Fonts.Select(f => f.Key).Should().Equal("CodeEditorText", "LineNumbers", "OpenTagHint", "DocumentTabs");
        row.HasInheritance.Should().BeTrue();
        row.InheritFromParent.Should().BeTrue("by default the numbers have the font of the editor");
        row.EffectiveFamily.Should().Be(vm.CodeEditor.FontFamily);
        row.EffectiveSize.Should().Be(vm.CodeEditor.FontSize);
        row.Parent.Should().BeSameAs(vm.CodeEditorFonts.Fonts[0]);

        // Act
        row.InheritFromParent = false;

        // Assert
        row.IsOwnFont.Should().BeTrue();
        (vm.LineNumbersFontFamily, vm.LineNumbersFontSize).Should().Be((vm.CodeEditor.FontFamily, vm.CodeEditor.FontSize),
            "unchecking starts from the editor font");

        // Act
        vm.LineNumbersFontSize = 17;

        // Assert
        host.Settings.LineNumberFontAppearance.Should().Be(LineNumberFontAppearance.Default, "not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.LineNumberFontAppearance.Should().Be(new LineNumberFontAppearance(vm.CodeEditor.FontFamily, 17));

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        row.InheritFromParent.Should().BeTrue();
        (vm.LineNumbersFontFamily, vm.LineNumbersFontSize).Should().Be((string.Empty, 0));
    }

    [Fact]
    public void Book_Browser_Fonts_page_has_rows_for_the_names_and_for_the_headers_before_the_tooltip()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.BookBrowserFonts.Fonts.Select(f => f.Key).Should().Equal(
            "BookBrowserItems", "BookBrowserHeaders", "Tooltip" + TooltipArea.BookBrowser);
        FontSettingViewModel items = vm.BookBrowserFonts.Fonts[0];
        items.Placeholder.Should().Be(vm.EffectiveUiFontFamily, "an empty typeface is the interface font");

        // Act
        items.Size = 15;
        items.Family = "Consolas";

        // Assert
        vm.BookBrowserItemsFontSize.Should().Be(15);
        vm.BookBrowserItemsFontFamily.Should().Be("Consolas");
        vm.BookBrowserFonts.Fonts[1].Size.Should().Be(0, "the headers have a font of their own");
    }

    [Fact]
    public void The_preferences_window_is_modal_by_default_and_it_is_a_setting_saved_with_the_others()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act & Assert
        vm.PreferencesWindowModal.Should().BeTrue();

        // Act
        vm.PreferencesWindowModal = false;

        // Assert
        host.Settings.PreferencesWindowModal.Should().BeTrue("not saved yet");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        host.Settings.PreferencesWindowModal.Should().BeFalse();

        // Act
        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        vm.PreferencesWindowModal.Should().BeTrue();
    }

    [Fact]
    public void Restore_appearance_defaults_resets_the_tooltip_fonts()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel main = vm.AppearanceFonts.Fonts.Single(f => f.Key == "TooltipMainWindow");

        // Act
        main.Size = 20;

        vm.RestoreAppearanceDefaultsCommand.Execute(null);

        // Assert
        main.Size.Should().Be(TooltipFont.Default.FontSize);
    }

    [Fact]
    public void Restore_defaults_of_a_font_page_resets_only_its_fonts()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.PreviewFontStandard = new Fixture().Create<string>();
        vm.PreviewFontSize = 22;
        vm.SpecialCharacterFontSize = 40;

        vm.PreviewFonts.RestoreDefaultsCommand.Execute(null);

        // Assert
        vm.PreviewFontStandard.Should().Be(PreviewAppearance.Default.FontFamilyStandard);
        vm.PreviewFontSize.Should().Be(PreviewAppearance.Default.FontSize);
        vm.SpecialCharacterFontSize.Should().Be(40, "another page");
    }

    [Fact]
    public void The_warning_size_0_is_previewed_at_the_interface_size_in_the_warning_color_of_the_theme()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel ui = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");
        FontSettingViewModel warnings = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Warnings");

        // Act & Assert
        warnings.HasFamily.Should().BeFalse();
        warnings.Size.Should().Be(0);
        warnings.EffectiveSize.Should().Be(ui.EffectiveSize);
        warnings.EffectiveFamily.Should().Be(ui.EffectiveFamily);

        warnings.PreviewForeground.Should().Be(AppTheme.Current.Color(ThemeKeys.UiWarningText).ToString());
    }

    [Fact]
    public void The_interface_font_row_describes_the_default_and_stores_a_chosen_family()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;
        FontSettingViewModel ui = vm.AppearanceFonts.Fonts.Single(f => f.Key == "Interface");
        string family = new Fixture().Create<string>();

        // Act & Assert
        ui.Family.Should().BeEmpty();
        ui.Placeholder.Should().Be(vm.UiFontDescription);

        // Act
        ui.Family = family;

        // Assert
        vm.UiFontFamily.Should().Be(family);
        ui.EffectiveFamily.Should().Be(family);
    }

    [Fact]
    public async Task ChooseUiFont_stores_family_and_size_and_ResetUiFont_restores_the_mode_default()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.CompactUi = true;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult("Tahoma", 13));
        };

        await vm.ChooseUiFontCommand.ExecuteAsync(null);

        // Assert
        asked!.Size.Should().Be(12, "the default size of the compact mode");
        asked.Family.Should().Be("Segoe UI", "the window starts with the font actually in use, so that OK is enabled");

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        (host.Settings.UiFont, host.Settings.UiFontSize).Should().Be(("Tahoma", 13));
        vm.UiFontDescription.Should().Be("Tahoma, 13 px");

        // Act
        vm.ResetUiFontCommand.Execute(null);
        vm.ApplyCommand.Execute(null);

        // Assert
        (host.Settings.UiFont, host.Settings.UiFontSize).Should().Be((string.Empty, 0));
        vm.UiFontDescription.Should().Contain("Segoe UI").And.Contain("12");
    }

    [Fact]
    public void Standard_mode_default_UI_font_falls_back_to_the_system_font_when_Inter_is_not_installed()
    {
        // Arrange
        (TestHost host, PreferencesViewModel vm) = New();
        using TestHost _ = host;

        // Act
        vm.CompactUi = false;

        // Act & Assert
        vm.EffectiveUiFontFamily.Should().Be("Segoe UI");
        vm.UiFontDescription.Should().Contain("14");
    }
}
