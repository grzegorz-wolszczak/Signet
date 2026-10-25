using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// <see cref="CssSyntaxSegments"/>: the CSS of the Live CSS panel cut into pieces by syntax, so that the panel colors
/// it the way Code Editor colors a stylesheet.
/// </summary>
public sealed class CssSyntaxSegmentsTests
{
    [Fact]
    public void The_pieces_put_together_are_the_text()
    {
        // Arrange
        const string Declaration = "font-family: KCaecilia Condensed, serif;";

        // Act
        IReadOnlyList<UsageContextSegment> segments = CssSyntaxSegments.Of(Declaration);

        // Assert
        string.Concat(segments.Select(s => s.Text)).Should().Be(Declaration);
    }

    [Fact]
    public void A_declaration_colors_its_property_and_its_value_apart()
    {
        // Act
        IReadOnlyList<UsageContextSegment> segments = CssSyntaxSegments.Of("margin-top: 18px;");

        // Assert
        segments.Should().Contain(s => s.Text.Contains("margin-top") && s.Format == SyntaxFormat.CssProperty);
        segments.Should().Contain(s => s.Text.Contains("18px") && s.Format == SyntaxFormat.CssValue);
    }

    /// <summary>
    /// A selector is told apart from a declaration: without that, a selector with a pseudo-class reads as
    /// "property: value" to a highlighter that starts a line with no context.
    /// </summary>
    [Fact]
    public void A_selector_is_colored_as_a_selector_even_with_a_pseudo_class()
    {
        // Act
        IReadOnlyList<UsageContextSegment> plain = CssSyntaxSegments.Of("p.rozdzial-podtytul", isSelector: true);
        IReadOnlyList<UsageContextSegment> pseudo = CssSyntaxSegments.Of("a:hover", isSelector: true);

        // Assert
        plain.Should().OnlyContain(s => s.Format == SyntaxFormat.CssSelector);
        string.Concat(pseudo.Select(s => s.Text)).Should().Be("a:hover", "the brace that gives the context is not shown");
        pseudo.Should().NotContain(s => s.Format == SyntaxFormat.CssProperty);
    }

    [Fact]
    public void Empty_text_has_no_pieces()
    {
        // Act & Assert
        CssSyntaxSegments.Of(string.Empty).Should().BeEmpty();
    }
}
