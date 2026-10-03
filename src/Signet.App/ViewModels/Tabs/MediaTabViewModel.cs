using System.Globalization;
using System.IO;
using System;
using Signet.App.Resources;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Audio / video / PDF resource tab: an info line (name, size, MIME type) and an "Open externally" button.
/// There is no real playback / rendering (no suitable library for Avalonia 12).
/// </summary>
public sealed class MediaTabViewModel : ContentTabViewModel
{
    private readonly Resource _resource;

    /// <summary>Creates a tab for an audio / video / PDF resource.</summary>
    public MediaTabViewModel(OpenTab tab)
        : base(tab)
    {
        _resource = tab.Resource;
        Media = _resource.Type switch
        {
            ResourceType.Audio => MediaKind.Audio,
            ResourceType.Video => MediaKind.Video,
            ResourceType.Pdf => MediaKind.Pdf,
            _ => throw new ArgumentException(
                $"MediaTab does not support a resource of type {_resource.Type}.", nameof(tab)),
        };
    }

    /// <summary>Request: open the file in the default system application.</summary>
    public event EventHandler? OpenExternallyRequested;

    /// <summary>Content kind (audio / video / PDF) — drives the icon and description in the view.</summary>
    public MediaKind Media { get; }

    /// <summary>Full path of the resource file (for "Open externally").</summary>
    public string FullPath => _resource.FullPath;

    /// <summary>Description of the content kind.</summary>
    public string KindLabel => Media switch
    {
        MediaKind.Audio => Strings.Get("MediaTab_AudioFile"),
        MediaKind.Video => Strings.Get("MediaTab_VideoFile"),
        _ => Strings.Get("MediaTab_PdfDocument"),
    };

    /// <summary>Info line: file name, size, MIME type.</summary>
    public string InfoLine
    {
        get
        {
            long bytes = File.Exists(_resource.FullPath) ? new FileInfo(_resource.FullPath).Length : 0;
            string size = string.Create(CultureInfo.InvariantCulture, $"{(bytes + 512) / 1024} KB");
            return $"{_resource.Filename} | {size} | {_resource.MediaType}";
        }
    }

    /// <summary>A short notice that the player/preview is not implemented.</summary>
    public string Notice => Media == MediaKind.Pdf
        ? Strings.Get("MediaTab_PdfNotAvailable")
        : Strings.Get("MediaTab_PlayerNotAvailable");

    /// <summary>Raises a request to open the file in the system application.</summary>
    public void RequestOpenExternally() => OpenExternallyRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>Content kind of a <see cref="MediaTabViewModel"/> tab.</summary>
public enum MediaKind
{
    /// <summary>Audio.</summary>
    Audio,

    /// <summary>Video.</summary>
    Video,

    /// <summary>PDF.</summary>
    Pdf,
}
