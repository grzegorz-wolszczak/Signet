using System;

namespace Signet.Core.Resources;

/// <summary>
/// Arguments of the <see cref="Resource.Renamed"/> and <see cref="Resource.Moved"/> events —
/// carry the previous full path of the resource on disk.
/// </summary>
public sealed class ResourcePathChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments with the given previous path.</summary>
    public ResourcePathChangedEventArgs(string oldFullPath)
    {
        ArgumentNullException.ThrowIfNull(oldFullPath);
        OldFullPath = oldFullPath;
    }

    /// <summary>Full path of the file before the rename / move.</summary>
    public string OldFullPath { get; }
}

/// <summary>
/// Arguments of resource collection events (<c>FolderKeeper</c>): carry the resource
/// the event refers to.
/// </summary>
public sealed class ResourceEventArgs : EventArgs
{
    /// <summary>Creates the arguments for the given resource.</summary>
    public ResourceEventArgs(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Resource = resource;
    }

    /// <summary>The resource the event refers to.</summary>
    public Resource Resource { get; }
}

/// <summary>
/// Arguments of <see cref="BookManipulation.FolderKeeper.ResourceBookPathChanged"/> — the renamed or moved resource
/// and its previous bookpath.
/// </summary>
public sealed class ResourceBookPathChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments for the given resource and its previous bookpath.</summary>
    public ResourceBookPathChangedEventArgs(Resource resource, string oldBookPath)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(oldBookPath);
        Resource = resource;
        OldBookPath = oldBookPath;
    }

    /// <summary>The resource (already with its new bookpath).</summary>
    public Resource Resource { get; }

    /// <summary>The bookpath before the rename / move.</summary>
    public string OldBookPath { get; }
}
