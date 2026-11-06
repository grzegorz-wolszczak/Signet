using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>Jing's XML Schema datatype tests (<c>mod/xsd-datatype/test/xsdtest.xml</c>), run through schemas as Jing runs them.</summary>
public sealed class XsdTestSuiteTests
{
    private const int DatatypeCount = 44;

    public static TheoryData<int> CaseIndexes => [.. Enumerable.Range(0, XsdTestSuite.Cases.Count)];

    [Fact]
    public void Suite_HasAllTheDatatypesOfJing()
    {
        // Act
        int count = XsdTestSuite.Cases.Count(c => !c.Name.Contains(' ', System.StringComparison.Ordinal));

        // Assert
        count.Should().Be(DatatypeCount);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void XsdTestCase_BehavesAsTheSuiteExpects(int index)
    {
        // Arrange
        XsdTestCase test = XsdTestSuite.Cases[index];
        CollectingErrorHandler schemaErrors = new();

        // Act
        RelaxNgSchema schema = new RelaxNgSchemaReader { ErrorHandler = schemaErrors }.CreateSchema(InputSource.FromString("file:///xsdtest/c.rng", test.Schema));

        // Assert
        schemaErrors.Messages.Should().BeEmpty(test.ToString());
        test.ValidInstances.Where(i => Validate(schema, i).Count > 0).Should().BeEmpty(test + ": valid");
        test.InvalidInstances.Where(i => Validate(schema, i).Count == 0).Should().BeEmpty(test + ": invalid");
    }

    private static List<string> Validate(RelaxNgSchema schema, string instance)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors).Validate(InputSource.FromString(null, instance));
        return errors.Messages;
    }
}
