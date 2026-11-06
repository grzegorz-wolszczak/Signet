using System;
using System.Globalization;
using System.IO;
using System.Text;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Cli;

/// <summary>
/// One run of the tool: reads the command line and does what it asks (upstream's <c>EpubChecker</c>). The options, their
/// spellings, the texts and the exit codes are upstream's.
/// </summary>
/// <remarks>
/// The checks themselves are not there yet (<c>EPUBCHECK_PLAN.md</c>): a run that has a file to check says so and fails.
/// Everything around them works - the help, the version, <c>--listChecks</c>, <c>--customMessages</c>, <c>--locale</c>, the
/// reporting levels, the argument errors and the closing counters.
/// </remarks>
internal sealed class ToolRun
{
    private const string CustomMessageFileVariable = "ePubCheckCustomMessageFile";

    private readonly ToolOutput _output;
    private readonly CultureInfo _defaultCulture;
    private ToolMessages _messages;
    private CultureInfo _locale;

    private string? _path;
    private string? _mode;
    private EpubProfile? _profile;
    private EpubVersion _version = EpubVersion.Version3;
    private bool _expanded;
    private bool _jsonOutput;
    private bool _xmlOutput;
    private bool _xmpOutput;
    private string? _listChecksOut;
    private string? _customMessageFile;
    private bool _listChecks;
    private bool _displayHelp;
    private bool _displayVersion;
    private bool _useCustomMessageFile;
    private Severity _reportingLevel = Severity.Info;
    private ConsoleReport? _report;

    public ToolRun(ToolOutput output, CultureInfo defaultCulture)
    {
        _output = output;
        _defaultCulture = defaultCulture;
        _locale = defaultCulture;
        _messages = ToolMessages.GetInstance(defaultCulture);
    }

    /// <summary>Whether warnings make the exit code 1 as errors do (<c>--failonwarnings</c>).</summary>
    public bool FailOnWarnings { get; private set; }

    /// <summary>Whether the EPUB made from an expanded one is kept (<c>--save</c>).</summary>
    public bool Keep { get; private set; }

    /// <summary>The maximum of messages of one id and text a report keeps (<c>--maxOfEachMessage</c>); negative means no limit.</summary>
    public int MaxOfEachMessage { get; private set; } = IReport.DefaultMaxOfEachMessage;

    /// <summary>Runs the tool; returns its exit code.</summary>
    public int Run(string[] args)
    {
        int returnValue = 1;
        try
        {
            if (ProcessArguments(args))
            {
                if (_displayHelp || (_displayVersion && _path is null))
                {
                    return 0;
                }

                if (_jsonOutput || _xmlOutput || _xmpOutput)
                {
                    string format = _xmlOutput ? "XML" : _xmpOutput ? "XMP" : "JSON";
                    _output.PrintError(_messages.GetPort("report_format_not_implemented", format));
                }
                else
                {
                    _report = CreateReport();
                    _report.Initialize();
                    if (_listChecks)
                    {
                        DumpMessageDictionary(_report);
                        return 0;
                    }

                    if (_useCustomMessageFile)
                    {
                        _report.CustomMessageFile = Path.GetFullPath(_customMessageFile!);
                    }

                    returnValue = ProcessFile(_report);
                    int generated = _report.Generate();
                    if (returnValue == 0)
                    {
                        returnValue = generated;
                    }
                }
            }
        }
        catch (ToolArgumentException ex)
        {
            // Upstream throws these and then swallows them, so that the reason is never shown; the port shows it.
            _output.PrintError(ex.Message);
            returnValue = 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _output.PrintError(ex.Message);
            returnValue = 1;
        }

        if (_report is not null)
        {
            PrintCompleted(_report);
        }
        else
        {
            _output.Println(_messages.Get("epubcheck_completed"));
        }

        return returnValue;
    }

    private int ProcessFile(ConsoleReport report)
    {
        report.Info(null, FeatureKind.ToolName, "epubcheck");
        report.Info(null, FeatureKind.ToolVersion, EpubCheckVersion.Text);
        report.Info(null, FeatureKind.ToolDate, EpubCheckVersion.BuildDate);

        if (_expanded)
        {
            if (!File.Exists(_path) && !Directory.Exists(_path))
            {
                _output.PrintError(_messages.Get("directory_not_found", _path));
                return 1;
            }

            _output.PrintError(_messages.GetPort("expanded_not_implemented"));
            return 1;
        }

        if (_mode is not null)
        {
            report.Info(null, FeatureKind.ExecMode, _messages.Get("single_file", _mode, VersionText(_version), (_profile ?? EpubProfile.Default).ToText()));
        }

        return ValidateFile(_path!, report);
    }

    private int ValidateFile(string path, ConsoleReport report)
    {
        if (path.StartsWith("http://", StringComparison.Ordinal) || path.StartsWith("https://", StringComparison.Ordinal))
        {
            _output.PrintError(_messages.GetPort("remote_not_supported", path));
            return 1;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _output.PrintError(_messages.Get("file_not_found", path));
            return 1;
        }

        if (!IsSupportedMode(_mode, _version))
        {
            _output.Println(_messages.Get("display_help"));
            throw new ToolArgumentException(_messages.Get("mode_version_not_supported", _mode, VersionText(_version)));
        }

        if (_mode is not null)
        {
            _output.PrintError(_messages.GetPort("checker_not_implemented"));
            return 1;
        }

        OCFChecker.Check(path, _version, report);
        return Result(report);
    }

    // Upstream's result of a check: the closing sentence and the exit code (warnings fail only with --failonwarnings).
    private int Result(ConsoleReport report)
    {
        if (report.ErrorCount == 0 && report.FatalErrorCount == 0)
        {
            if (report.WarningCount == 0)
            {
                _output.Println(_messages.Get("no_errors__or_warnings"));
                return 0;
            }

            _output.PrintError(_messages.Get("there_were_warnings"));
            return FailOnWarnings ? 1 : 0;
        }

        _output.PrintError(_messages.Get("there_were_errors"));
        return 1;
    }

    // The modes upstream has a checker for: none (a whole EPUB), opf, xhtml, svg (EPUB 2 and 3), mo and nav (EPUB 3 only).
    private static bool IsSupportedMode(string? mode, EpubVersion version) => mode switch
    {
        null => true,
        "opf" or "xhtml" or "svg" => true,
        "mo" or "nav" => version == EpubVersion.Version3,
        _ => false,
    };

    private static string VersionText(EpubVersion version) => version switch
    {
        EpubVersion.Version2 => "2.0.1",
        EpubVersion.Version3 => "3.4",
        _ => "unknown",
    };

    private void PrintCompleted(ConsoleReport report)
    {
        StringBuilder counts = new();
        if (_reportingLevel <= Severity.Fatal)
        {
            counts.Append(_messages.Get("messages")).Append(": ").Append(Counter("fatal", report.FatalErrorCount));
        }

        if (_reportingLevel <= Severity.Error)
        {
            counts.Append(" / ").Append(Counter("error", report.ErrorCount));
        }

        if (_reportingLevel <= Severity.Warning)
        {
            counts.Append(" / ").Append(Counter("warn", report.WarningCount));
        }

        if (_reportingLevel <= Severity.Info)
        {
            counts.Append(" / ").Append(Counter("info", report.InfoCount));
        }

        if (_reportingLevel <= Severity.Usage)
        {
            counts.Append(" / ").Append(Counter("usage", report.UsageCount));
        }

        if (counts.Length > 0)
        {
            counts.Append('\n');
            _output.Println(counts.ToString());
        }

        _output.Println(_messages.Get("epubcheck_completed"));
    }

    private string Counter(string kind, int count) =>
        _messages.Get($"counter_{kind}_{(count == 0 ? "zero" : count == 1 ? "one" : "many")}", count);

    private void DumpMessageDictionary(ConsoleReport report)
    {
        StreamWriter? file = null;
        try
        {
            if (_listChecksOut is not null)
            {
                file = new StreamWriter(_listChecksOut, append: false, new UTF8Encoding(false));
            }

            new MessageDictionaryDumper(report.Dictionary).Dump(file ?? _output.Out);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_listChecksOut is not null)
            {
                _output.PrintError(_messages.Get("error_creating_config_file", Path.GetFullPath(_listChecksOut)));
            }

            _output.PrintError(ex.Message);
        }
        finally
        {
            file?.Dispose();
        }
    }

    private ConsoleReport CreateReport()
    {
        ConsoleReport report = new(_listChecks ? "none" : _path ?? "none", _output, _messages)
        {
            MaxOfEachMessage = MaxOfEachMessage,
            ReportingLevel = _reportingLevel,
        };
        report.SetLocale(_locale);
        if (_useCustomMessageFile)
        {
            report.SetOverrideFile(_customMessageFile!, _locale);
        }

        return report;
    }

    private bool ProcessArguments(string[] args)
    {
        if (args.Length < 1)
        {
            _output.PrintError(_messages.Get("argument_needed"));
            return false;
        }

        SetCustomMessageFileFromEnvironment();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith('-'))
            {
                if (_path is null)
                {
                    _path = arg;
                    continue;
                }

                return Unrecognized(arg);
            }

            // "--?(.*)": one or two dashes, then the name.
            switch (arg.StartsWith("--", StringComparison.Ordinal) ? arg[2..] : arg[1..])
            {
                case "v":
                    if (i + 1 >= args.Length)
                    {
                        _output.Println(_messages.Get("display_help"));
                        throw new ToolArgumentException(_messages.Get("version_argument_expected"));
                    }

                    string version = args[++i];
                    _version = version is "2.0" or "2" ? EpubVersion.Version2
                        : version is "3.0" or "3" ? EpubVersion.Version3
                        : throw UnsupportedVersion();
                    break;
                case "m" or "mode":
                    if (i + 1 >= args.Length)
                    {
                        _output.Println(_messages.Get("display_help"));
                        throw new ToolArgumentException(_messages.Get("mode_argument_expected"));
                    }

                    _mode = args[++i];
                    _expanded = _mode == "exp";
                    break;
                case "p" or "profile":
                    if (i + 1 >= args.Length)
                    {
                        _output.Println(_messages.Get("display_help"));
                        throw new ToolArgumentException(_messages.Get("profile_argument_expected"));
                    }

                    string profileName = args[++i];
                    if (EpubProfiles.TryParse(profileName, out EpubProfile profile))
                    {
                        _profile = profile;
                    }
                    else
                    {
                        // Upstream prints the text about ignored modes here, which has nothing to say about a profile.
                        _output.PrintError(_messages.Get("profile_unknown", profileName));
                        _profile = EpubProfile.Default;
                    }

                    break;
                case "s" or "save":
                    Keep = true;
                    break;
                case "o" or "out":
                    _xmlOutput = true;
                    ReadReportFile(args, ref i);
                    break;
                case "j" or "json":
                    _jsonOutput = true;
                    ReadReportFile(args, ref i);
                    break;
                case "x" or "xmp":
                    _xmpOutput = true;
                    ReadReportFile(args, ref i);
                    break;
                case "i" or "info":
                    _reportingLevel = Severity.Info;
                    break;
                case "f" or "fatal":
                    _reportingLevel = Severity.Fatal;
                    break;
                case "e" or "error":
                    _reportingLevel = Severity.Error;
                    break;
                case "w" or "warn":
                    _reportingLevel = Severity.Warning;
                    break;
                case "u" or "usage":
                    _reportingLevel = Severity.Usage;
                    break;
                case "q" or "quiet":
                    _output.Quiet = true;
                    break;
                case "failonwarnings":
                    FailOnWarnings = true;
                    break;
                case "r" or "redir":
                    if (i + 1 < args.Length)
                    {
                        i++;
                    }

                    break;
                case "c" or "customMessages":
                    if (i + 1 < args.Length)
                    {
                        string fileName = args[i + 1];
                        if (string.Equals(fileName, "none", StringComparison.OrdinalIgnoreCase))
                        {
                            _customMessageFile = null;
                            _useCustomMessageFile = false;
                            i++;
                        }
                        else if (!fileName.StartsWith('-'))
                        {
                            _customMessageFile = fileName;
                            _useCustomMessageFile = true;
                            i++;
                        }
                        else
                        {
                            _output.PrintError(_messages.Get("expected_message_filename", fileName));
                            DisplayHelp();
                            return false;
                        }
                    }

                    break;
                case "l" or "listChecks":
                    if (i + 1 < args.Length)
                    {
                        _listChecksOut = !args[i + 1].StartsWith('-') ? args[++i] : null;
                    }

                    _listChecks = true;
                    break;
                case "locale":
                    if (i + 1 >= args.Length)
                    {
                        _output.PrintError(_messages.Get("missing_locale"));
                        DisplayHelp();
                        return false;
                    }

                    if (args[i + 1].StartsWith('-'))
                    {
                        _output.PrintError(_messages.Get("incorrect_locale", args[i + 1]));
                        DisplayHelp();
                        return false;
                    }

                    SetLocale(args[++i]);
                    break;
                case "maxOfEachMessage" or "maxofeachmessage":
                    if (i + 1 >= args.Length)
                    {
                        _output.PrintError(_messages.Get("missing_maxofeach"));
                        DisplayHelp();
                        return false;
                    }

                    string maxValue = args[++i];
                    if (maxValue == "unlimited")
                    {
                        MaxOfEachMessage = -1;
                    }
                    else if (int.TryParse(maxValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int max))
                    {
                        MaxOfEachMessage = max;
                    }
                    else
                    {
                        _output.PrintError(_messages.Get("incorrect_maxofeach", maxValue));
                        DisplayHelp();
                        return false;
                    }

                    break;
                case "h" or "?" or "help":
                    DisplayHelp();
                    _displayHelp = true;
                    break;
                case "version":
                    _output.Println(_messages.Get("epubcheck_version_text", EpubCheckVersion.Text));
                    _displayVersion = true;
                    break;
                default:
                    return Unrecognized(arg);
            }
        }

        if ((_xmlOutput && _xmpOutput) || (_xmlOutput && _jsonOutput) || (_xmpOutput && _jsonOutput))
        {
            _output.PrintError(_messages.Get("output_type_conflict"));
            return false;
        }

        if (_path is not null)
        {
            _path = _path.Replace('\\', '/');
        }

        if (_path is null)
        {
            if (_listChecks || _displayHelp || _displayVersion)
            {
                return true;
            }

            _output.PrintError(_messages.Get("no_file_specified"));
            return false;
        }

        if (IsEpubFileName(_path))
        {
            if (_mode is not null || _version != EpubVersion.Version3)
            {
                _output.PrintError(_messages.Get("mode_version_ignored"));
                _mode = null;
            }
        }
        else if (_mode is null && _profile is null)
        {
            _output.Println(_messages.Get("mode_required"));
            return false;
        }

        return true;
    }

    // ".+\.[Ee][Pp][Uu][Bb]": a name with something before a ".epub" in any letter case.
    private static bool IsEpubFileName(string path) =>
        path.Length > 5 && path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);

    private bool Unrecognized(string arg)
    {
        _output.PrintError(_messages.Get("unrecognized_argument", arg));
        DisplayHelp();
        return false;
    }

    private ToolArgumentException UnsupportedVersion()
    {
        _output.Println(_messages.Get("display_help"));
        return new ToolArgumentException("Version not supported");
    }

    // -o, -j, -x: a file name, or "-" for the console, or nothing (a name made from the file to check, which needs the file to be named before).
    private void ReadReportFile(string[] args, ref int i)
    {
        if (args.Length > i + 1 && !args[i + 1].StartsWith('-'))
        {
            i++;
        }
        else if (args.Length > i + 1 && string.Equals(args[i + 1], "-", StringComparison.OrdinalIgnoreCase))
        {
            i++;
        }
        else if (_path is null)
        {
            throw new ToolArgumentException(_messages.GetPort("path_needed_for_report"));
        }
    }

    private void SetLocale(string tag)
    {
        CultureInfo requested;
        try
        {
            requested = CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            requested = _defaultCulture;
        }

        // A language without texts falls back to the default locale of the user, and to English when that has none either.
        _locale = ToolMessages.IsSupported(requested) ? requested : _defaultCulture;
        _messages = ToolMessages.GetInstance(_locale);
    }

    private void SetCustomMessageFileFromEnvironment()
    {
        string? name = Environment.GetEnvironmentVariable(CustomMessageFileVariable);
        if (!string.IsNullOrEmpty(name) && File.Exists(name))
        {
            _customMessageFile = name;
            _useCustomMessageFile = true;
        }
    }

    private void DisplayHelp() => _output.Println(_messages.Get("help_text", EpubCheckVersion.Text));

    private sealed class ToolArgumentException : Exception
    {
        public ToolArgumentException(string message)
            : base(message)
        {
        }
    }
}
