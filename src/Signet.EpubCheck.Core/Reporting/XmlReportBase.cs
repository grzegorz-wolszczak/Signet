using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>
/// What the reports in XML have in common (upstream's <c>XmlReportAbstract</c>): they collect what the checks tell about the
/// publication and the messages, and a derived class builds the document from that in <see cref="GenerateReport"/>.
/// </summary>
public abstract class XmlReportBase : ReportBase
{
    private const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>";
    private const string XmlnsPrefix = "xmlns:";
    private const string DefaultToolDate = "2012-10-31";
    private const string DefaultToolName = "epubcheck";

    private readonly TextWriter _out;
    private readonly bool _closeWriter;
    private readonly Dictionary<string, XNamespace> _namespaces = new(StringComparer.Ordinal);
    private XNamespace? _defaultNamespace;
    private XDocument? _document;
    private XElement? _current;

    /// <summary>Creates the report for <paramref name="epubName"/>, to be written to <paramref name="output"/>.</summary>
    /// <param name="output">Where the XML goes.</param>
    /// <param name="epubName">The checked file or folder, as the report names it.</param>
    /// <param name="toolVersion">The version of the tool, until the checks report their own.</param>
    /// <param name="closeWriter">Whether <see cref="Generate"/> disposes <paramref name="output"/> (a file does; the console does not).</param>
    protected XmlReportBase(TextWriter output, string epubName, string? toolVersion, bool closeWriter = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(epubName);
        _out = output;
        _closeWriter = closeWriter;
        EpubFileName = PathUtil.RemoveWorkingDirectory(epubName);
        ToolVersion = toolVersion;
    }

    /// <summary>The name of the tool.</summary>
    protected string ToolName { get; private set; } = DefaultToolName;

    /// <summary>The version of the tool.</summary>
    protected string? ToolVersion { get; private set; }

    /// <summary>The date of the tool's build.</summary>
    protected string ToolDate { get; private set; } = DefaultToolDate;

    /// <summary>The time the report was made, in ISO 8601.</summary>
    protected string? GenerationDate { get; set; }

    /// <summary>The creation date of the publication.</summary>
    protected string? CreationDate { get; private set; }

    /// <summary>The last modification date of the publication.</summary>
    protected string? LastModifiedDate { get; private set; }

    /// <summary>The identifier of the publication.</summary>
    protected string? Identifier { get; private set; }

    /// <summary>The titles.</summary>
    protected ISet<string> Titles { get; } = new OrderedSet();

    /// <summary>The creators.</summary>
    protected ISet<string> Creators { get; } = new OrderedSet();

    /// <summary>The contributors.</summary>
    protected ISet<string> Contributors { get; } = new OrderedSet();

    /// <summary>The subjects.</summary>
    protected ISet<string> Subjects { get; } = new OrderedSet();

    /// <summary>The publisher.</summary>
    protected string? Publisher { get; private set; }

    /// <summary>The rights statements.</summary>
    protected ISet<string> Rights { get; } = new OrderedSet();

    /// <summary>The date of the publication.</summary>
    protected string? Date { get; private set; }

    /// <summary>The media types the publication declares.</summary>
    protected ISet<string> MediaTypes { get; } = new OrderedSet();

    /// <summary>The name of the format (<c>application/epub+zip</c>).</summary>
    protected string? FormatName { get; private set; }

    /// <summary>The version of the format.</summary>
    protected string? FormatVersion { get; private set; }

    /// <summary>The number of pages.</summary>
    protected long PagesCount { get; private set; }

    /// <summary>The number of characters.</summary>
    protected long CharsCount { get; private set; }

    /// <summary>The language of the publication.</summary>
    protected string? Language { get; private set; }

    /// <summary>The fonts the publication embeds.</summary>
    protected ISet<string> EmbeddedFonts { get; } = new OrderedSet();

    /// <summary>The fonts the publication refers to without embedding them.</summary>
    protected ISet<string> ReferencedFonts { get; } = new OrderedSet();

    /// <summary>The references to other publications and sites.</summary>
    protected ISet<string> References { get; } = new OrderedSet();

    /// <summary>Whether the publication uses encryption.</summary>
    protected bool HasEncryption { get; private set; }

    /// <summary>Whether the publication is signed.</summary>
    protected bool HasSignatures { get; private set; }

    /// <summary>Whether the publication has audio.</summary>
    protected bool HasAudio { get; private set; }

    /// <summary>Whether the publication has video.</summary>
    protected bool HasVideo { get; private set; }

    /// <summary>Whether the publication has fixed layout.</summary>
    protected bool HasFixedLayout { get; private set; }

    /// <summary>Whether the publication has scripts.</summary>
    protected bool HasScripts { get; private set; }

    /// <summary>The warnings.</summary>
    protected List<CheckMessage> Warnings { get; } = [];

    /// <summary>The errors.</summary>
    protected List<CheckMessage> Errors { get; } = [];

    /// <summary>The fatal errors.</summary>
    protected List<CheckMessage> FatalErrors { get; } = [];

    /// <summary>The usages (the "hints" of the reports).</summary>
    protected List<CheckMessage> Hints { get; } = [];

    /// <inheritdoc />
    public override void Info(string? resource, FeatureKind feature, string? value)
    {
        // Upstream does not keep a fact without a value.
        if (value is null)
        {
            return;
        }

        switch (feature)
        {
            case FeatureKind.ToolDate:
                if (!value.StartsWith('$'))
                {
                    ToolDate = value;
                }

                break;
            case FeatureKind.ToolName: ToolName = value; break;
            case FeatureKind.ToolVersion: ToolVersion = value; break;
            case FeatureKind.FormatName: FormatName = value; break;
            case FeatureKind.FormatVersion: FormatVersion = value; break;
            case FeatureKind.CreationDate: CreationDate = value; break;
            case FeatureKind.ModifiedDate: LastModifiedDate = value; break;
            case FeatureKind.PagesCount:
                if (FeatureValues.TryLong(value, out long pages))
                {
                    PagesCount = pages;
                }

                break;
            case FeatureKind.CharsCount:
                if (FeatureValues.TryLong(value, out long chars))
                {
                    CharsCount += chars;
                }

                break;
            case FeatureKind.DeclaredMimetype:
                MediaTypes.Add(value);
                if (value.StartsWith("audio/", StringComparison.Ordinal))
                {
                    HasAudio = true;
                }
                else if (value.StartsWith("video/", StringComparison.Ordinal))
                {
                    HasVideo = true;
                }

                break;
            case FeatureKind.FontEmbedded: EmbeddedFonts.Add(value); break;
            case FeatureKind.FontReference: ReferencedFonts.Add(value); break;
            case FeatureKind.Reference: References.Add(value); break;
            case FeatureKind.DcLanguage: Language = value; break;
            case FeatureKind.DcTitle: Titles.Add(value); break;
            case FeatureKind.DcCreator: Creators.Add(value); break;
            case FeatureKind.DcContributor: Contributors.Add(value); break;
            case FeatureKind.DcPublisher: Publisher = value; break;
            case FeatureKind.DcSubject: Subjects.Add(value); break;
            case FeatureKind.DcRights: Rights.Add(value); break;
            case FeatureKind.DcDate: Date = value; break;
            case FeatureKind.UniqueIdent:
                if (resource is null)
                {
                    Identifier = value;
                }

                break;
            case FeatureKind.HasSignatures: HasSignatures = true; break;
            case FeatureKind.HasEncryption: HasEncryption = true; break;
            case FeatureKind.HasFixedLayout: HasFixedLayout = true; break;
            case FeatureKind.HasScripts: HasScripts = true; break;
        }
    }

    /// <summary>Builds the document with <see cref="StartElement"/>, <see cref="EndElement"/> and <see cref="GenerateElement(string, string?)"/>.</summary>
    /// <returns>0 when the report was built.</returns>
    protected abstract int GenerateReport();

    /// <summary>Builds the document with <see cref="GenerateReport"/> and writes it.</summary>
    public override int Generate()
    {
        _namespaces.Clear();
        _defaultNamespace = null;
        _current = null;
        _document = new XDocument();
        int returnCode;
        try
        {
            returnCode = GenerateReport();
            if (returnCode == 0)
            {
                Write();
            }
        }
        catch (IOException)
        {
            returnCode = 1;
        }
        finally
        {
            if (_closeWriter)
            {
                _out.Dispose();
            }
            else
            {
                _out.Flush();
            }
        }

        return returnCode;
    }

    /// <summary>Whether the publication has no fatal error and no error.</summary>
    protected bool IsWellFormed => FatalErrors.Count == 0 && Errors.Count == 0;

    /// <summary>The time now as ISO 8601 with the offset (<c>2026-11-07T12:00:00+01:00</c>).</summary>
    protected static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Sets the namespace of the elements that have no prefix.</summary>
    protected void SetNamespace(string uri) => _defaultNamespace = uri;

    /// <summary>Declares what a prefix of an element or attribute name stands for.</summary>
    protected void AddPrefixNamespace(string prefix, string uri) => _namespaces[prefix] = uri;

    /// <summary>The last part of a path or a URL.</summary>
    protected static string? GetNameFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        int slash = path.LastIndexOfAny(['/', Path.DirectorySeparatorChar]);
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>The text with its first letter in upper case.</summary>
    protected static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Opens an element inside the current one (or the root element).</summary>
    protected void StartElement(string name, params (string Key, string? Value)[] attributes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        XElement element = MakeElement(name.Trim());
        AddAttributes(element, attributes);
        if (_current is null)
        {
            _document!.Add(element);
        }
        else
        {
            _current.Add(element);
        }

        _current = element;
    }

    /// <summary>Closes the current element.</summary>
    protected void EndElement() => _current = _current?.Parent;

    /// <summary>Adds an element with a text; nothing is added when the text is missing or blank.</summary>
    protected void GenerateElement(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        XElement element = MakeElement(name.Trim());
        element.Add(new XText(CorrectToUtf8(value.Trim())));
        _current!.Add(element);
    }

    /// <summary>Adds an element with attributes and a text; the text is left out when it is missing or blank.</summary>
    protected void GenerateElement(string name, string? value, params (string Key, string? Value)[] attributes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        XElement element = MakeElement(name.Trim());
        AddAttributes(element, attributes);
        if (!string.IsNullOrWhiteSpace(value))
        {
            element.Add(new XText(CorrectToUtf8(value.Trim())));
        }

        _current!.Add(element);
    }

    /// <inheritdoc />
    protected override void OnMessage(Message message, EpubLocation location, object?[] args)
    {
        List<CheckMessage>? target = message.Severity switch
        {
            Severity.Fatal => FatalErrors,
            Severity.Error => Errors,
            Severity.Warning => Warnings,
            Severity.Usage => Hints,
            _ => null,
        };

        if (target is not null)
        {
            CheckMessage.Add(target, MaxOfEachMessage, message, Dictionary.Format(message, args), location);
        }
    }

    // Upstream's correctToUtf8: control characters other than line breaks cannot be written in XML; they become 0x<hex>.
    internal static string CorrectToUtf8(string input)
    {
        StringBuilder? result = null;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (IsIsoControl(c) && c is not ('\r' or '\n'))
            {
                result ??= new StringBuilder(input, 0, i, input.Length);
                result.Append("0x").Append(((int)c).ToString("x", CultureInfo.InvariantCulture));
            }
            else
            {
                result?.Append(c);
            }
        }

        return result?.ToString() ?? input;
    }

    private static bool IsIsoControl(char c) => c <= '\u001F' || (c >= '\u007F' && c <= '\u009F');

    private XElement MakeElement(string name)
    {
        int index = name.IndexOf(':', StringComparison.Ordinal);
        if (index < 0)
        {
            return new XElement(_defaultNamespace is null ? XName.Get(name) : _defaultNamespace + name);
        }

        string prefix = name[..index];
        return _namespaces.TryGetValue(prefix, out XNamespace? ns)
            ? new XElement(ns + name[(index + 1)..])
            : new XElement(XName.Get(name.Replace(':', '_'), string.Empty));
    }

    private void AddAttributes(XElement element, (string Key, string? Value)[] attributes)
    {
        foreach ((string key, string? value) in attributes)
        {
            XAttribute attribute;
            if (key.StartsWith(XmlnsPrefix, StringComparison.Ordinal))
            {
                attribute = new XAttribute(XNamespace.Xmlns + key[XmlnsPrefix.Length..], value ?? string.Empty);
            }
            else if (key.StartsWith("xml:", StringComparison.Ordinal))
            {
                attribute = new XAttribute(XNamespace.Xml + key[4..], value ?? string.Empty);
            }
            else
            {
                int index = key.IndexOf(':', StringComparison.Ordinal);
                attribute = index >= 0 && _namespaces.TryGetValue(key[..index], out XNamespace? ns)
                    ? new XAttribute(ns + key[(index + 1)..], value ?? string.Empty)
                    : new XAttribute(XName.Get(key.Replace(':', '_')), value ?? string.Empty);
            }

            element.Add(attribute);
        }
    }

    private void Write()
    {
        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "    ",
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = false,
        };

        _out.Write(XmlDeclaration);
        _out.Write('\n');
        using XmlWriter writer = XmlWriter.Create(_out, settings);
        _document!.Save(writer);
    }

    // A set that remembers the order its members were added in (Java's LinkedHashSet).
    private sealed class OrderedSet : ISet<string>
    {
        private readonly List<string> _order = [];
        private readonly HashSet<string> _set = new(StringComparer.Ordinal);

        public int Count => _order.Count;

        public bool IsReadOnly => false;

        public bool Add(string item)
        {
            if (!_set.Add(item))
            {
                return false;
            }

            _order.Add(item);
            return true;
        }

        void ICollection<string>.Add(string item) => Add(item);

        public void Clear()
        {
            _order.Clear();
            _set.Clear();
        }

        public bool Contains(string item) => _set.Contains(item);

        public void CopyTo(string[] array, int arrayIndex) => _order.CopyTo(array, arrayIndex);

        public IEnumerator<string> GetEnumerator() => _order.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Remove(string item) => _set.Remove(item) && _order.Remove(item);

        public void ExceptWith(IEnumerable<string> other) => throw new NotSupportedException();

        public void IntersectWith(IEnumerable<string> other) => throw new NotSupportedException();

        public bool IsProperSubsetOf(IEnumerable<string> other) => _set.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<string> other) => _set.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<string> other) => _set.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<string> other) => _set.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<string> other) => _set.Overlaps(other);

        public bool SetEquals(IEnumerable<string> other) => _set.SetEquals(other);

        public void SymmetricExceptWith(IEnumerable<string> other) => throw new NotSupportedException();

        public void UnionWith(IEnumerable<string> other) => throw new NotSupportedException();
    }
}
