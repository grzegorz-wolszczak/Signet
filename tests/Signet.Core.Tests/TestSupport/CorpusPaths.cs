using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Paths to the directories of the test corpus (<c>tests/corpus</c>). The root directory is
/// passed through the assembly attribute <c>AssemblyMetadata("CorpusRoot", …)</c> set
/// in <c>Signet.Core.Tests.csproj</c>.
/// </summary>
public static class CorpusPaths
{
    /// <summary>The corpus root directory. Throws if it does not exist.</summary>
    public static string Root { get; } = ResolveRoot();

    /// <summary>EPUB 3.0 — the simplest valid one.</summary>
    public static string Epub3Minimal => Sub("epub3", "minimal");

    /// <summary>EPUB 3.0 with an NCX included.</summary>
    public static string Epub3WithNcx => Sub("epub3", "with-ncx");

    /// <summary>EPUB 3.0 z obrazami, fontem i audio.</summary>
    public static string Epub3Media => Sub("epub3", "media");

    /// <summary>EPUB 3.0 with rich metadata (refinements, multiple identifiers, link, guide).</summary>
    public static string Epub3RichMetadata => Sub("epub3", "rich-metadata");

    /// <summary>
    /// EPUB 3.0 with two obfuscated fonts (IDPF + Adobe) and <c>META-INF/encryption.xml</c>.
    /// The font files are stored in the directory <b>deobfuscated</b> (golden) — the test obfuscates
    /// a copy of them before packing, and after the import compares with the golden form.
    /// </summary>
    public static string Epub3ObfuscatedFonts => Sub("epub3", "obfuscated-fonts");

    /// <summary>EPUB 2.0 — the simplest valid one.</summary>
    public static string Epub2Minimal => Sub("epub2", "minimal");

    /// <summary>EPUB 3.0 with a non-standard, deep directory structure.</summary>
    public static string EdgeDeepFolders => Sub("edge", "deep-folders");

    /// <summary>The directory of a malformed EPUB with the given name (a <c>malformed/</c> subdirectory).</summary>
    public static string Malformed(string name) => Sub("malformed", name);

    /// <summary>The directories of all valid EPUBs — for parametrized tests.</summary>
    public static IReadOnlyList<string> ValidEpubDirs { get; } =
    [
        Sub("epub3", "minimal"),
        Sub("epub3", "with-ncx"),
        Sub("epub3", "media"),
        Sub("epub3", "rich-metadata"),
        Sub("epub2", "minimal"),
        Sub("edge", "deep-folders"),
    ];

    private static string Sub(params string[] parts)
    {
        string[] all = new string[parts.Length + 1];
        all[0] = Root;
        Array.Copy(parts, 0, all, 1, parts.Length);
        string path = Path.Combine(all);

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Corpus directory missing: {path}");
        }

        return path;
    }

    private static string ResolveRoot()
    {
        string? raw = typeof(CorpusPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "CorpusRoot", StringComparison.Ordinal))
            ?.Value;

        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException(
                "The assembly attribute AssemblyMetadata(\"CorpusRoot\") is not set — check Signet.Core.Tests.csproj.");
        }

        string full = Path.GetFullPath(raw);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"The test corpus does not exist: {full}");
        }

        return full;
    }
}
