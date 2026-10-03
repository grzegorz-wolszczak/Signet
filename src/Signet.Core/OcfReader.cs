using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SysPath = System.IO.Path;
using Signet.Core.Localization;

namespace Signet.Core;

/// <summary>
/// Unpacks an <c>.epub</c> archive (ZIP/OCF) into a working folder and reads
/// the container structure: the path to the OPF from <c>META-INF/container.xml</c> and the encryption
/// map from <c>META-INF/encryption.xml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Problems with the <c>mimetype</c> file (missing, wrong content, compression,
/// not the first entry) are <b>warnings</b>, not errors — readers open
/// such EPUBs. <c>encryption.xml</c> is not removed from the working copy (export
/// regenerates it when saving). CP437 entry names are not
/// supported (UTF-8 is assumed).
/// </para>
/// </remarks>
public sealed class OcfReader
{
    /// <summary>The required content of the <c>mimetype</c> file.</summary>
    public const string OcfMimetype = "application/epub+zip";

    /// <summary>The media-type of the OPF file in <c>container.xml</c>.</summary>
    public const string OpfMediaType = "application/oebps-package+xml";

    /// <summary>The IDPF font obfuscation algorithm.</summary>
    public const string IdpfFontAlgorithmId = "http://www.idpf.org/2008/embedding";

    /// <summary>The Adobe font obfuscation algorithm.</summary>
    public const string AdobeFontAlgorithmId = "http://ns.adobe.com/pdf/enc#RC";

    private const string MimetypeEntryName = "mimetype";
    private const string MetaInfDirectory = "META-INF";

    private readonly string _epubPath;

    /// <summary>Creates a reader for the given <c>.epub</c> file.</summary>
    /// <param name="epubPath">The path of the EPUB archive.</param>
    public OcfReader(string epubPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(epubPath);
        _epubPath = epubPath;
    }

    /// <summary>Unpacks the EPUB into <paramref name="destination"/> and reads the container.</summary>
    public OcfContainer Extract(TempFolder destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Extract(destination.Path);
    }

    /// <summary>Unpacks the EPUB into <paramref name="destinationDirectory"/> and reads the container.</summary>
    /// <param name="destinationDirectory">The target working folder (it will be created).</param>
    /// <exception cref="EpubLoadException">The EPUB is corrupted or incomplete.</exception>
    public OcfContainer Extract(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        if (!File.Exists(_epubPath))
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_EpubNotFound", _epubPath));
        }

        string root = SysPath.GetFullPath(destinationDirectory)
            .TrimEnd(SysPath.DirectorySeparatorChar, SysPath.AltDirectorySeparatorChar);
        Directory.CreateDirectory(root);

        List<string> warnings = new();

        ExtractArchive(root, warnings);
        IReadOnlyList<string> rootfiles = LocateRootfiles(root, warnings);
        IReadOnlyDictionary<string, string> encrypted = ReadEncryptionXml(root, warnings);

        return new OcfContainer
        {
            RootDirectory = root,
            OpfBookPath = rootfiles[0],
            Rootfiles = rootfiles,
            EncryptedFiles = encrypted,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Reads the OCF container from an <b>already unpacked</b> folder (e.g. the book's working folder or
    /// a checkpoint snapshot): locates the OPF via <c>META-INF/container.xml</c> and reads
    /// <c>encryption.xml</c>, without unpacking or modifying anything.
    /// </summary>
    /// <param name="rootDirectory">The publication's root folder.</param>
    /// <exception cref="EpubLoadException">The folder does not contain a correctly referenced OPF.</exception>
    public static OcfContainer ReadExtracted(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        string root = SysPath.GetFullPath(rootDirectory)
            .TrimEnd(SysPath.DirectorySeparatorChar, SysPath.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_PublicationFolderNotFound", root));
        }

        List<string> warnings = new();
        IReadOnlyList<string> rootfiles = LocateRootfiles(root, warnings);
        IReadOnlyDictionary<string, string> encrypted = ReadEncryptionXml(root, warnings);

        return new OcfContainer
        {
            RootDirectory = root,
            OpfBookPath = rootfiles[0],
            Rootfiles = rootfiles,
            EncryptedFiles = encrypted,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Parses the content of <c>container.xml</c> and returns the bookpaths of the OPF files (<c>rootfile</c>
    /// with <c>media-type="application/oebps-package+xml"</c>), in order of occurrence.
    /// </summary>
    /// <exception cref="EpubLoadException">The XML is syntactically invalid.</exception>
    public static IReadOnlyList<string> ParseContainerXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document = ParseXml(xml, "container.xml");

        List<string> rootfiles = new();
        foreach (XElement rootfile in document.Descendants().Where(e => e.Name.LocalName == "rootfile"))
        {
            string? mediaType = (string?)rootfile.Attribute("media-type");
            string? fullPath = (string?)rootfile.Attribute("full-path");

            if (fullPath is null || mediaType != OpfMediaType)
            {
                continue;
            }

            string bookPath = NormalizeInternalPath(fullPath);
            if (bookPath.Length != 0 && !rootfiles.Contains(bookPath))
            {
                rootfiles.Add(bookPath);
            }
        }

        return rootfiles;
    }

    /// <summary>
    /// Parses the content of <c>encryption.xml</c> and returns a map of bookpath -&gt; algorithm identifier.
    /// URIs are percent-decoded and reduced to bookpaths.
    /// </summary>
    /// <exception cref="EpubLoadException">The XML is syntactically invalid.</exception>
    public static IReadOnlyDictionary<string, string> ParseEncryptionXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document = ParseXml(xml, "encryption.xml");

        Dictionary<string, string> encrypted = new(StringComparer.Ordinal);
        string currentAlgorithm = string.Empty;

        // Document order: EncryptionMethod precedes the matching
        // CipherReference in the same EncryptedData.
        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName == "EncryptionMethod")
            {
                currentAlgorithm = (string?)element.Attribute("Algorithm") ?? string.Empty;
            }
            else if (element.Name.LocalName == "CipherReference")
            {
                string? uri = (string?)element.Attribute("URI");
                if (uri is null)
                {
                    continue;
                }

                string bookPath = NormalizeInternalPath(Uri.UnescapeDataString(uri));
                encrypted[bookPath] = currentAlgorithm;
            }
        }

        return encrypted;
    }

    private void ExtractArchive(string root, List<string> warnings)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(_epubPath);
        }
        catch (InvalidDataException ex)
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_CorruptZip", _epubPath), ex);
        }

        using (archive)
        {
            ValidateMimetype(archive, warnings);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ExtractEntry(entry, root);
            }
        }
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string root)
    {
        string rawName = entry.FullName;
        if (rawName.Length == 0)
        {
            return;
        }

        // Security (a malicious/corrupted archive): reject backslashes
        // and ".." segments in an entry name.
        if (rawName.Contains('\\', StringComparison.Ordinal))
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_SuspiciousEntryName", rawName));
        }

        string normalized = rawName.Normalize(NormalizationForm.FormC);
        string[] segments = normalized.Split('/');
        if (segments.Any(s => s == ".."))
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_SuspiciousEntryName", rawName));
        }

        // A directory entry.
        if (normalized.EndsWith('/'))
        {
            Directory.CreateDirectory(SysPath.Combine(root, normalized.Replace('/', SysPath.DirectorySeparatorChar)));
            return;
        }

        string relative = normalized.Replace('/', SysPath.DirectorySeparatorChar);
        string destination = SysPath.GetFullPath(SysPath.Combine(root, relative));

        if (!destination.StartsWith(root + SysPath.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_EntryOutsideTarget", rawName));
        }

        string? parent = SysPath.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        try
        {
            entry.ExtractToFile(destination, overwrite: true);
        }
        catch (InvalidDataException ex)
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_CannotExtractEntry", rawName), ex);
        }
    }

    private static void ValidateMimetype(ZipArchive archive, List<string> warnings)
    {
        ZipArchiveEntry? mimetype = archive.GetEntry(MimetypeEntryName);

        if (mimetype is null)
        {
            warnings.Add(CoreStrings.Get("LoadWarning_MimetypeMissing"));
            return;
        }

        if (archive.Entries.Count == 0 || !ReferenceEquals(archive.Entries[0], mimetype))
        {
            warnings.Add(CoreStrings.Get("LoadWarning_MimetypeNotFirst"));
        }

        if (mimetype.CompressedLength != mimetype.Length)
        {
            warnings.Add(CoreStrings.Get("LoadWarning_MimetypeCompressed"));
        }

        string content;
        using (StreamReader reader = new(mimetype.Open(), Encoding.ASCII))
        {
            content = reader.ReadToEnd();
        }

        if (content != OcfMimetype)
        {
            warnings.Add(content.Trim() == OcfMimetype
                ? CoreStrings.Get("LoadWarning_MimetypeWhitespace")
                : CoreStrings.Format("LoadWarning_MimetypeWrongContent", content, OcfMimetype));
        }
    }

    private static IReadOnlyList<string> LocateRootfiles(string root, List<string> warnings)
    {
        string containerPath = SysPath.Combine(root, MetaInfDirectory, "container.xml");

        if (!File.Exists(containerPath))
        {
            string? fallback = FindFirstOpf(root);
            if (fallback is null)
            {
                throw new EpubLoadException(
                    CoreStrings.Get("LoadError_NoContainerNoOpf"), warnings);
            }

            warnings.Add(CoreStrings.Get("LoadWarning_NoContainerUsedFirstOpf"));
            return new[] { fallback };
        }

        IReadOnlyList<string> rootfiles = ParseContainerXml(File.ReadAllText(containerPath));

        if (rootfiles.Count == 0)
        {
            throw new EpubLoadException(
                CoreStrings.Get("LoadError_ContainerNoRootfile"), warnings);
        }

        if (rootfiles.Count > 1)
        {
            warnings.Add(CoreStrings.Format("LoadWarning_MultipleRenditions", rootfiles.Count));
        }

        string opfFullPath = SysPath.Combine(root, rootfiles[0].Replace('/', SysPath.DirectorySeparatorChar));
        if (!File.Exists(opfFullPath))
        {
            throw new EpubLoadException(
                CoreStrings.Format("LoadError_OpfFromContainerMissing", rootfiles[0]), warnings);
        }

        return rootfiles;
    }

    private static IReadOnlyDictionary<string, string> ReadEncryptionXml(string root, List<string> warnings)
    {
        string path = SysPath.Combine(root, MetaInfDirectory, "encryption.xml");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return ParseEncryptionXml(File.ReadAllText(path));
        }
        catch (EpubLoadException ex)
        {
            warnings.Add(CoreStrings.Format("LoadWarning_EncryptionXmlIgnored", ex.Message));
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string? FindFirstOpf(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.opf", SearchOption.AllDirectories)
            .Select(path => SysPath.GetRelativePath(root, path).Replace(SysPath.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static XDocument ParseXml(string xml, string what)
    {
        try
        {
            return XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new EpubLoadException(CoreStrings.Format("LoadError_CannotParse", what, ex.Message), ex);
        }
    }

    private static string NormalizeInternalPath(string path)
    {
        string forward = path.Replace('\\', '/');
        return BookPath.ResolveRelativeSegments(forward).TrimStart('/');
    }
}
