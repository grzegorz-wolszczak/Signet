using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Checks the OCF container of a publication (upstream's <c>OCFChecker</c>), in upstream's order: the ZIP header, the
/// container structure (duplicate names, file names, empty directories), the <c>mimetype</c> file and the presence of
/// <c>META-INF/container.xml</c>. The parsing of <c>container.xml</c> and what follows it come with later tasks.
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
    public static void Check(string path, EpubVersion version, IReport report)
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
            Check(source, version, report, location);
        }
    }

    /// <summary>
    /// Checks an open source, reporting the problems of the publication as a whole at <paramref name="location"/>. The
    /// ZIP header is not checked here (it needs the file: <see cref="Check(string, EpubVersion, IReport)"/>).
    /// </summary>
    public static void Check(IEpubSource source, EpubVersion version, IReport report, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(location);

        CheckContainerStructure(source, version, report, location);
        CheckMimeTypeFile(source, report, location);
        CheckContainerFile(source, report, location);
    }

    private static void CheckContainerStructure(IEpubSource source, EpubVersion version, IReport report, EpubLocation location)
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

        string trimmed = JavaTrim(content);
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

    private static void CheckContainerFile(IEpubSource source, IReport report, EpubLocation location)
    {
        if (!source.Entries.Any(entry => !entry.IsDirectory && entry.Name == ContainerPath) && report.FatalErrorCount == 0)
        {
            // Not reported when a fatal error was already reported.
            report.Message(MessageId.RSC_002, location);
        }
    }

    // Java's String.trim(): removes the characters up to U+0020 at both ends.
    private static string JavaTrim(string value)
    {
        int start = 0;
        int end = value.Length;
        while (start < end && value[start] <= ' ')
        {
            start++;
        }

        while (end > start && value[end - 1] <= ' ')
        {
            end--;
        }

        return value[start..end];
    }
}
