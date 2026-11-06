using System.IO;
using System.Text;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Parse.Compact;

/// <summary>A schema document in the compact syntax (Jing's <c>CompactParseable</c>).</summary>
internal sealed class CompactParseable(InputSource source, ISchemaResolver resolver, ISaxErrorHandler? eh, JingLocalizer compactLocalizer)
    : ISubParseable
{
    private const byte Utf16LittleEndianFirst = 0xFF;
    private const byte Utf16LittleEndianSecond = 0xFE;

    public string? Uri => source.SystemId is null ? null : JingUri.EscapeDisallowedChars(source.SystemId);

    public ISubParseable CreateSubParseable(string href, string? baseUri) =>
        new CompactParseable(resolver.Resolve(href, baseUri), resolver, eh, compactLocalizer);

    public Pattern Parse(SchemaBuilderImpl schemaBuilder, IScope scope) =>
        new CompactSyntax(ReadText(), source.SystemId, schemaBuilder, eh, compactLocalizer).Parse(scope);

    public Pattern? ParseAsInclude(SchemaBuilderImpl schemaBuilder, IIncludedGrammar grammar) =>
        new CompactSyntax(ReadText(), source.SystemId, schemaBuilder, eh, compactLocalizer).ParseInclude(grammar);

    // Jing reads UTF-8 unless the first two bytes are a UTF-16 byte order mark (then Java's "UTF-16" decoder takes it
    // away); the character stream then skips one U+FEFF at the start. Malformed bytes become U+FFFD, as in Java.
    private string ReadText()
    {
        using Stream stream = source.Open();
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        byte[] data = bytes.ToArray();
        if (data.Length >= 2 && ((data[0] == Utf16LittleEndianFirst && data[1] == Utf16LittleEndianSecond)
            || (data[0] == Utf16LittleEndianSecond && data[1] == Utf16LittleEndianFirst)))
        {
            Encoding utf16 = data[0] == Utf16LittleEndianFirst ? Encoding.Unicode : Encoding.BigEndianUnicode;
            return utf16.GetString(data, 2, data.Length - 2);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(data);
    }
}
