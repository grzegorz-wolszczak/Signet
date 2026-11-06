namespace Signet.EpubCheck.Core.Ocf;

/// <summary>The constants of the ZIP file format (PKWARE APPNOTE.TXT) that the OCF checks read by hand.</summary>
internal static class ZipFormat
{
    /// <summary>The value of a 16-bit field that means "see the ZIP64 record".</summary>
    public const ushort Zip64Marker16 = ushort.MaxValue;

    /// <summary>The value of a 32-bit field that means "see the ZIP64 record".</summary>
    public const uint Zip64Marker32 = uint.MaxValue;

    /// <summary>The longest comment at the end of the archive.</summary>
    public const int MaxCommentLength = ushort.MaxValue;

    /// <summary>The general purpose flag saying that the name is UTF-8 (bit 11).</summary>
    public const ushort Utf8NameFlag = 0x0800;

    /// <summary>The first two bytes of a ZIP file ("PK", the initials of Phil Katz).</summary>
    public static readonly byte[] LocalHeaderMagic = "PK"u8.ToArray();

    /// <summary>Compression method "stored".</summary>
    public const ushort MethodStored = 0;

    /// <summary>Compression method "deflated".</summary>
    public const ushort MethodDeflated = 8;

    /// <summary>The local file header.</summary>
    public static class LocalHeader
    {
        public const uint Signature = 0x04034B50;
        public const int Length = 30;
        public const int VersionNeededOffset = 4;
        public const int FlagsOffset = 6;
        public const int MethodOffset = 8;
        public const int TimeOffset = 10;
        public const int DateOffset = 12;
        public const int CrcOffset = 14;
        public const int CompressedSizeOffset = 18;
        public const int SizeOffset = 22;
        public const int NameLengthOffset = 26;
        public const int ExtraLengthOffset = 28;
        public const int NameOffset = Length;
    }

    /// <summary>A header of the central directory.</summary>
    public static class CentralHeader
    {
        public const uint Signature = 0x02014B50;
        public const int Length = 46;
        public const int FlagsOffset = 8;
        public const int MethodOffset = 10;
        public const int CompressedSizeOffset = 20;
        public const int SizeOffset = 24;
        public const int NameLengthOffset = 28;
        public const int ExtraLengthOffset = 30;
        public const int CommentLengthOffset = 32;
        public const int LocalHeaderOffsetOffset = 42;
    }

    /// <summary>The end of central directory record.</summary>
    public static class EndOfCentralDirectory
    {
        public const uint Signature = 0x06054B50;
        public const int Length = 22;
        public const int EntryCountOffset = 10;
        public const int DirectorySizeOffset = 12;
        public const int DirectoryOffsetOffset = 16;
    }

    /// <summary>The ZIP64 end of central directory locator, just before the end of central directory record.</summary>
    public static class Zip64Locator
    {
        public const uint Signature = 0x07064B50;
        public const int Length = 20;
        public const int RecordOffsetOffset = 8;
    }

    /// <summary>The ZIP64 end of central directory record.</summary>
    public static class Zip64EndOfCentralDirectory
    {
        public const uint Signature = 0x06064B50;
        public const int Length = 56;
        public const int EntryCountOffset = 32;
        public const int DirectorySizeOffset = 40;
        public const int DirectoryOffsetOffset = 48;
    }

    /// <summary>An extra field: a 2-byte id and a 2-byte size, then the data.</summary>
    public static class ExtraField
    {
        public const int HeaderLength = 4;
        public const int SizeOffset = 2;
        public const ushort Zip64Id = 0x0001;
        public const int Zip64SizeLength = sizeof(ulong);
    }
}
