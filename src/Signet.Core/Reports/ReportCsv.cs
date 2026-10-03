using System;
using System.Collections.Generic;
using System.Text;

namespace Signet.Core.Reports;

/// <summary>
/// CSV writing for report export. A value is quoted whenever it contains a comma,
/// a quote or a newline character, per RFC 4180.
/// </summary>
public static class ReportCsv
{
    private static readonly System.Buffers.SearchValues<char> QuoteTriggers =
        System.Buffers.SearchValues.Create(",\"\n\r");

    /// <summary>Builds a single CSV line (without a trailing newline) from the given fields.</summary>
    public static string WriteLine(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        StringBuilder line = new();
        bool first = true;
        foreach (string field in fields)
        {
            if (!first)
            {
                line.Append(',');
            }

            first = false;
            line.Append(QuoteIfNeeded(field));
        }

        return line.ToString();
    }

    private static string QuoteIfNeeded(string value)
    {
        bool needsQuotes = value.AsSpan().IndexOfAny(QuoteTriggers) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
