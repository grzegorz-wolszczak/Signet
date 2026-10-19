using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests for <see cref="CssInfo"/>: selectors, offsets, values, reformatting, removal.</summary>
public sealed class CssInfoTests
{
    private const string Sample =
        "/* c */\n" +
        "body { margin: 0; color: #333 }\n" +
        "h1, .title , div.note { font-weight: bold }\n" +
        "@media screen and (max-width: 600px) {\n" +
        "  .responsive { display: none !important }\n" +
        "  p > span.hl { color: red }\n" +
        "}\n" +
        "@font-face { font-family: \"X\"; src: url(x.woff2) }\n" +
        "a:hover { text-decoration: underline }\n" +
        ".a.b.c { x: 1 }\n";

    private static string CssFixture(string name) =>
        File.ReadAllText(Path.Combine(CorpusPaths.Root, "css", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    // =====================================================================
    //  Selector list and decomposition semantics
    // =====================================================================

    [Fact]
    public void GetAllSelectors_SplitsGroupsAndSkipsAtRules()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act & Assert
        info.GetAllSelectors().Select(s => s.Text)
            .Should().Equal("body", "h1", ".title", "div.note", ".responsive", "p > span.hl", "a:hover", ".a.b.c");
    }

    [Fact]
    public void GetAllSelectors_GroupMembersShareTheGroupPosition()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act
        var group = info.GetAllSelectors().Where(s => s.Text is "h1" or ".title" or "div.note").ToList();

        // Assert
        group.Select(s => s.Pos).Distinct().Should().ContainSingle();
        Sample[group[0].Pos..].Should().StartWith("h1,");
    }

    [Theory]
    [InlineData("body", "body", "")]
    [InlineData("div.note", "div", "note")]
    [InlineData(".title", "", "title")]
    [InlineData(".a.b.c", "", "a")]          // only the first class is reported
    [InlineData("p > span.hl", "", "")]       // combinator → no decomposition
    [InlineData("a:hover", "", "")]           // pseudo-class → no decomposition
    public void GetAllSelectors_ClassAndElementSemantics(string selectorText, string element, string className)
    {
        // Arrange
        CssInfo info = new(selectorText + " { x: 1 }");

        // Act
        CssSelector selector = info.GetAllSelectors().Single(s => s.Text == selectorText);

        // Assert
        selector.ElementName.Should().Be(element);
        selector.ClassName.Should().Be(className);
    }

    [Fact]
    public void GetAllSelectors_RicherDecompositionIsBracketAware()
    {
        // Arrange
        CssInfo info = new("p[data-x=\"a.b, c\"] > span.big.hl#lead:hover::before { x: 1 }");

        // Act
        CssSelector s = info.GetAllSelectors().Single();

        // Assert
        s.ClassNames.Should().Equal("big", "hl");
        s.Ids.Should().Equal("lead");
        s.ElementNames.Should().Equal("p", "span");
        s.PseudoClasses.Should().Equal("hover", "before");
        s.HasCombinator.Should().BeTrue();
        s.HasPseudo.Should().BeTrue();
    }

    [Fact]
    public void SplitGroupSelector_IgnoresCommasInsideBracketsParensAndStrings()
    {
        // Act & Assert
        CssInfo.SplitGroupSelector("a[title=\"x, y\"], :is(b, i), .c")
            .Should().Equal("a[title=\"x, y\"]", ":is(b, i)", ".c");
    }

    // =====================================================================
    //  Queries
    // =====================================================================

    [Fact]
    public void GetClassSelectors_FiltersByClassName()
    {
        // Arrange
        CssInfo info = new(".note { a: 1 } p.note { b: 2 } .other { c: 3 }");

        // Act & Assert
        info.GetClassSelectors("note").Select(s => s.Text).Should().Equal(".note", "p.note");

        // Act & Assert
        info.GetClassSelectors().Select(s => s.Text).Should().Equal(".note", "p.note", ".other");
    }

    [Fact]
    public void GetCssSelectorForElementClass_PrefersElementClassThenWildcardClass()
    {
        // Arrange
        CssInfo info = new(".hl { a: 1 } code.hl { b: 2 }");

        // Act & Assert
        info.GetCssSelectorForElementClass("code", "hl")!.Text.Should().Be(".hl");

        // Act & Assert
        info.GetCssSelectorForElementClass("span", "missing").Should().BeNull();
    }

    [Fact]
    public void GetCssSelectorForElementClass_ElementOnlyWhenClassEmpty()
    {
        // Arrange
        CssInfo info = new("div { a: 1 } div.note { b: 2 }");

        // Act & Assert
        info.GetCssSelectorForElementClass("div", string.Empty)!.Text.Should().Be("div");
    }

    [Fact]
    public void GetAllSelectorsWithCombinators_ReturnsOnlyCombinatorSelectors()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act & Assert
        info.GetAllSelectorsWithCombinators().Select(s => s.Text).Should().Equal("p > span.hl");
    }

    [Fact]
    public void GetAllPropertyValues_CollectsFromSelectorRulesIncludingInsideAtMedia()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act & Assert
        info.GetAllPropertyValues("color").Should().Equal("#333", "red");
    }

    [Fact]
    public void GetAllPropertyValues_EmptyPropertyReturnsEveryValue()
    {
        // Arrange
        CssInfo info = new("p { color: red; margin: 0 }");

        // Act & Assert
        info.GetAllPropertyValues(string.Empty).Should().Equal("red", "0");
    }

    [Fact]
    public void GetAllPropertyValues_IgnoresFontFaceDeclarations()
    {
        // Arrange
        CssInfo info = new("@font-face { font-family: \"X\" }\np { font-family: serif }");

        // Act & Assert
        info.GetAllPropertyValues("font-family").Should().Equal("serif");
    }

    // =====================================================================
    //  Rules and offsets
    // =====================================================================

    [Fact]
    public void Rules_OffsetsDelimitEachRuleExactly()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act
        foreach (CssRule rule in info.Rules)
        {
            // Assert
            Sample[rule.BlockStart].Should().Be('{');
            Sample[rule.BlockEnd - 1].Should().Be('}');
            Sample[rule.SelectorStart..rule.BlockStart].TrimEnd().Should().Be(rule.SelectorText.Length == 0 ? rule.AtRulePrelude : rule.SelectorText.TrimEnd());
        }
    }

    [Fact]
    public void Rules_CuttingByOffsetsLeavesTheRestParseable()
    {
        // Arrange
        CssInfo info = new(Sample);
        CssRule target = info.Rules.Single(r => r.SelectorText == "a:hover");

        // Act
        string without = Sample[..target.SelectorStart] + Sample[target.BlockEnd..];

        // Assert
        without.Should().NotContain("a:hover");
        new CssInfo(without).GetAllSelectors().Select(s => s.Text).Should().NotContain("a:hover").And.Contain("body");
    }

    [Fact]
    public void Rules_FontFaceHasEmptySelectorAndAtPrelude()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act
        CssRule fontFace = info.Rules.Single(r => r.AtRulePrelude == "@font-face");

        // Assert
        fontFace.SelectorText.Should().BeEmpty();
        fontFace.Declarations.Select(d => d.Property).Should().Equal("font-family", "src");
    }

    [Fact]
    public void Rules_AtMediaChildrenCarryTheMediaPrelude()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act & Assert
        info.Rules.Where(r => r.SelectorText is ".responsive" or "p > span.hl")
            .Should().OnlyContain(r => r.AtRulePrelude == "@media screen and (max-width: 600px)");
    }

    [Fact]
    public void Rules_DetectsImportant()
    {
        // Arrange
        CssInfo info = new(Sample);

        // Act & Assert
        info.Rules.Single(r => r.SelectorText == ".responsive").Declarations.Single().IsImportant.Should().BeTrue();

        // Act & Assert
        info.Rules.Single(r => r.SelectorText == "body").Declarations.Should().OnlyContain(d => !d.IsImportant);
    }

    // =====================================================================
    //  RemoveMatchingSelectors
    // =====================================================================

    [Fact]
    public void RemoveMatchingSelectors_RemovesOneMemberFromAGroupKeepingTheRest()
    {
        // Arrange
        CssInfo info = new(Sample);
        CssSelector title = info.GetAllSelectors().Single(s => s.Text == ".title");

        // Act
        string result = info.RemoveMatchingSelectors(new[] { title })!;

        // Assert
        result.Should().Contain("h1,div.note {").And.NotContain(".title");
    }

    [Fact]
    public void RemoveMatchingSelectors_RemovesWholeRuleWhenGroupEmpties()
    {
        // Arrange
        CssInfo info = new(Sample);
        CssSelector hover = info.GetAllSelectors().Single(s => s.Text == "a:hover");

        // Act
        string result = info.RemoveMatchingSelectors(new[] { hover })!;

        // Assert
        result.Should().NotContain("a:hover").And.NotContain("text-decoration");
        new CssInfo(result).ParseErrors.Should().BeEmpty();
    }

    [Fact]
    public void RemoveMatchingSelectors_ReturnsNullWhenNothingMatches()
    {
        // Arrange
        CssInfo info = new(Sample);
        CssSelector foreign = new CssInfo(".ghost { a: 1 }").GetAllSelectors().Single();

        // Act & Assert
        info.RemoveMatchingSelectors(new[] { foreign }).Should().BeNull();
    }

    [Fact]
    public void RemoveMatchingSelectors_ReturnsPlaceholderWhenEverythingRemoved()
    {
        // Arrange
        CssInfo info = new("p { a: 1 }");

        // Act & Assert
        info.RemoveMatchingSelectors(info.GetAllSelectors()).Should().Be("/* CSS */\n");
    }

    // =====================================================================
    //  Reformat
    // =====================================================================

    [Fact]
    public void GetReformattedCssText_Multiline_MatchesGolden()
    {
        // Act
        string result = new CssInfo(CssFixture("messy-in.css")).GetReformattedCssText(true);

        // Assert
        result.Should().Be(CssFixture("messy-out-multi.css").TrimEnd('\n'));
    }

    [Fact]
    public void GetReformattedCssText_SingleLine_MatchesGolden()
    {
        // Act
        string result = new CssInfo(CssFixture("messy-in.css")).GetReformattedCssText(false);

        // Assert
        result.Should().Be(CssFixture("messy-out-single.css").TrimEnd('\n'));
    }

    [Fact]
    public void GetReformattedCssText_IsIdempotent()
    {
        // Act
        string once = new CssInfo(CssFixture("messy-in.css")).GetReformattedCssText(true);
        string twice = new CssInfo(once).GetReformattedCssText(true);

        // Assert
        twice.Should().Be(once);
    }

    [Fact]
    public void GetReformattedCssText_ReturnsSourceUnchangedOnStructuralError()
    {
        // Arrange
        const string broken = "body { color: red ";

        // Act & Assert
        new CssInfo(broken).GetReformattedCssText(true).Should().Be(broken);
    }

    [Theory]
    [MemberData(nameof(AllCorpusCss))]
    public void GetReformattedCssText_EveryCorpusStylesheet_IsIdempotent(string cssPath)
    {
        // Arrange
        string source = File.ReadAllText(cssPath);

        // Act
        string once = new CssInfo(source).GetReformattedCssText(true);
        string twice = new CssInfo(once).GetReformattedCssText(true);

        // Assert
        twice.Should().Be(once);
    }

    public static TheoryData<string> AllCorpusCss()
    {
        TheoryData<string> data = new();
        foreach (string path in Directory.EnumerateFiles(CorpusPaths.Root, "*.css", SearchOption.AllDirectories))
        {
            data.Add(path);
        }

        return data;
    }

    [Fact]
    public void ParseErrors_EmptyForWellFormedInput_ReportedForUnterminatedBlock()
    {
        // Act & Assert
        new CssInfo(Sample).ParseErrors.Should().BeEmpty();

        // Act & Assert
        new CssInfo("body { color: red").ParseErrors.Should().NotBeEmpty();
    }

    // =====================================================================
    //  Statement at-rules and comments in unknown at-rules (calibre 9.14, 5.44, 6.6.1)
    // =====================================================================

    [Theory]
    [InlineData("@charset \"utf-8\";\n")]
    [InlineData("@namespace epub \"http://www.idpf.org/2007/ops\";\n")]
    [InlineData("@import url(base.css);\n")]
    [InlineData("@charset \"utf-8\";\n@namespace svg \"http://www.w3.org/2000/svg\";\n")]
    public void Statement_at_rules_do_not_shift_the_selector_offsets(string prefix)
    {
        // Arrange
        string css = prefix + ".a { color: red }\n.b { color: blue }\n";

        // Act
        CssInfo info = new(css);

        // Assert
        info.GetAllSelectors().Select(s => (s.Text, s.Pos))
            .Should().Equal((".a", css.IndexOf(".a", StringComparison.Ordinal)), (".b", css.IndexOf(".b", StringComparison.Ordinal)));
        info.GetCssSelectorForElementClass("", "b")!.Pos.Should().Be(css.IndexOf(".b", StringComparison.Ordinal));
        info.Rules.Where(r => r.SelectorText.Length > 0).Select(r => r.Declarations.Single().Value)
            .Should().Equal("red", "blue");
    }

    [Theory]
    [InlineData("@unknown-rule { /* comment } */ x: 1; }\n")]
    [InlineData("@-webkit-keyframes spin { /* from { */ from { opacity: 0 } to { opacity: 1 } }\n")]
    [InlineData("@page :first { /* margin-top: 0 } */ margin: 1em }\n")]
    public void Comments_inside_at_rules_do_not_break_parsing_of_the_following_rules(string prefix)
    {
        // Arrange
        string css = prefix + ".a { color: red }\n";

        // Act
        CssInfo info = new(css);

        // Assert
        info.GetCssSelectorForElementClass("", "a")!.Pos.Should().Be(css.IndexOf(".a", StringComparison.Ordinal));
        info.Rules.Single(r => r.SelectorText == ".a").Declarations.Single().Value.Should().Be("red");
    }
}
