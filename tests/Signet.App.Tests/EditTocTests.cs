using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="EditTocViewModel"/> (the "Edit Table Of Contents" dialog).</summary>
public sealed class EditTocTests : IDisposable
{
    private const string Body =
        "<body>\n" +
        "  <h1>Alpha</h1>\n" +
        "  <h2>Alpha One</h2>\n" +
        "  <h1>Beta</h1>\n" +
        "  <h1>Gamma</h1>\n" +
        "</body>";

    private readonly Book _book = BookCreator.CreateNewBook("3.0");

    public EditTocTests()
    {
        HtmlResource html = _book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + Body + text[end..]);

        new HeadingSelectorModel(_book).Apply();
        TocGenerator.GenerateToc(_book);
    }

    public void Dispose() => _book.Dispose();

    private EditTocViewModel NewViewModel() => new(_book);

    private static EditTocNodeViewModel Find(EditTocViewModel vm, string text) =>
        Flatten(vm).First(n => n.Text == text);

    private static System.Collections.Generic.IEnumerable<EditTocNodeViewModel> Flatten(EditTocViewModel vm) =>
        vm.Nodes.SelectMany(Flatten);

    private static System.Collections.Generic.IEnumerable<EditTocNodeViewModel> Flatten(EditTocNodeViewModel node)
    {
        yield return node;
        foreach (EditTocNodeViewModel child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    [Fact]
    public void Builds_a_node_tree_from_the_book_toc()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();

        // Act & Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta", "Gamma");
        vm.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Alpha One");
        vm.Nodes[0].Children[0].Target.Should().NotBeEmpty();
    }

    [Fact]
    public void Expand_and_collapse_all_changes_every_nested_branch()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        EditTocNodeViewModel alphaOne = vm.Nodes[0].Children[0];
        alphaOne.Children.Add(new EditTocNodeViewModel { Parent = alphaOne });
        EditTocNodeViewModel[] expandable = Flatten(vm).Where(node => node.Children.Count > 0).ToArray();

        // Act
        vm.CollapseAllCommand.Execute(null);

        // Assert
        expandable.Should().HaveCount(2, "the test tree includes a grandchild");
        expandable.Should().OnlyContain(node => !node.IsExpanded);

        // Act
        vm.ExpandAllCommand.Execute(null);

        // Assert
        expandable.Should().OnlyContain(node => node.IsExpanded);
    }

    [Fact]
    public void Sort_puts_the_entries_of_every_level_back_in_book_order()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();

        // Act
        vm.SetSelectedNodes(new[] { Find(vm, "Gamma") });
        vm.MoveUpCommand.Execute(null);
        vm.MoveUpCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Gamma", "Alpha", "Beta");

        // Act
        vm.SortByBookOrderCommand.Execute(null);

        // Assert
        vm.Rows.Select(r => (r.Text, r.Level)).Should().Equal(("Alpha", 1), ("Alpha One", 2), ("Beta", 1), ("Gamma", 1));
        vm.SelectedNode!.Text.Should().Be("Gamma", "the selection stays on the same entry");
        vm.Message.Should().BeEmpty();
    }

    [Fact]
    public void Sort_reports_when_the_entries_are_already_in_book_order()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();

        // Act
        vm.SortByBookOrderCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta", "Gamma");
        vm.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Rows_list_every_entry_in_reading_order_with_its_level()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();

        // Act & Assert
        vm.Rows.Select(r => (r.Text, r.Level)).Should().Equal(("Alpha", 1), ("Alpha One", 2), ("Beta", 1), ("Gamma", 1));
    }

    [Fact]
    public void Rows_and_levels_follow_moves_additions_and_deletions()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        List<IReadOnlyList<EditTocNodeViewModel>> requested = new();
        vm.SelectionChangeRequested += (_, nodes) => requested.Add(nodes);
        vm.SetSelectedNodes(new[] { Find(vm, "Beta") });

        // Act
        vm.MoveRightCommand.Execute(null);

        // Assert
        vm.Rows.Select(r => (r.Text, r.Level)).Should().Equal(("Alpha", 1), ("Alpha One", 2), ("Beta", 2), ("Gamma", 1));
        requested.Last().Should().ContainSingle().Which.Text.Should().Be("Beta");

        // Act
        vm.AddBelowCommand.Execute(null);

        // Assert
        vm.Rows.Should().HaveCount(5);
        vm.Rows[3].Level.Should().Be(2);
        vm.Rows[3].Text.Should().BeEmpty();

        // Act
        vm.SetSelectedNodes(new[] { Find(vm, "Gamma") });
        vm.DeleteCommand.Execute(null);

        // Assert
        vm.Rows.Select(r => r.Text).Should().NotContain("Gamma");
    }

    [Fact]
    public void MoveRight_indents_a_contiguous_selection_under_the_preceding_sibling()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { Find(vm, "Beta"), Find(vm, "Gamma") });

        // Act
        vm.MoveRightCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha");
        vm.Nodes[0].Children.Select(n => n.Text).Should().Equal("Alpha One", "Beta", "Gamma");
        vm.Nodes[0].Children[1].Parent.Should().BeSameAs(vm.Nodes[0]);
    }

    [Fact]
    public void MoveRight_on_the_first_child_is_a_no_op()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { vm.Nodes[0] });

        // Act
        vm.MoveRightCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void MoveRight_expands_a_leaf_that_becomes_a_parent()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        EditTocNodeViewModel beta = Find(vm, "Beta");
        EditTocNodeViewModel gamma = Find(vm, "Gamma");
        beta.IsExpanded = false;
        vm.SetSelectedNodes(new[] { gamma });

        // Act
        vm.MoveRightCommand.Execute(null);

        // Assert
        beta.IsExpanded.Should().BeTrue();
        beta.Children.Should().ContainSingle().Which.Should().BeSameAs(gamma);
    }

    [Fact]
    public void MoveLeft_outdents_a_child_to_the_grandparent()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { Find(vm, "Alpha One") });

        // Act
        vm.MoveLeftCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Alpha One", "Beta", "Gamma");
        vm.Nodes[1].Children.Should().BeEmpty();
    }

    [Fact]
    public void MoveUp_reorders_within_the_parent()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { Find(vm, "Gamma") });

        // Act
        vm.MoveUpCommand.Execute(null);

        // Assert
        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Gamma", "Beta");
    }

    [Fact]
    public void Deleting_the_last_entry_inserts_a_placeholder()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        EditTocViewModel vm = NewViewModel();

        // Act
        foreach (EditTocNodeViewModel node in vm.Nodes.ToList())
        {
            vm.SetSelectedNodes(new[] { node });
            vm.DeleteCommand.Execute(null);
        }

        // Assert
        vm.Nodes.Should().ContainSingle().Which.Text.Should().Be("[placeholder]");
    }

    [Fact]
    public void Accept_is_blocked_when_an_entry_has_no_target()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { vm.Nodes[0] });
        vm.AddBelowCommand.Execute(null);

        // Act
        vm.AcceptCommand.Execute(null);

        // Assert
        vm.Accepted.Should().BeFalse();
        vm.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void NextInvalidEntryCommand_WhenMultipleEntriesAreInvalid_CyclesThroughReadingOrder()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        EditTocNodeViewModel alpha = vm.Nodes[0];
        EditTocNodeViewModel alphaOne = alpha.Children[0];
        EditTocNodeViewModel gamma = vm.Nodes[vm.Nodes.Count - 1];
        alpha.Text = string.Empty;
        alphaOne.Target = string.Empty;
        gamma.Target = string.Empty;
        vm.AcceptCommand.Execute(null);
        vm.SetSelectedNodes(new[] { alpha });

        // Act
        vm.NextInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(alphaOne);

        // Act
        vm.NextInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(gamma);

        // Act
        vm.PreviousInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(alphaOne);

        // Act
        vm.NextInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(gamma);

        // Act
        vm.NextInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(alpha, "next wraps to the first invalid entry");

        // Act
        vm.PreviousInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(gamma, "previous wraps to the last invalid entry");
    }

    [Fact]
    public void InvalidEntryNavigation_WhenNothingIsSelected_StartsAtFirstOrLastEntry()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        EditTocNodeViewModel alpha = vm.Nodes[0];
        EditTocNodeViewModel gamma = vm.Nodes[vm.Nodes.Count - 1];
        alpha.Text = string.Empty;
        gamma.Target = string.Empty;
        vm.AcceptCommand.Execute(null);

        // Act
        vm.NextInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(alpha);

        // Act
        vm.SetSelectedNodes(Array.Empty<EditTocNodeViewModel>());
        vm.PreviousInvalidEntryCommand.Execute(null);

        // Assert
        vm.SelectedNode.Should().BeSameAs(gamma);
    }

    [Fact]
    public void InvalidEntryNavigation_WhenEntriesBecomeValid_ClearsValidationMessage()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        EditTocNodeViewModel alpha = vm.Nodes[0];
        alpha.Text = string.Empty;
        vm.AcceptCommand.Execute(null);
        alpha.Text = "Alpha";

        // Act
        vm.NextInvalidEntryCommand.Execute(null);

        // Assert
        vm.HasInvalidEntries.Should().BeFalse();
        vm.Message.Should().BeEmpty();
    }

    [Fact]
    public void Accept_saves_edited_titles_to_the_book()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();
        Find(vm, "Beta").Text = "Beta Renamed";
        vm.SetSelectedNodes(Array.Empty<EditTocNodeViewModel>());

        // Act
        vm.AcceptCommand.Execute(null);

        // Assert
        vm.Accepted.Should().BeTrue();
        new NavProcessor(_book.GetNavResource()!).GetToc()
            .Select(e => e.Title).Should().Equal("Alpha", "Alpha One", "Beta Renamed", "Gamma");
    }

    [Fact]
    public void Select_target_list_offers_book_resources_relative_to_the_nav()
    {
        // Arrange
        EditTocViewModel vm = NewViewModel();

        // Act
        var items = vm.GetTargetItems();

        // Assert
        items.Should().NotBeEmpty();
        items.Should().Contain(i => i.Href.Contains("chapter", StringComparison.OrdinalIgnoreCase)
            || i.Href.Contains("Section", StringComparison.OrdinalIgnoreCase));
    }
}
