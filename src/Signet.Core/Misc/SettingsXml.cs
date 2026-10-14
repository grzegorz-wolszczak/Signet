using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Signet.Core.Misc;

/// <summary>
/// The XML form of the settings file: the two-level settings object (<c>group → key → value</c>, see
/// <see cref="SettingsFile"/>) with every value as a typed element.
/// </summary>
/// <remarks>
/// <code>
/// &lt;settings&gt;
///   &lt;group name="user_preferences"&gt;
///     &lt;string key="ui_language"&gt;en&lt;/string&gt;
///     &lt;number key="zoom_text"&gt;1.2&lt;/number&gt;
///     &lt;bool key="spellcheck"&gt;false&lt;/bool&gt;
///     &lt;array key="recentfiles"&gt;&lt;string&gt;/books/a.epub&lt;/string&gt;&lt;/array&gt;
///     &lt;object key="geometry"&gt;&lt;number key="x"&gt;10&lt;/number&gt;&lt;/object&gt;
///   &lt;/group&gt;
/// &lt;/settings&gt;
/// </code>
/// Values inside an <c>array</c> have no <c>key</c>; <c>&lt;null/&gt;</c> is a null value. Unknown elements and
/// unreadable values are skipped when reading (like a missing setting).
/// </remarks>
internal static class SettingsXml
{
    private const string RootElement = "settings";
    private const string GroupElement = "group";
    private const string NameAttribute = "name";
    private const string KeyAttribute = "key";
    private const string StringElement = "string";
    private const string NumberElement = "number";
    private const string BoolElement = "bool";
    private const string NullElement = "null";
    private const string ArrayElement = "array";
    private const string ObjectElement = "object";

    /// <summary>The settings document of the two-level settings object.</summary>
    public static XDocument ToDocument(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        XElement settings = new(RootElement);
        foreach (KeyValuePair<string, JsonNode?> group in root)
        {
            if (group.Value is JsonObject values)
            {
                XElement groupElement = new(GroupElement, new XAttribute(NameAttribute, group.Key));
                foreach (KeyValuePair<string, JsonNode?> pair in values)
                {
                    groupElement.Add(ToElement(pair.Value, pair.Key));
                }

                settings.Add(groupElement);
            }
        }

        return new XDocument(settings);
    }

    /// <summary>The two-level settings object of a settings document (empty for <see langword="null"/> or another document).</summary>
    public static JsonObject FromDocument(XDocument? document)
    {
        JsonObject root = new();
        if (document?.Root is not { } settings || settings.Name != RootElement)
        {
            return root;
        }

        foreach (XElement groupElement in settings.Elements(GroupElement))
        {
            string name = groupElement.Attribute(NameAttribute)?.Value ?? string.Empty;
            JsonObject values = root[name] as JsonObject ?? new JsonObject();
            ReadMembers(groupElement, values);
            root[name] = values;
        }

        return root;
    }

    private static XElement ToElement(JsonNode? node, string? key)
    {
        XElement element = node switch
        {
            null => new XElement(NullElement),
            JsonObject obj => new XElement(ObjectElement, obj.Select(pair => ToElement(pair.Value, pair.Key))),
            JsonArray array => new XElement(ArrayElement, array.Select(item => ToElement(item, null))),
            JsonValue value => value.GetValueKind() switch
            {
                JsonValueKind.String => new XElement(StringElement, value.GetValue<string>()),
                JsonValueKind.Number => new XElement(NumberElement, value.ToJsonString()),
                JsonValueKind.True => new XElement(BoolElement, "true"),
                JsonValueKind.False => new XElement(BoolElement, "false"),
                _ => new XElement(NullElement),
            },
            _ => new XElement(NullElement),
        };

        if (key is not null)
        {
            element.SetAttributeValue(KeyAttribute, key);
        }

        return element;
    }

    private static void ReadMembers(XElement container, JsonObject into)
    {
        foreach (XElement element in container.Elements())
        {
            if (element.Attribute(KeyAttribute)?.Value is { } key && TryRead(element, out JsonNode? value))
            {
                into[key] = value;
            }
        }
    }

    private static bool TryRead(XElement element, out JsonNode? value)
    {
        value = null;
        switch (element.Name.LocalName)
        {
            case StringElement:
                value = JsonValue.Create(element.Value);
                return true;
            case NumberElement:
                // Kept as a JSON number, so it converts to int/long/double exactly like a number set in memory.
                try
                {
                    value = JsonNode.Parse(element.Value.Trim());
                    return value is JsonValue number && number.GetValueKind() == JsonValueKind.Number;
                }
                catch (JsonException)
                {
                    return false;
                }
            case BoolElement:
                if (bool.TryParse(element.Value.Trim(), out bool flag))
                {
                    value = JsonValue.Create(flag);
                    return true;
                }

                return false;
            case NullElement:
                return true;
            case ArrayElement:
                JsonArray array = new();
                foreach (XElement item in element.Elements())
                {
                    if (TryRead(item, out JsonNode? itemValue))
                    {
                        array.Add(itemValue);
                    }
                }

                value = array;
                return true;
            case ObjectElement:
                JsonObject obj = new();
                ReadMembers(element, obj);
                value = obj;
                return true;
            default:
                return false;
        }
    }
}
