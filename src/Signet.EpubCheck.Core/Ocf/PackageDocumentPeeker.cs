using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>Reads the package version and metadata facts needed before OPF validation.</summary>
public static class PackageDocumentPeeker
{
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";

    public static PackageDocumentInfo Peek(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            XDocument document = XDocument.Load(stream, LoadOptions.None);
            XElement? package = document.Root;
            if (package?.Name.LocalName != "package")
            {
                return new PackageDocumentInfo(null, null, [], "Package element not found.");
            }

            EpubVersion? version = package.Attribute("version")?.Value switch
            {
                "2.0" => EpubVersion.Version2,
                "3.0" => EpubVersion.Version3,
                null => null,
                _ => null,
            };
            if (version is null)
            {
                return new PackageDocumentInfo(null, null, [], package.Attribute("version") is null ? "Version attribute not found." : "Unsupported EPUB version.");
            }

            string? identifierId = package.Attribute("unique-identifier")?.Value;
            string? identifier = null;
            List<string> types = [];
            foreach (XElement element in package.Descendants(XName.Get("type", DcNamespace)))
            {
                string text = element.Value.Trim();
                if (text.Length > 0)
                {
                    types.Add(text);
                }
            }

            foreach (XElement element in package.Descendants(XName.Get("identifier", DcNamespace)))
            {
                if (element.Attribute("id")?.Value == identifierId)
                {
                    string text = element.Value.Trim();
                    if (text.Length > 0)
                    {
                        identifier = text;
                    }
                }
            }

            return new PackageDocumentInfo(version, identifier, types, null);
        }
        catch (XmlException exception)
        {
            return new PackageDocumentInfo(null, null, [], exception.Message);
        }
    }
}

/// <summary>The data obtained from the package-document pre-scan.</summary>
public sealed record PackageDocumentInfo(EpubVersion? Version, string? UniqueIdentifier, IReadOnlyList<string> Types, string? Error);
