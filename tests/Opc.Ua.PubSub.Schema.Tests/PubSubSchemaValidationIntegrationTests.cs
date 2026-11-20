// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            return jsonSchema.Evaluate(
                JsonSerializer.SerializeToElement(instance),
                new EvaluationOptions { OutputFormat = OutputFormat.List });
        }
    }
}
