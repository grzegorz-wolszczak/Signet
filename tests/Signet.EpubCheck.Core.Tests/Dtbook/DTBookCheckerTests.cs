using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Dtbook;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Dtbook;

/// <summary>The DTBook content documents of EPUB 2: ids, links and images.</summary>
public sealed class DTBookCheckerTests
{
    private const string DtBook = "application/x-dtbook+xml";

    [Fact]
    public void Constructor_ResourceThatIsNotDtBook_Fails()
    {
        // Arrange
        ContentDocumentHarness harness = new("book.xml", "<x/>", ContentDocumentHarness.Xhtml) { Version = EpubVersion.Version2 };
        harness.Build();

        // Act & Assert
        FluentActions.Invoking(() => new DTBookChecker(harness.Context)).Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Check_LinksAndImages_AreRegistered()
    {
        // Arrange
        ContentDocumentHarness harness = Book("<p><a href=\"other.xml#x\">a</a> <img src=\"a.png\" alt=\"\"/></p>")
            .With("other.xml", DtBook).With("a.png", "image/png");

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => (System.IO.Path.GetFileName(reference.Url.Path), reference.Type)).Should().BeEquivalentTo(
        [
            ("other.xml", ReferenceType.Generic),
            ("a.png", ReferenceType.Image),
        ]);
    }

    [Fact]
    public void Check_Ids_AreRegistered()
    {
        // Arrange
        ContentDocumentHarness harness = Book("<p id=\"first\">a</p>");

        // Act
        Check(harness);

        // Assert
        Resource resource = harness.Context.ResourceRegistry!.GetResource(harness.Context.Url)!;
        harness.Context.ResourceRegistry.GetIdPosition("first", resource).Should().BePositive();
    }

    [Fact]
    public void Check_ExternalLinkWithAKnownScheme_IsAFeatureOnly()
    {
        // Arrange
        ContentDocumentHarness harness = Book("<p><a external=\"true\" href=\"https://example.org/x\">a</a></p>");

        // Act
        Check(harness);

        // Assert
        harness.References.Should().BeEmpty();
        harness.Report.Features.Should().Contain(feature => feature.Feature == FeatureKind.Reference && feature.Value == "https://example.org/x");
    }

    [Fact]
    public void Check_ExternalLinkWithAnUnknownScheme_ReportsOpf021()
    {
        // Arrange
        ContentDocumentHarness harness = Book("<p><a external=\"true\" href=\"zzz://example.org/x\">a</a></p>");

        // Act
        Check(harness);

        // Assert
        harness.Messages(MessageId.OPF_021).Should().ContainSingle();
    }

    [Fact]
    public void Check_RemoteLinkNotDeclaredExternal_IsRegisteredAndReported()
    {
        // Arrange
        ContentDocumentHarness harness = Book("<p><a href=\"https://example.org/x\">a</a></p>");

        // Act
        Check(harness);

        // Assert
        harness.References.Should().ContainSingle();
        harness.Report.Features.Should().Contain(feature => feature.Feature == FeatureKind.Reference);
    }

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new DTBookChecker(harness.Context).Check();
    }

    private static ContentDocumentHarness Book(string content) =>
        new(
            "book.xml",
            "<?xml version=\"1.0\"?><dtbook xmlns=\"http://www.daisy.org/z3986/2005/dtbook/\" version=\"2005-2\" xml:lang=\"en\"><head><meta name=\"dtb:uid\" content=\"x\"/></head>"
            + $"<book><bodymatter><level1>{content}</level1></bodymatter></book></dtbook>",
            DtBook)
        {
            Version = EpubVersion.Version2,
        };
}
