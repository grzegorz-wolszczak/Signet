using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Infrastructure;

/// <summary>
/// Preview page script: scrolls to the <c>[data-signet-loc]</c> element (Code View → Preview
/// synchronization) and highlights its block according to <see cref="PreviewHighlight"/> in the color of the theme — with a background or
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
    /// <param name="highlightColor">The color of the highlight (the theme's <c>preview.highlight</c>; its transparency is not used).</param>
    public static string Build(int loc, PreviewHighlight highlight, ThemeColor highlightColor)
    {
        PreviewHighlight h = highlight.Normalized();
        string color = ThemeColor.FromRgb(highlightColor.R, highlightColor.G, highlightColor.B).ToString();
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

/// <summary>
/// Preview page script: forces the background and the text color of the theme (<c>preview.background</c>,
/// <c>preview.text</c>) on the book — e.g. a dark page for a dark theme — or removes them when the theme sets none.
/// </summary>
public static class PreviewThemeScript
{
    /// <summary>The id of the <c>&lt;style&gt;</c> element the script adds to the page.</summary>
    public const string StyleId = "signet-theme-preview-colors";

    /// <summary>Builds the script; with both colors unset it only removes an earlier style.</summary>
    public static string Build(ThemeColor? background, ThemeColor? text)
    {
        StringBuilder css = new();
        if (background is { } bg)
        {
            css.Append("html,body{background:").Append(Css(bg)).Append(" !important;}");
        }

        if (text is { } fg)
        {
            css.Append("body,body *{color:").Append(Css(fg)).Append(" !important;}");
        }

        StringBuilder js = new();
        js.Append("(function(){var old=document.getElementById('").Append(StyleId).Append("');if(old)old.remove();");
        if (css.Length > 0)
        {
            js.Append("var s=document.createElement('style');s.id='").Append(StyleId).Append("';s.textContent='")
                .Append(css).Append("';(document.head||document.documentElement).appendChild(s);");
        }

        return js.Append("})();").ToString();
    }

    // "rgba(r,g,b,a)" — keeps the transparency of the theme color.
    private static string Css(ThemeColor c) => string.Create(
        CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})");
}

/// <summary>
/// Preview page script: the fonts of Preferences › Appearance › Fonts › Preview as the default fonts of the book, as a
/// browser's font settings work (Sigil sets them on its web engine; the native web views have no such setting):
/// <list type="bullet">
/// <item>the standard font and the size go to <c>html</c> in a style element put first in <c>&lt;head&gt;</c>, so any
/// rule of the book wins over them;</item>
/// <item>the serif and the sans-serif font are put before the generic <c>serif</c> / <c>sans-serif</c> in the
/// <c>font-family</c> of the rules and the <c>style</c> attributes of the book (a generic family cannot be mapped to
/// another font with CSS). Stylesheets that load later are handled when they load; the original lists are kept, so
/// running the script again with other fonts replaces the earlier ones.</item>
/// </list>
/// An empty family or a size of 0 leaves that part to the web view.
/// </summary>
public static class PreviewFontsScript
{
    /// <summary>The id of the <c>&lt;style&gt;</c> element the script adds to the page.</summary>
    public const string StyleId = "signet-preview-fonts";

    /// <summary>Builds the script for the fonts.</summary>
    public static string Build(PreviewAppearance fonts)
    {
        StringBuilder js = new();
        js.Append("(function(){var F={std:").Append(Js(fonts.FontFamilyStandard))
            .Append(",serif:").Append(Js(fonts.FontFamilySerif))
            .Append(",sans:").Append(Js(fonts.FontFamilySansSerif))
            .Append(",size:").Append(Math.Max(0, fonts.FontSize).ToString(CultureInfo.InvariantCulture)).Append("};")
            .Append("function q(n){return '\"'+n.replace(/[\"\\\\]/g,'')+'\"';}")
            // The defaults of the page: first in <head>, so that the book overrides them.
            .Append("var old=document.getElementById('").Append(StyleId).Append("');if(old)old.remove();")
            .Append("var css='';if(F.std)css+='font-family:'+q(F.std)+';';if(F.size>0)css+='font-size:'+F.size+'px;';")
            .Append("if(css){var s=document.createElement('style');s.id='").Append(StyleId).Append("';s.textContent='html{'+css+'}';")
            .Append("var h=document.head||document.documentElement;h.insertBefore(s,h.firstChild);}")
            // The generic families of the book.
            .Append("function fix(st){if(!st)return;var o=st.__signetFamily;if(o===undefined){o=st.fontFamily;st.__signetFamily=o;}")
            .Append("if(!o)return;var p=o.split(','),out=[];for(var i=0;i<p.length;i++){var t=p[i].trim(),l=t.toLowerCase();")
            .Append("if(l==='serif'&&F.serif)out.push(q(F.serif));else if(l==='sans-serif'&&F.sans)out.push(q(F.sans));out.push(t);}")
            .Append("var v=out.join(', ');if(v!==st.fontFamily)st.fontFamily=v;}")
            .Append("function walk(r){for(var i=0;i<r.length;i++){if(r[i].style)fix(r[i].style);if(r[i].cssRules)walk(r[i].cssRules);}}")
            .Append("function all(){for(var i=0;i<document.styleSheets.length;i++){try{walk(document.styleSheets[i].cssRules);}catch(e){}}")
            .Append("var els=document.querySelectorAll('[style]');for(var j=0;j<els.length;j++)fix(els[j].style);}")
            .Append("window.__signetPreviewFonts=all;all();")
            .Append("var links=document.querySelectorAll('link[rel~=\"stylesheet\"]');for(var k=0;k<links.length;k++){")
            .Append("if(!links[k].__signetFonts){links[k].__signetFonts=true;links[k].addEventListener('load',function(){")
            .Append("if(window.__signetPreviewFonts)window.__signetPreviewFonts();});}}")
            .Append("})();");
        return js.ToString();
    }

    // A JavaScript string literal (JSON is valid JavaScript; the HTML-sensitive characters are escaped).
    private static string Js(string? value) => JsonSerializer.Serialize(value?.Trim() ?? string.Empty);
}

