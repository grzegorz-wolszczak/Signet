using System.Linq;
using AwesomeAssertions;
using Signet.Core.MiscEditors;
using Xunit;

namespace Signet.Core.Tests.MiscEditors;

/// <summary>Tests of <see cref="SearchEditorModel"/> — the group/entry tree, CRUD, moving.</summary>
public sealed class SearchEditorModelTests
{
    private static SearchEntry Leaf(string fullName, string find = "f", string replace = "r", string controls = "NL DN CF") =>
        new(IsGroup: false, fullName, fullName.Split('/').Last(), find, replace, controls);

    [Fact]
    public void AddFullNameEntry_CreatesIntermediateGroups()
    {
        var model = new SearchEditorModel();

        model.AddFullNameEntry(Leaf("Typografia/Cudzysłowy/Otwierający"));

        SearchEditorNode typografia = model.Root.Children.Single();
        typografia.IsGroup.Should().BeTrue();
        typografia.Name.Should().Be("Typografia");
        SearchEditorNode quotes = typografia.Children.Single();
        quotes.Name.Should().Be("Cudzysłowy");
        quotes.Children.Single().Name.Should().Be("Otwierający");
    }

    [Fact]
    public void AddFullNameEntry_SharesExistingGroups()
    {
        var model = new SearchEditorModel();

        model.AddFullNameEntry(Leaf("Grupa/Pierwszy"));
        model.AddFullNameEntry(Leaf("Grupa/Drugi"));

        model.Root.Children.Single().Children.Select(c => c.Name).Should().Equal("Pierwszy", "Drugi");
    }

    [Fact]
    public void ToEntries_FlattensWithComputedFullNames()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("A/B/x"));
        model.AddFullNameEntry(Leaf("A/y"));

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
        model.AddFullNameEntry(Leaf("Grupa/Wpis"));
        SearchEditorNode group = model.Root.Children.Single();

        model.Rename(group, "Nowa/Nazwa   ");

        group.Name.Should().Be("NowaNazwa");
        model.ToEntries().Single().FullName.Should().Be("NowaNazwa/Wpis");
    }

    [Fact]
    public void Delete_RemovesSubtree()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("Grupa/A"));
        model.AddFullNameEntry(Leaf("Grupa/B"));
        SearchEditorNode a = model.GetNodeFromFullName("Grupa/A")!;

        model.Delete(a).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/B");
    }

    [Fact]
    public void MoveUp_And_MoveDown_SwapSiblings()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("one"));
        model.AddFullNameEntry(Leaf("two"));
        model.AddFullNameEntry(Leaf("three"));

        model.MoveDown(model.Root.Children[0]).Should().BeTrue();
        model.Root.Children.Select(c => c.Name).Should().Equal("two", "one", "three");

        model.MoveUp(model.Root.Children[2]).Should().BeTrue();
        model.Root.Children.Select(c => c.Name).Should().Equal("two", "three", "one");
    }

    [Fact]
    public void MoveDown_LastChildOfGroup_PromotesToParentLevel()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("Grupa/only"));
        model.AddFullNameEntry(Leaf("after"));
        SearchEditorNode only = model.GetNodeFromFullName("Grupa/only")!;

        model.MoveDown(only).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/", "only", "after");
    }

    [Fact]
    public void MoveRight_MovesEntryIntoPrecedingGroup()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("Grupa/inside"));
        model.AddFullNameEntry(Leaf("outside"));
        SearchEditorNode outside = model.GetNodeFromFullName("outside")!;

        model.MoveRight(outside).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inside", "Grupa/outside");
    }

    [Fact]
    public void MoveLeft_MovesEntryToParentLevel()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("Grupa/inner"));
        model.AddFullNameEntry(Leaf("Grupa/inner2"));
        SearchEditorNode inner = model.GetNodeFromFullName("Grupa/inner")!;

        model.MoveLeft(inner).Should().BeTrue();

        model.ToEntries().Select(e => e.FullName).Should().Equal("Grupa/inner2", "inner");
    }

    [Fact]
    public void FillControls_CopiesControlsFromFirstNodeToOthers()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("a", controls: "RX WR DN AH"));
        model.AddFullNameEntry(Leaf("b", controls: "NL DN CF"));
        model.AddFullNameEntry(Leaf("c", controls: "NL DN CF"));

        model.FillControls(model.Root.Children.ToList());

        model.Root.Children.Select(c => c.Controls).Should().AllBe("RX WR DN AH");
    }

    [Fact]
    public void GetNonGroupItems_ReturnsOnlyLeavesRecursively()
    {
        var model = new SearchEditorModel();
        model.AddFullNameEntry(Leaf("G/H/deep"));
        model.AddFullNameEntry(Leaf("G/shallow"));

        model.GetNonGroupItems(model.Root.Children).Select(n => n.Name)
            .Should().Equal("deep", "shallow");
    }
}
