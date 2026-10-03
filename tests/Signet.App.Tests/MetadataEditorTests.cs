using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Semantics;
using Signet.Core.Metadata;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="MetadataEditorViewModel"/> (the "Metadata Editor" dialog).</summary>
public sealed class MetadataEditorTests : IDisposable
{
    private readonly Book _epub3 = BookCreator.CreateNewBook("3.0");
    private readonly Book _epub2 = BookCreator.CreateNewBook("2.0");

    public void Dispose()
    {
        _epub3.Dispose();
        _epub2.Dispose();
    }

    private static MetadataNodeViewModel Element(MetadataEditorViewModel vm, string code) =>
        vm.Nodes.Single(n => n.Code == code);

    [Fact]
    public void Constructor_extracts_existing_title_and_language()
    {
        MetadataEditorViewModel vm = new(_epub3);

        vm.IsEpub3.Should().BeTrue();
        Element(vm, "dc:title").Content.Should().Be("[Main title here]");
        Element(vm, "dc:language").Content.Should().Be("en");
    }

    [Fact]
    public void NameDisplay_and_ValuePreview_translate_codes_for_readability()
    {
        MetadataEditorViewModel vm = new(_epub3);

        MetadataNodeViewModel creator = vm.InsertElement("dc:creator", "Jane Doe");
        creator.NameDisplay.Should().Be(MetadataFieldCatalog.Epub3Elements["dc:creator"].Name);

        MetadataNodeViewModel role = vm.InsertChild("role", "aut")!;

        role.NameDisplay.Should().Be(MetadataFieldCatalog.Epub3Properties["role"].Name);
        role.ValuePreview.Should().Be(MarcRelators.GetName("aut"));
        creator.Children.Should().Contain(role);
    }

    [Fact]
    public void InsertElement_inserts_after_top_level_ancestor_of_selection()
    {
        MetadataEditorViewModel vm = new(_epub3);
        vm.SelectedNode = Element(vm, "dc:title");

        MetadataNodeViewModel creator = vm.InsertElement("dc:creator", "Jane Doe");

        vm.Nodes.IndexOf(creator).Should().Be(vm.Nodes.IndexOf(Element(vm, "dc:title")) + 1);
        vm.SelectedNode.Should().Be(creator);
    }

    [Fact]
    public void InsertChild_targets_top_level_ancestor_even_when_a_child_is_selected()
    {
        MetadataEditorViewModel vm = new(_epub3);
        MetadataNodeViewModel creator = vm.InsertElement("dc:creator", "Jane Doe");
        MetadataNodeViewModel role = vm.InsertChild("role", "aut")!;

        vm.SelectedNode = role;
        MetadataNodeViewModel scheme = vm.InsertChild("scheme", "marc:relators")!;

        scheme.Parent.Should().Be(creator);
        creator.Children.Should().Contain(scheme);
    }

    [Fact]
    public void Remove_and_move_commands_mutate_the_tree()
    {
        MetadataEditorViewModel vm = new(_epub3);
        MetadataNodeViewModel creator = vm.InsertElement("dc:creator", "Jane Doe");
        MetadataNodeViewModel contributor = vm.InsertElement("dc:contributor", "John Roe");

        vm.SelectedNode = contributor;
        vm.MoveUpCommand.Execute(null);
        vm.Nodes.IndexOf(contributor).Should().Be(vm.Nodes.IndexOf(creator) - 1);

        vm.SelectedNode = creator;
        vm.RemoveCommand.Execute(null);
        vm.Nodes.Should().NotContain(creator);
    }

    [Fact]
    public void Accept_saves_new_creator_with_role_refines_to_epub3_opf()
    {
        MetadataEditorViewModel vm = new(_epub3);
        MetadataNodeViewModel creator = vm.InsertElement("dc:creator", "Jane Doe");
        vm.InsertChild("role", "aut");
        vm.SelectedNode = creator;

        vm.AcceptCommand.Execute(null);

        vm.Accepted.Should().BeTrue();
        OpfDocument doc = _epub3.GetOpf().GetOpfDocument();
        MetaEntry savedCreator = doc.Metadata.Single(m => m.Name == "dc:creator");
        savedCreator.Content.Should().Be("Jane Doe");
        string id = savedCreator.Attributes.Value("id");
        id.Should().NotBeEmpty();
        doc.Metadata.Should().ContainSingle(
            m => m.Name == "meta" && m.Attributes.Value("refines") == "#" + id &&
                 m.Attributes.Value("property") == "role" && m.Content == "aut");
    }

    [Fact]
    public void Accept_saves_new_creator_with_role_attribute_to_epub2_opf()
    {
        MetadataEditorViewModel vm = new(_epub2);
        vm.IsEpub3.Should().BeFalse();
        vm.InsertElement("dc:creator", "Jane Doe");
        vm.InsertChild("opf:role", "aut");

        vm.AcceptCommand.Execute(null);

        OpfDocument doc = _epub2.GetOpf().GetOpfDocument();
        MetaEntry savedCreator = doc.Metadata.Single(m => m.Name == "dc:creator");
        savedCreator.Content.Should().Be("Jane Doe");
        savedCreator.Attributes.Value("opf:role").Should().Be("aut");
    }

    [Fact]
    public void Cancel_does_not_modify_the_book()
    {
        MetadataEditorViewModel vm = new(_epub3);
        vm.InsertElement("dc:creator", "Jane Doe");

        vm.CancelCommand.Execute(null);

        vm.Accepted.Should().BeFalse();
        _epub3.GetOpf().GetOpfDocument().Metadata.Should().NotContain(m => m.Name == "dc:creator");
    }
}
