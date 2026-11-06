using System;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Opf;

/// <summary>The metadata of a package: primary and refining metadata, refinement chains and cycles.</summary>
public sealed class MetadataSetTests
{
    private static readonly VocabProperty s_title = DCMESVocab.Vocab.Lookup("title")!;
    private static readonly VocabProperty s_creator = DCMESVocab.Vocab.Lookup("creator")!;
    private static readonly VocabProperty s_role = PackageVocabs.MetaVocab.Get(PackageVocabs.MetaProperty.Role);
    private static readonly VocabProperty s_fileAs = PackageVocabs.MetaVocab.Get(PackageVocabs.MetaProperty.FileAs);

    [Fact]
    public void Build_MetadataWithoutIds_AreAllPrimaryUnlessTheyRefine()
    {
        // Arrange
        MetadataSet.Builder builder = MetadataSet.CreateBuilder();
        builder.Meta(null, s_title, " A title ", null).Meta(null, s_creator, "Me", null).Meta(null, s_role, "aut", "#c");

        // Act
        MetadataSet set = builder.Build();

        // Assert
        set.Primary.Select(m => m.Value).Should().Equal("A title", "Me");
        set.All.Should().HaveCount(3);
        set.ContainsPrimary(s_title, "A title").Should().BeTrue();
        set.ContainsPrimary(s_role).Should().BeFalse();
        set.ContainsAny(s_role).Should().BeTrue();
    }

    [Fact]
    public void Build_RefinementOfAnElementWithAnId_IsFoundFromBothSides()
    {
        // Arrange
        MetadataSet.Builder builder = MetadataSet.CreateBuilder();
        builder.Meta("c", s_creator, "Me", null).Meta(null, s_role, "aut", "#c").Meta("fa", s_fileAs, "Me, Myself", "#c");

        // Act
        MetadataSet set = builder.Build();

        // Assert
        MetadataSet.Metadata creator = set.GetPrimary(s_creator).Single();
        creator.Refiners.Select(r => r.Property).Should().Equal(s_role, s_fileAs);
        set.GetRefinedBy(creator.Refiners[0])!.Should().Be(creator);
        set.GetRefining("c").Select(r => r.Value).Should().Equal("aut", "Me, Myself");
        MetadataSet.TryFindInRefines(set.GetPrimary(s_creator), s_role, "aut").Should().Be(creator);
        MetadataSet.TryFind(set.All, s_role, null)!.Value.Should().Be("aut");
    }

    [Fact]
    public void Build_RefinementOfARefinement_IsChained()
    {
        // Arrange
        MetadataSet.Builder builder = MetadataSet.CreateBuilder();
        builder.Meta("t", s_title, "T", null).Meta("r", s_role, "x", "#t").Meta(null, s_fileAs, "y", "#r");

        // Act
        MetadataSet set = builder.Build();

        // Assert
        MetadataSet.Metadata role = set.GetPrimary(s_title).Single().Refiners.Single();
        role.Refiners.Single().Value.Should().Be("y");
    }

    [Fact]
    public void Build_RefinementsThatFormACycle_Throws()
    {
        // Arrange
        MetadataSet.Builder builder = MetadataSet.CreateBuilder();
        builder.Meta("a", s_role, "x", "#b").Meta("b", s_role, "y", "#a");

        // Act & Assert
        builder.Invoking(b => b.Build()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("#id", "id")]
    [InlineData("#", null)]
    [InlineData("id.xml#x", "id.xml#x")]
    public void Metadata_RefinesAttribute_IsTheIdItNames(string refines, string? expected)
    {
        // Arrange
        MetadataSet.Builder builder = MetadataSet.CreateBuilder();
        builder.Meta(null, s_role, "v", refines);

        // Act
        MetadataSet.Metadata meta = builder.Build().All.Single();

        // Assert
        meta.Refines.Should().Be(expected);
        meta.IsPrimary.Should().Be(expected is null);
    }
}
