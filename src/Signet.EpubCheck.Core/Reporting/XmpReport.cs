using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>The report in XMP (<c>--xmp</c>; upstream's <c>XmpReportImpl</c>): the description of the publication as RDF, with the messages as a PREMIS event.</summary>
public sealed class XmpReport : XmlReportBase
{
    private const string AdobeMeta = "adobe:ns:meta/";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string DublinCore = "http://purl.org/dc/elements/1.1/";
    private const string Xmp = "http://ns.adobe.com/xap/1.0/";
    private const string XmpPages = "http://ns.adobe.com/xap/1.0/t/pg/";
    private const string XmpFont = "http://ns.adobe.com/xap/1.0/sType/Font#";
    private const string ExtendedProperties = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties/";
    private const string Premis = "http://www.loc.gov/premis/rdf/v1#";
    private const string XmpToolkit = "Adobe XMP Core 5.1.0-jc003";
    private const string DefaultFormat = "application/octet-stream";
    private const string EventType = "http://id.loc.gov/vocabulary/preservation/eventType/val";
    private const string AgentType = "http://id.loc.gov/vocabulary/preservation/agentType/sof";
    private const string DateTimeType = "http://www.w3.org/2001/XMLSchema#dateTime";
    private const int MaxReferences = 50;

    /// <summary>Creates the report for <paramref name="epubName"/>, to be written to <paramref name="output"/>.</summary>
    /// <param name="output">Where the XMP goes.</param>
    /// <param name="epubName">The checked file or folder, as the report names it.</param>
    /// <param name="toolVersion">The version of the tool.</param>
    /// <param name="closeWriter">Whether <see cref="XmlReportBase.Generate"/> disposes <paramref name="output"/>.</param>
    public XmpReport(TextWriter output, string epubName, string? toolVersion, bool closeWriter = true)
        : base(output, epubName, toolVersion, closeWriter)
    {
    }

    /// <inheritdoc />
    protected override int GenerateReport()
    {
        GenerationDate = Now();
        AddPrefixNamespace("x", AdobeMeta);
        AddPrefixNamespace("rdf", Rdf);
        AddPrefixNamespace("dc", DublinCore);
        AddPrefixNamespace("xmp", Xmp);
        AddPrefixNamespace("xmpTPg", XmpPages);
        AddPrefixNamespace("stFnt", XmpFont);
        AddPrefixNamespace("extended-properties", ExtendedProperties);
        AddPrefixNamespace("premis", Premis);

        StartElement("x:xmpmeta", ("xmlns:x", AdobeMeta), ("x:xmptk", XmpToolkit));
        StartElement("rdf:RDF", ("xmlns:rdf", Rdf));

        List<(string Key, string? Value)> attributes =
        [
            ("rdf:about", string.Empty),
            ("xmlns:dc", DublinCore),
            ("xmlns:xmp", Xmp),
            ("xmlns:xmpTPg", XmpPages),
            ("xmlns:stFnt", XmpFont),
            ("xmlns:extended-properties", ExtendedProperties),
            ("xmlns:premis", Premis),
        ];
        attributes.Add(("dc:format", FormatName is null ? DefaultFormat : FormatVersion is null ? FormatName : FormatName + ";version=" + FormatVersion));
        if (CreationDate is not null)
        {
            attributes.Add(("xmp:CreateDate", CreationDate));
        }

        if (CharsCount != 0)
        {
            attributes.Add(("extended-properties:Characters", CharsCount.ToString(CultureInfo.InvariantCulture)));
        }

        if (PagesCount != 0)
        {
            attributes.Add(("xmpTPg:NPages", PagesCount.ToString(CultureInfo.InvariantCulture)));
        }

        if (Publisher is not null)
        {
            attributes.Add(("dc:publisher", Publisher));
        }

        attributes.Add(("dc:identifier", Identifier));
        if (Language is not null)
        {
            attributes.Add(("dc:language", Language));
        }

        StartElement("rdf:Description", [.. attributes]);

        if (Creators.Count > 0)
        {
            StartElement("dc:creator");
            StartElement("rdf:Seq");
            foreach (string creator in Creators)
            {
                GenerateElement("rdf:li", creator);
            }

            EndElement();
            EndElement();
        }

        if (Titles.Count > 0)
        {
            StartElement("dc:title");
            StartElement("rdf:Alt");
            bool first = true;
            foreach (string title in Titles)
            {
                if (first)
                {
                    GenerateElement("rdf:li", title.Trim(), ("xml:lang", "x-default"));
                    first = false;
                }
                else
                {
                    GenerateElement("rdf:li", title);
                }
            }

            EndElement();
            EndElement();
        }

        if (Subjects.Count > 0)
        {
            StartElement("dc:subject");
            StartElement("rdf:Bag");
            foreach (string subject in Subjects)
            {
                GenerateElement("rdf:li", subject);
            }

            EndElement();
            EndElement();
        }

        if (EmbeddedFonts.Count > 0 || ReferencedFonts.Count > 0)
        {
            StartElement("xmpTPg:Fonts");
            StartElement("rdf:Bag");
            foreach (string font in EmbeddedFonts)
            {
                GenerateFont(font);
            }

            foreach (string font in ReferencedFonts)
            {
                GenerateFont(font);
            }

            EndElement();
            EndElement();
        }

        GenerateEvent();
        GenerateSignificantProperties();

        EndElement();
        EndElement();
        EndElement();
        return 0;
    }

    private void GenerateEvent()
    {
        StartElement("premis:hasEvent", ("rdf:parseType", "Resource"));
        GenerateElement("premis:hasEventDateTime", GenerationDate, ("rdf:datatype", DateTimeType));
        GenerateElement("premis:hasEventType", null, ("rdf:resource", EventType));
        GenerateElement("premis:hasEventDetail", IsWellFormed ? "Well-formed" : "Not well-formed");
        if (FatalErrors.Count + Errors.Count + Warnings.Count + Hints.Count != 0)
        {
            StartElement("premis:hasEventOutcomeInformation");
            StartElement("rdf:Seq");
            GenerateEventOutcome(FatalErrors, "FATAL");
            GenerateEventOutcome(Errors, "ERROR");
            GenerateEventOutcome(Warnings, "WARN");
            GenerateEventOutcome(Hints, "HINT");
            EndElement();
            EndElement();
        }

        StartElement("premis:hasEventRelatedAgent", ("rdf:parseType", "Resource"));
        GenerateElement("premis:hasAgentType", null, ("rdf:resource", AgentType));
        GenerateElement("premis:hasAgentName", ToolVersion is null ? ToolName : ToolName + " " + ToolVersion);
        EndElement();
        EndElement();
    }

    private void GenerateSignificantProperties()
    {
        StartElement("premis:hasSignificantProperties");
        StartElement("rdf:Bag");
        GenerateSignificantProperty("renditionLayout", HasFixedLayout ? "fixed-layout" : "reflowable");
        GenerateSignificantProperty("isScripted", Flag(HasScripts));
        GenerateSignificantProperty("hasEncryption", Flag(HasEncryption));
        GenerateSignificantProperty("hasAudio", Flag(HasAudio));
        GenerateSignificantProperty("hasVideo", Flag(HasVideo));
        GenerateSignificantProperty("hasSignatures", Flag(HasSignatures));
        GenerateSignificantProperty("hasAllFontsEmbedded", Flag(ReferencedFonts.Count == 0));
        int count = 0;
        foreach (string reference in References)
        {
            count++;
            if (count > MaxReferences)
            {
                GenerateSignificantProperty("reference", (References.Count - MaxReferences).ToString(CultureInfo.InvariantCulture) + " more references");
                break;
            }

            GenerateSignificantProperty("reference", reference);
        }

        EndElement();
        EndElement();
    }

    private static string Flag(bool value) => value ? "true" : "false";

    // A font is described as "family,face,face…"; the face is "Regular" when there is none.
    private void GenerateFont(string font)
    {
        string[] parts = font.Split(',');
        List<(string Key, string? Value)> attributes = [("stFnt:fontFamily", Capitalize(parts[0]))];
        string fontFace = string.Empty;
        for (int i = 1; i < parts.Length; i++)
        {
            fontFace += Capitalize(parts[i]) + " ";
        }

        fontFace = fontFace.Trim();
        attributes.Add(("stFnt:fontFace", fontFace.Length == 0 ? "Regular" : fontFace));
        GenerateElement("rdf:li", null, [.. attributes]);
    }

    private void GenerateEventOutcome(List<CheckMessage> messages, string severity)
    {
        foreach (CheckMessage message in messages)
        {
            StartElement("rdf:li", ("rdf:parseType", "Resource"));
            GenerateElement("premis:hasEventOutcome", $"{message.Id}, {severity}, {message.Text}");
            if (message.Locations.Count != 0)
            {
                StartElement("premis:hasEventOutcomeDetail");
                StartElement("rdf:Seq");
                foreach (string value in message.DescribeDistinctNeighbours())
                {
                    GenerateElement("rdf:li", null, ("premis:hasEventOutcomeDetailNote", value));
                }

                EndElement();
                EndElement();
            }

            EndElement();
        }
    }

    private void GenerateSignificantProperty(string property, string value) =>
        GenerateElement("rdf:li", null, ("premis:hasSignificantPropertiesType", property), ("premis:hasSignificantPropertiesValue", value));
}
