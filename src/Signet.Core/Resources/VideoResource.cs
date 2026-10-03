namespace Signet.Core.Resources;

/// <summary>Video resource.</summary>
public sealed class VideoResource : Resource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public VideoResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Video;
}
