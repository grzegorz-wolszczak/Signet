using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// Jing's compact syntax test suite (<c>compacttest.xml</c>): an incorrect schema must not load. A correct one is
/// correct syntax, not always a correct schema (Jing runs the suite through Trang, which does not simplify): it must
/// give the same errors as its counterpart in the XML syntax, and when both load, the same simplified pattern.
/// </summary>
public sealed class CompactTestSuiteTests
{
    private const int CaseCount = 87;

    private const string SyntaxError = "syntax error";

    public static TheoryData<int> CaseNumbers => [.. CompactTestSuite.Cases.Select(c => c.Number)];

    [Fact]
    public void Suite_HasAllTheCasesOfJing()
    {
        // Act
        int count = CompactTestSuite.Cases.Count;

        // Assert
        count.Should().Be(CaseCount);
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void CompactTestCase_BehavesAsTheSuiteExpects(int number)
    {
        // Arrange
        CompactTestCase test = CompactTestSuite.Get(number);
        if (test.PendingReason is not null)
        {
            Assert.Skip(test.PendingReason);
        }

        CollectingErrorHandler compactErrors = new();
        CollectingErrorHandler xmlErrors = new();

        // Act
        RelaxNgSchema? compact = TryLoad(RelaxNgSyntax.Compact, test.CompactUri, test.Compact, test.CreateResolver(test.CompactResources), compactErrors);

        // Assert
        if (!test.IsCorrect)
        {
            compact.Should().BeNull(test.ToString());
            compactErrors.Messages.Should().NotBeEmpty(test.ToString());
            return;
        }

        compactErrors.Messages.Should().NotContain(SyntaxError, test.ToString());
        RelaxNgSchema? xml;
        try
        {
            xml = TryLoad(RelaxNgSyntax.Xml, test.XmlUri, test.Xml!, test.CreateResolver(test.XmlResources), xmlErrors);
        }
        catch (FileNotFoundException)
        {
            // The suite gives the XML counterpart without the document it includes (case 75): nothing to compare.
            return;
        }

        compactErrors.Messages.Should().Equal(xmlErrors.Messages, test.ToString());
        (compact is null).Should().Be(xml is null, test.ToString());
        if (compact is not null)
        {
            PatternEquivalence.AreEquivalent(compact.Start, xml!.Start).Should().BeTrue(test.ToString());
        }
    }

    private static RelaxNgSchema? TryLoad(RelaxNgSyntax syntax, string uri, string text, ISchemaResolver resolver, CollectingErrorHandler errors)
    {
        RelaxNgSchemaReader reader = new() { ErrorHandler = errors, Resolver = resolver, Syntax = syntax };
        try
        {
            return reader.CreateSchema(InputSource.FromString(uri, text));
        }
        catch (IncorrectSchemaException)
        {
            return null;
        }
        catch (SaxException)
        {
            return null;
        }
    }
}
