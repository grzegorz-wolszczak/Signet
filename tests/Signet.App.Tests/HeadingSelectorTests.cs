using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.App.Tests.TestSupport;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="HeadingSelectorViewModel"/> (the "Generate Table Of Contents" dialog).</summary>
public sealed class HeadingSelectorTests : IDisposable
{
    private readonly Book _book = BookCreator.CreateNewBook("3.0");

    public void Dispose() => _book.Dispose();

    private void SetBody(string body)
    {
        HtmlResource html = _book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + body + text[end..]);
    }

    private HeadingSelectorViewModel NewViewModel() => new(_book);

    // The nodes in document order.
    private static IEnumerable<HeadingNodeViewModel> Flatten(IEnumerable<HeadingNodeViewModel> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [Fact]
    public void Builds_a_node_tree_from_the_headings()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>A one</h2>\n<h1>B</h1>\n</body>");

        // Act
        HeadingSelectorViewModel vm = NewViewModel();

        // Assert
        vm.Nodes.Select(n => n.TitleText).Should().Equal("A", "B");
        vm.Nodes[0].Children.Should().ContainSingle().Which.TitleText.Should().Be("A one");
    }

    [Fact]
    public void Toc_items_only_hides_excluded_headings_and_promotes_their_children()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>A one</h2>\n<h1>B</h1>\n</body>");

        // Act
        HeadingSelectorViewModel vm = NewViewModel();

        vm.Nodes[0].IncludeInToc = false;

        // Assert
        vm.TocItemsOnly.Should().BeTrue();
        vm.Nodes.Select(n => n.TitleText).Should().Equal("A one", "B");
    }

    [Fact]
    public void Up_to_level_choice_marks_inclusion_and_resets_the_combo()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n<h3>C</h3>\n</body>");

        // Act
        HeadingSelectorViewModel vm = NewViewModel();

        // 0 = prompt, 1 = "None", 2 = "Up to level 1", 3 = "Up to level 2", 4 = "All"
        vm.SelectedLevelChoiceIndex = 2;

        // Assert
        vm.SelectedLevelChoiceIndex.Should().Be(0);

        // Act
        vm.TocItemsOnly = true;

        // Assert
        vm.Nodes.Select(n => n.TitleText).Should().Equal("A");
    }

    [Fact]
    public void Change_level_command_reparents_and_updates_selection()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        vm.SelectedNode = vm.Nodes[0].Children[0];

        // Act
        vm.DecreaseLevelCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.TitleText).Should().Equal("A", "B");
        vm.SelectedNode!.TitleText.Should().Be("B");
    }

    [Fact]
    public void Accept_applies_changes_and_raises_close()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");

        // Act
        HeadingSelectorViewModel vm = NewViewModel();
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.Nodes[0].Children[0].IncludeInToc = false;

        vm.AcceptCommand.Execute(null);

        // Assert
        vm.Accepted.Should().BeTrue();
        vm.BookChanged.Should().BeTrue();
        closed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, "h4", "h5", "h6")]
    [InlineData(true, "h5", "h6", "h6")]
    public async Task Raising_a_heading_whose_descendants_would_pass_h6_asks_first(
        bool confirm, string expectedA, string expectedB, string expectedC)
    {
        // Arrange
        SetBody("<body>\n<h4>A</h4>\n<h5>B</h5>\n<h6>C</h6>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        IReadOnlyList<Heading>? asked = null;
        vm.ConfirmHeadingLevelOverflowAsync = headings =>
        {
            asked = headings;
            return Task.FromResult(confirm);
        };
        vm.SelectedNode = vm.Nodes[0];

        // Act
        await vm.IncreaseLevelCommand.ExecuteAsync(null);

        // Assert
        asked.Should().ContainSingle().Which.Text.Should().Be("C");
        Flatten(vm.Nodes).Select(n => n.LevelLabel).Should().Equal(expectedA, expectedB, expectedC);
    }

    [Fact]
    public async Task Raising_a_heading_moves_its_descendants_unless_switched_off()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        vm.UpdateChildHeadingsLevels.Should().BeTrue("moving the descendants is the default");
        vm.UpdateChildHeadingsLevels = false;
        vm.SelectedNode = vm.Nodes[0];

        // Act
        await vm.IncreaseLevelCommand.ExecuteAsync(null);

        // Assert
        vm.Nodes.Select(n => n.LevelLabel).Should().Equal("h2", "h2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accept_with_changed_levels_writes_them_only_after_confirmation(bool confirm)
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HtmlResource html = _book.GetHtmlResourcesExcludingNav().First();
        string before = html.GetText();
        HeadingSelectorViewModel vm = NewViewModel();
        int asked = 0;
        vm.ConfirmHeadingLevelChangesAsync = () =>
        {
            asked++;
            return Task.FromResult(confirm);
        };
        vm.SelectedNode = vm.Nodes[0].Children[0];
        await vm.DecreaseLevelCommand.ExecuteAsync(null);

        // Act
        await vm.AcceptCommand.ExecuteAsync(null);

        // Assert
        asked.Should().Be(1);
        vm.Accepted.Should().Be(confirm);
        (html.GetText() == before).Should().Be(!confirm);
    }

    [Fact]
    public async Task Accept_without_changed_levels_does_not_ask()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        bool asked = false;
        vm.ConfirmHeadingLevelChangesAsync = () =>
        {
            asked = true;
            return Task.FromResult(true);
        };

        // Act
        await vm.AcceptCommand.ExecuteAsync(null);

        // Assert
        asked.Should().BeFalse();
        vm.Accepted.Should().BeTrue();
    }

    [Fact]
    public void Reveal_shows_a_heading_hidden_by_the_filter_and_a_collapsed_parent_and_selects_it()
    {
        // Arrange
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n<h3>C</h3>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        HeadingNodeViewModel b = vm.Nodes[0].Children[0];
        Heading c = b.Children[0].Heading;
        b.Children[0].IncludeInToc = false;
        vm.Nodes[0].IsExpanded = false;
        b.IsExpanded = false;
        HeadingNodeViewModel? revealed = null;
        vm.HeadingRevealed += (_, node) => revealed = node;

        // Act
        vm.RevealHeading(c);

        // Assert
        vm.TocItemsOnly.Should().BeFalse("the heading is left out of the TOC and the filter hid it");
        vm.SelectedNode!.Heading.Should().BeSameAs(c);
        revealed.Should().BeSameAs(vm.SelectedNode);
        vm.Nodes[0].IsExpanded.Should().BeTrue();
        vm.Nodes[0].Children[0].IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void An_h6_list_entry_names_the_file_and_the_line_of_the_heading()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        SetBody("<body>\n<h1>A</h1>\n<p>x</p>\n<h2>B</h2>\n</body>");
        HtmlResource html = _book.GetHtmlResourcesExcludingNav().First();
        Heading b = Headings.GetHeadingList(_book.GetHtmlResourcesExcludingNav()).Single(h => h.Text == "B");
        int line = html.GetText()[..html.GetText().IndexOf("<h2>", StringComparison.Ordinal)].Count(ch => ch == '\n') + 1;

        // Act
        HeadingOverflowEntry entry = HeadingOverflowEntry.For(b);

        // Assert
        entry.Title.Should().Be("B");
        entry.Location.Should().Be($"{html.BookPath}, line {line}");
    }
}
