using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// The schemas of EPUBCheck, embedded in this assembly (<c>Xml/Schemas</c>, imported by
/// <c>scripts/epubcheck/import_upstream_schemas.sh</c>), as upstream's class path holds them: each has a URI
/// <c>resource:/schema/…</c> (upstream's <c>ResourceUtil.getResourceURL</c> gives a <c>jar:</c> URL), against which
/// the references between schemas resolve. Upstream's build runs two XSLT 1.0 stylesheets on the
/// <c>schema/30/*-30.sch</c> files (<c>pom.xml</c>, <c>xml-maven-plugin</c>: <c>iso-sch-include.xsl</c> resolves
/// <c>include</c>, then <c>iso-sch-abstract.xsl</c> expands abstract patterns) and ships the result; the port runs them
/// the first time such a file is opened and keeps the result.
/// </summary>
public static partial class SchemaResources
{
    /// <summary>The scheme of the URIs of the embedded files.</summary>
    public const string Scheme = "resource";

    private const string UriPrefix = Scheme + ":/";
    private const string IncludeStylesheet = "util/sch/iso-sch-include.xsl";
    private const string AbstractStylesheet = "util/sch/iso-sch-abstract.xsl";

    private static readonly Assembly s_assembly = typeof(SchemaResources).Assembly;

    // Logical name of each resource by its path; MSBuild writes the folders of %(RecursiveDir) with the separator of the
    // platform the assembly was built on.
    private static readonly Dictionary<string, string> s_resources = s_assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith("schema/", StringComparison.Ordinal) || n.StartsWith("schema\\", StringComparison.Ordinal)
            || n.StartsWith("util/", StringComparison.Ordinal) || n.StartsWith("util\\", StringComparison.Ordinal))
        .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> s_preprocessed = new(StringComparer.Ordinal);
    private static readonly Lazy<XslCompiledTransform> s_include = new(() => LoadStylesheet(IncludeStylesheet));
    private static readonly Lazy<XslCompiledTransform> s_abstract = new(() => LoadStylesheet(AbstractStylesheet));

    /// <summary>The resolver of the references between schemas: embedded files by their URI, other URIs as Jing's
    /// basic resolver resolves them (upstream's <c>XMLValidator.BasicResolver</c>).</summary>
    public static ISchemaResolver Resolver { get; } = new EmbeddedSchemaResolver();

    /// <summary>The paths of the embedded files (<c>schema/30/package-30.rnc</c>, …).</summary>
    public static IEnumerable<string> Paths => s_resources.Keys;

    /// <summary>The URI of the embedded file at <paramref name="path"/>.</summary>
    public static string UriOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return UriPrefix + path;
    }

    /// <summary>The embedded file at <paramref name="path"/> as a schema source, its system id its URI.</summary>
    /// <exception cref="FileNotFoundException">No file is embedded at <paramref name="path"/>.</exception>
    public static InputSource Source(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!s_resources.ContainsKey(path))
        {
            throw new FileNotFoundException("Could not find resource " + path, path);
        }

        return new InputSource(UriOf(path), () => Open(path));
    }

    /// <summary>Whether <paramref name="path"/> is one of the files upstream's build preprocesses.</summary>
    internal static bool IsPreprocessed(string path) => PreprocessedPath().IsMatch(path);

    /// <summary>Opens the embedded file at <paramref name="path"/>, preprocessed when upstream's build preprocesses it.</summary>
    internal static Stream Open(string path)
    {
        if (IsPreprocessed(path))
        {
            byte[] bytes = s_preprocessed.GetOrAdd(path, p => new Lazy<byte[]>(() => Preprocess(p))).Value;
            return new MemoryStream(bytes, writable: false);
        }

        return OpenRaw(path);
    }

    private static Stream OpenRaw(string path)
    {
        if (!s_resources.TryGetValue(path, out string? name))
        {
            throw new FileNotFoundException("Could not find resource " + path, path);
        }

        return s_assembly.GetManifestResourceStream(name)!;
    }

    // The path of an embedded file's URI, or null when the URI is not one of them.
    private static string? PathOf(string uri)
    {
        if (!uri.StartsWith(UriPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string path = WebUtility.UrlDecode(uri[UriPrefix.Length..].Split('#', 2)[0]);
        return s_resources.ContainsKey(path) ? path : null;
    }

    private static byte[] Preprocess(string path)
    {
        byte[] included = Transform(s_include.Value, OpenRaw(path), UriOf(path));
        return Transform(s_abstract.Value, new MemoryStream(included, writable: false), UriOf(path));
    }

    private static byte[] Transform(XslCompiledTransform stylesheet, Stream input, string baseUri)
    {
        XmlReaderSettings readerSettings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(input, readerSettings, baseUri);
        using MemoryStream output = new();
        XsltArgumentList arguments = new();

        // The stylesheets report a broken include or a missing abstract pattern with a non-terminating message; upstream's
        // build only prints it, but the schema is then wrong, so it is an error here.
        arguments.XsltMessageEncountered += (_, e) =>
            throw new InvalidOperationException("Preprocessing " + baseUri + " failed: " + e.Message.Trim());
        using (XmlWriter writer = XmlWriter.Create(output, stylesheet.OutputSettings))
        {
            stylesheet.Transform(reader, arguments, writer, new EmbeddedXmlResolver());
        }

        return output.ToArray();
    }

    private static XslCompiledTransform LoadStylesheet(string path)
    {
        XslCompiledTransform stylesheet = new();
        using XmlReader reader = XmlReader.Create(OpenRaw(path), new XmlReaderSettings { XmlResolver = null }, UriOf(path));
        stylesheet.Load(reader, new XsltSettings(enableDocumentFunction: true, enableScript: false), new EmbeddedXmlResolver());
        return stylesheet;
    }

    [GeneratedRegex(@"\Aschema/30/[^/]*-30\.sch\z", RegexOptions.CultureInvariant)]
    private static partial Regex PreprocessedPath();

    private sealed class EmbeddedSchemaResolver : ISchemaResolver
    {
        public InputSource Resolve(string href, string? baseUri)
        {
            string uri = BasicSchemaResolver.ResolveUri(href, baseUri);
            string? path = PathOf(uri);
            return path is null ? BasicSchemaResolver.Instance.Resolve(uri, null) : new InputSource(uri, () => Open(path));
        }
    }

    // What the stylesheets' document() reads: the embedded files only, unprocessed (iso-sch-include.xsl reads the
    // included schemas as they are).
    private sealed class EmbeddedXmlResolver : XmlResolver
    {
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new(BasicSchemaResolver.ResolveUri(relativeUri ?? string.Empty, baseUri?.OriginalString), UriKind.Absolute);

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            ArgumentNullException.ThrowIfNull(absoluteUri);
            string path = PathOf(absoluteUri.OriginalString)
                ?? throw new FileNotFoundException("Could not find resource " + absoluteUri.OriginalString);
            return OpenRaw(path);
        }
    }
}
