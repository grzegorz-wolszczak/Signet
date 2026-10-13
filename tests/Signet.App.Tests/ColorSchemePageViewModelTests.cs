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
using Signet.Core.Spellcheck;
using Signet.Core.Themes;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The pages of the keys of Appearance › Color Scheme (<see cref="ColorSchemePageViewModel"/>).</summary>
public sealed class ColorSchemePageViewModelTests : IDisposable
{
    private static readonly Fixture Any = new();
    private readonly TestHost _host = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "scheme-page-" + Guid.NewGuid().ToString("N"));
    private readonly Queue<string?> _names = new();

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ColorSchemeViewModel New(string themeId = ThemeStore.LightId)
    {
        LocalizationManager localization = new(_host.Settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(_host.Settings, NullLogger<IconThemeManager>.Instance);
        SpellChecker spellChecker = new(_host.Settings, Path.Combine(_root, "hunspell"), Path.Combine(_root, "user"));
        UiDensityManager uiDensity = new(_host.Settings, iconTheme, NullLogger<UiDensityManager>.Instance, () => "Segoe UI", _ => false);
        ThemeManager themes = new(_host.Settings, new ThemeStore(Path.Combine(_root, "themes")), NullLogger<ThemeManager>.Instance);
        PreferencesViewModel vm = new(_host.Settings, spellChecker, localization, iconTheme, _host.Shortcuts, _host.Registry, uiDensity, themes);
        vm.ColorScheme.TextPrompt = (_, _, _) => Task.FromResult(_names.Dequeue());
        vm.ColorScheme.Selected = vm.ColorScheme.Themes.Single(t => t.Id == themeId);
        return vm.ColorScheme;
    }

    // A theme of the user with Signet Dark as the parent, selected.
    private ColorSchemeViewModel NewWithUserTheme()
    {
        ColorSchemeViewModel sut = New(ThemeStore.DarkId);
        _names.Enqueue(Any.Create<string>());
        sut.DuplicateCommand.Execute(null);
        return sut;
    }

    private static ColorSchemePageViewModel Select(ColorSchemeViewModel scheme, ThemeKey key)
    {
        ColorSchemePageViewModel page = scheme.PageFor(key.Page);
        page.SelectKey(key);
        return page;
    }

    [Fact]
    public void Every_key_is_in_the_tree_of_its_page_under_its_group()
    {
        ColorSchemeViewModel sut = New();

        foreach (ThemePage page in Enum.GetValues<ThemePage>())
        {
            ColorSchemePageViewModel vm = sut.PageFor(page);
            vm.Page.Should().Be(page);
            List<ThemeKey> keys = vm.Roots.SelectMany(r => r.IsGroup ? r.Children : new[] { r }.AsEnumerable()).Select(n => n.Key!).ToList();
            keys.Should().Equal(ThemeKeys.On(page));
            vm.Roots.Where(r => r.IsGroup).Should().OnlyContain(g => g.Children.Count > 0 && g.Name.Length > 0);
        }

        sut.PageFor(ThemePage.Preview).Roots[0].Key.Should().Be(ThemeKeys.PreviewHighlight, "a key without a group is at the top");
        sut.PageFor(ThemePage.Interface).Roots.Single(r => r.IsGroup && r.Children[0].Key!.Id.StartsWith("fluent.", StringComparison.Ordinal))
            .Children.Select(n => n.Name).Should().Contain("RegionColor", "the Fluent palette keeps the names of its colors");
    }

    [Fact]
    public void A_color_typed_for_a_key_changes_the_theme_until_save_and_marks_the_key_and_its_group()
    {
        ColorSchemeViewModel sut = New();
        int changed = 0;
        sut.Changed += (_, _) => changed++;
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.XhtmlTag);

        page.Foreground = "not a color";
        sut.HasChanges.Should().BeFalse("a text that is not a color is ignored");

        page.Foreground = "#123456";

        sut.Current!.Entries[ThemeKeys.XhtmlTag.Id].Attributes!.Foreground.Should().Be(ThemeColor.Parse("#123456"));
        page.Foreground.Should().Be("#123456");
        sut.HasChanges.Should().BeTrue();
        changed.Should().Be(1);
        page.IsModified.Should().BeTrue();
        page.Selected!.IsModified.Should().BeTrue();
        page.Roots.Single(r => r.Children.Contains(page.Selected)).IsModified.Should().BeTrue();
        sut.PageFor(ThemePage.Css).Roots.SelectMany(r => r.Children).Should().OnlyContain(n => !n.IsModified);
    }

    [Fact]
    public void The_attributes_of_a_key_are_edited_one_by_one()
    {
        ColorSchemeViewModel sut = New();
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.CssProperty);

        page.Bold = true;
        page.Italic = true;
        page.HasBackground = true;
        page.Background = "#80FFEEDD";
        page.HasEffect = true;
        page.Effect = page.Effects.Single(e => e.Value == EffectType.Wave);
        page.HasEffectColor = true;
        page.EffectColor = "#00FF00";
        page.HasForeground = false;

        sut.Resolved!.Attributes(ThemeKeys.CssProperty).Should().Be(new TextAttributes
        {
            Bold = true,
            Italic = true,
            Background = ThemeColor.Parse("#80FFEEDD"),
            Effect = EffectType.Wave,
            EffectColor = ThemeColor.Parse("#00FF00"),
        });
        page.Effects.Should().NotContain(e => e.Value == EffectType.None, "the check box turns the effect off");

        page.HasEffect = false;

        sut.Resolved!.Attributes(ThemeKeys.CssProperty).Effect.Should().Be(EffectType.None);
        sut.Resolved!.Attributes(ThemeKeys.CssProperty).EffectColor.Should().BeNull();
    }

    [Fact]
    public void An_inheriting_key_shows_the_values_of_its_base_and_cannot_be_edited_until_inheriting_is_turned_off()
    {
        ColorSchemeViewModel sut = New(ThemeStore.DarkId);
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.JsonString);
        TextAttributes jsString = sut.Resolved!.Attributes(ThemeKeys.JsString);

        page.CanInherit.Should().BeTrue();
        page.BaseName.Should().NotBeEmpty();
        page.Inherit.Should().BeTrue("Signet Dark says json.string inherits");
        page.IsEditable.Should().BeFalse();
        page.Foreground.Should().Be(jsString.Foreground.ToString());
        page.Bold = !jsString.Bold;
        sut.HasChanges.Should().BeFalse("an inheriting key is not edited");

        page.Inherit = false;

        page.IsEditable.Should().BeTrue();
        sut.Current!.Entries[ThemeKeys.JsonString.Id].Should().Be(ThemeEntry.Of(jsString), "it keeps the values it showed");
        page.Foreground = "#ABCDEF";

        page.Inherit = true;

        sut.Current!.Entries[ThemeKeys.JsonString.Id].Should().Be(ThemeEntry.Inherited);
        page.Foreground.Should().Be(jsString.Foreground.ToString());
    }

    [Fact]
    public void A_key_no_theme_sets_keeps_its_default_and_does_not_inherit()
    {
        ColorSchemeViewModel sut = New();
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.XhtmlCss);

        page.CanInherit.Should().BeTrue();
        page.Inherit.Should().BeFalse("Signet Light sets nothing, so the key keeps its default");
        page.Foreground.Should().Be(ThemeKeys.XhtmlCss.DefaultAttributes!.Foreground.ToString());
        Select(sut, ThemeKeys.XhtmlTag).CanInherit.Should().BeFalse();
    }

    [Fact]
    public void The_link_to_the_base_selects_it_on_its_page()
    {
        ColorSchemeViewModel sut = New();
        List<ThemeKey> requested = new();
        sut.KeyRequested += (_, key) => requested.Add(key);
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.XhtmlCss);

        page.ShowBaseCommand.Execute(null);

        requested.Should().Equal(ThemeKeys.CssValue);
        sut.PageFor(ThemePage.Css).Key.Should().Be(ThemeKeys.CssValue);
    }

    [Fact]
    public void Restore_in_a_bundled_theme_brings_back_its_value()
    {
        ColorSchemeViewModel sut = New(ThemeStore.DarkId);
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.XhtmlTag);
        string bundled = page.Foreground;
        page.Foreground = "#123456";

        page.RestoreText.Should().Be(Signet.App.Resources.Strings.Get("ColorScheme_RestoreDefault"));
        page.RestoreCommand.Execute(null);

        page.Foreground.Should().Be(bundled);
        page.IsModified.Should().BeFalse();
        page.Selected!.IsModified.Should().BeFalse();
    }

    [Fact]
    public void Restore_in_a_theme_of_the_user_takes_the_value_of_the_parent()
    {
        ColorSchemeViewModel sut = NewWithUserTheme();
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.XhtmlTag);
        string parent = page.Foreground;
        page.Foreground = "#123456";

        page.RestoreText.Should().Contain("Signet Dark");
        page.RestoreCommand.Execute(null);

        page.Foreground.Should().Be(parent);
        sut.Current!.Entries.Should().NotContainKey(ThemeKeys.XhtmlTag.Id);
    }

    [Fact]
    public void A_color_key_that_may_be_empty_has_an_own_color_switch()
    {
        ColorSchemeViewModel sut = New();
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.UiAccent);

        page.IsColorKey.Should().BeTrue();
        page.CanBeEmpty.Should().BeTrue();
        page.HasColor.Should().BeFalse("Signet Light uses the system accent");

        page.HasColor = true;
        page.Color = "#3366CC";

        sut.Resolved!.Resolver.Color(sut.Current!, ThemeKeys.UiAccent).Should().Be(ThemeColor.Parse("#3366CC"));

        page.HasColor = false;

        sut.Resolved!.Resolver.Color(sut.Current!, ThemeKeys.UiAccent).Should().BeNull();
        Select(sut, ThemeKeys.EditorCurrentLine).CanBeEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task The_color_picker_starts_from_the_color_and_its_choice_is_kept()
    {
        ColorSchemeViewModel sut = New();
        List<string> initial = new();
        sut.ColorPicker = (_, start) =>
        {
            initial.Add(start);
            return Task.FromResult<string?>("#40102030");
        };
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.DiffDeletedLine);
        string before = page.Background;

        await page.PickBackgroundCommand.ExecuteAsync(null);

        initial.Should().Equal(before);
        page.Background.Should().Be("#40102030");
    }

    [Fact]
    public void Another_theme_shows_its_own_values_and_markers()
    {
        ColorSchemeViewModel sut = New();
        ColorSchemePageViewModel page = Select(sut, ThemeKeys.EditorText);
        page.Background = "#FAFAFA";

        sut.Selected = sut.Themes.Single(t => t.Id == ThemeStore.DarkId);

        page.Background.Should().Be(sut.Resolved!.Attributes(ThemeKeys.EditorText).Background.ToString());
        page.IsModified.Should().BeFalse();
        page.Selected!.IsModified.Should().BeFalse();
    }
}
