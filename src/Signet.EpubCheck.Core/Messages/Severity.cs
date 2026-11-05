using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>
/// The severity of a message. The numeric order is the order of upstream's reporting levels: a message is reported
/// when its severity is at least the reporting level of the report.
/// </summary>
public enum Severity
{
    /// <summary>Never reported (the message is switched off).</summary>
    Suppressed = 0,

    /// <summary>Information about the use of features of EPUB (reported only on request).</summary>
    Usage = 1,

    /// <summary>Information.</summary>
    Info = 2,

    /// <summary>A recommendation of the specification is not followed.</summary>
    Warning = 3,

    /// <summary>A requirement of the specification is not met.</summary>
    Error = 4,

    /// <summary>The check cannot go on.</summary>
    Fatal = 5,
}

/// <summary>The text forms of <see cref="Severity"/>, as upstream prints and parses them (<c>ERROR</c>, <c>WARNING</c>, …).</summary>
public static class Severities
{
    private static readonly Dictionary<string, Severity> s_byText = BuildIndex();

    /// <summary>The upstream text form: the upper-case name (<c>SUPPRESSED</c>, <c>USAGE</c>, <c>INFO</c>, <c>WARNING</c>, <c>ERROR</c>, <c>FATAL</c>).</summary>
    public static string ToText(this Severity severity) => severity switch
    {
        Severity.Suppressed => "SUPPRESSED",
        Severity.Usage => "USAGE",
        Severity.Info => "INFO",
        Severity.Warning => "WARNING",
        Severity.Error => "ERROR",
        Severity.Fatal => "FATAL",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
    };

    /// <summary>Parses the upstream text form (exact, upper case); <c>false</c> when the text names no severity.</summary>
    public static bool TryParse(string? text, out Severity severity)
    {
        severity = default;
        return text is not null && s_byText.TryGetValue(text, out severity);
    }

    private static Dictionary<string, Severity> BuildIndex()
    {
        Dictionary<string, Severity> index = new(StringComparer.Ordinal);
        foreach (Severity severity in Enum.GetValues<Severity>())
        {
            index.Add(severity.ToText(), severity);
        }

        return index;
    }
}
