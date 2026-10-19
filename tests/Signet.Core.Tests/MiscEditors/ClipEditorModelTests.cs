using System.Linq;
using AwesomeAssertions;
using Signet.Core.MiscEditors;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>Tests of <see cref="ClipEditorModel"/> — the group/clip tree, CRUD, moving, slots.</summary>
public sealed class ClipEditorModelTests
{
    // A clip from a "Group/Subgroup/Name" test path (the test names contain no "/" of their own).
    private static ClipEntry Leaf(string path, string text = "<p>\\1</p>")
    {
        string[] parts = path.Split('/');
        return new(IsGroup: false, parts[..^1], parts[^1], text);
    }

    [Fact]
    public void AddPathEntry_CreatesIntermediateGroups()
    {
        // Arrange
        var model = new ClipEditorModel();

        // Act
        model.AddPathEntry(Leaf("Example Clips/epub3/section"));

        // Assert
        ClipEditorNode example = model.Root.Children.Single();
        example.IsGroup.Should().BeTrue();
        example.Name.Should().Be("Example Clips");
        ClipEditorNode epub3 = example.Children.Single();
        epub3.Name.Should().Be("epub3");
        epub3.Children.Single().Name.Should().Be("section");
    }

    [Fact]
    public void AddPathEntry_SharesExistingGroups()
    {
        // Arrange
        var model = new ClipEditorModel();

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
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("A/B/x"));
        model.AddPathEntry(Leaf("A/y"));

        // Act & Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("A/B/x", "A/y");
    }

    [Fact]
    public void LoadEntries_RoundTripsThroughToEntries()
    {
        // Arrange
        var model = new ClipEditorModel();
        ClipEntry[] entries =
        {
            Leaf("Grupa/Pierwszy"),
            Leaf("Grupa/Drugi", text: "<span>\\1</span>"),
            Leaf("Luźny", text: "<hr />"),
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
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis"));
        ClipEditorNode group = model.Root.Children.Single();

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
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis"));
        ClipEditorNode leaf = model.Root.Children.Single().Children.Single();

        // Act
        model.Rename(leaf, "<test/>");

        // Assert
        leaf.Name.Should().Be("<test/>");
        ClipEntry entry = model.ToEntries().Single();
        entry.GroupPath.Should().Equal("Grupa");
        entry.Name.Should().Be("<test/>");
    }

    [Fact]
    public void LoadEntries_keeps_names_with_a_slash_as_single_nodes()
    {
        // Arrange
        var model = new ClipEditorModel();

        string[] groupPath = { "a/b" };

        // Act
        model.LoadEntries(new[] { new ClipEntry(false, groupPath, "<br/>", "<br/>") });

        ClipEditorNode group = model.Root.Children.Single();

        // Assert
        group.Name.Should().Be("a/b");
        group.Children.Single().Name.Should().Be("<br/>");
        model.GetNodeFromPath("a/b", "<br/>").Should().NotBeNull();
    }

    [Fact]
    public void SetText_UpdatesLeafTextOnly()
    {
        // Arrange
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis", text: "old"));
        ClipEditorNode group = model.Root.Children.Single();
        ClipEditorNode leaf = group.Children.Single();

        // Act
        model.SetText(leaf, "<div>\\1</div>");

        model.SetText(group, "ignored — group");

        // Assert
        leaf.Text.Should().Be("<div>\\1</div>");
        group.Text.Should().BeEmpty();
    }

    [Fact]
    public void Delete_RemovesSubtree()
    {
        // Arrange
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/A"));
        model.AddPathEntry(Leaf("Grupa/B"));
        ClipEditorNode a = model.GetNodeFromPath("Grupa", "A")!;

        // Act
        model.Delete(a).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/B");
    }

    [Fact]
    public void MoveUp_And_MoveDown_SwapSiblings()
    {
        // Arrange
        var model = new ClipEditorModel();
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
    public void MoveRight_MovesEntryIntoPrecedingGroup()
    {
        // Arrange
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/inside"));
        model.AddPathEntry(Leaf("outside"));
        ClipEditorNode outside = model.GetNodeFromPath("outside")!;

        // Act
        model.MoveRight(outside).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inside", "Grupa/outside");
    }

    [Fact]
    public void MoveLeft_MovesEntryToParentLevel()
    {
        // Arrange
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/inner"));
        model.AddPathEntry(Leaf("Grupa/inner2"));
        ClipEditorNode inner = model.GetNodeFromPath("Grupa", "inner")!;

        // Act
        model.MoveLeft(inner).Should().BeTrue();

        // Assert
        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inner2", "inner");
    }

    [Fact]
    public void GetNonGroupItems_ReturnsOnlyLeavesRecursively()
    {
        // Arrange
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("G/H/deep"));
        model.AddPathEntry(Leaf("G/shallow"));

        // Act & Assert
        model.GetNonGroupItems(model.Root.Children).Select(n => n.Name)
            .Should().Equal("deep", "shallow");
    }

    [Fact]
    public void TopLevelSlots_OnlyCountsRootLevelNonGroupEntries()
    {
        // Arrange
        var model = new ClipEditorModel();
        model.AddPathEntry(Leaf("Grupa/nested")); // in a group — does not count towards slots
        model.AddPathEntry(Leaf("first"));
        model.AddPathEntry(Leaf("second"));

        // Act & Assert
        model.TopLevelSlots().Select(n => n.Name).Should().Equal("first", "second");

        // Act & Assert
        model.GetSlot(1)!.Name.Should().Be("first");

        // Act & Assert
        model.GetSlot(2)!.Name.Should().Be("second");

        // Act & Assert
        model.GetSlot(3).Should().BeNull();
    }

    [Fact]
    public void TopLevelSlots_TruncatesAtMaxSlots()
    {
        // Arrange
        var model = new ClipEditorModel();
        for (int i = 1; i <= ClipEditorModel.MaxSlots + 5; i++)
        {
            model.AddPathEntry(Leaf("clip" + i));
        }

        // Act & Assert
        model.TopLevelSlots().Should().HaveCount(ClipEditorModel.MaxSlots);

        // Act & Assert
        model.GetSlot(ClipEditorModel.MaxSlots)!.Name.Should().Be("clip" + ClipEditorModel.MaxSlots);

        // Act & Assert
        model.GetSlot(ClipEditorModel.MaxSlots + 1).Should().BeNull();
    }
}
