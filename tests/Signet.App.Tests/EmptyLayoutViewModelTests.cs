using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for the view model of the "Custom Epub Layout" designer.</summary>
public sealed class EmptyLayoutViewModelTests
{
    [Theory]
    [InlineData("2.0")]
    [InlineData("3.0")]
    public void Default_tree_matches_the_standard_layout_and_validates(string version)
    {
        EmptyLayoutViewModel vm = new(version);

        vm.GetBookPaths().Should().BeEquivalentTo(BookCreator.StandardLayout(version));
        vm.Validate().Should().BeTrue();
        vm.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void Deleting_the_opf_makes_the_layout_invalid()
    {
        EmptyLayoutViewModel vm = new("2.0");
        vm.SelectedNode = TryFind(vm.Root, "content.opf");

        vm.DeleteSelection();

        vm.Validate().Should().BeFalse();
        vm.ValidationError.Should().Contain("OPF");
    }

    [Fact]
    public void The_opf_file_type_cannot_be_added_twice()
    {
        EmptyLayoutViewModel vm = new("2.0");
        vm.IsFileTypeAllowed("content.opf").Should().BeFalse();
        vm.IsFileTypeAllowed("marker.xhtml").Should().BeTrue();
    }

    [Fact]
    public void Epub2_layout_has_no_nav_or_js_marker_types()
    {
        EmptyLayoutViewModel vm = new("2.0");
        vm.MarkerTypes.Select(t => t.FileName).Should().NotContain("nav.xhtml").And.NotContain("marker.js");
    }

    [Fact]
    public void Adding_a_folder_and_file_extends_the_book_paths()
    {
        EmptyLayoutViewModel vm = new("3.0");
        vm.SelectedNode = vm.Root;
        LayoutNode? extras = vm.AddFolder("Extra");
        vm.SelectedNode = extras;
        vm.AddFile(vm.MarkerTypes.First(t => t.FileName == "marker.xhtml"));

        vm.GetBookPaths().Should().Contain("Extra/marker.xhtml");
    }

    private static LayoutNode? TryFind(LayoutNode node, string name)
    {
        foreach (LayoutNode child in node.Children)
        {
            if (!child.IsFolder && child.Name == name)
            {
                return child;
            }

            if (child.IsFolder && TryFind(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
