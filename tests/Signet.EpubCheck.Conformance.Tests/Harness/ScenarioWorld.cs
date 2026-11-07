using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>
/// The state one scenario builds up while its steps run: the environment and configuration of the checker, the report,
/// and what the command line printed. (Upstream spreads this over <c>TestEnvironment</c>, <c>EPUBCheckConfiguration</c> and
/// <c>CLISteps</c>.)
/// </summary>
internal sealed class ScenarioWorld
{
    public ScenarioWorld(string corpusRoot, IEpubChecker checker)
    {
        CorpusRoot = corpusRoot;
        Checker = checker;
    }

    public string CorpusRoot { get; }

    public IEpubChecker Checker { get; }

    // TestEnvironment
    public string BasePath { get; set; } = string.Empty;

    public CultureInfo DefaultCulture { get; set; } = CultureInfo.GetCultureInfo("en");

    // EPUBCheckConfiguration
    public EpubVersion Version { get; set; } = EpubVersion.Version3;

    public CheckerMode? Mode { get; set; }

    public EpubProfile Profile { get; set; } = EpubProfile.Default;

    public ScenarioReport Report { get; } = new();

    // ReportSteps: what the report writer wrote, and the namespace the XML report's elements are in
    public string? ReportText { get; set; }

    public string? XmlNamespace { get; set; }

    // CLISteps: the tool of the last "running" step
    public Signet.EpubCheck.Cli.ToolRun? Tool { get; set; }

    // AssertionSteps
    public MessageInfo? LastAssertedMessage { get; set; }

    // URL fragment steps
    public EpubUrlFragment? LastFragment { get; set; }

    /// <summary>The viewport the last "parsing viewport" step parsed, and the errors it reported.</summary>
    public ViewportMeta? LastViewport { get; set; }

    public List<ViewportMetaParseError> ViewportErrors { get; } = [];

    // CLISteps
    public StringWriter StdOut { get; } = new();

    public StringWriter StdErr { get; } = new();

    public bool ErrorRedirectedToOutput { get; set; }

    public int ExitCode { get; set; }

    /// <summary>Files the scenario made that the scenario's end removes.</summary>
    public List<string> Deletes { get; } = [];

    /// <summary>The full path of a file or folder of the test files, from the base path and the path a step gives.</summary>
    public string ResolvePath(string path) =>
        Path.GetFullPath(Path.Combine(CorpusRoot, BasePath.TrimStart('/'), path.TrimStart('/')));

    /// <summary>The text printed so far on <c>stdout</c> or <c>stderr</c>.</summary>
    public string ReadStream(string name) => name switch
    {
        "stdout" => StdOut.ToString(),
        "stderr" => StdErr.ToString(),
        _ => throw new System.ArgumentException("Unknown output stream: " + name),
    };
}
