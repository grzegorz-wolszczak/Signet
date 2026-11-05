using System.IO;

namespace Signet.EpubCheck.Cli;

/// <summary>
/// Where the tool writes (upstream's <c>outWriter</c> and <c>System.err</c>): standard output, which <c>--quiet</c> silences
/// for the lines the tool prints itself, and standard error.
/// </summary>
internal sealed class ToolOutput
{
    public ToolOutput(TextWriter output, TextWriter error)
    {
        Out = output;
        Error = error;
    }

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    public bool Quiet { get; set; }

    /// <summary>Prints a line on standard output unless the tool is quiet.</summary>
    public void Println(string text)
    {
        if (!Quiet)
        {
            Out.Write(NormalizeLineEndings(text));
            Out.Write('\n');
        }
    }

    /// <summary>Prints a line on standard output even when the tool is quiet.</summary>
    public void PrintlnAlways(string text)
    {
        Out.Write(NormalizeLineEndings(text));
        Out.Write('\n');
    }

    /// <summary>Prints a line on standard error.</summary>
    public void PrintError(string text)
    {
        Error.Write(NormalizeLineEndings(text));
        Error.Write('\n');
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n", System.StringComparison.Ordinal);
}
