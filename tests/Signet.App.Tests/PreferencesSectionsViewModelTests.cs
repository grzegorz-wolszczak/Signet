using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of <see cref="PreferencesSectionsViewModel"/> — the Preferences section tree and its search.</summary>
public sealed class PreferencesSectionsViewModelTests
{
    private static readonly Dictionary<int, string[]> Texts = new()
    {
        [0] = new[] { "Interface language" },
        [1] = new[] { "Font size", "Code Editor colors" },
        [2] = new[] { "Highlight misspelled words" },
        [3] = new[] { "Tab width" },
    };

    // Appearance (1) with the subsection Code Editor (3); Language (0); Spelling (2).
    private static PreferencesSectionsViewModel New(out PreferencesSectionNode codeEditor)
    {
        PreferencesSectionNode language = new("Language", 0);
        PreferencesSectionNode appearance = new("Appearance", 1);
        codeEditor = new PreferencesSectionNode("Code Editor", 3, appearance);
        PreferencesSectionNode spelling = new("Spelling", 2);
        return new PreferencesSectionsViewModel(new[] { language, appearance, spelling }, i => Texts[i]);
    }

    [Fact]
    public void Sections_know_their_level_and_path_and_the_first_one_is_selected()
    {
        PreferencesSectionsViewModel sut = New(out PreferencesSectionNode codeEditor);

        sut.Selected!.Title.Should().Be("Language");
        codeEditor.IsTopLevel.Should().BeFalse();
        codeEditor.Parent!.IsTopLevel.Should().BeTrue();
        codeEditor.Parent.HasChildren.Should().BeTrue();
        codeEditor.Path.Should().Be("Appearance › Code Editor");
        sut.All.Select(n => n.Title).Should().Equal("Language", "Appearance", "Code Editor", "Spelling");
        sut.FindByPage(3).Should().BeSameAs(codeEditor);

        sut.Selected = codeEditor;
        sut.Breadcrumb.Should().Be("Appearance › Code Editor");
    }

    [Fact]
    public void The_search_keeps_sections_matching_by_name_or_page_text_with_their_parents_expanded()
    {
        PreferencesSectionsViewModel sut = New(out PreferencesSectionNode codeEditor);
        codeEditor.Parent!.IsExpanded = false;

        sut.Query = "tab";

        sut.All.Where(n => n.IsVisible).Select(n => n.Title).Should().Equal("Appearance", "Code Editor");
        codeEditor.Parent.IsExpanded.Should().BeTrue("the parent of a match is opened");
        sut.Selected.Should().BeSameAs(codeEditor, "the selected section was filtered out, so the first match is shown");

        sut.Query = "LANGUAGE";
        sut.All.Where(n => n.IsVisible).Select(n => n.Title).Should().Equal("Language");

        sut.Query = "  ";
        sut.All.Should().OnlyContain(n => n.IsVisible);
        sut.IsSearching.Should().BeFalse();
    }

    [Fact]
    public void Matches_compares_case_insensitively_and_only_while_searching()
    {
        PreferencesSectionsViewModel sut = New(out _);
        sut.Matches("Font size").Should().BeFalse("there is no search");

        sut.Query = " FONT ";

        sut.Matches("Font size").Should().BeTrue();
        sut.Matches("Spelling").Should().BeFalse();
        sut.Matches(null).Should().BeFalse();
    }
}
