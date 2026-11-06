using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

/// <summary>
/// The cases of upstream's <c>OPFPeekerTest</c> (EPUBCheck 5.4.0) on its fixtures (<c>tests/corpus/epubcheck/opf-peeker</c>).
/// Upstream's test turns the result into messages: an error or a missing version is <c>RSC-005</c>, an XML that is not
/// well formed is also the fatal <c>RSC-016</c> of its parser.
/// </summary>
public sealed class PackageDocumentPeekerTests
{
    [Theory]
    [InlineData("validVersion.opf")]
    [InlineData("singleDCType.opf")]
    [InlineData("uniqueId.opf")]
    public void Peek_ValidPackage_ReportsNothing(string file)
    {
        // Act
        PackageDocumentInfo info = Peek(file);

        // Assert
        Messages(info).Should().BeEmpty();
    }

    [Theory]
    [InlineData("noPackageElement.opf", PackageDocumentPeeker.PackageElementNotFound)]
    [InlineData("noVersion.opf", PackageDocumentPeeker.VersionAttributeNotFound)]
    [InlineData("spacesBetweenQuotes.opf", PackageDocumentPeeker.UnsupportedVersion)]
    [InlineData("spacesInValue.opf", PackageDocumentPeeker.UnsupportedVersion)]
    [InlineData("version123.323.opf", PackageDocumentPeeker.UnsupportedVersion)]
    [InlineData("noPointInValue.opf", PackageDocumentPeeker.UnsupportedVersion)]
    [InlineData("negativeVersion.opf", PackageDocumentPeeker.UnsupportedVersion)]
    public void Peek_BadVersion_IsRsc005(string file, string error)
    {
        // Act
        PackageDocumentInfo info = Peek(file);

        // Assert
        info.Error.Should().Be(error);
        Messages(info).Should().Equal(MessageId.RSC_005);
    }

    [Theory]
    [InlineData("noEqual.opf")]
    [InlineData("valueWithoutQuotes.opf")]
    public void Peek_NotWellFormed_IsRsc016AndRsc005(string file)
    {
        // Act
        PackageDocumentInfo info = Peek(file);

        // Assert
        info.Version.Should().BeNull();
        Messages(info).Should().Equal(MessageId.RSC_016, MessageId.RSC_005);
    }

    [Theory]
    [InlineData("singleDCType.opf", new[] { PublicationType.Edupub })]
    [InlineData("multipleDCType.opf", new[] { PublicationType.Edupub, PublicationType.Index })]
    [InlineData("collectionDCType.opf", new[] { PublicationType.Edupub })] // only the top-level types
    [InlineData("whitespaceInDCType.opf", new[] { PublicationType.Edupub })]
    public void Peek_Types_AreTheKnownTopLevelOnes(string file, PublicationType[] expected)
    {
        // Act
        PackageDocumentInfo info = Peek(file);

        // Assert
        info.Types.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("uniqueId.opf", "foo")]
    [InlineData("emptyId.opf", null)]
    [InlineData("missingId.opf", null)]
    [InlineData("multipleIds.opf", "foo")]
    [InlineData("whitespaceInDCIdentifier.opf", "foo")]
    public void Peek_UniqueIdentifier_IsTheNamedIdentifier(string file, string? expected)
    {
        // Act
        PackageDocumentInfo info = Peek(file);

        // Assert
        info.UniqueIdentifier.Should().Be(expected);
    }

    private static PackageDocumentInfo Peek(string file)
    {
        using FileStream stream = File.OpenRead(UpstreamCorpus.Combine("opf-peeker", file));
        return PackageDocumentPeeker.Peek(stream);
    }

    // What upstream's retrieveData reports, in the order of its report (fatal errors are listed with the errors here).
    private static List<MessageId> Messages(PackageDocumentInfo info)
    {
        List<MessageId> messages = [];
        if (info.WellFormednessError is not null)
        {
            messages.Add(MessageId.RSC_016);
        }

        if (info.Error is not null || info.Version is null)
        {
            messages.Add(MessageId.RSC_005);
        }

        return messages;
    }
}
