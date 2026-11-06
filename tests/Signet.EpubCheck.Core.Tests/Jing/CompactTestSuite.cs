using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// One case of Jing's compact syntax test suite: a schema in the compact syntax, correct or not, and for a correct one
/// the same schema in the XML syntax.
/// </summary>
internal sealed record CompactTestCase(
    int Number,
    bool IsCorrect,
    string Compact,
    string? Xml,
    IReadOnlyDictionary<string, string> CompactResources,
    IReadOnlyDictionary<string, string> XmlResources,
    string? PendingReason)
{
    private const string BaseUriPrefix = "file:///compacttest/";
    private const string CompactSchemaName = "c.rnc";
    private const string XmlSchemaName = "c.rng";

    public string CompactUri => BaseUri + CompactSchemaName;

    public string XmlUri => BaseUri + XmlSchemaName;

    private string BaseUri => BaseUriPrefix + Number.ToString("000", CultureInfo.InvariantCulture) + "/";

    /// <summary>A resolver that serves the resources of one syntax of the case (relative to its folder).</summary>
    public ISchemaResolver CreateResolver(IReadOnlyDictionary<string, string> resources) =>
        new CaseResolver(resources.ToDictionary(r => BaseUri + r.Key, r => r.Value, StringComparer.Ordinal));

    public override string ToString() => "case " + Number.ToString(CultureInfo.InvariantCulture);

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

/// <summary>Reads <c>Jing/TestData/compacttest.xml</c> (Jing's <c>mod/rng-schema/test</c>).</summary>
internal static class CompactTestSuite
{
    private const string XsdPendingReason = "uses the XML Schema datatypes (task 2.3)";
    private const string ImplicitXsdPrefix = "xsd:";

    private static readonly Lazy<IReadOnlyList<CompactTestCase>> s_cases = new(Load);

    public static IReadOnlyList<CompactTestCase> Cases => s_cases.Value;

    public static CompactTestCase Get(int number) => Cases[number - 1];

    private static List<CompactTestCase> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Jing", "TestData", "compacttest.xml");
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        List<CompactTestCase> cases = [];
        foreach (XElement testCase in document.Descendants("testCase"))
        {
            XElement compact = testCase.Element("compact")!;
            XElement schema = compact.Element("correct") ?? compact.Element("incorrect")!;
            XElement? xmlPart = testCase.Element("xml");
            string? xml = xmlPart?.Element("correct") is { } x ? Content(x) : null;
            Dictionary<string, string> compactResources = Resources(compact);
            Dictionary<string, string> xmlResources = xmlPart is null ? [] : Resources(xmlPart);
            string compactText = schema.Value;
            bool requiresXsd = new[] { compactText, xml ?? string.Empty }.Concat(compactResources.Values).Concat(xmlResources.Values)
                .Any(t => t.Contains(WellKnownNamespaces.XmlSchemaDatatypes, StringComparison.Ordinal)
                    || t.Contains(ImplicitXsdPrefix, StringComparison.Ordinal));
            cases.Add(new CompactTestCase(
                cases.Count + 1,
                schema.Name.LocalName == "correct",
                compactText,
                xml,
                compactResources,
                xmlResources,
                requiresXsd ? XsdPendingReason : null));
        }

        return cases;
    }

    private static Dictionary<string, string> Resources(XElement part) =>
        part.Elements("resource").ToDictionary(r => (string)r.Attribute("name")!, Content, StringComparer.Ordinal);

    private static string Content(XElement holder)
    {
        XElement? root = holder.Elements().FirstOrDefault();
        return root is null ? holder.Value : root.ToString(SaveOptions.DisableFormatting);
    }
}
