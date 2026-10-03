using System;
using System.Collections.Generic;
using CoreBookPath = Signet.Core.BookPath;

namespace Signet.Core.SourceUpdates;

/// <summary>
/// A shared algorithm for recalculating a single reference value (the <c>href</c>/<c>src</c> attribute,
/// <c>url(...)</c> in CSS, <c>content/@src</c> in NCX etc.) after resources in the
/// publication are renamed or moved.
/// </summary>
/// <remarks>
/// The rule (identical for all file types):
/// <list type="number">
/// <item>a value with a scheme (<c>:</c>) — an external reference / <c>data:</c> — is left unchanged;</item>
/// <item>the value is split into a path part and a fragment (<c>#…</c>); a pure fragment
/// (<c>#foo</c>) is left unchanged;</item>
/// <item>the path part is resolved to the target's <em>old</em> bookpath relative to the directory of the
/// <em>old</em> location of the source file;</item>
/// <item>the target's old bookpath is mapped through the <c>updates</c> map (if the target did not
/// move — it stays the same, but we recalculate anyway because the <em>source
/// file</em> may have moved);</item>
/// <item>the new value = a relative path from the <em>new</em> bookpath of the source file to
/// the target's new bookpath, URL-encoded, with the (URL-encoded) fragment re-attached.</item>
/// </list>
/// </remarks>
public static class HrefUpdate
{
    /// <summary>
    /// Recalculates the value of a single reference. Returns <paramref name="value"/> unchanged if
    /// the reference is external, is a pure fragment or cannot be resolved.
    /// </summary>
    /// <param name="value">The raw (URL-encoded) reference value from the source file.</param>
    /// <param name="updates">A map: old bookpath → new bookpath for the moved resources.</param>
    /// <param name="oldSourceBookPath">The bookpath of the source file <em>before</em> the operation.</param>
    /// <param name="newSourceBookPath">The bookpath of the source file <em>after</em> the operation.</param>
    public static string UpdateValue(
        string value,
        IReadOnlyDictionary<string, string> updates,
        string oldSourceBookPath,
        string newSourceBookPath)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(updates);

        if (value.Contains(':', StringComparison.Ordinal))
        {
            return value;
        }

        int hashIndex = value.IndexOf('#', StringComparison.Ordinal);
        bool hasFragment = hashIndex >= 0;
        string rawPath = hasFragment ? value[..hashIndex] : value;
        string rawFragment = hasFragment ? value[(hashIndex + 1)..] : string.Empty;

        if (rawPath.Length == 0 && hasFragment)
        {
            // A pure reference within the same file (#id) — needs no change.
            return value;
        }

        string oldSourceDir = CoreBookPath.StartingDir(oldSourceBookPath);
        string oldTarget = rawPath.Length == 0
            ? oldSourceBookPath
            : CoreBookPath.BuildBookPath(Utility.UrlDecodePath(rawPath), oldSourceDir);

        string newTarget = updates.TryGetValue(oldTarget, out string? mapped) ? mapped : oldTarget;
        if (newTarget.Length == 0 || newSourceBookPath.Length == 0)
        {
            return value;
        }

        string newHref = Utility.UrlEncodePath(CoreBookPath.Relative(newSourceBookPath, newTarget));
        if (newHref.Length == 0)
        {
            int slash = newTarget.LastIndexOf('/');
            newHref = slash >= 0 ? newTarget[(slash + 1)..] : newTarget;
        }

        if (hasFragment)
        {
            newHref += "#" + Utility.UrlEncodePath(Utility.UrlDecodePath(rawFragment));
        }

        return newHref;
    }
}
