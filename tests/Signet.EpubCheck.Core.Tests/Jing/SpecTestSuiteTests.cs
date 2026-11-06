using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// Jing's RELAX NG test suite (<c>spectest.xml</c>) run as Jing's <c>TestDriver</c> runs it: an incorrect schema must
/// not load, a correct one must load, accept its valid instances and reject its invalid ones.
/// </summary>
public sealed class SpecTestSuiteTests
{
    private const int CaseCount = 385;

    public static TheoryData<int> CaseNumbers => [.. SpecTestSuite.Cases.Select(c => c.Number)];

    [Fact]
    public void Suite_HasAllTheCasesOfJing()
    {
        // Act
        int count = SpecTestSuite.Cases.Count;

        // Assert
        count.Should().Be(CaseCount);
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void SpecTestCase_BehavesAsTheSuiteExpects(int number)
    {
        // Arrange
        SpecTestCase test = SpecTestSuite.Get(number);
        CollectingErrorHandler schemaErrors = new();
        RelaxNgSchemaReader reader = new() { ErrorHandler = schemaErrors, Resolver = test.CreateResolver() };

        // Act
        RelaxNgSchema? schema = TryLoad(reader, test);

        // Assert
        if (!test.IsCorrect)
        {
            schema.Should().BeNull(test.ToString());
            return;
        }

        schema.Should().NotBeNull(test + ": " + string.Join(" | ", schemaErrors.Messages));
        for (int i = 0; i < test.ValidInstances.Count; i++)
        {
            Validate(schema!, test.ValidInstances[i]).Should().BeEmpty(test + ", valid instance " + (i + 1));
        }

        for (int i = 0; i < test.InvalidInstances.Count; i++)
        {
            Validate(schema!, test.InvalidInstances[i]).Should().NotBeEmpty(test + ", invalid instance " + (i + 1));
        }
    }

    private static RelaxNgSchema? TryLoad(RelaxNgSchemaReader reader, SpecTestCase test)
    {
        try
        {
            return reader.CreateSchema(InputSource.FromString(test.SchemaUri, test.Schema));
        }
        catch (IncorrectSchemaException)
        {
            return null;
        }
        catch (SaxException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static List<string> Validate(RelaxNgSchema schema, string instance)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors).Validate(InputSource.FromString(null, instance));
        return errors.Messages;
    }
}

/// <summary>An error handler that keeps the messages of the warnings, errors and fatal errors.</summary>
internal sealed class CollectingErrorHandler : ISaxErrorHandler
{
    public List<string> Messages { get; } = [];

    public List<SaxParseException> Errors { get; } = [];

    public List<SaxParseException> Warnings { get; } = [];

    public void OnWarning(SaxParseException exception) => Warnings.Add(exception);

    public void OnError(SaxParseException exception)
    {
        Errors.Add(exception);
        Messages.Add(exception.Message);
    }

    public void OnFatalError(SaxParseException exception)
    {
        Errors.Add(exception);
        Messages.Add(exception.Message);
    }
}
