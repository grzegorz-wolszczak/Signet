using System.Linq;
using AwesomeAssertions;
using Signet.Core.MiscEditors;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>Tests of <see cref="SearchEditorModel"/> — the group/entry tree, CRUD, moving.</summary>
public sealed class SearchEditorModelTests
{
    // A search from a "Group/Subgroup/Name" test path (the test names contain no "/" of their own).
    private static SearchEntry Leaf(string path, string find = "f", string replace = "r", string controls = "NL DN CF")
    {
        string[] parts = path.Split('/');
        return new(IsGroup: false, parts[..^1], parts[^1], find, replace, controls);
    }

    [Fact]
    public void AddPathEntry_CreatesIntermediateGroups()
    {
        // Arrange
        var model = new SearchEditorModel();

        // Act
        model.AddPathEntry(Leaf("Typografia/Cudzysłowy/Otwierający"));

        // Assert
        SearchEditorNode typografia = model.Root.Children.Single();
        typografia.IsGroup.Should().BeTrue();
        typografia.Name.Should().Be("Typografia");
        SearchEditorNode quotes = typografia.Children.Single();
        quotes.Name.Should().Be("Cudzysłowy");
        quotes.Children.Single().Name.Should().Be("Otwierający");
    }

    [Fact]
    public void AddPathEntry_SharesExistingGroups()
    {
        // Arrange
        var model = new SearchEditorModel();

        // Act
        model.AddPathEntry(Leaf("Grupa/Pierwszy"));
        model.AddPathEntry(Leaf("Grupa/Drugi"));

        // Assert
        model.Root.Children.Single().Children.Select(c => c.Name).Should().Equal("Pierwszy", "Drugi");
    }

    [Fact]
    public void ToEntries_FlattensWithComputedFullNames()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("A/B/x"));
        model.AddPathEntry(Leaf("A/y"));

        // Act & Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("A/B/x", "A/y");
    }

    [Fact]
    public void LoadEntries_RoundTripsThroughToEntries()
    {
        // Arrange
        var model = new SearchEditorModel();
        SearchEntry[] entries =
        {
            Leaf("Grupa/Pierwszy"),
            Leaf("Grupa/Drugi", find: "kot", replace: "pies", controls: "RX WR DN AH"),
            Leaf("Luźny"),
        };

        // Act
        model.LoadEntries(entries);

        // Assert
        model.ToEntries().Should().BeEquivalentTo(entries, o => o.WithStrictOrdering());
        model.IsDataModified.Should().BeFalse();
    }

    [Fact]
    public void Rename_ChangesSegmentAndPropagatesToFullName()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis"));
        SearchEditorNode group = model.Root.Children.Single();

        // Act
        model.Rename(group, "NowaNazwa   ");

        // Assert
        group.Name.Should().Be("NowaNazwa");
        model.ToEntries().Single().FullName.Should().Be("NowaNazwa/Wpis");
    }

    [Fact]
    public void Rename_keeps_a_slash_in_the_name_and_does_not_create_groups()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis"));
        SearchEditorNode leaf = model.Root.Children.Single().Children.Single();

        // Act
        model.Rename(leaf, "<br/> to <br />");

        // Assert
        leaf.Name.Should().Be("<br/> to <br />");
        SearchEntry entry = model.ToEntries().Single();
        entry.GroupPath.Should().Equal("Grupa");
        entry.Name.Should().Be("<br/> to <br />");
    }

    [Fact]
    public void Delete_RemovesSubtree()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/A"));
        model.AddPathEntry(Leaf("Grupa/B"));
        SearchEditorNode a = model.GetNodeFromPath("Grupa", "A")!;

        // Act
        model.Delete(a).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/B");
    }

    [Fact]
    public void MoveUp_And_MoveDown_SwapSiblings()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("one"));
        model.AddPathEntry(Leaf("two"));
        model.AddPathEntry(Leaf("three"));

        // Act
        model.MoveDown(model.Root.Children[0]).Should().BeTrue();

        // Assert
        model.Root.Children.Select(c => c.Name).Should().Equal("two", "one", "three");

        // Act
        model.MoveUp(model.Root.Children[2]).Should().BeTrue();

        // Assert
        model.Root.Children.Select(c => c.Name).Should().Equal("two", "three", "one");
    }

    [Fact]
    public void MoveDown_LastChildOfGroup_PromotesToParentLevel()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/only"));
        model.AddPathEntry(Leaf("after"));
        SearchEditorNode only = model.GetNodeFromPath("Grupa", "only")!;

        // Act
        model.MoveDown(only).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/", "only", "after");
    }

    [Fact]
    public void MoveRight_MovesEntryIntoPrecedingGroup()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/inside"));
        model.AddPathEntry(Leaf("outside"));
        SearchEditorNode outside = model.GetNodeFromPath("outside")!;

        // Act
        model.MoveRight(outside).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inside", "Grupa/outside");
    }

    [Fact]
    public void MoveLeft_MovesEntryToParentLevel()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/inner"));
        model.AddPathEntry(Leaf("Grupa/inner2"));
        SearchEditorNode inner = model.GetNodeFromPath("Grupa", "inner")!;

        // Act
        model.MoveLeft(inner).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inner2", "inner");
    }

    [Fact]
    public void FillControls_CopiesControlsFromFirstNodeToOthers()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("a", controls: "RX WR DN AH"));
        model.AddPathEntry(Leaf("b", controls: "NL DN CF"));
        model.AddPathEntry(Leaf("c", controls: "NL DN CF"));

        // Act
        model.FillControls(model.Root.Children.ToList());

        // Assert
        model.Root.Children.Select(c => c.Controls).Should().AllBe("RX WR DN AH");
    }

    [Fact]
    public void GetNonGroupItems_ReturnsOnlyLeavesRecursively()
    {
        // Arrange
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("G/H/deep"));
        model.AddPathEntry(Leaf("G/shallow"));

        // Act & Assert
        model.GetNonGroupItems(model.Root.Children).Select(n => n.Name)
            .Should().Equal("deep", "shallow");
    }
}
