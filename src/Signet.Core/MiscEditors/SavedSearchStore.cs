using System;
using System.Collections.Generic;
using Signet.Core.Misc;

namespace Signet.Core.MiscEditors;

/// <summary>
/// A persistent store of the saved searches library in an XML file next to the settings (<see cref="SavedSearchIo"/>).
/// Reading is tolerant (a missing/empty/invalid file = an empty list), writing is atomic (<see cref="XmlConfig.Save"/>).
/// </summary>
public sealed class SavedSearchStore
{
    /// <summary>The default file name in the preferences folder.</summary>
    public const string DefaultFileName = "saved_searches.xml";

    private readonly string _path;

    /// <summary>Creates a store pointing at a file in the preferences folder.</summary>
    public SavedSearchStore()
        : this(System.IO.Path.Combine(AppDirectories.PrefsDirectory, DefaultFileName))
    {
    }

    /// <summary>Creates a store pointing at a specific file (used in tests).</summary>
    public SavedSearchStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>The store file's path.</summary>
    public string Path => _path;

    /// <summary>Loads entries from the file (an empty list when the file does not exist or is invalid).</summary>
    public IReadOnlyList<SearchEntry> Load() => SavedSearchIo.FromDocument(XmlConfig.TryLoad(_path));

    /// <summary>Writes entries to the file (atomically).</summary>
    public void Save(IReadOnlyList<SearchEntry> entries) => XmlConfig.Save(_path, SavedSearchIo.ToDocument(entries));
}
