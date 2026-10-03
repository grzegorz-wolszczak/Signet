using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="CodeViewSyntaxMap"/> — choosing the syntax for a resource / extension.</summary>
public sealed class CodeViewSyntaxTests
{
    [Theory]
    [InlineData(".xhtml", CodeViewSyntax.Html)]
    [InlineData(".html", CodeViewSyntax.Html)]
    [InlineData(".css", CodeViewSyntax.Css)]
    [InlineData(".js", CodeViewSyntax.JavaScript)]
    [InlineData(".json", CodeViewSyntax.Json)]
    [InlineData(".opf", CodeViewSyntax.Xml)]
    [InlineData(".ncx", CodeViewSyntax.Xml)]
    [InlineData(".svg", CodeViewSyntax.Xml)]
    [InlineData(".txt", CodeViewSyntax.PlainText)]
    [InlineData(".weird", CodeViewSyntax.PlainText)]
    public void ForExtension_maps_common_ebook_file_types(string ext, CodeViewSyntax expected) =>
        CodeViewSyntaxMap.ForExtension(ext).Should().Be(expected);

    [Fact]
    public void ForExtension_accepts_full_paths()
    {
        CodeViewSyntaxMap.ForExtension("OEBPS/Text/chapter1.xhtml").Should().Be(CodeViewSyntax.Html);
        CodeViewSyntaxMap.ForExtension("OEBPS/scripts/app.js").Should().Be(CodeViewSyntax.JavaScript);
    }

    [Fact]
    public void ForResource_maps_html_css_and_misc_by_type_then_extension()
    {
        using TempDir temp = new();
        var html = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        var css = new CssResource(temp.Path, temp.Combine("style.css"));
        var js = new MiscTextResource(temp.Path, temp.Combine("app.js"));
        var json = new MiscTextResource(temp.Path, temp.Combine("data.json"));

        CodeViewSyntaxMap.ForResource(html).Should().Be(CodeViewSyntax.Html);
        CodeViewSyntaxMap.ForResource(css).Should().Be(CodeViewSyntax.Css);
        CodeViewSyntaxMap.ForResource(js).Should().Be(CodeViewSyntax.JavaScript);
        CodeViewSyntaxMap.ForResource(json).Should().Be(CodeViewSyntax.Json);
    }

    [Theory]
    [InlineData(CodeViewSyntax.Html, "html")]
    [InlineData(CodeViewSyntax.Xml, "xml")]
    [InlineData(CodeViewSyntax.Css, "css")]
    [InlineData(CodeViewSyntax.JavaScript, "javascript")]
    [InlineData(CodeViewSyntax.Json, "json")]
    public void ToTextMateLanguageId_returns_grammar_id(CodeViewSyntax syntax, string expected) =>
        CodeViewSyntaxMap.ToTextMateLanguageId(syntax).Should().Be(expected);

    [Fact]
    public void ToTextMateLanguageId_is_null_for_plain_text() =>
        CodeViewSyntaxMap.ToTextMateLanguageId(CodeViewSyntax.PlainText).Should().BeNull();

    [Theory]
    [InlineData(CodeViewSyntax.Html, true)]
    [InlineData(CodeViewSyntax.Xml, true)]
    [InlineData(CodeViewSyntax.Css, true)]
    [InlineData(CodeViewSyntax.JavaScript, false)]
    [InlineData(CodeViewSyntax.Json, false)]
    [InlineData(CodeViewSyntax.PlainText, false)]
    public void SupportsWellFormedCheck_is_true_for_markup_and_css(CodeViewSyntax syntax, bool expected) =>
        CodeViewSyntaxMap.SupportsWellFormedCheck(syntax).Should().Be(expected);
}
