using System;
using System.Collections.Generic;
using System.Text;

namespace Signet.Core.Toc;

/// <summary>
/// Generates a separate XHTML file with the table of contents (<c>TOC.xhtml</c>) based on an entry hierarchy
/// (from the nav document / NCX).
/// </summary>
/// <remarks>
/// The output format is reproduced with its own readable indentation. The behavior
/// (the <c>&lt;div class="sgc-toc-level-N"&gt;</c> structure, relative links with a fragment, a DOCTYPE per version)
/// is what matters. The text is not passed through <c>CleanSource</c>.
/// </remarks>
public static class TocHtmlWriter
{
    /// <summary>The name of the table of contents CSS file.</summary>
    public const string SgcTocCssFilename = "sgc-toc.css";

    /// <summary>The name of the table of contents HTML file created by "Create HTML TOC".</summary>
    public const string HtmlTocFilename = "TOC.xhtml";

    /// <summary>The default content of <c>sgc-toc.css</c>.</summary>
    public const string SgcTocCss =
        "div.sgc-toc-title {\n" +
        "    font-size: 2em;\n" +
        "    font-weight: bold;\n" +
        "    margin-bottom: 1em;\n" +
        "    text-align: center;\n" +
        "}\n\n" +
        "div.sgc-toc-level-1 {\n" +
        "    margin-left: 0em;\n" +
        "}\n\n" +
        "div.sgc-toc-level-2 {\n" +
        "    margin-left: 2em;\n" +
        "}\n\n" +
        "div.sgc-toc-level-3 {\n" +
        "    margin-left: 2em;\n" +
        "}\n\n" +
        "div.sgc-toc-level-4 {\n" +
        "    margin-left: 2em;\n" +
        "}\n\n" +
        "div.sgc-toc-level-5 {\n" +
        "    margin-left: 2em;\n" +
        "}\n\n" +
        "div.sgc-toc-level-6 {\n" +
        "    margin-left: 2em;\n" +
        "}\n";

    /// <summary>An entry of the input table of contents tree for the writer (the target = an absolute bookpath + a fragment without <c>#</c>).</summary>
    public sealed class Entry
    {
        /// <summary>The entry text.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>The absolute bookpath of the target (empty = no link / a container entry).</summary>
        public string TargetBookPath { get; init; } = string.Empty;

        /// <summary>The fragment in the target file without <c>#</c> (empty = the start of the file).</summary>
        public string Fragment { get; init; } = string.Empty;

        /// <summary>An external address (with a <c>:</c> scheme) — used verbatim instead of <see cref="TargetBookPath"/>.</summary>
        public string ExternalHref { get; init; } = string.Empty;

        /// <summary>The child entries.</summary>
        public IReadOnlyList<Entry> Children { get; init; } = Array.Empty<Entry>();
    }

    /// <summary>
    /// Builds the full XHTML document of the table of contents.
    /// </summary>
    /// <param name="tocBookPath">The bookpath of the <c>TOC.xhtml</c> file (used to compute relative links).</param>
    /// <param name="cssBookPath">The bookpath of the <c>sgc-toc.css</c> file.</param>
    /// <param name="rootEntries">The top-level entries (level 1).</param>
    /// <param name="title">The page title (the heading + <c>&lt;title&gt;</c>).</param>
    /// <param name="version">The EPUB version (<c>"2.0"</c> =&gt; the XHTML 1.1 DOCTYPE, otherwise <c>&lt;!DOCTYPE html&gt;</c>).</param>
    public static string WriteXml(
        string tocBookPath,
        string cssBookPath,
        IReadOnlyList<Entry> rootEntries,
        string title,
        string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(tocBookPath);
        ArgumentNullException.ThrowIfNull(cssBookPath);
        ArgumentNullException.ThrowIfNull(rootEntries);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(version);

        bool epub2 = version.StartsWith('2');
        string cssHref = Utility.UrlEncodePath(Core.BookPath.Relative(tocBookPath, cssBookPath));

        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        if (epub2)
        {
            sb.Append("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n")
              .Append("   \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n");
            sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\">\n");
        }
        else
        {
            sb.Append("<!DOCTYPE html>\n");
            sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n");
        }

        sb.Append("<head>\n");
        sb.Append("  <title>").Append(Utility.EncodeXml(title)).Append("</title>\n");
        sb.Append("  <link href=\"").Append(cssHref).Append("\" rel=\"stylesheet\" type=\"text/css\"/>\n");
        sb.Append("</head>\n");

        sb.Append("<body>\n");
        sb.Append("  <div class=\"sgc-toc-title\">").Append(Utility.EncodeXml(title)).Append("</div>\n");
        foreach (Entry entry in rootEntries)
        {
            WriteEntry(sb, entry, 1, tocBookPath);
        }

        sb.Append("</body>\n");
        sb.Append("</html>\n");
        return sb.ToString();
    }

    private static void WriteEntry(StringBuilder sb, Entry entry, int level, string tocBookPath)
    {
        string pad = new(' ', level * 2);
        sb.Append(pad).Append("<div class=\"sgc-toc-level-").Append(level).Append("\">\n");
        sb.Append(pad).Append("  <a href=\"").Append(BuildHref(entry, tocBookPath)).Append("\">")
          .Append(Utility.EncodeXml(entry.Text)).Append("</a>\n");
        foreach (Entry child in entry.Children)
        {
            WriteEntry(sb, child, level + 1, tocBookPath);
        }

        sb.Append(pad).Append("</div>\n");
    }

    private static string BuildHref(Entry entry, string tocBookPath)
    {
        if (entry.ExternalHref.Length > 0)
        {
            return Utility.EncodeXml(entry.ExternalHref);
        }

        if (entry.TargetBookPath.Length == 0)
        {
            return entry.Fragment.Length > 0 ? "#" + Utility.EncodeXml(entry.Fragment) : "#";
        }

        string href = Utility.UrlEncodePath(Core.BookPath.Relative(tocBookPath, entry.TargetBookPath));
        if (entry.Fragment.Length > 0)
        {
            href += "#" + entry.Fragment;
        }

        return Utility.EncodeXml(href);
    }
}
