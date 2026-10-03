using Signet.Core.Fonts;

namespace Signet.Core.Resources;

/// <summary>A font resource (TTF / OTF / WOFF).</summary>
/// <remarks>
/// <see cref="ObfuscationAlgorithm"/> is set from <c>META-INF/encryption.xml</c>
/// (see <see cref="OcfReader.IdpfFontAlgorithmId"/> / <see cref="OcfReader.AdobeFontAlgorithmId"/>);
/// the (de)obfuscation itself is done elsewhere.
/// </remarks>
public sealed class FontResource : Resource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public FontResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Font;

    /// <summary>
    /// The font obfuscation algorithm identifier (empty = none). Usually
    /// <see cref="OcfReader.IdpfFontAlgorithmId"/> or <see cref="OcfReader.AdobeFontAlgorithmId"/>.
    /// </summary>
    public string ObfuscationAlgorithm { get; set; } = string.Empty;

    /// <summary>
    /// The names read from the font file's <c>name</c> table (family, subfamily, full name, version).
    /// For WOFF/WOFF2 or an unrecognized format — <see cref="FontFileInfo.Empty"/>.
    /// </summary>
    public FontFileInfo GetFontInfo() => OpenTypeFontInfo.Read(FullPath);

    /// <inheritdoc/>
    protected override bool LoadFromDisk()
    {
        RaiseModified();
        return true;
    }
}
