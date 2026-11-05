using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Messages;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>A kind of value a step expression can name with <c>{name}</c> (Cucumber's "parameter type").</summary>
/// <param name="Name">The name used in the expressions.</param>
/// <param name="Pattern">The regular expression the text of the value matches. Upstream's start with <c>?i:</c> for a case-insensitive match.</param>
/// <param name="Convert">Makes the value from the text that matched.</param>
internal sealed record ParameterType(string Name, string Pattern, Func<string, object?> Convert);

/// <summary>The parameter types of upstream's step definitions (<c>ParameterTypes.java</c>, <c>CLISteps</c>, …) and Cucumber's built-in ones.</summary>
internal sealed class ParameterTypeRegistry
{
    private readonly Dictionary<string, ParameterType> _types = new(StringComparer.Ordinal);

    public ParameterTypeRegistry()
    {
        // Cucumber's own.
        Add("int", @"-?\d+", s => int.Parse(s, CultureInfo.InvariantCulture));
        Add("float", @"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?", s => double.Parse(s, CultureInfo.InvariantCulture));
        Add("word", @"[^\s]+", s => s);
        Add("string", @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'", Unquote);
        Add(string.Empty, ".*", s => s);

        // Upstream's (ParameterTypes.java).
        Add("bool", "true|false", s => s == "true");
        Add("checkerMode", "?i:(full publication|((Media Overlays|Navigation|Package|SVG Content|XHTML Content) Document))", s => ToCheckerMode(s));
        Add("locale", ".*?", ToCulture);
        Add("messageId", "[A-Z]{3}-[0-9]{3}[a-z]?", s => ToMessageId(s));
        Add("messageQuantity", @"(?: )?(\d*)(?: time(?:s)?)?", s => Regex.Match(s, @"\d+") is { Success: true } m ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 1);
        Add("profile", ".*?", s => ToProfile(s));
        Add("severity", "?i:(fatal error|error|warning|usage|info)", s => ToSeverity(s));
        Add("version", @"\d(?:\.\d)?(?:\.\d)?", s => ToVersion(s));

        // The other steps' (CLISteps, FilenameCheckerSteps, ViewportSteps, SharedParameterTypes).
        Add("outputStream", "stdout|stderr", s => s);
        Add("codepoint", @"U\+[0-9a-zA-Z]{4,6}", s => s);
        Add("error", ".*", s => s);
        Add("multimap", "\".*?\"", s => s);
    }

    public ParameterType Get(string name) =>
        _types.TryGetValue(name, out ParameterType? type) ? type : throw new StepBindingException($"Undefined parameter type {{{name}}}.");

    private void Add(string name, string pattern, Func<string, object?> convert) => _types.Add(name, new ParameterType(name, pattern, convert));

    private static string Unquote(string quoted)
    {
        string inner = quoted[1..^1];
        return inner.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\'", "'", StringComparison.Ordinal);
    }

    private static CheckerMode ToCheckerMode(string text) => text.ToLowerInvariant() switch
    {
        "full publication" => CheckerMode.Epub,
        "media overlays document" => CheckerMode.MediaOverlaysDocument,
        "navigation document" => CheckerMode.NavigationDocument,
        "package document" => CheckerMode.PackageDocument,
        "svg content document" => CheckerMode.SvgContentDocument,
        "xhtml content document" => CheckerMode.XhtmlContentDocument,
        _ => throw new ArgumentException("Unknown file type: " + text),
    };

    private static CultureInfo ToCulture(string tag) => CultureInfo.GetCultureInfo(tag);

    private static MessageId ToMessageId(string text) =>
        Enum.TryParse(text.Replace('-', '_'), out MessageId id) && Enum.IsDefined(id)
            ? id
            : throw new ArgumentException("Unknown message id: " + text);

    private static EpubProfile ToProfile(string name) =>
        EpubProfiles.TryParse(name, out EpubProfile profile) ? profile : throw new ArgumentException("Unknown EPUBCheck profile: " + name);

    private static Severity ToSeverity(string text) => text.ToLowerInvariant() switch
    {
        "fatal error" or "fatal" => Severity.Fatal,
        "error" => Severity.Error,
        "warning" => Severity.Warning,
        "usage" => Severity.Usage,
        "info" => Severity.Info,
        _ => throw new ArgumentException("Unknown severity: " + text),
    };

    private static EpubVersion ToVersion(string text) =>
        text == "3" || text.StartsWith("3.", StringComparison.Ordinal) ? EpubVersion.Version3
        : text == "2" || text.StartsWith("2.", StringComparison.Ordinal) ? EpubVersion.Version2
        : EpubVersion.Unknown;
}

/// <summary>What kind of thing a "checking …" step checks (after upstream's <c>CheckerMode</c>).</summary>
internal enum CheckerMode
{
    Epub,
    MediaOverlaysDocument,
    NavigationDocument,
    PackageDocument,
    SvgContentDocument,
    XhtmlContentDocument,
}
