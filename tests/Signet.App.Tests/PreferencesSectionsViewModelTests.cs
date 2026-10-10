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
        [1] = new[] { "Font size", "Code View colors" },
        [2] = new[] { "Highlight misspelled words" },
        [3] = new[] { "Tab width" },
    };

    // Appearance (1) with the subsection Code View (3); Language (0); Spelling (2).
    private static PreferencesSectionsViewModel New(out PreferencesSectionNode codeView)
    {
        PreferencesSectionNode language = new("Language", 0);
        PreferencesSectionNode appearance = new("Appearance", 1);
        codeView = new PreferencesSectionNode("Code View", 3, appearance);
        PreferencesSectionNode spelling = new("Spelling", 2);
        return new PreferencesSectionsViewModel(new[] { language, appearance, spelling }, i => Texts[i]);
    }

    [Fact]
    public void Sections_know_their_level_and_path_and_the_first_one_is_selected()
    {
        PreferencesSectionsViewModel sut = New(out PreferencesSectionNode codeView);

        sut.Selected!.Title.Should().Be("Language");
        codeView.IsTopLevel.Should().BeFalse();
        codeView.Parent!.IsTopLevel.Should().BeTrue();
        codeView.Parent.HasChildren.Should().BeTrue();
        codeView.Path.Should().Be("Appearance › Code View");
        sut.All.Select(n => n.Title).Should().Equal("Language", "Appearance", "Code View", "Spelling");
        sut.FindByPage(3).Should().BeSameAs(codeView);

        sut.Selected = codeView;
        sut.Breadcrumb.Should().Be("Appearance › Code View");
    }

    [Fact]
    public void The_search_keeps_sections_matching_by_name_or_page_text_with_their_parents_expanded()
    {
        PreferencesSectionsViewModel sut = New(out PreferencesSectionNode codeView);
        codeView.Parent!.IsExpanded = false;

        sut.Query = "tab";

        sut.All.Where(n => n.IsVisible).Select(n => n.Title).Should().Equal("Appearance", "Code View");
        codeView.Parent.IsExpanded.Should().BeTrue("the parent of a match is opened");
        sut.Selected.Should().BeSameAs(codeView, "the selected section was filtered out, so the first match is shown");

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
