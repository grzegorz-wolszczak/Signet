using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util.Mime;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// What <see cref="OCFChecker"/> learns about the publication as it goes (upstream's <c>OCFCheckerState</c>): the
/// files of the container, the package documents the container file declares, what the peeker read from them, the
/// encrypted and obfuscated files; and the context the later checks start from.
/// </summary>
internal sealed class OCFCheckerState
{
    private readonly OCFContainer.Builder _containerBuilder;
    private readonly List<Url> _packageDocuments = [];
    private readonly Dictionary<Url, EpubLocation> _obfuscated = [];
    private readonly Dictionary<Url, HashSet<PublicationType>> _publicationTypes = [];
    private readonly List<KeyValuePair<Url, string>> _publicationIds = [];
    private readonly List<KeyValuePair<Url, EpubVersion>> _publicationVersions = [];
    private readonly HashSet<Url> _declaredResources = [];
    private ValidationContext _context;
    private OCFContainer? _container;

    public OCFCheckerState(ValidationContext context, IEpubSource source)
    {
        _context = context;
        _containerBuilder = new OCFContainer.Builder(source);
    }

    /// <summary>The problem the peeker found in the last package document (empty when none).</summary>
    public string Error { get; private set; } = string.Empty;

    /// <summary>The mapping document of multiple renditions, if the container file links one.</summary>
    public Url? MappingDocument { get; private set; }

    /// <summary>The package documents, in the order of the container file.</summary>
    public IReadOnlyList<Url> PackageDocuments => _packageDocuments;

    /// <summary>The container, as built from what was added so far.</summary>
    public OCFContainer Container
    {
        get
        {
            if (_container is null)
            {
                _container = _containerBuilder.Build();
                _context = _context with { Container = _container };
            }

            return _container;
        }
    }

    /// <summary>A context for a check of the publication (the container, version and profile known so far).</summary>
    public ValidationContext Context()
    {
        _ = Container;
        return _context;
    }

    public void AddResource(EpubSourceEntry entry)
    {
        _containerBuilder.AddResource(entry);
        _container = null;
    }

    public void AddEncryptedResource(Url resource, EncryptionKind kind)
    {
        _containerBuilder.AddEncryption(resource, kind);
        _container = null;
    }

    public void AddObfuscatedResource(Url resource, EpubLocation location) => _obfuscated[resource] = location;

    public bool IsObfuscated(Url resource) => _obfuscated.ContainsKey(resource);

    public EpubLocation ObfuscationLocation(Url resource) =>
        _obfuscated.TryGetValue(resource, out EpubLocation? location) ? location : throw new InvalidOperationException();

    public void AddRootfile(string mediaType, Url resource)
    {
        if (MediaTypes.Is(MediaTypes.PackageDocument, mediaType))
        {
            _packageDocuments.Add(resource);
        }

        _declaredResources.Add(resource);
    }

    public void AddMappingDocument(Url resource)
    {
        MappingDocument = resource;
        _declaredResources.Add(resource);
    }

    public bool IsDeclared(Url resource) => _declaredResources.Contains(resource);

    public void AddError(string? error) => Error = error ?? string.Empty;

    public void ErrorReset() => Error = string.Empty;

    public void AddTypes(Url url, IEnumerable<PublicationType> types)
    {
        if (!_publicationTypes.TryGetValue(url, out HashSet<PublicationType>? set))
        {
            _publicationTypes[url] = set = [];
        }

        set.UnionWith(types);
    }

    public void AddUniqueId(Url url, string id) => Put(_publicationIds, url, id);

    public void AddVersion(Url url, EpubVersion version) => Put(_publicationVersions, url, version);

    /// <summary>The unique identifier of the first package document peeked.</summary>
    public string? PublicationId => _publicationIds.Count > 0 ? _publicationIds[0].Value : null;

    /// <summary>The types of the first package document peeked.</summary>
    public IReadOnlySet<PublicationType> PublicationTypes =>
        _publicationTypes.Count > 0 ? _publicationTypes.First().Value : new HashSet<PublicationType>();

    public IReadOnlySet<PublicationType> PublicationTypesOf(Url url) =>
        _publicationTypes.TryGetValue(url, out HashSet<PublicationType>? types) ? types : new HashSet<PublicationType>();

    /// <summary>The version of the first package document peeked, or <c>null</c>.</summary>
    public EpubVersion? PublicationVersion => _publicationVersions.Count > 0 ? _publicationVersions[0].Value : null;

    public EpubVersion PublicationVersionOf(Url url)
    {
        foreach (KeyValuePair<Url, EpubVersion> pair in _publicationVersions)
        {
            if (pair.Key.Equals(url))
            {
                return pair.Value;
            }
        }

        return EpubVersion.Unknown;
    }

    public void SetVersion(EpubVersion version) => _context = _context with { Version = version };

    public void SetProfile(EpubProfile profile) => _context = _context with { Profile = profile };

    public void AddProperty(EpubCheckVocab.Property property) => _context = _context.WithProperty(property);

    // A LinkedHashMap's put: the value is replaced, the key keeps its place.
    private static void Put<T>(List<KeyValuePair<Url, T>> map, Url key, T value)
    {
        int index = map.FindIndex(p => p.Key.Equals(key));
        if (index < 0)
        {
            map.Add(new(key, value));
        }
        else
        {
            map[index] = new(key, value);
        }
    }
}
