using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Xml;
using Xunit;
using static Signet.EpubCheck.Core.Xml.ValidationContextPredicates;

namespace Signet.EpubCheck.Core.Tests.Xml;

public sealed class ValidatorMapTests
{
    [Fact]
    public void GetValidators_GivesTheSchemasWhosePredicateHolds_InTheOrderTheyWereAdded()
    {
        // Arrange
        ValidatorMap map = ValidatorMap.CreateBuilder()
            .PutAll(Version(EpubVersion.Version3), XmlValidators.Nav30Rnc, XmlValidators.Nav30Sch)
            .PutAll(Version(EpubVersion.Version2), XmlValidators.NcxRng)
            .PutAll(XmlValidators.Xhtml30Sch)
            .Build();
        ValidationContext context = Context(EpubVersion.Version3);

        // Act
        System.Collections.Generic.IReadOnlyList<XmlValidator> validators = map.GetValidators(context);

        // Assert
        validators.Should().Equal(XmlValidators.Nav30Rnc.Get(), XmlValidators.Nav30Sch.Get(), XmlValidators.Xhtml30Sch.Get());
    }

    [Fact]
    public void GetValidators_SchemaAddedWithSeveralPredicates_AppliesWhenAnyHolds_AtItsFirstPosition()
    {
        // Arrange
        ValidatorMap map = ValidatorMap.CreateBuilder()
            .PutAll(Version(EpubVersion.Version2), XmlValidators.Idunique20Sch)
            .PutAll(Version(EpubVersion.Version3), XmlValidators.Nav30Sch)
            .PutAll(MimeType(ContentDocumentHarness.Svg), XmlValidators.Idunique20Sch)
            .Build();

        // Act
        System.Collections.Generic.IReadOnlyList<XmlValidator> validators = map.GetValidators(Context(EpubVersion.Version3, ContentDocumentHarness.Svg));

        // Assert
        validators.Should().Equal(XmlValidators.Idunique20Sch.Get(), XmlValidators.Nav30Sch.Get());
    }

    [Fact]
    public void Predicates_CombineWithAndOrNot()
    {
        // Arrange
        ValidationContext context = Context(EpubVersion.Version3) with { Profile = EpubProfile.Edupub };

        // Act & Assert
        And(Profile(EpubProfile.Edupub), Version(EpubVersion.Version3))(context).Should().BeTrue();
        And(Profile(EpubProfile.Edupub), Version(EpubVersion.Version2))(context).Should().BeFalse();
        Or(Profile(EpubProfile.Dict), Version(EpubVersion.Version3))(context).Should().BeTrue();
        Not(Profile(EpubProfile.Edupub))(context).Should().BeFalse();
        HasPubType(PublicationType.Edupub)(context).Should().BeFalse();
        HasProp(EpubCheckVocab.Property.NonLinear)(context).Should().BeFalse();
    }

    private static ValidationContext Context(EpubVersion version, string mimeType = ContentDocumentHarness.Xhtml) => new()
    {
        Url = Url.Parse("file:///doc.xhtml"),
        Report = new CollectingReport(),
        Version = version,
        MimeType = mimeType,
    };
}
