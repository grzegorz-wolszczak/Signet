using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="CodeEditorSyntaxMap"/> — choosing the syntax for a resource / extension.</summary>
public sealed class CodeEditorSyntaxTests
{
    [Theory]
    [InlineData(".xhtml", CodeEditorSyntax.Html)]
    [InlineData(".html", CodeEditorSyntax.Html)]
    [InlineData(".css", CodeEditorSyntax.Css)]
    [InlineData(".js", CodeEditorSyntax.JavaScript)]
    [InlineData(".json", CodeEditorSyntax.Json)]
    [InlineData(".opf", CodeEditorSyntax.Xml)]
    [InlineData(".ncx", CodeEditorSyntax.Xml)]
    [InlineData(".svg", CodeEditorSyntax.Xml)]
    [InlineData(".txt", CodeEditorSyntax.PlainText)]
    [InlineData(".weird", CodeEditorSyntax.PlainText)]
    public void ForExtension_maps_common_ebook_file_types(string ext, CodeEditorSyntax expected)
    {
        // Act & Assert
        CodeEditorSyntaxMap.ForExtension(ext).Should().Be(expected);
    }

    [Fact]
    public void ForExtension_accepts_full_paths()
    {
        // Act & Assert
        CodeEditorSyntaxMap.ForExtension("OEBPS/Text/chapter1.xhtml").Should().Be(CodeEditorSyntax.Html);

        // Act & Assert
        CodeEditorSyntaxMap.ForExtension("OEBPS/scripts/app.js").Should().Be(CodeEditorSyntax.JavaScript);
    }

    [Fact]
    public void ForResource_maps_html_css_and_misc_by_type_then_extension()
    {
        // Arrange
        using TempDir temp = new();
        var html = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        var css = new CssResource(temp.Path, temp.Combine("style.css"));
        var js = new MiscTextResource(temp.Path, temp.Combine("app.js"));
        var json = new MiscTextResource(temp.Path, temp.Combine("data.json"));

        // Act & Assert
        CodeEditorSyntaxMap.ForResource(html).Should().Be(CodeEditorSyntax.Html);

        // Act & Assert
        CodeEditorSyntaxMap.ForResource(css).Should().Be(CodeEditorSyntax.Css);

        // Act & Assert
        CodeEditorSyntaxMap.ForResource(js).Should().Be(CodeEditorSyntax.JavaScript);

        // Act & Assert
        CodeEditorSyntaxMap.ForResource(json).Should().Be(CodeEditorSyntax.Json);
    }

    [Theory]
    [InlineData(CodeEditorSyntax.Html, "html")]
    [InlineData(CodeEditorSyntax.Xml, "xml")]
    [InlineData(CodeEditorSyntax.Css, "css")]
    [InlineData(CodeEditorSyntax.JavaScript, "javascript")]
    [InlineData(CodeEditorSyntax.Json, "json")]
    public void ToTextMateLanguageId_returns_grammar_id(CodeEditorSyntax syntax, string expected)
    {
        // Act & Assert
        CodeEditorSyntaxMap.ToTextMateLanguageId(syntax).Should().Be(expected);
    }

    [Fact]
    public void ToTextMateLanguageId_is_null_for_plain_text()
    {
        // Act & Assert
        CodeEditorSyntaxMap.ToTextMateLanguageId(CodeEditorSyntax.PlainText).Should().BeNull();
    }

    [Theory]
    [InlineData(CodeEditorSyntax.Html, true)]
    [InlineData(CodeEditorSyntax.Xml, true)]
    [InlineData(CodeEditorSyntax.Css, true)]
    [InlineData(CodeEditorSyntax.JavaScript, false)]
    [InlineData(CodeEditorSyntax.Json, false)]
    [InlineData(CodeEditorSyntax.PlainText, false)]
    public void SupportsWellFormedCheck_is_true_for_markup_and_css(CodeEditorSyntax syntax, bool expected)
    {
        // Act & Assert
        CodeEditorSyntaxMap.SupportsWellFormedCheck(syntax).Should().Be(expected);
    }
}
