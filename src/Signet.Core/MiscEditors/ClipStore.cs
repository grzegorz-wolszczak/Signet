using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Signet.Core.Misc;

namespace Signet.Core.MiscEditors;

/// <summary>
/// Persistent clip library store in a JSON file next to the settings. Reading is lenient
/// (missing/empty/invalid file = empty list), saving is atomic (temporary file +
/// <see cref="File.Move(string, string, bool)"/>).
/// </summary>
public sealed class ClipStore
{
    /// <summary>Default file name in the preferences folder.</summary>
    public const string DefaultFileName = "clips.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
    public IReadOnlyList<ClipEntry> Load()
    {
        try
        {
            return File.Exists(_path)
                ? ClipIo.ReadJson(File.ReadAllText(_path))
                : Array.Empty<ClipEntry>();
        }
        catch (IOException)
        {
            return Array.Empty<ClipEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<ClipEntry>();
        }
    }

    /// <summary>Saves entries to the file (atomically).</summary>
    public void Save(IReadOnlyList<ClipEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = ClipIo.WriteJson(entries);
        string tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json, Utf8NoBom);
        File.Move(tempPath, _path, overwrite: true);
    }
}
