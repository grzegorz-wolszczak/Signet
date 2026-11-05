using System;
using System.IO;

namespace Signet.EpubCheck.Core.Util;

/// <summary>Path helpers shared by the reports (upstream's <c>PathUtil</c>).</summary>
public static class PathUtil
{
    /// <summary>
    /// Shortens a path under the working directory to <c>./relative/path</c>, as upstream does for the paths it prints;
    /// any other path (and the empty one) is returned as it is.
    /// </summary>
    public static string RemoveWorkingDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string workingDirectory = Directory.GetCurrentDirectory();

        // Like upstream, a plain prefix test: "/work" also matches "/workshop/file".
        if (path.Length == 0 || workingDirectory == "/" || !path.StartsWith(workingDirectory, StringComparison.Ordinal))
        {
            return path;
        }

        return "." + path[workingDirectory.Length..];
    }
}
