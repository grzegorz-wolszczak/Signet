using System;
using System.Collections.Generic;
using System.IO;

namespace Signet.Core;

/// <summary>
/// The result of reading an OCF container with <see cref="OcfReader"/>: the unpacked EPUB
/// together with the information from <c>META-INF</c>.
/// </summary>
public sealed record OcfContainer
{
    /// <summary>The folder the EPUB was unpacked to (the bookpath root), without a trailing separator.</summary>
    public required string RootDirectory { get; init; }

    /// <summary>The bookpath of the main OPF file (the first matching <c>rootfile</c> in <c>container.xml</c>).</summary>
    public required string OpfBookPath { get; init; }

    /// <summary>
    /// The bookpaths of all OPF files (renditions) listed in <c>container.xml</c>,
    /// in order of occurrence. Signet works only with the first one (<see cref="OpfBookPath"/>).
    /// </summary>
    public required IReadOnlyList<string> Rootfiles { get; init; }

    /// <summary>
    /// A map of bookpath -&gt; encryption algorithm identifier from <c>META-INF/encryption.xml</c>
    /// (empty when there is no such file). For fonts: <see cref="OcfReader.IdpfFontAlgorithmId"/> or
    /// <see cref="OcfReader.AdobeFontAlgorithmId"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, string> EncryptedFiles { get; init; }

    /// <summary>Non-critical warnings detected while reading (e.g. problems with <c>mimetype</c>).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>The bookpath of the folder containing the OPF (the base for relative paths in the OPF).</summary>
    public string OpfDirectory => BookPath.StartingDir(OpfBookPath);

    /// <summary>The full on-disk path of the file given by <paramref name="bookPath"/>.</summary>
    public string ToAbsolutePath(string bookPath)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        return Path.Combine(RootDirectory, bookPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
