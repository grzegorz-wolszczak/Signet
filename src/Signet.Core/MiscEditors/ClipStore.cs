using System;
using System.Collections.Generic;
using Signet.Core.Misc;

namespace Signet.Core.MiscEditors;

/// <summary>
/// Persistent clip library store in an XML file next to the settings (<see cref="ClipIo"/>). Reading is lenient
/// (missing/empty/invalid file = empty list), saving is atomic (<see cref="XmlConfig.Save"/>).
/// </summary>
public sealed class ClipStore
{
    /// <summary>Default file name in the preferences folder.</summary>
    public const string DefaultFileName = "clips.xml";

    private readonly string _path;

    /// <summary>Creates a store pointing to the file in the preferences folder.</summary>
    public ClipStore()
        : this(System.IO.Path.Combine(AppDirectories.PrefsDirectory, DefaultFileName))
    {
    }

    /// <summary>Creates a store pointing to a specific file (used in tests).</summary>
    public ClipStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Path of the store file.</summary>
    public string Path => _path;

    /// <summary>Loads entries from the file (an empty list when the file is missing or invalid).</summary>
    public IReadOnlyList<ClipEntry> Load() => ClipIo.FromDocument(XmlConfig.TryLoad(_path));

    /// <summary>Saves entries to the file (atomically).</summary>
    public void Save(IReadOnlyList<ClipEntry> entries) => XmlConfig.Save(_path, ClipIo.ToDocument(entries));
}
