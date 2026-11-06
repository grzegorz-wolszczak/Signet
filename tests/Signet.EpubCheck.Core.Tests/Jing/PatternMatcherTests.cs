using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>The port of Jing's <c>PatternMatcherTest</c>.</summary>
public sealed class PatternMatcherTests
{
    private static readonly Name s_root = new(string.Empty, "root");
    private static readonly Name s_foo = new(string.Empty, "foo");
    private static readonly Name s_bar = new(string.Empty, "bar");
    private static readonly FixedLocator s_loc = new(null, FixedLocator.Unknown, FixedLocator.Unknown);
    private static readonly JingLocalizer s_localizer = JingLocalizer.ForPattern(CultureInfo.GetCultureInfo("en"));

    [Fact]
    public void PossibleStartTagNames_ChoiceOfTwoElements_AreBoth()
    {
        // Arrange
        SchemaPatternBuilder spb = new();
        PatternMatcher matcher = RootMatcher(spb, spb.MakeChoice(
            spb.MakeElement(new SimpleNameClass(s_foo), spb.MakeEmpty(), s_loc),
            spb.MakeElement(new SimpleNameClass(s_bar), spb.MakeEmpty(), s_loc)));

        // Act
        NormalizedNameClass names = matcher.PossibleStartTagNames();

        // Assert
        names.Should().Be(new NormalizedNsNameClass(new HashSet<Name> { s_foo, s_bar }, new Dictionary<string, HashSet<string>>()));
    }

    [Fact]
    public void PossibleAttributeNames_GroupOfTwoAttributes_AreBoth()
    {
        // Arrange
        SchemaPatternBuilder spb = new();
        PatternMatcher matcher = RootMatcher(spb, spb.MakeElement(new SimpleNameClass(s_root),
            spb.MakeGroup(spb.MakeAttribute(new SimpleNameClass(s_foo), spb.MakeText(), s_loc),
                spb.MakeAttribute(new SimpleNameClass(s_bar), spb.MakeText(), s_loc)),
            s_loc));
        matcher.MatchStartTagOpen(s_root, string.Empty, new ValidatorContext()).Should().BeTrue();

        // Act
        NormalizedNameClass names = matcher.PossibleAttributeNames();

        // Assert
        names.Should().Be(new NormalizedNsNameClass(new HashSet<Name> { s_foo, s_bar }, new Dictionary<string, HashSet<string>>()));
    }

    [Fact]
    public void ErrorMessage_MissingAttributeInTheDefaultNamespaceOfElements_GivesItsNamespace()
    {
        // Arrange
        SchemaPatternBuilder spb = new();
        Name namespacedFoo = new("http://example.com/", "foo");
        PatternMatcher matcher = RootMatcher(spb, spb.MakeElement(new SimpleNameClass(s_root),
            spb.MakeAttribute(new SimpleNameClass(namespacedFoo), spb.MakeText(), s_loc), s_loc));
        ValidatorContext context = new();
        context.StartPrefixMapping(string.Empty, "http://example.com/");
        matcher.MatchStartTagOpen(s_root, string.Empty, context).Should().BeTrue();

        // Act
        bool ok = matcher.MatchStartTagClose(s_root, string.Empty, context);

        // Assert
        ok.Should().BeFalse();

        // Before Jing fixed its issue 105 the message was: element "root" missing required attribute "foo".
        matcher.ErrorMessage.Should().Be("element \"root\" missing required attribute \"ns:foo\" (with xmlns:ns=\"http://example.com/\")");
    }

    private static PatternMatcher RootMatcher(SchemaPatternBuilder spb, Pattern start)
    {
        PatternMatcher matcher = new(start, new ValidatorPatternBuilder(spb), s_localizer);
        matcher.MatchStartDocument().Should().BeTrue();
        return matcher;
    }
}

/// <summary>The port of Jing's <c>NameClassNormalizerTest</c>.</summary>
public sealed class NameClassNormalizerTests
{
    private const string Ns1 = "http://www.example.com/1";

    public static TheoryData<int> Pairs => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Normalize_GivesTheExpectedNormalizedClass(int index)
    {
        // Arrange
        (NameClass nc, NormalizedNameClass expected) = Pair(index);

        // Act
        NormalizedNameClass normalized = new NameClassNormalizer(nc).Normalize();

        // Assert
        normalized.Should().Be(expected);
    }

    private static (NameClass, NormalizedNameClass) Pair(int index)
    {
        Name foo = new(string.Empty, "foo");
        Name ns1Foo = new(Ns1, "foo");
        NormalizedNameClass ns1Nnc = new NormalizedNsNameClass(
            new HashSet<Name>(), new Dictionary<string, HashSet<string>> { [Ns1] = [] });
        NormalizedNameClass anyNnc = new NormalizedAnyNameClass(new HashSet<Name>(), new HashSet<string>(), new HashSet<Name>());
        NormalizedNameClass fooNnc = new NormalizedNsNameClass(new HashSet<Name> { foo }, new Dictionary<string, HashSet<string>>());
        NormalizedNameClass ns1FooNnc = new NormalizedNsNameClass(new HashSet<Name> { ns1Foo }, new Dictionary<string, HashSet<string>>());
        NormalizedNameClass emptyNnc = new NormalizedNsNameClass(new HashSet<Name>(), new Dictionary<string, HashSet<string>>());
        (NameClass, NormalizedNameClass)[] pairs =
        [
            (new SimpleNameClass(foo), fooNnc),
            (new ChoiceNameClass(new SimpleNameClass(foo), new SimpleNameClass(foo)), fooNnc),
            (new AnyNameClass(), anyNnc),
            (new AnyNameExceptNameClass(new AnyNameClass()), emptyNnc),
            (new NsNameClass(Ns1), ns1Nnc),
            (new ChoiceNameClass(new SimpleNameClass(foo), new AnyNameClass()), anyNnc),
            (new ChoiceNameClass(new NsNameClass(Ns1), new AnyNameClass()), anyNnc),
            (new NsNameExceptNameClass(Ns1, new AnyNameClass()), emptyNnc),
            (new NsNameExceptNameClass(Ns1, new NsNameClass(Ns1)), emptyNnc),
            (new NsNameExceptNameClass(Ns1, new SimpleNameClass(foo)), ns1Nnc),
            (new NsNameExceptNameClass(Ns1, new NsNameExceptNameClass(Ns1, new SimpleNameClass(ns1Foo))), ns1FooNnc),
        ];
        return pairs[index];
    }
}
