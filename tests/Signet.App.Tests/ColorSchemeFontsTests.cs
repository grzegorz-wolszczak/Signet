using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Themes;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Appearance › Color Scheme › Fonts (<see cref="PreferencesViewModel.ThemeFonts"/>, <see cref="ColorSchemeViewModel.UseThemeFonts"/>).</summary>
public sealed class ColorSchemeFontsTests : IDisposable
{
    private static readonly Fixture Any = new();
    private readonly TestHost _host = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "scheme-fonts-" + Guid.NewGuid().ToString("N"));
    private readonly Queue<string?> _names = new();
    private ThemeStore? _store;

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PreferencesViewModel New()
    {
        LocalizationManager localization = new(_host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(_host.Settings, NullLogger<IconThemeManager>.Instance);
        SpellChecker spellChecker = new(_host.Settings, Path.Combine(_root, "hunspell"), Path.Combine(_root, "user"));
        UiDensityManager uiDensity = new(_host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        _store = new ThemeStore(Path.Combine(_root, "themes"));
        ThemeManager themes = new(_host.Settings, _store, NullLogger<ThemeManager>.Instance);
        PreferencesViewModel vm = new(_host.Settings, spellChecker, localization, iconTheme, _host.Shortcuts, _host.Registry, uiDensity, themes);
        vm.ColorScheme.TextPrompt = (_, _, _) => Task.FromResult(_names.Dequeue());
        return vm;
    }

    private static FontSettingViewModel Row(PreferencesViewModel vm, string key) => vm.ThemeFonts.Fonts.Single(f => f.Key == key);

    [Fact]
    public void The_page_has_a_row_for_every_font_of_the_fonts_pages_under_its_section()
    {
        PreferencesViewModel vm = New();

        vm.ThemeFonts.Fonts.Select(f => f.Key).Should().Equal(
            new[]
            {
                vm.AppearanceFonts, vm.StartScreenFonts, vm.BookBrowserFonts, vm.TableOfContentsFonts, vm.CodeViewFonts,
                vm.PreviewFonts, vm.FindReplaceFonts, vm.SpecialCharacterFonts, vm.CheckpointsFonts, vm.ValidationFonts,
                vm.MetadataEditorFonts, vm.CleanupFonts, vm.FootnotesFonts,
            }.SelectMany(p => p.Fonts).Select(f => f.Key));
        vm.ThemeFonts.Fonts.Should().OnlyContain(f => !string.IsNullOrEmpty(f.Group));
        vm.ThemeFonts.Fonts.Select(f => f.Group).Distinct().Should().HaveCount(13);
    }

    [Fact]
    public void Turning_the_theme_fonts_on_copies_the_fonts_of_the_settings_into_the_theme()
    {
        PreferencesViewModel vm = New();
        vm.CodeView.FontFamily = "Consolas";
        vm.CodeView.FontSize = 14;
        ColorSchemeViewModel sut = vm.ColorScheme;
        _names.Enqueue(Any.Create<string>());
        sut.DuplicateCommand.Execute(null);

        sut.UseThemeFonts = true;

        sut.Current!.UseThemeFonts.Should().BeTrue();
        sut.Current.Fonts.Keys.Should().BeEquivalentTo(ThemeFontKeys.All);
        sut.Current.Fonts[ThemeFontKeys.CodeViewEditor].Should().Be(new ThemeFont("Consolas", 14));
        Row(vm, "CodeViewEditor").Family.Should().Be("Consolas");
        vm.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void A_font_edited_on_the_page_belongs_to_the_theme_and_the_settings_stay()
    {
        PreferencesViewModel vm = New();
        vm.ColorScheme.UseThemeFonts = true;
        string family = vm.CodeView.FontFamily;

        Row(vm, "CodeViewEditor").Family = "DejaVu Sans Mono";
        Row(vm, "CodeViewEditor").Size = 17;
        Row(vm, "PreviewSerif").Family = "Garamond";
        Row(vm, "TooltipBookBrowser").Size = 11;

        ThemeFonts fonts = vm.ColorScheme.Resolved!.Fonts;
        fonts.CodeView(CodeViewAppearance.Default).Should().Be(new CodeViewAppearance { FontFamily = "DejaVu Sans Mono", FontSize = 17 });
        fonts.Preview(PreviewAppearance.Default).FontFamilySerif.Should().Be("Garamond");
        fonts.Tooltip(TooltipArea.BookBrowser, TooltipFont.Default).FontSize.Should().Be(11);
        vm.CodeView.FontFamily.Should().Be(family, "the default font of the settings does not change");
        Row(vm, "CodeViewEditor").EffectiveFamily.Should().Be("DejaVu Sans Mono");
    }

    [Fact]
    public void Restoring_a_font_copies_the_one_of_the_settings_again()
    {
        PreferencesViewModel vm = New();
        vm.ColorScheme.UseThemeFonts = true;
        FontSettingViewModel row = Row(vm, "SpecialCharacters");
        ThemeFont setting = vm.ColorScheme.FontOf(ThemeFontKeys.SpecialCharacters);
        row.Family = "Segoe UI Symbol";
        row.Size = 30;

        row.RestoreDefault();

        vm.ColorScheme.FontOf(ThemeFontKeys.SpecialCharacters).Should().Be(setting);
    }

    [Fact]
    public void Turning_the_theme_fonts_off_keeps_them_in_the_theme_and_saving_stores_them()
    {
        PreferencesViewModel vm = New();
        ColorSchemeViewModel sut = vm.ColorScheme;
        sut.UseThemeFonts = true;
        Row(vm, "Interface").Family = "Inter";

        sut.UseThemeFonts = false;
        sut.Apply();

        Theme saved = _store!.Get(ThemeStore.LightId);
        saved.UseThemeFonts.Should().BeFalse();
        saved.Fonts[ThemeFontKeys.Interface].Family.Should().Be("Inter");
        new ThemeFonts(saved, _store.Resolver).Interface("Segoe UI", 12).Should().Be(("Segoe UI", 12));
    }

    [Fact]
    public async Task The_font_of_the_previews_is_a_monospaced_setting_of_the_program_that_the_pages_follow()
    {
        PreferencesViewModel vm = New();
        string family = Any.Create<string>();
        int size = Any.Create<int>() % 20 + 8;
        FontPickRequest? asked = null;
        vm.FontPicker = r =>
        {
            asked = r;
            return Task.FromResult<FontPickResult?>(new FontPickResult(family, size));
        };
        ColorSchemePageViewModel css = vm.ColorScheme.PageFor(ThemePage.Css);
        List<string?> changed = new();
        css.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ColorSchemePreviewFont.Family.Should().Be(ColorSchemePreviewAppearance.Default.FontFamily);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.ColorSchemePreviewFont.ChooseCommand!).ExecuteAsync(null);

        asked!.MonospaceOnly.Should().BeTrue();
        css.PreviewFont.Should().Be(new ColorSchemePreviewAppearance(family, size));
        _host.Settings.ColorSchemePreviewAppearance.Should().Be(ColorSchemePreviewAppearance.Default, "a setting waits for Save");
        changed.Should().Contain(nameof(ColorSchemePageViewModel.PreviewFont));
        vm.ColorScheme.UseThemeFonts.Should().BeFalse("the font is not part of the theme");
        vm.ColorScheme.HasChanges.Should().BeFalse("nor does it change the theme");

        vm.ApplyCommand.Execute(null);
        _host.Settings.ColorSchemePreviewAppearance.Should().Be(new ColorSchemePreviewAppearance(family, size));

        vm.ColorSchemePreviewFont.RestoreDefault();
        css.PreviewFont.Should().Be(ColorSchemePreviewAppearance.Default);
    }
}
