using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.CodeEditor;
using Signet.Core.MainUI;
using Signet.Core.Themes;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The demo texts of the theme editor (<see cref="ThemeSample"/>, <see cref="ThemeSamples"/>).</summary>
public sealed class ThemeSampleTests
{
    [Fact]
    public void The_markup_marks_nested_fragments_with_their_keys()
    {
        ThemeSample sut = ThemeSample.Parse("a «diff.deletedLine|b «diff.deletedWord|c» d» e", CodeEditorSyntax.PlainText);

        sut.Text.Should().Be("a b c d e");
        sut.Marks.Should().BeEquivalentTo(new[]
        {
            new KeyRange(ThemeKeys.DiffDeletedWord, 4, 1),
            new KeyRange(ThemeKeys.DiffDeletedLine, 2, 5),
        });
        sut.KeyAt(4).Should().Be(ThemeKeys.DiffDeletedWord, "the innermost mark wins");
        sut.KeyAt(2).Should().Be(ThemeKeys.DiffDeletedLine);
        sut.KeyAt(0).Should().BeNull();
    }

    [Theory]
    [InlineData("«xhtml.tag|unclosed")]
    [InlineData("closed»")]
    [InlineData("«no.such.key|text»")]
    public void A_broken_markup_is_rejected(string markup)
    {
        Action parse = () => ThemeSample.Parse(markup, CodeEditorSyntax.PlainText);

        parse.Should().Throw<Exception>();
    }

    [Fact]
    public void The_keys_of_the_syntax_come_from_the_highlighting_of_code_editor()
    {
        ThemeSample sut = ThemeSample.Parse("<p class=\"a\">x &amp; <a href=\"missing.xhtml\">y</a></p>", CodeEditorSyntax.Html);

        sut.KeyAt(1).Should().Be(ThemeKeys.XhtmlTag);
        sut.KeyAt(4).Should().Be(ThemeKeys.XhtmlAttributeName);
        sut.KeyAt(sut.Text.IndexOf("&amp;", StringComparison.Ordinal) + 1).Should().Be(ThemeKeys.XhtmlEntity);
        sut.KeyAt(sut.Text.IndexOf("missing", StringComparison.Ordinal)).Should().Be(ThemeKeys.LinkBroken);
        sut.KeyAt(sut.Text.IndexOf("x ", StringComparison.Ordinal)).Should().BeNull("plain text has no key of the syntax");
    }

    [Fact]
    public void A_style_block_takes_the_css_keys_in_the_extended_highlighting_and_the_embedded_css_key_in_the_basic_one()
    {
        ThemeSample sut = ThemeSample.Parse("<style>\np { color: red; }\n</style>", CodeEditorSyntax.Html);
        int property = sut.Text.IndexOf("color", StringComparison.Ordinal);

        sut.IsExtended.Should().BeTrue();
        sut.KeyAt(property).Should().Be(ThemeKeys.CssProperty);
        sut.Basic.IsExtended.Should().BeFalse();
        sut.Basic.KeyAt(property).Should().Be(ThemeKeys.XhtmlCss);
    }

    [Fact]
    public void The_keys_of_javascript_and_json_come_from_the_textmate_scopes()
    {
        ThemeSample js = ThemeSample.Parse("var n = 42; // done\nif (n) { return \"s\"; }", CodeEditorSyntax.JavaScript);
        ThemeSample json = ThemeSample.Parse("{ \"a\": 1, \"b\": true }", CodeEditorSyntax.Json);

        js.KeyAt(0).Should().Be(ThemeKeys.JsKeyword);
        js.KeyAt(js.Text.IndexOf("42", StringComparison.Ordinal)).Should().Be(ThemeKeys.JsNumber);
        js.KeyAt(js.Text.IndexOf("done", StringComparison.Ordinal)).Should().Be(ThemeKeys.JsComment);
        js.KeyAt(js.Text.IndexOf("if", StringComparison.Ordinal)).Should().Be(ThemeKeys.JsControlKeyword);
        js.KeyAt(js.Text.IndexOf("\"s\"", StringComparison.Ordinal) + 1).Should().Be(ThemeKeys.JsString);
        json.KeyAt(json.Text.IndexOf('a', StringComparison.Ordinal)).Should().Be(ThemeKeys.JsonProperty);
        json.KeyAt(json.Text.IndexOf('1', StringComparison.Ordinal)).Should().Be(ThemeKeys.JsonNumber);
        json.KeyAt(json.Text.IndexOf("true", StringComparison.Ordinal)).Should().Be(ThemeKeys.JsonConstant);
    }

    [Fact]
    public void The_sample_of_every_editor_page_shows_each_key_of_its_page()
    {
        (ThemePage Page, ThemeSample[] Samples)[] pages =
        {
            (ThemePage.General, new[] { ThemeSamples.General }),
            (ThemePage.Xhtml, new[] { ThemeSamples.Xhtml }),
            (ThemePage.Css, new[] { ThemeSamples.Css }),
            (ThemePage.JavaScriptJson, new[] { ThemeSamples.JavaScript, ThemeSamples.Json }),
            (ThemePage.Diff, new[] { ThemeSamples.Diff }),
        };

        foreach ((ThemePage page, ThemeSample[] samples) in pages)
        {
            // The extended and the basic highlighting together (e.g. xhtml.css is the <style> block of the basic one).
            ThemeKey[] shown = samples.SelectMany(s => new[] { s, s.Basic })
                .SelectMany(s => s.Marks.Concat(s.SyntaxRanges)).Select(r => r.Key).Distinct().ToArray();
            ThemeKey[] notShown = ThemeKeys.On(page)
                .Where(k => k != ThemeKeys.EditorText && k != ThemeKeys.EditorCurrentLine && k != ThemeKeys.EditorLineNumbers
                            && k != ThemeKeys.EditorCurrentLineNumber)
                .Except(shown).ToArray();
            notShown.Select(k => k.Id).Should().BeEmpty($"the sample of {page} shows every key of the page");
        }
    }
}
