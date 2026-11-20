/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Schema.Tests.OpenApi
{
    /// <summary>
    /// Tests of the OpenAPI schemas of the built-in types.
    /// </summary>
    [TestFixture]
    [Category("WebApiOpenApi")]
    [Parallelizable]
    public class OpenApiBuiltInSchemasTests
    {
        private static readonly string[] s_extensionObjectProperties = ["UaTypeId", "UaEncoding", "UaBody"];

        [Test]
        public void UndescribedBuiltInTypesAcceptAnyValue()
        {
            JsonObject schema = OpenApiBuiltInSchemas.CreateScalar(BuiltInType.Null);

            Assert.That(schema.ToJsonString(), Is.EqualTo("{}"));
        }

        [Test]
        public void ScalarBuiltInTypesHaveNoComponent()
        {
            Assert.That(OpenApiBuiltInSchemas.GetComponentName(BuiltInType.Int32), Is.Null);
            Assert.That(OpenApiBuiltInSchemas.GetComponentName(BuiltInType.Null), Is.Null);
        }

        [TestCase(BuiltInType.StatusCode, "StatusCode")]
        [TestCase(BuiltInType.LocalizedText, "LocalizedText")]
        [TestCase(BuiltInType.ExtensionObject, "ExtensionObject")]
        [TestCase(BuiltInType.DataValue, "DataValue")]
        [TestCase(BuiltInType.DiagnosticInfo, "DiagnosticInfo")]
        [TestCase(BuiltInType.Variant, "Variant")]
        public void StructuredBuiltInTypesAreComponents(BuiltInType type, string name)
        {
            Assert.That(OpenApiBuiltInSchemas.GetComponentName(type), Is.EqualTo(name));
            JsonObject component = OpenApiBuiltInSchemas.CreateComponent(name);
            Assert.That((string?)component["type"], Is.EqualTo("object"));
            Assert.That(component["properties"]!.AsObject(), Is.Not.Empty);
        }

        [Test]
        public void ExtensionObjectAllowsTheInlineBodyOfAJsonEncodedStructure()
        {
            JsonObject component = OpenApiBuiltInSchemas.CreateComponent("ExtensionObject");

            Assert.That(component.ContainsKey("additionalProperties"), Is.False);
            Assert.That(
                component["properties"]!.AsObject().Select(p => p.Key),
                Is.EqualTo(s_extensionObjectProperties));
        }

        [TestCase(BuiltInType.String, true)]
        [TestCase(BuiltInType.XmlElement, true)]
        [TestCase(BuiltInType.Guid, true)]
        [TestCase(BuiltInType.ByteString, true)]
        [TestCase(BuiltInType.NodeId, true)]
        [TestCase(BuiltInType.ExpandedNodeId, true)]
        [TestCase(BuiltInType.QualifiedName, true)]
        [TestCase(BuiltInType.LocalizedText, true)]
        [TestCase(BuiltInType.ExtensionObject, true)]
        [TestCase(BuiltInType.DataValue, true)]
        [TestCase(BuiltInType.DiagnosticInfo, true)]
        [TestCase(BuiltInType.Variant, true)]
        [TestCase(BuiltInType.Boolean, false)]
        [TestCase(BuiltInType.Int32, false)]
        [TestCase(BuiltInType.UInt64, false)]
        [TestCase(BuiltInType.Double, false)]
        [TestCase(BuiltInType.DateTime, false)]
        [TestCase(BuiltInType.StatusCode, false)]
        [TestCase(BuiltInType.Enumeration, false)]
        public void OnlyTheTypesWithANullValueCanBeNull(BuiltInType type, bool expected)
        {
            Assert.That(OpenApiBuiltInSchemas.CanBeNull(type), Is.EqualTo(expected));
        }

        [Test]
        public void AnInlineSchemaIsMadeNullableInPlace()
        {
            JsonObject schema = OpenApiBuiltInSchemas.MakeNullable(
                OpenApiBuiltInSchemas.CreateScalar(BuiltInType.String));

            Assert.That(schema.ToJsonString(), Is.EqualTo("{\"type\":\"string\",\"nullable\":true}"));
        }

        [Test]
        public void AReferenceIsMadeNullableInAnAllOfBecauseItCarriesNoKeywords()
        {
            JsonObject schema = OpenApiBuiltInSchemas.MakeNullable(
                OpenApiBuiltInSchemas.Reference("DataValue"));

            Assert.That(
                schema.ToJsonString(),
                Is.EqualTo("{\"nullable\":true,\"allOf\":[{\"$ref\":\"#/components/schemas/DataValue\"}]}"));
        }

        [Test]
        public void AMatrixIsAnObjectWithTheFlattenedValuesAndTheDimensions()
        {
            JsonObject schema = OpenApiBuiltInSchemas.CreateMatrix(
                OpenApiBuiltInSchemas.CreateScalar(BuiltInType.Boolean));

            Assert.That(
                schema.ToJsonString(),
                Is.EqualTo(
                    "{\"type\":\"object\",\"properties\":{\"Array\":{\"type\":\"array\",\"items\":" +
                    "{\"type\":\"boolean\"}},\"Dimensions\":{\"type\":\"array\",\"items\":" +
                    "{\"type\":\"integer\",\"format\":\"int32\",\"minimum\":0}}}}"));
        }
    }
}
