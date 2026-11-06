using System;
using System.Collections.Generic;
using System.IO;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>How a resource of the container is encrypted (upstream's <c>EncryptionFilter</c> implementations).</summary>
public enum EncryptionKind
{
    /// <summary>IDPF font obfuscation (<c>http://www.idpf.org/2008/embedding</c>).</summary>
    IdpfFontObfuscation,

    /// <summary>Adobe font obfuscation (<c>http://ns.adobe.com/pdf/enc#RC</c>).</summary>
    AdobeFontObfuscation,

    /// <summary>Any other algorithm: the resource cannot be read.</summary>
    Unsupported,
}

/// <summary>
/// The files of an OCF container by their URL (upstream's <c>OCFContainer</c>): each file gets a URL under a root URL
/// unique to the check (<c>https://&lt;uuid&gt;.epubcheck.w3c.org/</c>), which is what the URLs of the publication
/// resolve against; and how the encrypted ones are encrypted.
/// </summary>
public sealed class OCFContainer : IResourceProvider
{
    private readonly IEpubSource _source;
    private readonly Dictionary<Url, EpubSourceEntry> _resources;
    private readonly Dictionary<Url, EncryptionKind> _encryption;

    private OCFContainer(Builder builder)
    {
        _source = builder.Source;
        RootUrl = builder.RootUrl;
        IsPackaged = builder.Source.IsPackaged;
        _resources = new Dictionary<Url, EpubSourceEntry>(builder.Resources);
        _encryption = new Dictionary<Url, EncryptionKind>(builder.Encryption);
    }

    /// <summary>The root URL of the container.</summary>
    public Url RootUrl { get; }

    /// <summary>Whether the container is a ZIP file (not a folder).</summary>
    public bool IsPackaged { get; }

    /// <summary>The URLs of the files, in the order of the container.</summary>
    public IReadOnlyCollection<Url> Resources => _resources.Keys;

    /// <summary>Where a message about <paramref name="url"/> points (upstream's <c>EPUBLocation.of(url, container)</c>).</summary>
    public static EpubLocation LocationOf(Url url, OCFContainer? container)
    {
        ArgumentNullException.ThrowIfNull(url);
        string path;
        if (container is not null)
        {
            path = container.Relativize(url);
        }
        else if (url.Scheme == Url.FileScheme && url.Authority is null or "")
        {
            path = PathUtil.RemoveWorkingDirectory(url.Path ?? string.Empty);
        }
        else
        {
            path = url.ToString();
        }

        return new EpubLocation(path);
    }

    /// <summary>Whether the container has a file at <paramref name="url"/>.</summary>
    public bool Contains(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return _resources.ContainsKey(url) || (EpubCheckUrlUtils.Normalize(url) is { } normalized && _resources.ContainsKey(normalized));
    }

    /// <inheritdoc />
    public Stream OpenStream(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Url key = EpubCheckUrlUtils.Normalize(url) ?? url;
        if (!_resources.TryGetValue(key, out EpubSourceEntry? entry))
        {
            throw new ArgumentException("Resource not found: " + url, nameof(url));
        }

        return _source.OpenRead(entry);
    }

    /// <summary>Whether the file at <paramref name="url"/> can be read: not encrypted, or obfuscated as a font.</summary>
    public bool CanDecrypt(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!_resources.ContainsKey(url))
        {
            throw new ArgumentException("Resource not found: " + url, nameof(url));
        }

        return !_encryption.TryGetValue(url, out EncryptionKind kind) || kind != EncryptionKind.Unsupported;
    }

    /// <summary><paramref name="url"/> relative to the root of the container.</summary>
    public string Relativize(Url url) => RootUrl.Relativize(url);

    /// <summary>Whether <paramref name="url"/> is outside the container.</summary>
    public bool IsRemote(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Scheme != "data" && !Contains(url) && EpubCheckUrlUtils.IsRemote(url, RootUrl);
    }

    /// <summary>Builds a container (upstream's <c>OCFContainer.Builder</c>).</summary>
    public sealed class Builder
    {
        private const string RootUrlStart = "https://";
        private const string RootUrlEnd = ".epubcheck.w3c.org";

        /// <summary>A builder for the files of <paramref name="source"/>.</summary>
        public Builder(IEpubSource source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            RootUrl = Url.Parse(RootUrlStart + Guid.NewGuid() + RootUrlEnd);
        }

        internal IEpubSource Source { get; }

        internal Url RootUrl { get; }

        internal Dictionary<Url, EpubSourceEntry> Resources { get; } = [];

        internal Dictionary<Url, EncryptionKind> Encryption { get; } = [];

        /// <summary>Adds a file of the container.</summary>
        public void AddResource(EpubSourceEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            Url url = RootUrl.Resolve(EpubCheckUrlUtils.EncodePath(entry.Name)!);
            Resources[EpubCheckUrlUtils.Normalize(url) ?? url] = entry;
        }

        /// <summary>Records how the file at <paramref name="url"/> is encrypted.</summary>
        public void AddEncryption(Url url, EncryptionKind kind)
        {
            ArgumentNullException.ThrowIfNull(url);
            if (!Resources.ContainsKey(url))
            {
                throw new ArgumentException(url + " was not found in the container", nameof(url));
            }

            Encryption[url] = kind;
        }

        /// <summary>The container.</summary>
        public OCFContainer Build() => new(this);
    }
}

/// <summary>The files of the OCF container with a fixed path (upstream's <c>OCFMetaFile</c>).</summary>
public enum OCFMetaFile
{
    /// <summary><c>mimetype</c>.</summary>
    Mimetype,

    /// <summary><c>META-INF/container.xml</c>.</summary>
    Container,

    /// <summary><c>META-INF/encryption.xml</c>.</summary>
    Encryption,

    /// <summary><c>META-INF/metadata.xml</c>.</summary>
    Metadata,

    /// <summary><c>META-INF/signatures.xml</c>.</summary>
    Signatures,
}

/// <summary>The paths and URLs of the <see cref="OCFMetaFile"/>s.</summary>
public static class OCFMetaFiles
{
    /// <summary>The path of the file in the container.</summary>
    public static string AsPath(this OCFMetaFile file) => file switch
    {
        OCFMetaFile.Mimetype => OCFChecker.MimeTypePath,
        OCFMetaFile.Container => OCFChecker.ContainerPath,
        OCFMetaFile.Encryption => "META-INF/encryption.xml",
        OCFMetaFile.Metadata => "META-INF/metadata.xml",
        OCFMetaFile.Signatures => "META-INF/signatures.xml",
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, null),
    };

    /// <summary>The URL of the file in <paramref name="container"/>.</summary>
    public static Url AsUrl(this OCFMetaFile file, OCFContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.RootUrl.Resolve(file.AsPath());
    }

    /// <summary>Where a message about the file points.</summary>
    public static EpubLocation AsLocation(this OCFMetaFile file, OCFContainer container) =>
        OCFContainer.LocationOf(file.AsUrl(container), container);

    /// <summary>Whether <paramref name="container"/> has the file.</summary>
    public static bool IsPresent(this OCFMetaFile file, OCFContainer container) => container.Contains(file.AsUrl(container));
}
