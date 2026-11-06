using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// Jing's tests of its XML Schema regular expression translator (<c>mod/regex/test</c>), run as its <c>TestDriver</c>
/// runs them: a correct expression must compile and match its valid strings and none of its invalid ones; an incorrect
/// one must not compile. <c>regextest.xml</c> runs on every engine of Jing, <c>hardtest.xml</c> on the java one only.
/// </summary>
public sealed class RegexTestSuiteTests
{
    private const string RegexSuite = "regextest.xml";
    private const string HardSuite = "hardtest.xml";
    private const int RegexCaseCount = 48;
    private const int HardCaseCount = 4;

    private static readonly JingLocalizer s_localizer = JingLocalizer.ForRegex(CultureInfo.GetCultureInfo("en"));

    public static TheoryData<string, int> Cases()
    {
        TheoryData<string, int> data = [];
        foreach (string suite in new[] { RegexSuite, HardSuite })
        {
            for (int i = 0; i < Load(suite).Count; i++)
            {
                data.Add(suite, i);
            }
        }

        return data;
    }

    [Theory]
    [InlineData(RegexSuite, RegexCaseCount)]
    [InlineData(HardSuite, HardCaseCount)]
    public void Suite_HasAllTheCasesOfJing(string suite, int count)
    {
        // Act
        int actual = Load(suite).Count;

        // Assert
        actual.Should().Be(count);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RegexTestCase_BehavesAsTheSuiteExpects(string suite, int index)
    {
        // Arrange
        XElement testCase = Load(suite)[index];
        XElement? correct = testCase.Element("correct");

        // Act & Assert
        if (correct is null)
        {
            string incorrect = testCase.Element("incorrect")!.Value;
            FluentActions.Invoking(() => XsdRegex.Compile(incorrect, s_localizer)).Should().Throw<RegexSyntaxException>(incorrect);
            return;
        }

        XsdRegex regex = XsdRegex.Compile(correct.Value, s_localizer);
        testCase.Elements("valid").Select(e => e.Value).Where(s => !regex.Matches(s)).Should().BeEmpty(correct.Value);
        testCase.Elements("invalid").Select(e => e.Value).Where(regex.Matches).Should().BeEmpty(correct.Value);
    }

    private static List<XElement> Load(string suite) =>
        [.. XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Jing", "TestData", suite), LoadOptions.PreserveWhitespace)
            .Root!.Elements("testCase")];
}
