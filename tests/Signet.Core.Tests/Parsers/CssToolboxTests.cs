using System.Linq;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests for <see cref="CssToolbox.FindClassesInSelector"/> — the basis of "Rename Class".</summary>
public sealed class CssToolboxTests
{
    [Fact]
    public void FindClassesInSelector_returns_every_class_with_its_position_including_not()
    {
        const string selector = "p.note, div .box:not(.hidden)";

        CssToolbox.FindClassesInSelector(selector).Should().Equal((2, "note"), (13, "box"), (22, "hidden"));
    }

    [Fact]
    public void FindClassesInSelector_ignores_attribute_selectors_strings_and_numbers()
    {
        const string selector = "a[href$=\".xhtml\"][title='.x'] > span";

        CssToolbox.FindClassesInSelector(selector).Should().BeEmpty();
    }

    [Fact]
    public void FindClassesInSelector_reads_whole_names_with_hyphens_and_underscores()
    {
        CssToolbox.FindClassesInSelector(".a-b_c.-d").Select(c => c.Name).Should().Equal("a-b_c", "-d");
    }
}
