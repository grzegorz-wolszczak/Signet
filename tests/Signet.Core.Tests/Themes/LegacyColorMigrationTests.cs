using System.IO;
using System.Linq;
using System.Xml.Linq;
using AwesomeAssertions;
using Signet.Core.Localization;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

public sealed class LegacyColorMigrationTests
{
    [Fact]
    public void Colors_never_changed_make_no_theme_and_their_keys_go()
    {
        using TempDir dir = new();
        string path = Settings(dir, LegacyColorMigration.Defaults.Select(d => (d.Key, d.Default.ToString())).Distinct().ToArray());
        SettingsStore settings = new(path);
        ThemeStore store = new(dir.Combine("themes"));

        Theme? migrated = LegacyColorMigration.Run(settings, store);

        migrated.Should().BeNull("every old default equals the default of its key");
        settings.UiThemeId.Should().Be(ThemeStore.LightId);
        store.Themes.Should().HaveCount(2);
        File.ReadAllText(path).Should().NotContain("_color");
    }

    [Fact]
    public void Changed_colors_move_into_my_colors_which_becomes_the_theme_in_use()
    {
        using TempDir dir = new();
        string path = Settings(dir,
            ("code_view_css_comment_color", "#112233"),
            ("code_view_xhtml_entity_color", "#445566"),
            ("code_view_error_underline_color", "#778899"),
            ("warning_color_light", "#aa5500"),
            ("open_tag_hint_light_background", "#101010"),
            ("open_tag_hint_light_foreground", "#efefef"),
            ("preview_highlight_color_light", "not a color"),
            ("code_view_css_value_color", "#000000"));
        SettingsStore settings = new(path);
        ThemeStore store = new(dir.Combine("themes"));

        Theme migrated = LegacyColorMigration.Run(settings, store)!;

        migrated.Name.Should().Be(CoreStrings.Get("Theme_MigratedName"));
        migrated.ParentId.Should().Be(ThemeStore.LightId);
        settings.UiThemeId.Should().Be(migrated.Id);
        new SettingsStore(path).UiThemeId.Should().Be(migrated.Id, "the settings are saved");
        File.ReadAllText(path).Should().NotContain("_color");

        ThemeResolver r = store.Resolver;
        Theme theme = store.Get(migrated.Id);
        r.Attributes(theme, ThemeKeys.CssComment).Foreground.Should().Be(ThemeColor.Parse("#112233"));
        r.Attributes(theme, ThemeKeys.XhtmlEntity).Foreground.Should().Be(ThemeColor.Parse("#445566"));
        r.Attributes(theme, ThemeKeys.XhtmlSpecialSpace).EffectColor.Should().Be(ThemeColor.Parse("#445566"),
            "the special space was drawn in the entity color");
        r.Attributes(theme, ThemeKeys.ErrorsSyntax).Should().Be(ThemeKeys.ErrorsSyntax.DefaultAttributes! with { EffectColor = ThemeColor.Parse("#778899") });
        r.Color(theme, ThemeKeys.UiWarningText).Should().Be(ThemeColor.Parse("#AA5500"));
        r.Attributes(theme, ThemeKeys.HintsOpenTag).Should().Be(new TextAttributes
        {
            Foreground = ThemeColor.Parse("#EFEFEF"),
            Background = ThemeColor.Parse("#101010"),
        });
        theme.Entries.Keys.Should().NotContain(new[] { ThemeKeys.PreviewHighlight.Id, ThemeKeys.CssValue.Id },
            "an invalid color and a default one do not move");
    }

    [Fact]
    public void Without_old_colors_nothing_happens()
    {
        using TempDir dir = new();
        SettingsStore settings = new(Settings(dir));
        ThemeStore store = new(dir.Combine("themes"));

        LegacyColorMigration.Run(settings, store).Should().BeNull();
        Directory.Exists(dir.Combine("themes")).Should().BeFalse();
    }

    private static string Settings(TempDir dir, params (string Key, string Value)[] values)
    {
        XElement group = new("group", new XAttribute("name", "user_preferences"),
            new XElement("bool", new XAttribute("key", "spellcheck"), "true"));
        foreach ((string key, string value) in values)
        {
            group.Add(new XElement("string", new XAttribute("key", key), value));
        }

        string path = dir.Combine("settings.xml");
        new XDocument(new XElement("settings", group)).Save(path);
        return path;
    }
}
