using System;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Xhtml;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Formats an XHTML tree (AngleSharp DOM) into a readable form via a recursive
/// <c>Prettyprint</c> / <c>PrettyprintContents</c> pass.
/// Tag classification: <see cref="PrettyPrintProps"/>. Serialization primitives (opening/closing
/// tags with attributes, entities in text) come from
/// <see cref="XhtmlMarkupFormatter"/> (<c>emptyTagsToSelfClosing: false</c>).
/// </summary>
internal sealed class XhtmlPrettyPrinter
{
    private const string TrimChars = " \n\r\t\v\f";

    private static readonly char[] WhitespaceRun = { ' ', '\n', '\r', '\t', '\v', '\f' };

    private readonly PrettyPrintProps _props;
    private readonly bool _keepWhitespace;
    private readonly bool _singleSpace;
    private readonly string _indentChars;
    private readonly XhtmlMarkupFormatter _formatter = new(emptyTagsToSelfClosing: false);

    public XhtmlPrettyPrinter(PrettyPrintProps props, bool keepWhitespace)
    {
        _props = props;
        _keepWhitespace = keepWhitespace;
        _singleSpace = props.SingleSpace;
        _indentChars = props.IndentString;
    }

    /// <summary>
    /// Equivalent of <c>PrettyprintContents(document, 1)</c> — the formatted document content
    /// (without the XML prolog and DOCTYPE; <see cref="CleanSource"/> prepends those).
    /// </summary>
    public string PrintDocumentContents(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return PrettyprintContents(document, 1);
    }

    // =====================================================================
    //  Prettyprint(node, lvl)
    // =====================================================================

    private string Prettyprint(INode node, int lvl)
    {
        if (node is IElement element)
        {
            return PrettyprintElement(element, lvl);
        }

        // The document node is handled by PrintDocumentContents; it should not get here.
        return PrettyprintContents(node, lvl + 1);
    }

    private string PrettyprintElement(IElement node, int lvl)
    {
        string tagname = TagName(node);
        string parentname = node.Parent is null ? string.Empty : TagName(node.Parent);
        bool inHead = parentname == "head";
        bool isStructural = _props.IsStructural(tagname);
        bool isInline = _props.IsInline(tagname);
        bool inXmlNs = !string.Equals(node.NamespaceUri, "http://www.w3.org/1999/xhtml", StringComparison.Ordinal);

        // Special case: <link rel="stylesheet"> in <head> without a type attribute.
        if (tagname == "link" && node.ParentElement is { } parentEl && TagName(parentEl) == "head"
            && string.Equals(node.GetAttribute("rel"), "stylesheet", StringComparison.OrdinalIgnoreCase)
            && node.GetAttribute("type") is null)
        {
            node.SetAttribute("type", "text/css");
        }

        bool isVoid = _props.IsVoid(tagname);

        string contents = string.Empty;
        if (!isVoid)
        {
            contents = isStructural && tagname != "html"
                ? PrettyprintContents(node, lvl + 1)
                : PrettyprintContents(node, lvl);
        }

        bool keepWhitespace = _props.IsPreserveSpace(tagname);
        if (!keepWhitespace && !isInline)
        {
            contents = RTrim(contents);
        }

        string testcontents = LTrim(contents);
        bool single = isVoid || (inXmlNs && testcontents.Length == 0);

        string indentSpace = IndentSpace(lvl);

        if (single)
        {
            string selfCloseTag = SelfCloseTag(node);
            if (isInline)
            {
                if (tagname == "br" && _props.IsStructural(parentname))
                {
                    selfCloseTag += "\n";
                    if (!inHead && tagname != "html" && !_singleSpace)
                    {
                        selfCloseTag += "\n";
                    }
                }

                return selfCloseTag;
            }

            if (!inHead && tagname != "html" && !_singleSpace)
            {
                selfCloseTag += "\n";
            }

            return indentSpace + selfCloseTag + "\n";
        }

        string startTag = _formatter.OpenTag(node, false);
        string closeTag = _formatter.CloseTag(node, false);
        string results;

        if (isStructural)
        {
            results = indentSpace + startTag;
            if (contents.Length > 0)
            {
                results += "\n" + contents + "\n" + indentSpace;
            }

            results += closeTag + "\n";
            if (!inHead && tagname != "html" && !_singleSpace)
            {
                results += "\n";
            }
        }
        else if (isInline)
        {
            results = startTag + contents + closeTag;
        }
        else
        {
            results = indentSpace + startTag;
            if (!keepWhitespace)
            {
                contents = LTrim(contents);
            }

            results += contents + closeTag + "\n";
            if (!inHead && tagname != "html" && !_singleSpace)
            {
                results += "\n";
            }
        }

        return results;
    }

    // =====================================================================
    //  PrettyprintContents(node, lvl)
    // =====================================================================

    private string PrettyprintContents(INode node, int lvl)
    {
        string tagname = TagName(node);
        bool noEntitySubstitution = _props.IsNoEntitySub(tagname);
        bool keepWhitespace = _props.IsPreserveSpace(tagname);
        bool isInline = _props.IsInline(tagname);
        bool isStructural = _props.IsStructural(tagname);

        string indentSpace = IndentSpace(lvl);
        char lastChar = 'x';
        bool containsBlockTags = false;

        if (isStructural || tagname == "#document")
        {
            lastChar = '\n';
        }

        bool inHeadWithoutTitle = tagname == "head";
        StringBuilder contents = new();

        foreach (INode child in node.ChildNodes)
        {
            switch (child)
            {
                case IDocumentType:
                    // DOCTYPE from AngleSharp — skipped; CleanSource prepends its own.
                    break;

                case IText text when !IsAllWhitespace(text.Data):
                {
                    string val = noEntitySubstitution ? text.Data : _formatter.Text(text);

                    if (isStructural && lastChar == '\n')
                    {
                        contents.Append(indentSpace);
                        val = LTrim(val);
                    }

                    if (!keepWhitespace && !_keepWhitespace)
                    {
                        val = CondenseWhitespace(val);
                    }

                    contents.Append(val);
                    break;
                }

                case IText whitespace:
                {
                    if (keepWhitespace)
                    {
                        contents.Append(whitespace.Data);
                    }
                    else if (isInline || _props.IsTextHolder(tagname))
                    {
                        if (!TrimChars.Contains(lastChar, StringComparison.Ordinal))
                        {
                            contents.Append(' ');
                        }
                    }

                    break;
                }

                case IElement childElement:
                {
                    string val = Prettyprint(childElement, lvl);
                    string childname = TagName(childElement);

                    if (inHeadWithoutTitle && childname == "title")
                    {
                        inHeadWithoutTitle = false;
                    }

                    if (!_props.IsInline(childname))
                    {
                        containsBlockTags = true;
                        if (lastChar != '\n')
                        {
                            contents.Append('\n');
                            if (tagname != "head" && tagname != "html" && !_singleSpace)
                            {
                                contents.Append('\n');
                            }

                            lastChar = '\n';
                        }
                    }

                    if (isStructural && _props.IsInline(childname) && lastChar == '\n')
                    {
                        contents.Append(indentSpace);
                        val = LTrim(val);
                    }

                    contents.Append(val);
                    break;
                }

                case IComment comment:
                    contents.Append("<!--").Append(comment.Data).Append("-->");
                    break;

                default:
                    if (child.NodeType == NodeType.CharacterData && child is ICharacterData cdata)
                    {
                        contents.Append("<![CDATA[").Append(cdata.Data).Append("]]>");
                    }

                    break;
            }

            if (contents.Length > 0)
            {
                lastChar = contents[^1];
            }
        }

        if (inHeadWithoutTitle)
        {
            if (lastChar != '\n')
            {
                contents.Append('\n');
            }

            contents.Append(indentSpace).Append("<title></title>\n");
            lastChar = '\n';
        }

        if (isInline && containsBlockTags)
        {
            if (lastChar != '\n' && !_singleSpace)
            {
                contents.Append("\n\n");
            }

            contents.Append(indentSpace);
        }

        return contents.ToString();
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string TagName(INode node) => node switch
    {
        IDocument => "#document",
        IElement element => element.LocalName,
        _ => "#text",
    };

    private string IndentSpace(int lvl)
    {
        int count = (lvl - 1) * _indentChars.Length;
        return count <= 0 ? string.Empty : new string(_indentChars[0], count);
    }

    /// <summary>Self-closing tag in the form <c>&lt;tag atts/&gt;</c> (no space before <c>/&gt;</c>).</summary>
    private string SelfCloseTag(IElement node)
    {
        string open = _formatter.OpenTag(node, false);
        return open[..^1] + "/>";
    }

    private static bool IsAllWhitespace(string value)
    {
        foreach (char c in value)
        {
            if (!" \t\n\r\f".Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string LTrim(string value) => value.TrimStart(WhitespaceRun);

    private static string RTrim(string value) => value.TrimEnd(WhitespaceRun);

    /// <summary>Collapses every run of whitespace into a single space (without trimming).</summary>
    private static string CondenseWhitespace(string value)
    {
        StringBuilder result = new(value.Length);
        char lastChar = 'x';
        foreach (char original in value)
        {
            char c = " \n\r\t\v\f".Contains(original, StringComparison.Ordinal) ? ' ' : original;
            if (c != ' ' || lastChar != ' ')
            {
                result.Append(c);
            }

            lastChar = c;
        }

        return result.ToString();
    }
}
