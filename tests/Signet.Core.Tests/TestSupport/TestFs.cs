using System.IO;

namespace Signet.Core.Tests.TestSupport;

/// <summary>Small file helpers for tests.</summary>
public static class TestFs
{
    /// <summary>
    /// Recursively copies the directory <paramref name="sourceDir"/> to <paramref name="destinationDir"/>
    /// (created if needed). Returns <paramref name="destinationDir"/>.
    /// </summary>
    public static string CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        return destinationDir;
    }
}
