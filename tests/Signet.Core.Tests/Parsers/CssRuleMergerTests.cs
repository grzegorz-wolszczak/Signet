using System.Linq;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests for <see cref="CssRuleMerger"/> — merging identical selectors/properties.</summary>
public sealed class CssRuleMergerTests
{
    [Fact]
    public void FindMergeGroups_GroupsRulesWithSameSelectorText()
    {
        // Arrange
        CssInfo info = new(".foo { color: red }\nh1 { font-weight: bold }\n.foo { margin: 0 }\n");

        // Act
        var groups = CssRuleMerger.FindMergeGroups(info);

        // Assert
        groups.Should().ContainSingle();
        groups[0].Kind.Should().Be(CssMergeKind.SameSelector);
        groups[0].Rules.Should().HaveCount(2);
        groups[0].Rules.Select(r => r.SelectorText).Should().AllBeEquivalentTo(".foo");
    }

    [Fact]
    public void FindMergeGroups_GroupsRulesWithIdenticalDeclarationsButDifferentSelectors()
    {
        // Arrange
        CssInfo info = new(".a { color: red; margin: 0 }\n.b { color: red; margin: 0 }\n.c { color: blue }\n");

        // Act
        var groups = CssRuleMerger.FindMergeGroups(info);

        // Assert
        groups.Should().ContainSingle();
        groups[0].Kind.Should().Be(CssMergeKind.SameProperties);
        groups[0].Rules.Select(r => r.SelectorText).Should().Equal(".a", ".b");
    }

    [Fact]
    public void FindMergeGroups_IgnoresAtRuleBlocks()
    {
        // Arrange
        CssInfo info = new("@font-face { font-family: X; src: url(x.ttf) }\n@font-face { font-family: X; src: url(x.ttf) }\n");

        // Act
        var groups = CssRuleMerger.FindMergeGroups(info);

        // Assert
        groups.Should().BeEmpty();
    }

    [Fact]
    public void FindMergeGroups_WithNoDuplicates_ReturnsEmpty()
    {
        // Arrange
        CssInfo info = new(".a { color: red }\n.b { color: blue }\n");

        // Act
        var groups = CssRuleMerger.FindMergeGroups(info);

        // Assert
        groups.Should().BeEmpty();
    }

    [Fact]
    public void ApplyMerges_SameSelector_CombinesDeclarationsIntoOneRuleAndRemovesOthers()
    {
        // Arrange
        string css = ".foo { color: red }\nh1 { font-weight: bold }\n.foo { margin: 0 }\n";
        CssInfo info = new(css);

        // Act
        var group = CssRuleMerger.FindMergeGroups(info).Single();

        string? result = CssRuleMerger.ApplyMerges(css, new[] { group });

        // Assert
        result.Should().NotBeNull();
        CssInfo merged = new(result!);
        merged.Rules.Count(r => r.SelectorText == ".foo").Should().Be(1);
        merged.Rules.Should().ContainSingle(r => r.SelectorText == "h1");
        result.Should().Contain("color: red").And.Contain("margin: 0");
    }

    [Fact]
    public void ApplyMerges_SameProperties_CombinesSelectorsIntoOneRuleAndRemovesOthers()
    {
        // Arrange
        string css = ".a { color: red; margin: 0 }\n.b { color: red; margin: 0 }\n.c { color: blue }\n";
        CssInfo info = new(css);

        // Act
        var group = CssRuleMerger.FindMergeGroups(info).Single();

        string? result = CssRuleMerger.ApplyMerges(css, new[] { group });

        // Assert
        result.Should().NotBeNull();
        CssInfo merged = new(result!);
        merged.Rules.Should().ContainSingle(r => r.SelectorText.Contains(".a", System.StringComparison.Ordinal) && r.SelectorText.Contains(".b", System.StringComparison.Ordinal));
        merged.Rules.Should().ContainSingle(r => r.SelectorText == ".c");
    }

    [Fact]
    public void ApplyMerges_WithGroupOfOne_IsIgnored()
    {
        // Arrange
        string css = ".a { color: red }\n";
        CssInfo info = new(css);
        CssMergeGroup group = new(CssMergeKind.SameSelector, new[] { info.Rules[0] });

        // Act
        string? result = CssRuleMerger.ApplyMerges(css, new[] { group });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ApplyMerges_WithEmptyGroups_ReturnsNull()
    {
        // Arrange
        string css = ".a { color: red }\n";

        // Act
        string? result = CssRuleMerger.ApplyMerges(css, System.Array.Empty<CssMergeGroup>());

        // Assert
        result.Should().BeNull();
    }
}
