using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>Checks the read-only OCF layer of an EPUB source.</summary>
public static class OCFChecker
{
    private const string MimeTypeEntryName = "mimetype";
    private const string MimeTypeValue = "application/epub+zip";

    /// <summary>Checks an EPUB archive. Broken archives are reported, never exposed as archive exceptions.</summary>
    public static void CheckZip(string path, EpubVersion version, IReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);

        if (!CheckZipHeader(path, report))
        {
            return;
        }

        try
        {
            using ZipEpubSource source = new(path);
            Check(source, version, report);
        }
        catch (EpubZipEncodingException)
        {
            report.Message(MessageId.PKG_027, new EpubLocation(path));
        }
        catch (InvalidDataException ex)
        {
            report.Message(MessageId.PKG_008, new EpubLocation(path), ex.Message.ToLowerInvariant());
        }
        catch (IOException ex)
        {
            report.Message(MessageId.PKG_008, new EpubLocation(path), ex.Message);
        }
    }

    /// <summary>Checks an already-open EPUB source.</summary>
    public static void Check(IEpubSource source, EpubVersion version, IReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);

        CheckEntries(source, version, report);
        if (source is ZipEpubSource zipSource)
        {
            CheckZipContainer(zipSource, report);
        }

        CheckMimeType(source, report);
        CheckContainer(source, report);
    }

    private static bool CheckZipHeader(string path, IReport report)
    {
        byte[] header = new byte[58];
        int read;
        using (FileStream stream = File.OpenRead(path))
        {
            read = stream.Read(header);
        }

        EpubLocation location = new(path);
        if (read < header.Length)
        {
            report.Message(MessageId.PKG_003, location);
            report.Message(MessageId.PKG_008, location, "zip file is empty");
            return false;
        }

        if (header[0] != (byte)'P' || header[1] != (byte)'K')
        {
            report.Message(MessageId.PKG_004, location);
            report.Message(MessageId.PKG_008, location, "corrupted ZIP header");
            return false;
        }

        return true;
    }

    private static void CheckEntries(IEpubSource source, EpubVersion version, IReport report)
    {
        HashSet<string> normalizedNames = new(StringComparer.Ordinal);
        foreach (EpubSourceEntry entry in source.Entries)
        {
            OCFFilenameChecker.Check(entry.Name, version, report);
            if (!normalizedNames.Add(OCFFilenameChecker.NormalizeForComparison(entry.Name)))
            {
                report.Message(MessageId.OPF_060, new EpubLocation(entry.Name), entry.Name);
            }

            if (entry.IsDirectory && !source.Entries.Any(other => !ReferenceEquals(other, entry) && other.Name.StartsWith(entry.Name, StringComparison.Ordinal)))
            {
                report.Message(MessageId.PKG_014, new EpubLocation(entry.Name), entry.Name);
            }

            if (entry.Name.StartsWith("META-INF/", StringComparison.Ordinal) && IsPublicationResourceInMetaInf(entry.Name))
            {
                report.Message(MessageId.PKG_025, new EpubLocation(entry.Name));
            }
        }
    }

    private static void CheckZipContainer(ZipEpubSource source, IReport report)
    {
        if (source.FirstEntryName != MimeTypeEntryName && source.Entries.Any(entry => entry.Name == MimeTypeEntryName))
        {
            report.Message(MessageId.PKG_006, new EpubLocation(source.Path));
        }
        else if (source.FirstEntryExtraLength != 0)
        {
            report.Message(MessageId.PKG_005, new EpubLocation(source.Path), source.FirstEntryExtraLength);
        }
    }

    private static void CheckMimeType(IEpubSource source, IReport report)
    {
        EpubSourceEntry? entry = source.Entries.FirstOrDefault(candidate => candidate.Name == MimeTypeEntryName);
        if (entry is null)
        {
            report.Message(MessageId.PKG_006, new EpubLocation(MimeTypeEntryName));
            return;
        }

        using Stream stream = source.OpenRead(entry);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        string value = reader.ReadToEnd();
        if (value != MimeTypeValue)
        {
            string suffix = value.Trim() == MimeTypeValue ? " with no whitespace or newline." : string.Empty;
            report.Message(MessageId.PKG_007, new EpubLocation(MimeTypeEntryName), suffix);
        }
    }

    private static void CheckContainer(IEpubSource source, IReport report)
    {
        if (!source.Entries.Any(entry => entry.Name == "META-INF/container.xml"))
        {
            report.Message(MessageId.RSC_002, new EpubLocation("META-INF/container.xml"), "container.xml");
        }
    }

    private static bool IsPublicationResourceInMetaInf(string name)
    {
        if (name == "META-INF/" || name is "META-INF/container.xml" or "META-INF/encryption.xml" or "META-INF/metadata.xml" or "META-INF/manifest.xml" or "META-INF/rights.xml" or "META-INF/signatures.xml")
        {
            return false;
        }

        string extension = Path.GetExtension(name);
        return extension is ".css" or ".gif" or ".htm" or ".html" or ".jpeg" or ".jpg" or ".ncx" or ".opf" or ".png" or ".svg" or ".xhtml";
    }
}
