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
        EditTocViewModel vm = NewViewModel();

        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta", "Gamma");
        vm.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Alpha One");
        vm.Nodes[0].Children[0].Target.Should().NotBeEmpty();
    }

    [Fact]
    public void Rows_list_every_entry_in_reading_order_with_its_level()
    {
        EditTocViewModel vm = NewViewModel();

        vm.Rows.Select(r => (r.Text, r.Level)).Should().Equal(("Alpha", 1), ("Alpha One", 2), ("Beta", 1), ("Gamma", 1));
        vm.Rows[1].Indent.Left.Should().BeGreaterThan(vm.Rows[0].Indent.Left);
    }

    [Fact]
    public void Rows_and_levels_follow_moves_additions_and_deletions()
    {
        EditTocViewModel vm = NewViewModel();
        List<IReadOnlyList<EditTocNodeViewModel>> requested = new();
        vm.SelectionChangeRequested += (_, nodes) => requested.Add(nodes);
        vm.SetSelectedNodes(new[] { Find(vm, "Beta") });

        vm.MoveRightCommand.Execute(null);

        vm.Rows.Select(r => (r.Text, r.Level)).Should().Equal(("Alpha", 1), ("Alpha One", 2), ("Beta", 2), ("Gamma", 1));
        requested.Last().Should().ContainSingle().Which.Text.Should().Be("Beta");

        vm.AddBelowCommand.Execute(null);
        vm.Rows.Should().HaveCount(5);
        vm.Rows[3].Level.Should().Be(2);
        vm.Rows[3].Text.Should().BeEmpty();

        vm.SetSelectedNodes(new[] { Find(vm, "Gamma") });
        vm.DeleteCommand.Execute(null);
        vm.Rows.Select(r => r.Text).Should().NotContain("Gamma");
    }

    [Fact]
    public void MoveRight_indents_a_contiguous_selection_under_the_preceding_sibling()
    {
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { Find(vm, "Beta"), Find(vm, "Gamma") });

        vm.MoveRightCommand.Execute(null);

        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha");
        vm.Nodes[0].Children.Select(n => n.Text).Should().Equal("Alpha One", "Beta", "Gamma");
        vm.Nodes[0].Children[1].Parent.Should().BeSameAs(vm.Nodes[0]);
    }

    [Fact]
    public void MoveRight_on_the_first_child_is_a_no_op()
    {
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { vm.Nodes[0] });

        vm.MoveRightCommand.Execute(null);

        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Fact]
    public void MoveLeft_outdents_a_child_to_the_grandparent()
    {
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { Find(vm, "Alpha One") });

        vm.MoveLeftCommand.Execute(null);

        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Alpha One", "Beta", "Gamma");
        vm.Nodes[1].Children.Should().BeEmpty();
    }

    [Fact]
    public void MoveUp_reorders_within_the_parent()
    {
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { Find(vm, "Gamma") });

        vm.MoveUpCommand.Execute(null);

        vm.Nodes.Select(n => n.Text).Should().Equal("Alpha", "Gamma", "Beta");
    }

    [Fact]
    public void Deleting_the_last_entry_inserts_a_placeholder()
    {
        using UiCultureScope culture = new("en");
        EditTocViewModel vm = NewViewModel();
        foreach (EditTocNodeViewModel node in vm.Nodes.ToList())
        {
            vm.SetSelectedNodes(new[] { node });
            vm.DeleteCommand.Execute(null);
        }

        vm.Nodes.Should().ContainSingle().Which.Text.Should().Be("[placeholder]");
    }

    [Fact]
    public void Accept_is_blocked_when_an_entry_has_no_target()
    {
        EditTocViewModel vm = NewViewModel();
        vm.SetSelectedNodes(new[] { vm.Nodes[0] });
        vm.AddBelowCommand.Execute(null);

        vm.AcceptCommand.Execute(null);

        vm.Accepted.Should().BeFalse();
        vm.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Accept_saves_edited_titles_to_the_book()
    {
        EditTocViewModel vm = NewViewModel();
        Find(vm, "Beta").Text = "Beta Renamed";
        vm.SetSelectedNodes(Array.Empty<EditTocNodeViewModel>());

        vm.AcceptCommand.Execute(null);

        vm.Accepted.Should().BeTrue();
        new NavProcessor(_book.GetNavResource()!).GetToc()
            .Select(e => e.Title).Should().Equal("Alpha", "Alpha One", "Beta Renamed", "Gamma");
    }

    [Fact]
    public void Select_target_list_offers_book_resources_relative_to_the_nav()
    {
        EditTocViewModel vm = NewViewModel();

        var items = vm.GetTargetItems();

        items.Should().NotBeEmpty();
        items.Should().Contain(i => i.Href.Contains("chapter", StringComparison.OrdinalIgnoreCase)
            || i.Href.Contains("Section", StringComparison.OrdinalIgnoreCase));
    }
}
