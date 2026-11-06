using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// Jing's NVDL test suite (<c>mod/nvdl/test/nvdltest.xml</c>), run through <see cref="AutoSchemaReader"/> as Jing's
/// validate <c>TestDriver</c> runs it. The cases that need MNS (an older language Jing also reads) or W3C XML Schema
/// (Jing uses Xerces for it) are not run: EPUBCheck uses neither.
/// </summary>
public sealed class NvdlTestSuiteTests
{
    private const int CaseCount = 12;
    private const string BaseUriPrefix = "file:///nvdltest/";
    private const string SchemaName = "c.nvdl";
    private const string NotPortedReason = "uses MNS or W3C XML Schema, which are not ported (EPUBCheck uses neither)";

    private static readonly Lazy<List<XElement>> s_cases = new(() =>
        [.. XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Jing", "TestData", "nvdltest.xml"), LoadOptions.PreserveWhitespace)
            .Root!.Elements("testCase")]);

    public static TheoryData<int> CaseNumbers => [.. Enumerable.Range(1, s_cases.Value.Count)];

    [Fact]
    public void Suite_HasAllTheCasesOfJing()
    {
        // Act
        int count = s_cases.Value.Count;

        // Assert
        count.Should().Be(CaseCount);
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void NvdlTestCase_BehavesAsTheSuiteExpects(int number)
    {
        // Arrange
        XElement testCase = s_cases.Value[number - 1];
        string baseUri = BaseUriPrefix + number.ToString("000", CultureInfo.InvariantCulture) + "/";
        Dictionary<string, string> resources = testCase.Elements("resource")
            .ToDictionary(r => baseUri + (string)r.Attribute("name")!, Content, StringComparer.Ordinal);
        if (resources.Keys.Any(k => k.EndsWith(".mns", StringComparison.Ordinal) || k.EndsWith(".xsd", StringComparison.Ordinal)))
        {
            Assert.Skip(NotPortedReason);
        }

        XElement schemaElement = testCase.Element("correct") ?? testCase.Element("incorrect")!;
        CollectingErrorHandler schemaErrors = new();
        AutoSchemaReader reader = new() { ErrorHandler = schemaErrors, Resolver = new MapResolver(resources), Culture = CultureInfo.GetCultureInfo("en") };

        // Act
        ISchema? schema = TryLoad(reader, InputSource.FromString(baseUri + SchemaName, Content(schemaElement)));

        // Assert
        if (schemaElement.Name.LocalName == "incorrect")
        {
            schema.Should().BeNull("case " + number);
            return;
        }

        schema.Should().NotBeNull("case " + number + ": " + string.Join(" | ", schemaErrors.Messages));
        List<string> valid = [.. testCase.Elements("valid").Select(Content)];
        List<string> invalid = [.. testCase.Elements("invalid").Select(Content)];
        valid.Where(i => Validate(schema!, i).Count > 0).Should().BeEmpty("case " + number + ": valid");
        invalid.Where(i => Validate(schema!, i).Count == 0).Should().BeEmpty("case " + number + ": invalid");
    }

    private static ISchema? TryLoad(AutoSchemaReader reader, InputSource source)
    {
        try
        {
            return reader.CreateSchema(source);
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

    private static List<string> Validate(ISchema schema, string instance)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors).Validate(InputSource.FromString(null, instance), errors);
        return errors.Messages;
    }

    private static string Content(XElement holder)
    {
        XElement? root = holder.Elements().FirstOrDefault();
        return root is null ? holder.Value : root.ToString(SaveOptions.DisableFormatting);
    }

    private sealed class MapResolver(Dictionary<string, string> resources) : ISchemaResolver
    {
        public InputSource Resolve(string href, string? baseUri)
        {
            string uri = BasicSchemaResolver.ResolveUri(href, baseUri);
            return new InputSource(uri, () => resources.TryGetValue(uri, out string? text)
                ? new MemoryStream(Encoding.UTF8.GetBytes(text))
                : throw new FileNotFoundException("No resource " + uri));
        }
    }
}
