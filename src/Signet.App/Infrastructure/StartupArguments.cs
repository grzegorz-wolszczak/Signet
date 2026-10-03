using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;

namespace Signet.App.Infrastructure;

/// <summary>
/// Command-line arguments of the application (parser: System.CommandLine, as in <c>Signet.Cli</c>).
/// The first argument is the path of a file to open at startup instead of the most recently used
/// publication, e.g. <c>Signet.App.exe book.epub</c> or "Open with" in the file explorer.
/// </summary>
public static class StartupArguments
{
    /// <summary>
    /// Full path of the file from the argument, or <c>null</c> when none was given. A relative path is
    /// resolved against <paramref name="currentDirectory"/>. Further arguments are ignored.
    /// The file's existence is checked only when opening it (with a message for the user).
    /// </summary>
    public static string? FileToOpen(IReadOnlyList<string>? args, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(currentDirectory);
        if (args is null || args.Count == 0)
        {
            return null;
        }

        Argument<string?> file = new("file")
        {
            Description = "File to open (EPUB, HTML, …).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        RootCommand root = new("Signet — EPUB ebook editor") { file };
        root.TreatUnmatchedTokensAsErrors = false;

        // No response files (@file): a file name starting with "@" is a plain path.
        ParseResult result = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
        string? value = result.GetValue(file);
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value, currentDirectory);
    }
}
