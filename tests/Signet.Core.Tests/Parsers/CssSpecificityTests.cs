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
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector("p");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 0, 0, 1));
    }

    [Fact]
    public void ForSelector_counts_a_single_class_selector()
    {
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector(".note");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 0, 1, 0));
    }

    [Fact]
    public void ForSelector_counts_a_single_id_selector()
    {
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector("#chapter1");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 1, 0, 0));
    }

    [Fact]
    public void ForSelector_counts_attribute_and_pseudo_class_as_class_level()
    {
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector("a[href]:hover");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 0, 2, 1));
    }

    [Fact]
    public void ForSelector_counts_pseudo_element_as_type_level()
    {
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector("p::first-line");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 0, 0, 2));
    }

    [Fact]
    public void ForSelector_ignores_attribute_value_contents_when_counting_id_and_class()
    {
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector("[data-x=\"#a.b\"]");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 0, 1, 0));
    }

    [Fact]
    public void ForSelector_combines_element_class_and_id_correctly()
    {
        // Act
        CssSpecificity spec = CssSpecificity.ForSelector("div.note#foo");

        // Assert
        spec.Should().Be(new CssSpecificity(0, 1, 1, 1));
    }

    [Fact]
    public void Id_beats_any_number_of_classes()
    {
        // Act
        CssSpecificity manyClasses = CssSpecificity.ForSelector(".a.b.c.d.e.f.g.h.i.j");
        CssSpecificity oneId = CssSpecificity.ForSelector("#x");

        // Act & Assert
        (oneId > manyClasses).Should().BeTrue();
    }

    [Fact]
    public void Inline_style_beats_any_selector_based_specificity()
    {
        // Act
        CssSpecificity huge = CssSpecificity.ForSelector("#a#b#c.d.e.f div span p");

        // Act & Assert
        (CssSpecificity.Inline > huge).Should().BeTrue();
    }
}
