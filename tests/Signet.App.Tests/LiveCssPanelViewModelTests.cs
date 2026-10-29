using System;
using System.Collections.Generic;
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

    [Fact]
    public void Update_explains_when_the_same_selector_matches_a_closer_element()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        CssDeclarationSource source = new(CssPath, 0, "*", false);
        CssMatchedRule bodyRule = new(CssPath, 0, "*", new CssSpecificity(0, 0, 0, 0), false,
            new[]
            {
                new CssMatchedDeclaration(
                    "font-family",
                    "\"Old Font\"",
                    false,
                    true,
                    AffectsElement: false,
                    OverriddenBy: source,
                    OverriddenByElementDescription: "p.paragraph"),
                new CssMatchedDeclaration(
                    "font-family",
                    "\"New Font\"",
                    false,
                    false,
                    AffectsElement: false,
                    OverriddenBy: source,
                    OverriddenByElementDescription: "p.paragraph",
                    IsRepeated: true),
            });
        CssMatchedRule paragraphRule = new(CssPath, 0, "*", new CssSpecificity(0, 0, 0, 0), false,
            new[]
            {
                new CssMatchedDeclaration(
                    "font-family",
                    "\"Old Font\"",
                    false,
                    true,
                    AffectsElement: false,
                    OverriddenBy: source),
                new CssMatchedDeclaration("font-family", "\"New Font\"", false, false),
            });
        CssCascadeResult result = new(
            "EPUB/text/chapter1.xhtml",
            0,
            "p.paragraph",
            new[]
            {
                new CssCascadeLevel("body.body01", 0, false, new[] { bodyRule }),
                new CssCascadeLevel("p.paragraph", 10, true, new[] { paragraphRule }),
            });
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(result, _ => CssText, (_, _) => { });
        LiveCssDeclarationViewModel overridden = vm.Levels[0].Rules.Single().Declarations.First();

        // Assert
        overridden.OverriddenByText.Should().Be("(the same selector also matches p.paragraph)");
    }

    [Fact]
    public void Update_marks_a_same_value_in_an_ancestor_as_repeated_not_overridden()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        const string cssText = ".body01 { text-align: justify; }\n.paragraph { text-align: justify; }";
        CssDeclarationSource childSource = new(
            CssPath,
            cssText.IndexOf(".paragraph", StringComparison.Ordinal),
            ".paragraph",
            false);
        CssMatchedRule bodyRule = new(CssPath, 0, ".body01", new CssSpecificity(0, 0, 1, 0), false,
            new[]
            {
                new CssMatchedDeclaration(
                    "text-align",
                    "justify",
                    false,
                    false,
                    AffectsElement: false,
                    OverriddenBy: childSource,
                    OverriddenByElementDescription: "p.paragraph",
                    IsRepeated: true),
            });
        CssMatchedRule paragraphRule = new(
            CssPath,
            childSource.Offset,
            ".paragraph",
            new CssSpecificity(0, 0, 1, 0),
            false,
            new[] { new CssMatchedDeclaration("text-align", "justify", false, false) });
        CssCascadeResult result = new(
            "EPUB/text/chapter1.xhtml",
            0,
            "p.paragraph",
            new[]
            {
                new CssCascadeLevel("body.body01", 0, false, new[] { bodyRule }),
                new CssCascadeLevel("p.paragraph", 10, true, new[] { paragraphRule }),
            });
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(result, _ => cssText, (_, _) => { });
        LiveCssDeclarationViewModel repeated = vm.Levels[0].Rules.Single().Declarations.Single();

        // Assert
        repeated.IsOverridden.Should().BeFalse();
        repeated.IsRepeated.Should().BeTrue();
        repeated.IsDimmed.Should().BeTrue();
        repeated.OverriddenByText.Should().Contain("repeated by .paragraph");
    }

    [Fact]
    public void Update_explains_when_the_same_rule_repeats_a_value_on_a_closer_element()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        CssDeclarationSource source = new(CssPath, 0, "*", false);
        CssMatchedRule bodyRule = new(CssPath, 0, "*", new CssSpecificity(0, 0, 0, 0), false,
            new[]
            {
                new CssMatchedDeclaration(
                    "font-family",
                    "\"KCaecilia Condensed\"",
                    false,
                    false,
                    AffectsElement: false,
                    OverriddenBy: source,
                    OverriddenByElementDescription: "p.paragraph",
                    IsRepeated: true),
            });
        CssMatchedRule paragraphRule = new(CssPath, 0, "*", new CssSpecificity(0, 0, 0, 0), false,
            new[] { new CssMatchedDeclaration("font-family", "\"KCaecilia Condensed\"", false, false) });
        CssCascadeResult result = new(
            "EPUB/text/chapter1.xhtml",
            0,
            "p.paragraph",
            new[]
            {
                new CssCascadeLevel("body.body01", 0, false, new[] { bodyRule }),
                new CssCascadeLevel("p.paragraph", 10, true, new[] { paragraphRule }),
            });
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(result, _ => CssText, (_, _) => { });
        LiveCssDeclarationViewModel repeated = vm.Levels[0].Rules.Single().Declarations.Single();

        // Assert
        repeated.OverriddenByText.Should().Be("(the same rule repeats this value on p.paragraph)");
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

    /// <summary>
    /// The annotations say where the value really comes from, so they take you there: the one of an overridden
    /// declaration to the rule that wins, the one of an inherited property to the rule of the ancestor.
    /// </summary>
    [Fact]
    public void The_annotations_jump_to_the_rule_they_name()
    {
        // Arrange
        List<(string BookPath, int Offset)> jumps = new();
        LiveCssPanelViewModel vm = new();
        vm.Update(ResultWithInherited(), _ => CssText, (path, offset) => jumps.Add((path, offset)));

        // Act
        LiveCssDeclarationViewModel overridden = vm.Levels[0].Rules.Single().Declarations.Single();
        overridden.JumpCommand!.Execute(null);

        // Assert
        jumps.Should().Equal((CssPath, CssText.IndexOf(".b", StringComparison.Ordinal)));

        // Act
        vm.Inherited.Single().JumpCommand.Execute(null);

        // Assert
        jumps.Should().HaveCount(2);
        jumps[1].Should().Be((CssPath, 0));
    }

    [Fact]
    public void A_declaration_that_is_not_overridden_has_nowhere_to_jump()
    {
        // Arrange
        LiveCssPanelViewModel vm = new();

        // Act
        vm.Update(HierarchyResult(), _ => CssText, (_, _) => { });

        // Assert
        vm.Levels[1].Rules.Single().Declarations.Single().JumpCommand.Should().BeNull();
    }
}
