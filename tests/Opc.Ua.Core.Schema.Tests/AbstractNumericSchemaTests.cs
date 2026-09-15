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

using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Json.Schema;
using NUnit.Framework;
using Opc.Ua.Schema.Bsd;
using Opc.Ua.Schema.Json;
using Opc.Ua.Schema.Xsd;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Verifies the Variant wire encoding of abstract numeric structure fields.
    /// </summary>
    [TestFixture]
    public sealed class AbstractNumericSchemaTests
    {
        [Test]
        public void AbstractNumericScalarAndArrayFieldsUseVariantEncoding(
            [Values(BuiltInType.Number, BuiltInType.Integer, BuiltInType.UInteger)] BuiltInType builtInType,
            [Values(UaSchemaFormat.JsonCompact, UaSchemaFormat.JsonVerbose, UaSchemaFormat.Xsd, UaSchemaFormat.Bsd)]
            UaSchemaFormat format)
        {
            UaTypeDescription type = NumericType(builtInType);
            IUaSchema schema = CreateProvider(type).CreateSchema(type, format);

            if (format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose)
            {
                JsonNode document = JsonNode.Parse(schema.ToSchemaString())!;
                JsonNode properties = document["$defs"]!["NumericFields"]!["properties"]!;
                Assert.That(properties["Scalar"]!.AsObject().ContainsKey("$ref"), Is.True,
                    "Abstract numeric fields must not become unconstrained schemas.");
                Assert.That(properties["Scalar"]!["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/Ua_Variant"));
                Assert.That(properties["Array"]!["items"]!["$ref"]!.GetValue<string>(),
                    Is.EqualTo("#/$defs/Ua_Variant"));
                Assert.That(document["$defs"]!["Ua_Variant"], Is.Not.Null);
            }
            else
            {
                var document = XDocument.Parse(schema.ToSchemaString());
                string fieldTag = format == UaSchemaFormat.Xsd ? "element" : "Field";
                string nameAttribute = format == UaSchemaFormat.Xsd ? "name" : "Name";
                string typeAttribute = format == UaSchemaFormat.Xsd ? "type" : "TypeName";
                XElement scalar = document.Descendants().Single(element =>
                    element.Name.LocalName == fieldTag && (string?)element.Attribute(nameAttribute) == "Scalar");
                XElement array = document.Descendants().Single(element =>
                    element.Name.LocalName == fieldTag && (string?)element.Attribute(nameAttribute) == "Array");
                if (format == UaSchemaFormat.Xsd)
                {
                    array = array.Descendants().Single(element => element.Name.LocalName == "element");
                }
                Assert.That((string?)scalar.Attribute(typeAttribute), Is.EqualTo("ua:Variant"));
                Assert.That((string?)array.Attribute(typeAttribute), Is.EqualTo("ua:Variant"));
            }
        }

        [Test]
        public void AbstractNumericJsonSchemaValidatesEncodedVariantsAndRejectsBareValues(
            [Values(BuiltInType.Number, BuiltInType.Integer, BuiltInType.UInteger)] BuiltInType builtInType,
            [Values(false, true)] bool verbose)
        {
            UaTypeDescription type = NumericType(builtInType);
            IUaSchema schema = CreateProvider(type).GetJsonSchema(type, verbose);
            Variant value = builtInType switch
            {
                BuiltInType.Integer => Variant.From(-42L),
                BuiltInType.UInteger => Variant.From(ulong.MaxValue),
                _ => Variant.From(12.5d)
            };
            JsonNode encoded = Encode(value, verbose);

            EvaluationResults valid = Evaluate(schema, encoded);
            Assert.That(valid.IsValid, Is.True, valid.ToString());
            Assert.That(encoded["Scalar"]!["UaType"], Is.Not.Null);
            Assert.That(encoded["Scalar"]!["Value"], Is.Not.Null);

            JsonNode bareScalar = encoded.DeepClone();
            bareScalar["Scalar"] = 12.5;
            Assert.That(Evaluate(schema, bareScalar).IsValid, Is.False);

            JsonNode bareArrayElement = encoded.DeepClone();
            bareArrayElement["Array"]![0] = 12.5;
            Assert.That(Evaluate(schema, bareArrayElement).IsValid, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NumericVariantDefaultPayloadUsesTheDeclaredWireType(bool verbose)
        {
            UaTypeDescription type = NumericType(BuiltInType.Number);
            IUaSchema schema = CreateProvider(type).GetJsonSchema(type, verbose);
            JsonNode encoded = Encode(Variant.From(0d), verbose);

            EvaluationResults result = Evaluate(schema, encoded);

            Assert.That(result.IsValid, Is.True, result.ToString());
            Assert.That(encoded["Scalar"]!["UaType"]!.GetValue<int>(), Is.EqualTo(11));
            Assert.That(encoded["Scalar"]!.AsObject().ContainsKey("Value"), Is.EqualTo(verbose));
        }

        [Test]
        public void AbstractNumericJsonSchemaRequiresTheCurrentVariantEnvelope()
        {
            UaTypeDescription type = NumericType(BuiltInType.Number);
            IUaSchema schema = CreateProvider(type).GetJsonSchema(type);
            JsonNode missingType = Encode(Variant.From(12.5d), verbose: false);
            missingType["Scalar"]!.AsObject().Remove("UaType");
            JsonNode legacyEnvelope = Encode(Variant.From(12.5d), verbose: false);
            legacyEnvelope["Scalar"] = new JsonObject { ["Type"] = 11, ["Body"] = 12.5 };
            JsonNode abstractTypeTag = Encode(Variant.From(12.5d), verbose: false);
            abstractTypeTag["Scalar"]!["UaType"] = 26;

            Assert.That(Evaluate(schema, missingType).IsValid, Is.False);
            Assert.That(Evaluate(schema, legacyEnvelope).IsValid, Is.False);
            Assert.That(Evaluate(schema, abstractTypeTag).IsValid, Is.False);
        }

        [Test]
        public void FieldEncodingClassificationPreservesPrimitiveAliasesAndCustomIdentity()
        {
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(DataTypeIds.Number), Is.EqualTo(BuiltInType.Variant));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(DataTypeIds.Integer), Is.EqualTo(BuiltInType.Variant));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(DataTypeIds.UInteger), Is.EqualTo(BuiltInType.Variant));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(DataTypeIds.Duration), Is.EqualTo(BuiltInType.Double));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(DataTypeIds.DiagnosticInfo),
                Is.EqualTo(BuiltInType.DiagnosticInfo));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(DataTypeIds.Enumeration),
                Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(new NodeId(26u, 1)), Is.EqualTo(BuiltInType.Null));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(new NodeId("26", 0)), Is.EqualTo(BuiltInType.Null));
            Assert.That(SchemaTypeInfo.GetFieldEncodingType(NodeId.Null), Is.EqualTo(BuiltInType.Null));
            Assert.That(TypeInfo.GetBuiltInType(DataTypeIds.Number), Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public void NumericIdentifiersInAnotherNamespaceResolveAsCustomStructures(
            [Values(26u, 27u, 28u)] uint identifier)
        {
            UaTypeDescription custom = SchemaTestData.Structure(
                identifier, "ApplicationNumber",
                SchemaTestData.Field("Value", DataTypeIds.Int32));
            UaTypeDescription root = SchemaTestData.Structure(
                3951, "CustomRoot",
                SchemaTestData.Field("Value", new NodeId(identifier, SchemaTestData.TestNamespaceIndex)));
            var registry = new DataTypeDefinitionRegistry();
            registry.Add(custom);
            registry.Add(root);
            var provider = new DefaultSchemaProvider(registry, [new JsonSchemaGenerator()]);

            JsonNode schema = JsonNode.Parse(provider.GetJsonSchema(root).ToSchemaString())!;

            Assert.That(schema["$defs"]!["CustomRoot"]!["properties"]!["Value"]!["$ref"]!.GetValue<string>(),
                Is.EqualTo("#/$defs/ApplicationNumber"));
        }

        private static UaTypeDescription NumericType(BuiltInType builtInType)
        {
            NodeId dataType = SchemaTestData.BuiltIn(builtInType);
            return SchemaTestData.Structure(
                3950, "NumericFields",
                SchemaTestData.Field("Scalar", dataType),
                SchemaTestData.Field("Array", dataType, ValueRanks.OneDimension));
        }

        private static DefaultSchemaProvider CreateProvider(UaTypeDescription type)
        {
            var registry = new DataTypeDefinitionRegistry();
            registry.Add(type);
            return new DefaultSchemaProvider(
                registry, [new JsonSchemaGenerator(), new XsdSchemaGenerator(), new BsdSchemaGenerator()]);
        }

        private static JsonNode Encode(Variant value, bool verbose)
        {
            using var encoder = new JsonEncoder(
                ServiceMessageContext.Create(null), verbose ? JsonEncoderOptions.Verbose : JsonEncoderOptions.Compact);
            encoder.WriteVariant("Scalar", value);
            encoder.WriteVariantArray("Array", [value, Variant.Null]);
            return JsonNode.Parse(encoder.CloseAndReturnText())!;
        }

        private static EvaluationResults Evaluate(IUaSchema schema, JsonNode instance)
        {
            JsonSchema compiled = JsonSchema.FromText(
                schema.ToSchemaString(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            return compiled.Evaluate(
                JsonSerializer.SerializeToElement(instance),
                new EvaluationOptions { OutputFormat = OutputFormat.List });
        }
    }
}
