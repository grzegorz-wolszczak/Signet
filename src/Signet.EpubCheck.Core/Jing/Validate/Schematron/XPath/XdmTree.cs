using System.Collections.Generic;
using System.Text;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;

/// <summary>The kinds of nodes of the tree XPath works on.</summary>
internal enum XdmNodeKind
{
    Document,
    Element,
    Attribute,
    Text,
}

/// <summary>
/// A node of the tree a Schematron schema is evaluated on (what Saxon builds from the SAX events): document, elements,
/// attributes and text. Nodes are numbered in document order. Comments and processing instructions are not kept.
/// </summary>
internal abstract class XdmNode(XdmNodeKind kind, XdmNode? parent, int order)
{
    public XdmNodeKind Kind { get; } = kind;

    public XdmNode? Parent { get; } = parent;

    /// <summary>The position in document order.</summary>
    public int Order { get; } = order;

    public virtual string LocalName => string.Empty;

    public virtual string NamespaceUri => string.Empty;

    /// <summary>The name as written (<c>name()</c>).</summary>
    public virtual string QName => string.Empty;

    public virtual IReadOnlyList<XdmNode> Children => [];

    public virtual IReadOnlyList<XdmNode> Attributes => [];

    public abstract string StringValue { get; }

    /// <summary>The line of the node (its element's for an attribute or text), or -1.</summary>
    public virtual int LineNumber => Parent?.LineNumber ?? FixedLocator.Unknown;

    public virtual int ColumnNumber => Parent?.ColumnNumber ?? FixedLocator.Unknown;

    public virtual string? SystemId => Parent?.SystemId;

    public XdmNode Root
    {
        get
        {
            XdmNode node = this;
            while (node.Parent is not null)
            {
                node = node.Parent;
            }

            return node;
        }
    }
}

internal sealed class XdmDocument(int order) : XdmNode(XdmNodeKind.Document, null, order)
{
    public List<XdmNode> ChildList { get; } = [];

    public override IReadOnlyList<XdmNode> Children => ChildList;

    public override string StringValue => XdmElement.Concatenate(ChildList);

    /// <summary>
    /// What expressions work out once for the document and reuse (the nodes a step selects in the whole document, the indexes of
    /// predicates), keyed by the expression. A document is evaluated by one thread.
    /// </summary>
    public Dictionary<object, object> Cache { get; } = new(ReferenceEqualityComparer.Instance);
}

internal sealed class XdmElement(XdmNode parent, int order, string namespaceUri, string localName, string qName, int line, int column, string? systemId)
    : XdmNode(XdmNodeKind.Element, parent, order)
{
    public List<XdmNode> ChildList { get; } = [];

    public List<XdmNode> AttributeList { get; } = [];

    public override string LocalName { get; } = localName;

    public override string NamespaceUri { get; } = namespaceUri;

    public override string QName { get; } = qName;

    public override IReadOnlyList<XdmNode> Children => ChildList;

    public override IReadOnlyList<XdmNode> Attributes => AttributeList;

    public override string StringValue => Concatenate(ChildList);

    public override int LineNumber { get; } = line;

    public override int ColumnNumber { get; } = column;

    public override string? SystemId { get; } = systemId;

    public static string Concatenate(List<XdmNode> children)
    {
        StringBuilder sb = new();
        Append(children, sb);
        return sb.ToString();
    }

    private static void Append(IReadOnlyList<XdmNode> children, StringBuilder sb)
    {
        foreach (XdmNode child in children)
        {
            if (child is XdmText text)
            {
                sb.Append(text.Value);
            }
            else
            {
                Append(child.Children, sb);
            }
        }
    }
}

internal sealed class XdmAttribute(XdmNode parent, int order, string namespaceUri, string localName, string qName, string value)
    : XdmNode(XdmNodeKind.Attribute, parent, order)
{
    public override string LocalName { get; } = localName;

    public override string NamespaceUri { get; } = namespaceUri;

    public override string QName { get; } = qName;

    public override string StringValue { get; } = value;
}

internal sealed class XdmText(XdmNode parent, int order) : XdmNode(XdmNodeKind.Text, parent, order)
{
    private readonly StringBuilder _value = new();

    public string Value => _value.ToString();

    public override string StringValue => Value;

    public void Append(string text) => _value.Append(text);
}

/// <summary>Builds the tree of a document from its SAX events.</summary>
internal sealed class XdmTreeBuilder : IContentHandler
{
    private readonly Stack<XdmNode> _open = new();
    private ILocator? _locator;
    private int _order;
    private XdmText? _pendingText;

    public XdmDocument? Document { get; private set; }

    public void SetDocumentLocator(ILocator locator) => _locator = locator;

    public void StartDocument()
    {
        _order = 0;
        Document = new XdmDocument(_order++);
        _open.Clear();
        _open.Push(Document);
        _pendingText = null;
    }

    public void EndDocument() => _pendingText = null;

    public void StartPrefixMapping(string prefix, string uri)
    {
    }

    public void EndPrefixMapping(string prefix)
    {
    }

    public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
    {
        _pendingText = null;
        XdmNode parent = _open.Peek();
        XdmElement element = new(parent, _order++, namespaceUri, localName, qName,
            _locator?.LineNumber ?? FixedLocator.Unknown, _locator?.ColumnNumber ?? FixedLocator.Unknown, _locator?.SystemId);
        for (int i = 0; i < attributes.Length; i++)
        {
            element.AttributeList.Add(new XdmAttribute(element, _order++, attributes.GetUri(i), attributes.GetLocalName(i), attributes.GetQName(i), attributes.GetValue(i)));
        }

        AddChild(parent, element);
        _open.Push(element);
    }

    public void EndElement(string namespaceUri, string localName, string qName)
    {
        _pendingText = null;
        _open.Pop();
    }

    public void Characters(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (_pendingText is null)
        {
            XdmNode parent = _open.Peek();
            _pendingText = new XdmText(parent, _order++);
            AddChild(parent, _pendingText);
        }

        _pendingText.Append(text);
    }

    public void ProcessingInstruction(string target, string data) => _pendingText = null;

    private static void AddChild(XdmNode parent, XdmNode child)
    {
        if (parent is XdmElement element)
        {
            element.ChildList.Add(child);
        }
        else
        {
            ((XdmDocument)parent).ChildList.Add(child);
        }
    }
}
