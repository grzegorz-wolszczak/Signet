using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Signet.Core.Misc;

namespace Signet.Core.MiscEditors;

/// <summary>
/// A persistent store of the saved searches library in a JSON file next to the settings.
/// Reading is tolerant (a missing/empty/invalid file = an empty list), writing is atomic (a temporary file + <see cref="File.Move(string, string, bool)"/>).
/// </summary>
public sealed class SavedSearchStore
{
    /// <summary>The default file name in the preferences folder.</summary>
    public const string DefaultFileName = "saved_searches.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
    public IReadOnlyList<SearchEntry> Load()
    {
        try
        {
            return File.Exists(_path)
                ? SavedSearchIo.ReadJson(File.ReadAllText(_path))
                : Array.Empty<SearchEntry>();
        }
        catch (IOException)
        {
            return Array.Empty<SearchEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<SearchEntry>();
        }
    }

    /// <summary>Writes entries to the file (atomically).</summary>
    public void Save(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = SavedSearchIo.WriteJson(entries);
        string tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json, Utf8NoBom);
        File.Move(tempPath, _path, overwrite: true);
    }
}
