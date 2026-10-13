using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

public sealed class ThemeFontsTests
{
    private static readonly Fixture Any = new();

    private static ThemeResolver Resolver(params Theme[] themes) => new(id => themes.FirstOrDefault(t => t.Id == id));

    private static Theme NewTheme() => new(Any.Create<string>(), Any.Create<string>());

    [Fact]
    public void The_fonts_of_the_settings_cover_every_place_with_their_values()
    {
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.json"))
        {
            UiFont = "Inter",
            UiFontSize = 13,
            CodeViewAppearance = new CodeViewAppearance { FontFamily = "Consolas", FontSize = 14 },
            PreviewAppearance = new PreviewAppearance("Georgia", "Times New Roman", "Verdana", 18),
        };
        settings.SetTooltipFont(TooltipArea.BookBrowser, new TooltipFont("Segoe UI", 11));

        var sut = ThemeFontKeys.FromSettings(settings);

        sut.Keys.Should().BeEquivalentTo(ThemeFontKeys.All);
        sut[ThemeFontKeys.Interface].Should().Be(new ThemeFont("Inter", 13));
        sut[ThemeFontKeys.CodeViewEditor].Should().Be(new ThemeFont("Consolas", 14));
        sut[ThemeFontKeys.PreviewSerif].Should().Be(new ThemeFont("Times New Roman", 0));
        sut[ThemeFontKeys.PreviewSize].Should().Be(new ThemeFont(string.Empty, 18));
        sut[ThemeFontKeys.Tooltip(TooltipArea.BookBrowser)].Should().Be(new ThemeFont("Segoe UI", 11));
        ThemeFontKeys.Tooltip(TooltipArea.BookBrowser).Should().Be("tooltip.bookBrowser");
    }

    [Fact]
    public void The_fonts_of_a_theme_count_only_while_it_turns_them_on()
    {
        Theme theme = NewTheme() with
        {
            Fonts = NewTheme().Fonts.SetItem(ThemeFontKeys.CodeViewEditor, new ThemeFont("Cascadia Code", 15)),
        };
        CodeViewAppearance setting = new() { FontFamily = "Consolas", FontSize = 14 };

        new ThemeFonts(theme, Resolver(theme)).CodeView(setting).Should().Be(setting, "the theme does not turn its fonts on");

        ThemeFonts sut = new(theme with { UseThemeFonts = true }, Resolver(theme));

        sut.CodeView(setting).Should().Be(new CodeViewAppearance { FontFamily = "Cascadia Code", FontSize = 15 });
        sut.Interface("Inter", 13).Should().Be(("Inter", 13), "a place the theme does not set keeps the setting");
    }

    [Fact]
    public void A_theme_takes_the_fonts_its_parent_sets_and_overrides_them()
    {
        Theme parent = NewTheme() with
        {
            Fonts = NewTheme().Fonts
                .SetItem(ThemeFontKeys.PreviewSerif, new ThemeFont("Garamond", 0))
                .SetItem(ThemeFontKeys.PreviewSize, new ThemeFont(string.Empty, 20)),
        };
        Theme child = NewTheme() with
        {
            ParentId = parent.Id,
            UseThemeFonts = true,
            Fonts = NewTheme().Fonts.SetItem(ThemeFontKeys.PreviewSize, new ThemeFont(string.Empty, 17)),
        };
        ThemeFonts sut = new(child, Resolver(parent, child));

        sut.Preview(new PreviewAppearance("Arial", "Times New Roman", "Arial", 16))
            .Should().Be(new PreviewAppearance("Arial", "Garamond", "Arial", 17));
    }

    [Fact]
    public void Every_place_follows_its_font_in_the_theme()
    {
        Theme theme = NewTheme() with
        {
            UseThemeFonts = true,
            Fonts = NewTheme().Fonts
                .SetItem(ThemeFontKeys.Interface, new ThemeFont("Inter", 12))
                .SetItem(ThemeFontKeys.Warnings, new ThemeFont(string.Empty, 15))
                .SetItem(ThemeFontKeys.OpenTagHint, new ThemeFont("Consolas", 11))
                .SetItem(ThemeFontKeys.SpecialCharacters, new ThemeFont("Segoe UI Symbol", 20))
                .SetItem(ThemeFontKeys.Tooltip(TooltipArea.Preview), new ThemeFont(string.Empty, 9)),
        };
        ThemeFonts sut = new(theme, Resolver(theme));

        sut.Interface(string.Empty, 0).Should().Be(("Inter", 12));
        sut.Warnings(new WarningAppearance(0)).Should().Be(new WarningAppearance(15));
        sut.OpenTagHint(new OpenTagHintAppearance(string.Empty, 0)).Should().Be(new OpenTagHintAppearance("Consolas", 11));
        sut.SpecialCharacters(new SpecialCharacterAppearance("Arial", 14)).Should().Be(new SpecialCharacterAppearance("Segoe UI Symbol", 20));
        sut.Tooltip(TooltipArea.Preview, new TooltipFont("Arial", 12)).Should().Be(new TooltipFont(string.Empty, 9));
        sut.Tooltip(TooltipArea.Footnotes, new TooltipFont("Arial", 12)).Should().Be(new TooltipFont("Arial", 12));
    }
}
