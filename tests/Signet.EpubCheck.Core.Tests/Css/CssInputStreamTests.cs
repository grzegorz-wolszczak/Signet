using System;
using System.Globalization;
using System.IO;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Css;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Css;

// A port of upstream's CssInputStreamTest: the byte order mark and the @charset rule of a style sheet.
public sealed class CssInputStreamTests
{
    [Theory]
    [InlineData("none.css", null, null)]
    [InlineData("charset-utf8.css", null, "UTF-8")]
    [InlineData("bom-charset15.css", "UTF-8", "iso-8859-15")]
    [InlineData("bom-utf16le.css", "UTF-16LE", null)]
    [InlineData("bom-utf16be.css", "UTF-16BE", null)]
    [InlineData("bom-utf16be-charset.css", "UTF-16BE", "UTF-16BE")]
    [InlineData("bom-utf16le-charset.css", "UTF-16LE", "UTF-16LE")]
    [InlineData("charset-malformed.css", null, null)]
    [InlineData("other-rule.css", null, null)]
    [InlineData("charset-empty.css", null, null)]
    [InlineData("other.txt", null, null)]
    public void Source_ReadsTheByteOrderMarkAndTheCharsetRule(string file, string? bom, string? charset)
    {
        // Arrange
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Css", "TestData", file));

        // Act
        CssSource source = new(file, stream);

        // Assert
        source.Bom.Should().Be(bom);
        source.Charset.Should().Be(charset);
    }

    [Fact]
    public void NewReader_Utf16WithAByteOrderMark_DecodesTheText()
    {
        // Arrange
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Css", "TestData", "bom-utf16be.css"));
        CssSource source = new("bom-utf16be.css", stream);

        // Act
        using TextReader reader = source.NewReader();
        string text = reader.ReadToEnd();

        // Assert
        text.Should().Be("div {color : green }");
    }

    [Fact]
    public void NewReader_CharsetRuleOfAnotherEncoding_DecodesWithIt()
    {
        // Arrange
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        byte[] bytes = [.. System.Text.Encoding.ASCII.GetBytes("@charset \"iso-8859-15\"; a{content:\""), 0xA4, .. System.Text.Encoding.ASCII.GetBytes("\"}")];
        CssSource source = new("sheet.css", new MemoryStream(bytes));

        // Act
        using TextReader reader = source.NewReader();
        string text = reader.ReadToEnd();

        // Assert
        text.Should().EndWith("€\"}");
    }

    [Fact]
    public void Source_FromText_HasNoByteOrderMark()
    {
        // Act
        CssSource source = new("virtual", "a{}");

        // Assert
        source.Bom.Should().BeNull();
        source.SystemId.Should().Be("virtual");
        source.ToString().Should().Contain("virtual");
    }

    [Fact]
    public void ErrorCodes_AllHaveATextInBothLanguages()
    {
        // Arrange
        string[] keys =
        [
            "css.scanner.token.syntax", "css.scanner.token.syntax.char", "css.scanner.token.syntax.firstChar", "css.scanner.token.syntax.escape",
            "css.scanner.token.syntax.urange", "css.scanner.prematureEOF", "css.grammar.prematureEOF", "css.grammar.token.unexpected",
            "css.grammar.token.expecting", "css.grammar.invalidSelector", "a_property_name", "a_property_value",
        ];

        // Act & Assert
        foreach (string key in keys)
        {
            CssException.Text(key, CultureInfo.GetCultureInfo("en")).Should().NotBe(key);
            CssException.Text(key, CultureInfo.GetCultureInfo("pl")).Should().NotBe(key).And.NotBe(CssException.Text(key, CultureInfo.GetCultureInfo("en")));
        }
    }
}
