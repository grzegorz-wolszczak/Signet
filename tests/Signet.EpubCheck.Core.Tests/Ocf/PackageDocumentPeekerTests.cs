using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Ocf;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

public sealed class PackageDocumentPeekerTests
{
    [Fact]
    public void Peek_OpfThreeMetadata_ReturnsVersionTypesAndUniqueIdentifier()
    {
        // Arrange
        const string opf = "<package version=\"3.0\" unique-identifier=\"book\" xmlns=\"http://www.idpf.org/2007/opf\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"book\"> urn:test </dc:identifier><dc:type> edupub </dc:type></metadata></package>";
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(opf));

        // Act
        PackageDocumentInfo result = PackageDocumentPeeker.Peek(stream);

        // Assert
        result.Version.Should().Be(EpubVersion.Version3);
        result.UniqueIdentifier.Should().Be("urn:test");
        result.Types.Should().ContainSingle().Which.Should().Be("edupub");
        result.Error.Should().BeNull();
    }
}
