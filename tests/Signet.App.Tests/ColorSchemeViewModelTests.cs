using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Spellcheck;
using Signet.Core.Themes;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Preferences › Appearance › Color Scheme (<see cref="ColorSchemeViewModel"/>).</summary>
public sealed class ColorSchemeViewModelTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "scheme-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _messages = new();
    private readonly Queue<string?> _names = new();

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ThemesFolder => Path.Combine(_root, "themes");

    private PreferencesViewModel New()
    {
        LocalizationManager localization = new(_host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(_host.Settings, NullLogger<IconThemeManager>.Instance);
        SpellChecker spellChecker = new(_host.Settings, Path.Combine(_root, "hunspell"), Path.Combine(_root, "user"));
        UiDensityManager uiDensity = new(_host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        ThemeManager themes = new(_host.Settings, new ThemeStore(ThemesFolder), NullLogger<ThemeManager>.Instance);
        PreferencesViewModel vm = new(_host.Settings, spellChecker, localization, iconTheme, _host.Shortcuts, _host.Registry, uiDensity, themes);
        vm.ColorScheme.TextPrompt = (_, _, _) => Task.FromResult(_names.Dequeue());
        vm.ColorScheme.Confirm = (_, _) => Task.FromResult(true);
        vm.ColorScheme.Message = (_, text) =>
        {
            _messages.Add(text);
            return Task.CompletedTask;
        };
        return vm;
    }

    [Fact]
    public void The_theme_in_use_is_chosen_in_the_window_and_applied_on_save()
    {
        PreferencesViewModel vm = New();
        ColorSchemeViewModel sut = vm.ColorScheme;

        sut.Themes.Select(t => t.Name).Should().Equal("Signet Light", "Signet Dark");
        sut.Selected!.Id.Should().Be(ThemeStore.LightId);

        sut.Selected = sut.Themes.Single(t => t.Id == ThemeStore.DarkId);

        vm.HasChanges.Should().BeTrue();
        _host.Settings.UiThemeId.Should().Be(ThemeStore.LightId, "not saved yet");
        vm.ApplyCommand.Execute(null);
        _host.Settings.UiThemeId.Should().Be(ThemeStore.DarkId);
    }

    [Fact]
    public void A_change_to_a_bundled_theme_is_saved_on_save_and_restore_defaults_removes_it()
    {
        PreferencesViewModel vm = New();
        ColorSchemeViewModel sut = vm.ColorScheme;
        sut.CanRestoreDefaults.Should().BeFalse();

        sut.Variant = sut.Variants.Single(v => v.Value == InterfaceVariant.Dark);
        sut.IconSet = sut.IconSets.Single(o => o.Code == "material");

        vm.HasChanges.Should().BeTrue("the theme changed");
        sut.CanRestoreDefaults.Should().BeTrue();
        new ThemeStore(ThemesFolder).IsModified(ThemeStore.LightId).Should().BeFalse("not saved yet");
        vm.ApplyCommand.Execute(null);
        vm.HasChanges.Should().BeFalse();
        ThemeStore saved = new(ThemesFolder);
        saved.Resolver.Variant(saved.Get(ThemeStore.LightId)).Should().Be(InterfaceVariant.Dark);
        saved.Resolver.IconSet(saved.Get(ThemeStore.LightId)).Should().Be("material");

        sut.RestoreDefaultsCommand.Execute(null);

        new ThemeStore(ThemesFolder).IsModified(ThemeStore.LightId).Should().BeFalse();
        sut.Variant!.Value.Should().Be(InterfaceVariant.Light);
        sut.IconSet!.Code.Should().BeNull("as set in Appearance");
    }

    [Fact]
    public async Task Duplicate_makes_a_theme_of_the_user_with_the_changes_not_saved_yet_and_selects_it()
    {
        PreferencesViewModel vm = New();
        ColorSchemeViewModel sut = vm.ColorScheme;
        sut.Variant = sut.Variants.Single(v => v.Value == InterfaceVariant.Dark);
        _names.Enqueue("Sepia");

        await sut.DuplicateCommand.ExecuteAsync(null);

        sut.Selected!.Name.Should().Be("Sepia");
        sut.Selected.IsBundled.Should().BeFalse();
        sut.Variant!.Value.Should().Be(InterfaceVariant.Dark, "the copy keeps the change made before");
        sut.RenameCommand.CanExecute(null).Should().BeTrue();
        new ThemeStore(ThemesFolder).Themes.Select(t => t.Name).Should().Contain("Sepia");
    }

    [Fact]
    public async Task A_taken_name_is_refused_with_a_message()
    {
        ColorSchemeViewModel sut = New().ColorScheme;
        _names.Enqueue("signet dark");

        await sut.DuplicateCommand.ExecuteAsync(null);

        sut.Themes.Should().HaveCount(2);
        _messages.Should().Equal(Strings.Format("ColorScheme_NameTaken", "signet dark"));
    }

    [Fact]
    public async Task Only_a_theme_of_the_user_is_renamed_or_deleted_and_its_parent_is_selected_after_deleting()
    {
        ColorSchemeViewModel sut = New().ColorScheme;
        sut.Selected = sut.Themes.Single(t => t.Id == ThemeStore.DarkId);
        sut.RenameCommand.CanExecute(null).Should().BeFalse();
        sut.DeleteCommand.CanExecute(null).Should().BeFalse();
        _names.Enqueue("Night");
        await sut.DuplicateCommand.ExecuteAsync(null);
        _names.Enqueue("Midnight");

        await sut.RenameCommand.ExecuteAsync(null);
        sut.Selected!.Name.Should().Be("Midnight");
        await sut.DeleteCommand.ExecuteAsync(null);

        sut.Selected!.Id.Should().Be(ThemeStore.DarkId, "the parent of the deleted theme");
        sut.Themes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_and_import_carry_a_theme_and_a_broken_file_gives_a_message()
    {
        ColorSchemeViewModel sut = New().ColorScheme;
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "exported" + ThemeXml.Extension);
        sut.Selected = sut.Themes.Single(t => t.Id == ThemeStore.DarkId);
        sut.IconSet = sut.IconSets.Single(o => o.Code == "fluent");
        sut.PickExportPath = _ => Task.FromResult<string?>(file);
        sut.PickImportPath = () => Task.FromResult<string?>(file);

        await sut.ExportCommand.ExecuteAsync(null);
        await sut.ImportCommand.ExecuteAsync(null);

        sut.Selected!.Name.Should().Be("Signet Dark (2)");
        sut.IconSet!.Code.Should().Be("fluent", "the export had the change not saved yet");
        sut.Variant!.Value.Should().Be(InterfaceVariant.Dark);

        File.WriteAllText(file, "<scheme name=\"Darcula\" />");
        await sut.ImportCommand.ExecuteAsync(null);

        _messages.Should().ContainSingle().Which.Should().StartWith(Strings.Format("ColorScheme_ImportFailed", string.Empty));
    }
}
