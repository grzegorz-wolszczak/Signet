using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using Avalonia.Threading;
using Signet.App.Resources;

namespace Signet.App.Infrastructure;

/// <summary>
/// The debug log (Preferences → Debug → "Enable debug logging"): what the user did — clicks, menu choices, settings
/// changed, window and panel operations, file and bulk operations — written to the regular log file as
/// <c>[DBG] Category: message</c> lines, so a problem report can be traced back to the steps that led to it.
/// </summary>
/// <remarks>
/// <para>Nothing typed is logged (Code View, text fields, filters): a text setting is logged as its final value once
/// the user stops typing, and the only typed texts that are ever logged are the Find &amp; Replace patterns, together
/// with the dialog's settings when one of its operations runs. Paths on disk are masked (<see cref="MaskPath"/>).</para>
/// <para>The last events are also kept in memory (<see cref="RecentEvents"/>), so an error can be logged with what
/// led to it.</para>
/// </remarks>
public static class DebugLog
{
    private const int RecentCapacity = 30;
    private const int MaxValueLength = 200;

    private static readonly Queue<string> Recent = new();
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _keysByText;
    private static string _keysCulture = string.Empty;

    // Resolved on every write: the global logger is configured at startup, after this class may have been touched.
    private static Serilog.ILogger Logger => Serilog.Log.ForContext("SourceContext", "Signet.Debug");

    /// <summary>Whether debug events are written (the Preferences setting).</summary>
    public static bool IsEnabled { get; set; }

    /// <summary>Writes one event (when enabled): <c>[DBG] {category}: {message}</c>.</summary>
    public static void Write(string category, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        string line = category + ": " + message;
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > RecentCapacity)
            {
                Recent.Dequeue();
            }
        }

        Logger.Information("[DBG] {DebugEvent}", line);
    }

    /// <summary>The last events written (oldest first).</summary>
    public static IReadOnlyList<string> RecentEvents()
    {
        lock (Gate)
        {
            return Recent.ToList();
        }
    }

    /// <summary>A path on disk with the user's profile folder replaced by <c>~</c>.</summary>
    public static string MaskPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[home.Length..]
            : path;
    }

    /// <summary>The time since <paramref name="startTimestamp"/> (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>), e.g. <c>"120 ms"</c>.</summary>
    public static string Elapsed(long startTimestamp) =>
        string.Create(CultureInfo.InvariantCulture, $"{System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:0} ms");

    /// <summary>A value for the log: quoted text (shortened), <c>(none)</c> for null, invariant numbers.</summary>
    public static string Describe(object? value)
    {
        string text = value switch
        {
            null => "(none)",
            string s => "\"" + Shorten(s) + "\"",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        return Shorten(text);
    }

    /// <summary>
    /// The resource key whose text (in the current UI language) is <paramref name="text"/> — a label that does not
    /// depend on the language (<c>Common_Close</c> instead of "Zamknij"/"Close"); <c>null</c> when there is none.
    /// Mnemonic markers (<c>&amp;</c>, <c>_</c>) and a trailing ellipsis are ignored.
    /// </summary>
    public static string? ResourceKeyOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Dictionary<string, string> map = KeysByText();
        return map.GetValueOrDefault(Normalize(text));
    }

    /// <summary>
    /// Logs every change of a simple public property (bool, number, enum, text) of <paramref name="source"/> as
    /// <c>{context}: Property: before → after</c> until disposed. A text property is logged once the user stops typing
    /// (with the value from before the typing), so single keystrokes are never logged.
    /// </summary>
    public static IDisposable TrackChanges(INotifyPropertyChanged source, string context)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ChangeTracker(source, context);
    }

    private static Dictionary<string, string> KeysByText()
    {
        string culture = CultureInfo.CurrentUICulture.Name;
        lock (Gate)
        {
            if (_keysByText is not null && _keysCulture == culture)
            {
                return _keysByText;
            }

            Dictionary<string, string> map = new(StringComparer.Ordinal);
            ResourceManager manager = Strings.RawManager;

            // The current language first, then the neutral (Polish) texts it falls back to.
            foreach (ResourceSet? set in new[]
                     {
                         manager.GetResourceSet(CultureInfo.CurrentUICulture, createIfNotExists: true, tryParents: true),
                         manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false),
                     })
            {
                if (set is null)
                {
                    continue;
                }

                foreach (DictionaryEntry entry in set)
                {
                    if (entry.Key is string key && entry.Value is string value && value.Length > 0)
                    {
                        map.TryAdd(Normalize(value), key);
                    }
                }
            }

            _keysByText = map;
            _keysCulture = culture;
            return map;
        }
    }

    private static string Normalize(string text) =>
        text.Replace("&&", "\u0001", StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("\u0001", "&", StringComparison.Ordinal)
            .TrimEnd('.', '…', ' ', ':')
            .Trim();

    private static string Shorten(string text) =>
        text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "…";

    // Logs the changes of the simple properties of one object (see TrackChanges).
    private sealed class ChangeTracker : IDisposable
    {
        private static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(1.5);

        private readonly INotifyPropertyChanged _source;
        private readonly string _context;
        private readonly Dictionary<string, PropertyInfo> _properties;
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DispatcherTimer> _typing = new(StringComparer.Ordinal);

        public ChangeTracker(INotifyPropertyChanged source, string context)
        {
            _source = source;
            _context = context;
            _properties = source.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsSimple(p.PropertyType))
                .ToDictionary(p => p.Name, StringComparer.Ordinal);
            foreach (PropertyInfo property in _properties.Values)
            {
                _values[property.Name] = Read(property);
            }

            _source.PropertyChanged += OnPropertyChanged;
        }

        public void Dispose()
        {
            _source.PropertyChanged -= OnPropertyChanged;
            foreach ((string name, DispatcherTimer timer) in _typing.ToList())
            {
                timer.Stop();
                Flush(name);
            }

            _typing.Clear();
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not { } name || !_properties.TryGetValue(name, out PropertyInfo? property))
            {
                return;
            }

            if (property.PropertyType == typeof(string))
            {
                // Typing: wait for a pause, then log the whole change once.
                if (!_typing.TryGetValue(name, out DispatcherTimer? timer))
                {
                    timer = new DispatcherTimer { Interval = TypingPause };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        _typing.Remove(name);
                        Flush(name);
                    };
                    _typing[name] = timer;
                }

                timer.Stop();
                timer.Start();
                return;
            }

            Flush(name);
        }

        private void Flush(string name)
        {
            object? before = _values.GetValueOrDefault(name);
            object? after = Read(_properties[name]);
            if (Equals(before, after))
            {
                return;
            }

            _values[name] = after;
            Write(_context, $"{name}: {Describe(before)} → {Describe(after)}");
        }

        private object? Read(PropertyInfo property)
        {
            try
            {
                return property.GetValue(_source);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private static bool IsSimple(Type type)
        {
            Type t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);
        }
    }
}
