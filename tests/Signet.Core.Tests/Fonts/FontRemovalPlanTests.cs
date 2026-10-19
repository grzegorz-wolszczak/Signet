using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="FontRemovalPlan"/> — what deleting font files does to the stylesheets.</summary>
public sealed class FontRemovalPlanTests : EnglishUiCultureTest
{
    private const string CssPath = "OEBPS/Styles/style.css";
    private const string HtmlPath = "OEBPS/Text/ch1.xhtml";
    private const string RegularPath = "OEBPS/Fonts/K-Regular.ttf";
    private const string BoldPath = "OEBPS/Fonts/K-Bold.ttf";

    private const string Faces =
        "@font-face {\n  font-family: \"KCaecilia\";\n  font-weight: normal;\n  src: url(\"../Fonts/K-Regular.ttf\");\n}\n\n"
        + "@font-face {\n  font-family: \"KCaecilia\";\n  font-weight: bold;\n  src: url(\"../Fonts/K-Bold.ttf\");\n}\n\n";

    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();

    private static FontRemovalPlan Plan(string css, string? html, params string[] fonts)
    {
        List<FontIndexSource> sources = new() { new FontIndexSource(CssPath, css, true) };
        if (html is not null)
        {
            sources.Add(new FontIndexSource(HtmlPath, html, false));
        }

        return FontRemovalPlan.Create(BookFontIndex.Build(sources), fonts);
    }

    [Fact]
    public void Removing_every_face_removes_the_rules_and_the_family_from_its_lists()
    {
        // Arrange
        const string css = Faces + "body {\n  font-family: \"KCaecilia\", serif;\n}\n\nh1 {\n  color: red;\n  font-family: KCaecilia;\n}\n";

        // Act
        FontRemovalPlan plan = Plan(css, null, RegularPath, BoldPath);

        // Assert
        plan.RuleItems.Select(i => i.Kind).Should().Equal(FontRemovalItemKind.RemoveRule, FontRemovalItemKind.RemoveRule);
        plan.UsageItems.Select(i => i.Kind).Should().Equal(FontRemovalItemKind.RemoveFamily, FontRemovalItemKind.RemoveDeclaration);
        plan.Notes.Should().BeEmpty();
        plan.BuildNewTexts(NoExclusions)[CssPath].Should().Be("body {\n  font-family: serif;\n}\n\nh1 {\n  color: red;\n}\n");
    }

    [Fact]
    public void Excluded_items_are_left_alone()
    {
        // Arrange
        const string css = Faces + "body { font-family: \"KCaecilia\", serif; }\n";

        // Act
        FontRemovalPlan plan = Plan(css, null, RegularPath, BoldPath);

        IReadOnlyDictionary<string, string> texts = plan.BuildNewTexts(new HashSet<string> { plan.UsageItems[0].Key });

        // Assert
        texts[CssPath].Should().Be("body { font-family: \"KCaecilia\", serif; }\n");
    }

    [Fact]
    public void Removing_one_face_keeps_the_usages_and_notes_the_lost_face()
    {
        // Arrange
        const string css = Faces + "body { font-family: \"KCaecilia\", serif; }\n";

        // Act
        FontRemovalPlan plan = Plan(css, null, BoldPath);

        // Assert
        plan.RuleItems.Should().ContainSingle().Which.Kind.Should().Be(FontRemovalItemKind.RemoveRule);
        plan.UsageItems.Should().BeEmpty();
        FontRemovalItem note = plan.Notes.Should().ContainSingle().Subject;
        note.Kind.Should().Be(FontRemovalItemKind.FamilyLosesFaces);
        note.Text.Should().Contain("bold").And.Contain("normal");
        plan.BuildNewTexts(NoExclusions)[CssPath].Should().Be(
            "@font-face {\n  font-family: \"KCaecilia\";\n  font-weight: normal;\n  src: url(\"../Fonts/K-Regular.ttf\");\n}\n\n"
            + "body { font-family: \"KCaecilia\", serif; }\n");
    }

    [Fact]
    public void A_rule_with_other_sources_only_loses_the_deleted_url()
    {
        // Arrange
        const string css = "@font-face { font-family: K; src: url(../Fonts/K-Regular.woff2) format(\"woff2\"), url(../Fonts/K-Regular.ttf); }";

        // Act
        FontRemovalPlan plan = Plan(css, null, "OEBPS/Fonts/K-Regular.woff2");

        // Assert
        plan.RuleItems.Should().ContainSingle().Which.Kind.Should().Be(FontRemovalItemKind.RemoveSources);
        plan.UsageItems.Should().BeEmpty();
        plan.BuildNewTexts(NoExclusions)[CssPath].Should().Be("@font-face { font-family: K; src: url(../Fonts/K-Regular.ttf); }");
    }

    [Fact]
    public void A_font_shorthand_naming_only_the_family_is_a_note_to_fix_by_hand()
    {
        // Arrange
        const string css = Faces + "h1 { font: bold 2em KCaecilia }\nh2 { font: 1em KCaecilia, serif }\n";

        // Act
        FontRemovalPlan plan = Plan(css, null, RegularPath, BoldPath);

        // Assert
        plan.UsageItems.Select(i => (i.Kind, i.IsActionable)).Should().Equal(
            (FontRemovalItemKind.ManualFix, false), (FontRemovalItemKind.RemoveFamily, true));
        plan.BuildNewTexts(NoExclusions)[CssPath].Should().Be("h1 { font: bold 2em KCaecilia }\nh2 { font: 1em serif }\n");
    }

    [Fact]
    public void A_style_attribute_left_empty_is_removed()
    {
        // Arrange
        const string html = "<p style=\"font-family: &quot;KCaecilia&quot;\">a</p><p style=\"color: red; font-family: KCaecilia;\">b</p>";

        // Act
        FontRemovalPlan plan = Plan(Faces, html, RegularPath, BoldPath);

        // Assert
        plan.UsageItems.Should().HaveCount(2).And.OnlyContain(i => i.Kind == FontRemovalItemKind.RemoveDeclaration);
        plan.BuildNewTexts(NoExclusions)[HtmlPath].Should().Be("<p>a</p><p style=\"color: red; \">b</p>");
    }

    [Fact]
    public void Fonts_no_stylesheet_mentions_give_an_empty_plan()
    {
        // Act & Assert
        Plan(Faces, null, "OEBPS/Fonts/Other.ttf").IsEmpty.Should().BeTrue();
    }
}
