using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Css;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Css;

// A port of upstream's CssScannerTest (the comments of that file quoting the CSS 2.1 test suite are left out).
public sealed class CssScannerTests
{
    private readonly List<CssException> exceptions = [];

    [Fact]
    public void TestLexerNull()
    {
        // Arrange
        string css = "";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(0);
        exceptions.Count.Should().Be(0);
    }

    [Fact]
    public void TestLexerSpace_10()
    {
        // Arrange
        string css = "  ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.S);
    }

    [Fact]
    public void TestLexerSpace_20()
    {
        // Arrange
        string css = " \n \t \f ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.S);
    }

    [Fact]
    public void TestLexerCDOCDC_10()
    {
        // Arrange
        string css = "<!--";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Cdo);
    }

    [Fact]
    public void TestLexerCDOCDC_20()
    {
        // Arrange
        string css = "-->";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Cdc);
    }

    [Fact]
    public void TestLexerCDOCDC_30()
    {
        // Arrange
        string css = "<!--/*c*/-->";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Cdo);
        tokens[1].Type.Should().Be(CssTokenType.Comment);
        tokens[1].Chars.Length.Should().Be(1);
        tokens[2].Type.Should().Be(CssTokenType.Cdc);
    }

    [Fact]
    public void TestLexerCDOCDC_40()
    {
        // Arrange
        string css = "red-->";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(2);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        (tokens[0].Chars == "red--").Should().BeTrue();
        tokens[1].Type.Should().Be(CssTokenType.Char);
        (tokens[1].Chars == ">").Should().BeTrue();
    }

    [Fact]
    public void TestLexerString_10()
    {
        // Arrange
        string css = "'string'";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Chars.Length.Should().Be(6);
    }

    [Fact]
    public void TestLexerString_20()
    {
        // Arrange
        string css = "\"string\"";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Chars.Length.Should().Be(6);
    }

    [Fact]
    public void TestLexerString_30()
    {
        // Arrange
        string css = "''";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Chars.Length.Should().Be(0);
    }

    [Fact]
    public void TestLexerString_40()
    {
        // Arrange
        string css = "\"string string\"";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Chars.Length.Should().Be(13);
    }

    [Fact]
    public void TestLexerString_50()
    {
        // Arrange
        string css = "\"string\nstring\"";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(1);
    }

    [Fact]
    public void TestLexerString_51()
    {
        // Arrange
        string css = "p { color: green; font-family: 'Courier New Times\n color: red; color: green; }";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(15);
        exceptions.Count.Should().Be(1);
    }

    [Fact]
    public void TestLexerComment_10()
    {
        // Arrange
        string css = "/* comment */";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Comment);
        tokens[0].Chars.Length.Should().Be(9);
    }

    [Fact]
    public void TestLexerComment_15()
    {
        // Arrange
        string css = "/**/";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Comment);
        tokens[0].Chars.Length.Should().Be(0);
    }

    [Fact]
    public void TestLexerComment_16()
    {
        // Arrange
        string css = "div { /*/**/color:green;*/}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        TokenTypeCount(CssTokenType.Comment, tokens).Should().Be(1);
        (HasIdent(tokens, "color")).Should().BeTrue();
        (HasIdent(tokens, "green")).Should().BeTrue();
        tokens[tokens.Count - 2].Type.Should().Be(CssTokenType.Char);
        tokens[tokens.Count - 3].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerComment_17()
    {
        // Arrange
        string css = "div {color: green;}<!--/*#div1{color: red;}-->#div1{color: red;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        (HasIdent(tokens, "red")).Should().BeFalse();
    }

    [Fact]
    public void TestLexerComment_18()
    {
        // Arrange
        string css = "div{/* *\\/*/color: green;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Comment, tokens).Should().Be(1);
        FirstTokenOfType(CssTokenType.Comment, tokens)!.Chars.Should().Be(" *\\/");
        (HasIdent(tokens, "color")).Should().BeTrue();
        (HasIdent(tokens, "green")).Should().BeTrue();
    }

    [Fact]
    public void TestLexerComment_20()
    {
        // Arrange
        string css = "div{\\/*;color: green;*/}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        (HasIdent(tokens, "color")).Should().BeTrue();
        (HasIdent(tokens, "green")).Should().BeTrue();
    }

    [Fact]
    public void TestLexerComment_21()
    {
        // Arrange
        string css = "div{/\\*;color: green;*/}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        (HasIdent(tokens, "color")).Should().BeTrue();
        (HasIdent(tokens, "green")).Should().BeTrue();
    }

    [Fact]
    public void TestLexerSpaceAndComment_10()
    {
        // Arrange
        string css = "  /* c1 */  /* c2 */\t";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(5);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.S);
        tokens[1].Type.Should().Be(CssTokenType.Comment);
        tokens[2].Type.Should().Be(CssTokenType.S);
        tokens[3].Type.Should().Be(CssTokenType.Comment);
        tokens[4].Type.Should().Be(CssTokenType.S);
    }

    [Fact]
    public void TestLexerIdent_10()
    {
        // Arrange
        string css = "ident";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("ident");
    }

    [Fact]
    public void TestLexerIdent_20()
    {
        // Arrange
        string css = "-ident";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("-ident");
    }

    [Fact]
    public void TestLexerIdent_21()
    {
        // Arrange
        string css = "-ident-ident";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("-ident-ident");
    }

    [Fact]
    public void TestLexerIdent_22()
    {
        // Arrange
        string css = "a";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("a");
    }

    [Fact]
    public void TestLexerIdent_23()
    {
        // Arrange
        string css = "-a";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("-a");
    }

    [Fact]
    public void TestLexerIdent_30()
    {
        // Arrange
        string css = "--a";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("--a");
    }

    [Fact]
    public void TestLexerAtKeyword_10()
    {
        // Arrange
        string css = "@ident";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Chars.Should().Be("@ident");
    }

    [Fact]
    public void TestLexerAtKeyword_20()
    {
        // Arrange
        string css = "@-ident";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Chars.Should().Be("@-ident");
    }

    [Fact]
    public void TestLexerAtKeyword_30()
    {
        // Arrange
        string css = "@z";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Chars.Should().Be("@z");
    }

    [Fact]
    public void TestLexerAtKeyword_40()
    {
        // Arrange
        string css = "@- ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        (CssTokenType.Char == tokens[0].Type).Should().BeTrue();
        (CssTokenType.Char == tokens[1].Type).Should().BeTrue();
        (CssTokenType.S == tokens[2].Type).Should().BeTrue();
    }

    [Fact]
    public void TestLexerAtKeyword_41()
    {
        // Arrange
        string css = "@-";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(2);
        (CssTokenType.Char == tokens[0].Type).Should().BeTrue();
        (CssTokenType.Char == tokens[1].Type).Should().BeTrue();
    }

    [Fact]
    public void TestLexerAtKeyword_42()
    {
        // Arrange
        string css = "@\\zoo";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Chars.Should().Be("@zoo");
    }

    [Fact]
    public void TestLexerAtKeyword_43()
    {
        // Arrange
        string css = "@zo\\o";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Chars.Should().Be("@zoo");
    }

    [Fact]
    public void TestLexerUri_10()
    {
        // Arrange
        string css = "url(\"foo\")";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url('foo')");
    }

    [Fact]
    public void TestLexerUri_11()
    {
        // Arrange
        string css = "url(  \"foo\"  )";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url('foo')");
    }

    [Fact]
    public void TestLexerUri_20()
    {
        // Arrange
        string css = "url('foo')";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url('foo')");
    }

    [Fact]
    public void TestLexerUri_30()
    {
        // Arrange
        string css = "url(foo)";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url(foo)");
    }

    [Fact]
    public void TestLexerUri_31()
    {
        // Arrange
        string css = "url(  foo  )";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url(foo)");
    }

    [Fact]
    public void TestLexerUri_40()
    {
        // Arrange
        string css = "UrL(fOo)";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url(fOo)");
    }

    [Fact]
    public void TestLexerUri_50()
    {
        // Arrange
        string css = "url(/*x*/pic.png)";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url(/*x*/pic.png)");
    }

    [Fact]
    public void TestLexerUri_51()
    {
        // Arrange
        string css = "url(pic/*x*/.png)";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Uri);
        tokens[0].Chars.Should().Be("url(pic/*x*/.png)");
    }

    [Fact]
    public void TestLexerUriBad_10()
    {
        // Arrange
        string css = "url(pic.png";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(1);
    }

    [Fact]
    public void TestLexerUriBad_11()
    {
        // Arrange
        string css = "url('pic.png";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(1);
    }

    [Fact]
    public void TestLexerUriBad_12()
    {
        // Arrange
        string css = "url('pic.png) foo";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(1);
    }

    [Fact]
    public void TestLexerUriBad_13()
    {
        // Arrange
        string css = "url('pic.png' foo";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(2);
        exceptions.Count.Should().Be(1);
    }

    [Fact]
    public void TestLexerFunction_10()
    {
        // Arrange
        string css = "ident(";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Chars.Should().Be("ident(");
    }

    [Fact]
    public void TestLexerNum_10()
    {
        // Arrange
        string css = "1";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Integer);
        tokens[0].Chars.Should().Be("1");
    }

    [Fact]
    public void TestLexerNum_11()
    {
        // Arrange
        string css = "1.1";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Chars.Should().Be("1.1");
    }

    [Fact]
    public void TestLexerNum_12()
    {
        // Arrange
        string css = "1.11";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Chars.Should().Be("1.11");
    }

    [Fact]
    public void TestLexerNum_13()
    {
        // Arrange
        string css = ".1";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Chars.Should().Be(".1");
    }

    [Fact]
    public void TestLexerNum_14()
    {
        // Arrange
        string css = ".11";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Chars.Should().Be(".11");
    }

    [Fact]
    public void TestLexerNum_15()
    {
        // Arrange
        string css = "1.";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(2);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Integer);
        tokens[1].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerNum_16()
    {
        // Arrange
        string css = ".";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerQnty_10()
    {
        // Arrange
        string css = "10em";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyEms);
        tokens[0].Chars.Should().Be("10em");
    }

    [Fact]
    public void TestLexerQnty_10REM()
    {
        // Arrange
        string css = "10rem";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyRems);
        tokens[0].Chars.Should().Be("10rem");
    }

    [Fact]
    public void TestLexerQnty_11()
    {
        // Arrange
        string css = "10ex";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyExs);
        tokens[0].Chars.Should().Be("10ex");
    }

    [Fact]
    public void TestLexerQnty_12()
    {
        // Arrange
        string css = "10%";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyPercentage);
        tokens[0].Chars.Should().Be("10%");
    }

    [Fact]
    public void TestLexerQnty_13()
    {
        // Arrange
        string css = "10cm 10px 10mm 10in 10pt 10pc";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(11);
        exceptions.Count.Should().Be(0);
        int c = 0;
        foreach (CssToken tk in tokens) {
        if(tk.Type == CssTokenType.QntyLength) {
        c++;
        tk.Chars.Length.Should().Be(4);
        }
        }
        c.Should().Be(6);
    }

    [Fact]
    public void TestLexerQnty_14()
    {
        // Arrange
        string css = "10deg 10rad 10grad";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(5);
        exceptions.Count.Should().Be(0);
        int c = 0;
        foreach (CssToken tk in tokens) {
        if(tk.Type == CssTokenType.QntyAngle) {
        c++;
        }
        }
        c.Should().Be(3);
    }

    [Fact]
    public void TestLexerQnty_15()
    {
        // Arrange
        string css = "10ms 10s";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        int c = 0;
        foreach (CssToken tk in tokens) {
        if(tk.Type == CssTokenType.QntyTime) {
        c++;
        }
        }
        c.Should().Be(2);
    }

    [Fact]
    public void TestLexerQnty_16()
    {
        // Arrange
        string css = "10hz 10khz";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        int c = 0;
        foreach (CssToken tk in tokens) {
        if(tk.Type == CssTokenType.QntyFreq) {
        c++;
        }
        }
        c.Should().Be(2);
    }

    [Fact]
    public void TestLexerQnty_17()
    {
        // Arrange
        string css = "10DPI 10dpcm";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        int c = 0;
        foreach (CssToken tk in tokens) {
        if(tk.Type == CssTokenType.QntyResolution) {
        c++;
        }
        }
        c.Should().Be(2);
    }

    [Fact]
    public void TestLexerQnty_20()
    {
        // Arrange
        string css = "10foo";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyDimen);
        tokens[0].Chars.Should().Be("10foo");
    }

    [Fact]
    public void TestLexerQnty_21()
    {
        // Arrange
        string css = "10emFOO";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyDimen);
        tokens[0].Chars.Should().Be("10emFOO");
    }

    [Fact]
    public void TestLexerQnty_22()
    {
        // Arrange
        string css = "10em10em";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyDimen);
    }

    [Fact]
    public void TestLexerQnty_23()
    {
        // Arrange
        string css = "10emFoo,10emBAR,10emBIP";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(5);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyDimen);
        tokens[2].Type.Should().Be(CssTokenType.QntyDimen);
        tokens[4].Type.Should().Be(CssTokenType.QntyDimen);
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[3].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerQnty_24()
    {
        // Arrange
        string css = "10emX";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyDimen);
    }

    [Fact]
    public void TestLexerQnty_25()
    {
        // Arrange
        string css = "10\\px";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyLength);
        tokens[0].Chars.Should().Be("10px");
    }

    [Fact]
    public void TestLexerQnty_26()
    {
        // Arrange
        string css = "10e\\m";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyEms);
    }

    [Fact]
    public void TestLexerQnty_27()
    {
        // Arrange
        string css = "1 10 -10 +10 +.10 -.10";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Number, tokens).Should().Be(2);
        TokenTypeCount(CssTokenType.Integer, tokens).Should().Be(4);
    }

    [Fact]
    public void TestLexerQnty_28REM()
    {
        // Arrange
        string css = "1rem 10rem -10rem +10rem +.10rem -.10rem";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyRems, tokens).Should().Be(6);
    }

    [Fact]
    public void TestLexerQnty_28()
    {
        // Arrange
        string css = "1em 10em -10em +10em +.10em -.10em";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyEms, tokens).Should().Be(6);
    }

    [Fact]
    public void TestLexerQnty_29()
    {
        // Arrange
        string css = "1em,10em,-10em,+10em,+.10em,-.10em";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.Char, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyEms, tokens).Should().Be(6);
    }

    [Fact]
    public void TestLexerQnty_30()
    {
        // Arrange
        string css = "1,10,-10,+10,+.10,-.10";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.Char, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Number, tokens).Should().Be(2);
        TokenTypeCount(CssTokenType.Integer, tokens).Should().Be(4);
    }

    [Fact]
    public void TestLexerQnty_40()
    {
        // Arrange
        string css = "1vmin 1cm 1px 1mm 1in 1pt 1pc 1vw 1vh 1q";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(10);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyLength, tokens).Should().Be(10);
    }

    [Fact]
    public void TestLexerQnty_41()
    {
        // Arrange
        string css = "1grad 1turn 1deg 1rad";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(4);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyAngle, tokens).Should().Be(4);
    }

    [Fact]
    public void TestLexerQnty_42()
    {
        // Arrange
        string css = "1dpcm 1dppx 1dpi";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyResolution, tokens).Should().Be(3);
    }

    [Fact]
    public void TestLexerQnty_50()
    {
        // Arrange
        string css = "10ch";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.QntyChs);
        tokens[0].Chars.Should().Be("10ch");
    }

    [Fact]
    public void TestLexerQnty_51()
    {
        // Arrange
        string css = "1ch 10ch -10ch +10ch +.10ch -.10ch";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.S, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyChs, tokens).Should().Be(6);
    }

    [Fact]
    public void TestLexerQnty_52()
    {
        // Arrange
        string css = "1ch,10ch,-10ch,+10ch,+.10ch,-.10ch";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        StripTokens(CssTokenType.Char, tokens).Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.QntyChs, tokens).Should().Be(6);
    }

    [Fact]
    public void TestLexerMQ_10()
    {
        // Arrange
        string css = "only";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Only);
        tokens[0].Chars.Should().Be("only");
    }

    [Fact]
    public void TestLexerMQ_11()
    {
        // Arrange
        string css = "ONLy";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Only);
        tokens[0].Chars.Should().Be("only");
    }

    [Fact]
    public void TestLexerMQ_12()
    {
        // Arrange
        string css = "onlyFOO";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
    }

    [Fact]
    public void TestLexerMQ_20()
    {
        // Arrange
        string css = "not";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Not);
        tokens[0].Chars.Should().Be("not");
    }

    [Fact]
    public void TestLexerMQ_21()
    {
        // Arrange
        string css = "NoT";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Not);
        tokens[0].Chars.Should().Be("not");
    }

    [Fact]
    public void TestLexerMQ_22()
    {
        // Arrange
        string css = "notFOO";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
    }

    [Fact]
    public void TestLexerMQ_30()
    {
        // Arrange
        string css = "and";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.And);
        tokens[0].Chars.Should().Be("and");
    }

    [Fact]
    public void TestLexerMQ_31()
    {
        // Arrange
        string css = "AND";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.And);
        tokens[0].Chars.Should().Be("and");
    }

    [Fact]
    public void TestLexerMQ_32()
    {
        // Arrange
        string css = "andFOO";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
    }

    [Fact]
    public void TestLexerIncludes10()
    {
        // Arrange
        string css = "~=";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Includes);
    }

    [Fact]
    public void TestLexerDashmatch10()
    {
        // Arrange
        string css = "|=";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.DashMatch);
    }

    [Fact]
    public void TestLexerSelectors3_10()
    {
        // Arrange
        string css = "^= $= *=";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(5);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.PrefixMatch);
        tokens[2].Type.Should().Be(CssTokenType.SuffixMatch);
        tokens[4].Type.Should().Be(CssTokenType.SubstringMatch);
    }

    [Fact]
    public void TestLexerSelectors3_20()
    {
        // Arrange
        string css = "p[title*='hello']";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(6);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[3].Type.Should().Be(CssTokenType.SubstringMatch);
    }

    [Fact]
    public void TestLexerHash10()
    {
        // Arrange
        string css = "#n";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.HashName);
        tokens[0].Chars.Should().Be("#n");
    }

    [Fact]
    public void TestLexerHash20()
    {
        // Arrange
        string css = "#name";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.HashName);
        tokens[0].Chars.Should().Be("#name");
    }

    [Fact]
    public void TestLexerHash30()
    {
        // Arrange
        string css = "#";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerHash40()
    {
        // Arrange
        string css = "#!";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(2);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Char);
        tokens[1].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerHash50()
    {
        // Arrange
        string css = "#name 89";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.HashName);
        tokens[1].Type.Should().Be(CssTokenType.S);
        tokens[2].Type.Should().Be(CssTokenType.Integer);
    }

    [Fact]
    public void TestLexerHash51()
    {
        // Arrange
        string css = "#\\name\\name";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.HashName);
        tokens[0].Chars.Should().Be("#namename");
    }

    [Fact]
    public void TestLexerImportant10()
    {
        // Arrange
        string css = "!important";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Important);
    }

    [Fact]
    public void TestLexerImportant20()
    {
        // Arrange
        string css = "!  \t\t\t\r\n\fimportant";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Important);
    }

    [Fact]
    public void TestLexerImportant30()
    {
        // Arrange
        string css = "!  IMPORTANT";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(1);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Important);
    }

    [Fact]
    public void TestLexerImportant40()
    {
        // Arrange
        string css = "!importan  ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        tokens.Count.Should().Be(3);
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Char);
        tokens[1].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Type.Should().Be(CssTokenType.S);
    }

    [Fact]
    public void TestLexerUrange10()
    {
        // Arrange
        string css = "U+A5 U+0-7F U+590-5ff U+4E00-9FFF U+30?? U+FF00-FF9F U+??? U+0??? ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Urange, tokens).Should().Be(8);
    }

    [Fact]
    public void TestLexerUrange20()
    {
        // Arrange
        string css = "U+A5,U+0-7F,U+590-5ff,U+4E00-9FFF,U+30??,U+FF00-FF9F,U+???,U+0???";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Urange, tokens).Should().Be(8);
    }

    [Fact]
    public void TestLexerUrange30()
    {
        // Arrange
        string css = "U+0?????? U+ ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(1);
        tokens[0].Type.Should().Be(CssTokenType.Urange);
        TokenTypeCount(CssTokenType.Urange, tokens).Should().Be(1);
        TokenTypeCount(CssTokenType.Ident, tokens).Should().Be(1);
        TokenTypeCount(CssTokenType.Char, tokens).Should().Be(1);
    }

    [Fact]
    public void TestLexerUrange40()
    {
        // Arrange
        string css = "U+0????? U+00000A-00000F ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Urange, tokens).Should().Be(2);
    }

    [Fact]
    public void TestLexerUrange50()
    {
        // Arrange
        string css = "\\U+A U+A";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        TokenTypeCount(CssTokenType.Urange, tokens).Should().Be(1);
    }

    [Fact]
    public void TestLexerEscape09()
    {
        // Arrange
        string css = "\\";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(1);
        tokens[0].Type.Should().Be(CssTokenType.Char);
    }

    [Fact]
    public void TestLexerEscape10()
    {
        // Arrange
        string css = "'a not s\\\no very long title'";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Chars.Should().Be("a not so very long title");
    }

    [Fact]
    public void TestLexerEscape11()
    {
        // Arrange
        string css = "'an escaped newline: \\A'";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Chars.Should().Be("an escaped newline: \\A");
    }

    [Fact]
    public void TestLexerEscape12()
    {
        // Arrange
        string css = "\\64";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("d");
    }

    [Fact]
    public void TestLexerEscape13()
    {
        // Arrange
        string css = "\\p";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("p");
    }

    [Fact]
    public void TestLexerEscape14()
    {
        // Arrange
        string css = "\\px";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("px");
    }

    [Fact]
    public void TestLexerEscape15()
    {
        // Arrange
        string css = "\\64 \\64 ";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("dd");
    }

    [Fact]
    public void TestLexerEscape16()
    {
        // Arrange
        string css = "#f\\oo";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens[0].Type.Should().Be(CssTokenType.HashName);
        tokens[0].Chars.Should().Be("#foo");
    }

    [Fact]
    public void TestLexerEscape17()
    {
        // Arrange
        string css = "\\64\\69\\76{\\63\\6F\\6C\\6F\\72:\\67\\72\\65\\65\\6E;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape18()
    {
        // Arrange
        string css = "di\\v{c\\o\\l\\o\\r:green;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape19()
    {
        // Arrange
        string css = "\\64\\69\\76 {\\63 \\6F \\6C \\6F \\72 :\\67 \\72 \\65 \\65 \\6E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape20()
    {
        // Arrange
        string css = "\\64\\69\\76 {\\63 \\6F \\6C \\6F \\72 :\\67 \\72 \\65 \\65 \\6E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape21()
    {
        // Arrange
        string css = "div{\\63 \\06F \\006C \\0006F \\72 :\\067 \\0072 \\00065 \\00065 \\6E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape22()
    {
        // Arrange
        string css = "div{c\\00006Fl\\00006Fr:\\000067r\\000065e\\00006E;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape23()
    {
        // Arrange
        string css = "div{\\000063 \\00006F \\00006C \\00006F \\000072 :\\000067 \\000072 \\000065 \\000065 \\00006E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape24()
    {
        // Arrange
        string css = "div{\\63 \\06F \\006C \\0006F \\72 :\\067 \\0072 \\00065 \\00065 \\6E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape25()
    {
        // Arrange
        string css = "div{\\63 \\6F \\6C \\6F \\72 :\\67 \\72 ee\\6E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape26()
    {
        // Arrange
        string css = "div{\\63\n\\06F\fl\\0006F\rr:g\\0072\t\\00065\r\fe\\6E ;}";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(7);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("div");
        tokens[1].Type.Should().Be(CssTokenType.Char);
        tokens[1].Chars.Should().Be("{");
        tokens[2].Type.Should().Be(CssTokenType.Ident);
        tokens[2].Chars.Should().Be("color");
        tokens[3].Type.Should().Be(CssTokenType.Char);
        tokens[3].Chars.Should().Be(":");
        tokens[4].Type.Should().Be(CssTokenType.Ident);
        tokens[4].Chars.Should().Be("green");
        tokens[5].Type.Should().Be(CssTokenType.Char);
        tokens[5].Chars.Should().Be(";");
        tokens[6].Type.Should().Be(CssTokenType.Char);
        tokens[6].Chars.Should().Be("}");
    }

    [Fact]
    public void TestLexerEscape27()
    {
        // Arrange
        string css = "\\0020red";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(1);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("\\0020red");
    }

    [Fact]
    public void TestLexerEscape28()
    {
        // Arrange
        string css = "\\0020red\\0020red\\0020";

        // Act
        List<CssToken> tokens = Scan(css);

        // Assert
        exceptions.Count.Should().Be(0);
        tokens.Count.Should().Be(1);
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Chars.Should().Be("\\0020red\\0020red\\0020");
    }

    private List<CssToken> Scan(string css)
    {
        exceptions.Clear();
        List<CssToken> tokens = [];
        new CssScanner(new StringReader(css), CssLocation.NoSystemId, new ErrorListener(exceptions), tokens.Add, System.Globalization.CultureInfo.GetCultureInfo("en")).Scan();
        return tokens;
    }

    private static bool HasIdent(List<CssToken> tokens, string ident) => tokens.Exists(token => token.Type == CssTokenType.Ident && token.Chars == ident);

    private static int TokenTypeCount(CssTokenType type, List<CssToken> tokens) => tokens.FindAll(token => token.Type == type).Count;

    private static CssToken? FirstTokenOfType(CssTokenType type, List<CssToken> tokens) => tokens.Find(token => token.Type == type);

    private static List<CssToken> StripTokens(CssTokenType type, List<CssToken> tokens) => tokens.FindAll(token => token.Type != type);

    private sealed class ErrorListener(List<CssException> errors) : ICssErrorHandler
    {
        public void HandleError(CssException exception) => errors.Add(exception);
    }
}
