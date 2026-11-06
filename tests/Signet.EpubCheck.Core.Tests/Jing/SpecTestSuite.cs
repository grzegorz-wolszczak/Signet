using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>One case of Jing's RELAX NG test suite: a correct or incorrect schema, its resources and instances.</summary>
internal sealed record SpecTestCase(
    int Number,
    string Sections,
    bool IsCorrect,
    string Schema,
    IReadOnlyDictionary<string, string> Resources,
    IReadOnlyList<string> ValidInstances,
    IReadOnlyList<string> InvalidInstances)
{
    // The schema of a case is written where Jing's prep.xsl writes it: NNN/c.rng or NNN/i.rng.
    private const string BaseUriPrefix = "file:///spectest/";
    private const string CorrectSchemaName = "c.rng";
    private const string IncorrectSchemaName = "i.rng";

    public string SchemaUri => BaseUri + (IsCorrect ? CorrectSchemaName : IncorrectSchemaName);

    private string BaseUri => BaseUriPrefix + Number.ToString("000", CultureInfo.InvariantCulture) + "/";

    /// <summary>A resolver that serves the resources of the case (relative to its folder).</summary>
    public ISchemaResolver CreateResolver() => new CaseResolver(Resources.ToDictionary(r => BaseUri + r.Key, r => r.Value, StringComparer.Ordinal));

    public override string ToString() => "case " + Number.ToString(CultureInfo.InvariantCulture) + " (section " + Sections + ")";

    private sealed class CaseResolver(Dictionary<string, string> resources) : ISchemaResolver
    {
        public InputSource Resolve(string href, string? baseUri)
        {
            string uri = BasicSchemaResolver.ResolveUri(href, baseUri);
            return new InputSource(uri, () => resources.TryGetValue(uri, out string? text)
                ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))
                : throw new FileNotFoundException("No resource " + uri));
        }
    }
}

/// <summary>Reads <c>Jing/TestData/spectest.xml</c> as Jing's <c>prep.xsl</c> splits it into files.</summary>
internal static class SpecTestSuite
{
    private static readonly Lazy<IReadOnlyList<SpecTestCase>> s_cases = new(Load);

    public static IReadOnlyList<SpecTestCase> Cases => s_cases.Value;

    public static SpecTestCase Get(int number) => Cases[number - 1];

    private static List<SpecTestCase> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Jing", "TestData", "spectest.xml");
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        List<SpecTestCase> cases = [];
        foreach (XElement testCase in document.Descendants("testCase"))
        {
            XElement? schema = testCase.Element("correct") ?? testCase.Element("incorrect");
            if (schema is null)
            {
                continue;
            }

            Dictionary<string, string> resources = new(StringComparer.Ordinal);
            AddResources(testCase, string.Empty, resources);
            string schemaText = Content(schema);
            cases.Add(new SpecTestCase(
                cases.Count + 1,
                string.Join(", ", testCase.Elements("section").Select(s => s.Value)),
                schema.Name.LocalName == "correct",
                schemaText,
                resources,
                testCase.Elements("valid").Select(Content).ToList(),
                testCase.Elements("invalid").Select(Content).ToList()));
        }

        return cases;
    }

    private static void AddResources(XElement parent, string directory, Dictionary<string, string> resources)
    {
        foreach (XElement resource in parent.Elements("resource"))
        {
            resources[directory + (string)resource.Attribute("name")!] = Content(resource);
        }

        foreach (XElement dir in parent.Elements("dir"))
        {
            AddResources(dir, directory + (string)dir.Attribute("name")! + "/", resources);
        }
    }

    // A document of the suite: the element it holds (with the namespaces it uses), or its text.
    private static string Content(XElement holder)
    {
        XElement? root = holder.Elements().FirstOrDefault();
        return root is null ? holder.Value : root.ToString(SaveOptions.DisableFormatting);
    }
}
