using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core.Themes;

/// <summary>
/// The themes of the interface or the color schemes of the editors (<see cref="ThemeDocumentKind"/>; one store each):
/// the bundled ones (Signet Light, Signet Dark) and the user's, which are files <c>&lt;id&gt;.signet-theme.xml</c> /
/// <c>&lt;id&gt;.signet-scheme.xml</c> in a folder (<see cref="Misc.AppDirectories.ThemesDirectory"/>,
/// <see cref="Misc.AppDirectories.SchemesDirectory"/>). The actions of the list of Preferences (IntelliJ's
/// <c>AbstractSchemeActions</c>): duplicate, rename, delete, restore defaults, export, import.
/// <para>A bundled theme can be changed as in Rider: the changes are kept in the folder as an overlay — a file with the
/// bundled theme's id holding only what differs — and <see cref="RestoreDefaults"/> removes it. A theme of the user has
/// a bundled theme as its parent and keeps what differs from it.</para>
/// </summary>
public sealed class ThemeStore
{
    /// <summary>The identifier of Signet Light — the default theme and the default color scheme.</summary>
    public const string LightId = "signet-light";

    /// <summary>The identifier of Signet Dark (a theme and a color scheme).</summary>
    public const string DarkId = "signet-dark";

    private static readonly string[] BundledIds = { LightId, DarkId };

    private readonly string _directory;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, Theme> _pristine;
    private readonly Dictionary<string, Theme> _overlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Theme> _user = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly List<string> _loadProblems = new();

    /// <summary>Creates the store and reads the folder (a file it cannot read is skipped, see <see cref="LoadProblems"/>).</summary>
    /// <param name="directory">The folder of the user's themes and of the overlays of the bundled ones.</param>
    /// <param name="kind">Whether the store holds the themes of the interface or the color schemes of the editors.</param>
    /// <param name="now">The clock for the dates of the themes; <c>null</c> = <see cref="DateTimeOffset.UtcNow"/>.</param>
    public ThemeStore(string directory, ThemeDocumentKind kind = ThemeDocumentKind.Theme, Func<DateTimeOffset>? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Kind = kind;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _pristine = BundledIds.ToDictionary(id => id, id => LoadBundled(id, kind), StringComparer.Ordinal);
        Resolver = new ThemeResolver(Find);
        Load();
    }

    /// <summary>Whether this store holds the themes of the interface or the color schemes of the editors.</summary>
    public ThemeDocumentKind Kind { get; }

    /// <summary>Resolves the keys of the themes of this store (with their parents).</summary>
    public ThemeResolver Resolver { get; }

    /// <summary>The files of the folder that could not be read, with the reason — to log or show.</summary>
    public IReadOnlyList<string> LoadProblems => _loadProblems;

    /// <summary>The bundled themes (with their changes) and then the user's, by name.</summary>
    public IReadOnlyList<Theme> Themes =>
        BundledIds.Select(Get)
            .Concat(_user.Values.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

    /// <summary>Whether the theme is bundled (it cannot be renamed or deleted).</summary>
    public static bool IsBundled(string id) => BundledIds.Contains(id, StringComparer.Ordinal);

    /// <summary>Whether a bundled theme has changes ("Restore Defaults" is available).</summary>
    public bool IsModified(string id) => _overlays.ContainsKey(id);

    /// <summary>The theme with the identifier (a bundled one with its changes); <c>null</c> = there is none.</summary>
    public Theme? Find(string id)
    {
        if (_pristine.TryGetValue(id, out Theme? bundled))
        {
            return _overlays.TryGetValue(id, out Theme? overlay) ? Apply(bundled, overlay) : bundled;
        }

        return _user.GetValueOrDefault(id);
    }

    /// <summary>The theme with the identifier.</summary>
    /// <exception cref="KeyNotFoundException">There is no such theme.</exception>
    public Theme Get(string id) => Find(id) ?? throw new KeyNotFoundException($"No theme '{id}'.");

    /// <summary>
    /// Whether the theme (possibly with changes not saved yet) changes the key against what it starts from: a theme of
    /// the user sets the key itself instead of taking its parent's value; a bundled theme gives it another value than
    /// the one it comes with.
    /// </summary>
    public bool ChangesKey(Theme theme, ThemeKey key)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(key);
        if (!_pristine.TryGetValue(theme.Id, out Theme? bundled))
        {
            return theme.Entries.ContainsKey(key.Id);
        }

        return theme.Entries.TryGetValue(key.Id, out ThemeEntry? entry)
            ? !Equals(entry, Effective(bundled, key))
            : bundled.Entries.ContainsKey(key.Id);
    }

    /// <summary>
    /// The theme with the change of the key undone (see <see cref="ChangesKey"/>): a theme of the user takes the
    /// parent's value again, a bundled theme the value it comes with.
    /// </summary>
    public Theme WithoutChange(Theme theme, ThemeKey key)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(key);
        return _pristine.TryGetValue(theme.Id, out Theme? bundled) && bundled.Entries.TryGetValue(key.Id, out ThemeEntry? entry)
            ? theme.With(key.Id, entry)
            : theme.Without(key.Id);
    }

    /// <summary>Whether a theme other than <paramref name="exceptId"/> has the name (ignoring case).</summary>
    public bool IsNameTaken(string name, string? exceptId = null) =>
        Themes.Any(t => t.Id != exceptId && string.Equals(t.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The name, or the name with the first free number added (<c>Sepia (2)</c>).</summary>
    public string UniqueName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string trimmed = name.Trim();
        string candidate = trimmed;
        for (int n = 2; IsNameTaken(candidate); n++)
        {
            candidate = $"{trimmed} ({n})";
        }

        return candidate;
    }

    /// <summary>
    /// "Duplicate…": a new theme of the user with the values of the source. A copy of a bundled theme has it as its
    /// parent and keeps its changes; a copy of a user's theme has the same parent and the same values.
    /// </summary>
    /// <param name="sourceId">The theme to copy.</param>
    /// <param name="name">The name of the copy (made unique).</param>
    /// <param name="edited">The source with changes not saved yet (the editor's copy); <c>null</c> = as saved.</param>
    public Theme Duplicate(string sourceId, string name, Theme? edited = null)
    {
        Theme source = edited ?? Get(sourceId);
        Theme copy = IsBundled(sourceId)
            ? Difference(source, _pristine[sourceId]) with { ParentId = sourceId }
            : source;
        DateTimeOffset now = _now();
        copy = copy with { Id = NewId(), Name = UniqueName(name), Created = now, Modified = now };
        _user[copy.Id] = copy;
        Write(copy);
        return copy;
    }

    /// <summary>"Rename…" a theme of the user.</summary>
    /// <exception cref="InvalidOperationException">The theme is bundled.</exception>
    /// <exception cref="ArgumentException">Another theme has the name.</exception>
    public Theme Rename(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Theme theme = UserTheme(id);
        if (IsNameTaken(name, id))
        {
            throw new ArgumentException($"A theme named '{name}' exists.", nameof(name));
        }

        theme = theme with { Name = name.Trim(), Modified = _now() };
        _user[id] = theme;
        Write(theme);
        return theme;
    }

    /// <summary>
    /// "Delete" a theme of the user (its file). The themes that had it as their parent take its parent and keep its
    /// values under their own, so they look the same.
    /// </summary>
    /// <exception cref="InvalidOperationException">The theme is bundled.</exception>
    public void Delete(string id)
    {
        Theme deleted = UserTheme(id);
        _user.Remove(id);
        File.Delete(PathOf(id));
        _paths.Remove(id);
        foreach (Theme child in _user.Values.Where(t => t.ParentId == id).ToList())
        {
            Theme adopted = child with
            {
                ParentId = deleted.ParentId ?? LightId,
                Variant = child.Variant ?? deleted.Variant,
                Entries = deleted.Entries.SetItems(child.Entries),
            };
            _user[child.Id] = adopted;
            Write(adopted);
        }
    }

    /// <summary>
    /// Saves a theme changed in the editor: a theme of the user as it is; a bundled theme (with all its values) as the
    /// overlay of what differs from it — no overlay when nothing does.
    /// </summary>
    public void Save(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (_pristine.TryGetValue(theme.Id, out Theme? bundled))
        {
            Theme overlay = Difference(theme, bundled);
            if (IsEmpty(overlay))
            {
                RestoreDefaults(theme.Id);
                return;
            }

            _overlays[theme.Id] = overlay;
            Write(overlay);
            return;
        }

        UserTheme(theme.Id);
        theme = theme with { Modified = _now() };
        _user[theme.Id] = theme;
        Write(theme);
    }

    /// <summary>"Restore Defaults" of a bundled theme: its changes are removed.</summary>
    public void RestoreDefaults(string id)
    {
        if (_overlays.Remove(id))
        {
            File.Delete(PathOf(id));
            _paths.Remove(id);
        }
    }

    /// <summary>
    /// "Export…": the theme as a file another Signet can import. A bundled theme is written as a theme of the user with
    /// it as the parent and its changes.
    /// </summary>
    /// <param name="id">The theme.</param>
    /// <param name="writer">Receives the file.</param>
    /// <param name="writtenBy">The Signet version written to the file; <c>null</c> = none.</param>
    /// <param name="edited">The theme with changes not saved yet (the editor's copy); <c>null</c> = as saved.</param>
    public void Export(string id, TextWriter writer, string? writtenBy = null, Theme? edited = null)
    {
        Theme theme = edited ?? Get(id);
        if (IsBundled(id))
        {
            theme = Difference(theme, _pristine[id]) with { Id = NewId(), ParentId = id };
        }

        ThemeXml.Write(theme, Kind, writer, writtenBy);
    }

    /// <summary>
    /// "Import…": reads a theme file and adds it as a theme of the user. A taken identifier gets a new one, a taken name
    /// a number; a missing parent is replaced by Signet Light (with a warning).
    /// </summary>
    /// <param name="reader">The file.</param>
    /// <param name="warnings">Receives what was skipped or changed.</param>
    /// <exception cref="ThemeFormatException">The file is not a theme this version can read.</exception>
    public Theme Import(TextReader reader, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        Theme theme = ThemeXml.Read(reader, Kind, warnings);
        if (theme.ParentId is { } parent && Find(parent) is null)
        {
            warnings.Add(CoreStrings.Format("Theme_ParentMissing", parent));
            theme = theme with { ParentId = LightId };
        }

        DateTimeOffset now = _now();
        theme = theme with
        {
            Id = Find(theme.Id) is null && Ulid.TryParse(theme.Id, out _) ? theme.Id : NewId(),
            Name = UniqueName(theme.Name),
            ParentId = theme.ParentId ?? LightId,
            Created = theme.Created ?? now,
            Modified = now,
        };
        _user[theme.Id] = theme;
        Write(theme);
        return theme;
    }

    // What a theme sets differently from a bundled one (compared with the bundled one's resolved values, so a value
    // equal to the bundled one is not kept).
    private static Theme Difference(Theme theme, Theme bundled)
    {
        ImmutableDictionary<string, ThemeEntry> entries = theme.Entries
            .Where(e => !ThemeKeys.TryGet(e.Key, out ThemeKey? key) || !Equals(e.Value, Effective(bundled, key)))
            .ToImmutableDictionary(StringComparer.Ordinal);
        return new Theme(theme.Id, theme.Name)
        {
            Variant = theme.Variant != bundled.Variant ? theme.Variant : null,
            Entries = entries,
        };
    }

    // The entry of the key in the bundled theme: its own, or the default of the key it resolves to.
    private static ThemeEntry Effective(Theme bundled, ThemeKey key) =>
        bundled.Entries.TryGetValue(key.Id, out ThemeEntry? entry)
            ? entry
            : key.Kind == ThemeKeyKind.Color ? ThemeEntry.Of(key.DefaultColor) : ThemeEntry.Of(key.DefaultAttributes!);

    private static bool IsEmpty(Theme overlay) =>
        overlay.Entries.IsEmpty && overlay.Variant is null;

    // A bundled theme with its changes.
    private static Theme Apply(Theme bundled, Theme overlay) => bundled with
    {
        Variant = overlay.Variant ?? bundled.Variant,
        Entries = bundled.Entries.SetItems(overlay.Entries),
    };

    private Theme UserTheme(string id) =>
        IsBundled(id)
            ? throw new InvalidOperationException($"The theme '{id}' is bundled.")
            : _user.TryGetValue(id, out Theme? theme) ? theme : throw new KeyNotFoundException($"No theme '{id}'.");

    private void Load()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(_directory, "*" + ThemeXml.ExtensionOf(Kind)).Order(StringComparer.Ordinal))
        {
            try
            {
                Theme theme;
                using (StreamReader reader = new(path, Encoding.UTF8))
                {
                    theme = ThemeXml.Read(reader, Kind);
                }

                if (IsBundled(theme.Id))
                {
                    _overlays[theme.Id] = theme;
                }
                else
                {
                    _user[theme.Id] = theme;
                }

                _paths[theme.Id] = path;
            }
            catch (Exception e) when (e is ThemeFormatException or IOException or UnauthorizedAccessException)
            {
                _loadProblems.Add($"{Path.GetFileName(path)}: {e.Message}");
            }
        }

        // Once every file is read (a parent may come after its child): a missing parent becomes Signet Light.
        foreach (Theme theme in _user.Values.ToList())
        {
            if (theme.ParentId is not { } parent || Find(parent) is null)
            {
                _user[theme.Id] = theme with { ParentId = LightId };
            }
        }
    }

    // Writes the file of the theme (through a temporary file, so a failure leaves the old one); a theme read from a file
    // named otherwise moves to <id>.signet-theme.xml (<id>.signet-scheme.xml for a color scheme).
    private void Write(Theme theme)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, theme.Id + ThemeXml.ExtensionOf(Kind));
        string temporary = path + ".tmp";
        using (StreamWriter writer = new(temporary, false, new UTF8Encoding(false)))
        {
            ThemeXml.Write(theme, Kind, writer);
        }

        File.Move(temporary, path, overwrite: true);
        if (_paths.TryGetValue(theme.Id, out string? old) && old != path)
        {
            File.Delete(old);
        }

        _paths[theme.Id] = path;
    }

    private string PathOf(string id) =>
        _paths.TryGetValue(id, out string? path) ? path : Path.Combine(_directory, id + ThemeXml.ExtensionOf(Kind));

    private static string NewId() => Ulid.NewUlid().ToString();

    private static Theme LoadBundled(string id, ThemeDocumentKind kind)
    {
        string resource = (kind == ThemeDocumentKind.Theme ? "Themes." : "Schemes.") + id;
        using Stream stream = typeof(ThemeStore).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(CoreStrings.Format("Error_EmbeddedResourceMissing", resource));
        using StreamReader reader = new(stream, Encoding.UTF8);
        return ThemeXml.Read(reader, kind);
    }
}
