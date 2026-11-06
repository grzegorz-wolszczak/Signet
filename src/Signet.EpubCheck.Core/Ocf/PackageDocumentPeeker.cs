using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// Reads, before the package document is checked, what decides how it is checked (upstream's
/// <c>PackageDocumentPeeker</c> and <c>PackageDocumentPeekerHandler</c>): the EPUB version, the publication types
/// (<c>dc:type</c>) and the unique identifier. It reads up to the end of <c>metadata</c> and reports nothing itself.
/// </summary>
public static class PackageDocumentPeeker
{
    /// <summary>Upstream's <c>InvalidVersionException.UNSUPPORTED_VERSION</c>.</summary>
    public const string UnsupportedVersion = "Version not supported";

    /// <summary>Upstream's <c>InvalidVersionException.VERSION_NOT_FOUND</c>.</summary>
    public const string VersionNotFound = "Version not found";

    /// <summary>Upstream's <c>InvalidVersionException.VERSION_ATTRIBUTE_NOT_FOUND</c>.</summary>
    public const string VersionAttributeNotFound = "Version attribute not found";

    /// <summary>Upstream's <c>InvalidVersionException.PACKAGE_ELEMENT_NOT_FOUND</c>.</summary>
    public const string PackageElementNotFound = "Package element must be the first element in the package file";

    private const string DcElementsNamespace = "http://purl.org/dc/elements/1.1/";
    private const string Version3 = "3.0";
    private const string Version2 = "2.0";

    private static readonly Dictionary<string, PublicationType> s_types = new(StringComparer.Ordinal)
    {
        ["EPUB"] = PublicationType.Epub,
        ["DICTIONARY"] = PublicationType.Dictionary,
        ["EDUPUB"] = PublicationType.Edupub,
        ["INDEX"] = PublicationType.Index,
        ["PREVIEW"] = PublicationType.Preview,
    };

    /// <summary>Peeks at the package document in <paramref name="stream"/>.</summary>
    public static PackageDocumentInfo Peek(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        EpubVersion? version = null;
        string? uniqueIdReference = null;
        string? uniqueIdentifier = null;
        HashSet<PublicationType> types = [];
        string? error = null;
        PackageDocumentInfo Result(XmlException? wellFormednessError = null) =>
            new(version, uniqueIdentifier, types, error, wellFormednessError);

        try
        {
            using XmlReader reader = XmlReader.Create(stream, settings);
            bool isPackageRoot = false;
            bool isUniqueId = false;
            string? currentText = null;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        bool isEmpty = reader.IsEmptyElement;
                        if (reader.LocalName == "package")
                        {
                            string? declared = reader.GetAttribute("version");
                            if (declared is null)
                            {
                                error = VersionAttributeNotFound;
                                return Result();
                            }

                            version = declared switch
                            {
                                Version3 => EpubVersion.Version3,
                                Version2 => EpubVersion.Version2,
                                _ => null,
                            };
                            if (version is null)
                            {
                                error = UnsupportedVersion;
                                return Result();
                            }

                            uniqueIdReference = reader.GetAttribute("unique-identifier") ?? uniqueIdReference;
                            isPackageRoot = true;
                        }
                        else if (!isPackageRoot)
                        {
                            error = PackageElementNotFound;
                            return Result();
                        }
                        else if (reader.LocalName == "type" && reader.NamespaceURI == DcElementsNamespace)
                        {
                            currentText = string.Empty;
                        }
                        else if (reader.LocalName == "identifier" && reader.NamespaceURI == DcElementsNamespace)
                        {
                            string? id = reader.GetAttribute("id");
                            isUniqueId = id is not null && JavaStrings.Trim(id) == uniqueIdReference;
                            if (isUniqueId)
                            {
                                currentText = string.Empty;
                            }
                        }

                        if (isEmpty && EndElement(reader.LocalName, reader.NamespaceURI, ref currentText, ref isUniqueId, types, ref uniqueIdentifier))
                        {
                            return Result();
                        }

                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        if (currentText is not null)
                        {
                            currentText += reader.Value;
                        }

                        break;
                    case XmlNodeType.EndElement:
                        if (EndElement(reader.LocalName, reader.NamespaceURI, ref currentText, ref isUniqueId, types, ref uniqueIdentifier))
                        {
                            return Result();
                        }

                        break;
                }
            }

            return Result();
        }
        catch (XmlException ex)
        {
            return Result(ex);
        }
    }

    // Returns true when the peeking is over (the end of metadata or package).
    private static bool EndElement(string localName, string namespaceUri, ref string? currentText, ref bool isUniqueId,
        HashSet<PublicationType> types, ref string? uniqueIdentifier)
    {
        if (localName is "metadata" or "package")
        {
            return true;
        }

        if (localName == "type" && namespaceUri == DcElementsNamespace && currentText is not null)
        {
            string type = JavaStrings.Trim(currentText);
            // Upstream: PublicationType.valueOf(type.toUpperCase(Locale.ROOT)); an unknown type is ignored.
            if (s_types.TryGetValue(type.ToUpperInvariant(), out PublicationType known))
            {
                types.Add(known);
            }

            currentText = null;
        }
        else if (isUniqueId && localName == "identifier" && namespaceUri == DcElementsNamespace && currentText is not null)
        {
            string identifier = JavaStrings.Trim(currentText);
            if (identifier.Length > 0)
            {
                uniqueIdentifier = identifier; // the last one wins (upstream's map put)
            }

            isUniqueId = false;
            currentText = null;
        }

        return false;
    }
}

/// <summary>What <see cref="PackageDocumentPeeker"/> found (upstream keeps it in <c>OCFCheckerState</c>).</summary>
/// <param name="Version">The EPUB version of the package document, or <c>null</c> when it was not found.</param>
/// <param name="UniqueIdentifier">The text of the <c>dc:identifier</c> the <c>unique-identifier</c> attribute names, or <c>null</c>.</param>
/// <param name="Types">The known publication types of the top-level <c>dc:type</c> elements.</param>
/// <param name="Error">Why the version could not be read (one of the constants of <see cref="PackageDocumentPeeker"/>), or <c>null</c>.</param>
/// <param name="WellFormednessError">The XML error that stopped the reading, or <c>null</c>; upstream's parser reports it as <c>RSC-016</c>.</param>
public sealed record PackageDocumentInfo(
    EpubVersion? Version,
    string? UniqueIdentifier,
    IReadOnlySet<PublicationType> Types,
    string? Error,
    XmlException? WellFormednessError);
