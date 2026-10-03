using System;
using System.Globalization;
using System.Text;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Preview page script: scrolls to the <c>[data-signet-loc]</c> element (Code View → Preview
/// synchronization) and highlights its block according to <see cref="PreviewHighlight"/> — with a background or
/// an outline, permanently or fading out after a delay.
/// </summary>
public static class PreviewHighlightScript
{
    /// <summary>
    /// Selector of a "sensible" ancestor to highlight: the element pointed to by <c>data-signet-loc</c>
    /// is often a deeply nested inline tag (e.g. <c>&lt;em&gt;</c>, <c>&lt;span&gt;</c>), so
    /// the nearest block-level text container is highlighted, not the literal target.
    /// </summary>
    public const string AncestorSelector =
        "p, li, h1, h2, h3, h4, h5, h6, blockquote, td, th, div, pre, dd, dt, figcaption, caption";

    // Duration of the smooth fade-out when disappearing (ms).
    private const int FadeMs = 400;

    /// <summary>Builds the script for the element with the given <paramref name="loc"/>.</summary>
    /// <param name="loc">The element's <c>data-signet-loc</c> value.</param>
    /// <param name="highlight">Highlight settings.</param>
    /// <param name="dark">Whether the application uses a dark theme (color selection).</param>
    public static string Build(int loc, PreviewHighlight highlight, bool dark)
    {
        PreviewHighlight h = highlight.Normalized();
        string color = h.ColorFor(dark);
        bool outline = h.Style == PreviewHighlightStyle.Outline;

        StringBuilder js = new();
        js.Append("(function(){var w=window;var el=document.querySelector('[data-signet-loc=\"")
            .Append(loc.ToString(CultureInfo.InvariantCulture))
            .Append("\"]');if(!el)return;el=el.closest('").Append(AncestorSelector).Append("')||el;")
            // Clean up the previous highlight (even one in another style) and its scheduled fade-out.
            .Append("function clear(e){e.style.transition='';e.style.backgroundColor='';")
            .Append("e.style.outline='';e.style.outlineColor='';e.style.outlineOffset='';}")
            .Append("if(w.__signetPreviewHighlightTimer){clearTimeout(w.__signetPreviewHighlightTimer);w.__signetPreviewHighlightTimer=0;}")
            .Append("if(w.__signetPreviewHighlightEl){clear(w.__signetPreviewHighlightEl);}")
            .Append("clear(el);");

        if (outline)
        {
            js.Append("el.style.outline='")
                .Append(h.OutlineWidth.ToString(CultureInfo.InvariantCulture)).Append("px solid ").Append(color)
                .Append("';el.style.outlineOffset='2px';");
        }
        else
        {
            js.Append("el.style.backgroundColor='").Append(Rgba(color, h.OpacityPercent)).Append("';");
        }

        js.Append("w.__signetPreviewHighlightEl=el;el.scrollIntoView({block:'center',inline:'nearest'});");

        if (h.AutoHide)
        {
            string fade = FadeMs.ToString(CultureInfo.InvariantCulture);
            js.Append("w.__signetPreviewHighlightTimer=setTimeout(function(){")
                .Append("el.style.transition='background-color ").Append(fade).Append("ms, outline-color ").Append(fade).Append("ms';")
                .Append(outline ? "el.style.outlineColor='transparent';" : "el.style.backgroundColor='transparent';")
                .Append("w.__signetPreviewHighlightTimer=setTimeout(function(){w.__signetPreviewHighlightTimer=0;")
                .Append("if(w.__signetPreviewHighlightEl===el){clear(el);w.__signetPreviewHighlightEl=null;}},")
                .Append(fade).Append(");},")
                .Append(h.AutoHideDelayMs.ToString(CultureInfo.InvariantCulture)).Append(");");
        }

        js.Append("})();");
        return js.ToString();
    }

    // "#rrggbb" + opacity % → "rgba(r,g,b,0.45)".
    private static string Rgba(string hex, int opacityPercent)
    {
        int r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        string alpha = (opacityPercent / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"rgba({r},{g},{b},{alpha})");
    }
}
