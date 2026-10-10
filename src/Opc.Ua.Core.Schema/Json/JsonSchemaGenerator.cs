/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Opc.Ua.Schema.Json
{
    /// <summary>
    /// Generates JSON Schema (draft 2020-12) documents for OPC UA data types
    /// according to the OPC UA Part 6 JSON encoding (Annex C) in both the
    /// compact (reversible) and verbose flavors. The schema is constructed as a
    /// <see cref="JsonObject"/> object model so that no
    /// reflection is required and the generator is NativeAOT compatible.
    /// </summary>
    public sealed class JsonSchemaGenerator : IUaSchemaGenerator
    {
        /// <inheritdoc/>
        public bool CanGenerate(UaSchemaFormat format)
        {
            return format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose;
        }

        /// <inheritdoc/>
        public IUaSchema Generate(
            UaTypeDescription type,
            IDataTypeDefinitionResolver resolver,
            UaSchemaFormat format,
            UaSchemaScope scope)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            bool verbose = format == UaSchemaFormat.JsonVerbose;
            var context = new GenerationContext(type.NamespaceUri, resolver, verbose);

            if (scope == UaSchemaScope.Namespace)
            {
                foreach (UaTypeDescription namespaceType in resolver.GetNamespaceTypes(type.NamespaceUri))
                {
                    context.EnsureType(namespaceType);
                }
                context.EnsureType(type);

                var namespaceDocument = new JsonObject
                {
                    ["$schema"] = JsonSchemaConstants.Dialect,
                    ["$id"] = DocumentId(type.NamespaceUri),
                    ["$defs"] = context.Definitions
                };
                return new JsonSchemaDocument(format, type.NamespaceUri, namespaceDocument);
            }

            string rootKey = context.EnsureType(type);
            var document = new JsonObject
            {
                ["$schema"] = JsonSchemaConstants.Dialect,
                ["$id"] = TypeDocumentId(type),
                ["title"] = type.Name,
                ["$ref"] = "#/$defs/" + rootKey
            };
            if (context.Definitions.Count > 0)
            {
                document["$defs"] = context.Definitions;
            }
            return new JsonSchemaDocument(format, type.NamespaceUri, document);
        }

        private static string TypeDocumentId(UaTypeDescription type)
        {
            string ns = string.IsNullOrEmpty(type.NamespaceUri) ? DefaultNamespace : type.NamespaceUri;
            return ns.TrimEnd('/') + "/" + type.Name + ".schema.json";
        }

        private static string DocumentId(string namespaceUri)
        {
            string ns = string.IsNullOrEmpty(namespaceUri) ? DefaultNamespace : namespaceUri;
            return ns.TrimEnd('/') + "/types.schema.json";
        }

        private const string DefaultNamespace = "urn:opcua:types";

        /// <summary>
        /// Holds the per-document state during schema generation.
        /// </summary>
        private sealed class GenerationContext
        {
            public GenerationContext(string targetNamespace, IDataTypeDefinitionResolver resolver, bool verbose)
            {
                m_targetNamespace = targetNamespace;
                m_resolver = resolver;
                m_verbose = verbose;
                Definitions = [];
                m_visiting = new HashSet<string>(StringComparer.Ordinal);
                m_emittedTypes = new HashSet<string>(StringComparer.Ordinal);
            }

            public JsonObject Definitions { get; }

            public string EnsureType(UaTypeDescription type)
            {
                string typeKey = TypeKey(type);
                string definitionKey = DefinitionKey(type);
                if (m_emittedTypes.Contains(typeKey) || m_visiting.Contains(typeKey))
                {
                    return definitionKey;
                }

                m_visiting.Add(typeKey);
                JsonObject schema = type.Definition switch
                {
                    StructureDefinition structure => BuildStructure(type, structure),
                    // a subtype of the OptionSet structure is encoded as {Value, ValidBits}.
                    EnumDefinition when type.IsStructureOptionSet
                        => BuildStructure(type, UaTypeDescription.CreateOptionSetStructure()),
                    EnumDefinition enumeration => BuildEnum(enumeration, m_verbose),
                    _ => new JsonObject { ["type"] = "object" }
                };
                m_visiting.Remove(typeKey);
                Definitions[definitionKey] = schema;
                m_emittedTypes.Add(typeKey);
                return definitionKey;
            }

            private JsonObject BuildStructure(UaTypeDescription type, StructureDefinition structure)
            {
                bool isUnion = structure.StructureType
                    is StructureType.Union or StructureType.UnionWithSubtypedValues;
                ArrayOf<StructureField> fields = structure.Fields;

                if (isUnion)
                {
                    var options = new List<JsonNode?>(fields.Count);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        StructureField field = fields[i];
                        string name = FieldName(field, i);
                        var properties = new JsonObject();
                        var optionRequired = new List<JsonNode?>();
                        if (!m_verbose)
                        {
                            // The compact encoding emits the union discriminator and
                            // omits the selected value when it is the default value.
                            properties["SwitchField"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["const"] = i + 1
                            };
                            optionRequired.Add("SwitchField");
                        }
                        else
                        {
                            // The verbose encoding identifies the selected field by name.
                            optionRequired.Add(name);
                        }
                        properties[name] = FieldSchema(field);
                        options.Add(new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = properties,
                            ["required"] = new JsonArray([.. optionRequired]),
                            ["additionalProperties"] = false
                        });
                    }

                    // A union without a selected field (SwitchField 0) is encoded as an
                    // empty object.
                    options.Add(new JsonObject
                    {
                        ["type"] = "object",
                        ["maxProperties"] = 0
                    });
                    return new JsonObject
                    {
                        ["title"] = type.Name,
                        ["oneOf"] = new JsonArray([.. options])
                    };
                }

                var fieldSchemas = new JsonObject();
                var required = new List<JsonNode?>();
                bool hasOptionalField = false;
                for (int i = 0; i < fields.Count; i++)
                {
                    StructureField field = fields[i];
                    string name = FieldName(field, i);
                    fieldSchemas[name] = FieldSchema(field);
                    if (field.IsOptional)
                    {
                        hasOptionalField = true;
                    }
                    else if (m_verbose)
                    {
                        // Part 6 5.4.1: only the verbose encoding writes every field; the
                        // compact encoding omits fields with a default or null value.
                        required.Add(name);
                    }
                }

                if (!m_verbose && hasOptionalField)
                {
                    // The compact encoding prefixes structures that have optional
                    // fields with an EncodingMask that selects the present fields; a
                    // zero mask is the default value and therefore omitted.
                    fieldSchemas["EncodingMask"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0
                    };
                }

                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["title"] = type.Name,
                    ["properties"] = fieldSchemas,
                    ["additionalProperties"] = false
                };
                if (required.Count > 0)
                {
                    schema["required"] = new JsonArray([.. required]);
                }
                return schema;
            }

            private static JsonObject BuildEnum(EnumDefinition enumeration, bool verbose)
            {
                ArrayOf<EnumField> fields = enumeration.Fields;
                if (enumeration.IsOptionSet)
                {
                    // Part 3 8.52: the fields of an (integer-backed) OptionSet are bit numbers and the value
                    // is the underlying unsigned integer (a string when 64 bit wide) with
                    // any combination of these bits, in both flavors.
                    return new JsonObject
                    {
                        ["type"] = new JsonArray("integer", "string"),
                        ["minimum"] = 0,
                        ["pattern"] = "^\\d+$"
                    };
                }

                if (verbose)
                {
                    // Verbose enums are encoded as the string "Name_Value".
                    var names = new List<JsonNode?>(fields.Count);
                    for (int i = 0; i < fields.Count; i++)
                    {
                        EnumField field = fields[i];
                        names.Add($"{field.Name}_{field.Value}");
                    }
                    var verboseSchema = new JsonObject { ["type"] = "string" };
                    if (names.Count > 0)
                    {
                        verboseSchema["enum"] = new JsonArray([.. names]);
                    }
                    return verboseSchema;
                }

                var options = new List<JsonNode?>(fields.Count);
                for (int i = 0; i < fields.Count; i++)
                {
                    EnumField field = fields[i];
                    var option = new JsonObject { ["const"] = field.Value };
                    if (!string.IsNullOrEmpty(field.Name))
                    {
                        option["title"] = field.Name;
                    }
                    options.Add(option);
                }
                var schema = new JsonObject { ["type"] = "integer" };
                if (options.Count > 0)
                {
                    schema["oneOf"] = new JsonArray([.. options]);
                }
                return schema;
            }

            private JsonObject FieldSchema(StructureField field)
            {
                NodeId dataType = field.DataType;
                bool isScalar = field.ValueRank == ValueRanks.Scalar;

                // Part 6 5.4.1: the verbose encoding writes JSON null for null values and
                // null array elements are written as JSON null in both flavors.
                bool allowNull = m_verbose || !isScalar;
                JsonObject schema = ApplyValueRank(() => ElementSchema(dataType, allowNull), field.ValueRank);
                return m_verbose && !isScalar ? Nullable(schema) : schema;
            }

            private JsonObject ElementSchema(NodeId dataType, bool allowNull)
            {
                BuiltInType builtInType = SchemaTypeInfo.GetFieldEncodingType(dataType);
                if (builtInType != BuiltInType.Null)
                {
                    JsonObject schema = JsonBuiltInTypeSchemas.Create(builtInType, m_verbose, Definitions);
                    if (dataType == DataTypeIds.Number ||
                        dataType == DataTypeIds.Integer ||
                        dataType == DataTypeIds.UInteger)
                    {
                        // Abstract numeric fields must retain their concrete Variant wire type.
                        schema["required"] = new JsonArray("UaType");
                        schema["properties"] = new JsonObject
                        {
                            ["UaType"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 25 }
                        };
                    }
                    return allowNull && IsNullable(builtInType) ? Nullable(schema) : schema;
                }

                if (m_resolver.TryResolve(dataType, out UaTypeDescription? referenced))
                {
                    string key = EnsureType(referenced);
                    JsonObject schema = JsonSchemaConstants.Ref(key);
                    return allowNull &&
                        (referenced.Definition is StructureDefinition || referenced.IsStructureOptionSet)
                        ? Nullable(schema)
                        : schema;
                }

                // Unresolved type: allow any value.
                return [];
            }

            /// <summary>
            /// Returns true for the built-in types whose null value is encoded as JSON null.
            /// </summary>
            private static bool IsNullable(BuiltInType builtInType)
            {
                switch (builtInType)
                {
                    case BuiltInType.String:
                    case BuiltInType.DateTime:
                    case BuiltInType.Guid:
                    case BuiltInType.ByteString:
                    case BuiltInType.XmlElement:
                    case BuiltInType.NodeId:
                    case BuiltInType.ExpandedNodeId:
                    case BuiltInType.QualifiedName:
                    case BuiltInType.LocalizedText:
                    case BuiltInType.ExtensionObject:
                    case BuiltInType.DataValue:
                    case BuiltInType.Variant:
                    case BuiltInType.DiagnosticInfo:
                        return true;
                    default:
                        return false;
                }
            }

            private static JsonObject Nullable(JsonObject schema)
            {
                return new JsonObject
                {
                    ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" })
                };
            }

            private static JsonObject ApplyValueRank(Func<JsonObject> elementFactory, int valueRank)
            {
                switch (valueRank)
                {
                    case ValueRanks.Scalar:
                        return elementFactory();
                    case ValueRanks.Any:
                        return new JsonObject
                        {
                            ["oneOf"] = new JsonArray(elementFactory(), AnyArray())
                        };
                    case ValueRanks.ScalarOrOneDimension:
                        return new JsonObject
                        {
                            ["oneOf"] = new JsonArray(elementFactory(), ArrayOf(elementFactory()))
                        };
                    case ValueRanks.OneOrMoreDimensions:
                        return ArrayOf(elementFactory());
                    default:
                        JsonObject node = elementFactory();
                        for (int i = 0; i < valueRank; i++)
                        {
                            node = ArrayOf(node);
                        }
                        return node;
                }
            }

            private static JsonObject AnyArray()
            {
                return new JsonObject
                {
                    ["type"] = "array"
                };
            }

            private static JsonObject ArrayOf(JsonObject items)
            {
                return new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = items
                };
            }

            private static string FieldName(StructureField field, int index)
            {
                return string.IsNullOrEmpty(field.Name) ? "Field" + index : field.Name!;
            }

            private string DefinitionKey(UaTypeDescription type)
            {
                if (string.Equals(type.NamespaceUri, m_targetNamespace, StringComparison.Ordinal))
                {
                    return type.Name;
                }

                return NamespaceToken(type.NamespaceUri) + "_" + type.Name;
            }

            private static string TypeKey(UaTypeDescription type)
            {
                return type.NamespaceUri + "|" + type.Name;
            }

            private static string NamespaceToken(string namespaceUri)
            {
                var builder = new StringBuilder(namespaceUri.Length);
                for (int i = 0; i < namespaceUri.Length; i++)
                {
                    char ch = namespaceUri[i];
                    builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
                }

                string sanitized = builder.Length == 0 ? "ns" : builder.ToString().Trim('_');
                if (sanitized.Length == 0)
                {
                    sanitized = "ns";
                }

                return sanitized + "_" + StableHash(namespaceUri).ToString("x8", CultureInfo.InvariantCulture);
            }

            private static uint StableHash(string value)
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }

                return hash;
            }

            private readonly string m_targetNamespace;
            private readonly IDataTypeDefinitionResolver m_resolver;
            private readonly bool m_verbose;
            private readonly HashSet<string> m_visiting;
            private readonly HashSet<string> m_emittedTypes;
        }
    }
}
