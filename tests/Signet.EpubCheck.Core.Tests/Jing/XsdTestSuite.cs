using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml.Linq;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>A case made from Jing's <c>xsdtest.xml</c>: a schema with one datatype, and documents it must accept or refuse.</summary>
internal sealed record XsdTestCase(string Name, string Schema, IReadOnlyList<string> ValidInstances, IReadOnlyList<string> InvalidInstances)
{
    public override string ToString() => Name;
}

/// <summary>
/// Turns <c>Jing/TestData/xsdtest.xml</c> into RELAX NG test cases as Jing's <c>xsdtest.xsl</c> does: per datatype its
/// valid and invalid literals; per class of equal values a <c>value</c> pattern that accepts the others of the class and
/// none of the other classes; per <c>length</c> the facet with the length, one more and one less; per <c>lessThan</c>
/// and <c>incomparable</c> pair the <c>minExclusive</c> facet with each value.
/// </summary>
internal static class XsdTestSuite
{
    private const string RelaxNg = "http://relaxng.org/ns/structure/1.0";
    private const string XsdLibrary = "http://www.w3.org/2001/XMLSchema-datatypes";
    private const string DocumentElement = "doc";
    private const string SchemaElement = "element";

    private static readonly Lazy<IReadOnlyList<XsdTestCase>> s_cases = new(Load);

    public static IReadOnlyList<XsdTestCase> Cases => s_cases.Value;

    private static List<XsdTestCase> Load()
    {
        XDocument document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Jing", "TestData", "xsdtest.xml"), LoadOptions.PreserveWhitespace);
        List<XsdTestCase> cases = [];
        foreach (XElement datatype in document.Root!.Elements("datatype"))
        {
            string type = (string)datatype.Attribute("name")!;
            cases.Add(new XsdTestCase(type, DataSchema(type, null, null),
                [.. datatype.Elements("valid").Select(Instance)], [.. datatype.Elements("invalid").Select(Instance)]));
            List<XElement> classes = [.. datatype.Elements("equiv").Elements("class")];
            for (int i = 0; i < classes.Count; i++)
            {
                List<XElement> values = [.. classes[i].Elements("value")];
                cases.Add(new XsdTestCase(type + " class " + (i + 1).ToString(CultureInfo.InvariantCulture), ValueSchema(type, values[0]),
                    [.. values.Skip(1).Select(Instance)],
                    [.. classes.Where((_, j) => j != i).SelectMany(c => c.Elements("value")).Select(Instance)]));
            }

            foreach (XElement length in datatype.Elements("length"))
            {
                int n = (int)length.Attribute("value")!;
                string name = type + " length " + n.ToString(CultureInfo.InvariantCulture);
                cases.Add(new XsdTestCase(name, LengthSchema(type, n), [Instance(length)], []));
                cases.Add(new XsdTestCase(name + " + 1", LengthSchema(type, n + 1), [], [Instance(length)]));
                if (n != 0)
                {
                    cases.Add(new XsdTestCase(name + " - 1", LengthSchema(type, n - 1), [], [Instance(length)]));
                }
            }

            foreach (XElement pair in datatype.Elements().Where(e => e.Name.LocalName is "lessThan" or "incomparable"))
            {
                XElement[] v = [.. pair.Elements("value")];
                bool lessThan = pair.Name.LocalName == "lessThan";
                string name = type + " " + pair.Name.LocalName + " " + v[0].Value + " " + v[1].Value;
                cases.Add(new XsdTestCase(name + " (minExclusive " + v[0].Value + ")", DataSchema(type, "minExclusive", v[0].Value),
                    lessThan ? [Instance(v[1])] : [], lessThan ? [Instance(v[0])] : [Instance(v[1]), Instance(v[0])]));
                cases.Add(new XsdTestCase(name + " (minExclusive " + v[1].Value + ")", DataSchema(type, "minExclusive", v[1].Value),
                    [], [Instance(v[0]), Instance(v[1])]));
            }
        }

        return cases;
    }

    private static string DataSchema(string type, string? param, string? value) =>
        "<element xmlns=\"" + RelaxNg + "\" name=\"" + DocumentElement + "\" datatypeLibrary=\"" + XsdLibrary + "\"><data type=\"" + type + "\">"
        + (param is null ? string.Empty : "<param name=\"" + param + "\">" + SecurityElement.Escape(value) + "</param>")
        + "</data></element>";

    private static string LengthSchema(string type, int length) => DataSchema(type, "length", length.ToString(CultureInfo.InvariantCulture));

    private static string ValueSchema(string type, XElement value) =>
        Doctype(value, SchemaElement)
        + "<element xmlns=\"" + RelaxNg + "\" name=\"" + DocumentElement + "\" datatypeLibrary=\"" + XsdLibrary + "\"><value"
        + NamespaceDeclarations(value) + " type=\"" + type + "\">" + SecurityElement.Escape(value.Value) + "</value></element>";

    // A document "doc" with the literal, the namespaces in scope where it is written, and its internal subset if any.
    private static string Instance(XElement literal) =>
        Doctype(literal, DocumentElement) + "<" + DocumentElement + NamespaceDeclarations(literal) + ">" + SecurityElement.Escape(literal.Value) + "</" + DocumentElement + ">";

    private static string Doctype(XElement literal, string root) =>
        literal.Attribute("internalSubset") is { } subset ? "<!DOCTYPE " + root + " [\n" + subset.Value + "\n]>" : string.Empty;

    private static string NamespaceDeclarations(XElement element) =>
        string.Concat(element.AncestorsAndSelf().SelectMany(e => e.Attributes()).Where(a => a.IsNamespaceDeclaration)
            .GroupBy(a => a.Name.LocalName).Select(g => g.First())
            .Select(a => " xmlns:" + a.Name.LocalName + "=\"" + SecurityElement.Escape(a.Value) + "\""));
}
