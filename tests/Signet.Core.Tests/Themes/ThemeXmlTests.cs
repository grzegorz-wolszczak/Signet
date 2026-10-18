using System;
using System.Collections.Generic;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Localization;
using Signet.Core.Themes;
using Xunit;

namespace Signet.Core.Tests.Themes;

public sealed class ThemeXmlTests
{
    private static readonly Fixture Any = new();

    [Fact]
    public void A_theme_survives_writing_and_reading()
    {
        Theme theme = new Theme("01JA2B3C4D5E6F7G8H9J0K1M2N", Any.Create<string>())
        {
            ParentId = "signet-light",
            Variant = InterfaceVariant.Dark,
            Created = new DateTimeOffset(2026, 10, 12, 14, 32, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 10, 12, 15, 10, 5, TimeSpan.Zero),
        }
            .With(ThemeKeys.UiAccent.Id, ThemeEntry.Of((ThemeColor?)null))
            .With(ThemeKeys.UiFrame.Id, ThemeEntry.Of(ThemeColor.Parse("#F5ECD7")))
            .With(ThemeKeys.UiDangerButtonHover.Id, ThemeEntry.Inherited)
            .With(ThemeKeys.UiTextSelection.Id, ThemeEntry.Of(new TextAttributes { Background = ThemeColor.Parse("#20FFFFFF") }));

        Theme read = ThemeXml.FromXml(ThemeXml.ToXml(theme, ThemeDocumentKind.Theme, "0.8.0-202610121510"));

        (read.Id, read.Name, read.ParentId, read.Variant, read.Created, read.Modified)
            .Should().Be((theme.Id, theme.Name, theme.ParentId, theme.Variant, theme.Created, theme.Modified));
        read.Entries.Should().BeEquivalentTo(theme.Entries);
    }

    [Fact]
    public void A_color_scheme_survives_writing_and_reading_and_has_no_interface_settings()
    {
        Theme scheme = new Theme("01JA2B3C4D5E6F7G8H9J0K1M2N", Any.Create<string>())
        {
            ParentId = "signet-light",
            Variant = InterfaceVariant.Dark,
        }
            .With(ThemeKeys.EditorCurrentLine.Id, ThemeEntry.Of(ThemeColor.Parse("#F5ECD7")))
            .With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(new TextAttributes
            {
                Foreground = ThemeColor.Parse("#8B4513"),
                Background = ThemeColor.Parse("#20FFFFFF"),
                Bold = true,
                Italic = true,
                Effect = EffectType.DottedUnderline,
                EffectColor = ThemeColor.Parse("#C0392B"),
            }))
            .With(ThemeKeys.XhtmlCssComment.Id, ThemeEntry.Inherited);

        string xml = ThemeXml.ToXml(scheme, ThemeDocumentKind.Scheme);
        Theme read = ThemeXml.FromXml(xml, ThemeDocumentKind.Scheme);

        xml.Should().Contain("<signet-scheme ").And.NotContain("signet-theme");
        read.Entries.Should().BeEquivalentTo(scheme.Entries);
        read.Variant.Should().BeNull("the variant belongs to the theme");
    }

    [Fact]
    public void The_file_holds_only_what_the_theme_sets_in_the_order_of_the_registry()
    {
        Theme theme = new Theme("my-theme", "Sepia") { ParentId = "signet-light" }
            .With(ThemeKeys.CssComment.Id, ThemeEntry.Of(new TextAttributes { Foreground = ThemeColor.Parse("#00aa00") }))
            .With(ThemeKeys.XhtmlTag.Id, ThemeEntry.Of(new TextAttributes { Bold = true }));

        string xml = ThemeXml.ToXml(theme, ThemeDocumentKind.Scheme);

        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<signet-scheme version=\"1\" id=\"my-theme\" name=\"Sepia\" parent=\"signet-light\">");
        xml.Should().Contain("""
              <attributes>
                <attribute key="xhtml.tag" bold="true" />
                <attribute key="css.comment" foreground="#00AA00" />
              </attributes>
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        xml.Should().NotContain("<colors>").And.NotContain("<meta");
    }

    [Fact]
    public void Unknown_keys_and_keys_in_the_wrong_section_are_skipped_with_a_warning()
    {
        string xml = """
            <signet-scheme version="1" id="t" name="T">
              <colors>
                <color key="xhtml.tag" value="#000000" />
              </colors>
              <attributes>
                <attribute key="future.key" foreground="#000000" />
                <attribute key="css.comment" foreground="#123456" />
              </attributes>
            </signet-scheme>
            """;
        List<string> warnings = new();

        Theme theme = ThemeXml.FromXml(xml, ThemeDocumentKind.Scheme, warnings);

        theme.Entries.Keys.Should().Equal("css.comment");
        warnings.Should().Equal(
            CoreStrings.Format("Theme_WrongSection", 3, "xhtml.tag"),
            CoreStrings.Format("Theme_UnknownKey", 6, "future.key"));
    }

    [Fact]
    public void A_key_of_the_other_document_is_skipped_with_a_warning()
    {
        string theme = """
            <signet-theme version="1" id="t" name="T">
              <colors>
                <color key="editor.currentLine" value="#000000" />
                <color key="ui.frame" value="#101010" />
              </colors>
            </signet-theme>
            """;
        string scheme = """
            <signet-scheme version="1" id="t" name="T">
              <colors>
                <color key="editor.currentLine" value="#000000" />
                <color key="ui.frame" value="#101010" />
              </colors>
            </signet-scheme>
            """;
        List<string> themeWarnings = new();
        List<string> schemeWarnings = new();

        ThemeXml.FromXml(theme, ThemeDocumentKind.Theme, themeWarnings).Entries.Keys.Should().Equal("ui.frame");
        ThemeXml.FromXml(scheme, ThemeDocumentKind.Scheme, schemeWarnings).Entries.Keys.Should().Equal("editor.currentLine");

        themeWarnings.Should().Equal(CoreStrings.Format("Theme_WrongDocument", 3, "editor.currentLine"));
        schemeWarnings.Should().Equal(CoreStrings.Format("Theme_WrongDocument", 4, "ui.frame"));
    }

    [Fact]
    public void A_file_of_the_other_kind_is_not_read()
    {
        FluentActions.Invoking(() => ThemeXml.FromXml("<signet-theme version=\"1\" id=\"t\" name=\"T\" />", ThemeDocumentKind.Scheme))
            .Should().Throw<ThemeFormatException>().WithMessage(CoreStrings.Get("Theme_NotAScheme"));
        FluentActions.Invoking(() => ThemeXml.FromXml("<signet-scheme version=\"1\" id=\"t\" name=\"T\" />", ThemeDocumentKind.Theme))
            .Should().Throw<ThemeFormatException>().WithMessage(CoreStrings.Get("Theme_NotATheme"));
    }

    [Theory]
    [InlineData("<signet-theme version=\"1\" id=\"t\" name=\"T\">\n<colors>\n<color key=\"ui.frame\" value=\"#12345\" />\n</colors>\n</signet-theme>", 3)]
    [InlineData("<signet-theme version=\"1\" id=\"t\" name=\"T\">\n<attributes>\n<attribute key=\"ui.textSelection\" bold=\"yes\" />\n</attributes>\n</signet-theme>", 3)]
    [InlineData("<signet-theme version=\"1\" id=\"t\" name=\"T\">\n<attributes>\n<attribute key=\"ui.textSelection\" effect=\"zigzag\" />\n</attributes>\n</signet-theme>", 3)]
    [InlineData("<signet-theme version=\"1\" id=\"t\" name=\"T\" variant=\"Sepia\" />", 1)]
    [InlineData("<signet-theme version=\"1\" name=\"T\" />", 1)]
    [InlineData("<signet-theme id=\"t\" name=\"T\" />", 1)]
    [InlineData("<signet-theme version=\"2\" id=\"t\" name=\"T\" />", 1)]
    [InlineData("<scheme name=\"Darcula\" version=\"142\" />", 1)]
    [InlineData("<signet-theme version=\"1\" id=\"t\" name=\"T\">\n<colors>", 2)]
    public void A_file_this_version_cannot_read_is_refused_with_its_line(string xml, int line)
    {
        FluentActions.Invoking(() => ThemeXml.FromXml(xml)).Should().Throw<ThemeFormatException>()
            .Which.Line.Should().Be(line);
    }

    [Fact]
    public void A_newer_format_is_named_in_the_message()
    {
        FluentActions.Invoking(() => ThemeXml.FromXml("<signet-theme version=\"7\" id=\"t\" name=\"T\" />"))
            .Should().Throw<ThemeFormatException>().WithMessage(CoreStrings.Format("Theme_UnsupportedVersion", 7));
    }
}
