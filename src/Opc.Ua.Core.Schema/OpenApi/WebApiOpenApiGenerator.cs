/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Text.Json.Nodes;
using System.Threading;
using Opc.Ua.Bindings;

namespace Opc.Ua.Schema.OpenApi
{
    /// <summary>
    /// Generates the OpenAPI 3.0 document of the OPC UA REST binding
    /// (OPC 10000-6, G.3 "OpenAPI Mapping") from the routes in
    /// <see cref="WebApiServiceRoutes"/> and the structure definitions of
    /// the request and response types. The document is built as a
    /// <see cref="JsonObject"/> object model without reflection, so the
    /// generator is NativeAOT compatible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator is the code counterpart of the documents the OPC
    /// Foundation publishes in <c>UA-Nodeset/OpenApi</c>: the same paths,
    /// operation ids, schema names and property names, derived from the
    /// types the binding encodes and decodes, so the document cannot drift
    /// from the wire format. The schemas describe the Compact JSON
    /// encoding, the default of the binding.
    /// </para>
    /// <para>
    /// A document without component schemas is small: each operation takes
    /// and returns a JSON object. With component schemas the document
    /// describes every request, response and the structures they contain,
    /// which is what OpenAPI client generators need to emit typed clients.
    /// </para>
    /// </remarks>
    public sealed class WebApiOpenApiGenerator
    {
        /// <summary>
        /// The OPC UA specification version the routes and the encoding
        /// follow; it is the <c>info.version</c> of the generated document.
        /// </summary>
        public const string SpecificationVersion = "1.5.7";

        private const string OpenApiVersion = "3.0.4";

        private const string kResponseNote = ". A failed service call is reported in ResponseHeader.ServiceResult.";

        /// <summary>
        /// Creates a generator that describes the types of the OPC UA
        /// specification as generated into this stack.
        /// </summary>
        public WebApiOpenApiGenerator()
        {
            m_resolver = new Lazy<IDataTypeDefinitionResolver>(
                CreateStandardResolver,
                LazyThreadSafetyMode.ExecutionAndPublication);
            m_serviceTypes = new Lazy<Dictionary<string, UaTypeDescription>>(
                () => IndexByName(m_resolver.Value),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Creates a generator that looks up the request, response and
        /// field types in <paramref name="resolver"/>.
        /// </summary>
        /// <param name="resolver">
        /// The resolver of the data types in the OPC UA namespace.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resolver"/> is <c>null</c>.
        /// </exception>
        public WebApiOpenApiGenerator(IDataTypeDefinitionResolver resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }
            m_resolver = new Lazy<IDataTypeDefinitionResolver>(() => resolver);
            m_serviceTypes = new Lazy<Dictionary<string, UaTypeDescription>>(
                () => IndexByName(resolver),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Generates the OpenAPI document for <paramref name="serviceSet"/>.
        /// </summary>
        /// <param name="serviceSet">The services to describe.</param>
        /// <param name="includeSchemas">
        /// <c>true</c> to describe the request, response and contained
        /// structures as component schemas; <c>false</c> (the default) to
        /// describe each body as a JSON object.
        /// </param>
        /// <param name="serverUrl">
        /// The URL of the server to list under <c>servers</c>, absolute or
        /// relative to the location of the document. <c>null</c> omits the
        /// list, which OpenAPI treats as <c>/</c>.
        /// </param>
        /// <returns>The OpenAPI document.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="serviceSet"/> is not a defined value.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The resolver does not know a request or response type of the
        /// service set.
        /// </exception>
        public JsonObject Generate(
            WebApiServiceSet serviceSet = WebApiServiceSet.AllServices,
            bool includeSchemas = false,
            string? serverUrl = null)
        {
            ArrayOf<WebApiServiceRoute> routes = WebApiServiceRoutes.GetRoutes(serviceSet);
            var components = includeSchemas ? new ComponentSchemas(m_resolver.Value) : null;

            var paths = new JsonObject();
            for (int i = 0; i < routes.Count; i++)
            {
                paths[routes[i].Path] = CreatePathItem(routes[i], components);
            }

            var document = new JsonObject
            {
                ["openapi"] = OpenApiVersion,
                ["info"] = new JsonObject
                {
                    ["title"] = "OPC UA Web API",
                    ["description"] =
                        "OPC UA services over HTTPS (OPC 10000-6, G.3). The bodies are OPC UA JSON " +
                        "(OPC 10000-6, 5.4) in the Compact encoding; send " +
                        "'Content-Type: application/json; encoding=verbose' for the Verbose encoding.",
                    ["version"] = SpecificationVersion
                }
            };
            if (!string.IsNullOrEmpty(serverUrl))
            {
                document["servers"] = new JsonArray(new JsonObject { ["url"] = serverUrl });
            }
            document["paths"] = paths;
            if (components != null)
            {
                document["components"] = new JsonObject { ["schemas"] = components.ToJsonObject() };
            }
            return document;
        }

        private JsonObject CreatePathItem(WebApiServiceRoute route, ComponentSchemas? components)
        {
            var operation = new JsonObject
            {
                ["operationId"] = route.OperationId,
                ["requestBody"] = new JsonObject
                {
                    ["description"] = "OPC UA " + route.RequestType.Name + ".",
                    ["required"] = true,
                    ["content"] = CreateContent(route.RequestType, components)
                },
                ["responses"] = new JsonObject
                {
                    ["200"] = new JsonObject
                    {
                        ["description"] = "OPC UA " + route.ResponseType.Name + kResponseNote,
                        ["content"] = CreateContent(route.ResponseType, components)
                    }
                }
            };
            return new JsonObject { ["post"] = operation };
        }

        private JsonObject CreateContent(Type messageType, ComponentSchemas? components)
        {
            JsonObject schema;
            if (components == null)
            {
                schema = new JsonObject { ["type"] = "object" };
            }
            else
            {
                if (!m_serviceTypes.Value.TryGetValue(messageType.Name, out UaTypeDescription? description))
                {
                    throw new InvalidOperationException(
                        $"The data type resolver does not know the service message '{messageType.Name}'.");
                }
                schema = OpenApiBuiltInSchemas.Reference(components.EnsureType(description));
            }
            return new JsonObject { [WebApiMediaType.ContentType] = new JsonObject { ["schema"] = schema } };
        }

        private static EncodeableFactoryDefinitionSource CreateStandardResolver()
        {
            return new EncodeableFactoryDefinitionSource(EncodeableFactory.Create(), new NamespaceTable());
        }

        private static Dictionary<string, UaTypeDescription> IndexByName(IDataTypeDefinitionResolver resolver)
        {
            var index = new Dictionary<string, UaTypeDescription>(StringComparer.Ordinal);
            foreach (UaTypeDescription description in resolver.GetNamespaceTypes(Namespaces.OpcUa))
            {
                index[description.Name] = description;
            }
            return index;
        }

        private readonly Lazy<IDataTypeDefinitionResolver> m_resolver;
        private readonly Lazy<Dictionary<string, UaTypeDescription>> m_serviceTypes;

        /// <summary>
        /// Collects the component schemas the messages of one document
        /// reach.
        /// </summary>
        private sealed class ComponentSchemas
        {
            public ComponentSchemas(IDataTypeDefinitionResolver resolver)
            {
                m_resolver = resolver;
            }

            public JsonObject ToJsonObject()
            {
                var schemas = new JsonObject();
                foreach (KeyValuePair<string, JsonObject> schema in m_schemas)
                {
                    schemas[schema.Key] = schema.Value;
                }
                return schemas;
            }

            /// <summary>
            /// Adds the schema of <paramref name="type"/> and of the types
            /// it references, and returns the name of its schema.
            /// </summary>
            public string EnsureType(UaTypeDescription type)
            {
                string name = type.Name;
                if (m_schemas.ContainsKey(name) || !m_building.Add(name))
                {
                    return name;
                }

                m_schemas[name] = type.Definition switch
                {
                    StructureDefinition structure => CreateStructure(structure),
                    EnumDefinition enumeration => CreateEnumeration(enumeration, type.IsStructureOptionSet),
                    _ => new JsonObject { ["type"] = "object" }
                };
                m_building.Remove(name);
                return name;
            }

            private JsonObject CreateStructure(StructureDefinition structure)
            {
                bool isUnion = structure.StructureType
                    is StructureType.Union or StructureType.UnionWithSubtypedValues;

                var properties = new JsonObject();
                if (isUnion)
                {
                    // Part 6, 5.4.7: the selected field and, in the Compact
                    // encoding, the SwitchField that identifies it.
                    properties["SwitchField"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" };
                }
                ArrayOf<StructureField> fields = structure.Fields;
                for (int i = 0; i < fields.Count; i++)
                {
                    StructureField field = fields[i];
                    properties[FieldName(field, i)] = CreateField(field);
                }

                var schema = new JsonObject { ["type"] = "object" };
                string? baseName = EnsureBase(structure.BaseDataType);
                if (baseName != null)
                {
                    schema["allOf"] = new JsonArray(OpenApiBuiltInSchemas.Reference(baseName));
                }
                schema["properties"] = properties;
                return schema;
            }

            private string? EnsureBase(NodeId baseDataType)
            {
                // Structure itself and the abstract built-in bases carry no fields.
                if (baseDataType.IsNull ||
                    baseDataType == DataTypeIds.Structure ||
                    TypeInfo.GetBuiltInType(baseDataType) != BuiltInType.Null ||
                    !m_resolver.TryResolve(baseDataType, out UaTypeDescription? baseType) ||
                    baseType.Definition is not StructureDefinition)
                {
                    return null;
                }
                return EnsureType(baseType);
            }

            private static JsonObject CreateEnumeration(EnumDefinition enumeration, bool isStructureOptionSet)
            {
                if (isStructureOptionSet)
                {
                    // Part 5, 12.18: Value and ValidBits are ByteStrings.
                    return new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["Value"] = OpenApiBuiltInSchemas.CreateScalar(BuiltInType.ByteString),
                            ["ValidBits"] = OpenApiBuiltInSchemas.CreateScalar(BuiltInType.ByteString)
                        }
                    };
                }
                if (enumeration.IsOptionSet)
                {
                    // Part 3, 8.52: the unsigned integer with the bits of the set.
                    return OpenApiBuiltInSchemas.CreateScalar(BuiltInType.UInt32);
                }

                ArrayOf<EnumField> fields = enumeration.Fields;
                var values = new List<JsonNode?>(fields.Count);
                var names = new List<JsonNode?>(fields.Count);
                for (int i = 0; i < fields.Count; i++)
                {
                    values.Add(fields[i].Value);
                    names.Add(fields[i].Name ?? string.Empty);
                }
                return new JsonObject
                {
                    ["type"] = "integer",
                    ["format"] = "int32",
                    ["enum"] = new JsonArray([.. values]),
                    ["x-enum-varnames"] = new JsonArray([.. names])
                };
            }

            private JsonObject CreateField(StructureField field)
            {
                JsonObject element = CreateElement(field.DataType);
                switch (field.ValueRank)
                {
                    case ValueRanks.Scalar:
                        return element;
                    case >= 1:
                        // One array level per dimension (Part 6, 5.4.5).
                        for (int dimension = 0; dimension < field.ValueRank; dimension++)
                        {
                            element = new JsonObject { ["type"] = "array", ["items"] = element };
                        }
                        return element;
                    default:
                        // Any, ScalarOrOneDimension or OneOrMoreDimensions.
                        return [];
                }
            }

            private JsonObject CreateElement(NodeId dataType)
            {
                // A field of the abstract number types carries a Variant on the wire.
                if (dataType == DataTypeIds.Number ||
                    dataType == DataTypeIds.Integer ||
                    dataType == DataTypeIds.UInteger)
                {
                    EnsureBuiltIn(OpenApiBuiltInSchemas.Variant);
                    return OpenApiBuiltInSchemas.Reference(OpenApiBuiltInSchemas.Variant);
                }

                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType);
                if (builtInType != BuiltInType.Null)
                {
                    string? componentName = OpenApiBuiltInSchemas.GetComponentName(builtInType);
                    if (componentName == null)
                    {
                        return OpenApiBuiltInSchemas.CreateScalar(builtInType);
                    }
                    EnsureBuiltIn(componentName);
                    return OpenApiBuiltInSchemas.Reference(componentName);
                }

                if (m_resolver.TryResolve(dataType, out UaTypeDescription? referenced))
                {
                    return OpenApiBuiltInSchemas.Reference(EnsureType(referenced));
                }

                // A type the resolver does not know can hold any value.
                return [];
            }

            private void EnsureBuiltIn(string componentName)
            {
                if (m_schemas.ContainsKey(componentName))
                {
                    return;
                }
                m_schemas[componentName] = OpenApiBuiltInSchemas.CreateComponent(componentName);
                if (componentName is OpenApiBuiltInSchemas.DataValue or OpenApiBuiltInSchemas.DiagnosticInfo)
                {
                    EnsureBuiltIn(OpenApiBuiltInSchemas.StatusCode);
                }
            }

            private static string FieldName(StructureField field, int index)
            {
                return string.IsNullOrEmpty(field.Name)
                    ? "Field" + index.ToString(CultureInfo.InvariantCulture)
                    : field.Name!;
            }

            private readonly IDataTypeDefinitionResolver m_resolver;
            private readonly SortedDictionary<string, JsonObject> m_schemas = new(StringComparer.Ordinal);
            private readonly HashSet<string> m_building = new(StringComparer.Ordinal);
        }
    }
}
