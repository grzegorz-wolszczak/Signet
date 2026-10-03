namespace Signet.Core.Resources;

/// <summary>An editable text resource not covered by the other types (JS, JSON, plain text).</summary>
public sealed class MiscTextResource : TextResource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public MiscTextResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.MiscText;
}
