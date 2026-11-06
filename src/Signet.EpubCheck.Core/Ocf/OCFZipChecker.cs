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
    // The local file header (30 bytes), the name "mimetype" (8) and its content "application/epub+zip" (20).
    private const int HeaderLength = 58;
    private const int NameLengthOffset = 26;
    private const int ExtraLengthOffset = 28;
    private const int NameOffset = 30;

    /// <summary>Checks the header of the ZIP file at <paramref name="path"/>; the messages point at <paramref name="location"/>.</summary>
    public static void Check(string path, IReport report, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(location);

        byte[] header = new byte[HeaderLength];
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

        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(NameLengthOffset));
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(ExtraLengthOffset));
        if (header[0] != (byte)'P' || header[1] != (byte)'K')
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
        else if (Encoding.ASCII.GetString(header, NameOffset, nameLength) != OCFChecker.MimeTypePath)
        {
            report.Message(MessageId.PKG_006, location);
        }
    }
}
