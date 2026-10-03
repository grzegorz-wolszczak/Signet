using AwesomeAssertions;
using Signet.Core.Semantics;
using Xunit;

namespace Signet.Core.Tests.Semantics;

/// <summary>
/// Tests for <see cref="AriaClipBuilder"/> — building the selected clip for the
/// Insert → "Aria Clip..." menu.
/// </summary>
public sealed class AriaClipBuilderTests
{
    [Theory]
    [InlineData("section")]
    [InlineData("aside")]
    public void RequiresRoleSelection_is_true_only_for_section_and_aside(string code) =>
        AriaClipBuilder.RequiresRoleSelection(code).Should().BeTrue();

    [Theory]
    [InlineData("chapter")]
    [InlineData("fn_ref")]
    public void RequiresRoleSelection_is_false_for_other_codes(string code) =>
        AriaClipBuilder.RequiresRoleSelection(code).Should().BeFalse();

    [Fact]
    public void BuildClip_for_update_only_number_code_substitutes_the_leading_number_but_leaves_backreferences()
    {
        // "fn_ref" -> UPDATE_ONLY_NUMBER: _N_ is substituted, but the clip does not contain \1.
        string clip = AriaClipBuilder.BuildClip("fn_ref", "3. footnote text", roleCode: null, bookLang: "en");

        clip.Should().Contain("[3]");
        clip.Should().NotContain("_N_");
    }

    [Fact]
    public void BuildClip_for_update_number_and_fill_code_substitutes_both_the_number_and_the_selection()
    {
        // "fn_aside" -> UPDATE_NUMBER_AND_FILL: both _N_ and \1 are substituted.
        string clip = AriaClipBuilder.BuildClip("fn_aside", "[12] the footnote body", roleCode: null, bookLang: "en");

        clip.Should().NotContain("_N_");
        clip.Should().NotContain("\\1");
        clip.Should().Contain("12");
        clip.Should().Contain("the footnote body");
    }

    [Fact]
    public void BuildClip_without_a_leading_number_leaves_the_placeholders_untouched()
    {
        string clip = AriaClipBuilder.BuildClip("fn_ref", "no digits here", roleCode: null, bookLang: "en");

        clip.Should().Contain("_N_");
    }

    [Fact]
    public void BuildClip_for_a_plain_template_leaves_the_selection_placeholder_for_PasteClipText()
    {
        // "aside" is in neither UPDATE_ONLY_NUMBER nor UPDATE_NUMBER_AND_FILL - \1 is left untouched;
        // CodeInsertOperations.PasteClipText substitutes it later (as for a regular user clip).
        string clip = AriaClipBuilder.BuildClip("aside", "selected text", roleCode: null, bookLang: "en");

        clip.Should().Contain("\\1");
    }

    [Fact]
    public void BuildClip_injects_epub_type_and_role_before_the_first_closing_bracket()
    {
        string clip = AriaClipBuilder.BuildClip("section", string.Empty, roleCode: "doc-chapter", bookLang: "en");

        clip.Should().StartWith("<section epub:type=\"chapter\" role=\"doc-chapter\">");
    }

    [Fact]
    public void BuildClip_for_an_epub_type_only_role_omits_the_role_attribute()
    {
        // "biblioentry": AriaRoles.EpubTypeMapping returns the same code as the role -> epub:type only.
        string clip = AriaClipBuilder.BuildClip("aside", string.Empty, roleCode: "biblioentry", bookLang: "en");

        clip.Should().Contain("epub:type=\"biblioentry\"");
        clip.Should().NotContain("role=\"biblioentry\"");
    }

    [Fact]
    public void BuildClip_without_a_role_selection_leaves_the_template_unmodified()
    {
        string withRole = AriaClipBuilder.BuildClip("section", string.Empty, roleCode: null, bookLang: "en");

        withRole.Should().Be(AriaClips.TranslatePlaceholders(AriaClips.GetDescriptionByCode("section"), "en"));
    }

    [Fact]
    public void ClipOptions_labels_include_the_code_and_the_description_is_translated()
    {
        var options = AriaClipBuilder.ClipOptions("en");

        options.Should().ContainKey("chapter");
        options["chapter"].Name.Should().Be(AriaClips.GetName("chapter") + " (chapter)");
        options["chapter"].Description.Should().NotContain("LABEL_FOR_");
    }

    [Fact]
    public void RoleOptions_filters_by_allowed_tags_for_the_given_code()
    {
        var forSection = AriaClipBuilder.RoleOptions("section");

        forSection.Should().ContainKey("doc-chapter");
        forSection.Values.Should().Contain(v => v.Name.Contains("(doc-chapter)"));
    }
}
