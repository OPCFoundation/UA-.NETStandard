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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Schema.Tests.OpenApi
{
    /// <summary>
    /// Tests of <see cref="WebApiOpenApiGenerator"/>: the shape of the
    /// document, the two levels of detail, the service sets, and the
    /// schemas of the structure, enumeration and built-in type variants.
    /// </summary>
    [TestFixture]
    [Category("WebApiOpenApi")]
    [Parallelizable]
    public class WebApiOpenApiGeneratorTests
    {
        private static readonly string[] s_postOnly = ["post"];
        private static readonly string[] s_optionSetProperties = ["Value", "ValidBits"];
        private static readonly string[] s_unionProperties = ["SwitchField", "Number", "Text"];
        private static readonly string[] s_derivedProperties = ["Extra"];

        private static readonly string[] s_builtInComponents =
        [
            "StatusCode",
            "LocalizedText",
            "Variant",
            "DataValue",
            "DiagnosticInfo",
            "ExtensionObject"
        ];

        private static readonly string[] s_dataValueProperties =
        [
            "UaType",
            "Value",
            "Dimensions",
            "Status",
            "SourceTimestamp",
            "SourcePicoseconds",
            "ServerTimestamp",
            "ServerPicoseconds"
        ];

        private static readonly WebApiOpenApiGenerator s_generator = new();

        [Test]
        public void DocumentIsOpenApi3WithOnePostOperationPerRoute(
            [Values] WebApiServiceSet serviceSet,
            [Values] bool includeSchemas)
        {
            JsonObject document = s_generator.Generate(serviceSet, includeSchemas);

            Assert.That((string?)document["openapi"], Does.StartWith("3.0."));
            Assert.That(
                (string?)document["info"]!["version"],
                Is.EqualTo(WebApiOpenApiGenerator.SpecificationVersion));
            JsonObject paths = document["paths"]!.AsObject();
            WebApiServiceRoute[] routes = WebApiServiceRoutes.GetRoutes(serviceSet).ToArray()!;
            Assert.That(paths.Select(p => p.Key), Is.EqualTo(routes.Select(r => r.Path)));
            foreach (WebApiServiceRoute route in routes)
            {
                JsonObject path = paths[route.Path]!.AsObject();
                Assert.That(path.Select(m => m.Key), Is.EqualTo(s_postOnly), route.Path);
                Assert.That((string?)path["post"]!["operationId"], Is.EqualTo(route.OperationId), route.Path);
            }
        }

        [Test]
        public void DocumentWithoutSchemasDescribesBodiesAsObjectsAndHasNoComponents(
            [Values] WebApiServiceSet serviceSet)
        {
            JsonObject document = s_generator.Generate(serviceSet);

            Assert.That(document.ContainsKey("components"), Is.False);
            foreach (KeyValuePair<string, JsonNode?> path in document["paths"]!.AsObject())
            {
                JsonObject post = path.Value!["post"]!.AsObject();
                Assert.That(
                    post["requestBody"]!["content"]!["application/json"]!["schema"]!.ToJsonString(),
                    Is.EqualTo("{\"type\":\"object\"}"),
                    path.Key);
                Assert.That(
                    post["responses"]!["200"]!["content"]!["application/json"]!["schema"]!.ToJsonString(),
                    Is.EqualTo("{\"type\":\"object\"}"),
                    path.Key);
                Assert.That((string?)post["requestBody"]!["description"], Does.StartWith("OPC UA "), path.Key);
            }
        }

        [Test]
        public void DocumentWithSchemasReferencesRequestAndResponseOfEveryRoute()
        {
            JsonObject document = s_generator.Generate(WebApiServiceSet.AllServices, includeSchemas: true);

            JsonObject paths = document["paths"]!.AsObject();
            JsonObject schemas = document["components"]!["schemas"]!.AsObject();
            foreach (WebApiServiceRoute route in WebApiServiceRoutes.Routes)
            {
                JsonObject post = paths[route.Path]!["post"]!.AsObject();
                Assert.That(
                    (string?)post["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"],
                    Is.EqualTo("#/components/schemas/" + route.RequestType.Name),
                    route.Path);
                Assert.That(
                    (string?)post["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"],
                    Is.EqualTo("#/components/schemas/" + route.ResponseType.Name),
                    route.Path);
                Assert.That(schemas.ContainsKey(route.RequestType.Name), Is.True, route.Path);
                Assert.That(schemas.ContainsKey(route.ResponseType.Name), Is.True, route.Path);
            }
        }

        [Test]
        public void EveryReferenceOfADocumentWithSchemasResolves(
            [Values] WebApiServiceSet serviceSet)
        {
            JsonObject document = s_generator.Generate(serviceSet, includeSchemas: true);

            JsonObject schemas = document["components"]!["schemas"]!.AsObject();
            string[] references = [.. CollectReferences(document)];
            Assert.That(references, Is.Not.Empty);
            foreach (string reference in references)
            {
                Assert.That(reference, Does.StartWith("#/components/schemas/"));
                Assert.That(
                    schemas.ContainsKey(reference["#/components/schemas/".Length..]),
                    Is.True,
                    reference);
            }
        }

        [Test]
        public void ServersAreListedOnlyWhenAServerUrlIsGiven()
        {
            JsonObject without = s_generator.Generate();
            JsonObject empty = s_generator.Generate(serverUrl: string.Empty);
            JsonObject with = s_generator.Generate(serverUrl: "/opcua/");

            Assert.That(without.ContainsKey("servers"), Is.False);
            Assert.That(empty.ContainsKey("servers"), Is.False);
            Assert.That(with["servers"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That((string?)with["servers"]![0]!["url"], Is.EqualTo("/opcua/"));
        }

        [Test]
        public void SessionlessDocumentIsASubsetOfTheAllServicesDocument()
        {
            JsonObject all = s_generator.Generate(WebApiServiceSet.AllServices, includeSchemas: true);
            JsonObject sessionless = s_generator.Generate(WebApiServiceSet.Sessionless, includeSchemas: true);

            Assert.That(sessionless["paths"]!.AsObject(), Has.Count.EqualTo(8));
            Assert.That(
                sessionless["paths"]!.AsObject().Select(p => p.Key),
                Is.SubsetOf(all["paths"]!.AsObject().Select(p => p.Key)));
            Assert.That(
                sessionless["components"]!["schemas"]!.AsObject(),
                Has.Count.LessThan(all["components"]!["schemas"]!.AsObject().Count));
        }

        [Test]
        public void SchemasAreSortedByNameAndGenerationIsRepeatable()
        {
            string first = s_generator.Generate(includeSchemas: true).ToJsonString();
            string second = new WebApiOpenApiGenerator().Generate(includeSchemas: true).ToJsonString();

            string[] names = [.. s_generator.Generate(includeSchemas: true)["components"]!["schemas"]!
                .AsObject().Select(s => s.Key)];
            Assert.That(second, Is.EqualTo(first));
            Assert.That(names, Is.EqualTo(names.OrderBy(n => n, StringComparer.Ordinal)));
        }

        [Test]
        public async Task ConcurrentGenerationsProduceTheSameDocumentAsync()
        {
            var generator = new WebApiOpenApiGenerator();

            string[] documents = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Task.Run(
                    () => generator.Generate(WebApiServiceSet.Sessionless, includeSchemas: true).ToJsonString())))
                .ConfigureAwait(false);

            Assert.That(documents.Distinct().Count(), Is.EqualTo(1));
        }

        [Test]
        public void GenerateThrowsForAnUndefinedServiceSet()
        {
            Assert.That(
                () => s_generator.Generate((WebApiServiceSet)42),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ConstructorThrowsForANullResolver()
        {
            Assert.That(
                () => new WebApiOpenApiGenerator(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ResolverWithoutTheServiceTypesIsEnoughForADocumentWithoutSchemas()
        {
            var generator = new WebApiOpenApiGenerator(new DataTypeDefinitionRegistry());

            JsonObject document = generator.Generate();

            Assert.That(document["paths"]!.AsObject(), Has.Count.EqualTo(WebApiServiceRoutes.Count));
        }

        [Test]
        public void ResolverWithoutTheServiceTypesFailsForADocumentWithSchemas()
        {
            var generator = new WebApiOpenApiGenerator(new DataTypeDefinitionRegistry());

            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => generator.Generate(includeSchemas: true));

            Assert.That(exception!.Message, Does.Contain("ReadRequest"));
        }

        [Test]
        public void BuiltInTypesAreDescribedAsTheEncoderWritesThem()
        {
            JsonObject schemas = GenerateWithProbe();
            JsonObject properties = schemas["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(Shape(properties, "Boolean"), Is.EqualTo("{\"type\":\"boolean\"}"));
                Assert.That(
                    Shape(properties, "SByte"),
                    Is.EqualTo("{\"type\":\"integer\",\"format\":\"int32\",\"minimum\":-128,\"maximum\":127}"));
                Assert.That(
                    Shape(properties, "Byte"),
                    Is.EqualTo("{\"type\":\"integer\",\"format\":\"int32\",\"minimum\":0,\"maximum\":255}"));
                Assert.That(
                    Shape(properties, "Int16"),
                    Is.EqualTo("{\"type\":\"integer\",\"format\":\"int32\",\"minimum\":-32768,\"maximum\":32767}"));
                Assert.That(
                    Shape(properties, "UInt16"),
                    Is.EqualTo("{\"type\":\"integer\",\"format\":\"int32\",\"minimum\":0,\"maximum\":65535}"));
                Assert.That(Shape(properties, "Int32"), Is.EqualTo("{\"type\":\"integer\",\"format\":\"int32\"}"));
                Assert.That(
                    Shape(properties, "UInt32"),
                    Is.EqualTo(
                        "{\"type\":\"integer\",\"format\":\"int64\",\"minimum\":0,\"maximum\":4294967295}"));

                // Part 6, 5.4.2.4: 64 bit integers are JSON strings.
                Assert.That(Shape(properties, "Int64"), Is.EqualTo("{\"type\":\"string\",\"format\":\"int64\"}"));
                Assert.That(Shape(properties, "UInt64"), Is.EqualTo("{\"type\":\"string\",\"format\":\"uint64\"}"));
                Assert.That(Shape(properties, "Float"), Is.EqualTo(FloatingPoint("float")));
                Assert.That(Shape(properties, "Double"), Is.EqualTo(FloatingPoint("double")));
                Assert.That(Shape(properties, "String"), Is.EqualTo("{\"type\":\"string\"}"));
                Assert.That(Shape(properties, "XmlElement"), Is.EqualTo("{\"type\":\"string\"}"));
                Assert.That(
                    Shape(properties, "DateTime"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"date-time\"}"));
                Assert.That(Shape(properties, "Guid"), Is.EqualTo("{\"type\":\"string\",\"format\":\"uuid\"}"));
                Assert.That(Shape(properties, "ByteString"), Is.EqualTo("{\"type\":\"string\",\"format\":\"byte\"}"));
                Assert.That(Shape(properties, "NodeId"), Is.EqualTo("{\"type\":\"string\",\"format\":\"UaNodeId\"}"));
                Assert.That(
                    Shape(properties, "ExpandedNodeId"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"UaExpandedNodeId\"}"));
                Assert.That(
                    Shape(properties, "QualifiedName"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"UaQualifiedName\"}"));
                Assert.That(
                    Shape(properties, "Enumeration"),
                    Is.EqualTo("{\"type\":\"integer\",\"format\":\"int32\"}"));
            });
        }

        [Test]
        public void StructuredBuiltInTypesAreReferencedAndAddedAsComponents()
        {
            JsonObject schemas = GenerateWithProbe();
            JsonObject properties = schemas["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(Shape(properties, "StatusCode"), Is.EqualTo(Ref("StatusCode")));
                Assert.That(Shape(properties, "LocalizedText"), Is.EqualTo(Ref("LocalizedText")));
                Assert.That(Shape(properties, "Variant"), Is.EqualTo(Ref("Variant")));
                Assert.That(Shape(properties, "DataValue"), Is.EqualTo(Ref("DataValue")));
                Assert.That(Shape(properties, "DiagnosticInfo"), Is.EqualTo(Ref("DiagnosticInfo")));
                Assert.That(Shape(properties, "ExtensionObject"), Is.EqualTo(Ref("ExtensionObject")));

                // Structure and the abstract Number carry an ExtensionObject and a Variant.
                Assert.That(Shape(properties, "Structure"), Is.EqualTo(Ref("ExtensionObject")));
                Assert.That(Shape(properties, "Number"), Is.EqualTo(Ref("Variant")));
                Assert.That(Shape(properties, "Duration"), Is.EqualTo(
                    FloatingPoint("double")));
            });
            foreach (string component in s_builtInComponents)
            {
                Assert.That(schemas.ContainsKey(component), Is.True, component);
            }
        }

        [Test]
        public void DataValueUsesTheStatusFieldNameOfPart6AndReferencesStatusCode()
        {
            JsonObject properties = GenerateWithProbe()["DataValue"]!["properties"]!.AsObject();

            Assert.That(
                properties.Select(p => p.Key),
                Is.EqualTo(s_dataValueProperties));
            Assert.That(Shape(properties, "Status"), Is.EqualTo(Ref("StatusCode")));
        }

        [Test]
        public void ArraysHaveItemsAndOtherRanksAcceptAnyValue()
        {
            JsonObject properties = GenerateWithProbe()["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(
                    Shape(properties, "Array"),
                    Is.EqualTo("{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"format\":\"int32\"}}"));
                Assert.That(Shape(properties, "AnyRank"), Is.EqualTo("{}"));
                Assert.That(Shape(properties, "ScalarOrArray"), Is.EqualTo("{}"));
            });
        }

        [Test]
        public void AFixedRankOfTwoOrMoreIsTheInlineMatrixObjectTheEncoderWrites()
        {
            JsonObject properties = GenerateWithProbe()["ReadRequest"]!["properties"]!.AsObject();
            const string dimensions =
                "{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"format\":\"int32\",\"minimum\":0}}";

            Assert.Multiple(() =>
            {
                // Part 6, 5.4.5: the flattened values in Array, the lengths in Dimensions.
                Assert.That(
                    Shape(properties, "Matrix"),
                    Is.EqualTo(
                        "{\"type\":\"object\",\"properties\":{\"Array\":{\"type\":\"array\",\"items\":" +
                        FloatingPoint("double") +
                        "},\"Dimensions\":" +
                        dimensions +
                        "}}"));
                Assert.That(
                    Shape(properties, "ThreeDimensions"),
                    Is.EqualTo(
                        "{\"type\":\"object\",\"properties\":{\"Array\":{\"type\":\"array\",\"items\":" +
                        "{\"type\":\"integer\",\"format\":\"int32\"}},\"Dimensions\":" +
                        dimensions +
                        "}}"));

                // The elements of a matrix are null elements like those of an array.
                Assert.That(
                    properties["StringMatrix"]!["properties"]!["Array"]!["items"]!.ToJsonString(),
                    Is.EqualTo("{\"type\":\"string\",\"nullable\":true}"));
                Assert.That(
                    properties["StructureMatrix"]!["properties"]!["Array"]!["items"]!.ToJsonString(),
                    Is.EqualTo(Nullable(Ref("ProbeNode"))));
            });
        }

        [Test]
        public void ArrayItemsThatTheEncoderWritesAsNullAreNullable()
        {
            // Part 6, 5.4.5: a null element of an array is the JSON literal null.
            JsonObject properties = GenerateWithProbe()["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(
                    ItemShape(properties, "StringArray"),
                    Is.EqualTo("{\"type\":\"string\",\"nullable\":true}"));
                Assert.That(
                    ItemShape(properties, "XmlElementArray"),
                    Is.EqualTo("{\"type\":\"string\",\"nullable\":true}"));
                Assert.That(
                    ItemShape(properties, "GuidArray"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"uuid\",\"nullable\":true}"));
                Assert.That(
                    ItemShape(properties, "ByteStringArray"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"byte\",\"nullable\":true}"));
                Assert.That(
                    ItemShape(properties, "NodeIdArray"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"UaNodeId\",\"nullable\":true}"));
                Assert.That(
                    ItemShape(properties, "ExpandedNodeIdArray"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"UaExpandedNodeId\",\"nullable\":true}"));
                Assert.That(
                    ItemShape(properties, "QualifiedNameArray"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"UaQualifiedName\",\"nullable\":true}"));
                Assert.That(ItemShape(properties, "LocalizedTextArray"), Is.EqualTo(Nullable(Ref("LocalizedText"))));
                Assert.That(ItemShape(properties, "VariantArray"), Is.EqualTo(Nullable(Ref("Variant"))));
                Assert.That(ItemShape(properties, "NumberArray"), Is.EqualTo(Nullable(Ref("Variant"))));
                Assert.That(ItemShape(properties, "DataValueArray"), Is.EqualTo(Nullable(Ref("DataValue"))));
                Assert.That(ItemShape(properties, "DiagnosticInfoArray"), Is.EqualTo(Nullable(Ref("DiagnosticInfo"))));
                Assert.That(
                    ItemShape(properties, "ExtensionObjectArray"),
                    Is.EqualTo(Nullable(Ref("ExtensionObject"))));
                Assert.That(ItemShape(properties, "StructureArray"), Is.EqualTo(Nullable(Ref("ProbeNode"))));
                Assert.That(ItemShape(properties, "UnionArray"), Is.EqualTo(Nullable(Ref("ProbeUnion"))));
            });
        }

        [Test]
        public void ArrayItemsThatTheEncoderAlwaysWritesAreNotNullable()
        {
            JsonObject properties = GenerateWithProbe()["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(
                    ItemShape(properties, "BooleanArray"),
                    Is.EqualTo("{\"type\":\"boolean\"}"));
                Assert.That(
                    ItemShape(properties, "DoubleArray"),
                    Is.EqualTo(FloatingPoint("double")));
                Assert.That(
                    ItemShape(properties, "DateTimeArray"),
                    Is.EqualTo("{\"type\":\"string\",\"format\":\"date-time\"}"));
                Assert.That(ItemShape(properties, "StatusCodeArray"), Is.EqualTo(Ref("StatusCode")));
                Assert.That(ItemShape(properties, "EnumerationArray"), Is.EqualTo(Ref("ProbeMode")));
                Assert.That(ItemShape(properties, "OptionSetArray"), Is.EqualTo(Ref("ProbeFlags")));
                Assert.That(ItemShape(properties, "UnknownArray"), Is.EqualTo("{}"));
            });
        }

        [Test]
        public void UnnamedFieldsGetAPositionalNameAndUnknownTypesAcceptAnyValue()
        {
            JsonObject properties = GenerateWithProbe()["ReadRequest"]!["properties"]!.AsObject();

            Assert.That(properties.Select(p => p.Key), Has.Some.StartsWith("Field"));
            Assert.That(Shape(properties, "Unknown"), Is.EqualTo("{}"));
        }

        [Test]
        public void EnumerationsListValuesAndNamesAndOptionSetsAreUnsignedIntegers()
        {
            JsonObject schemas = GenerateWithProbe();
            JsonObject properties = schemas["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(Shape(properties, "Mode"), Is.EqualTo(Ref("ProbeMode")));
                Assert.That(
                    schemas["ProbeMode"]!.ToJsonString(),
                    Is.EqualTo(
                        "{\"type\":\"integer\",\"format\":\"int32\",\"enum\":[0,1,5]," +
                        "\"x-enum-varnames\":[\"Off\",\"On\",\"Auto\"]}"));
                Assert.That(Shape(properties, "Flags"), Is.EqualTo(Ref("ProbeFlags")));
                Assert.That(
                    schemas["ProbeFlags"]!.ToJsonString(),
                    Is.EqualTo("{\"type\":\"integer\",\"format\":\"int64\",\"minimum\":0,\"maximum\":4294967295}"));
                Assert.That(Shape(properties, "Bits"), Is.EqualTo(Ref("ProbeBits")));
                Assert.That(
                    schemas["ProbeBits"]!["properties"]!.AsObject().Select(p => p.Key),
                    Is.EqualTo(s_optionSetProperties));
            });
        }

        [Test]
        public void UnionsAddTheSwitchFieldAndStructuresListTheirBase()
        {
            JsonObject schemas = GenerateWithProbe();
            JsonObject properties = schemas["ReadRequest"]!["properties"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(Shape(properties, "Choice"), Is.EqualTo(Ref("ProbeUnion")));
                Assert.That(
                    schemas["ProbeUnion"]!["properties"]!.AsObject().Select(p => p.Key),
                    Is.EqualTo(s_unionProperties));
                Assert.That(Shape(properties, "Derived"), Is.EqualTo(Ref("ProbeDerived")));
                Assert.That(schemas["ProbeDerived"]!["allOf"]!.ToJsonString(), Is.EqualTo("[" + Ref("ProbeBase") + "]"));
                Assert.That(
                    schemas["ProbeDerived"]!["properties"]!.AsObject().Select(p => p.Key),
                    Is.EqualTo(s_derivedProperties));
                Assert.That(schemas["ProbeBase"]!.AsObject().ContainsKey("allOf"), Is.False);
            });
        }

        [Test]
        public void DefinitionsThatAreNeitherStructuresNorEnumerationsAreObjects()
        {
            JsonObject schemas = GenerateWithProbe();

            Assert.That(schemas["ProbeOther"]!.ToJsonString(), Is.EqualTo("{\"type\":\"object\"}"));
        }

        [Test]
        public void StructuresThatReferenceThemselvesAreDescribedOnce()
        {
            JsonObject schemas = GenerateWithProbe();

            Assert.That(
                schemas["ProbeNode"]!["properties"]!["Child"]!.ToJsonString(),
                Is.EqualTo(Ref("ProbeNode")));
        }

        private static JsonObject GenerateWithProbe()
        {
            var generator = new WebApiOpenApiGenerator(new ProbeResolver());
            return generator.Generate(includeSchemas: true)["components"]!["schemas"]!.AsObject();
        }

        private static string Shape(JsonObject properties, string name)
        {
            return properties[name]!.ToJsonString();
        }

        private static string FloatingPoint(string format)
        {
            // NaN and the infinities are written as strings (Part 6, 5.4.2).
            return "{\"oneOf\":[{\"type\":\"number\",\"format\":\"" +
                format +
                "\"}," +
                "{\"type\":\"string\",\"enum\":[\"NaN\",\"Infinity\",\"-Infinity\"]}]}";
        }

        private static string ItemShape(JsonObject properties, string name)
        {
            return properties[name]!["items"]!.ToJsonString();
        }

        private static string Ref(string name)
        {
            return "{\"$ref\":\"#/components/schemas/" + name + "\"}";
        }

        private static string Nullable(string reference)
        {
            return "{\"nullable\":true,\"allOf\":[" + reference + "]}";
        }

        private static IEnumerable<string> CollectReferences(JsonNode node)
        {
            switch (node)
            {
                case JsonObject jsonObject:
                    foreach (KeyValuePair<string, JsonNode?> member in jsonObject)
                    {
                        if (member.Key == "$ref")
                        {
                            yield return (string)member.Value!;
                        }
                        else if (member.Value != null)
                        {
                            foreach (string reference in CollectReferences(member.Value))
                            {
                                yield return reference;
                            }
                        }
                    }
                    break;
                case JsonArray jsonArray:
                    foreach (JsonNode? item in jsonArray)
                    {
                        if (item == null)
                        {
                            continue;
                        }
                        foreach (string reference in CollectReferences(item))
                        {
                            yield return reference;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// A data type definition that is neither a structure nor an enumeration.
        /// </summary>
        private sealed class OtherDefinition : DataTypeDefinition
        {
            public override void Encode(IEncoder encoder)
            {
                throw new NotSupportedException();
            }

            public override void Decode(IDecoder decoder)
            {
                throw new NotSupportedException();
            }

            public override bool IsEqual(IEncodeable? encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public override object Clone()
            {
                return this;
            }
        }

        /// <summary>
        /// Resolves the standard types, but gives the ReadRequest the fields
        /// of a probe structure: one field per built-in type and one for
        /// each structure, union, enumeration and option set variant the
        /// generator handles.
        /// </summary>
        private sealed class ProbeResolver : IDataTypeDefinitionResolver
        {
            public ProbeResolver()
            {
                m_standard = new EncodeableFactoryDefinitionSource(
                    EncodeableFactory.Create(),
                    new NamespaceTable());
                m_probe = BuildProbe();
                foreach (UaTypeDescription type in BuildTypes())
                {
                    m_custom.Add(type);
                }
            }

            public bool TryResolve(
                ExpandedNodeId typeId,
                [NotNullWhen(true)] out UaTypeDescription? description)
            {
                return m_custom.TryResolve(typeId, out description) ||
                    m_standard.TryResolve(typeId, out description);
            }

            public bool TryResolve(
                NodeId typeId,
                [NotNullWhen(true)] out UaTypeDescription? description)
            {
                return m_custom.TryResolve(typeId, out description) ||
                    m_standard.TryResolve(typeId, out description);
            }

            public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
            {
                var types = new List<UaTypeDescription>();
                foreach (UaTypeDescription type in m_standard.GetNamespaceTypes(namespaceUri))
                {
                    // ReadRequest points at the probe; the other routes keep their types.
                    types.Add(type.Name == "ReadRequest" ? WithDefinition(type, m_probe) : type);
                }
                return types;
            }

            private static UaTypeDescription WithDefinition(UaTypeDescription type, DataTypeDefinition definition)
            {
                return new UaTypeDescription(type.TypeId, type.BrowseName, definition, type.NamespaceUri);
            }

            private static StructureDefinition BuildProbe()
            {
                (string Name, NodeId DataType, int ValueRank)[] fields =
                [
                    ("Boolean", Id(BuiltInType.Boolean), ValueRanks.Scalar),
                    ("SByte", Id(BuiltInType.SByte), ValueRanks.Scalar),
                    ("Byte", Id(BuiltInType.Byte), ValueRanks.Scalar),
                    ("Int16", Id(BuiltInType.Int16), ValueRanks.Scalar),
                    ("UInt16", Id(BuiltInType.UInt16), ValueRanks.Scalar),
                    ("Int32", Id(BuiltInType.Int32), ValueRanks.Scalar),
                    ("UInt32", Id(BuiltInType.UInt32), ValueRanks.Scalar),
                    ("Int64", Id(BuiltInType.Int64), ValueRanks.Scalar),
                    ("UInt64", Id(BuiltInType.UInt64), ValueRanks.Scalar),
                    ("Float", Id(BuiltInType.Float), ValueRanks.Scalar),
                    ("Double", Id(BuiltInType.Double), ValueRanks.Scalar),
                    ("String", Id(BuiltInType.String), ValueRanks.Scalar),
                    ("XmlElement", Id(BuiltInType.XmlElement), ValueRanks.Scalar),
                    ("DateTime", Id(BuiltInType.DateTime), ValueRanks.Scalar),
                    ("Guid", Id(BuiltInType.Guid), ValueRanks.Scalar),
                    ("ByteString", Id(BuiltInType.ByteString), ValueRanks.Scalar),
                    ("NodeId", Id(BuiltInType.NodeId), ValueRanks.Scalar),
                    ("ExpandedNodeId", Id(BuiltInType.ExpandedNodeId), ValueRanks.Scalar),
                    ("QualifiedName", Id(BuiltInType.QualifiedName), ValueRanks.Scalar),
                    ("Enumeration", Id(BuiltInType.Enumeration), ValueRanks.Scalar),
                    ("StatusCode", Id(BuiltInType.StatusCode), ValueRanks.Scalar),
                    ("LocalizedText", Id(BuiltInType.LocalizedText), ValueRanks.Scalar),
                    ("Variant", Id(BuiltInType.Variant), ValueRanks.Scalar),
                    ("DataValue", Id(BuiltInType.DataValue), ValueRanks.Scalar),
                    ("DiagnosticInfo", Id(BuiltInType.DiagnosticInfo), ValueRanks.Scalar),
                    ("ExtensionObject", Id(BuiltInType.ExtensionObject), ValueRanks.Scalar),
                    ("Structure", DataTypeIds.Structure, ValueRanks.Scalar),
                    ("Number", DataTypeIds.Number, ValueRanks.Scalar),
                    ("Duration", DataTypeIds.Duration, ValueRanks.Scalar),
                    ("Array", Id(BuiltInType.Int32), ValueRanks.OneDimension),
                    ("Matrix", Id(BuiltInType.Double), 2),
                    ("ThreeDimensions", Id(BuiltInType.Int32), 3),
                    ("StringMatrix", Id(BuiltInType.String), 2),
                    ("StructureMatrix", Custom(kNode), 2),
                    ("BooleanArray", Id(BuiltInType.Boolean), ValueRanks.OneDimension),
                    ("DoubleArray", Id(BuiltInType.Double), ValueRanks.OneDimension),
                    ("DateTimeArray", Id(BuiltInType.DateTime), ValueRanks.OneDimension),
                    ("StatusCodeArray", Id(BuiltInType.StatusCode), ValueRanks.OneDimension),
                    ("EnumerationArray", Custom(kMode), ValueRanks.OneDimension),
                    ("OptionSetArray", Custom(kFlags), ValueRanks.OneDimension),
                    ("UnknownArray", new NodeId(9999, SchemaTestData.TestNamespaceIndex), ValueRanks.OneDimension),
                    ("StringArray", Id(BuiltInType.String), ValueRanks.OneDimension),
                    ("XmlElementArray", Id(BuiltInType.XmlElement), ValueRanks.OneDimension),
                    ("GuidArray", Id(BuiltInType.Guid), ValueRanks.OneDimension),
                    ("ByteStringArray", Id(BuiltInType.ByteString), ValueRanks.OneDimension),
                    ("NodeIdArray", Id(BuiltInType.NodeId), ValueRanks.OneDimension),
                    ("ExpandedNodeIdArray", Id(BuiltInType.ExpandedNodeId), ValueRanks.OneDimension),
                    ("QualifiedNameArray", Id(BuiltInType.QualifiedName), ValueRanks.OneDimension),
                    ("LocalizedTextArray", Id(BuiltInType.LocalizedText), ValueRanks.OneDimension),
                    ("VariantArray", Id(BuiltInType.Variant), ValueRanks.OneDimension),
                    ("NumberArray", DataTypeIds.Number, ValueRanks.OneDimension),
                    ("DataValueArray", Id(BuiltInType.DataValue), ValueRanks.OneDimension),
                    ("DiagnosticInfoArray", Id(BuiltInType.DiagnosticInfo), ValueRanks.OneDimension),
                    ("ExtensionObjectArray", Id(BuiltInType.ExtensionObject), ValueRanks.OneDimension),
                    ("StructureArray", Custom(kNode), ValueRanks.OneDimension),
                    ("UnionArray", Custom(kUnion), ValueRanks.OneDimension),
                    ("AnyRank", Id(BuiltInType.Int32), ValueRanks.Any),
                    ("ScalarOrArray", Id(BuiltInType.Int32), ValueRanks.ScalarOrOneDimension),
                    (string.Empty, Id(BuiltInType.Int32), ValueRanks.Scalar),
                    ("Unknown", new NodeId(9999, SchemaTestData.TestNamespaceIndex), ValueRanks.Scalar),
                    ("Mode", Custom(kMode), ValueRanks.Scalar),
                    ("Flags", Custom(kFlags), ValueRanks.Scalar),
                    ("Bits", Custom(kBits), ValueRanks.Scalar),
                    ("Choice", Custom(kUnion), ValueRanks.Scalar),
                    ("Derived", Custom(kDerived), ValueRanks.Scalar),
                    ("Node", Custom(kNode), ValueRanks.Scalar),
                    ("Other", Custom(kOther), ValueRanks.Scalar)
                ];
                return new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields = [.. fields.Select(f => SchemaTestData.Field(f.Name, f.DataType, f.ValueRank))]
                };
            }

            private static IEnumerable<UaTypeDescription> BuildTypes()
            {
                yield return SchemaTestData.Enumeration(kMode, "ProbeMode", ("Off", 0), ("On", 1), ("Auto", 5));
                yield return new UaTypeDescription(
                    new ExpandedNodeId(new NodeId(kFlags, SchemaTestData.TestNamespaceIndex)),
                    new QualifiedName("ProbeFlags", SchemaTestData.TestNamespaceIndex),
                    new EnumDefinition
                    {
                        IsOptionSet = true,
                        Fields = [new EnumField { Name = "A", Value = 0 }, new EnumField { Name = "B", Value = 1 }]
                    },
                    SchemaTestData.TestNamespace);
                yield return new UaTypeDescription(
                    new ExpandedNodeId(new NodeId(kBits, SchemaTestData.TestNamespaceIndex)),
                    new QualifiedName("ProbeBits", SchemaTestData.TestNamespaceIndex),
                    new EnumDefinition
                    {
                        IsOptionSet = true,
                        Fields = [new EnumField { Name = "A", Value = 0 }]
                    },
                    SchemaTestData.TestNamespace,
                    isStructureOptionSet: true);
                yield return SchemaTestData.Union(
                    kUnion,
                    "ProbeUnion",
                    SchemaTestData.Field("Number", Id(BuiltInType.Int32)),
                    SchemaTestData.Field("Text", Id(BuiltInType.String)));
                yield return SchemaTestData.Structure(
                    kBase,
                    "ProbeBase",
                    SchemaTestData.Field("Own", Id(BuiltInType.Int32)));
                yield return new UaTypeDescription(
                    new ExpandedNodeId(new NodeId(kDerived, SchemaTestData.TestNamespaceIndex)),
                    new QualifiedName("ProbeDerived", SchemaTestData.TestNamespaceIndex),
                    new StructureDefinition
                    {
                        BaseDataType = new NodeId(kBase, SchemaTestData.TestNamespaceIndex),
                        StructureType = StructureType.Structure,
                        Fields = [SchemaTestData.Field("Extra", Id(BuiltInType.Int32))]
                    },
                    SchemaTestData.TestNamespace);
                yield return SchemaTestData.Structure(
                    kNode,
                    "ProbeNode",
                    SchemaTestData.Field("Child", Custom(kNode)));
                yield return new UaTypeDescription(
                    new ExpandedNodeId(new NodeId(kOther, SchemaTestData.TestNamespaceIndex)),
                    new QualifiedName("ProbeOther", SchemaTestData.TestNamespaceIndex),
                    new OtherDefinition(),
                    SchemaTestData.TestNamespace);
            }

            private static NodeId Id(BuiltInType type)
            {
                return new NodeId((uint)type);
            }

            private static NodeId Custom(uint id)
            {
                return new NodeId(id, SchemaTestData.TestNamespaceIndex);
            }

            private const uint kMode = 7001;
            private const uint kFlags = 7002;
            private const uint kBits = 7003;
            private const uint kUnion = 7004;
            private const uint kBase = 7005;
            private const uint kDerived = 7006;
            private const uint kNode = 7007;
            private const uint kOther = 7008;

            private readonly EncodeableFactoryDefinitionSource m_standard;
            private readonly DataTypeDefinitionRegistry m_custom = new();
            private readonly StructureDefinition m_probe;
        }
    }
}
