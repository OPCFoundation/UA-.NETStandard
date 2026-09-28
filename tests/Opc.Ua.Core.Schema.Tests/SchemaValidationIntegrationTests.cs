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
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using NUnit.Framework;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Validates generated runtime JSON schemas against stack-produced OPC UA JSON.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class SchemaValidationIntegrationTests
    {
        [Test]
        public void GeneratedCompactRangeSchemaValidatesEncodedRange()
        {
            UaTypeDescription rangeType = SchemaTestData.Structure(
                884,
                "Range",
                SchemaTestData.Field("Low", SchemaTestData.BuiltIn(BuiltInType.Double)),
                SchemaTestData.Field("High", SchemaTestData.BuiltIn(BuiltInType.Double)));
            IUaSchema schema = SchemaTestData.CreateProvider(rangeType)
                .CreateSchema(rangeType, UaSchemaFormat.JsonCompact);
            JsonNode instance = EncodeEncodeable(new Range { Low = 1.0, High = 2.0 });

            EvaluationResults results = Evaluate(schema, instance);

            Assert.That(results.IsValid, Is.True, results.ToString());
        }

        [Test]
        public void GeneratedCompactEuInformationSchemaValidatesEncodedEuInformation()
        {
            UaTypeDescription euInformationType = SchemaTestData.Structure(
                887,
                "EUInformation",
                SchemaTestData.Field("NamespaceUri", SchemaTestData.BuiltIn(BuiltInType.String)),
                SchemaTestData.Field("UnitId", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("DisplayName", SchemaTestData.BuiltIn(BuiltInType.LocalizedText)),
                SchemaTestData.Field("Description", SchemaTestData.BuiltIn(BuiltInType.LocalizedText)));
            IUaSchema schema = SchemaTestData.CreateProvider(euInformationType)
                .CreateSchema(euInformationType, UaSchemaFormat.JsonCompact);
            JsonNode instance = EncodeEncodeable(
                new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4408652,
                    DisplayName = new LocalizedText("en", "degree Celsius"),
                    Description = new LocalizedText("en", "degree Celsius")
                });

            EvaluationResults results = Evaluate(schema, instance);

            Assert.That(results.IsValid, Is.True, results.ToString());
        }

        [Test]
        public void GeneratedVerboseSchemaRejectsMissingRequiredField()
        {
            UaTypeDescription sampleType = SchemaTestData.Structure(
                3901,
                "RequiredInt32Sample",
                SchemaTestData.Field("RequiredValue", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            IUaSchema schema = SchemaTestData.CreateProvider(sampleType)
                .CreateSchema(sampleType, UaSchemaFormat.JsonVerbose);

            EvaluationResults results = Evaluate(schema, new JsonObject());

            Assert.That(results.IsValid, Is.False, results.ToString());
        }

        /// <summary>
        /// Part 6 5.4.1: the compact encoding omits all fields with a default value, so an
        /// empty object is a valid compact encoding.
        /// </summary>
        [Test]
        public void GeneratedCompactSchemaAcceptsOmittedDefaultFields()
        {
            UaTypeDescription sampleType = SchemaTestData.Structure(
                3901,
                "RequiredInt32Sample",
                SchemaTestData.Field("RequiredValue", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            IUaSchema schema = SchemaTestData.CreateProvider(sampleType)
                .CreateSchema(sampleType, UaSchemaFormat.JsonCompact);

            EvaluationResults results = Evaluate(schema, new JsonObject());

            Assert.That(results.IsValid, Is.True, results.ToString());
        }

        /// <summary>
        /// S1-9/S1-10: the generated schemas validate what JsonEncoder writes for every
        /// standard built-in type (Part 6 5.4.2), in both flavors, for populated and for
        /// default/null values.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void GeneratedSchemaValidatesJsonEncoderOutputOfBuiltInTypes(bool verbose, bool populated)
        {
            UaTypeDescription color = SchemaTestData.Enumeration(3931, "TestColor", ("Red", 0), ("Green", 1));
            UaTypeDescription flags = OptionSet(3932, "TestFlags");
            UaTypeDescription inner = SchemaTestData.Structure(
                3933,
                "TestInner",
                SchemaTestData.Field("Low", SchemaTestData.BuiltIn(BuiltInType.Double)),
                SchemaTestData.Field("High", SchemaTestData.BuiltIn(BuiltInType.Double)));
            UaTypeDescription sampleType = SchemaTestData.Structure(
                3930,
                "BuiltInSample",
                SchemaTestData.Field("Int", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Long", SchemaTestData.BuiltIn(BuiltInType.Int64)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)),
                SchemaTestData.Field("Time", SchemaTestData.BuiltIn(BuiltInType.DateTime)),
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Guid)),
                SchemaTestData.Field("Blob", SchemaTestData.BuiltIn(BuiltInType.ByteString)),
                SchemaTestData.Field("Node", SchemaTestData.BuiltIn(BuiltInType.NodeId)),
                SchemaTestData.Field("ExpandedNode", SchemaTestData.BuiltIn(BuiltInType.ExpandedNodeId)),
                SchemaTestData.Field("Status", SchemaTestData.BuiltIn(BuiltInType.StatusCode)),
                SchemaTestData.Field("Name", SchemaTestData.BuiltIn(BuiltInType.QualifiedName)),
                SchemaTestData.Field("Label", SchemaTestData.BuiltIn(BuiltInType.LocalizedText)),
                SchemaTestData.Field("Body", SchemaTestData.BuiltIn(BuiltInType.ExtensionObject)),
                SchemaTestData.Field("Sample", SchemaTestData.BuiltIn(BuiltInType.DataValue)),
                SchemaTestData.Field("Any", SchemaTestData.BuiltIn(BuiltInType.Variant)),
                SchemaTestData.Field("Names", SchemaTestData.BuiltIn(BuiltInType.String), ValueRanks.OneDimension),
                SchemaTestData.Field("Shade", new NodeId(3931, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Flags", new NodeId(3932, SchemaTestData.TestNamespaceIndex)),
                SchemaTestData.Field("Child", new NodeId(3933, SchemaTestData.TestNamespaceIndex)));
            IUaSchema schema = SchemaTestData.CreateProvider(color, flags, inner, sampleType)
                .CreateSchema(
                    sampleType,
                    verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact);

            var context = ServiceMessageContext.Create(null);
            ushort ns = context.NamespaceUris.GetIndexOrAppend(SchemaTestData.TestNamespace);
            using var encoder = new JsonEncoder(
                context,
                verbose ? JsonEncoderOptions.Verbose : JsonEncoderOptions.Compact);
            if (populated)
            {
                encoder.WriteInt32("Int", 7);
                encoder.WriteInt64("Long", 1234567890123);
                encoder.WriteString("Text", "text");
                encoder.WriteDateTime("Time", DateTimeUtc.Now);
                encoder.WriteGuid("Id", Uuid.NewUuid());
                encoder.WriteByteString("Blob", ByteString.From([1, 2, 3]));
                encoder.WriteNodeId("Node", new NodeId("Node", ns));
                encoder.WriteExpandedNodeId("ExpandedNode", new ExpandedNodeId(42u, ns));
                encoder.WriteStatusCode("Status", StatusCodes.BadUnexpectedError);
                encoder.WriteQualifiedName("Name", new QualifiedName("Name", ns));
                encoder.WriteLocalizedText("Label", new LocalizedText("en", "label"));
                encoder.WriteExtensionObject(
                    "Body",
                    new ExtensionObject(new Range { Low = 1.0, High = 2.0 }));
                encoder.WriteDataValue(
                    "Sample",
                    new DataValue(Variant.From(3.5), StatusCodes.Uncertain, DateTimeUtc.Now));
                encoder.WriteVariant("Any", Variant.From(s_intValues));
                encoder.WriteStringArray("Names", ["a", null!, "c"]);
                encoder.WriteEnumerated("Shade", TestColor.Green);
                encoder.WriteUInt32("Flags", 5);
                encoder.WriteEncodeable("Child", new Range { Low = 1.0, High = 0 });
            }
            else
            {
                encoder.WriteInt32("Int", 0);
                encoder.WriteInt64("Long", 0);
                encoder.WriteString("Text", null);
                encoder.WriteDateTime("Time", default);
                encoder.WriteGuid("Id", default);
                encoder.WriteByteString("Blob", default(ByteString));
                encoder.WriteNodeId("Node", default);
                encoder.WriteExpandedNodeId("ExpandedNode", default);
                encoder.WriteStatusCode("Status", StatusCodes.Good);
                encoder.WriteQualifiedName("Name", default);
                encoder.WriteLocalizedText("Label", default);
                encoder.WriteExtensionObject("Body", default);
                encoder.WriteDataValue("Sample", default);
                encoder.WriteVariant("Any", default);
                encoder.WriteStringArray("Names", default);
                encoder.WriteEnumerated("Shade", TestColor.Red);
                encoder.WriteUInt32("Flags", 0);
                encoder.WriteEncodeable<Range>("Child", null!);
            }
            JsonNode instance = JsonNode.Parse(encoder.CloseAndReturnText())
                ?? throw new ServiceResultException(StatusCodes.BadEncodingError);

            EvaluationResults results = Evaluate(schema, instance);

            Assert.That(results.IsValid, Is.True, instance.ToJsonString() + "\n" + Errors(results));
        }

        /// <summary>
        /// A4-7: a LocalizedText with a locale but no text, as written by JsonEncoder.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void GeneratedSchemaValidatesLocalizedTextWithLocaleOnly(bool verbose)
        {
            UaTypeDescription sampleType = SchemaTestData.Structure(
                3934,
                "LocaleOnlySample",
                SchemaTestData.Field("Label", SchemaTestData.BuiltIn(BuiltInType.LocalizedText)));
            IUaSchema schema = SchemaTestData.CreateProvider(sampleType)
                .CreateSchema(
                    sampleType,
                    verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact);
            JsonNode instance = Encode(verbose, e => e.WriteLocalizedText("Label", new LocalizedText("en-US", (string)null!)));

            EvaluationResults results = Evaluate(schema, instance);

            Assert.That(results.IsValid, Is.True, instance.ToJsonString() + "\n" + Errors(results));
        }

        /// <summary>
        /// S1-10: unions and structures with optional fields as written by JsonEncoder.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void GeneratedSchemaValidatesJsonEncoderOutputOfUnionsAndOptionalFields(bool verbose)
        {
            UaTypeDescription unionType = SchemaTestData.Union(
                3940,
                "SampleUnion",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            UaTypeDescription optionalType = SchemaTestData.Structure(
                3941,
                "SampleOptional",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true));
            UaSchemaFormat format = verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact;
            ISchemaProvider provider = SchemaTestData.CreateProvider(unionType, optionalType);
            IUaSchema unionSchema = provider.CreateSchema(unionType, format);
            IUaSchema optionalSchema = provider.CreateSchema(optionalType, format);

            var instances = new List<(IUaSchema, JsonNode)>
            {
                (unionSchema, Encode(verbose, e =>
                {
                    e.WriteSwitchField(1, out _);
                    e.WriteInt32("Number", 0);
                })),
                (unionSchema, Encode(verbose, e =>
                {
                    e.WriteSwitchField(2, out _);
                    e.WriteString("Text", "x");
                })),
                (unionSchema, Encode(verbose, e => e.WriteSwitchField(0, out _))),
                (optionalSchema, Encode(verbose, e =>
                {
                    e.WriteEncodingMask(0);
                    e.WriteInt32("Id", 0);
                })),
                (optionalSchema, Encode(verbose, e =>
                {
                    e.WriteEncodingMask(1);
                    e.WriteInt32("Id", 5);
                    e.WriteString("Note", "note");
                }))
            };

            foreach ((IUaSchema schema, JsonNode instance) in instances)
            {
                EvaluationResults results = Evaluate(schema, instance);
                Assert.That(results.IsValid, Is.True, instance.ToJsonString() + "\n" + Errors(results));
            }
        }

        [Test]
        public void GeneratedCompactOptionalStructSchemaAcceptsOmittedZeroEncodingMask()
        {
            UaTypeDescription optionalType = SchemaTestData.Structure(
                3910,
                "OptionalSample",
                SchemaTestData.Field("Id", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Note", SchemaTestData.BuiltIn(BuiltInType.String), optional: true));
            IUaSchema schema = SchemaTestData.CreateProvider(optionalType)
                .CreateSchema(optionalType, UaSchemaFormat.JsonCompact);

            EvaluationResults withMask = Evaluate(
                schema,
                new JsonObject { ["EncodingMask"] = 0, ["Id"] = 5 });
            EvaluationResults withoutMask = Evaluate(
                schema,
                new JsonObject { ["Id"] = 5 });

            Assert.Multiple(() =>
            {
                Assert.That(withMask.IsValid, Is.True, withMask.ToString());

                // a zero EncodingMask is the default value and omitted by the encoder.
                Assert.That(withoutMask.IsValid, Is.True, withoutMask.ToString());
            });
        }

        [Test]
        public void GeneratedCompactUnionSchemaRequiresSwitchField()
        {
            UaTypeDescription unionType = SchemaTestData.Union(
                3920,
                "UnionSample",
                SchemaTestData.Field("Number", SchemaTestData.BuiltIn(BuiltInType.Int32)),
                SchemaTestData.Field("Text", SchemaTestData.BuiltIn(BuiltInType.String)));
            IUaSchema schema = SchemaTestData.CreateProvider(unionType)
                .CreateSchema(unionType, UaSchemaFormat.JsonCompact);

            EvaluationResults withSwitch = Evaluate(
                schema,
                new JsonObject { ["SwitchField"] = 1, ["Number"] = 7 });
            EvaluationResults withoutSwitch = Evaluate(
                schema,
                new JsonObject { ["Number"] = 7 });

            Assert.Multiple(() =>
            {
                Assert.That(withSwitch.IsValid, Is.True, withSwitch.ToString());
                Assert.That(withoutSwitch.IsValid, Is.False, withoutSwitch.ToString());
            });
        }

        private static readonly int[] s_intValues = [1, 2, 3];

        private enum TestColor
        {
            Red = 0,
            Green = 1
        }

        private static UaTypeDescription OptionSet(uint id, string name)
        {
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(id, SchemaTestData.TestNamespaceIndex)),
                new QualifiedName(name, SchemaTestData.TestNamespaceIndex),
                new EnumDefinition
                {
                    IsOptionSet = true,
                    Fields =
                    [
                        new EnumField { Name = "Bit0", Value = 0 },
                        new EnumField { Name = "Bit2", Value = 2 }
                    ]
                },
                SchemaTestData.TestNamespace);
        }

        private static JsonNode Encode(bool verbose, Action<JsonEncoder> write)
        {
            using var encoder = new JsonEncoder(
                ServiceMessageContext.Create(null),
                verbose ? JsonEncoderOptions.Verbose : JsonEncoderOptions.Compact);
            write(encoder);
            return JsonNode.Parse(encoder.CloseAndReturnText())
                ?? throw new ServiceResultException(StatusCodes.BadEncodingError);
        }

        private static string Errors(EvaluationResults results)
        {
            return JsonSerializer.Serialize(results);
        }

        private static JsonNode EncodeEncodeable<T>(T value)
            where T : IEncodeable, new()
        {
            using var encoder = new JsonEncoder(ServiceMessageContext.Create(null), JsonEncoderOptions.Compact);
            encoder.WriteEncodeable("Value", value);

            JsonNode root = JsonNode.Parse(encoder.CloseAndReturnText())
                ?? throw new ServiceResultException(StatusCodes.BadEncodingError);
            return root["Value"] ?? throw new ServiceResultException(StatusCodes.BadEncodingError);
        }

        private static EvaluationResults Evaluate(IUaSchema schema, JsonNode instance)
        {
            var jsonSchema = JsonSchema.FromText(
                schema.ToSchemaString(),
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            return jsonSchema.Evaluate(
                JsonSerializer.SerializeToElement(instance),
                new EvaluationOptions { OutputFormat = OutputFormat.List });
        }
    }
}
