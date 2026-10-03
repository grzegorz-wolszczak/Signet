using System.IO;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>Tests of <see cref="HtmlEncodingResolver"/> — detecting the encoding of (X)HTML/XML.</summary>
public sealed class HtmlEncodingResolverTests
{
    [Fact]
    public void Detects_utf8_bom()
    {
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        byte[] body = Encoding.UTF8.GetBytes("<html>café</html>");

        HtmlEncodingResolver.GetEncodingForHtml(Concat(bom, body)).WebName.Should().Be("utf-8");
    }

    [Fact]
    public void Detects_utf16le_bom()
    {
        byte[] data = Encoding.Unicode.GetPreamble();

        HtmlEncodingResolver.GetEncodingForHtml(Concat(data, new byte[] { (byte)'<', 0, (byte)'a', 0 }))
            .WebName.Should().Be("utf-16");
    }

    [Fact]
    public void Uses_the_xml_encoding_declaration()
    {
        HtmlEncodingResolver.TryGetEncoding("windows-1250", out Encoding cp1250).Should().BeTrue();
        byte[] data = cp1250.GetBytes("<?xml version=\"1.0\" encoding=\"windows-1250\"?><p>ą</p>");

        Encoding detected = HtmlEncodingResolver.GetEncodingForHtml(data);

        detected.WebName.Should().Be("windows-1250");
        detected.GetString(data).Should().Contain("<p>ą</p>");
    }

    [Fact]
    public void Uses_the_meta_charset_declaration()
    {
        byte[] data = Encoding.Latin1.GetBytes("<html><head><meta charset='iso-8859-1'></head><body>x</body></html>");

        HtmlEncodingResolver.GetEncodingForHtml(data).WebName.Should().Be("iso-8859-1");
    }

    [Fact]
    public void Defaults_to_utf8_for_plain_ascii()
    {
        byte[] data = Encoding.ASCII.GetBytes("<html><body>plain</body></html>");

        HtmlEncodingResolver.GetEncodingForHtml(data).WebName.Should().Be("utf-8");
    }

    [Theory]
    [InlineData("cp1252", "windows-1252")]
    [InlineData("CP-1250", "windows-1250")]
    [InlineData("utf-8", "utf-8")]
    public void FixupCodePageMapping_normalizes_cp_names(string input, string expected)
    {
        HtmlEncodingResolver.FixupCodePageMapping(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("plain ascii text", true)]
    [InlineData("valid utf-8: éè中", true)]
    public void IsValidUtf8_accepts_well_formed_sequences(string text, bool expected)
    {
        HtmlEncodingResolver.IsValidUtf8(Encoding.UTF8.GetBytes(text)).Should().Be(expected);
    }

    [Fact]
    public void IsValidUtf8_rejects_a_stray_high_byte()
    {
        HtmlEncodingResolver.IsValidUtf8(new byte[] { (byte)'a', 0xC3, (byte)'b' }).Should().BeFalse();
    }

    [Fact]
    public void ReadHtmlFile_strips_bom_and_normalizes_line_endings()
    {
        using TestSupport.TempDir root = new();
        string full = root.Combine("x.xhtml");
        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] body = Encoding.UTF8.GetBytes("<p>a</p>\r\n<p>b</p>");
        File.WriteAllBytes(full, Concat(bom, body));

        string text = HtmlEncodingResolver.ReadHtmlFile(full);

        text.Should().Be("<p>a</p>\n<p>b</p>");
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
