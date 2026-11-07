using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Xml.XPath;
using AwesomeAssertions;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>The steps that look into the XML and JSON reports the checker wrote (upstream's <c>XMLReportSteps</c> and <c>JSONReportSteps</c>).</summary>
internal static class ReportSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((w, a) => w.XmlNamespace = a.Get<string>(0), "the default namespace is {string}");
        steps.Add((w, _) => ParseXml(w), "the XML report is well-formed");
        steps.Add((w, a) => ToBool(Evaluate(w, a.Get<string>(0))).Should().Be(a.Get<bool>(1)), "(the )XPath (value of ){string} is {bool}");
        steps.Add((w, a) => ToNumber(Evaluate(w, a.Get<string>(0))).Should().Be(a.Get<int>(1)), "(the )XPath (value of ){string} is {int}");
        steps.Add((w, a) => ToText(Evaluate(w, a.Get<string>(0))).Should().Be(a.Get<string>(1)), "(the )XPath (value of ){string} is {string}");
        steps.Add((w, a) => ToBool(Evaluate(w, a.Get<string>(0))).Should().BeTrue(), "(the )XPath {string} exists");

        steps.Add((w, _) => ParseJson(w), "the JSON report is valid");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Items.Should().HaveCount(a.Get<int>(1)), "JSON at {string} contains {int} items");
        steps.Add((w, a) => Text(Read(w, a.Get<string>(0)).Single).Should().Be(a.Get<string>(1)), "JSON at {string} is {string}");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Single!.GetValue<bool>().Should().Be(a.Get<bool>(1)), "JSON at {string} is {bool}");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Single!.GetValue<long>().Should().Be(a.Get<int>(1)), "JSON at {string} is {int}");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Items.Select(Text).Should().Equal(a.Table!.Select(r => r[0])), "JSON at {string} is:");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Items.Should().BeEmpty(), "JSON at {string} is empty");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Single.Should().NotBeNull(), "JSON at {string} is not null");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Items.Should().NotContainNulls(), "JSON at {string} has no null values");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Items.Select(Text).Should().OnlyContain(v => v == a.Get<string>(1)), "JSON at {string} are all {string}");
        steps.Add((w, a) => Read(w, a.Get<string>(0)).Items.Select(n => n!.GetValue<bool>()).Should().OnlyContain(v => v == a.Get<bool>(1)), "JSON at {string} are all {bool}");
    }

    private static string? Text(JsonNode? node) => node?.GetValue<string>();

    private static JsonPathResult Read(ScenarioWorld world, string path) =>
        JsonPathLite.Evaluate(ParseJson(world), path);

    private static JsonNode ParseJson(ScenarioWorld world)
    {
        world.ReportText.Should().NotBeNullOrEmpty("the checker must have written a report");
        return JsonNode.Parse(world.ReportText!) ?? throw new InvalidOperationException("The JSON report is empty.");
    }

    private static XDocument ParseXml(ScenarioWorld world)
    {
        world.ReportText.Should().NotBeNullOrEmpty("the checker must have written a report");
        XDocument document = XDocument.Parse(world.ReportText!);

        // XPath 1.0 cannot name elements of a default namespace without a prefix; the scenarios do, so the namespace the
        // step declared is taken off the elements that have it.
        if (world.XmlNamespace is { } ns)
        {
            foreach (XElement element in document.Descendants().Where(e => e.Name.NamespaceName == ns).ToList())
            {
                element.Name = element.Name.LocalName;
            }
        }

        return document;
    }

    private static object Evaluate(ScenarioWorld world, string expression) =>
        ParseXml(world).CreateNavigator().Evaluate(expression);

    // A node set is true when it has a node, and its text is the text of the first one.
    private static bool ToBool(object result) => result switch
    {
        XPathNodeIterator nodes => nodes.Count > 0,
        bool flag => flag,
        double number => number != 0,
        _ => Convert.ToString(result, CultureInfo.InvariantCulture)!.Length > 0,
    };

    private static double ToNumber(object result) => result switch
    {
        double number => number,
        XPathNodeIterator nodes => nodes.MoveNext() ? double.Parse(nodes.Current!.Value, CultureInfo.InvariantCulture) : double.NaN,
        _ => double.Parse(Convert.ToString(result, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
    };

    private static string ToText(object result) => result switch
    {
        XPathNodeIterator nodes => nodes.MoveNext() ? nodes.Current!.Value : string.Empty,
        _ => Convert.ToString(result, CultureInfo.InvariantCulture)!,
    };
}
