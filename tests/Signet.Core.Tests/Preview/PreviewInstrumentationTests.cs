using System.Globalization;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Preview;
using Xunit;

namespace Signet.Core.Tests.Preview;

/// <summary>Tests for <see cref="PreviewInstrumentation"/> — Code View ↔ Preview position mapping.</summary>
public sealed class PreviewInstrumentationTests
{
    private const string Sample =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><title>T</title></head>\n"
        + "<body>\n"
        + "  <h1>Nagłówek</h1>\n"
        + "  <p>Pierwszy akapit.</p>\n"
        + "  <p>Drugi <em>akapit</em>.</p>\n"
        + "</body>\n"
        + "</html>\n";

    [Fact]
    public void Create_TagsEveryElementWithSourceOffset()
    {
        PreviewInstrumentation result = PreviewInstrumentation.Create(Sample);

        result.Locs.Should().NotBeEmpty();
        result.Locs.Should().BeInAscendingOrder();

        IHtmlDocument instrumented = XhtmlDoc.Parse(result.Html);
        IHtmlDocument original = XhtmlDoc.Parse(Sample);

        foreach (IElement element in original.All)
        {
            int offset = XhtmlDoc.OffsetFromNode(element);
            if (offset < 0)
            {
                continue;
            }

            instrumented.QuerySelector($"[{PreviewInstrumentation.LocAttribute}=\"{offset}\"]")
                .Should().NotBeNull($"element <{element.LocalName}> at offset {offset} should be tagged");
        }
    }

    [Fact]
    public void Create_LocValuesPointAtOpeningTagInSource()
    {
        PreviewInstrumentation result = PreviewInstrumentation.Create(Sample);

        foreach (int loc in result.Locs)
        {
            Sample[loc].Should().Be('<', "the offset should point at the '<' of the opening tag");
        }
    }

    [Fact]
    public void NearestLocAtOrBefore_ReturnsLargestLocNotAfterOffset()
    {
        PreviewInstrumentation result = PreviewInstrumentation.Create(Sample);
        int[] locs = result.Locs.ToArray();

        // Exact hit.
        result.NearestLocAtOrBefore(locs[2]).Should().Be(locs[2]);

        // Inside an element (offset between two tags) → the earlier loc.
        int between = locs[2] + 1;
        result.NearestLocAtOrBefore(between).Should().Be(locs[2]);

        // Far past the end → the last loc.
        result.NearestLocAtOrBefore(Sample.Length + 100).Should().Be(locs[^1]);
    }

    [Fact]
    public void NearestLocAtOrBefore_OffsetBeforeFirstElement_ReturnsMinusOne()
    {
        PreviewInstrumentation result = PreviewInstrumentation.Create(Sample);

        result.NearestLocAtOrBefore(0).Should().Be(-1);
    }

    [Fact]
    public void Create_PlainText_ProducesUsableResult()
    {
        const string plain = "tylko goły tekst bez znaczników";

        PreviewInstrumentation result = PreviewInstrumentation.Create(plain);

        result.Html.Should().Contain(plain);
        result.Locs.Should().BeInAscendingOrder();
        result.NearestLocAtOrBefore(0).Should().BeGreaterThanOrEqualTo(-1);
    }

    [Fact]
    public void Create_IsIdempotentOnLocValues()
    {
        PreviewInstrumentation once = PreviewInstrumentation.Create(Sample);
        PreviewInstrumentation twice = PreviewInstrumentation.Create(Sample);

        twice.Locs.Should().Equal(once.Locs);
        once.Locs.Select(l => l.ToString(CultureInfo.InvariantCulture)).Should().OnlyHaveUniqueItems();
    }
}
