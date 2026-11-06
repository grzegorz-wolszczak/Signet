using System;
using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Parse;

// Jing's parse package with the types of the pattern builder filled in: its parsers are generic over the builder
// (Trang has another one), the port has one builder only. Comments and foreign elements are not kept: Jing's builder
// throws them away too, it keeps only the a:defaultValue attribute.

/// <summary>A schema has errors; they were reported to the error handler (Jing's <c>IllegalSchemaException</c>).</summary>
internal sealed class IllegalSchemaException : Exception
{
    public IllegalSchemaException()
    {
    }

    public IllegalSchemaException(string message)
        : base(message)
    {
    }

    public IllegalSchemaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>How a definition combines with the other definitions of its name (<c>combine</c> attribute).</summary>
internal enum GrammarCombine
{
    Choice,
    Interleave,
}

/// <summary>The names in a grammar section (Jing's <c>GrammarSection</c> constants).</summary>
internal static class GrammarSection
{
    /// <summary>The name of the start definition: compared by reference, so that a definition named <c>#start</c> is another one.</summary>
    public static readonly string Start = new("#start".AsSpan());

    /// <summary>The namespace inherited from the including schema: compared by reference (Jing's <c>SchemaBuilder.INHERIT_NS</c>).</summary>
    public static readonly string InheritNs = new("#inherit".AsSpan());
}

/// <summary>The attributes of a RELAX NG element that are not RELAX NG ones (Jing's <c>AnnotationsImpl</c>): only <c>a:defaultValue</c> is kept.</summary>
internal sealed class AnnotationsImpl
{
    private const string DefaultValueAttribute = "defaultValue";

    public string? DefaultValue { get; private set; }

    public void AddAttribute(string ns, string localName, string value)
    {
        if (string.Equals(localName, DefaultValueAttribute, StringComparison.Ordinal)
            && string.Equals(ns, WellKnownNamespaces.RelaxNgCompatibilityAnnotations, StringComparison.Ordinal))
        {
            DefaultValue = value;
        }
    }
}

/// <summary>The namespaces in scope where a pattern is written (Jing's parse <c>Context</c>).</summary>
internal interface IParseContext : IValidationContext
{
    ISet<string> Prefixes();

    IParseContext Copy();
}

/// <summary>Where <c>ref</c> and <c>parentRef</c> find their definitions.</summary>
internal interface IScope
{
    Pattern MakeParentRef(string name, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeRef(string name, ILocator? loc, AnnotationsImpl? anno);
}

/// <summary>The content of a <c>grammar</c>, <c>div</c> or <c>include</c>: definitions.</summary>
internal interface IGrammarSection
{
    void Define(string name, GrammarCombine? combine, Pattern pattern, ILocator? loc, AnnotationsImpl? anno);

    IDiv MakeDiv();

    /// <summary>An include in this section, or <c>null</c> when the section is already in an include.</summary>
    IInclude? MakeInclude();
}

internal interface IDiv : IGrammarSection
{
    void EndDiv(ILocator? loc, AnnotationsImpl? anno);
}

internal interface IInclude : IGrammarSection
{
    void EndInclude(string href, string? baseUri, string ns, ILocator? loc, AnnotationsImpl? anno);
}

internal interface IGrammar : IGrammarSection, IScope
{
    Pattern EndGrammar(ILocator? loc, AnnotationsImpl? anno);
}

/// <summary>A grammar whose definitions go into the grammar that includes it.</summary>
internal interface IIncludedGrammar : IGrammarSection, IScope
{
    Pattern? EndIncludedGrammar(ILocator? loc, AnnotationsImpl? anno);
}

/// <summary>Builds a <c>data</c> pattern: parameters, then the pattern (Jing's <c>DataPatternBuilder</c>).</summary>
internal interface IDataPatternBuilder
{
    void AddParam(string name, string value, IParseContext context, string ns, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakePattern(ILocator? loc, AnnotationsImpl? anno);

    Pattern MakePattern(Pattern except, ILocator? loc, AnnotationsImpl? anno);
}

/// <summary>Opens the schemas an <c>include</c> or <c>externalRef</c> names (Jing's <c>SubParser</c>).</summary>
internal interface ISubParser
{
    ISubParseable CreateSubParseable(string href, string? baseUri);
}

/// <summary>A schema document that can be parsed (Jing's <c>SubParseable</c>).</summary>
internal interface ISubParseable : ISubParser
{
    /// <summary>The URI of the document (with the characters a URI cannot hold escaped), or <c>null</c>.</summary>
    string? Uri { get; }

    /// <exception cref="IllegalSchemaException">The schema has errors (reported).</exception>
    Pattern Parse(SchemaBuilderImpl schemaBuilder, IScope scope);

    /// <exception cref="IllegalSchemaException">The schema has errors (reported).</exception>
    Pattern? ParseAsInclude(SchemaBuilderImpl schemaBuilder, IIncludedGrammar grammar);
}

/// <summary>Finds the schema documents an <c>include</c> or <c>externalRef</c> refers to (Jing's <c>Resolver</c>).</summary>
public interface ISchemaResolver
{
    /// <summary>The document <paramref name="href"/> refers to, relative to <paramref name="baseUri"/>; its system id is the resolved URI.</summary>
    InputSource Resolve(string href, string? baseUri);
}

/// <summary>
/// What the schema parsers build with (Jing's <c>SchemaBuilder</c>): the XML syntax parser and the compact syntax
/// parser call the same methods. The locations and annotations are passed as Jing passes them, even where this
/// builder does not use them.
/// </summary>
internal interface ISchemaBuilder
{
    Pattern MakeChoice(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeInterleave(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeGroup(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeOneOrMore(Pattern p, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeZeroOrMore(Pattern p, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeOptional(Pattern p, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeList(Pattern p, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeMixed(Pattern p, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeEmpty(ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeNotAllowed(ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeText(ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeAttribute(NameClass nc, Pattern p, ILocator? loc, AnnotationsImpl? anno);

    Pattern MakeElement(NameClass nc, Pattern p, ILocator? loc, AnnotationsImpl? anno);

    IDataPatternBuilder MakeDataPatternBuilder(string datatypeLibrary, string type, ILocator? loc);

    Pattern MakeValue(string datatypeLibrary, string type, string value, IParseContext context, string ns, ILocator? loc, AnnotationsImpl? anno);

    IGrammar MakeGrammar(IScope? parent);

    Pattern MakeExternalRef(string href, string? baseUri, string ns, IScope scope, ILocator? loc, AnnotationsImpl? anno);

    NameClass MakeNameClassChoice(List<NameClass> nameClasses, ILocator? loc, AnnotationsImpl? anno);

    NameClass MakeName(string ns, string localName, string? prefix, ILocator? loc, AnnotationsImpl? anno);

    NameClass MakeNsName(string ns, ILocator? loc, AnnotationsImpl? anno);

    NameClass MakeNsName(string ns, NameClass except, ILocator? loc, AnnotationsImpl? anno);

    NameClass MakeAnyName(ILocator? loc, AnnotationsImpl? anno);

    NameClass MakeAnyName(NameClass except, ILocator? loc, AnnotationsImpl? anno);

    ILocator MakeLocation(string? systemId, int lineNumber, int columnNumber);

    Pattern MakeErrorPattern();

    NameClass MakeErrorNameClass();
}
