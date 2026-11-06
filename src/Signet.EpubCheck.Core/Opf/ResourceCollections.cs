using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>A resource a package document links to (upstream's <c>LinkedResource</c>).</summary>
public sealed class LinkedResource
{
    internal LinkedResource(Url url, string? id, IEnumerable<VocabProperty>? rel, string? refines, string? mimeType)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Id = Optional(id);
        Rel = rel is null ? [] : [.. rel.Distinct()];
        Refines = Optional(refines);
        MimeType = Optional(mimeType);
    }

    /// <summary>The URL.</summary>
    public Url Url { get; }

    /// <summary>The URL without its fragment.</summary>
    public Url DocumentUrl => Url.WithFragment(null);

    /// <summary>The <c>id</c>, or <c>null</c>.</summary>
    public string? Id { get; }

    /// <summary>The <c>rel</c> properties.</summary>
    public IReadOnlyList<VocabProperty> Rel { get; }

    /// <summary>The <c>refines</c> attribute, or <c>null</c>.</summary>
    public string? Refines { get; }

    /// <summary>The <c>media-type</c> attribute, or <c>null</c>.</summary>
    public string? MimeType { get; }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The resources linked from the metadata or a collection, by id and URL (upstream's <c>LinkedResources</c>).</summary>
public sealed class LinkedResources
{
    private readonly Dictionary<string, LinkedResource> _byId = new(StringComparer.Ordinal);

    private LinkedResources(List<LinkedResource> resources)
    {
        AsList = resources;
        foreach (LinkedResource resource in resources)
        {
            if (resource.Id is not null)
            {
                _byId[resource.Id] = resource;
            }
        }
    }

    /// <summary>The resources, in order.</summary>
    public IReadOnlyList<LinkedResource> AsList { get; }

    /// <summary>The resource with the id, or <c>null</c>.</summary>
    public LinkedResource? GetById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The resources whose document URL (without the fragment) is <paramref name="url"/>.</summary>
    public IReadOnlyList<LinkedResource> GetByUrl(Url url) => [.. AsList.Where(resource => resource.DocumentUrl.Equals(url))];

    /// <summary>Whether a resource has the URL.</summary>
    public bool HasResource(Url url) => AsList.Any(resource => resource.DocumentUrl.Equals(url));

    /// <summary>A builder.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>Collects linked resources.</summary>
    public sealed class Builder
    {
        private readonly List<LinkedResource> _resources = [];

        /// <summary>Adds a resource.</summary>
        public Builder Add(LinkedResource? resource)
        {
            if (resource is not null)
            {
                _resources.Add(resource);
            }

            return this;
        }

        /// <summary>Builds the set.</summary>
        public LinkedResources Build() => new([.. _resources]);
    }
}

/// <summary>The roles upstream knows for a collection (upstream's <c>ResourceCollection.Roles</c>).</summary>
public enum CollectionRole
{
    /// <summary><c>dictionary</c>.</summary>
    Dictionary,

    /// <summary><c>distributable-object</c>.</summary>
    DistributableObject,

    /// <summary><c>index</c>.</summary>
    Index,

    /// <summary><c>index-group</c>.</summary>
    IndexGroup,

    /// <summary><c>manifest</c>.</summary>
    Manifest,

    /// <summary><c>preview</c>.</summary>
    Preview,

    /// <summary><c>scriptable-component</c>.</summary>
    ScriptableComponent,
}

/// <summary>Helpers of <see cref="CollectionRole"/>.</summary>
public static class CollectionRoles
{
    /// <summary>The text of the role: lower case with hyphens.</summary>
    public static string ToText(this CollectionRole role)
    {
        string name = role.ToString();
        System.Text.StringBuilder text = new();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append('-');
            }

            text.Append(char.ToLower(name[i], CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>The role named <paramref name="role"/>, or <c>null</c>.</summary>
    public static CollectionRole? FromText(string? role) =>
        Enum.GetValues<CollectionRole>().Cast<CollectionRole?>().FirstOrDefault(candidate =>
            string.Equals(candidate!.Value.ToText(), (role ?? string.Empty).Replace('_', '-'), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A collection of the package document (upstream's <c>ResourceCollection</c>, renamed: .NET reserves the suffix): roles, metadata, links, resources and sub-collections.</summary>
public sealed class CollectionNode
{
    private CollectionNode(IReadOnlySet<string> roles, MetadataSet metadata, LinkedResources metadataLinks, ResourceCollections collections, LinkedResources resources)
    {
        Roles = roles;
        Metadata = metadata;
        MetadataLinks = metadataLinks;
        Collections = collections;
        Resources = resources;
    }

    /// <summary>The roles.</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>The metadata of the collection.</summary>
    public MetadataSet Metadata { get; }

    /// <summary>The links of the metadata.</summary>
    public LinkedResources MetadataLinks { get; }

    /// <summary>The sub-collections.</summary>
    public ResourceCollections Collections { get; }

    /// <summary>The resources of the collection.</summary>
    public LinkedResources Resources { get; }

    /// <summary>Whether the collection has the role <paramref name="role"/>.</summary>
    public bool HasRole(string? role) => role is not null && Roles.Contains(role);

    /// <summary>Whether the collection has the role <paramref name="role"/>.</summary>
    public bool HasRole(CollectionRole role) => Roles.Contains(role.ToText());

    /// <summary>A builder.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>Builds a collection.</summary>
    public sealed class Builder
    {
        private readonly HashSet<string> _roles = new(StringComparer.Ordinal);
        private readonly ResourceCollections.Builder _collections = ResourceCollections.CreateBuilder();
        private MetadataSet? _metadata;
        private LinkedResources? _metadataLinks;
        private LinkedResources? _resources;

        /// <summary>Adds roles.</summary>
        public Builder Roles(IEnumerable<string> roles)
        {
            _roles.UnionWith(roles);
            return this;
        }

        /// <summary>Sets the metadata.</summary>
        public Builder Metadata(MetadataSet? metadata)
        {
            _metadata = metadata;
            return this;
        }

        /// <summary>Sets the links of the metadata.</summary>
        public Builder MetadataLinks(LinkedResources? links)
        {
            _metadataLinks = links;
            return this;
        }

        /// <summary>Adds a sub-collection.</summary>
        public Builder Collection(CollectionNode collection)
        {
            _collections.Add(collection);
            return this;
        }

        /// <summary>Sets the resources.</summary>
        public Builder Resources(LinkedResources? resources)
        {
            _resources = resources;
            return this;
        }

        /// <summary>Builds the collection.</summary>
        public CollectionNode Build() => new(
            new HashSet<string>(_roles, StringComparer.Ordinal),
            _metadata ?? MetadataSet.CreateBuilder().Build(),
            _metadataLinks ?? LinkedResources.CreateBuilder().Build(),
            _collections.Build(),
            _resources ?? LinkedResources.CreateBuilder().Build());
    }
}

/// <summary>The collections of the package document or of a collection, by role (upstream's <c>ResourceCollections</c>).</summary>
public sealed class ResourceCollections
{
    private ResourceCollections(List<CollectionNode> collections)
    {
        AsList = collections;
    }

    /// <summary>The collections, in order.</summary>
    public IReadOnlyList<CollectionNode> AsList { get; }

    /// <summary>The collections that have the role.</summary>
    public IReadOnlyList<CollectionNode> GetByRole(string role) => [.. AsList.Where(collection => collection.HasRole(role))];

    /// <summary>The collections that have the role.</summary>
    public IReadOnlyList<CollectionNode> GetByRole(CollectionRole role) => GetByRole(role.ToText());

    /// <summary>Whether a collection has the role.</summary>
    public bool HasRole(string role) => AsList.Any(collection => collection.HasRole(role));

    /// <summary>Whether a collection has the role.</summary>
    public bool HasRole(CollectionRole role) => HasRole(role.ToText());

    /// <summary>A builder.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>Collects collections.</summary>
    public sealed class Builder
    {
        private readonly List<CollectionNode> _collections = [];

        /// <summary>Adds a collection.</summary>
        public Builder Add(CollectionNode? collection)
        {
            if (collection is not null)
            {
                _collections.Add(collection);
            }

            return this;
        }

        /// <summary>Builds the set.</summary>
        public ResourceCollections Build() => new([.. _collections]);
    }
}
