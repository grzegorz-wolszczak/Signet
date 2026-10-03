using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="W3CValidation.BuildCssValidationHtml"/> (Validate Stylesheets
/// With W3C: a page with an auto-submitted form, opened in the user's browser).
/// </summary>
public sealed class W3CValidationTests
{
    [Fact]
    public void BuildCssValidationHtml_embeds_the_stylesheet_text_and_profile()
    {
        string html = W3CValidation.BuildCssValidationHtml("body { color: red; }", "css30");

        html.Should().Contain("body { color: red; }");
        html.Should().Contain("value='css30'");
        html.Should().Contain(W3CValidation.ValidatorUrl);
        html.Should().Contain("<form");
    }

    [Fact]
    public void BuildCssValidationHtml_html_encodes_the_stylesheet_text()
    {
        string html = W3CValidation.BuildCssValidationHtml("a[href^=\"x\"] { color: red; } /* <tag> & */", "css21");

        html.Should().Contain("&lt;tag&gt;").And.Contain("&amp;");
        html.Should().NotContain("<tag>");
    }
}
