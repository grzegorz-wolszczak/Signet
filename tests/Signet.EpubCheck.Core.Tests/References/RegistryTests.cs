using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.References;

/// <summary>The registries of resources and references, and the report of features.</summary>
public sealed class RegistryTests
{
    private static readonly EpubLocation s_location = new("EPUB/a.xhtml", 1, 1);

    [Fact]
    public void RegisterReference_UrlWithAQueryToAResourceOfTheContainer_LosesTheQuery()
    {
        // Arrange
        (ReferenceRegistry references, OCFContainer container) = Registries();

        // Act
        references.RegisterReference(container.RootUrl.Resolve("a.png?x=1#f"), ReferenceType.Image, s_location);

        // Assert
        references.AsList().Should().ContainSingle().Which.Url.ToString().Should().EndWith("a.png#f");
    }

    [Fact]
    public void RegisterReference_RemoteUrl_KeepsItsQuery()
    {
        // Arrange
        (ReferenceRegistry references, _) = Registries();

        // Act
        references.RegisterReference(Url.Parse("https://example.org/a.png?x=1"), ReferenceType.Image, s_location);

        // Assert
        references.AsList().Should().ContainSingle().Which.Url.Query.Should().Be("x=1");
    }

    [Fact]
    public void RegisterReference_DataUrl_RegistersItsResource()
    {
        // Arrange
        ResourceRegistry resources = new();
        (ReferenceRegistry references, OCFContainer container) = Registries(resources);
        Url data = Url.Parse("data:image/png;base64,AAAA");

        // Act
        references.RegisterReference(data, ReferenceType.Image, s_location);

        // Assert
        resources.GetMimeType(data).Should().Be("image/png");
        container.IsRemote(data).Should().BeFalse();
    }

    [Fact]
    public void HasReferencesTo_Resource_IgnoresTheFragment()
    {
        // Arrange
        (ReferenceRegistry references, OCFContainer container) = Registries();
        references.RegisterReference(container.RootUrl.Resolve("a.xhtml#top"), ReferenceType.Hyperlink, s_location);

        // Act & Assert
        references.HasReferencesTo(container.RootUrl.Resolve("a.xhtml")).Should().BeTrue();
        references.IsReferenced(container.RootUrl.Resolve("a.xhtml")).Should().BeFalse();
        references.HasReferencesTo(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(ReferenceType.Generic, true)]
    [InlineData(ReferenceType.Stylesheet, true)]
    [InlineData(ReferenceType.MediaOverlay, true)]
    [InlineData(ReferenceType.Hyperlink, false)]
    [InlineData(ReferenceType.Link, false)]
    [InlineData(ReferenceType.NavTocLink, false)]
    public void IsPublicationResourceReference_DependsOnTheType(ReferenceType type, bool expected)
    {
        // Act & Assert
        type.IsPublicationResourceReference().Should().Be(expected);
    }

    [Fact]
    public void RegisterId_IdOfARegisteredResource_HasAPositionAndAType()
    {
        // Arrange
        ResourceRegistry resources = new();
        Url url = Url.Parse("https://x.example/a.xhtml");
        resources.RegisterResource(url, "application/xhtml+xml");
        resources.RegisterId("one", ReferenceType.Hyperlink, url);
        resources.RegisterId("two", ReferenceType.SvgSymbol, url);
        resources.RegisterId("one", ReferenceType.Image, url);
        Resource resource = resources.GetResource(url)!;

        // Act & Assert
        resources.GetIdPosition("two", resource).Should().Be(2);
        resources.GetIdPosition("none", resource).Should().Be(-1);
        resources.GetIdPosition("  ", resource).Should().Be(0);
        resources.GetIdType("one", resource).Should().Be(ReferenceType.Hyperlink);
    }

    [Fact]
    public void RegisterId_ResourceThatIsNotRegistered_Throws()
    {
        // Act & Assert
        new ResourceRegistry().Invoking(r => r.RegisterId("id", ReferenceType.Hyperlink, Url.Parse("https://x.example/a"))).Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void RegisterResource_ResourceRegisteredBefore_IsNotReplaced()
    {
        // Arrange
        ResourceRegistry resources = new();
        Url url = Url.Parse("https://x.example/a");

        // Act
        resources.RegisterResource(url, "text/css");
        resources.RegisterResource(url, "text/html");

        // Assert
        resources.GetMimeType(url).Should().Be("text/css");
        resources.GetOpfItem(url).Should().BeNull();
        resources.GetResource(url)!.HasItem.Should().BeFalse();
    }

    [Fact]
    public void FeatureReport_Features_AreKeptByKindWithoutDuplicates()
    {
        // Arrange
        FeatureReport report = new();

        // Act
        report.Report(FeatureKind.PageList, s_location);
        report.Report(FeatureKind.PageList, s_location);
        report.Report(FeatureKind.UniqueIdent, s_location, "urn:x");
        report.Report(FeatureKind.UniqueIdent, s_location, "");

        // Assert
        report.HasFeature(FeatureKind.PageList).Should().BeTrue();
        report.HasFeature(FeatureKind.Loa).Should().BeFalse();
        report.GetFeature(FeatureKind.PageList).Should().ContainSingle();
        report.GetFeature(FeatureKind.UniqueIdent).Select(f => f.Value).Should().Equal("urn:x", null);
    }

    private static (ReferenceRegistry References, OCFContainer Container) Registries(ResourceRegistry? resources = null)
    {
        MemoryEpubSource source = new([new KeyValuePair<string, byte[]>("a.png", Encoding.UTF8.GetBytes("x"))]);
        OCFContainer.Builder builder = new(source);
        builder.AddResource(source.Entries[0]);
        OCFContainer container = builder.Build();
        return (new ReferenceRegistry(container, resources ?? new ResourceRegistry()), container);
    }
}
