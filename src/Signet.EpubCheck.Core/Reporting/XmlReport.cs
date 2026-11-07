using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>The report in XML (<c>--out</c>; upstream's <c>XmlReportImpl</c>): a JHOVE-style description of the publication and its messages.</summary>
public sealed class XmlReport : XmlReportBase
{
    private const string JhoveNamespace = "http://schema.openpreservation.org/ois/xml/ns/jhove";
    private const string SchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string SchemaLocation = JhoveNamespace + " https://schema.openpreservation.org/ois/xml/xsd/jhove/jhove.xsd";
    private const string DefaultFormat = "application/octet-stream";
    private const string Scalar = "Scalar";
    private const string ArrayArity = "Array";
    private const string ListArity = "List";
    private const string PropertyType = "Property";

    /// <summary>Creates the report for <paramref name="epubName"/>, to be written to <paramref name="output"/>.</summary>
    /// <param name="output">Where the XML goes.</param>
    /// <param name="epubName">The checked file or folder, as the report names it.</param>
    /// <param name="toolVersion">The version of the tool.</param>
    /// <param name="closeWriter">Whether <see cref="XmlReportBase.Generate"/> disposes <paramref name="output"/>.</param>
    public XmlReport(TextWriter output, string epubName, string? toolVersion, bool closeWriter = true)
        : base(output, epubName, toolVersion, closeWriter)
    {
    }

    /// <inheritdoc />
    protected override int GenerateReport()
    {
        GenerationDate = Now();
        SetNamespace(JhoveNamespace);
        AddPrefixNamespace("xsi", SchemaInstanceNamespace);
        StartElement(
            "jhove",
            ("xmlns:xsi", SchemaInstanceNamespace),
            ("name", ToolName),
            ("release", ToolVersion),
            ("date", ToolDate),
            ("xsi:schemaLocation", SchemaLocation));

        GenerateElement("date", GenerationDate);
        StartElement("repInfo", ("uri", EpubFileName));
        GenerateElement("created", CreationDate);
        GenerateElement("lastModified", LastModifiedDate);
        GenerateElement("format", FormatName ?? DefaultFormat);
        GenerateElement("version", FormatVersion);
        if (!string.IsNullOrEmpty(CustomMessageFile))
        {
            GenerateElement("customMessageFileName", CustomMessageFile);
        }

        GenerateElement("status", IsWellFormed ? "Well-formed" : "Not well-formed");
        if (Warnings.Count > 0 || FatalErrors.Count > 0 || Errors.Count > 0 || Hints.Count > 0)
        {
            StartElement("messages");
            GenerateMessages(FatalErrors, "FATAL", "error");
            GenerateMessages(Errors, "ERROR", "error");
            GenerateMessages(Warnings, "WARN", "warning");
            GenerateMessages(Hints, "HINT", "info");
            EndElement();
        }

        GenerateElement("mimeType", FormatName);
        StartElement("properties");

        GenerateProperty("FileName", GetNameFromPath(EpubFileName), "String");
        GenerateProperty("PageCount", PagesCount);
        GenerateProperty("CharacterCount", CharsCount);
        GenerateProperty("Language", Language, "String");

        StartElement("property");
        GenerateElement("name", "Info");
        StartValues(ListArity, PropertyType);
        GenerateProperty("Identifier", Identifier, "String");
        GenerateProperty("CreationDate", CreationDate, "Date");
        GenerateProperty("ModDate", LastModifiedDate, "Date");
        GenerateProperty("Title", Titles, "String");
        GenerateProperty("Creator", Creators, "String");
        GenerateProperty("Contributor", Contributors, "String");
        GenerateProperty("Date", Date, "String");
        GenerateProperty("Publisher", Publisher, "String");
        GenerateProperty("Subject", Subjects, "String");
        GenerateProperty("Rights", Rights, "String");
        EndElement();
        EndElement();

        if (EmbeddedFonts.Count > 0 || ReferencedFonts.Count > 0)
        {
            StartElement("property");
            GenerateElement("name", "Fonts");
            StartValues(ListArity, PropertyType);
            foreach (string font in EmbeddedFonts)
            {
                GenerateFont(font, embedded: true);
            }

            foreach (string font in ReferencedFonts)
            {
                GenerateFont(font, embedded: false);
            }

            EndElement();
            EndElement();
        }

        if (References.Count > 0)
        {
            StartElement("property");
            GenerateElement("name", "References");
            StartValues(ListArity, PropertyType);
            foreach (string reference in References)
            {
                GenerateProperty("Reference", reference, "String");
            }

            EndElement();
            EndElement();
        }

        GenerateProperty("MediaTypes", MediaTypes, "String");
        GenerateFlag("hasEncryption", HasEncryption);
        GenerateFlag("hasSignatures", HasSignatures);
        GenerateFlag("hasAudio", HasAudio);
        GenerateFlag("hasVideo", HasVideo);
        GenerateFlag("hasFixedLayout", HasFixedLayout);
        GenerateFlag("hasScripts", HasScripts);

        EndElement();
        EndElement();
        EndElement();
        return 0;
    }

    private void GenerateMessages(List<CheckMessage> messages, string severityLabel, string severityAttribute)
    {
        foreach (CheckMessage message in messages)
        {
            string prefix = $"{message.Id}, {severityLabel}, [{message.Text}], ";
            foreach (EpubLocation location in message.Locations)
            {
                GenerateElement("message", prefix + CheckMessage.DescribeLocation(location), ("id", message.Id), ("severity", severityAttribute));
            }
        }
    }

    private void GenerateFont(string font, bool embedded)
    {
        StartElement("property");
        GenerateElement("name", "Font");
        StartValues(ListArity, PropertyType);
        GenerateProperty("FontName", GetNameFromPath(font), "String");
        GenerateProperty("FontFile", embedded);
        EndElement();
        EndElement();
    }

    private void StartValues(string arity, string type) => StartElement("values", ("arity", arity), ("type", type));

    private void GenerateProperty(string name, IEnumerable<string> values, string type)
    {
        string[] array = values.ToArray();
        if (array.Length == 0)
        {
            return;
        }

        StartElement("property");
        GenerateElement("name", name);
        StartValues(array.Length == 1 ? Scalar : ArrayArity, type);
        foreach (string value in array)
        {
            GenerateElement("value", value);
        }

        EndElement();
        EndElement();
    }

    private void GenerateProperty(string name, string? value, string type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        StartElement("property");
        GenerateElement("name", name);
        StartValues(Scalar, type);
        GenerateElement("value", value);
        EndElement();
        EndElement();
    }

    private void GenerateProperty(string name, long value)
    {
        if (value != 0)
        {
            GenerateProperty(name, value.ToString(CultureInfo.InvariantCulture), "Long");
        }
    }

    private void GenerateProperty(string name, bool value) => GenerateProperty(name, value ? "true" : "false", "Boolean");

    private void GenerateFlag(string name, bool value)
    {
        if (value)
        {
            GenerateProperty(name, value);
        }
    }
}
