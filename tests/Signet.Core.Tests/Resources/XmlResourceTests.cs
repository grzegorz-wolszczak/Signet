using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Resources;

/// <summary>Tests for <see cref="XmlResource"/> — well-formedness, GetValidId, encoding detection.</summary>
public sealed class XmlResourceTests
{
    [Theory]
    [InlineData("<root><a/></root>", true)]
    [InlineData("<root><a></root>", false)]
    [InlineData("   ", false)]
    public void IsWellFormed_reflects_xml_validity(string content, bool expected)
    {
        using TempDir root = new();
        XmlResource resource = new(root.Path, root.Combine("x.xml"));
        resource.SetText(content);

        resource.IsWellFormed().Should().Be(expected);
    }

    [Theory]
    [InlineData("chapter 1", "chapter_1")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("1abc", "x1abc")]
    [InlineData(".dot-start", "x.dot-start")]
    [InlineData("już-tak", "juz-tak")]
    [InlineData("Mój Rozdział.xhtml", "Moj_Rozdzial.xhtml")]
    [InlineData("résumé.xhtml", "resume.xhtml")]
    [InlineData("第一章.xhtml", "diyizhang.xhtml")]
    public void GetValidId_sanitizes_and_transliterates_the_requested_value(string input, string expected)
    {
        XmlResource.GetValidId(input).Should().Be(expected);
    }

    [Fact]
    public void GetValidId_generates_an_id_when_nothing_valid_remains()
    {
        string id = XmlResource.GetValidId("€");

        id.Should().MatchRegex("^[A-Za-z][A-Za-z0-9]*$");
        id.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void Reads_a_windows_1250_encoded_file_via_the_charset_declaration()
    {
        using TempDir root = new();
        string full = root.Combine("ch.xhtml");
        HtmlEncodingResolver.TryGetEncoding("windows-1250", out Encoding cp1250).Should().BeTrue();
        string xhtml =
            "<?xml version=\"1.0\"?><html><head><meta charset=\"windows-1250\"/></head>" +
            "<body><p>Zażółć gęślą jaźń</p></body></html>";
        File.WriteAllBytes(full, cp1250.GetBytes(xhtml));

        XmlResource resource = new(root.Path, full);
        resource.InitialLoad();

        resource.GetText().Should().Contain("Zażółć gęślą jaźń");
    }
}
