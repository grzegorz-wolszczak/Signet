using System;
using System.Text;

namespace Signet.EpubCheck.Core.Jing.Datatype;

/// <summary>The kind of identifier a datatype is (RELAX NG DTD compatibility).</summary>
public enum IdType
{
    /// <summary>Not an identifier.</summary>
    Null = 0,

    /// <summary>An <c>ID</c>.</summary>
    Id = 1,

    /// <summary>An <c>IDREF</c>.</summary>
    IdRef = 2,

    /// <summary>An <c>IDREFS</c>.</summary>
    IdRefs = 3,
}

/// <summary>What a datatype needs to know about where a value is (<c>org.relaxng.datatype.ValidationContext</c>).</summary>
public interface IValidationContext
{
    /// <summary>The namespace URI bound to <paramref name="prefix"/> (the empty prefix is the default namespace), or <c>null</c>.</summary>
    string? ResolveNamespacePrefix(string prefix);

    /// <summary>The base URI, or <c>null</c>.</summary>
    string? BaseUri { get; }

    /// <summary>Whether the DTD declared an unparsed entity of that name.</summary>
    bool IsUnparsedEntity(string entityName);

    /// <summary>Whether the DTD declared a notation of that name.</summary>
    bool IsNotation(string notationName);
}

/// <summary>A datatype of a datatype library (<c>org.relaxng.datatype.Datatype</c>).</summary>
public interface IDatatype
{
    /// <summary>Whether <paramref name="literal"/> is a value of the datatype.</summary>
    bool IsValid(string literal, IValidationContext context);

    /// <summary>Throws when <paramref name="literal"/> is not a value of the datatype, saying why.</summary>
    /// <exception cref="DatatypeException">It is not.</exception>
    void CheckValid(string literal, IValidationContext context);

    /// <summary>A validator that takes the literal in pieces.</summary>
    IDatatypeStreamingValidator CreateStreamingValidator(IValidationContext context);

    /// <summary>The value <paramref name="literal"/> stands for, or <c>null</c> when it is not a value of the datatype.</summary>
    object? CreateValue(string literal, IValidationContext context);

    /// <summary>Whether two values made by <see cref="CreateValue"/> are the same.</summary>
    bool SameValue(object value1, object value2);

    /// <summary>A hash code consistent with <see cref="SameValue"/>.</summary>
    int ValueHashCode(object value);

    /// <summary>The identifier kind.</summary>
    IdType IdType { get; }

    /// <summary>Whether the value depends on the context (e.g. a QName).</summary>
    bool IsContextDependent { get; }
}

/// <summary>A datatype that can say it accepts every string (Jing's <c>Datatype2</c>).</summary>
public interface IDatatype2 : IDatatype
{
    /// <summary>Whether every string is a value.</summary>
    bool AlwaysValid { get; }
}

/// <summary>Builds a datatype from a base type and parameters (<c>org.relaxng.datatype.DatatypeBuilder</c>).</summary>
public interface IDatatypeBuilder
{
    /// <summary>Adds a parameter.</summary>
    /// <exception cref="DatatypeException">The parameter is not valid.</exception>
    void AddParameter(string name, string value, IValidationContext context);

    /// <summary>The datatype.</summary>
    /// <exception cref="DatatypeException">The parameters do not make a datatype.</exception>
    IDatatype CreateDatatype();
}

/// <summary>A datatype library (<c>org.relaxng.datatype.DatatypeLibrary</c>).</summary>
public interface IDatatypeLibrary
{
    /// <summary>A builder for a datatype derived from <paramref name="baseTypeLocalName"/>.</summary>
    /// <exception cref="DatatypeException">The library has no such type.</exception>
    IDatatypeBuilder CreateDatatypeBuilder(string baseTypeLocalName);

    /// <summary>The datatype <paramref name="typeLocalName"/> without parameters.</summary>
    /// <exception cref="DatatypeException">The library has no such type.</exception>
    IDatatype CreateDatatype(string typeLocalName);
}

/// <summary>Finds the datatype library of a namespace URI (<c>org.relaxng.datatype.DatatypeLibraryFactory</c>).</summary>
public interface IDatatypeLibraryFactory
{
    /// <summary>The library, or <c>null</c> when the factory does not know the URI.</summary>
    IDatatypeLibrary? CreateDatatypeLibrary(string namespaceUri);
}

/// <summary>Validates a literal given in pieces (<c>org.relaxng.datatype.DatatypeStreamingValidator</c>).</summary>
public interface IDatatypeStreamingValidator
{
    /// <summary>Adds a piece of the literal.</summary>
    void AddCharacters(string text);

    /// <summary>Whether the literal so far is valid.</summary>
    bool IsValid();

    /// <summary>Throws when the literal so far is not valid.</summary>
    /// <exception cref="DatatypeException">It is not.</exception>
    void CheckValid();
}

/// <summary>Why a literal or a parameter is not valid (<c>org.relaxng.datatype.DatatypeException</c>).</summary>
public class DatatypeException : Exception
{
    /// <summary>The index of an unknown position.</summary>
    public const int Unknown = -1;

    /// <summary>Creates the exception, with no message.</summary>
    public DatatypeException()
        : this(Unknown, null)
    {
    }

    /// <summary>Creates the exception.</summary>
    public DatatypeException(string? message)
        : this(Unknown, message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public DatatypeException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Index = Unknown;
        HasMessage = message is not null;
    }

    /// <summary>Creates the exception for the character at <paramref name="index"/>.</summary>
    public DatatypeException(int index, string? message)
        : base(message)
    {
        Index = index;
        HasMessage = message is not null;
    }

    /// <summary>The index in the literal where the problem is, or <see cref="Unknown"/>.</summary>
    public int Index { get; }

    /// <summary>The message, or <c>null</c> when the exception was made without one (Java's <c>getMessage()</c>).</summary>
    public string? Detail => HasMessage ? Message : null;

    private bool HasMessage { get; }
}

/// <summary>A builder for a datatype that takes no parameter (<c>org.relaxng.datatype.helpers.ParameterlessDatatypeBuilder</c>).</summary>
public sealed class ParameterlessDatatypeBuilder(IDatatype baseType) : IDatatypeBuilder
{
    /// <inheritdoc />
    public void AddParameter(string name, string value, IValidationContext context) => throw new DatatypeException();

    /// <inheritdoc />
    public IDatatype CreateDatatype() => baseType;
}

/// <summary>A streaming validator that collects the pieces and checks the whole (<c>org.relaxng.datatype.helpers.BufferingStreamingValidator</c>).</summary>
public sealed class BufferingStreamingValidator(IDatatype baseType, IValidationContext context) : IDatatypeStreamingValidator
{
    private readonly StringBuilder _buffer = new();

    /// <inheritdoc />
    public void AddCharacters(string text) => _buffer.Append(text);

    /// <inheritdoc />
    public bool IsValid() => baseType.IsValid(_buffer.ToString(), context);

    /// <inheritdoc />
    public void CheckValid() => baseType.CheckValid(_buffer.ToString(), context);
}
