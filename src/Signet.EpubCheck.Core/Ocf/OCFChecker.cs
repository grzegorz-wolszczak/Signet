using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Util.Mime;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Checks the OCF container of a publication (upstream's <c>OCFChecker</c>), in upstream's order: the ZIP header, the
/// container structure (duplicate names, file names, empty directories), the <c>mimetype</c> file, <c>META-INF/container.xml</c>
/// (the package documents it declares and what the peeker reads of them), the version and profile of the publication, the
/// other files of <c>META-INF</c> (<c>encryption.xml</c>, <c>metadata.xml</c>, <c>signatures.xml</c>) and the file
/// extension. The checks of the package documents and of the resources they declare come with later tasks.
/// </summary>
public static class OCFChecker
{
    /// <summary>The path of the <c>mimetype</c> file in the container.</summary>
    public const string MimeTypePath = "mimetype";

    /// <summary>The path of the container file in the container.</summary>
    public const string ContainerPath = "META-INF/container.xml";

    /// <summary>The only content the <c>mimetype</c> file may have.</summary>
    public const string EpubMimeType = "application/epub+zip";

    // The localized argument of PKG-007 that adds "with no whitespace or newline." (PKG_007.whitespace).
    private const string WhitespaceArgument = "whitespace";
    private const string CreationDateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// Checks the publication at <paramref name="path"/>: a ZIP file (packaged) or a directory (expanded). Archives that
    /// cannot be read are reported (<see cref="MessageId.PKG_008"/>, <see cref="MessageId.PKG_027"/>), never thrown.
    /// </summary>
    public static void Check(string path, EpubVersion version, IReport report, EpubProfile profile = EpubProfile.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);

        EpubLocation location = new(path);
        bool packaged = File.Exists(path);
        if (packaged)
        {
            OCFZipChecker.Check(path, report, location);
        }

        IEpubSource source;
        try
        {
            source = packaged ? new ZipEpubSource(path) : new DirectoryEpubSource(path);
        }
        catch (EpubZipEncodingException ex)
        {
            report.Message(MessageId.PKG_027, location, ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            report.Message(MessageId.PKG_008, location, ex.Message);
            return;
        }

        using (source)
        {
            Check(source, version, report, location, profile);
            if (packaged)
            {
                CheckFileExtension(path, version, report, location);
            }
        }
    }

    /// <summary>
    /// Checks an open source, reporting the problems of the publication as a whole at <paramref name="location"/>. The
    /// ZIP header and the file extension are not checked here (they need the file: <see cref="Check(string, EpubVersion, IReport, EpubProfile)"/>).
    /// </summary>
    public static void Check(IEpubSource source, EpubVersion version, IReport report, EpubLocation location, EpubProfile profile = EpubProfile.Default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(location);

        ValidationContext context = new()
        {
            Url = EpubCheckUrlUtils.ToUrl(location.Path),
            Report = report,
            Version = version,
            Profile = profile,
            MimeType = MediaTypes.Epub,
            Culture = (report as ReportBase)?.Culture,
        };
        OCFCheckerState state = new(context, source);

        CheckContainerStructure(source, state, version, report, location);
        CheckMimeTypeFile(source, report, location);
        if (!CheckContainerFile(state, report, location) || !CheckDeclaredPackageDocuments(state, report))
        {
            return;
        }

        EpubVersion validationVersion = CheckPublicationVersion(state, version, report);
        state.SetVersion(validationVersion);
        state.SetProfile(CheckPublicationProfile(state, profile, validationVersion, report));
        CheckRenditions(state, validationVersion, report, location);
        CheckEncryptionFile(state, report);
        CheckOtherMetaFiles(state, report);
        CheckPackageDocuments(state, validationVersion, report, location);
    }

    // Each package document is checked by the checker of the version; the checker of EPUB 3 package documents comes with
    // task 3.3, so only EPUB 2 is checked (and the consistency of the container with what the documents declare).
    private static void CheckPackageDocuments(OCFCheckerState state, EpubVersion version, IReport report, EpubLocation location)
    {
        if (version != EpubVersion.Version2)
        {
            return;
        }

        List<OPFHandler> handlers = [];
        foreach (Url packageDocument in state.PackageDocuments)
        {
            ValidationContext context = state.Context() with
            {
                Url = packageDocument,
                MimeType = MediaTypes.PackageDocument,
                PublicationTypes = state.PublicationTypesOf(packageDocument),
            };
            OPFChecker checker = new(context);
            checker.Check();
            handlers.Add(checker.OpfHandler!);
        }

        CheckContainerConsistency(state, handlers, report, location);
    }

    // Upstream: the files of the container that neither belong to the OCF (META-INF, mimetype) nor are declared by the
    // container file or one of the package documents are reported (OPF-003); an obfuscated resource must be a font (PKG-026).
    private static void CheckContainerConsistency(OCFCheckerState state, List<OPFHandler> handlers, IReport report, EpubLocation location)
    {
        foreach (Url resource in state.Container.Resources)
        {
            string path = (resource.Path ?? string.Empty).Length > 0 ? resource.Path![1..] : string.Empty;
            if (!path.StartsWith("META-INF/", StringComparison.Ordinal) && !path.StartsWith("META-INF\\", StringComparison.Ordinal)
                && path != OCFChecker.MimeTypePath && !state.IsDeclared(resource) && !handlers.Exists(handler => handler.GetItemByUrl(resource) is not null))
            {
                report.Message(MessageId.OPF_003, location, path);
            }

            if (state.IsObfuscated(resource))
            {
                foreach (OPFHandler handler in handlers)
                {
                    if (handler.GetItemByUrl(resource) is { } item)
                    {
                        if (!OpfMediaTypes.IsBlessedFontType(item.MimeType))
                        {
                            report.Message(MessageId.PKG_026, state.ObfuscationLocation(resource), item.MimeType, handler.PackagePath);
                        }

                        break;
                    }
                }
            }
        }
    }

    private static void CheckContainerStructure(IEpubSource source, OCFCheckerState state, EpubVersion version, IReport report, EpubLocation location)
    {
        HashSet<string> normalizedPaths = new(StringComparer.Ordinal);
        List<string> filePaths = [];
        List<string> directoryPaths = [];
        foreach (EpubSourceEntry entry in source.Entries)
        {
            if (entry.IsDirectory)
            {
                directoryPaths.Add(entry.Name);
                continue;
            }

            filePaths.Add(entry.Name);
            if (!normalizedPaths.Add(UnicodeUtils.CanonicalCaseFold(entry.Name)))
            {
                report.Message(MessageId.OPF_060, location, entry.Name);
            }

            OCFFilenameChecker.Check(entry.Name, version, report, location);
            ReportFeatures(source, entry, report);
            state.AddResource(entry);
        }

        // Directory names end with '/', so "a/" does not take "ab.xhtml" for its content.
        foreach (string directoryPath in directoryPaths)
        {
            if (!filePaths.Any(filePath => filePath.StartsWith(directoryPath, StringComparison.Ordinal)))
            {
                report.Message(MessageId.PKG_014, location, directoryPath);
            }
        }
    }

    private static void ReportFeatures(IEpubSource source, EpubSourceEntry entry, IReport report)
    {
        report.Info(entry.Name, FeatureKind.Size, entry.Length.ToString(CultureInfo.InvariantCulture));
        report.Info(entry.Name, FeatureKind.CompressedSize, entry.CompressedLength.ToString(CultureInfo.InvariantCulture));
        report.Info(entry.Name, FeatureKind.CompressionMethod, entry.CompressionMethod switch
        {
            CompressionMethod.Stored => "Stored",
            CompressionMethod.Deflate => "Deflated",
            _ => "Unsupported",
        });
        report.Info(entry.Name, FeatureKind.Sha256, Sha256(source, entry));

        // The creation date is reported once, for the container file.
        if (entry.Name == ContainerPath && entry.Timestamp is DateTime timestamp)
        {
            report.Info(entry.Name, FeatureKind.CreationDate, timestamp.ToString(CreationDateFormat, CultureInfo.InvariantCulture));
        }
    }

    private static string Sha256(IEpubSource source, EpubSourceEntry entry)
    {
        try
        {
            using Stream stream = source.OpenRead(entry);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static void CheckMimeTypeFile(IEpubSource source, IReport report, EpubLocation location)
    {
        EpubSourceEntry? entry = source.Entries.FirstOrDefault(candidate => !candidate.IsDirectory && candidate.Name == MimeTypePath);
        if (entry is null)
        {
            // A packaged container's missing mimetype is reported by the ZIP header check.
            if (!source.IsPackaged)
            {
                report.Message(MessageId.PKG_006, new EpubLocation(MimeTypePath));
            }

            return;
        }

        string content;
        try
        {
            using Stream stream = source.OpenRead(entry);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            content = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return; // as upstream: a file that cannot be read is reported elsewhere
        }

        string trimmed = JavaStrings.Trim(content);
        if (trimmed != EpubMimeType)
        {
            report.Message(MessageId.PKG_007, location, string.Empty);
        }
        else if (content != EpubMimeType)
        {
            report.Message(MessageId.PKG_007, location, WhitespaceArgument);
        }

        report.Info(null, FeatureKind.FormatName, trimmed);
    }

    private static bool CheckContainerFile(OCFCheckerState state, IReport report, EpubLocation location)
    {
        OCFContainer container = state.Container;
        if (!OCFMetaFile.Container.IsPresent(container))
        {
            // Not reported when a fatal error was already reported.
            if (report.FatalErrorCount == 0)
            {
                report.Message(MessageId.RSC_002, location);
            }

            return false;
        }

        OCFContainerFileChecker.Check(state.Context() with { Url = OCFMetaFile.Container.AsUrl(container), MimeType = MediaTypes.Xml }, state);
        return true;
    }

    private static bool CheckDeclaredPackageDocuments(OCFCheckerState state, IReport report)
    {
        IReadOnlyList<Url> packageDocuments = state.PackageDocuments;
        OCFContainer container = state.Container;
        if (packageDocuments.Count == 0)
        {
            report.Message(MessageId.RSC_003, OCFMetaFile.Container.AsLocation(container));
            return false;
        }

        if (packageDocuments.Count > 1)
        {
            report.Info(null, FeatureKind.EpubRenditionsCount, packageDocuments.Count.ToString(CultureInfo.InvariantCulture));
        }

        bool isPrimary = true;
        foreach (Url packageDocument in packageDocuments)
        {
            if (!container.Contains(packageDocument))
            {
                report.Message(MessageId.OPF_002, OCFMetaFile.Container.AsLocation(container), container.Relativize(packageDocument));
                return false;
            }

            ValidationContext documentContext = state.Context() with { Url = packageDocument, MimeType = string.Empty };
            PeekPackageDocument(documentContext, state);
            if (state.Error.Length > 0)
            {
                report.Message(MessageId.OPF_001, documentContext.Location(), state.Error);
                if (isPrimary)
                {
                    return false;
                }
            }
            else if (state.PublicationVersionOf(packageDocument) == EpubVersion.Unknown)
            {
                report.Message(MessageId.OPF_001, documentContext.Location(), PackageDocumentPeeker.VersionNotFound);
            }

            isPrimary = false;
        }

        return true;
    }

    // Upstream's PackageDocumentPeeker, run through its XML parser with the reporting of errors off: a document that is
    // not well-formed is reported as RSC-016 unless a fatal error was reported already.
    private static void PeekPackageDocument(ValidationContext context, OCFCheckerState state)
    {
        state.ErrorReset();
        PackageDocumentInfo info;
        try
        {
            using Stream stream = context.Resources.OpenStream(context.Url);
            info = PackageDocumentPeeker.Peek(stream);
        }
        catch (IOException)
        {
            context.Report.Message(MessageId.PKG_008, context.Location(), context.Path);
            return;
        }

        state.AddError(info.Error);
        if (info.Version is { } version)
        {
            state.AddVersion(context.Url, version);
        }

        if (info.UniqueIdentifier is { } identifier)
        {
            state.AddUniqueId(context.Url, identifier);
        }

        if (info.Types.Count > 0)
        {
            state.AddTypes(context.Url, info.Types);
        }

        if (info.WellFormednessError is { } error && context.Report.FatalErrorCount == 0)
        {
            context.Report.Message(MessageId.RSC_016, context.Location().At(error.LineNumber, error.LinePosition), error.Message);
        }
    }

    private static EpubVersion CheckPublicationVersion(OCFCheckerState state, EpubVersion requested, IReport report)
    {
        // Upstream fails an assertion when no package document has a version; the version asked for is used then.
        if (state.PublicationVersion is not { } detected)
        {
            return requested;
        }

        report.Info(null, FeatureKind.FormatVersion, detected.ToText());
        if (requested != detected && requested != EpubVersion.Unknown)
        {
            report.Message(MessageId.PKG_001, FirstPackageDocumentLocation(state), requested.ToText(), detected.ToText());
            return requested;
        }

        return detected;
    }

    private static EpubProfile CheckPublicationProfile(OCFCheckerState state, EpubProfile profile, EpubVersion version, IReport report)
    {
        if (version == EpubVersion.Version2)
        {
            if (profile != EpubProfile.Default)
            {
                // A validation profile is not supported for EPUB 2.
                report.Message(MessageId.PKG_023, FirstPackageDocumentLocation(state));
            }

            return EpubProfile.Default;
        }

        EpubProfile checkedProfile = profile.MakeTypeCompatible(state.PublicationTypes);
        if (checkedProfile != profile)
        {
            report.Message(
                MessageId.OPF_064,
                FirstPackageDocumentLocation(state),
                checkedProfile.MatchingType().ToString().ToLowerInvariant(),
                checkedProfile.ToString().ToUpperInvariant());
        }

        return checkedProfile;
    }

    private static void CheckRenditions(OCFCheckerState state, EpubVersion version, IReport report, EpubLocation location)
    {
        int count = state.PackageDocuments.Count;
        if (version == EpubVersion.Version2 && count > 1)
        {
            report.Message(MessageId.PKG_013, OCFMetaFile.Container.AsLocation(state.Container));
        }

        // EPUB 3 Multiple Renditions recommends a metadata file; the mapping document is checked with the profiles (task 3.11).
        if (version == EpubVersion.Version3 && count > 1)
        {
            state.AddProperty(EpubCheckVocab.Property.MultipleRendition);
            if (!OCFMetaFile.Metadata.IsPresent(state.Container))
            {
                report.Message(MessageId.RSC_019, location);
            }
        }
    }

    private static void CheckEncryptionFile(OCFCheckerState state, IReport report)
    {
        OCFContainer container = state.Container;
        if (OCFMetaFile.Encryption.IsPresent(container))
        {
            report.Info(null, FeatureKind.HasEncryption, OCFMetaFile.Encryption.AsPath());
            OCFEncryptionFileChecker.Check(state.Context() with { Url = OCFMetaFile.Encryption.AsUrl(container), MimeType = MediaTypes.Xml }, state);
        }
    }

    private static void CheckOtherMetaFiles(OCFCheckerState state, IReport report)
    {
        // The container file is checked again for the schema errors: the first reading only looked for the rootfiles.
        foreach (OCFMetaFile file in new[] { OCFMetaFile.Container, OCFMetaFile.Metadata, OCFMetaFile.Signatures })
        {
            OCFContainer container = state.Container;
            if (file.IsPresent(container))
            {
                OCFMetaFileChecker.Check(state.Context() with { Url = file.AsUrl(container), MimeType = MediaTypes.Xml });
                if (file == OCFMetaFile.Signatures)
                {
                    report.Info(null, FeatureKind.HasSignatures, file.AsPath());
                }
            }
        }
    }

    private static EpubLocation FirstPackageDocumentLocation(OCFCheckerState state) =>
        OCFContainer.LocationOf(state.PackageDocuments[0], state.Container);

    // Upstream's OCFExtensionChecker.
    private static void CheckFileExtension(string path, EpubVersion version, IReport report, EpubLocation location)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length < 2)
        {
            return;
        }

        extension = extension[1..];
        if (extension == "epub")
        {
            return;
        }

        if (string.Equals(extension, "epub", StringComparison.OrdinalIgnoreCase))
        {
            report.Message(MessageId.PKG_016, location);
        }
        else
        {
            report.Message(version == EpubVersion.Version3 ? MessageId.PKG_024 : MessageId.PKG_017, location.WithContext(extension));
        }
    }
}
