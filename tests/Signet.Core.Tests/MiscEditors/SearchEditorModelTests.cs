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
        var model = new SearchEditorModel();

        model.AddPathEntry(Leaf("Typografia/Cudzysłowy/Otwierający"));

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
        var model = new SearchEditorModel();

        model.AddPathEntry(Leaf("Grupa/Pierwszy"));
        model.AddPathEntry(Leaf("Grupa/Drugi"));

        model.Root.Children.Single().Children.Select(c => c.Name).Should().Equal("Pierwszy", "Drugi");
    }

    [Fact]
    public void ToEntries_FlattensWithComputedFullNames()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("A/B/x"));
        model.AddPathEntry(Leaf("A/y"));

        model.ToEntries().Select(e => e.FullName).Should().Equal("A/B/x", "A/y");
    }

    [Fact]
    public void LoadEntries_RoundTripsThroughToEntries()
    {
        var model = new SearchEditorModel();
        SearchEntry[] entries =
        {
            Leaf("Grupa/Pierwszy"),
            Leaf("Grupa/Drugi", find: "kot", replace: "pies", controls: "RX WR DN AH"),
            Leaf("Luźny"),
        };

        model.LoadEntries(entries);

        model.ToEntries().Should().BeEquivalentTo(entries, o => o.WithStrictOrdering());
        model.IsDataModified.Should().BeFalse();
    }

    [Fact]
    public void Rename_ChangesSegmentAndPropagatesToFullName()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis"));
        SearchEditorNode group = model.Root.Children.Single();

        model.Rename(group, "NowaNazwa   ");

        group.Name.Should().Be("NowaNazwa");
        model.ToEntries().Single().FullName.Should().Be("NowaNazwa/Wpis");
    }

    [Fact]
    public void Rename_keeps_a_slash_in_the_name_and_does_not_create_groups()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/Wpis"));
        SearchEditorNode leaf = model.Root.Children.Single().Children.Single();

        model.Rename(leaf, "<br/> to <br />");

        leaf.Name.Should().Be("<br/> to <br />");
        SearchEntry entry = model.ToEntries().Single();
        entry.GroupPath.Should().Equal("Grupa");
        entry.Name.Should().Be("<br/> to <br />");
    }

    [Fact]
    public void Delete_RemovesSubtree()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/A"));
        model.AddPathEntry(Leaf("Grupa/B"));
        SearchEditorNode a = model.GetNodeFromPath("Grupa", "A")!;

        model.Delete(a).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/B");
    }

    [Fact]
    public void MoveUp_And_MoveDown_SwapSiblings()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("one"));
        model.AddPathEntry(Leaf("two"));
        model.AddPathEntry(Leaf("three"));

        model.MoveDown(model.Root.Children[0]).Should().BeTrue();
        model.Root.Children.Select(c => c.Name).Should().Equal("two", "one", "three");

        model.MoveUp(model.Root.Children[2]).Should().BeTrue();
        model.Root.Children.Select(c => c.Name).Should().Equal("two", "three", "one");
    }

    [Fact]
    public void MoveDown_LastChildOfGroup_PromotesToParentLevel()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/only"));
        model.AddPathEntry(Leaf("after"));
        SearchEditorNode only = model.GetNodeFromPath("Grupa", "only")!;

        model.MoveDown(only).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/", "only", "after");
    }

    [Fact]
    public void MoveRight_MovesEntryIntoPrecedingGroup()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/inside"));
        model.AddPathEntry(Leaf("outside"));
        SearchEditorNode outside = model.GetNodeFromPath("outside")!;

        model.MoveRight(outside).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inside", "Grupa/outside");
    }

    [Fact]
    public void MoveLeft_MovesEntryToParentLevel()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("Grupa/inner"));
        model.AddPathEntry(Leaf("Grupa/inner2"));
        SearchEditorNode inner = model.GetNodeFromPath("Grupa", "inner")!;

        model.MoveLeft(inner).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inner2", "inner");
    }

    [Fact]
    public void FillControls_CopiesControlsFromFirstNodeToOthers()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("a", controls: "RX WR DN AH"));
        model.AddPathEntry(Leaf("b", controls: "NL DN CF"));
        model.AddPathEntry(Leaf("c", controls: "NL DN CF"));

        model.FillControls(model.Root.Children.ToList());

        model.Root.Children.Select(c => c.Controls).Should().AllBe("RX WR DN AH");
    }

    [Fact]
    public void GetNonGroupItems_ReturnsOnlyLeavesRecursively()
    {
        var model = new SearchEditorModel();
        model.AddPathEntry(Leaf("G/H/deep"));
        model.AddPathEntry(Leaf("G/shallow"));

        model.GetNonGroupItems(model.Root.Children).Select(n => n.Name)
            .Should().Equal("deep", "shallow");
    }
}
