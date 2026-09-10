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
 *
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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Four channels can say what a Variable's DataType is, and they are
    /// ranked: <c>uav:mapToType</c>, then an authored DataType definition, then
    /// <c>uav:dataTypeId</c>, then what the json type implies.
    /// </summary>
    /// <remarks>
    /// The ranking settles which statement is read when several are present.
    /// It does not excuse a document that makes two different definitive
    /// statements: silently taking the higher-ranked one would leave a Variable
    /// typed against a statement its own author contradicted, and the
    /// contradiction would surface only where a value failed to encode.
    /// A definitive identity may refine inference, but the accompanying value
    /// schema must describe the same semantic contract.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotDataTypeAuthorityTests
    {
        private const string DefinitionId = "ns=1;s=DataTypes/Reading";

        [Test]
        public void MapToTypeOutranksAnAuthoredDefinition()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:mapToType\":\"i=11\"," +
                    "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"},",
                    expectError: true),
                Is.EqualTo("i=11"));
        }

        [Test]
        public void MapToTypeOutranksDataTypeId()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\",",
                    expectError: true),
                Is.EqualTo("i=11"));
        }

        [Test]
        public void AnAuthoredDefinitionOutranksDataTypeId()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"}," +
                    "\"uav:dataTypeId\":\"i=12\",",
                    expectError: true,
                    valueSchema: DefinitionValueSchema),
                Is.EqualTo(DefinitionId));
        }

        /// <summary>
        /// A compatible inferred base is refined by the definitive identity,
        /// without changing the value schema's semantic contract.
        /// </summary>
        [TestCase("\"uav:mapToType\":\"i=11\",", "i=11")]
        [TestCase("\"uav:dataTypeId\":\"i=11\",", "i=11")]
        [TestCase(
            "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"},",
            DefinitionId)]
        public void ADefinitiveStatementSilentlyOverridesInference(
            string terms, string expected)
        {
            Assert.That(DataTypeOf(
                terms, expectError: false,
                valueSchema: expected == DefinitionId ? DefinitionValueSchema : "\"type\":\"number\","),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// Two definitive statements that name the same type agree, so nothing
        /// is reported: the check is about contradiction, not redundancy.
        /// </summary>
        [Test]
        public void TwoDefinitiveStatementsThatAgreeAreAccepted()
        {
            Assert.That(
                DataTypeOf(
                    "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=11\",",
                    expectError: false),
                Is.EqualTo("i=11"));
        }

        [Test]
        public void TheDisagreementNamesBothTermsAndBothTypes()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\",");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ValidationError &&
                    d.Message.Contains("uav:mapToType 'i=11'", StringComparison.Ordinal) &&
                    d.Message.Contains("uav:dataTypeId 'i=12'", StringComparison.Ordinal)),
                Is.True,
                Messages(result));
        }

        /// <summary>
        /// All three at once is one contradiction per disagreeing pair, so a
        /// reader is told about every statement it has to reconcile rather than
        /// only the first.
        /// </summary>
        [Test]
        public void EveryDisagreeingPairIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:mapToType\":\"i=11\"," +
                "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:pump#Reading\"}," +
                "\"uav:dataTypeId\":\"i=12\",");

            Assert.That(
                result.Diagnostics.Count(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.ValidationError &&
                    d.Message.Contains("definitive statements", StringComparison.Ordinal)),
                Is.EqualTo(3),
                Messages(result));
        }

        /// <summary>
        /// A definitive statement stated once cannot disagree with itself, so a
        /// perfectly ordinary document gains no diagnostic from the check.
        /// </summary>
        [Test]
        public void ASingleDefinitiveStatementIsNeverAContradiction()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:mapToType\":\"i=11\",");

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty,
                Messages(result));
        }

        /// <summary>
        /// A DataSchema with no BrowseName of its own still names the
        /// contradiction it makes, falling back to its title, so the reader is
        /// not handed a diagnostic that points at nothing.
        /// </summary>
        [Test]
        public void ASchemaWithoutABrowseNameIsStillLocated()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"properties\":{\"speed\":{\"type\":\"string\"," +
                "\"title\":\"Speed\"," +
                "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\"}}}");

            using var document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ValidationError &&
                    d.Location?.Reference == "Speed"),
                Is.True,
                Messages(result));
        }

        [Test]
        public void InferredFieldOrderCannotOmitARequiredProperty()
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                                     /*lang=json,strict*/
                                     """
                {
                  "type": "object",
                  "uav:dataTypeName": "pump:Reading",
                  "properties": { "A": { "type": "boolean" }, "B": { "type": "boolean" } },
                  "required": ["A", "B"],
                  "uav:fieldOrder": ["A"]
                }
                """);

            Assert.That(result.HasErrors, Is.True, Messages(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Message.Contains("fieldOrder", StringComparison.Ordinal)), Is.True, Messages(result));
        }

        [Test]
        public void AnAuthoritativeDoubleRejectsAStringValueSchema()
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                                     /*lang=json,strict*/
                                     """{ "type": "string", "uav:mapToType": "i=11" }""");

            Assert.That(result.HasErrors, Is.True, Messages(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError &&
                diagnostic.Message.Contains("i=11", StringComparison.Ordinal)), Is.True, Messages(result));
        }

        [TestCase("""["A","A"]""")]
        [TestCase("""["A","C"]""")]
        [TestCase("""["A",1]""")]
        [TestCase("\"A\"")]
        [TestCase("null")]
        [TestCase("[]")]
        public void InferredFieldOrderIsAnExactPermutation(string order)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "object",
                  "uav:dataTypeName": "pump:Reading",
                  "properties": { "A": { "type": "boolean" }, "B": { "type": "boolean" } },
                  "required": ["A", "B"],
                  "uav:fieldOrder": {{order}}
                }
                """);

            Assert.That(result.Success, Is.False);
            WotDiagnostic error = result.Diagnostics.Single(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid);
            Assert.That(error.Location?.JsonPointer, Is.EqualTo("/properties/value/uav:fieldOrder"));
            Assert.That(error.Location?.Reference, Is.EqualTo("pump:Reading"));
            Assert.That(error.Message, Does.Contain("every property exactly once"));
        }

        [TestCase("""["A","B"]""", "A", "B")]
        [TestCase("""["B","A"]""", "B", "A")]
        public void CompleteFieldOrderPreservesExactFields(string order, string first, string second)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "object",
                  "uav:dataTypeName": "pump:Reading",
                  "properties": { "A": { "type": "boolean" }, "B": { "type": "boolean" } },
                  "required": ["B", "A"],
                  "uav:fieldOrder": {{order}}
                }
                """);

            Assert.That(result.Success, Is.True, Messages(result));
            UADataType type = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(type.NodeId, Is.EqualTo(DefinitionId));
            Assert.That(type.Definition!.Field!.Select(field =>
                (field.Name, field.DataType, field.ValueRank, field.IsOptional, field.AllowSubTypes)),
                Is.EqualTo([(first, "i=1", -1, false, false), (second, "i=1", -1, false, false)]));
            Assert.That(result.Value.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo(type.NodeId));
        }

        [TestCase("i=11", "\"number\"")]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=11", "\"number\"")]
        [TestCase("i=11", "[\"number\"]")]
        [TestCase("i=1", "\"boolean\"")]
        [TestCase("i=12", "\"string\"")]
        [TestCase("i=5", "\"integer\"")]
        public void EquivalentAuthoritativeScalarSchemasAreAccepted(string identity, string type)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""{ "type": {{type}}, "uav:mapToType": "{{identity}}" }""");

            Assert.That(result.Success, Is.True, Messages(result));
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.DataType, Is.EqualTo(WotTestData.LocalNodeId(result.Value, identity)));
        }

        [TestCase(false, "boolean", false)]
        [TestCase(true, "boolean", false)]
        [TestCase(false, "string", true)]
        [TestCase(true, "string", true)]
        public void AuthoritativeNestedValueSchemasAreComparedRecursively(
            bool array,
            string leafType,
            bool invalid)
        {
            string nested = $$"""
                { "type": "object", "properties": { "Value": { "type": "{{leafType}}" } }, "required": ["Value"] }
                """;
            if (array)
            {
                nested = """{ "type": "array", "uav:valueRank": 1, "items": """ + nested + "}";
            }
            string definitions = $$"""
                {
                  "@id": "urn:test:pump#Inner", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Inner", "uav:dataTypeId": "nsu=urn:test:pump;i=2101",
                  "uav:fields": [{ "uav:fieldName": "Value", "uav:fieldDataTypeId": "i=1" }]
                },
                {
                  "@id": "urn:test:pump#Outer", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Outer", "uav:dataTypeId": "nsu=urn:test:pump;i=2102",
                  "uav:fields": [{
                    "uav:fieldName": "Nested",
                    "uav:fieldDataTypeDefinition": { "@id": "urn:test:pump#Inner" },
                    "uav:valueRank": {{(array ? 1 : -1)}}
                  }]
                }
                """;
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "object", "uav:mapToType": "nsu=urn:test:pump;i=2102",
                  "properties": { "Nested": {{nested}} }, "required": ["Nested"]
                }
                """,
                definitions);

            Assert.That(result.HasErrors, Is.EqualTo(invalid), Messages(result));
            Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=2102"));
            if (invalid)
            {
                WotDiagnostic error = result.Diagnostics.Single(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ValidationError);
                Assert.That(error.Location?.NodeId, Is.EqualTo("i=1"));
                Assert.That(error.Location?.JsonPointer, Is.EqualTo(array
                    ? "/properties/value/properties/Nested/items/properties/Value/type"
                    : "/properties/value/properties/Nested/properties/Value/type"));
            }
        }

        [Test]
        public void AnAuthoritativeScalarFieldRejectsAnExplicitArraySchema()
        {
            WotConversionResult<UANodeSet> result = ConvertNestedRankSchema(-1, 1);

            Assert.That(result.Success, Is.False, Messages(result));
            WotDiagnostic error = result.Diagnostics.Single(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError);
            Assert.That(error.Location?.JsonPointer, Is.EqualTo("/properties/value/properties/Nested/type"));
            Assert.That(error.Location?.NodeId, Is.EqualTo("nsu=urn:test:pump;i=2101"));
            UADataType outer = result.Value!.Items!.OfType<UADataType>()
                .Single(node => node.NodeId == "ns=1;i=2102");
            Assert.That(outer.Definition!.Field!.Single().ValueRank, Is.EqualTo(-1));
        }

        [TestCase(-1, 0, false)]
        [TestCase(-1, 2, true)]
        [TestCase(1, 0, false)]
        [TestCase(1, 1, false)]
        [TestCase(1, 2, true)]
        [TestCase(2, 0, false)]
        [TestCase(2, 1, false)]
        [TestCase(2, 2, false)]
        [TestCase(2, 3, true)]
        [TestCase(-3, 0, false)]
        [TestCase(-3, 1, false)]
        [TestCase(-3, 2, true)]
        [TestCase(-2, 2, false)]
        [TestCase(0, 2, false)]
        public void AuthoritativeFieldRanksRetainValidShorthandAndVariableRanks(
            int rank,
            int arrayDepth,
            bool invalid)
        {
            WotConversionResult<UANodeSet> result = ConvertNestedRankSchema(rank, arrayDepth);

            Assert.That(result.HasErrors, Is.EqualTo(invalid), Messages(result));
            UADataType outer = result.Value!.Items!.OfType<UADataType>()
                .Single(node => node.NodeId == "ns=1;i=2102");
            DataTypeField field = outer.Definition!.Field!.Single();
            Assert.That(field.DataType, Is.EqualTo("ns=1;i=2101"));
            Assert.That(field.ValueRank, Is.EqualTo(rank));
            if (invalid)
            {
                WotDiagnostic error = result.Diagnostics.Single(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ValidationError);
                Assert.That(error.Location?.JsonPointer, Is.EqualTo("/properties/value/properties/Nested/type"));
                Assert.That(error.Message, Does.Contain("ValueRank " + rank));
            }
        }

        [TestCase("default", "null")]
        [TestCase("default", /*lang=json,strict*/ """{"Unknown":null}""")]
        public void InferredUnionValuesCannotBypassValidation(string term, string value)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "object", "uav:dataTypeName": "pump:Choice", "uav:structureType": "Union",
                  "properties": { "Text": { "type": "string" } },
                  "{{term}}": {{value}}
                }
                """);

            Assert.That(result.Success, Is.False, Messages(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ValidationError &&
                diagnostic.Location?.JsonPointer == "/properties/value/" + term), Is.True, Messages(result));
            Assert.That(result.Value!.Items!.OfType<UADataType>().Single().Definition!.IsUnion, Is.True);
        }

        [TestCase("const", "null", false, true)]
        [TestCase("const", /*lang=json,strict*/ """{"Unknown":null}""", false, true)]
        [TestCase("const", /*lang=json,strict*/ """{"Text":"value","Enabled":true}""", false, true)]
        [TestCase("default", /*lang=json,strict*/ """{"Text":"value","Enabled":true}""", false, true)]
        [TestCase("const", /*lang=json,strict*/ """{"Enabled":null}""", false, true)]
        [TestCase("default", /*lang=json,strict*/ """{"Enabled":null}""", false, true)]
        [TestCase("const", "{}", false, false)]
        [TestCase("default", "{}", false, false)]
        [TestCase("const", /*lang=json,strict*/ """{"Text":null}""", false, false)]
        [TestCase("default", /*lang=json,strict*/ """{"Text":null}""", false, false)]
        [TestCase("const", "[]", false, true)]
        [TestCase("default", "[]", false, true)]
        [TestCase("const", "[null]", true, true)]
        [TestCase("default", "[null]", true, true)]
        [TestCase("const", /*lang=json,strict*/ """[{"Unknown":null}]""", true, true)]
        [TestCase("default", /*lang=json,strict*/ """[{"Unknown":null}]""", true, true)]
        [TestCase("const", "{}", true, true)]
        [TestCase("default", "{}", true, true)]
        [TestCase("const", "[]", true, false)]
        [TestCase("default", "[]", true, false)]
        [TestCase("const", /*lang=json,strict*/ """[{},{"Text":null}]""", true, false)]
        [TestCase("default", /*lang=json,strict*/ """[{},{"Text":null}]""", true, false)]
        public void InferredUnionConstAndDefaultContractsAreChecked(
            string term,
            string value,
            bool array,
            bool invalid)
        {
            const string element =
                                     /*lang=json,strict*/
                                     """
                {
                  "type": "object",
                  "properties": { "Text": { "type": "string" }, "Enabled": { "type": "boolean" } },
                  "uav:fieldOrder": ["Text","Enabled"]
                }
                """;
            string schema = array
                ? """{"type":"array","uav:valueRank":1,"items":""" + element + "}"
                : element;
            System.Text.Json.Nodes.JsonObject definition = System.Text.Json.Nodes.JsonNode.Parse(schema)!.AsObject();
            definition["uav:dataTypeName"] = "pump:Choice";
            definition["uav:structureType"] = "Union";
            definition[term] = System.Text.Json.Nodes.JsonNode.Parse(value);
            WotConversionResult<UANodeSet> result = ConvertSchema(definition.ToJsonString());

            Assert.That(result.HasErrors, Is.EqualTo(invalid), Messages(result));
            UADataType type = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(type.Definition!.IsUnion, Is.True);
            Assert.That(result.Value.Items!.OfType<UAVariable>().Single().ValueRank, Is.EqualTo(array ? 1 : -1));
            if (invalid)
            {
                string pointer = "/properties/value/" + term;
                if (array && value.Length != 0 && value[0] == '[')
                {
                    pointer += "/0";
                }
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ValidationError &&
                    diagnostic.Location?.JsonPointer == pointer), Is.True, Messages(result));
            }
            else
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(result.Value);
                using var expected = System.Text.Json.JsonDocument.Parse(value);
                Assert.That(System.Text.Json.JsonElement.DeepEquals(
                    restored.Properties.Values.Single().GetProperty(term), expected.RootElement), Is.True);
            }
        }

        [TestCase("{}", false)]
        [TestCase(/*lang=json,strict*/ """{"Text":null}""", false)]
        [TestCase("null", true)]
        [TestCase(/*lang=json,strict*/ """{"Text":"value","Enabled":true}""", true)]
        [TestCase(/*lang=json,strict*/ """{"Unknown":null}""", true)]
        [TestCase(/*lang=json,strict*/ """{"Enabled":null}""", true)]
        public void UnionNoSelectionAndSelectedNullRemainDistinct(string value, bool invalid)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "object", "uav:mapToType": "nsu=urn:test:pump;i=2501",
                  "properties": { "Text": { "type": ["string","null"] }, "Enabled": { "type": "boolean" } },
                  "uav:fieldOrder": ["Text","Enabled"], "minProperties": 0, "maxProperties": 1,
                  "default": {{value}}
                }
                """,
                                     /*lang=json,strict*/
                                     """
                {
                  "@id": "urn:test:pump#Choice", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Choice", "uav:dataTypeId": "nsu=urn:test:pump;i=2501",
                  "uav:structureType": "Union",
                  "uav:fields": [
                    { "uav:fieldName": "Text", "uav:fieldDataTypeId": "i=12" },
                    { "uav:fieldName": "Enabled", "uav:fieldDataTypeId": "i=1" }
                  ]
                }
                """);

            Assert.That(result.HasErrors, Is.EqualTo(invalid), Messages(result));
            Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("ns=1;i=2501"));
            if (invalid)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ValidationError &&
                    diagnostic.Location?.JsonPointer == "/properties/value/default"), Is.True, Messages(result));
            }
            else
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(result.Value);
                using var expected = System.Text.Json.JsonDocument.Parse(value);
                Assert.That(System.Text.Json.JsonElement.DeepEquals(
                    restored.Properties.Values.Single().GetProperty("default"), expected.RootElement), Is.True);
            }
        }

        [TestCase("true")]
        [TestCase("\"1\"")]
        [TestCase("1.5")]
        [TestCase("2147483648")]
        public void InferredEnumerationsDoNotReplaceInvalidConstantsWithMinusOne(string value)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "integer", "uav:dataTypeName": "pump:Mode",
                  "oneOf": [{ "uav:enumName": "Invalid", "const": {{value}} }]
                }
                """);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.JsonPointer == "/properties/value/oneOf/0"), Is.True, Messages(result));
        }

        [TestCase("""["Missing"]""")]
        [TestCase("""["A","A"]""")]
        [TestCase("[1]")]
        public void InferredRequiredFieldsMustNameDistinctDeclaredProperties(string required)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "object", "uav:dataTypeName": "pump:Reading",
                  "properties": { "A": { "type": "boolean" } }, "required": {{required}}
                }
                """);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.DataTypeDefinitionInvalid &&
                diagnostic.Location?.JsonPointer == "/properties/value/required"), Is.True, Messages(result));
        }

        [Test]
        public void AnInferredStructureRetainsItsResolvedSupertype()
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                                     /*lang=json,strict*/
                                     """
                {
                  "type": "object", "uav:dataTypeName": "pump:Derived",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeName": "pump:Base" },
                  "properties": { "A": { "type": "boolean" }, "B": { "type": "string" } },
                  "required": ["A","B"], "uav:fieldOrder": ["A","B"]
                }
                """,
                                     /*lang=json,strict*/
                                     """
                {
                  "@id": "urn:test:pump#Base", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Base", "uav:dataTypeId": "nsu=urn:test:pump;i=2601",
                  "uav:fields": [{ "uav:fieldName": "A", "uav:fieldDataTypeId": "i=1" }]
                }
                """);

            Assert.That(result.Success, Is.True, Messages(result));
            UADataType derived = result.Value!.Items!.OfType<UADataType>()
                .Single(type => type.BrowseName == "1:Derived");
            Assert.That(derived.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value,
                Is.EqualTo("ns=1;i=2601"));
            Assert.That(derived.Definition!.Field!.Select(field => (field.Name, field.DataType)),
                Is.EqualTo([("A", "i=1"), ("B", "i=12")]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InferredSimpleAliasesKeepTheirDeclaredAbstractness(bool isAbstract)
        {
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "integer", "uav:dataTypeName": "pump:Word",
                  "uav:dataTypeSubtypeOf": { "uav:dataTypeId": "i=5" },
                  "uav:isAbstract": {{(isAbstract ? "true" : "false")}}
                }
                """);

            Assert.That(result.Success, Is.True, Messages(result));
            UADataType type = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(type.IsAbstract, Is.EqualTo(isAbstract));
            Assert.That(type.Definition, Is.Null);
            Assert.That(type.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value, Is.EqualTo("i=5"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InferredArrayUnionsPreserveTheirDeclaredFamily(bool facetOnElement)
        {
            string rootFacet = facetOnElement ? string.Empty : "\"uav:structureType\":\"Union\",";
            string elementFacet = facetOnElement ? "\"uav:structureType\":\"Union\"," : string.Empty;
            WotConversionResult<UANodeSet> result = ConvertSchema(
                $$"""
                {
                  "type": "array", "uav:valueRank": 1, "uav:dataTypeName": "pump:Choice", {{rootFacet}}
                  "items": {
                    "type": "object", {{elementFacet}}
                    "properties": { "Text": { "type": "string" } }
                  }
                }
                """);

            Assert.That(result.Success, Is.True, Messages(result));
            UADataType type = result.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(type.Definition!.IsUnion, Is.True);
            Assert.That(type.Definition.Field!.Single().IsOptional, Is.False);
            Assert.That(type.References!.Single(reference =>
                reference.ReferenceType == "HasSubtype" && !reference.IsForward).Value, Is.EqualTo("i=12756"));
        }

        private static WotConversionResult<UANodeSet> ConvertNestedRankSchema(int rank, int arrayDepth)
        {
            string nested =
                /*lang=json,strict*/
                """
                {"type":"object","properties":{"Value":{"type":"boolean"}},"required":["Value"]}
                """;
            for (int index = 0; index < arrayDepth; index++)
            {
                nested = """{"type":"array","items":""" + nested + "}";
            }
            return ConvertSchema(
                $$"""
                {
                  "type": "object", "uav:mapToType": "nsu=urn:test:pump;i=2102",
                  "properties": { "Nested": {{nested}} }, "required": ["Nested"]
                }
                """,
                $$"""
                {
                  "@id": "urn:test:pump#Inner", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Inner", "uav:dataTypeId": "nsu=urn:test:pump;i=2101",
                  "uav:fields": [{ "uav:fieldName": "Value", "uav:fieldDataTypeId": "i=1" }]
                },
                {
                  "@id": "urn:test:pump#Outer", "@type": "uav:StructureDefinition",
                  "uav:dataTypeName": "pump:Outer", "uav:dataTypeId": "nsu=urn:test:pump;i=2102",
                  "uav:fields": [{
                    "uav:fieldName": "Nested", "uav:fieldDataTypeId": "nsu=urn:test:pump;i=2101",
                    "uav:valueRank": {{rank}}
                  }]
                }
                """);
        }

        private static WotConversionResult<UANodeSet> ConvertSchema(string schema, string definitions = "")
        {
            using var document = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@context": {
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                    "ua": "http://opcfoundation.org/UA/",
                    "pump": "urn:test:pump"
                  },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:pump;i=1001",
                  "uav:browseName": "pump:PumpType",
                  "properties": { "value": {{schema}} },
                  "uav:dataTypeDefinitions": [{{definitions}}]
                }
                """));
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private static string? DataTypeOf(
            string terms,
            bool expectError,
            string valueSchema = "\"type\":\"number\",")
        {
            WotConversionResult<UANodeSet> result = Convert(terms, valueSchema);

            Assert.That(
                result.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.EqualTo(expectError),
                Messages(result));
            return result.Value?.Items?
                .OfType<UAVariable>()
                .FirstOrDefault(v => string.Equals(
                    v.BrowseName, "1:Speed", StringComparison.Ordinal))?
                .DataType;
        }

        private static string Messages(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        private static WotConversionResult<UANodeSet> Convert(
            string terms,
            string valueSchema = "\"type\":\"number\",")
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                "\"properties\":{\"speed\":{" +
                valueSchema +
                "\"uav:browseName\":\"pump:Speed\"," +
                terms.TrimEnd(',') +
                "}}," +
                "\"uav:dataTypeDefinitions\":[{" +
                "\"@id\":\"urn:test:pump#Reading\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:Reading\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[{\"@type\":\"uav:StructureField\"," +
                "\"uav:fieldName\":\"Sample\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\"," +
                "\"uav:fieldDataTypeId\":\"i=11\"}]}]}");

            using var document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private const string DefinitionValueSchema =
            "\"type\":\"object\",\"properties\":{\"Sample\":{\"type\":\"number\"}},\"required\":[\"Sample\"],";
    }
}
