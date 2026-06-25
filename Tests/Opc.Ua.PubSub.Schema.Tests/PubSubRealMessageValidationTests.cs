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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Json.Schema;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using UaSchema = Opc.Ua.Schema.IUaSchema;
using PubSubJson = Opc.Ua.PubSub.Encoding.Json;

namespace Opc.Ua.PubSub.Schema.Tests
{
    /// <summary>
    /// Validates PubSub JSON messages emitted by the PubSub encoder against generated PubSub schemas.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class PubSubRealMessageValidationTests
    {
        [Test]
        public async Task GeneratedNetworkMessageSchemaValidatesEncoderProducedUaDataAsync()
        {
            DataSetMetaDataType metaData = CreateMetaData();
            JsonNetworkMessageContentMask networkMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader
                | JsonNetworkMessageContentMask.PublisherId;
            JsonDataSetMessageContentMask messageMask = JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Timestamp
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.MessageType
                | JsonDataSetMessageContentMask.MetaDataVersion;
            var provider = new PubSubSchemaProvider();
            UaSchema schema = provider.CreateNetworkMessageSchema(
                metaData,
                networkMask,
                messageMask,
                DataSetFieldContentMask.RawData);

            JsonNode encoded = await EncodeNetworkMessageAsync(metaData, networkMask, messageMask).ConfigureAwait(false);
            EvaluationResults validResults = Evaluate(schema, encoded);
            JsonObject invalid = encoded.DeepClone().AsObject();
            invalid.Remove("Messages");
            EvaluationResults invalidResults = Evaluate(schema, invalid);

            Assert.Multiple(() =>
            {
                Assert.That(validResults.IsValid, Is.True, validResults.ToString());
                Assert.That(invalidResults.IsValid, Is.False, invalidResults.ToString());
            });
        }

        [Test]
        public async Task GeneratedMetaDataMessageSchemaValidatesEncoderProducedUaMetadataAsync()
        {
            DataSetMetaDataType metaData = CreateMetaData();
            var provider = new PubSubSchemaProvider();
            UaSchema schema = provider.CreateMetaDataMessageSchema(metaData);

            JsonNode encoded = await EncodeMetaDataMessageAsync(metaData).ConfigureAwait(false);
            EvaluationResults validResults = Evaluate(schema, encoded);
            JsonObject invalid = encoded.DeepClone().AsObject();
            invalid.Remove("MessageType");
            EvaluationResults invalidResults = Evaluate(schema, invalid);

            Assert.Multiple(() =>
            {
                Assert.That(validResults.IsValid, Is.True, validResults.ToString());
                Assert.That(invalidResults.IsValid, Is.False, invalidResults.ToString());
            });
        }

        private static async Task<JsonNode> EncodeNetworkMessageAsync(
            DataSetMetaDataType metaData,
            JsonNetworkMessageContentMask networkMask,
            JsonDataSetMessageContentMask messageMask)
        {
            var dataSetMessage = new PubSubJson.JsonDataSetMessage
            {
                ContentMask = messageMask,
                DataSetWriterId = DataSetWriterId,
                SequenceNumber = 12,
                Timestamp = new DateTimeUtc(new DateTime(2026, 6, 25, 16, 0, 0, DateTimeKind.Utc)),
                Status = StatusCodes.Good,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = metaData.ConfigurationVersion,
                FieldContentMask = DataSetFieldContentMask.RawData,
                Fields =
                [
                    new DataSetField
                    {
                        Name = "Enabled",
                        Value = new Variant(true),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Temperature",
                        Value = new Variant(21.5d),
                        Encoding = PubSubFieldEncoding.RawData
                    },
                    new DataSetField
                    {
                        Name = "Name",
                        Value = new Variant("PumpA"),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ]
            };
            var message = new PubSubJson.JsonNetworkMessage
            {
                MessageId = "ua-data-1",
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                ContentMask = networkMask,
                MetaData = metaData,
                DataSetMessages = [dataSetMessage]
            };
            var encoder = new PubSubJson.JsonEncoder(PubSubJson.JsonEncodingMode.RawData);
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(message, CreateContext(metaData)).ConfigureAwait(false);
            return JsonNode.Parse(bytes.Span) ?? throw new JsonException("The PubSub encoder emitted an empty JSON payload.");
        }

        private static async Task<JsonNode> EncodeMetaDataMessageAsync(DataSetMetaDataType metaData)
        {
            var message = new PubSubJson.JsonMetaDataMessage
            {
                MessageId = "ua-metadata-1",
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                DataSetWriterId = DataSetWriterId,
                DataSetClassId = new Uuid(new Guid("11112222-3333-4444-5555-666677778888")),
                MetaDataPayload = metaData
            };
            var encoder = new PubSubJson.JsonEncoder(PubSubJson.JsonEncodingMode.RawData);
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(message, CreateContext(metaData)).ConfigureAwait(false);
            return JsonNode.Parse(bytes.Span) ?? throw new JsonException("The PubSub encoder emitted an empty JSON payload.");
        }

        private static PubSubNetworkMessageContext CreateContext(DataSetMetaDataType metaData)
        {
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(PublisherIdValue), DataSetWriterId, 0, Uuid.Empty, 0),
                metaData);
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                TimeProvider.System);
        }

        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "RealMessageDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Enabled",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Name",
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static EvaluationResults Evaluate(UaSchema schema, JsonNode instance)
        {
            JsonSchema jsonSchema = JsonSchema.FromText(schema.ToSchemaString());
            return jsonSchema.Evaluate(
                instance,
                new EvaluationOptions { OutputFormat = OutputFormat.List });
        }

        private const ushort DataSetWriterId = 1;
        private const ushort PublisherIdValue = 300;
    }
}
