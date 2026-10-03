namespace Signet.Core.Resources;

/// <summary>An audio resource.</summary>
public sealed class AudioResource : Resource
{
    /// <inheritdoc cref="Resource(string, string)"/>
    public AudioResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Audio;
}
