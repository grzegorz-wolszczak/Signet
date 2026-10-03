using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>
/// Tests for <see cref="CssSpecificity"/> — the specificity engine behind the Live CSS panel.
/// </summary>
public sealed class CssSpecificityTests
{
    [Fact]
    public void ForSelector_counts_a_single_type_selector()
    {
        CssSpecificity spec = CssSpecificity.ForSelector("p");

        spec.Should().Be(new CssSpecificity(0, 0, 0, 1));
    }

    [Fact]
    public void ForSelector_counts_a_single_class_selector()
    {
        CssSpecificity spec = CssSpecificity.ForSelector(".note");

        spec.Should().Be(new CssSpecificity(0, 0, 1, 0));
    }

    [Fact]
    public void ForSelector_counts_a_single_id_selector()
    {
        CssSpecificity spec = CssSpecificity.ForSelector("#chapter1");

        spec.Should().Be(new CssSpecificity(0, 1, 0, 0));
    }

    [Fact]
    public void ForSelector_counts_attribute_and_pseudo_class_as_class_level()
    {
        CssSpecificity spec = CssSpecificity.ForSelector("a[href]:hover");

        spec.Should().Be(new CssSpecificity(0, 0, 2, 1));
    }

    [Fact]
    public void ForSelector_counts_pseudo_element_as_type_level()
    {
        CssSpecificity spec = CssSpecificity.ForSelector("p::first-line");

        spec.Should().Be(new CssSpecificity(0, 0, 0, 2));
    }

    [Fact]
    public void ForSelector_ignores_attribute_value_contents_when_counting_id_and_class()
    {
        CssSpecificity spec = CssSpecificity.ForSelector("[data-x=\"#a.b\"]");

        spec.Should().Be(new CssSpecificity(0, 0, 1, 0));
    }

    [Fact]
    public void ForSelector_combines_element_class_and_id_correctly()
    {
        CssSpecificity spec = CssSpecificity.ForSelector("div.note#foo");

        spec.Should().Be(new CssSpecificity(0, 1, 1, 1));
    }

    [Fact]
    public void Id_beats_any_number_of_classes()
    {
        CssSpecificity manyClasses = CssSpecificity.ForSelector(".a.b.c.d.e.f.g.h.i.j");
        CssSpecificity oneId = CssSpecificity.ForSelector("#x");

        (oneId > manyClasses).Should().BeTrue();
    }

    [Fact]
    public void Inline_style_beats_any_selector_based_specificity()
    {
        CssSpecificity huge = CssSpecificity.ForSelector("#a#b#c.d.e.f div span p");

        (CssSpecificity.Inline > huge).Should().BeTrue();
    }
}
