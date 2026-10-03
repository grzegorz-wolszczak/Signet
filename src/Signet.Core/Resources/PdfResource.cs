namespace Signet.Core.Resources;

/// <summary>A PDF resource.</summary>
public sealed class PdfResource : Resource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public PdfResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Pdf;
}
