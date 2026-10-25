using System.Linq;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Parsers;
using Signet.Core.Tests.TestSupport;
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
        // Arrange
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
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
        // Arrange
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        LiveCssDeclarationViewModel overridden = vm.Levels[0].Rules.Single().Declarations.Single();
        LiveCssDeclarationViewModel winner = vm.Levels[1].Rules.Single().Declarations.Single();

        // Assert
        overridden.IsOverridden.Should().BeTrue();
        overridden.IsDimmed.Should().BeTrue();
        overridden.HasOverriddenBy.Should().BeTrue();
        overridden.OverriddenByText.Should().Contain(".b, style.css:2");
        winner.IsOverridden.Should().BeFalse();
        winner.IsDimmed.Should().BeFalse();
        winner.HasOverriddenBy.Should().BeFalse();
    }

    private static CssCascadeResult ResultWithInherited() =>
        HierarchyResult() with
        {
            Inherited = new[]
            {
                new CssInheritedDeclaration("color", "red", false, "div.a", new CssDeclarationSource(CssPath, 0, ".a", false)),
            },
        };

    [Fact]
    public void Levels_above_the_inspected_element_can_be_collapsed_and_the_last_one_cannot()
    {
        // Arrange
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
        vm.Levels[0].CanCollapse.Should().BeTrue();
        vm.Levels[0].IsExpanded.Should().BeTrue("levels open by default");
        vm.Levels[0].ShowSeparator.Should().BeFalse("the first level needs no line above it");
        vm.Levels[1].CanCollapse.Should().BeFalse("the element under the caret is what the panel is about");
        vm.Levels[1].ShowSeparator.Should().BeTrue();
    }

    [Fact]
    public void A_collapsed_level_stays_collapsed_when_the_panel_is_rebuilt()
    {
        // Arrange
        LiveCssPanelViewModel vm = new();
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Act \u2014 the caret moves and the panel is computed again for the same hierarchy.
        vm.Levels[0].IsExpanded = false;
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
        vm.Levels[0].IsExpanded.Should().BeFalse();
        vm.Levels[0].ExpandGlyph.Should().Be("\u25b8");

        // Act
        vm.Levels[0].IsExpanded = true;
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
        vm.Levels[0].IsExpanded.Should().BeTrue();
        vm.Levels[0].ExpandGlyph.Should().Be("\u25be");
    }

    [Fact]
    public void The_inherited_properties_are_listed_with_the_ancestor_they_come_from()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(ResultWithInherited(), _ => CssText, (_, _) => { });

        // Assert
        vm.HasInherited.Should().BeTrue();
        LiveCssInheritedViewModel inherited = vm.Inherited.Should().ContainSingle().Subject;
        inherited.Text.Should().Be("color: red;");
        inherited.SourceText.Should().Be("(from div.a \u2014 style.css:1)");
    }

    [Fact]
    public void An_update_without_inherited_properties_empties_the_list()
    {
        // Arrange
        LiveCssPanelViewModel vm = new();
        vm.Update(ResultWithInherited(), _ => CssText, (_, _) => { });

        // Act
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
        vm.Inherited.Should().BeEmpty();
        vm.HasInherited.Should().BeFalse();
    }

    [Fact]
    public void Show_inherited_is_kept_in_the_settings()
    {
        // Arrange
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.xml"));
        LiveCssPanelViewModel vm = new(settings);

        // Assert
        vm.ShowInherited.Should().BeFalse("the box starts unchecked");

        // Act
        vm.ShowInherited = true;

        // Assert
        settings.LiveCssShowInherited.Should().BeTrue();
        new LiveCssPanelViewModel(new SettingsStore(temp.Combine("settings.xml"))).ShowInherited
            .Should().BeTrue("a panel opened later starts the way it was left");
    }

    [Fact]
    public void A_rule_header_is_split_into_the_selector_and_the_rest()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
        LiveCssRuleViewModel rule = vm.Levels[0].Rules.Single();
        rule.SelectorText.Should().Be(".a");
        rule.HeaderSuffix.Should().StartWith("  \u2014  style.css:1");
        (rule.SelectorText + rule.HeaderSuffix).Should().Be(rule.Header);
    }
}
