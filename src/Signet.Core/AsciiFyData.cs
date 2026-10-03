using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core;

/// <summary>
/// Loads the <see cref="AsciiFy"/> transliteration tables from embedded resources
/// (<c>EmbeddedData/asciify-unidecode-*.bin</c> — the position and text tables).
/// </summary>
internal static class AsciiFyData
{
    /// <summary>Number of entries in <see cref="Pos"/> (0x10000 minus the 0x800 surrogate code points).</summary>
    public const int PosLength = 63488;

    /// <summary>
    /// One packed entry per BMP code point (excluding surrogates):
    /// <c>(offset &lt;&lt; 5) | length</c> points at a substring of <see cref="Text"/>.
    /// </summary>
    public static uint[] Pos { get; } = LoadPos();

    /// <summary>Shared replacement text buffer (each byte = one Latin-1 character).</summary>
    public static string Text { get; } = Encoding.Latin1.GetString(LoadResource("AsciiFy.unidecode-text"));

    private static uint[] LoadPos()
    {
        byte[] raw = LoadResource("AsciiFy.unidecode-pos");
        if (raw.Length != PosLength * sizeof(uint))
        {
            throw new InvalidDataException(
                $"Corrupted AsciiFy data: expected {PosLength * sizeof(uint)} B, got {raw.Length} B.");
        }

        uint[] result = new uint[PosLength];
        for (int i = 0; i < PosLength; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * sizeof(uint)));
        }

        return result;
    }

    private static byte[] LoadResource(string logicalName)
    {
        using Stream? stream = typeof(AsciiFyData).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(CoreStrings.Format("Error_EmbeddedResourceMissing", logicalName));
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
