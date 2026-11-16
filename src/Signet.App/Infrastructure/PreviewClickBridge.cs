using System.Linq;

namespace Signet.App.Infrastructure;

/// <summary>
/// The script injected into every page of the preview that reports clicks to the host: where in the source the click
/// was (Preview → Code Editor), a link clicked inside the book, and the block of text that was clicked (a place of the
/// preview's history).
/// </summary>
public static class PreviewClickBridge
{
    /// <summary>
    /// The elements that hold a basic piece of text: a click in one of them (or in something inside it) is a place of
    /// the history. Containers (<c>div</c>, <c>section</c>, lists, tables) and inline elements are not; notes are
    /// recognized by <c>epub:type</c> as well (see <see cref="NoteTypes"/>).
    /// </summary>
    public static readonly string[] BlockTags =
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "li", "dt", "dd", "blockquote", "pre", "figcaption",
        "td", "th", "caption", "aside",
    };

    /// <summary>The <c>epub:type</c> values that make any element a block (a footnote or endnote).</summary>
    public static readonly string[] NoteTypes = { "footnote", "endnote", "rearnote" };

    /// <summary>Message prefix of a click in a block of text: <c>signet-block:{source offset of the block}</c>.</summary>
    public const string BlockMessagePrefix = "signet-block:";

    /// <summary>
    /// A click on an element sends the host <c>signet-loc:{offset}</c> of the nearest ancestor with a
    /// <c>data-signet-loc</c> attribute (Preview → Code Editor), preceded by <c>signet-block:{offset}</c> of the nearest
    /// block of text (it comes first, so that the host still knows where the caret was). A plain click on a link inside
    /// the book sends <c>signet-link:{offset} {address}</c> instead and leaves the navigation to the host, which follows
    /// footnote references and links back into Code View and navigates to any other link as the page would.
    /// </summary>
    public static readonly string Script =
        "(function(){if(window.__signetPreviewBridge)return;window.__signetPreviewBridge=true;"
        + "function post(m){try{window.chrome.webview.postMessage(m);}catch(x){}}"
        + "var T={" + string.Join(",", BlockTags.Select(t => "'" + t + "':1")) + "};"
        + "var N=/(^|\\s)(" + string.Join("|", NoteTypes) + ")(\\s|$)/;"
        + "function isBlock(n){if(!n.hasAttribute('data-signet-loc'))return false;"
        + "if(T[String(n.localName).toLowerCase()])return true;"
        + "return N.test(n.getAttribute('epub:type')||n.getAttributeNS('http://www.idpf.org/2007/ops','type')||'');}"
        + "document.addEventListener('click',function(e){var t=e.target;if(t&&t.nodeType!==1)t=t.parentElement;"
        + "var el=t;while(el&&el.nodeType===1&&!el.hasAttribute('data-signet-loc'))el=el.parentElement;"
        + "var loc=el&&el.nodeType===1?el.getAttribute('data-signet-loc'):null;"
        + "var a=t&&t.closest?t.closest('a[href]'):null;"
        + "if(a&&typeof a.href==='string'&&a.origin===location.origin&&!a.target&&e.button===0"
        + "&&!e.ctrlKey&&!e.shiftKey&&!e.altKey&&!e.metaKey){"
        + "e.preventDefault();post('signet-link:'+(loc===null?'-1':loc)+' '+a.href);return;}"
        + "var b=t;while(b&&b.nodeType===1&&!isBlock(b))b=b.parentElement;"
        + "if(b&&b.nodeType===1)post('" + BlockMessagePrefix + "'+b.getAttribute('data-signet-loc'));"
        + "if(loc!==null)post('signet-loc:'+loc);"
        + "},true);})();";
}
