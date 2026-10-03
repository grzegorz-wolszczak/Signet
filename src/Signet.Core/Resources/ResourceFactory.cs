using System;
using SysPath = System.IO.Path;

namespace Signet.Core.Resources;

/// <summary>
/// Creates the appropriate <see cref="Resource"/> subtype for a file based on its MIME type
/// (or, when unknown, its extension).
/// </summary>
public static class ResourceFactory
{
    /// <summary>
    /// Creates a resource for <paramref name="fullFilePath"/> in the <paramref name="mainFolder"/> tree.
    /// </summary>
    /// <param name="mainFolder">The book's working folder (bookpath root).</param>
    /// <param name="fullFilePath">Full path of the file on disk.</param>
    /// <param name="mediaType">
    /// MIME type from the OPF manifest. When <c>null</c>/empty it is derived from the file extension.
    /// </param>
    public static Resource Create(string mainFolder, string fullFilePath, string? mediaType = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mainFolder);
        ArgumentException.ThrowIfNullOrEmpty(fullFilePath);

        string resolvedMediaType = mediaType ?? string.Empty;
        if (string.IsNullOrEmpty(resolvedMediaType))
        {
            string extension = SysPath.GetExtension(fullFilePath);
            resolvedMediaType = MediaTypes.GetMediaTypeFromExtension(extension);
        }

        Resource resource = Build(mainFolder, fullFilePath, resolvedMediaType);
        resource.MediaType = resolvedMediaType;
        return resource;
    }

    private static Resource Build(string mainFolder, string fullFilePath, string mediaType) =>
        MediaTypes.GetResourceType(mediaType) switch
        {
            ResourceType.Opf => new OpfResource(mainFolder, fullFilePath),
            ResourceType.Ncx => new NcxResource(mainFolder, fullFilePath),
            ResourceType.Html => new HtmlResource(mainFolder, fullFilePath),
            ResourceType.Css => new CssResource(mainFolder, fullFilePath),
            ResourceType.Svg => new SvgResource(mainFolder, fullFilePath),
            ResourceType.Image => new ImageResource(mainFolder, fullFilePath),
            ResourceType.Font => new FontResource(mainFolder, fullFilePath),
            ResourceType.Audio => new AudioResource(mainFolder, fullFilePath),
            ResourceType.Video => new VideoResource(mainFolder, fullFilePath),
            ResourceType.Pdf => new PdfResource(mainFolder, fullFilePath),
            ResourceType.Xml => new XmlResource(mainFolder, fullFilePath),
            ResourceType.MiscText => new MiscTextResource(mainFolder, fullFilePath),
            ResourceType.Text => new TextResource(mainFolder, fullFilePath),
            _ => new Resource(mainFolder, fullFilePath),
        };
}
