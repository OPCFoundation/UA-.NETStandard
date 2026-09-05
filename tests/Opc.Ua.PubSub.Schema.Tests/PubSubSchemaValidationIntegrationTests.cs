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
using UaSchema = Opc.Ua.Schema.IUaSchema;

namespace Opc.Ua.PubSub.Schema.Tests
{
    /// <summary>
    /// Validates generated PubSub JSON schemas against representative PubSub JSON payloads.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class PubSubSchemaValidationIntegrationTests
    {
        [Test]
        public void GeneratedDataSetSchemaValidatesConformingRawDataPayloadAndRejectsWrongType()
        {
            var provider = new PubSubSchemaProvider();
            UaSchema schema = provider.CreateDataSetSchema(CreateMetaData(), DataSetFieldContentMask.RawData);
            var validPayload = new JsonObject
            {
                ["Field1"] = 1,
                ["Field2"] = "x",
                ["Field3"] = "123",
                ["Field4"] = new JsonArray(true, false)
            };
            var invalidPayload = new JsonObject
            {
                ["Field1"] = 1,
                ["Field2"] = "x",
                ["Field3"] = 123,
                ["Field4"] = new JsonArray(true, false)
            };

            EvaluationResults validResults = Evaluate(schema, validPayload);
            EvaluationResults invalidResults = Evaluate(schema, invalidPayload);

            Assert.Multiple(() =>
            {
                Assert.That(validResults.IsValid, Is.True, validResults.ToString());
                Assert.That(invalidResults.IsValid, Is.False, invalidResults.ToString());
            });
        }

        [Test]
        public void GeneratedNetworkMessageSchemaValidatesMinimalUaDataEnvelope()
        {
            var provider = new PubSubSchemaProvider();
            UaSchema schema = provider.CreateNetworkMessageSchema(
                CreateMetaData(),
                JsonNetworkMessageContentMask.NetworkMessageHeader,
                JsonDataSetMessageContentMask.None,
                DataSetFieldContentMask.RawData);
            var instance = new JsonObject
            {
                ["MessageType"] = "ua-data",
                ["Messages"] = new JsonArray(
                    new JsonObject
                    {
                        ["MessageType"] = "ua-keyframe",
                        ["Payload"] = new JsonObject
                        {
                            ["Field1"] = 1,
                            ["Field2"] = "x",
                            ["Field3"] = "123",
                            ["Field4"] = new JsonArray(true, false)
                        }
                    })
            };

            EvaluationResults results = Evaluate(schema, instance);

            Assert.That(results.IsValid, Is.True, results.ToString());
        }

        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "TelemetryValidation",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Field1",
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Field2",
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Field3",
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Field4",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.OneDimension
                    }
                ]
            };
        }

        private static EvaluationResults Evaluate(UaSchema schema, JsonNode instance)
        {
            var jsonSchema = JsonSchema.FromText(
                schema.ToSchemaString(),
                new BuildOptions { SchemaRegistry = new Json.Schema.SchemaRegistry() });
            return jsonSchema.Evaluate(
                JsonSerializer.SerializeToElement(instance),
                new EvaluationOptions { OutputFormat = OutputFormat.List });
        }
    }
}
