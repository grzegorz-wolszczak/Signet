using System.Globalization;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.Core.Themes;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The script that highlights the caret location in the preview (<see cref="PreviewHighlightScript"/>).</summary>
public sealed class PreviewHighlightScriptTests
{
    private static readonly ThemeColor Yellow = ThemeColor.Parse("#FFEB3B");

    [Theory]
    [AutoData]
    public void Default_highlights_block_background_with_translucent_yellow_and_stays(int loc)
    {
        // Act
        string js = PreviewHighlightScript.Build(loc, PreviewHighlight.Default, Yellow);

        // Assert
        js.Should().Contain($"[data-signet-loc=\"{loc.ToString(CultureInfo.InvariantCulture)}\"]")
            .And.Contain(".closest('" + PreviewHighlightScript.AncestorSelector + "')")
            .And.Contain("el.style.backgroundColor='rgba(255,235,59,0.45)'")
            .And.Contain("scrollIntoView")
            .And.NotContain("setTimeout(")
            .And.NotContain("px solid");
    }

    [Theory]
    [AutoData]
    public void Without_scroll_the_element_is_highlighted_where_it_is(int loc)
    {
        // Act
        string js = PreviewHighlightScript.Build(loc, PreviewHighlight.Default, Yellow, scroll: false);

        // Assert
        js.Should().Contain("el.style.backgroundColor=").And.NotContain("scrollIntoView");
    }

    [Fact]
    public void Outline_style_draws_frame_of_configured_width_and_color()
    {
        // Arrange
        PreviewHighlight h = PreviewHighlight.Default with
        {
            Style = PreviewHighlightStyle.Outline, OutlineWidth = 5,
        };

        // Act
        string js = PreviewHighlightScript.Build(1, h, ThemeColor.Parse("#112233"));

        // Assert
        js.Should().Contain("el.style.outline='5px solid #112233'").And.NotContain("rgba(");
    }

    [Fact]
    public void Background_uses_color_and_opacity_with_invariant_decimal_point()
    {
        // Arrange
        PreviewHighlight h = PreviewHighlight.Default with { OpacityPercent = 50 };
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
        try
        {
            // Act & Assert
            PreviewHighlightScript.Build(1, h, ThemeColor.Parse("#800080FF")).Should().Contain("rgba(0,128,255,0.5)",
                "the opacity is the setting's, not the color's");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(PreviewHighlightStyle.Background, "el.style.backgroundColor='transparent'")]
    [InlineData(PreviewHighlightStyle.Outline, "el.style.outlineColor='transparent'")]
    public void Auto_hide_fades_out_after_configured_delay(PreviewHighlightStyle style, string fade)
    {
        // Arrange
        PreviewHighlight h = PreviewHighlight.Default with { Style = style, AutoHide = true, AutoHideDelayMs = 2500 };

        // Act
        string js = PreviewHighlightScript.Build(1, h, Yellow);

        // Assert
        js.Should().Contain("w.__signetPreviewHighlightTimer=setTimeout(function(){")
            .And.Contain(fade)
            .And.EndWith("},2500);})();");
    }

    [Fact]
    public void Out_of_range_settings_are_normalized()
    {
        // Arrange
        PreviewHighlight h = PreviewHighlight.Default with { Style = PreviewHighlightStyle.Outline, OutlineWidth = 500 };

        // Act & Assert
        PreviewHighlightScript.Build(1, h, Yellow)
            .Should().Contain($"'{PreviewHighlight.OutlineWidthMax}px solid ");
    }
}

/// <summary>The colors a theme forces on the book in the preview (<see cref="PreviewThemeScript"/>).</summary>
public sealed class PreviewThemeScriptTests
{
    [Fact]
    public void Set_colors_become_an_important_style_and_unset_ones_remove_it()
    {
        // Act
        string dark = PreviewThemeScript.Build(ThemeColor.Parse("#1E1E1E"), ThemeColor.Parse("#80D4D4D4"));
        string none = PreviewThemeScript.Build(null, null);

        // Assert
        dark.Should().Contain("html,body{background:rgba(30,30,30,1) !important;}")
            .And.Contain("body,body *{color:rgba(212,212,212,0.502) !important;}")
            .And.Contain(PreviewThemeScript.StyleId);
        none.Should().Contain("old.remove()").And.NotContain("createElement");
    }
}
