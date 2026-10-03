namespace Signet.Core.Resources;

/// <summary>A CSS resource (a stylesheet).</summary>
public sealed class CssResource : TextResource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public CssResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Css;
}
