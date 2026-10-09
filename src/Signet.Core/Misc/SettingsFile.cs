using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Signet.Core.Misc;

/// <summary>
/// Low-level layer over the JSON settings file. The file is a two-level object
/// <c>{ "group": { "key": value } }</c> — settings are organized in groups
/// (e.g. the <c>user_preferences</c> group).
/// </summary>
/// <remarks>
/// Reading is lenient: a missing file, an empty file or invalid JSON yield an
/// empty store (no exception). Saving is atomic (temporary file + <c>File.Move</c>).
/// The class is not thread-safe.
/// </remarks>
internal sealed class SettingsFile
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly bool _detached;
    private JsonObject _root;

    public SettingsFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _root = Load(path);
    }

    private SettingsFile(string path, JsonObject root)
    {
        _path = path;
        _root = root;
        _detached = true;
    }

    /// <summary>Path of the settings file.</summary>
    public string Path => _path;

    /// <summary>
    /// An in-memory copy of the current state. It reads like this file but never writes it: <see cref="Save"/> does
    /// nothing.
    /// </summary>
    public SettingsFile CloneDetached() => new(_path, (JsonObject)_root.DeepClone());

    /// <summary>All <c>(group, key)</c> pairs of the two-level object.</summary>
    public IEnumerable<(string Group, string Key)> Entries()
    {
        foreach (KeyValuePair<string, JsonNode?> group in _root)
        {
            if (group.Value is JsonObject groupObj)
            {
                foreach (KeyValuePair<string, JsonNode?> entry in groupObj)
                {
                    yield return (group.Key, entry.Key);
                }
            }
        }
    }

    /// <summary>Returns the raw node for <paramref name="group"/>/<paramref name="key"/> or <see langword="null"/>.</summary>
    public JsonNode? GetRaw(string group, string key)
    {
        return _root.TryGetPropertyValue(group, out JsonNode? groupNode)
            && groupNode is JsonObject groupObj
            && groupObj.TryGetPropertyValue(key, out JsonNode? value)
                ? value
                : null;
    }

    /// <summary>Whether an entry exists for <paramref name="group"/>/<paramref name="key"/>.</summary>
    public bool Contains(string group, string key)
    {
        return _root.TryGetPropertyValue(group, out JsonNode? groupNode)
            && groupNode is JsonObject groupObj
            && groupObj.ContainsKey(key);
    }

    /// <summary>Sets a value; <paramref name="value"/> = <see langword="null"/> removes the entry.</summary>
    public void SetRaw(string group, string key, JsonNode? value)
    {
        if (value is null)
        {
            Remove(group, key);
            return;
        }

        if (_root[group] is not JsonObject groupObj)
        {
            groupObj = new JsonObject();
            _root[group] = groupObj;
        }

        groupObj[key] = value;
    }

    /// <summary>Removes an entry (and the group if it becomes empty). Returns whether anything was removed.</summary>
    public bool Remove(string group, string key)
    {
        if (_root[group] is not JsonObject groupObj || !groupObj.Remove(key))
        {
            return false;
        }

        if (groupObj.Count == 0)
        {
            _root.Remove(group);
        }

        return true;
    }

    /// <summary>
    /// Returns the whole group as a key→value map, skipping entries that are not strings.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetStringGroup(string group)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (_root.TryGetPropertyValue(group, out JsonNode? node) && node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                if (pair.Value is JsonValue value && value.TryGetValue(out string? s))
                {
                    result[pair.Key] = s;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces the whole group with the given set of key→value pairs. An empty set removes the group.
    /// </summary>
    public void SetStringGroup(string group, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            _root.Remove(group);
            return;
        }

        JsonObject obj = new();
        foreach (KeyValuePair<string, string> pair in values)
        {
            obj[pair.Key] = pair.Value;
        }

        _root[group] = obj;
    }

    /// <summary>Discards in-memory changes and reloads the file.</summary>
    public void Reload() => _root = Load(_path);

    /// <summary>Saves the current state to the file (atomically); a detached copy is never saved.</summary>
    public void Save()
    {
        if (_detached)
        {
            return;
        }

        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = _root.ToJsonString(WriteOptions);
        string tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, _path, overwrite: true);
    }

    private static JsonObject Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new JsonObject();
            }

            string text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JsonObject();
            }

            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
        catch (IOException)
        {
            return new JsonObject();
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }
}
