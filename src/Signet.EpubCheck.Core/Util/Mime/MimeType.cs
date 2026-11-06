using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Util.Mime;

/// <summary>
/// A MIME type as the WHATWG MIME Sniffing standard defines it (upstream's <c>org.w3c.epubcheck.util.mime.MIMEType</c>):
/// a type, a subtype and parameters in the order they were given. Parse one with <see cref="MimeTypeParser"/>.
/// </summary>
public sealed class MimeType : IEquatable<MimeType>
{
    private string? _serialization;

    private MimeType(string type, string subtype, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(subtype);
        Type = type;
        Subtype = subtype;
        Parameters = new ReadOnlyCollection<KeyValuePair<string, string>>(parameters.ToArray());
    }

    /// <summary>The type, lower case.</summary>
    public string Type { get; }

    /// <summary>The subtype, lower case.</summary>
    public string Subtype { get; }

    /// <summary>The type and the subtype: <c>type/subtype</c>.</summary>
    public string Essence => Type + "/" + Subtype;

    /// <summary>The parameters (names lower case), in their order; each name once.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    /// <summary>A MIME type without parameters.</summary>
    public static MimeType Of(string type, string subtype) => new Builder().WithType(type).WithSubtype(subtype).Build();

    /// <summary>Parses <paramref name="value"/>; <c>null</c> when it is not a MIME type.</summary>
    public static MimeType? Parse(string? value) => new MimeTypeParser().Parse(value);

    /// <summary>The serialization of the parsed <paramref name="value"/>, or the empty string when it is not a MIME type.</summary>
    public static string Normalize(string? value) => Parse(value)?.ToString() ?? string.Empty;

    /// <summary>The value of the parameter <paramref name="name"/>, or <c>null</c>.</summary>
    public string? GetParameter(string name)
    {
        foreach ((string key, string value) in Parameters)
        {
            if (key == name)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>This MIME type with only the parameters named in <paramref name="parameterNames"/>.</summary>
    public MimeType FilterParameters(params string?[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        if (Parameters.Count == 0)
        {
            return this;
        }

        HashSet<string> keys = parameterNames.Where(name => !string.IsNullOrEmpty(name)).Select(name => name!).ToHashSet(StringComparer.Ordinal);
        Builder builder = new Builder().WithType(Type).WithSubtype(Subtype);
        foreach ((string key, string value) in Parameters.Where(parameter => keys.Contains(parameter.Key)))
        {
            builder.WithParameter(key, value);
        }

        return builder.Build();
    }

    /// <summary>The serialization of the MIME type (<see cref="MimeTypeSerializer"/>).</summary>
    public override string ToString() => _serialization ??= MimeTypeSerializer.Serialize(this);

    /// <inheritdoc />
    public bool Equals(MimeType? other) =>
        other is not null && Type == other.Type && Subtype == other.Subtype && Parameters.SequenceEqual(other.Parameters);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as MimeType);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Type);
        hash.Add(Subtype);
        foreach (KeyValuePair<string, string> parameter in Parameters)
        {
            hash.Add(parameter);
        }

        return hash.ToHashCode();
    }

    /// <summary>Builds a <see cref="MimeType"/>; the first value of a repeated parameter is kept.</summary>
    internal sealed class Builder
    {
        private readonly List<KeyValuePair<string, string>> _parameters = [];
        private string? _type;
        private string? _subtype;

        public Builder WithType(string type)
        {
            _type = type;
            return this;
        }

        public Builder WithSubtype(string subtype)
        {
            _subtype = subtype ?? throw new ArgumentNullException(nameof(subtype));
            return this;
        }

        public Builder WithParameter(string name, string value)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(value);
            if (!_parameters.Any(parameter => parameter.Key == name))
            {
                _parameters.Add(new KeyValuePair<string, string>(name, value));
            }

            return this;
        }

        public MimeType Build() => new(
            _type ?? throw new InvalidOperationException("The type is not set."),
            _subtype ?? throw new InvalidOperationException("The subtype is not set."),
            _parameters);
    }
}

/// <summary>Serializes a <see cref="MimeType"/> (upstream's <c>MIMETypeSerializer</c>, after the MIME Sniffing standard).</summary>
public static class MimeTypeSerializer
{
    /// <summary>The serialization of <paramref name="mimeType"/>; the empty string for <c>null</c>.</summary>
    public static string Serialize(MimeType? mimeType)
    {
        if (mimeType is null)
        {
            return string.Empty;
        }

        if (mimeType.Parameters.Count == 0)
        {
            return mimeType.Essence;
        }

        StringBuilder result = new(mimeType.Essence);
        foreach ((string name, string value) in mimeType.Parameters)
        {
            result.Append(';').Append(name).Append('=');
            if (value.Length == 0)
            {
                result.Append("\"\"");
            }
            else if (value.EnumerateRunes().Any(rune => !CodePoints.IsHttpTokenCodePoint(rune.Value)))
            {
                result.Append('"');
                foreach (char c in value)
                {
                    if (c is '"' or '\\')
                    {
                        result.Append('\\');
                    }

                    result.Append(c);
                }

                result.Append('"');
            }
            else
            {
                result.Append(value);
            }
        }

        return result.ToString();
    }
}

/// <summary>Groups of MIME types (the part of upstream's <c>MIMETypeGroups</c> that upstream implements).</summary>
public static class MimeTypeGroups
{
    /// <summary>Whether the type is <c>image</c>.</summary>
    public static bool IsImage(MimeType mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        return mimeType.Type == "image";
    }

    /// <summary>Whether the type is <c>audio</c> or <c>video</c>, or the essence is <c>application/ogg</c>.</summary>
    public static bool IsAudioOrVideo(MimeType mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        return mimeType.Type is "audio" or "video" || mimeType.Essence == "application/ogg";
    }

    /// <summary>Whether the essence is a ZIP, RAR or GZIP archive.</summary>
    public static bool IsArchive(MimeType mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        return mimeType.Essence is "application/x-rar-compressed" or "application/zip" or "application/x-gzip";
    }
}
