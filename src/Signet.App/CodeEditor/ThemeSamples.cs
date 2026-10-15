using Signet.Core.MainUI;

namespace Signet.App.CodeEditor;

/// <summary>The demo texts of the pages of the theme editor with an editor preview.</summary>
public static class ThemeSamples
{
    /// <summary>General: XHTML with the editor's own looks (current line, selection, search, errors, hints).</summary>
    public static ThemeSample General { get; } = ThemeSample.Parse(
        """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml">
        <body>
          <p>The caret is on this line.</p>
          <p>Some «editor.selection|selected text» and a «search.match|search match».</p>
          <p>Marked «search.marked|results» and a «errors.spelling|mispelled» word.</p>
          «editor.tagPair|<div>»<p>The pair of tags around the caret.</p>«editor.tagPair|</div>»
          <p>An «errors.wellFormed|<span>» element that is never closed.</p>
          «errors.wellFormedWarning|<p>A warning about the document.</p>»
          <p>Fish & chips, an unescaped ampersand.</p>
          «hints.openTag|<section id="chapter-1">»
        </body>
        </html>
        """,
        CodeEditorSyntax.Html,
        caretLine: 4);

    /// <summary>XHTML: the markup, links, emphasized text and an embedded stylesheet.</summary>
    public static ThemeSample Xhtml { get; } = ThemeSample.Parse(
        """
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
        <head>
          <title>Chapter One</title>
          <link href="../Styles/style.css" rel="stylesheet" type="text/css"/>
          <style>
            /* Embedded rules */
            p.note { color: #336699; }
          </style>
        </head>
        <body>
          <!-- The first chapter -->
          <h1 epub:type="title">Chapter&#160;One</h1>
          <p>Text in <b>bold</b>, <i>italic</i> and <b><i>both</i></b>.</p>
          <p>A link to <a href="chapter2.xhtml">the next chapter</a> and a <a href="missing.xhtml">broken one</a>.</p>
          <p>A no-break space between 10 km.</p>
        </body>
        </html>
        """.Replace("10 km", "10 km", System.StringComparison.Ordinal),
        CodeEditorSyntax.Html);

    /// <summary>CSS: selectors, declarations and rules.</summary>
    public static ThemeSample Css { get; } = ThemeSample.Parse(
        """
        @charset "utf-8";
        /* Headings */
        h1, h2.title {
          font-family: "Georgia", serif;
          margin: 1.5em 0 0.5em;
          color: #336699 !important;
        }
        p:first-child::first-letter { font-size: 200%; }
        @media screen { .note { display: none; } }
        """,
        CodeEditorSyntax.Css);

    /// <summary>JavaScript.</summary>
    public static ThemeSample JavaScript { get; } = ThemeSample.Parse(
        """
        // Shows the footnote of a link.
        function showNote(link) {
          const id = link.getAttribute("href").replace(/^#/, "");
          if (id.length === 0) {
            return null;
          }
          var count = 42;
          return document.getElementById(id) instanceof HTMLElement;
        }
        """,
        CodeEditorSyntax.JavaScript);

    /// <summary>JSON.</summary>
    public static ThemeSample Json { get; } = ThemeSample.Parse(
        """
        {
          "title": "Chapter One",
          "pages": 12,
          "draft": false,
          "notes": null
        }
        """,
        CodeEditorSyntax.Json);

    /// <summary>Diff: a changed line in both versions and a line missing on one side.</summary>
    public static ThemeSample Diff { get; } = ThemeSample.Parse(
        """
          The first line of the chapter stays the same.
        «diff.deletedLine|- The cat sat on the «diff.deletedWord|old» mat.»
        «diff.insertedLine|+ The cat sat on the «diff.insertedWord|new» mat.»
        «diff.emptyCell|  (no line in this version)»
          The last line stays the same too.
        """,
        CodeEditorSyntax.PlainText);
}
