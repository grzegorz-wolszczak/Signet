using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
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

    [Fact]
    public void Builds_a_node_tree_from_the_headings()
    {
        SetBody("<body>\n<h1>A</h1>\n<h2>A one</h2>\n<h1>B</h1>\n</body>");

        HeadingSelectorViewModel vm = NewViewModel();

        vm.Nodes.Select(n => n.TitleText).Should().Equal("A", "B");
        vm.Nodes[0].Children.Should().ContainSingle().Which.TitleText.Should().Be("A one");
    }

    [Fact]
    public void Toc_items_only_hides_excluded_headings_and_promotes_their_children()
    {
        SetBody("<body>\n<h1>A</h1>\n<h2>A one</h2>\n<h1>B</h1>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();

        vm.Nodes[0].IncludeInToc = false;

        vm.TocItemsOnly.Should().BeTrue();
        vm.Nodes.Select(n => n.TitleText).Should().Equal("A one", "B");
    }

    [Fact]
    public void Up_to_level_choice_marks_inclusion_and_resets_the_combo()
    {
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n<h3>C</h3>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();

        // 0 = prompt, 1 = "None", 2 = "Up to level 1", 3 = "Up to level 2", 4 = "All"
        vm.SelectedLevelChoiceIndex = 2;

        vm.SelectedLevelChoiceIndex.Should().Be(0);
        vm.TocItemsOnly = true;
        vm.Nodes.Select(n => n.TitleText).Should().Equal("A");
    }

    [Fact]
    public void Change_level_command_reparents_and_updates_selection()
    {
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        vm.SelectedNode = vm.Nodes[0].Children[0];

        vm.DecreaseLevelCommand.Execute(null);

        vm.Nodes.Select(n => n.TitleText).Should().Equal("A", "B");
        vm.SelectedNode!.TitleText.Should().Be("B");
    }

    [Fact]
    public void Accept_applies_changes_and_raises_close()
    {
        SetBody("<body>\n<h1>A</h1>\n<h2>B</h2>\n</body>");
        HeadingSelectorViewModel vm = NewViewModel();
        bool closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.Nodes[0].Children[0].IncludeInToc = false;

        vm.AcceptCommand.Execute(null);

        vm.Accepted.Should().BeTrue();
        vm.BookChanged.Should().BeTrue();
        closed.Should().BeTrue();
    }
}
