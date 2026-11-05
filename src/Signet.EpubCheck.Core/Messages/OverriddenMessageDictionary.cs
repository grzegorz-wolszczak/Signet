using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Messages;

/// <summary>
/// The messages with the severities, texts and suggestions that a custom messages file overrides (<c>--customMessages</c>;
/// upstream's <c>OverriddenMessageDictionary</c> with <c>OverriddenMessages</c>). The file is UTF-8, one message per
/// line, the fields separated by a tab: <c>ID</c>, <c>Severity</c>, optionally <c>Message</c> and <c>Suggestion</c>; the
/// first line is skipped when it starts with <c>id</c> (the header <c>--listChecks</c> writes). The problems of the file
/// are reported as <c>CHK-001</c>–<c>CHK-007</c> to the report given.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Keeps the name of the upstream type.")]
public sealed class OverriddenMessageDictionary : IMessageDictionary
{
    private readonly LocalizedMessages _defaults;
    private readonly Dictionary<MessageId, Message> _overridden = [];
    private readonly string _path;
    private readonly IReport _report;

    /// <summary>Reads <paramref name="overrideFile"/> (problems go to <paramref name="report"/>).</summary>
    /// <param name="overrideFile">The custom messages file.</param>
    /// <param name="report">Where the problems of the file are reported.</param>
    /// <param name="culture">The language of the texts that are not overridden (<see cref="CultureInfo.CurrentUICulture"/> when <c>null</c>).</param>
    public OverriddenMessageDictionary(string overrideFile, IReport report, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(overrideFile);
        ArgumentNullException.ThrowIfNull(report);
        _defaults = LocalizedMessages.GetInstance(culture);
        _report = report;
        _path = PathUtil.RemoveWorkingDirectory(Path.GetFullPath(overrideFile));
        Load(overrideFile);
    }

    /// <inheritdoc />
    public Message GetMessage(MessageId id) => _overridden.TryGetValue(id, out Message? message) ? message : _defaults.GetMessage(id);

    /// <inheritdoc />
    public string Format(Message message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _defaults.Format(message, args);
    }

    private void Load(string overrideFile)
    {
        int lineNumber = -1;
        int columnNumber = -1;
        try
        {
            using StreamReader reader = new(overrideFile, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
            lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if (lineNumber == 1 && line.StartsWith("id", StringComparison.OrdinalIgnoreCase))
                {
                    // The header that --listChecks writes.
                    continue;
                }

                columnNumber = 0;
                string[] fields = SplitFields(line);
                if (fields.Length >= 2)
                {
                    if (!MessageIds.TryParse(fields[0], out MessageId id))
                    {
                        _report.Message(MessageId.CHK_002, At(lineNumber, 0), fields[0], _path);
                        continue;
                    }

                    columnNumber += 1 + fields[0].Length;
                    if (!Severities.TryParse(fields[1], out Severity newSeverity))
                    {
                        _report.Message(MessageId.CHK_003, At(lineNumber, columnNumber), fields[1], _path);
                        continue;
                    }

                    Message message = _defaults.GetMessage(id);
                    string? messageText = message.Text;
                    if (fields.Length >= 3 && fields[2].Length > 0)
                    {
                        columnNumber += 1 + fields[1].Length;
                        messageText = CheckParameterCount(lineNumber, columnNumber, message.Text, fields[2]);
                        if (messageText is null)
                        {
                            _report.Message(MessageId.CHK_004, At(lineNumber, 0).WithContext(fields[2]), _path);
                            continue;
                        }
                    }

                    Severity oldSeverity = DefaultSeverities.Get(message.Id);
                    if (newSeverity != oldSeverity)
                    {
                        // A message without a text is described by the port's fallback, so that the override does not leave it blank.
                        string described = messageText.Length == 0 ? _defaults.NoDescription(id) : messageText;
                        messageText = $" (severity overridden from {oldSeverity.ToText()}) {described}";
                    }

                    string? suggestionText = message.Suggestion;
                    if (fields.Length >= 4 && fields[3].Length > 0)
                    {
                        columnNumber += 1 + fields[1].Length;
                        suggestionText = CheckParameterCount(lineNumber, columnNumber, message.Suggestion, fields[3]);
                        if (suggestionText is null)
                        {
                            _report.Message(MessageId.CHK_005, At(lineNumber, 0).WithContext(fields[3]), _path);
                            continue;
                        }
                    }

                    if (newSeverity != message.Severity
                        || !string.Equals(messageText, message.Text, StringComparison.Ordinal)
                        || !string.Equals(suggestionText, message.Suggestion, StringComparison.Ordinal))
                    {
                        _overridden[id] = new Message(message.Id, newSeverity, message.Severity, messageText, suggestionText);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            _report.Message(MessageId.CHK_001, new EpubLocation(_path));
        }
        catch (IOException ex)
        {
            _report.Message(MessageId.CHK_007, At(lineNumber, columnNumber), _path, ex.Message);
        }
    }

    // Java's String.split("\t"): the trailing empty fields are dropped (an empty line gives one empty field).
    private static string[] SplitFields(string line)
    {
        List<string> fields = [.. line.Split('\t')];
        while (fields.Count > 1 && fields[^1].Length == 0)
        {
            fields.RemoveAt(fields.Count - 1);
        }

        return [.. fields];
    }

    private EpubLocation At(int line, int column) => new EpubLocation(_path).At(line, column);

    // The new text may use no more positional arguments than the original; otherwise null.
    private string? CheckParameterCount(int lineNumber, int columnNumber, string original, string replacement)
    {
        int maxOriginal = HighestArgument(lineNumber, columnNumber, original);
        int maxNew = HighestArgument(lineNumber, columnNumber, replacement);
        return maxNew <= maxOriginal ? replacement : null;
    }

    private int HighestArgument(int lineNumber, int columnNumber, string text)
    {
        int max = 0;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"%(\d+)\$s"))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                max = Math.Max(max, number);
            }
            else
            {
                _report.Message(MessageId.CHK_006, At(lineNumber, columnNumber + match.Index).WithContext(text), _path);
            }
        }

        return max;
    }
}
