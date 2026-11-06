using System;
using System.IO;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Jing.Parse;

/// <summary>A schema document in the XML syntax (Jing's <c>SAXParseable</c> and <c>SAXSubParser</c>).</summary>
internal sealed class SaxParseable(InputSource source, ISchemaResolver resolver, ISaxErrorHandler? eh, JingLocalizer parseLocalizer)
    : ISubParseable
{
    public string? Uri => source.SystemId is null ? null : JingUri.EscapeDisallowedChars(source.SystemId);

    public ISubParseable CreateSubParseable(string href, string? baseUri) =>
        new SaxParseable(resolver.Resolve(href, baseUri), resolver, eh, parseLocalizer);

    public Pattern Parse(SchemaBuilderImpl schemaBuilder, IScope scope) =>
        ParseWith(schemaBuilder, null, scope) ?? throw new IllegalSchemaException();

    public Pattern? ParseAsInclude(SchemaBuilderImpl schemaBuilder, IIncludedGrammar grammar) => ParseWith(schemaBuilder, grammar, grammar);

    private Pattern? ParseWith(SchemaBuilderImpl schemaBuilder, IIncludedGrammar? grammar, IScope scope)
    {
        XmlEventReader xr = new();
        SchemaParser sp = new(xr, eh, schemaBuilder, grammar, scope, parseLocalizer);
        xr.Parse(source);
        return sp.GetParsedPattern();
    }
}

/// <summary>
/// The resolver of EPUBCheck (its <c>XMLValidator.BasicResolver</c>): a relative reference is resolved against its
/// base as <c>java.net.URI</c> does it, a <c>file:</c> URI is opened from the disk. Anything else cannot be opened
/// (the tool never touches the network).
/// </summary>
public sealed class BasicSchemaResolver : ISchemaResolver
{
    /// <summary>The resolver.</summary>
    public static BasicSchemaResolver Instance { get; } = new();

    /// <inheritdoc />
    public InputSource Resolve(string href, string? baseUri)
    {
        string uri = ResolveUri(href, baseUri);
        return new InputSource(uri, () => Open(uri));
    }

    /// <summary>The URI <paramref name="href"/> refers to.</summary>
    /// <exception cref="SaxException">A URI does not parse.</exception>
    public static string ResolveUri(string href, string? baseUri)
    {
        ArgumentNullException.ThrowIfNull(href);
        if (!JavaUri.IsValid(href))
        {
            throw new SaxException("Illegal URI: " + href);
        }

        if (baseUri is null || JingUri.IsAbsolute(href))
        {
            return href;
        }

        try
        {
            return JavaUri.Resolve(baseUri, href);
        }
        catch (FormatException e)
        {
            throw new SaxException("Illegal URI: " + baseUri, e);
        }
    }

    private static FileStream Open(string uri)
    {
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            throw new IOException("cannot open relative URI: " + uri);
        }

        if (!parsed.IsFile)
        {
            throw new IOException("cannot open URI: " + uri);
        }

        return File.OpenRead(parsed.LocalPath);
    }
}
