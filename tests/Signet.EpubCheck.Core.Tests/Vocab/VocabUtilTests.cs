using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Vocab;

/// <summary>The cases of upstream's <c>VocabTest</c> and <c>PropertyTest</c> (EPUBCheck 5.4.0), on the same vocabularies.</summary>
public sealed class VocabUtilTests
{
    private const string XhtmlMimeType = "application/xhtml+xml";

    private static readonly IVocab s_fooBar = new EnumVocab<FooBar>("http://example.org/foobar#", null,
        [(FooBar.Foo, "foo", PropertyStatus.Allowed), (FooBar.Bar, "bar", PropertyStatus.Allowed), (FooBar.FooBar, "foo-bar", PropertyStatus.Allowed)]);

    private static readonly IVocab s_numbers = new EnumVocab<Numbers>("http://example.org/number#", "num",
        [(Numbers.One, "one", PropertyStatus.Allowed), (Numbers.Two, "two", PropertyStatus.Allowed), (Numbers.Three, "three", PropertyStatus.Allowed)]);

    private static readonly IVocab s_camel = new EnumVocab<Single>("http://example.org/camel#", "camel", [(Single.Prop, "fooBar", PropertyStatus.Allowed)]);
    private static readonly IVocab s_deprecated = new EnumVocab<Single>("http://example.org/deprecated#", "deprecated", [(Single.Prop, "prop", PropertyStatus.Deprecated)]);
    private static readonly IVocab s_disallowed = new EnumVocab<Single>("http://example.org/disallowed#", "disallowed", [(Single.Prop, "prop", PropertyStatus.DisallowedInXhtml)]);
    private static readonly IVocab s_outdated = new EnumVocab<Single>("http://example.org/outdated#", "outdated", [(Single.Prop, "prop", PropertyStatus.Outdated)]);
    private static readonly IVocab s_bazUnchecked = new UncheckedVocab("http://example.org/number#baz", "baz");

    private static readonly Dictionary<string, IVocab> s_predefined = new()
    {
        [string.Empty] = s_fooBar,
        ["num"] = s_numbers,
        ["baz"] = s_bazUnchecked,
        ["camel"] = s_camel,
        ["deprecated"] = s_deprecated,
        ["disallowed"] = s_disallowed,
        ["outdated"] = s_outdated,
    };

    private static readonly Dictionary<string, IVocab> s_known = new()
    {
        ["http://example.org/foobar#"] = s_fooBar,
        ["http://example.org/number#"] = s_numbers,
        ["http://example.org/number#baz"] = s_bazUnchecked,
    };

    private static readonly HashSet<string> s_forbidden = ["http://example.org/default#", "http://example.org/forbidden#"];

    private enum FooBar
    {
        Foo,
        Bar,
        FooBar,
    }

    private enum Numbers
    {
        One,
        Two,
        Three,
    }

    private enum Single
    {
        Prop,
    }

    private enum Letters
    {
        A,
        B,
        C,
    }

    [Fact]
    public void ParsePropertyList_WithoutVocabs_Throws()
    {
        // Arrange
        VocabContext context = new(VocabTestSupport.NewReport());

        // Act & Assert
        FluentActions.Invoking(() => VocabUtil.ParsePropertyList("prop1 prop2", null!, context, VocabTestSupport.Location))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("foo-bar")]
    [InlineData("camel:fooBar")]
    public void ParseProperty_Known_IsFound(string value)
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        VocabProperty? property = VocabUtil.ParseProperty(value, s_predefined, new VocabContext(report), VocabTestSupport.Location);

        // Assert
        property.Should().NotBeNull();
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void ParseProperty_List_IsOpf025()
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        VocabProperty? property = VocabUtil.ParseProperty("foo bar", s_predefined, new VocabContext(report), VocabTestSupport.Location);

        // Assert
        property.Should().BeNull();
        report.Ids(Severity.Error).Should().Equal(MessageId.OPF_025);
    }

    [Theory]
    [InlineData("deprecated:prop", null, Severity.Warning, MessageId.OPF_086)]
    [InlineData("disallowed:prop", XhtmlMimeType, Severity.Usage, MessageId.OPF_087)]
    [InlineData("outdated:prop", null, Severity.Usage, MessageId.OBS_001)]
    public void ParseProperty_ByStatus_IsReported(string value, string? mimeType, Severity severity, MessageId expected)
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        VocabUtil.ParseProperty(value, s_predefined, new VocabContext(report, mimeType), VocabTestSupport.Location);

        // Assert
        report.Ids(severity).Should().Equal(expected);
        report.Messages.Should().ContainSingle();
    }

    [Fact]
    public void ParseProperty_DisallowedOutsideXhtml_IsAllowed()
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        VocabUtil.ParseProperty("disallowed:prop", s_predefined, new VocabContext(report, "image/svg+xml"), VocabTestSupport.Location);

        // Assert
        report.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("  foo bar  ", new MessageId[0])]
    [InlineData("\tfoo\n\v bar\f\r", new MessageId[0])]
    [InlineData("", new MessageId[0])]
    [InlineData(" \t\n ", new MessageId[0])]
    [InlineData("foo bar", new[] { MessageId.OPF_027 })]
    [InlineData(":world :world :", new[] { MessageId.OPF_026, MessageId.OPF_026, MessageId.OPF_026 })]
    [InlineData("foo num:one num:foo baz foo-bar", new[] { MessageId.OPF_027, MessageId.OPF_027 })]
    [InlineData("foo foo:bar bar:bar num:one", new[] { MessageId.OPF_028, MessageId.OPF_028 })]
    [InlineData("foo baz:foo baz:bar num:one", new MessageId[0])]
    public void ParsePropertyList_ReportsTheErrors(string value, MessageId[] errors)
    {
        // Arrange
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        VocabUtil.ParsePropertyList(value, s_predefined, new VocabContext(report), VocabTestSupport.Location);

        // Assert
        report.Ids(Severity.Error).Should().Equal(errors);
        report.Ids(Severity.Warning).Should().BeEmpty();
    }

    [Fact]
    public void ParsePropertyList_SplitsOnAsciiWhitespaceOnly()
    {
        // Arrange (Java's \s is ASCII: NO-BREAK SPACE is part of the property)
        CollectingReport report = VocabTestSupport.NewReport();

        // Act
        IReadOnlyList<VocabProperty> properties = VocabUtil.ParsePropertyList("foo bar", s_predefined, new VocabContext(report), VocabTestSupport.Location);

        // Assert
        properties.Should().BeEmpty();
        report.Ids(Severity.Error).Should().Equal(MessageId.OPF_027);
    }

    [Fact]
    public void ParsePrefixDeclaration_Null_IsThePredefinedVocabs()
    {
        // Act
        IReadOnlyDictionary<string, IVocab> vocabs = Declare(null, out CollectingReport report);

        // Assert
        vocabs.Should().HaveCount(s_predefined.Count);
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void ParsePrefixDeclaration_NewPrefixes_AreUnchecked()
    {
        // Act
        IReadOnlyDictionary<string, IVocab> vocabs = Declare("hello: http://example.org/hello# world: http://example.org/world#", out CollectingReport report);

        // Assert
        vocabs.Should().HaveCount(s_predefined.Count + 2);
        vocabs["hello"].Should().BeOfType<UncheckedVocab>();
        vocabs["world"].Should().BeOfType<UncheckedVocab>();
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void ParsePrefixDeclaration_RedeclaredPrefix_IsOpf007()
    {
        // Act
        IReadOnlyDictionary<string, IVocab> vocabs = Declare("num: http://example.org/hello#", out CollectingReport report);

        // Assert
        vocabs.Should().HaveCount(s_predefined.Count);
        vocabs["num"].Should().BeOfType<UncheckedVocab>();
        report.Ids(Severity.Warning).Should().Equal(MessageId.OPF_007);
    }

    [Fact]
    public void ParsePrefixDeclaration_KnownVocabUnderAnotherPrefix_IsTheKnownVocab()
    {
        // Act
        IReadOnlyDictionary<string, IVocab> vocabs = Declare("int: http://example.org/number#", out CollectingReport report);

        // Assert
        vocabs.Should().HaveCount(s_predefined.Count + 1);
        vocabs["int"].Should().BeSameAs(s_numbers);
        report.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("_: http://example.org/hello# hello: http://example.org/hello#", "_", MessageId.OPF_007a)]
    [InlineData("default: http://example.org/default# hello: http://example.org/hello#", "default", MessageId.OPF_007b)]
    public void ParsePrefixDeclaration_ForbiddenPrefix_IsReportedAndLeftOut(string value, string prefix, MessageId expected)
    {
        // Act
        IReadOnlyDictionary<string, IVocab> vocabs = Declare(value, out CollectingReport report);

        // Assert
        vocabs.Should().HaveCount(s_predefined.Count + 1).And.ContainKey("hello").And.NotContainKey(prefix);
        report.Ids(Severity.Error).Should().Equal(expected);
    }

    [Fact]
    public void Filter_KeepsTheMembersOfTheEnum()
    {
        // Arrange
        VocabProperty[] properties =
        [
            VocabProperty.Of("foo", "http://example.org/foo#", "foo"),
            VocabProperty.Of("a", "http://example.org/letters#", string.Empty, Letters.A),
            VocabProperty.Of("one", "http://example.org/numbers#", string.Empty, Numbers.One),
            VocabProperty.Of("c", "http://example.org/letters#", string.Empty, Letters.C),
        ];

        // Act
        IReadOnlySet<Letters> letters = VocabProperty.Filter<Letters>(properties);

        // Assert
        letters.Should().BeEquivalentTo([Letters.A, Letters.C]);
    }

    [Fact]
    public void Filter_NoMatchOrNull_IsEmpty()
    {
        // Arrange
        VocabProperty[] properties = [VocabProperty.Of("one", "http://example.org/numbers#", string.Empty, Numbers.One)];

        // Act
        IReadOnlySet<Letters> none = VocabProperty.Filter<Letters>(properties);
        IReadOnlySet<Letters> ofNull = VocabProperty.Filter<Letters>(null);

        // Assert
        none.Should().BeEmpty();
        ofNull.Should().BeEmpty();
    }

    [Fact]
    public void GeneratedVocabs_KeepUpstreamsNamesAndStatuses()
    {
        // Act
        VocabProperty? marc = PackageVocabs.LinkrelVocab.Lookup("marc21xml-record");
        VocabProperty? certified = AccessibilityVocab.MetaVocab.Lookup("certifiedBy");
        VocabProperty? aside = StructureVocab.Vocab.Lookup("aside");

        // Assert
        marc!.IsDeprecated.Should().BeTrue();
        certified!.PrefixedName.Should().Be("a11y:certifiedBy");
        aside!.IsAllowed(XhtmlMimeType).Should().BeFalse();
        RenditionVocabs.SpreadProperties.Should().HaveCount(5);
        PackageVocabs.IsAllowedForType(PackageVocabs.ItemProperty.CoverImage, "image/png").Should().BeTrue();
        PackageVocabs.IsAllowedForType(PackageVocabs.ItemProperty.Nav, "image/svg+xml").Should().BeFalse();
    }

    private static IReadOnlyDictionary<string, IVocab> Declare(string? value, out CollectingReport report)
    {
        report = VocabTestSupport.NewReport();
        return VocabUtil.ParsePrefixDeclaration(value, s_predefined, s_known, s_forbidden, report, VocabTestSupport.Location);
    }
}
