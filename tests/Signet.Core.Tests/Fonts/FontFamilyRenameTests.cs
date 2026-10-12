using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="FontFamilyRename"/> — renaming a family in the whole book.</summary>
public sealed class FontFamilyRenameTests
{
    private const string CssPath = "OEBPS/Styles/style.css";
    private const string HtmlPath = "OEBPS/Text/ch1.xhtml";

    [Fact]
    public void Rename_changes_the_rules_and_every_list_that_names_the_family()
    {
        const string css = "@font-face { font-family: 'KCaecilia Condensed'; src: url(a.ttf) }\n"
            + "body { font-family: \"kcaecilia  condensed\", serif }\nh1 { font: bold 1em KCaecilia Condensed, serif }\np { font-family: Other }";
        const string html = "<p style=\"font-family: &quot;KCaecilia Condensed&quot;, serif\">a</p>";

        IReadOnlyDictionary<string, string> texts = FontFamilyRename.Rename(
            BookFontIndex.Build(new[] { new FontIndexSource(CssPath, css, true), new FontIndexSource(HtmlPath, html, false) }),
            "KCaecilia Condensed",
            "KCaecilia");

        texts[CssPath].Should().Be(
            "@font-face { font-family: 'KCaecilia'; src: url(a.ttf) }\n"
            + "body { font-family: \"KCaecilia\", serif }\nh1 { font: bold 1em \"KCaecilia\", serif }\np { font-family: Other }");
        texts[HtmlPath].Should().Be("<p style=\"font-family: 'KCaecilia', serif\">a</p>");
    }

    [Fact]
    public void Rename_to_the_same_name_changes_nothing()
    {
        BookFontIndex index = BookFontIndex.Build(new[] { new FontIndexSource(CssPath, "body { font-family: A }", true) });

        FontFamilyRename.Rename(index, "A", " A ").Should().BeEmpty();
    }
}
