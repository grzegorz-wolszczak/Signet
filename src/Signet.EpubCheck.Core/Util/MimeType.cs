using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Signet.EpubCheck.Core.Util;

/// <summary>A MIME type parsed according to the MIME Sniffing standard.</summary>
public sealed class MimeType : IEquatable<MimeType>
{
    private MimeType(string type, string subtype, IReadOnlyDictionary<string, string> parameters)
    {
        Type = type;
        Subtype = subtype;
        Parameters = parameters;
    }

    public string Type { get; }
    public string Subtype { get; }
    public string Essence => $"{Type}/{Subtype}";
    public IReadOnlyDictionary<string, string> Parameters { get; }

    public static MimeType Of(string type, string subtype) =>
        new(type ?? throw new ArgumentNullException(nameof(type)), subtype ?? throw new ArgumentNullException(nameof(subtype)), new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)));

    public static MimeType? Parse(string? value, IMimeTypeErrorHandler? errorHandler = null)
    {
        if (value is null)
        {
            errorHandler?.HandleFailure(MimeTypeParseError.Null, -1);
            return null;
        }

        int index = 0;
        SkipWhitespace(value, ref index);
        string? type = ReadToken(value, ref index, errorHandler);
        if (type is null || index >= value.Length || value[index] != '/')
        {
            errorHandler?.HandleFailure(MimeTypeParseError.NoType, index);
            return null;
        }

        index++;
        string? subtype = ReadToken(value, ref index, errorHandler);
        if (subtype is null)
        {
            errorHandler?.HandleFailure(MimeTypeParseError.NoSubtype, index);
            return null;
        }

        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        SkipWhitespace(value, ref index);
        while (index < value.Length)
        {
            if (value[index] != ';')
            {
                errorHandler?.HandleFailure(MimeTypeParseError.IllegalCodePoint, index);
                return null;
            }

            index++;
            SkipWhitespace(value, ref index);
            int namePosition = index;
            string? name = ReadToken(value, ref index, null);
            if (name is null || index >= value.Length || value[index] != '=')
            {
                errorHandler?.HandleError(name is null ? MimeTypeParseError.NoParamName : MimeTypeParseError.NoParamValue, index);
                SkipParameter(value, ref index);
                continue;
            }

            index++;
            string parameterValue = ReadParameterValue(value, ref index, errorHandler);
            if (namePosition != index && !parameters.ContainsKey(name))
            {
                parameters.Add(name, parameterValue);
            }

            SkipWhitespace(value, ref index);
        }

        return new MimeType(type, subtype, new ReadOnlyDictionary<string, string>(parameters));
    }

    public static string Normalize(string? value) => Parse(value)?.ToString() ?? string.Empty;

    public MimeType FilterParameters(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string name in parameterNames)
        {
            if (!string.IsNullOrEmpty(name) && Parameters.TryGetValue(name, out string? value))
            {
                result.TryAdd(name, value);
            }
        }

        return new MimeType(Type, Subtype, new ReadOnlyDictionary<string, string>(result));
    }

    public override string ToString()
    {
        StringBuilder builder = new(Essence);
        foreach ((string name, string value) in Parameters)
        {
            builder.Append(';').Append(name).Append('=');
            bool quote = value.Length == 0;
            foreach (char character in value)
            {
                if (!CodePoints.IsHttpTokenCodePoint(character))
                {
                    quote = true;
                    break;
                }
            }

            if (quote)
            {
                builder.Append('"');
                foreach (char character in value)
                {
                    if (character is '"' or '\\')
                    {
                        builder.Append('\\');
                    }

                    builder.Append(character);
                }

                builder.Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }

        return builder.ToString();
    }

    public bool Equals(MimeType? other) => other is not null && Type == other.Type && Subtype == other.Subtype && Parameters.Count == other.Parameters.Count && ToString() == other.ToString();
    public override bool Equals(object? obj) => obj is MimeType other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Type, Subtype, ToString());

    private static string? ReadToken(string value, ref int index, IMimeTypeErrorHandler? handler)
    {
        int start = index;
        while (index < value.Length && CodePoints.IsHttpTokenCodePoint(value[index]))
        {
            index++;
        }

        if (index == start)
        {
            return null;
        }

        if (index < value.Length && value[index] is not '/' and not ';' && !CodePoints.IsHttpWhitespace(value[index]) && value[index] != '=')
        {
            handler?.HandleFailure(MimeTypeParseError.IllegalCodePoint, index);
            return null;
        }

        return value[start..index].ToLowerInvariant();
    }

    private static string ReadParameterValue(string value, ref int index, IMimeTypeErrorHandler? handler)
    {
        if (index < value.Length && value[index] == '"')
        {
            index++;
            StringBuilder quoted = new();
            while (index < value.Length && value[index] != '"')
            {
                if (value[index] == '\\' && index + 1 < value.Length)
                {
                    index++;
                }

                if (!CodePoints.IsHttpQuotedStringTokenCodePoint(value[index]))
                {
                    handler?.HandleError(MimeTypeParseError.UnexpectedCodePoint, index);
                    SkipParameter(value, ref index);
                    return quoted.ToString();
                }

                quoted.Append(value[index++]);
            }

            if (index == value.Length)
            {
                handler?.HandleError(MimeTypeParseError.EndQuoteMissing, index);
                return quoted.ToString();
            }

            index++;
            return quoted.ToString();
        }

        int start = index;
        while (index < value.Length && value[index] != ';' && !CodePoints.IsHttpWhitespace(value[index]))
        {
            index++;
        }

        string result = value[start..index];
        if (result.Length == 0)
        {
            handler?.HandleError(MimeTypeParseError.NoParamValue, index);
        }

        return result;
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && CodePoints.IsHttpWhitespace(value[index]))
        {
            index++;
        }
    }

    private static void SkipParameter(string value, ref int index)
    {
        while (index < value.Length && value[index] != ';')
        {
            index++;
        }
    }
}

public enum MimeTypeParseError { Null, NoType, NoSubtype, IllegalCodePoint, UnexpectedCodePoint, NoParamName, NoParamValue, EndQuoteMissing }

public interface IMimeTypeErrorHandler
{
    void HandleFailure(MimeTypeParseError parseError, int position);
    void HandleError(MimeTypeParseError parseError, int position);
}
