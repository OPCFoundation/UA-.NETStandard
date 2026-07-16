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
        public void GeneratedCompactSchemaRejectsMissingRequiredField()
        {
            UaTypeDescription sampleType = SchemaTestData.Structure(
                3901,
                "RequiredInt32Sample",
                SchemaTestData.Field("RequiredValue", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            IUaSchema schema = SchemaTestData.CreateProvider(sampleType)
                .CreateSchema(sampleType, UaSchemaFormat.JsonCompact);

            EvaluationResults results = Evaluate(schema, new JsonObject());

            Assert.That(results.IsValid, Is.False, results.ToString());
        }

        [Test]
        public void GeneratedCompactOptionalStructSchemaRequiresEncodingMask()
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
                Assert.That(withoutMask.IsValid, Is.False, withoutMask.ToString());
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
