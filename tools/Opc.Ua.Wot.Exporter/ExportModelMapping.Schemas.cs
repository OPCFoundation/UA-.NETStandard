// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot.Exporter
{
    internal sealed partial class ExportModelMapping
    {
        /// <summary>
        /// Enriches root-level value schemas without treating interaction maps as value objects.
        /// </summary>
        private void VisitRootSchemas(JsonObject root, string href, List<string> diagnostics)
        {
            foreach (string member in s_schemaMembers)
            {
                if (root[member] is JsonObject schema)
                {
                    ApplySchema(schema, "/" + member, false, href, diagnostics);
                }
            }
            foreach (string map in s_schemaMaps)
            {
                if (root[map] is JsonObject schemas)
                {
                    foreach ((string key, JsonNode? value) in schemas)
                    {
                        if (value is JsonObject schema)
                        {
                            ApplySchema(schema, "/" + map + "/" + Escape(key), false, href, diagnostics);
                        }
                    }
                }
            }
            VisitSchemaArrays(root, string.Empty, href, diagnostics);
        }

        /// <summary>
        /// Follows ordinary schema positions and retains exact diagnostic locations.
        /// </summary>
        private void VisitSchemas(JsonObject parent, string pointer, string href, List<string> diagnostics)
        {
            foreach (string member in s_schemaMembers)
            {
                if (parent[member] is JsonObject schema)
                {
                    ApplySchema(schema, pointer + "/" + member, false, href, diagnostics);
                }
            }
            foreach (string map in s_schemaMaps.Prepend("properties"))
            {
                if (parent[map] is not JsonObject properties)
                {
                    continue;
                }
                foreach ((string key, JsonNode? value) in properties)
                {
                    if (value is JsonObject schema)
                    {
                        ApplySchema(schema, pointer + "/" + map + "/" + Escape(key), false, href, diagnostics);
                    }
                }
            }
            VisitSchemaArrays(parent, pointer, href, diagnostics);
        }

        /// <summary>
        /// Enriches schema alternatives and schemas carried by forms or responses.
        /// </summary>
        private void VisitSchemaArrays(JsonObject parent, string pointer, string href, List<string> diagnostics)
        {
            foreach (string member in s_schemaArrays)
            {
                if (parent[member] is JsonArray schemas)
                {
                    for (int i = 0; i < schemas.Count; i++)
                    {
                        if (schemas[i] is JsonObject schema)
                        {
                            ApplySchema(schema, pointer + "/" + member + "/" +
                                i.ToString(CultureInfo.InvariantCulture), false, href, diagnostics);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a native type's ordinary shape and, for concrete integer properties, storage bounds.
        /// </summary>
        private void ApplySchema(
            JsonObject schema, string pointer, bool integerProperty, string href, List<string> diagnostics)
        {
            if (schema.ContainsKey("uav:engineeringUnits"))
            {
                schema["uav:typeName"] = Name(String(schema, "uav:mapToType") ?? "i=887");
                return;
            }
            if (String(schema, "uav:mapToType") is not { } identity)
            {
                VisitSchemas(schema, pointer, href, diagnostics);
                return;
            }
            identity = CanonicalIdentity(identity);
            int rank = schema["uav:valueRank"]?.GetValue<int>() ?? -1;
            VisitSchemas(schema, pointer, href, diagnostics);
            JsonObject canonical = ValueSchema(identity, rank, pointer, href, diagnostics, []);
            ApplyDimensions(canonical, schema["uav:arrayDimensions"] as JsonArray);
            MergeSchema(schema, canonical);
            if (integerProperty &&
                (!m_nodes.TryGetValue(identity, out NativeNode? node) ||
                    node.Node is not Export.UADataType { IsAbstract: true }) &&
                IntegerBounds(Terminal(identity, [])) is { } bounds)
            {
                AddStorageBounds(schema, bounds, pointer);
            }
        }

        /// <summary>
        /// Expands native values recursively with bounded depth and node count.
        /// </summary>
        private JsonObject ValueSchema(
            string identity, int rank, string pointer, string href, List<string> diagnostics, HashSet<string> path)
        {
            if (++m_schemaNodes > 100_000)
            {
                throw new FormatException($"The ordinary WoT schema at '{href}#{pointer}' exceeds the node limit.");
            }
            var schema = new JsonObject { ["uav:typeName"] = Name(identity) };
            if (rank is -3 or -2)
            {
                schema["oneOf"] = new JsonArray(
                    ValueSchema(identity, -1, pointer + "/oneOf/0", href, diagnostics, path),
                    ValueSchema(identity, rank == -3 ? 1 : 0, pointer + "/oneOf/1", href, diagnostics, path));
                return schema;
            }
            if (rank >= 0)
            {
                schema["type"] = "array";
                if (rank == 0)
                {
                    Incomplete("The rank is not fixed; a finite ordinary item schema cannot describe every rank.",
                        pointer + "/items", href, diagnostics);
                    return schema;
                }
                schema["items"] = ValueSchema(
                    identity, rank == 1 ? -1 : rank - 1, pointer + "/items", href, diagnostics, path);
                return schema;
            }
            if (path.Count >= 64 || !path.Add(identity))
            {
                Incomplete($"Recursive DataType '{identity}' cannot be expanded as a finite WoT DataSchema.",
                    pointer, href, diagnostics);
                return schema;
            }
            try
            {
                JsonObject? definition = ValueDefinition(identity, []);
                if (definition?["uav:enumFields"] is JsonArray fields)
                {
                    schema["type"] = "integer";
                    if (definition["uav:isOptionSet"]?.GetValue<bool>() == true)
                    {
                        if (IntegerBounds(Terminal(identity, [])) is { } bounds)
                        {
                            schema["minimum"] = bounds.Minimum;
                            schema["maximum"] = bounds.Maximum;
                        }
                        else
                        {
                            Incomplete("The OptionSet storage type is unresolved.", pointer, href, diagnostics);
                        }
                    }
                    else
                    {
                        var alternatives = new JsonArray();
                        foreach (JsonObject field in fields.OfType<JsonObject>())
                        {
                            var alternative = new JsonObject
                            {
                                ["const"] = field["uav:enumValue"]?.DeepClone(),
                                ["uav:enumName"] = field["uav:enumName"]?.DeepClone()
                            };
                            alternatives.Add((JsonNode)alternative);
                        }
                        schema["oneOf"] = alternatives;
                    }
                    return schema;
                }
                if (definition?["uav:fields"] is JsonArray structureFields)
                {
                    schema["type"] = "object";
                    schema["additionalProperties"] = false;
                    var properties = new JsonObject();
                    var required = new JsonArray();
                    var order = new JsonArray();
                    bool union = String(definition, "uav:structureType") is "Union" or "UnionWithSubtypedValues";
                    var branches = new JsonArray();
                    foreach (JsonObject field in EffectiveFields(
                        String(definition, "uav:dataTypeId") ?? identity, structureFields, [],
                        pointer, href, diagnostics))
                    {
                        string name = String(field, "uav:fieldName")!;
                        string location = pointer + "/properties/" + Escape(name);
                        string? fieldType = String(field, "uav:fieldDataTypeId");
                        JsonObject child;
                        if (fieldType is null)
                        {
                            child = [];
                            Incomplete("The Structure field DataType is unavailable.", location, href, diagnostics);
                        }
                        else
                        {
                            fieldType = CanonicalIdentity(fieldType);
                            child = ValueSchema(fieldType, field["uav:valueRank"]?.GetValue<int>() ?? -1,
                                location, href, diagnostics, path);
                            child["uav:mapToType"] = fieldType;
                        }
                        foreach (string member in s_fieldMetadata)
                        {
                            if (field[member] is { } value)
                            {
                                child[member] = value.DeepClone();
                            }
                        }
                        ApplyDimensions(child, field["uav:arrayDimensions"] as JsonArray);
                        if (field["uav:allowSubtypes"]?.GetValue<bool>() == true)
                        {
                            Incomplete("Subtyped Structure field values require dynamic type information.",
                                location, href, diagnostics);
                        }
                        properties.Add(name, child);
                        order.Add((JsonNode)JsonValue.Create(name)!);
                        if (union)
                        {
                            branches.Add((JsonNode)new JsonObject
                            {
                                ["required"] = new JsonArray(JsonValue.Create(name))
                            });
                        }
                        else if (field["uav:isOptional"]?.GetValue<bool>() != true)
                        {
                            required.Add((JsonNode)JsonValue.Create(name)!);
                        }
                    }
                    schema["properties"] = properties;
                    schema["uav:fieldOrder"] = order;
                    if (union)
                    {
                        schema["uav:structureType"] = String(definition!, "uav:structureType");
                        schema["maxProperties"] = 1;
                        branches.Add((JsonNode)new JsonObject { ["const"] = new JsonObject() });
                        schema["oneOf"] = branches;
                    }
                    else if (required.Count != 0)
                    {
                        schema["required"] = required;
                    }
                    return schema;
                }
                string terminal = Terminal(identity, []);
                string? type = terminal switch
                {
                    "i=1" => "boolean",
                    "i=2" or "i=3" or "i=4" or "i=5" or "i=6" or "i=7" or "i=8" or "i=9" or
                        "i=19" or "i=27" or "i=28" or "i=29" => "integer",
                    "i=10" or "i=11" or "i=26" => "number",
                    "i=12" or "i=13" or "i=14" or "i=15" or "i=16" or "i=17" or "i=18" or
                        "i=20" or "i=21" => "string",
                    _ => null
                };
                if (type is null)
                {
                    Incomplete($"No ordinary value schema is available for DataType '{identity}'.",
                        pointer, href, diagnostics);
                }
                else
                {
                    schema["type"] = type;
                    switch (terminal)
                    {
                        case "i=13":
                            schema["format"] = "date-time";
                            break;
                        case "i=14":
                            schema["format"] = "uuid";
                            break;
                        case "i=15":
                            schema["contentEncoding"] = "base64";
                            break;
                    }
                }
                return schema;
            }
            finally
            {
                path.Remove(identity);
            }
        }

        /// <summary>
        /// Expresses native maximum array lengths in ordinary schemas at each fixed dimension.
        /// </summary>
        private static void ApplyDimensions(JsonObject schema, JsonArray? dimensions)
        {
            foreach (JsonNode? dimension in dimensions ?? [])
            {
                if (String(schema, "type") != "array")
                {
                    break;
                }
                uint length = dimension?.GetValue<uint>() ?? 0;
                if (length > 0)
                {
                    schema["maxItems"] = length;
                }
                if (schema["items"] is not JsonObject items)
                {
                    break;
                }
                schema = items;
            }
        }

        /// <summary>
        /// Finds effective fields or enum alternatives, including subtypes that add no definition.
        /// </summary>
        private JsonObject? ValueDefinition(string identity, HashSet<string> path)
        {
            if (!path.Add(identity) || path.Count > 64)
            {
                return null;
            }
            if (m_definitions.TryGetValue(identity, out JsonObject? definition))
            {
                if (definition["uav:fields"] is JsonArray || definition["uav:enumFields"] is JsonArray)
                {
                    return definition;
                }
                if (definition["uav:dataTypeSubtypeOf"] is JsonObject parent &&
                    String(parent, "uav:dataTypeId") is { } baseId)
                {
                    return ValueDefinition(CanonicalIdentity(baseId), path);
                }
            }
            if (m_nodes.TryGetValue(identity, out NativeNode? node))
            {
                foreach (Export.Reference reference in node.Node.References ?? [])
                {
                    if (!reference.IsForward && Portable(reference.ReferenceType, node.Source) == "i=45")
                    {
                        return ValueDefinition(Portable(reference.Value, node.Source), path);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Completes inherited Structure prefixes when their definitions came from separate files.
        /// </summary>
        private List<JsonObject> EffectiveFields(
            string identity, JsonArray declared, HashSet<string> path,
            string pointer, string href, List<string> diagnostics)
        {
            if (!path.Add(identity) || path.Count > 64)
            {
                throw new FormatException($"Cyclic or excessive DataType inheritance at '{identity}'.");
            }
            var inherited = new List<JsonObject>();
            if (m_definitions.TryGetValue(identity, out JsonObject? definition) &&
                definition["uav:dataTypeSubtypeOf"] is JsonObject baseType &&
                String(baseType, "uav:dataTypeId") is { } baseId)
            {
                baseId = CanonicalIdentity(baseId);
                JsonObject? parent = ValueDefinition(baseId, []);
                if (parent?["uav:fields"] is JsonArray parentFields)
                {
                    inherited = EffectiveFields(String(parent, "uav:dataTypeId") ?? baseId,
                        parentFields, path, pointer, href, diagnostics);
                }
                else if (baseId is not ("i=22" or "i=12756"))
                {
                    Incomplete($"Inherited Structure fields are unavailable for '{baseId}'.",
                        pointer, href, diagnostics);
                }
            }
            List<JsonObject> fields = [.. declared.OfType<JsonObject>()];
            if (fields.Count >= inherited.Count && inherited.Select(field => String(field, "uav:fieldName"))
                .SequenceEqual(fields.Take(inherited.Count).Select(field => String(field, "uav:fieldName"))))
            {
                return fields;
            }
            var names = inherited.Select(field => String(field, "uav:fieldName")).ToHashSet(StringComparer.Ordinal);
            if (fields.Any(field => names.Contains(String(field, "uav:fieldName"))))
            {
                throw new FormatException($"Conflicting inherited Structure fields at '{identity}'.");
            }
            inherited.AddRange(fields);
            return inherited;
        }

        /// <summary>
        /// Combines native requiredness and upper bounds without weakening authored nested constraints.
        /// </summary>
        private static void MergeSchema(JsonObject target, JsonObject canonical)
        {
            foreach ((string member, JsonNode? value) in canonical)
            {
                if (member is "minimum" or "maximum" && target.ContainsKey(member))
                {
                    continue;
                }
                if (member == "maxItems" &&
                    IntegerLimits(target, member) is { } authoredLimit &&
                    IntegerLimits(canonical, member) is { } nativeLimit && authoredLimit.Floor <= nativeLimit.Floor)
                {
                    continue;
                }
                if (value is JsonObject child && target[member] is JsonObject existing)
                {
                    MergeSchema(existing, child);
                }
                else if (member == "required" && value is JsonArray mandatory &&
                    target[member] is JsonArray authored)
                {
                    var names = authored.Select(name => name!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    foreach (JsonNode? name in mandatory)
                    {
                        if (names.Add(name!.GetValue<string>()))
                        {
                            authored.Add(name.DeepClone());
                        }
                    }
                }
                else if (member == "oneOf" && value is JsonArray alternatives && target[member] is JsonArray current)
                {
                    if (current.Count != alternatives.Count)
                    {
                        throw new FormatException("Authored alternatives disagree with the native value schema.");
                    }
                    for (int i = 0; i < alternatives.Count; i++)
                    {
                        if (current[i] is JsonObject existingBranch && alternatives[i] is JsonObject branch)
                        {
                            MergeSchema(existingBranch, branch);
                        }
                    }
                }
                else
                {
                    target[member] = value?.DeepClone();
                }
            }
        }

        /// <summary>
        /// Resolves a simple type to its built-in storage type.
        /// </summary>
        private string Terminal(string identity, HashSet<string> path)
        {
            if (IntegerBounds(identity) is not null ||
                identity is "i=1" or "i=10" or "i=11" or "i=12" or "i=13" or "i=14" or "i=15" or
                    "i=16" or "i=17" or "i=18" or "i=19" or "i=20" or "i=21" or "i=26" or
                    "i=27" or "i=28" or "i=29")
            {
                var parsed = ExpandedNodeId.Parse(identity);
                BuiltInType builtIn = string.IsNullOrEmpty(parsed.NamespaceUri)
                    ? TypeInfo.GetBuiltInType(parsed.InnerNodeId) : BuiltInType.Null;
                return builtIn == BuiltInType.Null
                    ? identity : "i=" + ((int)builtIn).ToString(CultureInfo.InvariantCulture);
            }
            if (!path.Add(identity) || path.Count > 128)
            {
                return identity;
            }
            if (m_definitions.TryGetValue(identity, out JsonObject? definition) &&
                definition["uav:dataTypeSubtypeOf"] is JsonObject parent &&
                String(parent, "uav:dataTypeId") is { } parentId)
            {
                return Terminal(CanonicalIdentity(parentId), path);
            }
            if (m_nodes.TryGetValue(identity, out NativeNode? node))
            {
                foreach (Export.Reference reference in node.Node.References ?? [])
                {
                    if (!reference.IsForward && Portable(reference.ReferenceType, node.Source) == "i=45")
                    {
                        return Terminal(Portable(reference.Value, node.Source), path);
                    }
                }
            }
            return identity;
        }

        /// <summary>
        /// Returns exact bounds only for concrete integer storage types.
        /// </summary>
        private static (decimal Minimum, decimal Maximum)? IntegerBounds(string identity)
        {
            return identity switch
            {
                "i=2" => (sbyte.MinValue, sbyte.MaxValue),
                "i=3" => (byte.MinValue, byte.MaxValue),
                "i=4" => (short.MinValue, short.MaxValue),
                "i=5" => (ushort.MinValue, ushort.MaxValue),
                "i=6" => (int.MinValue, int.MaxValue),
                "i=7" => (uint.MinValue, uint.MaxValue),
                "i=8" => (long.MinValue, long.MaxValue),
                "i=9" => (ulong.MinValue, ulong.MaxValue),
                _ => null
            };
        }

        /// <summary>
        /// Intersects integer leaf constraints with storage bounds and rejects empty domains.
        /// </summary>
        private static void AddStorageBounds(
            JsonObject schema, (decimal Minimum, decimal Maximum) bounds, string pointer)
        {
            if (String(schema, "type") == "array")
            {
                if (schema["items"] is JsonObject items)
                {
                    AddStorageBounds(items, bounds, pointer + "/items");
                }
                return;
            }
            if (schema["oneOf"] is JsonArray alternatives && String(schema, "type") != "integer")
            {
                foreach (JsonObject alternative in alternatives.OfType<JsonObject>())
                {
                    AddStorageBounds(alternative, bounds, pointer + "/oneOf");
                }
                return;
            }
            if (String(schema, "type") != "integer")
            {
                return;
            }
            (decimal Floor, decimal Ceiling)? minimum = IntegerLimits(schema, "minimum");
            (decimal Floor, decimal Ceiling)? maximum = IntegerLimits(schema, "maximum");
            if (minimum is null || minimum.Value.Ceiling <= bounds.Minimum)
            {
                schema["minimum"] = bounds.Minimum;
            }
            if (maximum is null || maximum.Value.Floor >= bounds.Maximum)
            {
                schema["maximum"] = bounds.Maximum;
            }
            decimal least = Math.Max(bounds.Minimum, minimum?.Ceiling ?? bounds.Minimum);
            decimal greatest = Math.Min(bounds.Maximum, maximum?.Floor ?? bounds.Maximum);
            if (IntegerLimits(schema, "exclusiveMinimum") is { } exclusiveMinimum)
            {
                least = Math.Max(least, exclusiveMinimum.Floor + 1);
            }
            if (IntegerLimits(schema, "exclusiveMaximum") is { } exclusiveMaximum)
            {
                greatest = Math.Min(greatest, exclusiveMaximum.Ceiling - 1);
            }
            if (least > greatest)
            {
                throw new FormatException($"Empty integer constraint intersection at '{pointer}'.");
            }
        }

        /// <summary>
        /// Computes exact integer limits from decimal JSON notation, clipped outside every storage domain.
        /// </summary>
        private static (decimal Floor, decimal Ceiling)? IntegerLimits(JsonObject node, string member)
        {
            if (node[member] is not { } value)
            {
                return null;
            }
            if (value.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            {
                throw new FormatException($"The '{member}' constraint must be numeric.");
            }
            const decimal outside = 18446744073709551616m;
            string text = value.ToJsonString();
            bool negative = text.AsSpan().StartsWith("-", StringComparison.Ordinal);
            string magnitude = negative ? text[1..] : text;
            int exponentIndex = magnitude.IndexOf('e', StringComparison.OrdinalIgnoreCase);
            int exponent = 0;
            if (exponentIndex >= 0)
            {
                string exponentText = magnitude[(exponentIndex + 1)..];
                if (!int.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out exponent))
                {
                    exponent = exponentText.AsSpan().StartsWith("-", StringComparison.Ordinal)
                        ? int.MinValue : int.MaxValue;
                }
                magnitude = magnitude[..exponentIndex];
            }
            int dot = magnitude.IndexOf('.', StringComparison.Ordinal);
            string digits = magnitude.Replace(".", string.Empty, StringComparison.Ordinal);
            string significant = digits.TrimStart('0');
            if (significant.Length == 0)
            {
                return (0, 0);
            }
            long decimalPosition = (long)(dot >= 0 ? dot : magnitude.Length) + exponent -
                (digits.Length - significant.Length);
            decimal whole;
            bool fraction;
            if (decimalPosition > 20)
            {
                whole = outside;
                fraction = false;
            }
            else if (decimalPosition <= 0)
            {
                whole = 0;
                fraction = true;
            }
            else
            {
                int integerDigits = (int)decimalPosition;
                string integer = significant.Length >= integerDigits
                    ? significant[..integerDigits]
                    : significant + new string('0', integerDigits - significant.Length);
                whole = Math.Min(outside, decimal.Parse(integer, CultureInfo.InvariantCulture));
                fraction = significant.Skip(integerDigits).Any(character => character != '0');
            }
            return negative
                ? (-whole - (fraction ? 1 : 0), -whole)
                : (whole, whole + (fraction ? 1 : 0));
        }

        /// <summary>
        /// Reports a schema gap at the value occurrence rather than only at the type definition.
        /// </summary>
        private static void Incomplete(string reason, string pointer, string href, List<string> diagnostics)
        {
            diagnostics.Add($"Incomplete generic WoT schema at {href}#{pointer}: {reason}");
        }

        /// <summary>Names direct schema positions, excluding literal values and preservation payloads.</summary>
        private static readonly string[] s_schemaMembers =
            ["input", "output", "data", "dataResponse", "subscription", "cancellation", "items",
                "additionalProperties", "not", "if", "then", "else", "contains", "propertyNames", "schema", "response"];

        /// <summary>Names root schema maps.</summary>
        private static readonly string[] s_schemaMaps =
            ["schemaDefinitions", "uriVariables", "$defs", "definitions", "patternProperties"];

        /// <summary>Names arrays whose children are schemas.</summary>
        private static readonly string[] s_schemaArrays = ["oneOf", "anyOf", "allOf", "forms", "additionalResponses"];

        /// <summary>Preserves field metadata at the generated value occurrence.</summary>
        private static readonly string[] s_fieldMetadata =
            ["uav:valueRank", "uav:arrayDimensions", "title", "titles", "description", "descriptions", "@context"];
    }
}
