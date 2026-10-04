using System.Linq;
using System.Net;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="XhtmlEntities"/> and of the DOCTYPE-dependent handling of named entities in
/// <see cref="WellFormedChecker"/>: HTML entities are valid only under an XHTML 1.x DOCTYPE.
/// </summary>
public sealed class XhtmlEntitiesTests
{
    private static readonly string[] XmlPredefined = { "amp", "lt", "gt", "quot", "apos" };

    private const string Xhtml11Doctype =
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n  \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n";

    private const string Xhtml10StrictDoctype =
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">\n";

    private static string Document(string doctype, string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + doctype +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>" + body + "</p></body></html>";

    [Fact]
    public void Dtd_names_are_the_html4_set_and_all_decode_to_one_character()
    {
        XhtmlEntities.DtdNames.Should().HaveCount(248);
        XhtmlEntities.DtdNames.Should().NotContain(XmlPredefined);
        XhtmlEntities.DtdNames.Select(n => WebUtility.HtmlDecode("&" + n + ";"))
            .Should().OnlyContain(s => s.Length == 1 && s[0] != '&');
    }

    [Theory]
    [InlineData(Xhtml11Doctype, true)]
    [InlineData(Xhtml10StrictDoctype, true)]
    [InlineData("<!DOCTYPE html>\n", false)]
    [InlineData("", false)]
    public void HasXhtml1Doctype_recognizes_xhtml_1_x(string doctype, bool expected)
    {
        XhtmlEntities.HasXhtml1Doctype(Document(doctype, "x")).Should().Be(expected);
    }

    [Theory]
    [InlineData("&amp;", false, true)]
    [InlineData("&apos;", false, true)]
    [InlineData("&#8212;", false, true)]
    [InlineData("&#x2014;", false, true)]
    [InlineData("&mdash;", false, false)]
    [InlineData("&mdash;", true, true)]
    [InlineData("&NewLine;", true, false)]
    public void IsDefined_depends_on_the_doctype_only_for_html_entities(string entity, bool xhtml1, bool expected)
    {
        XhtmlEntities.IsDefined(entity, xhtml1).Should().Be(expected);
    }

    [Theory]
    [InlineData(Xhtml11Doctype)]
    [InlineData(Xhtml10StrictDoctype)]
    public void Html_entities_are_well_formed_under_an_xhtml_1_x_doctype(string doctype)
    {
        WellFormedChecker.Check(Document(doctype, "a&mdash;b&nbsp;c&euro;")).IsWellFormed.Should().BeTrue();
        WellFormedChecker.CheckXhtmlStructure(Document(doctype, "&shy;"), "2.0").IsWellFormed.Should().BeTrue();
    }

    [Theory]
    [InlineData("<!DOCTYPE html>\n")]
    [InlineData("")]
    public void Html_entities_are_errors_without_an_xhtml_1_x_doctype(string doctype)
    {
        WellFormedChecker.Check(Document(doctype, "a&mdash;b")).IsWellFormed.Should().BeFalse();
    }

    [Fact]
    public void Unknown_entities_stay_errors_even_under_an_xhtml_1_x_doctype()
    {
        WellFormedResult result = WellFormedChecker.Check(Document(Xhtml11Doctype, "&NewLine;"));

        result.IsWellFormed.Should().BeFalse();
        result.Line.Should().Be(4);
    }
}
