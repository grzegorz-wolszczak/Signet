namespace Signet.Core.Resources;

/// <summary>SVG resource (a vector image handled as text).</summary>
public sealed class SvgResource : TextResource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public SvgResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Svg;
}
