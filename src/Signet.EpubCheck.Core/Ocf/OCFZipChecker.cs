using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Checks the first local file header of a packaged publication (upstream's <c>OCFZipChecker</c>): the file must
/// start with a ZIP header whose entry is <c>mimetype</c>, with no extra field.
/// </summary>
public static class OCFZipChecker
{
    // The local file header, the name "mimetype" and its content "application/epub+zip" (upstream reads 58 bytes).
    private static readonly int s_headerLength = ZipFormat.LocalHeader.Length + OCFChecker.MimeTypePath.Length + OCFChecker.EpubMimeType.Length;

    /// <summary>Checks the header of the ZIP file at <paramref name="path"/>; the messages point at <paramref name="location"/>.</summary>
    public static void Check(string path, IReport report, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(location);

        byte[] header = new byte[s_headerLength];
        int read;
        try
        {
            using FileStream stream = File.OpenRead(path);
            read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Message(MessageId.PKG_008, location, ex.Message);
            return;
        }

        if (read != header.Length)
        {
            report.Message(MessageId.PKG_003, location);
            return;
        }

        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(ZipFormat.LocalHeader.NameLengthOffset));
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(ZipFormat.LocalHeader.ExtraLengthOffset));
        if (header[0] != ZipFormat.LocalHeaderMagic[0] || header[1] != ZipFormat.LocalHeaderMagic[1])
        {
            report.Message(MessageId.PKG_004, location);
        }
        else if (nameLength != OCFChecker.MimeTypePath.Length)
        {
            report.Message(MessageId.PKG_006, location);
        }
        else if (extraLength != 0)
        {
            report.Message(MessageId.PKG_005, location, extraLength);
        }
        else if (Encoding.ASCII.GetString(header, ZipFormat.LocalHeader.NameOffset, nameLength) != OCFChecker.MimeTypePath)
        {
            report.Message(MessageId.PKG_006, location);
        }
    }
}
