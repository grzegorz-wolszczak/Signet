using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>Opens the resources a check reads (upstream's <c>GenericResourceProvider</c>).</summary>
public interface IResourceProvider
{
    /// <summary>Opens the resource at <paramref name="url"/>.</summary>
    /// <exception cref="IOException">The resource cannot be read.</exception>
    Stream OpenStream(Url url);
}

/// <summary>
/// What one check works on (upstream's <c>ValidationContext</c>; its builder is the <c>with</c> expression): the URL of
/// the checked resource, its media type, the EPUB version and profile, the report, the container it is in. The part of
/// upstream's context the checks ported so far use; the registries of resources and references come with the checks
/// that fill them.
/// </summary>
public sealed record ValidationContext
{
    /// <summary>The URL of the checked resource: in the container, or a <c>file:</c> URL.</summary>
    public required Url Url { get; init; }

    /// <summary>The media type of the checked resource (empty when not known).</summary>
    public string MimeType { get; init; } = string.Empty;

    /// <summary>The EPUB version the resource is checked against.</summary>
    public EpubVersion Version { get; init; } = EpubVersion.Unknown;

    /// <summary>The validation profile.</summary>
    public EpubProfile Profile { get; init; } = EpubProfile.Default;

    /// <summary>Where the messages go.</summary>
    public required IReport Report { get; init; }

    /// <summary>The language of the messages of the schema languages (upstream: the locale of the report).</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>What opens the resources when there is no container (a single file checked alone).</summary>
    public IResourceProvider? ResourceProvider { get; init; }

    /// <summary>The container the resource is in, or <c>null</c> for a single file checked alone.</summary>
    public OCFContainer? Container { get; init; }

    /// <summary>The publication types (<c>dc:type</c>).</summary>
    public IReadOnlySet<PublicationType> PublicationTypes { get; init; } = new HashSet<PublicationType>();

    /// <summary>EPUBCheck's own properties of the check (upstream's <c>EpubCheckVocab</c>: <c>multiple-rendition</c>, …).</summary>
    public IReadOnlySet<EpubCheckVocab.Property> Properties { get; init; } = new HashSet<EpubCheckVocab.Property>();

    /// <summary>The path of the resource in messages: relative to the container, or the file path.</summary>
    public string Path => ComputePath();

    /// <summary>The provider of the resources: the container, else <see cref="ResourceProvider"/>, else local files.</summary>
    public IResourceProvider Resources => (IResourceProvider?)Container ?? ResourceProvider ?? FileResourceProvider.Instance;

    /// <summary>Where a message about the resource as a whole points (upstream's <c>EPUBLocation.of(context)</c>).</summary>
    public EpubLocation Location() => OCFContainer.LocationOf(Url, Container);

    /// <summary>Whether <paramref name="url"/> is outside the publication.</summary>
    public bool IsRemote(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Container is not null ? Container.IsRemote(url) : EpubCheckUrlUtils.IsRemote(url, Url);
    }

    /// <summary><paramref name="url"/> relative to the container root, or to the checked resource.</summary>
    public string Relativize(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Container is not null ? Container.Relativize(url) : Url.Relativize(url);
    }

    /// <summary>Whether the check has the property <paramref name="property"/>.</summary>
    public bool HasProperty(EpubCheckVocab.Property property) => Properties.Contains(property);

    /// <summary>This context with <paramref name="property"/> added.</summary>
    public ValidationContext WithProperty(EpubCheckVocab.Property property)
    {
        HashSet<EpubCheckVocab.Property> properties = new(Properties) { property };
        return this with { Properties = properties };
    }

    private string ComputePath()
    {
        if (Container is not null && !Container.IsRemote(Url))
        {
            return string.IsNullOrEmpty(Url.Path) ? string.Empty : Url.Path[1..];
        }

        return Url.Scheme == Url.FileScheme ? EpubCheckUrlUtils.ToFilePath(Url) : Url.ToHumanString();
    }

    // Upstream's URLResourceProvider, for local files only: remote resources are never fetched.
    private sealed class FileResourceProvider : IResourceProvider
    {
        public static FileResourceProvider Instance { get; } = new();

        public Stream OpenStream(Url url)
        {
            if (url.Scheme != Url.FileScheme)
            {
                throw new IOException("Remote resources are not read: " + url);
            }

            return File.OpenRead(EpubCheckUrlUtils.ToFilePath(url));
        }
    }
}
