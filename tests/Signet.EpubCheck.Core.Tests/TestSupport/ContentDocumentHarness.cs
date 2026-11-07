using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Tests.TestSupport;

/// <summary>
/// A publication made of a few files in memory, for the tests of the checkers of content documents: the document under test
/// is in the container with the other files it refers to, registered as the manifest of a package document would register them.
/// </summary>
internal sealed class ContentDocumentHarness
{
    public const string Xhtml = "application/xhtml+xml";
    public const string Svg = "image/svg+xml";
    public const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    private readonly List<(string Name, string MimeType, byte[] Content)> _files = [];
    private readonly List<VocabProperty> _properties = [];
    private readonly HashSet<string> _undeclared = new(StringComparer.Ordinal);
    private ValidationContext? _context;
    private readonly Dictionary<string, Action<OPFItemBuilder>> _items = new(StringComparer.Ordinal);

    public ContentDocumentHarness(string name, string content, string mimeType = Xhtml)
        : this(name, Encoding.UTF8.GetBytes(content), mimeType)
    {
    }

    public ContentDocumentHarness(string name, byte[] content, string mimeType)
    {
        Name = name;
        MimeType = mimeType;
        _files.Add((name, mimeType, content));
    }

    /// <summary>The name of the document under test.</summary>
    public string Name { get; }

    /// <summary>The media type of the document under test.</summary>
    public string MimeType { get; }

    /// <summary>The version of EPUB to check against.</summary>
    public EpubVersion Version { get; set; } = EpubVersion.Version3;

    /// <summary>The validation profile.</summary>
    public EpubProfile Profile { get; set; } = EpubProfile.Default;

    /// <summary>The <c>dc:type</c> values of the publication.</summary>
    public HashSet<PublicationType> PublicationTypes { get; } = [];

    /// <summary>Whether the document is fixed-layout.</summary>
    public bool FixedLayout { get; set; }

    /// <summary>The media overlay of the document, if any.</summary>
    public string? MediaOverlay { get; set; }

    /// <summary>The report; every severity is kept, and the accessibility hints (suppressed by default) are reported as usage.</summary>
    public CollectingReport Report { get; } = new(new UsageForSuppressed()) { ReportingLevel = Severity.Usage };

    /// <summary>The context of the check; made when first asked for (or by <see cref="Build"/>), so the files must be added before.</summary>
    public ValidationContext Context => _context ?? Build();

    /// <summary>The URL of the file <paramref name="name"/> (it may be followed by a fragment) in the container.</summary>
    public Core.Galimatias.Url Local(string name) => Context.Container!.RootUrl.Resolve(name);

    /// <summary>Adds a file to the container that the manifest does not declare.</summary>
    public ContentDocumentHarness UndeclaredFile(string name)
    {
        _files.Add((name, string.Empty, []));
        _undeclared.Add(name);
        return this;
    }

    /// <summary>Adds a file the document refers to.</summary>
    public ContentDocumentHarness With(string name, string mimeType, string content = "")
    {
        _files.Add((name, mimeType, Encoding.UTF8.GetBytes(content)));
        return this;
    }

    /// <summary>Declares the file <paramref name="name"/> (added with <see cref="With"/>) as a manifest item, set up by <paramref name="setup"/>.</summary>
    public ContentDocumentHarness WithItem(string name, Action<OPFItemBuilder> setup)
    {
        _items[name] = setup;
        return this;
    }

    /// <summary>Gives the item of the document a property of the manifest.</summary>
    public ContentDocumentHarness WithItemProperty(PackageVocabs.ItemProperty property)
    {
        _properties.Add(PackageVocabs.ItemVocab.Get(property));
        return this;
    }

    /// <summary>Makes the context.</summary>
    public ValidationContext Build()
    {
        MemoryEpubSource source = new(_files.Select(file => new KeyValuePair<string, byte[]>(file.Name, file.Content)));
        OCFContainer.Builder builder = new(source);
        foreach (EpubSourceEntry entry in source.Entries)
        {
            builder.AddResource(entry);
        }

        OCFContainer container = builder.Build();
        Core.Galimatias.Url url = container.RootUrl.Resolve(Name);
        ValidationContext context = new ValidationContext
        {
            Url = url,
            Report = Report,
            Version = Version,
            Profile = Profile,
            MimeType = MimeType,
            PublicationTypes = PublicationTypes,
        }.WithContainer(container);

        OPFItemBuilder item = new()
        {
            Id = "doc",
            Url = url,
            Location = new EpubLocation("package.opf"),
            Container = container,
            MimeType = MimeType,
            FixedLayout = FixedLayout,
            MediaOverlay = MediaOverlay,
            SpinePosition = 0,
        };
        item.AddProperties(_properties);
        OPFItem built = item.Build();
        context.ResourceRegistry!.RegisterResource(built);
        foreach ((string name, string mimeType, _) in _files.Skip(1).Where(file => !_undeclared.Contains(file.Name)))
        {
            Core.Galimatias.Url fileUrl = container.RootUrl.Resolve(name);
            if (_items.TryGetValue(name, out Action<OPFItemBuilder>? setup))
            {
                OPFItemBuilder other = new()
                {
                    Id = name,
                    Url = fileUrl,
                    Location = new EpubLocation("package.opf"),
                    Container = container,
                    MimeType = mimeType,
                };
                setup(other);
                context.ResourceRegistry.RegisterResource(other.Build());
            }
            else
            {
                context.ResourceRegistry.RegisterResource(fileUrl, mimeType);
            }
        }

        _context = context with { Properties = new HashSet<VocabProperty>(built.Properties) };
        return _context;
    }

    /// <summary>The ids of the messages reported so far.</summary>
    public IReadOnlyList<MessageId> Ids => [.. Report.Messages.Select(message => message.Message.Id)];

    /// <summary>The messages reported with <paramref name="id"/>.</summary>
    public IReadOnlyList<ReportedMessage> Messages(MessageId id) => [.. Report.Messages.Where(message => message.Message.Id == id)];

    /// <summary>The references registered so far.</summary>
    public IReadOnlyList<Reference> References => Context.ReferenceRegistry!.AsList();

    /// <summary>An XHTML document with the given body and head content.</summary>
    public static string Html(string body, string head = "", string rootAttributes = "")
    {
        string xhtml = XhtmlNamespace;
        return $"<html xmlns=\"{xhtml}\" xmlns:epub=\"http://www.idpf.org/2007/ops\"{rootAttributes}><head><title>T</title>{head}</head><body>{body}</body></html>";
    }

    // The messages that are suppressed by default (the accessibility hints) are usage, to see them in the tests.
    private sealed class UsageForSuppressed : IMessageDictionary
    {
        private readonly LocalizedMessageDictionary _inner = new(System.Globalization.CultureInfo.InvariantCulture);

        public Message GetMessage(MessageId id)
        {
            Message message = _inner.GetMessage(id);
            return message.Severity == Severity.Suppressed ? message with { Severity = Severity.Usage } : message;
        }

        public string Format(Message message, params object?[] args) => _inner.Format(message, args);
    }
}
