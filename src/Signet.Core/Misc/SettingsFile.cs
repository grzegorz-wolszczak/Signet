using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Signet.Core.Misc;

/// <summary>
/// Low-level layer over the XML settings file. The settings are a two-level object
/// <c>{ "group": { "key": value } }</c> held in memory as JSON nodes — settings are organized in groups
/// (e.g. the <c>user_preferences</c> group); <see cref="SettingsXml"/> maps it to and from the file.
/// </summary>
/// <remarks>
/// Reading is lenient: a missing file, an empty file or invalid XML yield an
/// empty store (no exception). Saving is atomic (<see cref="XmlConfig.Save"/>).
/// The class is not thread-safe.
/// <para>
/// Several instances of the program share the file, so saving does not just write the in-memory state: it writes
/// only what this instance changed since it loaded or last saved the file, on top of the file as it is now, and then
/// takes the merged result as its own state. An instance that changed nothing writes nothing of other instances'
/// work away. A list registered with <see cref="AddRecentList"/> (e.g. the recent files) is merged as a list: what this
/// instance put at its front stays in front, the entries other instances added stay, and the ones this instance
/// removed are gone.
/// </para>
/// </remarks>
internal sealed class SettingsFile
{
    private readonly string _path;
    private readonly bool _detached;
    private readonly Dictionary<(string Group, string Key), int> _recentLists = new();
    private JsonObject _root;

    // What the file held when this instance loaded or last saved it: the entries that differ from it are this instance's changes.
    private JsonObject _baseline;

    public SettingsFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _root = Load(path);
        _baseline = (JsonObject)_root.DeepClone();
    }

    private SettingsFile(string path, JsonObject root)
    {
        _path = path;
        _root = root;
        _baseline = root;
        _detached = true;
    }

    /// <summary>
    /// Registers a list of strings, newest first (<paramref name="maxCount"/> entries at most), that is merged as a list
    /// when other instances of the program changed it too (see the class remarks).
    /// </summary>
    public void AddRecentList(string group, string key, int maxCount) => _recentLists[(group, key)] = maxCount;

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
    public void Reload()
    {
        _root = Load(_path);
        _baseline = (JsonObject)_root.DeepClone();
    }

    /// <summary>
    /// Saves this instance's changes to the file (atomically), merged with what other instances saved meanwhile (see the
    /// class remarks); a detached copy is never saved.
    /// </summary>
    public void Save()
    {
        if (_detached)
        {
            return;
        }

        using FileLock fileLock = new(_path);

        // A missing or unreadable file (e.g. a first run) has nothing to merge with: the whole state is written.
        JsonObject merged = XmlConfig.TryLoad(_path) is { } document
            ? Merge(SettingsXml.FromDocument(document))
            : (JsonObject)_root.DeepClone();

        XmlConfig.Save(_path, SettingsXml.ToDocument(merged));
        _root = merged;
        _baseline = (JsonObject)merged.DeepClone();
    }

    // The file's content with this instance's changes (the entries that differ from the baseline) applied.
    private JsonObject Merge(JsonObject onDisk)
    {
        Dictionary<(string Group, string Key), JsonNode> mine = Flatten(_root);
        Dictionary<(string Group, string Key), JsonNode> baseline = Flatten(_baseline);

        foreach ((string Group, string Key) entry in mine.Keys.Union(baseline.Keys))
        {
            mine.TryGetValue(entry, out JsonNode? mineNode);
            baseline.TryGetValue(entry, out JsonNode? baselineNode);
            if (JsonNode.DeepEquals(mineNode, baselineNode))
            {
                continue;
            }

            if (mineNode is null)
            {
                RemoveFrom(onDisk, entry.Group, entry.Key);
                continue;
            }

            JsonNode result = mineNode.DeepClone();
            if (_recentLists.TryGetValue(entry, out int maxCount) && mineNode is JsonArray mineList)
            {
                JsonNode? diskNode = onDisk[entry.Group] is JsonObject diskGroup && diskGroup.TryGetPropertyValue(entry.Key, out JsonNode? n) ? n : null;
                result = MergeRecentList(mineList, baselineNode as JsonArray, diskNode as JsonArray, maxCount);
            }

            if (onDisk[entry.Group] is not JsonObject group)
            {
                group = new JsonObject();
                onDisk[entry.Group] = group;
            }

            group[entry.Key] = result;
        }

        return onDisk;
    }

    /// <summary>
    /// Merges a list kept newest first. <paramref name="mine"/> is this instance's list, <paramref name="baseline"/> the
    /// list it started from, <paramref name="onDisk"/> the list in the file now. The entries this instance put in front
    /// (added, or moved to the front) come first, then the file's entries without the ones this instance removed.
    /// </summary>
    internal static JsonArray MergeRecentList(JsonArray mine, JsonArray? baseline, JsonArray? onDisk, int maxCount)
    {
        List<string> mineList = Strings(mine);
        List<string> baselineList = Strings(baseline);
        List<string> diskList = Strings(onDisk);

        // The part of the front of `mine` that is not just the baseline list: the smallest prefix after which the rest of `mine`
        // is what the baseline had left (the baseline without the prefix, shortened at the end).
        int front = 0;
        for (; front < mineList.Count; front++)
        {
            List<string> prefix = mineList.GetRange(0, front);
            List<string> rest = baselineList.Where(x => !prefix.Contains(x)).Take(mineList.Count - front).ToList();
            if (mineList.Skip(front).SequenceEqual(rest))
            {
                break;
            }
        }

        List<string> promoted = mineList.GetRange(0, front);
        HashSet<string> removed = baselineList.Where(x => !mineList.Contains(x)).ToHashSet(StringComparer.Ordinal);
        List<string> merged = promoted
            .Concat(diskList.Where(x => !promoted.Contains(x) && !removed.Contains(x)))
            .Take(maxCount)
            .ToList();
        return new JsonArray(merged.Select(x => JsonValue.Create(x)).ToArray<JsonNode?>());
    }

    private static List<string> Strings(JsonArray? array) =>
        array?.Select(n => n?.GetValue<string>() ?? string.Empty).Where(x => x.Length > 0).ToList() ?? new List<string>();

    private static Dictionary<(string Group, string Key), JsonNode> Flatten(JsonObject root)
    {
        Dictionary<(string Group, string Key), JsonNode> result = new();
        foreach (KeyValuePair<string, JsonNode?> group in root)
        {
            if (group.Value is JsonObject groupObj)
            {
                foreach (KeyValuePair<string, JsonNode?> entry in groupObj)
                {
                    if (entry.Value is not null)
                    {
                        result[(group.Key, entry.Key)] = entry.Value;
                    }
                }
            }
        }

        return result;
    }

    private static void RemoveFrom(JsonObject root, string group, string key)
    {
        if (root[group] is JsonObject groupObj && groupObj.Remove(key) && groupObj.Count == 0)
        {
            root.Remove(group);
        }
    }

    /// <summary>
    /// An exclusive lock on the settings file between the processes (a named mutex), so that two instances saving at the
    /// same moment do not interleave the read-merge-write. When the lock cannot be taken in time, saving goes on without it.
    /// </summary>
    private sealed class FileLock : IDisposable
    {
        private readonly System.Threading.Mutex? _mutex;
        private readonly bool _held;

        public FileLock(string path)
        {
            try
            {
                string name = "Signet-settings-" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToUpperInvariant())));
                _mutex = new System.Threading.Mutex(false, name);
                _held = _mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Threading.AbandonedMutexException or IOException or ArgumentException)
            {
                _held = false;
            }
        }

        public void Dispose()
        {
            if (_held)
            {
                _mutex!.ReleaseMutex();
            }

            _mutex?.Dispose();
        }
    }

    private static JsonObject Load(string path) => SettingsXml.FromDocument(XmlConfig.TryLoad(path));
}
