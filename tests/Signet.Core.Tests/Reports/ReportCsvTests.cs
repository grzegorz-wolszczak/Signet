using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.Reports;
using Xunit;

namespace Signet.Core.Tests.Reports;

/// <summary>Tests for <see cref="ReportCsv"/>.</summary>
public sealed class ReportCsvTests
{
    [Fact]
    public void WriteLine_JoinsPlainFieldsWithComma()
    {
        ReportCsv.WriteLine(new List<string> { "chapter1.xhtml", "12", "Yes" }).Should().Be("chapter1.xhtml,12,Yes");
    }

    [Fact]
    public void WriteLine_QuotesFieldsContainingComma()
    {
        ReportCsv.WriteLine(new List<string> { "a, b", "c" }).Should().Be("\"a, b\",c");
    }

    [Fact]
    public void WriteLine_QuotesAndDoublesQuoteCharacterEvenWithoutComma()
    {
        // A quote character alone (without a comma) still forces the value to be quoted.
        ReportCsv.WriteLine(new List<string> { "5\" screen" }).Should().Be("\"5\"\" screen\"");
    }

    [Fact]
    public void WriteLine_QuotesFieldsContainingNewline()
    {
        ReportCsv.WriteLine(new List<string> { "line1\nline2" }).Should().Be("\"line1\nline2\"");
    }
}
