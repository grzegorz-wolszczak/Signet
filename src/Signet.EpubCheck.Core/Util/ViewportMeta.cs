using System;
using System.Collections.Generic;
using System.Globalization;

namespace Signet.EpubCheck.Core.Util;

/// <summary>Parses the HTML viewport meta microsyntax.</summary>
public sealed class ViewportMeta
{
    private ViewportMeta(IReadOnlyDictionary<string, IReadOnlyList<string>> properties) => Properties = properties;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Properties { get; }
    public bool HasProperty(string name) => Properties.ContainsKey(name);
    public IReadOnlyList<string> GetValues(string name) => Properties.TryGetValue(name, out IReadOnlyList<string>? values) ? values : Array.Empty<string>();

    public static ViewportMeta Parse(string? value, Action<ViewportMetaParseError, int>? errorHandler = null)
    {
        Dictionary<string, List<string>> mutable = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(value))
        {
            errorHandler?.Invoke(ViewportMetaParseError.NullOrEmpty, -1);
            return New(mutable);
        }

        int offset = 0;
        foreach (string part in value.Split([',', ';']))
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                errorHandler?.Invoke(offset == 0 ? ViewportMetaParseError.LeadingSeparator : ViewportMetaParseError.TrailingSeparator, offset);
                offset += part.Length + 1;
                continue;
            }

            int equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                errorHandler?.Invoke(equals == 0 ? ViewportMetaParseError.NameEmpty : ViewportMetaParseError.ValueEmpty, offset);
                return New(mutable);
            }

            string name = trimmed[..equals].Trim();
            string propertyValue = trimmed[(equals + 1)..].Trim();
            if (propertyValue.Length == 0 || propertyValue.Contains('='))
            {
                errorHandler?.Invoke(propertyValue.Length == 0 ? ViewportMetaParseError.ValueEmpty : ViewportMetaParseError.AssignUnexpected, offset + equals + 1);
                return New(mutable);
            }

            if (!mutable.TryGetValue(name, out List<string>? values))
            {
                values = [];
                mutable.Add(name, values);
            }

            values.Add(propertyValue);
            offset += part.Length + 1;
        }

        return New(mutable);
    }

    public static bool IsValidProperty(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return name switch
        {
            "width" => value == "device-width" || IsPositiveDecimal(value),
            "height" => value == "device-height" || IsPositiveDecimal(value),
            _ => true,
        };
    }

    private static bool IsPositiveDecimal(string value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _) && !value.StartsWith('.');
    private static ViewportMeta New(Dictionary<string, List<string>> values)
    {
        Dictionary<string, IReadOnlyList<string>> copied = new(StringComparer.Ordinal);
        foreach ((string key, List<string> value) in values)
        {
            copied.Add(key, value.AsReadOnly());
        }

        return new ViewportMeta(copied);
    }
}

public enum ViewportMetaParseError { NullOrEmpty, NameEmpty, ValueEmpty, LeadingSeparator, TrailingSeparator, AssignUnexpected }
