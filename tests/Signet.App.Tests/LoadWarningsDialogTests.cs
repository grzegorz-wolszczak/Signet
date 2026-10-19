using System.Collections.Generic;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The tree of the load warnings window (<see cref="LoadWarningsViewModel"/>).</summary>
public sealed class LoadWarningsDialogTests
{
    private static readonly Fixture Fixture = new();

    private static LoadWarning Group(string title, params LoadWarningItem[] items) =>
        new(Fixture.Create<string>(), title, items);

    [Fact]
    public void Roots_has_a_leaf_for_a_single_warning_and_a_group_with_a_file_count_for_a_list()
    {
        // Arrange
        string single = Fixture.Create<string>();
        string title = Fixture.Create<string>();
        LoadWarningItem[] items = { new("Text/a.xhtml", "bad", 3, 7), new("Text/b.xhtml", "worse") };

        // Act
        LoadWarningsViewModel vm = new("book.epub", new[] { new LoadWarning(single), Group(title, items) }, null);

        // Assert
        vm.Roots.Should().HaveCount(2);
        vm.Roots[0].Text.Should().Be(single);
        vm.Roots[0].HasChildren.Should().BeFalse();
        vm.Roots[1].Text.Should().Be($"{title} (2)");
        vm.Roots[1].Children.Select(c => c.Text).Should().Equal("Text/a.xhtml", "Text/b.xhtml");
        vm.Roots[1].Children.Select(c => c.Reason).Should().Equal("bad", "worse");
        vm.Roots[1].Children.Select(c => c.Location).Should().Equal("3:7", string.Empty);
    }

    [Fact]
    public void Activate_opens_a_file_at_its_position_and_ignores_warnings()
    {
        // Arrange
        List<(string Path, int Line, int Column)> opened = new();

        // Act
        LoadWarningsViewModel vm = new(
            "book.epub",
            new[] { Group("t", new LoadWarningItem("Text/a.xhtml", "bad", 3, 7)), new LoadWarning("single") },
            (path, line, column) => opened.Add((path, line, column)));

        // Act & Assert
        vm.Activate(vm.Roots[0]).Should().BeFalse("a warning is not a file");
        vm.Activate(vm.Roots[1]).Should().BeFalse();
        vm.Activate(vm.Roots[0].Children[0]).Should().BeTrue();

        // Assert
        opened.Should().Equal(("Text/a.xhtml", 3, 7));
    }

    [Fact]
    public void Activate_opens_the_file_of_a_single_warning_without_a_position()
    {
        // Arrange
        List<(string Path, int Line, int Column)> opened = new();

        // Act
        LoadWarningsViewModel vm = new(
            "book.epub",
            new[] { new LoadWarning("font problem") { BookPath = "Fonts/a.ttf" }, new LoadWarning("no file") },
            (path, line, column) => opened.Add((path, line, column)));

        // Act & Assert
        vm.Activate(vm.Roots[0]).Should().BeTrue();
        vm.Activate(vm.Roots[1]).Should().BeFalse();

        // Assert
        opened.Should().Equal(("Fonts/a.ttf", -1, -1));
    }

    [Fact]
    public void ExpandAll_and_CollapseAll_change_every_warning_with_files()
    {
        // Arrange
        LoadWarningsViewModel vm = new(
            "book.epub", new[] { Group("a", new LoadWarningItem("x", "r")), new LoadWarning("single"), Group("b", new LoadWarningItem("y", "r")) }, null);

        // Act
        vm.CollapseAll();

        // Assert
        vm.Roots.Where(r => r.HasChildren).Should().OnlyContain(r => !r.IsExpanded);

        // Act
        vm.ExpandAll();

        // Assert
        vm.Roots.Where(r => r.HasChildren).Should().OnlyContain(r => r.IsExpanded);
    }

    [Fact]
    public void CopyText_lists_the_header_each_warning_and_its_files()
    {
        // Arrange
        LoadWarningsViewModel vm = new(
            "book.epub", new[] { Group("Group", new LoadWarningItem("Text/a.xhtml", "bad", 3, 7)) }, null);

        // Act
        string text = vm.CopyText();

        // Assert
        text.Should().StartWith(vm.Header);
        text.Should().Contain("• Group (1)").And.Contain("Text/a.xhtml\tbad\t3:7");
    }

    [Fact]
    public void Location_is_empty_without_a_line_and_has_no_column_when_only_the_line_is_known()
    {
        // Arrange
        LoadWarningsViewModel vm = new(
            "book.epub",
            new[] { Group("t", new LoadWarningItem("a", "r"), new LoadWarningItem("b", "r", 5)) },
            null);

        // Act & Assert
        vm.Roots[0].Children.Select(c => c.Location).Should().Equal(string.Empty, "5");
    }
}
