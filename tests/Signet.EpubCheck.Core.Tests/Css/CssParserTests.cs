using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Css;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Css;

// The grammar of the CSS parser, with the cases of upstream's CssParserTest that show how rules, selectors, values and errors are read.
public sealed class CssParserTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void Parse_EmptyStyleSheet_OnlyStartsAndEndsTheDocument(string css)
    {
        // Act
        Recorder recorder = Parse(css);

        // Assert
        recorder.Calls.Should().Equal("startDocument", "endDocument");
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AtRulesWithStringParameters_AreReported()
    {
        // Act
        Recorder recorder = Parse("@charset \t 'UTF-8'; <!-- @namespace 'http://foo/ns' ; --> ");

        // Assert
        recorder.AtRules.Select(rule => rule.Name).Should().Equal("@charset", "@namespace");
        recorder.AtRules.Select(rule => rule.Components[0].ToCssString()).Should().Equal("'UTF-8'", "'http://foo/ns'");
        recorder.AtRules.Should().OnlyContain(rule => rule.Components.Count == 1 && rule.Components[0].Type == CssConstructType.StringLiteral);
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RuleSetsAndDeclarations_AreCounted()
    {
        // Act
        Recorder recorder = Parse("@charset 'UTF-8'; selector1 { name1a : property1a ; name1b : property1b } selector2 { name2a : property2a ; name2b : property2b ; } ");

        // Assert
        recorder.AtRules.Should().ContainSingle();
        recorder.Selectors.Should().HaveCount(2);
        recorder.Declarations.Should().HaveCount(4);
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyRuleSets_HaveNoDeclarations()
    {
        // Act
        Recorder recorder = Parse("E { } E{}");

        // Assert
        recorder.Selectors.Should().HaveCount(2);
        recorder.Declarations.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Lengths_AreQuantitiesOfTheLengthUnit()
    {
        // Act
        Recorder recorder = Parse("E { border : 1px 2px 3px 4px } ");

        // Assert
        recorder.Declarations.Should().ContainSingle().Which.Components.Should().HaveCount(4)
            .And.OnlyContain(construct => construct is CssQuantity && ((CssQuantity)construct).Unit == CssQuantityUnit.Length);
    }

    [Fact]
    public void Parse_FunctionValue_IsAFunctionWithItsArguments()
    {
        // Act
        Recorder recorder = Parse("E { foo : bar(1px 2em 'foo' bar) } ");

        // Assert
        CssFunction function = recorder.Declarations.Single().Components.Single().Should().BeOfType<CssFunction>().Subject;
        function.Name.Should().Be("bar");
        function.Components.Select(component => component.Type).Should().Equal(
            CssConstructType.Quantity, CssConstructType.Quantity, CssConstructType.StringLiteral, CssConstructType.Keyword);
    }

    [Fact]
    public void Parse_ImportantDeclaration_IsMarked()
    {
        // Act
        Recorder recorder = Parse("p { color : red !important ; margin : 0 }");

        // Assert
        recorder.Declarations.Select(declaration => declaration.Important).Should().Equal(true, false);
        recorder.Declarations[0].ToCssString().Should().Be("color : red !important ;");
    }

    [Fact]
    public void Parse_PropertyNames_AreLowerCased()
    {
        // Act
        Recorder recorder = Parse("p { COLOR : Red }");

        // Assert
        recorder.Declarations.Single().Name.Should().Be("color");
        recorder.Declarations.Single().Components.Single().ToCssString().Should().Be("Red");
    }

    [Fact]
    public void Parse_Urls_AreUriConstructs()
    {
        // Act
        Recorder recorder = Parse("p { background : url( 'a.png' ) ; cursor : url(b.cur) }");

        // Assert
        recorder.Declarations.Select(declaration => ((CssURI)declaration.Components[0]).ToUriString()).Should().Equal("a.png", "b.cur");
    }

    [Fact]
    public void Parse_CustomPropertyWithoutAValue_IsAccepted()
    {
        // Act
        Recorder recorder = Parse("p { --empty: ; color: red }");

        // Assert
        recorder.Declarations.Select(declaration => declaration.Name).Should().Equal("--empty", "color");
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Selectors_AreSplitIntoSequencesAndCombinators()
    {
        // Act
        Recorder recorder = Parse("div.note > p:first-child + a[href^='http'] , ul li::before { }");

        // Assert
        List<CssSelector> selectors = recorder.Selectors.Single();
        selectors.Should().HaveCount(2);
        selectors[0].ToCssString().Should().Be("div.note>p:first-child+a[href^='http']");
        selectors[0].Components.OfType<CssSelectorCombinator>().Select(combinator => combinator.CombinatorType)
            .Should().Equal(CssCombinatorType.Child, CssCombinatorType.AdjacentSibling);
        selectors[1].Components.OfType<CssSelectorCombinator>().Single().CombinatorType.Should().Be(CssCombinatorType.Descendant);
        CssPseudoSelector pseudo = selectors[1].Components.OfType<CssSimpleSelectorSequence>().Last().Components.OfType<CssPseudoSelector>().Single();
        pseudo.SubType.Should().Be(CssPseudoSelectorType.PseudoElement);
        pseudo.Name.Should().Be("::before");
    }

    [Fact]
    public void Parse_AttributeSelectors_KeepTheirMatchers()
    {
        // Act
        Recorder recorder = Parse("a[href], a[rel~=x], a[lang|=en], a[href$='.pdf'], a[href*=\"foo\"] { }");

        // Assert
        List<CssAttributeMatchSelector> matches = recorder.Selectors.Single().SelectMany(CssGrammar.Flatten).OfType<CssAttributeMatchSelector>().ToList();
        matches.Select(match => match.AttributeMatchType).Should().Equal(
            CssAttributeMatchType.Includes, CssAttributeMatchType.DashMatch, CssAttributeMatchType.SuffixMatch, CssAttributeMatchType.SubstringMatch);
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NegationAndNthSelectors_AreFunctionalPseudos()
    {
        // Act
        Recorder recorder = Parse("html|*:not(:link):not(:visited) , img:nth-of-type(2n+1) , *|*:not(*) { float : right }");

        // Assert
        recorder.Errors.Should().BeEmpty();
        recorder.Selectors.Single().Should().HaveCount(3);
        recorder.Selectors.Single()[1].Components.OfType<CssSimpleSelectorSequence>().Single().Components.OfType<CssPseudoSelector>().Single().Function!.Name.Should().Be(":nth-of-type");
    }

    [Fact]
    public void Parse_AtRuleWithRuleSets_ReportsTheNestedRules()
    {
        // Act
        Recorder recorder = Parse("@media screen and (min-width: 100px) { p { color: red } a { color: blue } }");

        // Assert
        recorder.Calls.Should().Equal("startDocument", "startAtRule @media", "selectors", "declaration color", "selectors", "declaration color", "endAtRule @media", "endDocument");
        recorder.AtRules.Single().HasBlock.Should().BeTrue();
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AtRuleWithDeclarations_ReportsTheDeclarationsInside()
    {
        // Act
        Recorder recorder = Parse("@font-face { font-family: 'X'; src: url(x.woff) format('woff'); }");

        // Assert
        recorder.AtRules.Single().Name.Should().Be("@font-face");
        recorder.Declarations.Select(declaration => declaration.Name).Should().Equal("font-family", "src");
    }

    [Fact]
    public void Parse_Keyframes_AcceptPercentageSelectors()
    {
        // Act
        Recorder recorder = Parse("@keyframes k { 0% { top: 0 } 50.5% { top: 5px } to { top: 10px } }");

        // Assert
        recorder.Selectors.Should().HaveCount(3);
        recorder.Declarations.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_DeclarationWithoutAColon_IsReportedAndSkipped()
    {
        // Act
        Recorder recorder = Parse("p { color red ; margin : 0 }");

        // Assert
        recorder.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(CssErrorCode.GrammarExpectingToken);
        recorder.Declarations.Select(declaration => declaration.Name).Should().Equal("margin");
    }

    [Fact]
    public void Parse_DeclarationWithoutAValue_IsReported()
    {
        // Act
        Recorder recorder = Parse("p { color : ; margin : 0 }");

        // Assert
        recorder.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(CssErrorCode.GrammarExpectingToken);

        // like upstream, the parser skips on to the end of the block after this error
        recorder.Declarations.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NumberInPlaceOfAPropertyName_IsReported()
    {
        // Act
        Recorder recorder = Parse("p { 12 : red ; color : blue }");

        // Assert
        recorder.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(CssErrorCode.GrammarExpectingToken);
        recorder.Declarations.Select(declaration => declaration.Name).Should().Equal("color");
    }

    [Theory]
    [InlineData("p { color : red", "'}'")]
    [InlineData("p { color", ":")]
    [InlineData("p", "'{'")]
    [InlineData("@media screen", "'{'")]
    public void Parse_UnexpectedEndOfFile_IsReportedOnceAndEndsTheDocument(string css, string expected)
    {
        // Act
        Recorder recorder = Parse(css);

        // Assert
        recorder.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(CssErrorCode.GrammarPrematureEof);
        recorder.Errors[0].Message.Should().Contain(expected);
        recorder.Calls[^1].Should().Be("endDocument");
    }

    [Fact]
    public void Parse_UnbalancedClosingParenthesisInASelector_IsReported()
    {
        // Act
        Recorder recorder = Parse("p ) { color : red } a { color : blue }");

        // Assert
        recorder.Errors.Should().NotBeEmpty();
        recorder.Declarations.Select(declaration => declaration.Name).Should().Equal("color");
    }

    [Fact]
    public void Parse_ErrorHandlerThatThrows_StopsTheParse()
    {
        // Arrange
        Recorder recorder = new() { ThrowOnError = true };

        // Act
        Action act = () => new CssParser(CultureInfo.GetCultureInfo("en")).Parse(new StringReader("p { color red }"), "x", recorder, recorder);

        // Assert
        act.Should().Throw<CssException>();
    }

    [Fact]
    public void ParseStyleAttribute_Declarations_AreReportedWithoutARuleSet()
    {
        // Arrange
        Recorder recorder = new();

        // Act
        new CssParser(CultureInfo.GetCultureInfo("en")).ParseStyleAttribute(new StringReader(";color: red; margin: 0 auto !important"), "x", recorder, recorder);

        // Assert
        recorder.Declarations.Select(declaration => declaration.Name).Should().Equal("color", "margin");
        recorder.Declarations[1].Important.Should().BeTrue();
        recorder.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseStyleAttribute_InvalidDeclaration_IsReportedAndStops()
    {
        // Arrange
        Recorder recorder = new();

        // Act
        new CssParser(CultureInfo.GetCultureInfo("en")).ParseStyleAttribute(new StringReader("color red; margin: 0"), "x", recorder, recorder);

        // Assert
        recorder.Errors.Should().ContainSingle();
        recorder.Declarations.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ScannerErrors_ReachTheErrorHandler()
    {
        // Act
        Recorder recorder = Parse("p { content : 'unterminated\n; color : red }");

        // Assert
        recorder.Errors.Should().Contain(error => error.ErrorCode == CssErrorCode.ScannerIllegalChar);
    }

    [Fact]
    public void Parse_Locations_AreLinesAndColumnsFromOne()
    {
        // Act
        Recorder recorder = Parse("p {\n  color: red\n}");

        // Assert
        CssLocation location = recorder.Declarations.Single().Location;
        location.Line.Should().Be(2);
        location.Column.Should().Be(3);
    }

    [Theory]
    [InlineData("caja.css")]
    [InlineData("columns.css")]
    [InlineData("counter-styles.css")]
    [InlineData("custom-properties.css")]
    [InlineData("flexbox.css")]
    [InlineData("issue231-crlf.css")]
    [InlineData("issue231-lf.css")]
    [InlineData("issue240-1.css")]
    [InlineData("issue240-2.css")]
    [InlineData("issue241.css")]
    [InlineData("issue262.css")]
    [InlineData("keyframes.css")]
    [InlineData("misc.css")]
    [InlineData("samples.css")]
    public void Parse_StyleSheetsOfUpstream_DoNotFail(string file)
    {
        // Arrange
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Css", "TestData", file));
        CssSource source = new(file, stream);
        Recorder recorder = new();

        // Act
        new CssParser(CultureInfo.GetCultureInfo("en")).Parse(source, recorder, recorder);

        // Assert
        recorder.Calls[0].Should().Be("startDocument");
        recorder.Calls[^1].Should().Be("endDocument");
        recorder.StartAtRules.Should().Be(recorder.EndAtRules);
    }

    [Fact]
    public void Parse_KeyframesSheet_HasNoGrammarError()
    {
        // Arrange
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Css", "TestData", "keyframes.css"));
        Recorder recorder = new();

        // Act
        new CssParser(CultureInfo.GetCultureInfo("en")).Parse(new CssSource("keyframes.css", stream), recorder, recorder);

        // Assert
        recorder.Errors.Where(error => error.ErrorCode != CssErrorCode.GrammarUnexpectedToken).Should().BeEmpty();
    }

    private static Recorder Parse(string css)
    {
        Recorder recorder = new();
        new CssParser(CultureInfo.GetCultureInfo("en")).Parse(new StringReader(css), CssLocation.NoSystemId, recorder, recorder);
        return recorder;
    }

    private sealed class Recorder : ICssErrorHandler, ICssContentHandler
    {
        public bool ThrowOnError { get; init; }

        public List<string> Calls { get; } = [];

        public List<CssException> Errors { get; } = [];

        public List<CssAtRule> AtRules { get; } = [];

        public List<CssDeclaration> Declarations { get; } = [];

        public List<List<CssSelector>> Selectors { get; } = [];

        public int StartAtRules { get; private set; }

        public int EndAtRules { get; private set; }

        public void HandleError(CssException exception)
        {
            Errors.Add(exception);
            if (ThrowOnError)
            {
                throw exception;
            }
        }

        public void StartDocument() => Calls.Add("startDocument");

        public void EndDocument() => Calls.Add("endDocument");

        public void StartAtRule(CssAtRule atRule)
        {
            StartAtRules++;
            AtRules.Add(atRule);
            Calls.Add("startAtRule " + atRule.Name);
        }

        public void EndAtRule(string name)
        {
            EndAtRules++;
            Calls.Add("endAtRule " + name);
        }

        public void Selectors_(IReadOnlyList<CssSelector> selectors) => Selectors.Add([.. selectors]);

        void ICssContentHandler.Selectors(IReadOnlyList<CssSelector> selectors)
        {
            Selectors_(selectors);
            Calls.Add("selectors");
        }

        public void EndSelectors(IReadOnlyList<CssSelector> selectors)
        {
        }

        public void Declaration(CssDeclaration declaration)
        {
            Declarations.Add(declaration);
            Calls.Add("declaration " + declaration.Name);
        }
    }
}
