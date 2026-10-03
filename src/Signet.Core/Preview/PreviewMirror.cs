using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using SysPath = System.IO.Path;

namespace Signet.Core.Preview;

/// <summary>
/// A mirror of the current state of the <see cref="Book"/> in a working folder from which the preview panel
/// (WebView) renders content via <c>file://</c> addresses.
/// </summary>
/// <remarks>
/// Why it exists: the WebView library in use (<c>NativeWebView</c>) has no "load HTML
/// from a string" method and no custom URI scheme registration — the content has to be given as a URL pointing at
/// a real file. Instead of rendering from the publication's own working folder (which would mix the preview
/// state with the model and wake file watchers), <see cref="PreviewMirror"/> maintains
/// a separate temporary folder.
/// <para>
/// <see cref="Sync(Book)"/> writes text resources from their <em>current in-memory content</em>,
/// and copies binary resources from the file in the publication's working folder. Unsaved changes from editor
/// tabs (living outside the resource) are passed as content overrides —
/// <see cref="Sync(Book, string?, IReadOnlyDictionary{string, string}?)"/>. Files left over from resources removed/renamed since the last synchronization
/// are deleted.
/// </para>
/// <para>
/// Synchronization is incremental: a file in the mirror is rewritten only when its content changed
/// (text — compared with the last written text; binary — source path,
/// size and modification time) or it disappeared from disk. The "live" preview calls <c>Sync</c> after every
/// pause in typing, so rewriting the whole book each time would be needless I/O.
/// </para>
/// </remarks>
public sealed class PreviewMirror : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TempFolder _folder;
    private readonly bool _ownsFolder;
    // The last written state of each mirror file (relative path → fingerprint), used to skip unchanged ones.
    private readonly Dictionary<string, object> _written = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Creates a mirror over a new temporary folder (deleted on <see cref="Dispose"/>).</summary>
    public PreviewMirror()
        : this(new TempFolder(), ownsFolder: true)
    {
    }

    /// <summary>Creates a mirror over the given working folder (not deleted on <see cref="Dispose"/>).</summary>
    /// <param name="folder">The preview's working folder.</param>
    public PreviewMirror(TempFolder folder)
        : this(folder, ownsFolder: false)
    {
    }

    private PreviewMirror(TempFolder folder, bool ownsFolder)
    {
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        _ownsFolder = ownsFolder;
    }

    /// <summary>The folder holding the mirrored publication.</summary>
    public string RootPath => _folder.Path;

    /// <summary>
    /// Writes all the publication's resources to <see cref="RootPath"/>, preserving the bookpath layout.
    /// Deletes files left over from resources that no longer exist.
    /// </summary>
    /// <param name="book">The publication to mirror.</param>
    public void Sync(Book book) => Sync(book, instrumentBookPath: null);

    /// <summary>
    /// Like <see cref="Sync(Book)"/>, but the resource with bookpath <paramref name="instrumentBookPath"/>
    /// is written in its instrumented version (<c>data-signet-loc</c> attributes for synchronizing
    /// the position with Code View).
    /// </summary>
    /// <param name="book">The publication to mirror.</param>
    /// <param name="instrumentBookPath">The bookpath of the (X)HTML resource to instrument, or <c>null</c>.</param>
    /// <returns>
    /// The instrumentation of the written resource, or <c>null</c> when <paramref name="instrumentBookPath"/>
    /// is <c>null</c> or does not point at a text resource.
    /// </returns>
    public PreviewInstrumentation? Sync(Book book, string? instrumentBookPath) =>
        Sync(book, instrumentBookPath, textOverrides: null);

    /// <summary>
    /// Like <see cref="Sync(Book, string?)"/>, but for the text resources listed in
    /// <paramref name="textOverrides"/> it writes the given text instead of <see cref="TextResource.GetText"/>
    /// (also in the instrumented version). The book's resources are not modified.
    /// </summary>
    /// <remarks>
    /// The "live" preview: the unsaved text of Code View tabs goes to the preview without writing
    /// the tab to the resource — the text is taken straight from the editor.
    /// </remarks>
    /// <param name="book">The publication to mirror.</param>
    /// <param name="instrumentBookPath">The bookpath of the (X)HTML resource to instrument, or <c>null</c>.</param>
    /// <param name="textOverrides">The working text by bookpath, or <c>null</c>. Entries for
    /// non-text or nonexistent resources are ignored.</param>
    /// <returns>As <see cref="Sync(Book, string?)"/>.</returns>
    public PreviewInstrumentation? Sync(
        Book book,
        string? instrumentBookPath,
        IReadOnlyDictionary<string, string>? textOverrides)
    {
        ArgumentNullException.ThrowIfNull(book);
        ThrowIfDisposed();

        HashSet<string> current = new(StringComparer.OrdinalIgnoreCase);
        PreviewInstrumentation? instrumentation = null;

        foreach (Resource resource in book.GetAllResources())
        {
            string relative = NormalizeRelative(resource.BookPath);
            if (relative.Length == 0)
            {
                continue;
            }

            string destination = SysPath.Combine(RootPath, relative);

            if (resource is TextResource text)
            {
                string content = textOverrides is not null
                    && textOverrides.TryGetValue(resource.BookPath, out string? working)
                    ? working
                    : LoadedText(text);
                bool instrument = instrumentBookPath is not null
                    && string.Equals(resource.BookPath, instrumentBookPath, StringComparison.Ordinal);

                if (instrument)
                {
                    instrumentation = PreviewInstrumentation.Create(content);
                    content = instrumentation.Html;
                }

                WriteIfChanged(relative, destination, content, () => File.WriteAllText(destination, content, Utf8NoBom));
            }
            else if (File.Exists(resource.FullPath))
            {
                FileInfo source = new(resource.FullPath);
                BinaryFingerprint fingerprint = new(source.FullName, source.Length, source.LastWriteTimeUtc);
                WriteIfChanged(relative, destination, fingerprint, () => File.Copy(source.FullName, destination, overwrite: true));
            }
            else
            {
                continue;
            }

            current.Add(relative);
        }

        PruneRemoved(current);

        return instrumentation;
    }

    /// <summary>The <c>file://</c> address of the file with the given bookpath inside the mirror.</summary>
    /// <param name="bookPath">The resource path relative to the publication's root folder (the <c>/</c> separator).</param>
    public Uri UrlForBookPath(string bookPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(bookPath);
        ThrowIfDisposed();

        string relative = NormalizeRelative(bookPath);
        return new Uri(SysPath.Combine(RootPath, relative));
    }

    /// <summary>The <c>file://</c> address of the given resource inside the mirror.</summary>
    /// <param name="resource">The publication resource.</param>
    public Uri UrlFor(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return UrlForBookPath(resource.BookPath);
    }

    /// <summary>
    /// Whether the address points at a file inside the mirror ("internal" navigation — a link to another chapter),
    /// as opposed to an external URL, which the preview should block.
    /// </summary>
    /// <param name="uri">The navigation target address.</param>
    public bool IsInsideMirror(Uri? uri)
    {
        ThrowIfDisposed();
        if (uri is null || !uri.IsAbsoluteUri || !uri.IsFile)
        {
            return false;
        }

        string root = SysPath.TrimEndingDirectorySeparator(SysPath.GetFullPath(RootPath));
        string target = SysPath.GetFullPath(uri.LocalPath);

        return target.Equals(root, PathComparison)
            || target.StartsWith(root + SysPath.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>
    /// The bookpath of the file pointed at by a <c>file://</c> address inside the mirror, or <c>null</c>
    /// when the address lies outside the mirror.
    /// </summary>
    /// <param name="uri">The <c>file://</c> address.</param>
    public string? BookPathForUri(Uri? uri)
    {
        if (!IsInsideMirror(uri))
        {
            return null;
        }

        string root = SysPath.TrimEndingDirectorySeparator(SysPath.GetFullPath(RootPath));
        string target = SysPath.GetFullPath(uri!.LocalPath);
        if (target.Equals(root, PathComparison))
        {
            return null;
        }

        return target[(root.Length + 1)..].Replace(SysPath.DirectorySeparatorChar, '/');
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsFolder)
        {
            _folder.Dispose();
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string NormalizeRelative(string bookPath)
    {
        string trimmed = bookPath.Replace('\\', '/').Trim('/');
        return trimmed.Length == 0
            ? string.Empty
            : trimmed.Replace('/', SysPath.DirectorySeparatorChar);
    }

    private static string LoadedText(TextResource text)
    {
        text.InitialLoad();
        return text.GetText();
    }

    // Writes the file if its fingerprint (the text or BinaryFingerprint) differs from the last written one
    // or the file is not on disk. The fingerprint is remembered only after a successful write.
    private void WriteIfChanged(string relative, string destination, object fingerprint, Action write)
    {
        if (_written.TryGetValue(relative, out object? previous)
            && previous.Equals(fingerprint)
            && File.Exists(destination))
        {
            return;
        }

        _written.Remove(relative);
        Directory.CreateDirectory(SysPath.GetDirectoryName(destination)!);
        write();
        _written[relative] = fingerprint;
    }

    private void PruneRemoved(HashSet<string> current)
    {
        List<string> stale = new();
        foreach (string relative in _written.Keys)
        {
            if (!current.Contains(relative))
            {
                stale.Add(relative);
            }
        }

        foreach (string relative in stale)
        {
            _written.Remove(relative);

            string path = SysPath.Combine(RootPath, relative);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                string? dir = SysPath.GetDirectoryName(path);
                while (!string.IsNullOrEmpty(dir)
                    && !dir.Equals(SysPath.TrimEndingDirectorySeparator(RootPath), PathComparison)
                    && Directory.Exists(dir)
                    && !Directory.EnumerateFileSystemEntries(dir).GetEnumerator().MoveNext())
                {
                    Directory.Delete(dir);
                    dir = SysPath.GetDirectoryName(dir);
                }
            }
            catch (IOException)
            {
                // Best effort — a locked file will be overwritten at the next synchronization anyway.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record BinaryFingerprint(string SourcePath, long Length, DateTime LastWriteTimeUtc);
}
