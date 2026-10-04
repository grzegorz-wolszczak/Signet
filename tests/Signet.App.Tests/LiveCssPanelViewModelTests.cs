using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="LiveCssPanelViewModel"/> — the element hierarchy of the Live CSS panel and the
/// "overridden by" annotations.
/// </summary>
public sealed class LiveCssPanelViewModelTests
{
    private const string CssPath = "EPUB/styles/style.css";
    private const string CssText = ".a { color: red; }\n.b { color: blue; }";

    private static CssCascadeResult HierarchyResult()
    {
        CssMatchedRule ruleB = new(CssPath, CssText.IndexOf(".b", System.StringComparison.Ordinal), ".b",
            new CssSpecificity(0, 0, 1, 0), false,
            new[] { new CssMatchedDeclaration("color", "blue", false, false) });
        CssMatchedRule ruleA = new(CssPath, 0, ".a", new CssSpecificity(0, 0, 1, 0), false,
            new[] { new CssMatchedDeclaration("color", "red", false, true, AffectsElement: false, OverriddenBy: ruleB.Source) });

        return new CssCascadeResult(
            "EPUB/text/chapter1.xhtml",
            0,
            "p.b",
            new[]
            {
                new CssCascadeLevel("div.a", 0, false, new[] { ruleA }),
                new CssCascadeLevel("p.b", 10, true, new[] { ruleB }),
            });
    }

    [Fact]
    public void Update_builds_one_level_per_hierarchy_element_in_order()
    {
        LiveCssPanelViewModel vm = new();

        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        vm.ElementDescription.Should().Be("p.b");
        vm.Levels.Should().HaveCount(2);
        vm.Levels[0].IsInspectedElement.Should().BeFalse();
        vm.Levels[0].Header.Should().Be("div.a");
        vm.Levels[1].IsInspectedElement.Should().BeTrue();
        vm.Levels[1].Header.Should().StartWith("p.b");
        vm.Levels.Should().OnlyContain(l => !l.ShowNoRules);
    }

    [Fact]
    public void Update_annotates_an_overridden_declaration_with_the_winning_selector_and_its_file_line()
    {
        LiveCssPanelViewModel vm = new();

        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        LiveCssDeclarationViewModel overridden = vm.Levels[0].Rules.Single().Declarations.Single();
        LiveCssDeclarationViewModel winner = vm.Levels[1].Rules.Single().Declarations.Single();

        overridden.IsOverridden.Should().BeTrue();
        overridden.IsDimmed.Should().BeTrue();
        overridden.HasOverriddenBy.Should().BeTrue();
        overridden.OverriddenByText.Should().Contain(".b, style.css:2");
        winner.IsOverridden.Should().BeFalse();
        winner.IsDimmed.Should().BeFalse();
        winner.HasOverriddenBy.Should().BeFalse();
    }
}
