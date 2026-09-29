/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry
{
#pragma warning disable CS8604 // Generated schemas are checked on load.
#pragma warning disable RCS1078 // JSON Schema paths use the Python empty-path convention.
    internal sealed class NativeJsonSchemaSet
    {
        public NativeJsonSchemaSet()
        {
            Load("message.schema.json", "message");
            Load("messagegroup.schema.json", "messagegroup");
            Load("message-registry.schema.json", "message-registry");
            Load("endpoint.schema.json", "endpoint");
            Load("registry.schema.json", "registry");
            Load("media-endpoint.schema.json", "media-endpoint");
            Load("media-registry.schema.json", "media-registry");
        }

        public void Validate(RegistryValueDataType value, string kind, bool media = false)
        {
            string key = kind switch
            {
                "message" => "message",
                "messagegroup" => "messagegroup",
                "message-registry" => "message-registry",
                "registry" when media => "media-registry",
                "registry" => "registry",
                "endpoint" when media => "media-endpoint",
                "endpoint" => "endpoint",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            ValidateSchema(m_schemas[key], value, string.Empty);
        }

        public JsonDocument LoadJsonDocument(string name)
        {
            using Stream stream = OpenResource(name);
            return JsonDocument.Parse(stream);
        }

        private void Load(string name, string key)
        {
            JsonDocument document = LoadJsonDocument(name);
            CheckKeywords(document.RootElement, false);
            m_schemas[key] = document.RootElement.Clone();
            if (document.RootElement.TryGetProperty("$id", out JsonElement id))
            {
                m_refs[id.GetString()!] = m_schemas[key];
            }
        }

        private void ValidateSchema(JsonElement schema, RegistryValueDataType value, string path)
        {
            if (schema.TryGetProperty("$ref", out JsonElement refValue))
            {
                string id = refValue.GetString()!;
                if (!m_refs.TryGetValue(id, out JsonElement target))
                {
                    throw new InvalidOperationException("Unresolved embedded schema reference: " + id);
                }
                ValidateSchema(target, value, path);
                return;
            }
            if (schema.TryGetProperty("type", out JsonElement type))
            {
                ValidateType(type.GetString()!, value, path);
            }
            if (schema.TryGetProperty("const", out JsonElement constant) && !JsonEqual(constant, value))
            {
                throw Schema(path, "value does not equal the required constant");
            }
            if (schema.TryGetProperty("enum", out JsonElement values) && !EnumContains(values, value))
            {
                throw Schema(path, "value is not one of the admitted values");
            }
            switch (value)
            {
                case RegistryObjectValueDataType map when value.Kind == 5:
                    ValidateObject(schema, map, path);
                    break;
                case RegistryArrayValueDataType array when value.Kind == 4:
                    ValidateArray(schema, array, path);
                    break;
                case RegistryStringValueDataType text when value.Kind == 2:
                    ValidateString(schema, text.Value, path);
                    break;
                case RegistryNumberValueDataType number when value.Kind == 3:
                    ValidateNumber(schema, number, path);
                    break;
            }
            if (schema.TryGetProperty("anyOf", out JsonElement anyOf) && !AnyValid(anyOf, value, path))
            {
                throw Schema(path, "value does not match any admitted schema");
            }
            if (schema.TryGetProperty("if", out JsonElement condition) && IsValid(condition, value, path) &&
                schema.TryGetProperty("then", out JsonElement thenSchema))
            {
                ValidateSchema(thenSchema, value, path);
            }
            if (schema.TryGetProperty("allOf", out JsonElement allOf))
            {
                foreach (JsonElement child in allOf.EnumerateArray())
                {
                    ValidateSchema(child, value, path);
                }
            }
        }

        private void ValidateObject(JsonElement schema, RegistryObjectValueDataType value, string path)
        {
            HashSet<string> declared = [];
            if (schema.TryGetProperty("required", out JsonElement required))
            {
                foreach (JsonElement item in required.EnumerateArray())
                {
                    string name = item.GetString()!;
                    if (!RegistryRuleValues.Has(value, name))
                    {
                        throw Schema(path, "a required property is missing");
                    }
                }
            }
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    declared.Add(property.Name);
                    if (RegistryRuleValues.TryGet(value, property.Name, out RegistryValueDataType? child))
                    {
                        ValidateSchema(property.Value, child!, Join(path, property.Name));
                    }
                }
            }
            if (schema.TryGetProperty("maxProperties", out JsonElement max) && value.Members.Count > max.GetInt32())
            {
                throw Schema(path, "object has too many properties");
            }
            if (schema.TryGetProperty("additionalProperties", out JsonElement additional))
            {
                foreach (KeyValuePair<string, RegistryValueDataType> member in RegistryRuleValues.Members(value))
                {
                    if (declared.Contains(member.Key))
                    {
                        continue;
                    }
                    if (additional.ValueKind == JsonValueKind.False)
                    {
                        throw Schema(Join(path, member.Key), "additional properties are not admitted");
                    }
                    if (additional.ValueKind == JsonValueKind.Object)
                    {
                        ValidateSchema(additional, member.Value, Join(path, member.Key));
                    }
                }
            }
        }

        private void ValidateArray(JsonElement schema, RegistryArrayValueDataType value, string path)
        {
            if (schema.TryGetProperty("minItems", out JsonElement min) && value.Items.Count < min.GetInt32())
            {
                throw Schema(path, "array has too few items");
            }
            if (schema.TryGetProperty("uniqueItems", out JsonElement unique) && unique.GetBoolean())
            {
                for (int i = 0; i < value.Items.Count; i++)
                {
                    for (int j = i + 1; j < value.Items.Count; j++)
                    {
                        if (RegistryRuleValues.JsonEqual(value.Items[i], value.Items[j]))
                        {
                            throw Schema(path, "array items are not unique");
                        }
                    }
                }
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                for (int index = 0; index < value.Items.Count; index++)
                {
                    ValidateSchema(items, value.Items[index], Join(path, index.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        private static void ValidateString(JsonElement schema, string value, string path)
        {
            if (schema.TryGetProperty("minLength", out JsonElement min) && value.Length < min.GetInt32())
            {
                throw Schema(path, "string is shorter than the minimum length");
            }
            if (schema.TryGetProperty("pattern", out JsonElement pattern) &&
                !RegexCache(pattern.GetString()!).IsMatch(value))
            {
                throw Schema(path, "string does not match the required pattern");
            }
        }

        private static void ValidateNumber(JsonElement schema, RegistryNumberValueDataType value, string path)
        {
            if (schema.TryGetProperty("minimum", out JsonElement min) &&
                RegistryRuleValues.CompareNumber(value, NumberFrom(min)) < 0)
            {
                throw Schema(path, "number is less than the minimum");
            }
            if (schema.TryGetProperty("maximum", out JsonElement max) &&
                RegistryRuleValues.CompareNumber(value, NumberFrom(max)) > 0)
            {
                throw Schema(path, "number is greater than the maximum");
            }
        }

        private static void ValidateType(string type, RegistryValueDataType value, string path)
        {
            bool ok = type switch
            {
                "object" => value is RegistryObjectValueDataType && value.Kind == 5,
                "array" => value is RegistryArrayValueDataType && value.Kind == 4,
                "string" => value is RegistryStringValueDataType && value.Kind == 2,
                "boolean" => value is RegistryBooleanValueDataType && value.Kind == 1,
                "number" => value is RegistryNumberValueDataType && value.Kind == 3,
                "integer" => RegistryRuleValues.IsInteger(value),
                _ => throw new InvalidOperationException("Unsupported JSON Schema type: " + type)
            };
            if (!ok)
            {
                throw Schema(path, "value has the wrong JSON type");
            }
        }

        private static bool AnyValid(JsonElement anyOf, RegistryValueDataType value, string path)
        {
            foreach (JsonElement child in anyOf.EnumerateArray())
            {
                if (s_default.IsValid(child, value, path))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsValid(JsonElement schema, RegistryValueDataType value, string path)
        {
            try
            {
                ValidateSchema(schema, value, path);
                return true;
            }
            catch (RegistryRuleException)
            {
                return false;
            }
        }

        private static bool EnumContains(JsonElement values, RegistryValueDataType value)
        {
            foreach (JsonElement item in values.EnumerateArray())
            {
                if (JsonEqual(item, value))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool JsonEqual(JsonElement expected, RegistryValueDataType actual)
        {
            switch (expected.ValueKind)
            {
                case JsonValueKind.Null:
                    return actual is RegistryNullValueDataType && actual.Kind == 0;
                case JsonValueKind.False:
                case JsonValueKind.True:
                    return actual is RegistryBooleanValueDataType flag && actual.Kind == 1 &&
                        flag.Value == expected.GetBoolean();
                case JsonValueKind.String:
                    return actual is RegistryStringValueDataType text && actual.Kind == 2 &&
                        string.Equals(text.Value, expected.GetString(), StringComparison.Ordinal);
                case JsonValueKind.Number:
                    return actual is RegistryNumberValueDataType number &&
                        RegistryRuleValues.CompareNumber(number, NumberFrom(expected)) == 0;
                case JsonValueKind.Array:
                    if (actual is not RegistryArrayValueDataType array || array.Items.Count != expected.GetArrayLength())
                    {
                        return false;
                    }
                    int index = 0;
                    foreach (JsonElement item in expected.EnumerateArray())
                    {
                        if (!JsonEqual(item, array.Items[index++]))
                        {
                            return false;
                        }
                    }
                    return true;
                case JsonValueKind.Object:
                    if (actual is not RegistryObjectValueDataType map || map.Members.Count != expected.EnumerateObject().Count())
                    {
                        return false;
                    }
                    foreach (JsonProperty member in expected.EnumerateObject())
                    {
                        if (!RegistryRuleValues.TryGet(map, member.Name, out RegistryValueDataType? child) ||
                            !JsonEqual(member.Value, child!))
                        {
                            return false;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }

        private static RegistryNumberValueDataType NumberFrom(JsonElement value)
        {
            return (RegistryNumberValueDataType)RegistryValues.Parse(Encoding.UTF8.GetBytes(value.GetRawText()));
        }

        private static RegistryRuleException Schema(string path, string detail)
        {
            return RegistryRuleException.Fail("E_SCHEMA", string.IsNullOrEmpty(path) ? "/" : "/" + path, detail);
        }

        private static string Join(string path, string part)
        {
            return string.IsNullOrEmpty(path) ? part : path + "/" + part;
        }

        private static Regex RegexCache(string pattern)
        {
            lock (s_regex)
            {
                if (!s_regex.TryGetValue(pattern, out Regex? regex))
                {
                    regex = new Regex(pattern, RegexOptions.CultureInvariant);
                    s_regex.Add(pattern, regex);
                }
                return regex;
            }
        }

        private static Stream OpenResource(string name)
        {
            string suffix = ".RuleSchemas." + name;
            Assembly assembly = typeof(NativeJsonSchemaSet).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (resource.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return assembly.GetManifestResourceStream(resource) ??
                        throw new InvalidOperationException("Cannot open embedded resource " + resource);
                }
            }
            throw new InvalidOperationException("Cannot find embedded rule resource " + name);
        }

        private static void CheckKeywords(JsonElement schema, bool propertyMap)
        {
            if (schema.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in schema.EnumerateObject())
                {
                    if (propertyMap)
                    {
                        CheckKeywords(property.Value, false);
                        continue;
                    }
                    if (!s_keywords.Contains(property.Name))
                    {
                        throw new InvalidOperationException("Unsupported JSON Schema keyword: " + property.Name);
                    }
                    CheckKeywords(property.Value, property.Name == "properties");
                }
            }
            else if (schema.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in schema.EnumerateArray())
                {
                    CheckKeywords(item, false);
                }
            }
        }

        private readonly Dictionary<string, JsonElement> m_schemas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> m_refs = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Regex> s_regex = new(StringComparer.Ordinal);
        private static readonly HashSet<string> s_keywords = new(StringComparer.Ordinal)
        {
            "$id", "$ref", "$schema", "additionalProperties", "allOf", "anyOf", "const", "default",
            "enum", "if", "items", "maxProperties", "maximum", "minItems", "minLength", "minimum",
            "pattern", "properties", "required", "then", "type", "uniqueItems"
        };
        private static readonly NativeJsonSchemaSet s_default = new();
    }
}
