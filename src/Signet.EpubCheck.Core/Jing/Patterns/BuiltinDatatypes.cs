using System;
using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Datatype;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>The built-in <c>string</c> datatype of RELAX NG (Jing's <c>StringDatatype</c>): every string, compared as it is.</summary>
internal class StringDatatype : IDatatype2
{
    public bool AlwaysValid => true;

    public IdType IdType => IdType.Null;

    public bool IsContextDependent => false;

    public bool IsValid(string literal, IValidationContext context) => true;

    public void CheckValid(string literal, IValidationContext context)
    {
        if (!IsValid(literal, context))
        {
            throw new DatatypeException();
        }
    }

    public virtual object? CreateValue(string literal, IValidationContext context) => literal;

    public bool SameValue(object value1, object value2) => value1.Equals(value2);

    public int ValueHashCode(object value) => value is string s ? StringComparer.Ordinal.GetHashCode(s) : value.GetHashCode();

    public IDatatypeStreamingValidator CreateStreamingValidator(IValidationContext context) => new BufferingStreamingValidator(this, context);
}

/// <summary>The built-in <c>token</c> datatype (Jing's <c>TokenDatatype</c>): every string, compared with its white space collapsed.</summary>
internal sealed class TokenDatatype : StringDatatype
{
    // java.util.StringTokenizer's default delimiters.
    private static readonly char[] s_delimiters = [' ', '\t', '\n', '\r', '\f'];

    public override object? CreateValue(string literal, IValidationContext context) => Normalize(literal);

    internal static string Normalize(string s) =>
        string.Join(' ', s.Split(s_delimiters, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The builder of a built-in datatype: no parameter (Jing's <c>BuiltinDatatypeBuilder</c>).</summary>
internal sealed class BuiltinDatatypeBuilder(IDatatype dt, JingLocalizer localizer) : IDatatypeBuilder
{
    public void AddParameter(string name, string value, IValidationContext context) =>
        throw new DatatypeException(localizer.Message("builtin_param"));

    public IDatatype CreateDatatype() => dt;
}

/// <summary>The datatype library of the empty URI: <c>string</c> and <c>token</c> (Jing's <c>BuiltinDatatypeLibrary</c>).</summary>
internal sealed class BuiltinDatatypeLibrary(JingLocalizer localizer) : IDatatypeLibrary
{
    private const string TokenType = "token";
    private const string StringType = "string";
    private readonly BuiltinDatatypeBuilder _tokenDatatypeBuilder = new(new TokenDatatype(), localizer);
    private readonly BuiltinDatatypeBuilder _stringDatatypeBuilder = new(new StringDatatype(), localizer);

    public IDatatypeBuilder CreateDatatypeBuilder(string baseTypeLocalName) => baseTypeLocalName switch
    {
        TokenType => _tokenDatatypeBuilder,
        StringType => _stringDatatypeBuilder,
        _ => throw new DatatypeException(),
    };

    public IDatatype CreateDatatype(string typeLocalName) => CreateDatatypeBuilder(typeLocalName).CreateDatatype();
}

/// <summary>
/// The datatype libraries of a schema (Jing's <c>BuiltinDatatypeLibraryFactory</c>): the built-in one for the empty URI,
/// the DTD compatibility one, and those of the given factory, each made once.
/// </summary>
internal sealed class BuiltinDatatypeLibraryFactory : IDatatypeLibraryFactory
{
    private readonly Dictionary<string, IDatatypeLibrary> _cache = new(StringComparer.Ordinal);
    private readonly IDatatypeLibraryFactory? _factory;
    private readonly BuiltinDatatypeLibrary _builtinDatatypeLibrary;

    public BuiltinDatatypeLibraryFactory(IDatatypeLibraryFactory? factory, JingLocalizer localizer)
    {
        _factory = factory;
        _builtinDatatypeLibrary = new BuiltinDatatypeLibrary(localizer);
        _cache[WellKnownNamespaces.RelaxNgCompatibilityDatatypes] = new CompatibilityDatatypeLibrary(this);
    }

    public IDatatypeLibrary? CreateDatatypeLibrary(string namespaceUri)
    {
        if (namespaceUri.Length == 0)
        {
            return _builtinDatatypeLibrary;
        }

        if (_cache.TryGetValue(namespaceUri, out IDatatypeLibrary? library))
        {
            return library;
        }

        library = _factory?.CreateDatatypeLibrary(namespaceUri);
        if (library is not null)
        {
            _cache[namespaceUri] = library;
        }

        return library;
    }
}

/// <summary>The RELAX NG DTD compatibility datatypes: <c>ID</c>, <c>IDREF</c>, <c>IDREFS</c> of the XML Schema library (Jing's <c>CompatibilityDatatypeLibrary</c>).</summary>
internal sealed class CompatibilityDatatypeLibrary(IDatatypeLibraryFactory factory) : IDatatypeLibrary
{
    private IDatatypeLibrary? _xsdDatatypeLibrary;

    public IDatatypeBuilder CreateDatatypeBuilder(string baseTypeLocalName)
    {
        if (baseTypeLocalName is "ID" or "IDREF" or "IDREFS")
        {
            _xsdDatatypeLibrary ??= factory.CreateDatatypeLibrary(WellKnownNamespaces.XmlSchemaDatatypes)
                ?? throw new DatatypeException();
            return _xsdDatatypeLibrary.CreateDatatypeBuilder(baseTypeLocalName);
        }

        throw new DatatypeException();
    }

    public IDatatype CreateDatatype(string typeLocalName) => CreateDatatypeBuilder(typeLocalName).CreateDatatype();
}
