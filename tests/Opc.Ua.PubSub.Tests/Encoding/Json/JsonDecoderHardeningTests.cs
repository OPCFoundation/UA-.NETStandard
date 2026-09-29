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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Regression tests for hostile JSON frames: the decoder must
    /// reject them through the diagnostics counters and never throw.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5")]
    public sealed class JsonDecoderHardeningTests
    {
        [TestCase("{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1," +
            "\"MetaDataVersion\":{\"MajorVersion\":\"x\",\"MinorVersion\":[]}}]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"Messages\":[{\"Status\":{\"Code\":\"x\"}}]}")]
        [TestCase("{\"MessageType\":\"ua-status\",\"WriterConfiguration\":{}," +
            "\"DataSetWriterIds\":[\"x\"]}")]
        public void DecodeNonNumericHeaderValuesDoesNotThrow(string json)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();

            Assert.DoesNotThrowAsync(async () => await DecodeAsync(json, ctx).ConfigureAwait(false));
        }

        [Test]
        public async Task DecodeFieldBeyondStringLimitCountsFailedDataSetMessageAsync()
        {
            string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"f\":{\"UaType\":12,\"Value\":\"" + new string('a', 70000) + "\"}}}]}";
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodeFrameAboveMaxMessageSizeIsRejectedAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            ((ServiceMessageContext)ctx.MessageContext).MaxMessageSize = 64;
            string json = "{\"MessageType\":\"ua-data\",\"MessageId\":\"" +
                new string('m', 64) + "\",\"Messages\":[]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }

        [TestCase("{\"MessageType\":\"ua-data\",\"PublisherId\":\"0123456789abcdefX\",\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"MessageId\":\"0123456789abcdefX\",\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"ReplyTo\":[\"0123456789abcdefX\"],\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"ReplyTo\":[\"\",\"\",\"\",\"\",\"\"],\"Messages\":[]}")]
        [TestCase("{\"MessageType\":\"ua-data\",\"Messages\":[{},{},{},{},{}]}")]
        [TestCase("[{},{},{},{},{}]")]
        [TestCase("{\"MessageType\":\"ua-metadata\",\"PublisherId\":\"0123456789abcdefX\"," +
            "\"DataSetWriterId\":1,\"MetaData\":{}}")]
        public async Task DecodeEnvelopeAboveStringOrArrayLimitIsRejectedAsync(string json)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var messageContext = (ServiceMessageContext)ctx.MessageContext;
            messageContext.MaxStringLength = 16;
            messageContext.MaxArrayLength = 4;

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodePayloadAboveMaxArrayLengthFailsDataSetMessageAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            ((ServiceMessageContext)ctx.MessageContext).MaxArrayLength = 4;
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Payload\":{" +
                "\"a\":null,\"b\":null,\"c\":null,\"d\":null,\"e\":null}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodeVerboseFieldsWithinLimitsSucceedsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7},\"b\":{\"UaType\":12,\"Value\":\"x\"}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(7)));
            Assert.That(result.DataSetMessages[0].Fields[1].Value, Is.EqualTo(new Variant("x")));
        }

        [Test]
        public async Task DecodePayloadWithMoreMembersThanMetaDataFieldsIsRejectedAsync()
        {
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(
                JsonTestUtilities.CreateMetaData());
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"MetaDataVersion\":{\"MajorVersion\":1,\"MinorVersion\":0},\"Payload\":{" +
                "\"BoolField\":true,\"IntField\":1,\"StringField\":\"s\",\"x0\":null}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        public async Task DecodeRawPayloadResolvesLargeMetaDataByNameAsync()
        {
            const int fieldCount = 5000;
            var fields = new FieldMetaData[fieldCount];
            var payload = new StringBuilder();
            for (int i = 0; i < fieldCount; i++)
            {
                fields[i] = new FieldMetaData
                {
                    Name = "Field" + i,
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                };
            }
            // Members arrive in reverse order so the ordinal fallback cannot match.
            for (int i = fieldCount - 1; i >= 0; i--)
            {
                payload.Append(i == fieldCount - 1 ? string.Empty : ",")
                    .Append("\"Field").Append(i).Append("\":").Append(i);
            }
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(new DataSetMetaDataType
            {
                Name = "Large",
                Fields = fields,
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1 }
            });
            string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"MetaDataVersion\":{\"MajorVersion\":1,\"MinorVersion\":0},\"Payload\":{" +
                payload + "}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            ArrayOf<DataSetField> decoded = result.DataSetMessages[0].Fields;
            Assert.That(decoded, Has.Count.EqualTo(fieldCount));
            Assert.That(decoded[0].Name, Is.EqualTo("Field4999"));
            Assert.That(decoded[0].Value, Is.EqualTo(new Variant(4999)));
            Assert.That(decoded[fieldCount - 1].Value, Is.EqualTo(new Variant(0)));
        }

        [TestCase(PubSubDataSetMessageType.KeyFrame)]
        [TestCase(PubSubDataSetMessageType.DeltaFrame)]
        [TestCase(PubSubDataSetMessageType.KeepAlive)]
        [TestSpec("7.2.5.4.1")]
        public async Task HeaderlessSingleDataSetMessageWithMessageTypeAndPublisherIdRoundTripsAsync(
            PubSubDataSetMessageType messageType)
        {
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(
                JsonTestUtilities.CreateMetaData());
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                PublisherId = PublisherId.FromString("P"),
                SequenceNumber = 3,
                MessageType = messageType,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                ContentMask = JsonDataSetMessageContentMask.DataSetWriterId |
                    JsonDataSetMessageContentMask.PublisherId |
                    JsonDataSetMessageContentMask.SequenceNumber |
                    JsonDataSetMessageContentMask.MetaDataVersion |
                    JsonDataSetMessageContentMask.MessageType,
                Fields = JsonTestUtilities.CreateFields()
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                ContentMask = JsonNetworkMessageContentMask.DataSetMessageHeader |
                    JsonNetworkMessageContentMask.SingleDataSetMessage,
                DataSetMessages = [dsm]
            };
            ReadOnlyMemory<byte> bytes = await new Opc.Ua.PubSub.Encoding.Json.JsonEncoder()
                .EncodeAsync(message, ctx).ConfigureAwait(false);

            PubSubNetworkMessage? result = await new Opc.Ua.PubSub.Encoding.Json.JsonDecoder()
                .TryDecodeAsync(bytes, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null, JsonTestUtilities.ToText(bytes));
            Assert.That(result!.PublisherId, Is.EqualTo(PublisherId.FromString("P")));
            Assert.That(result.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].MessageType, Is.EqualTo(messageType));
            Assert.That(result.DataSetMessages[0].Fields,
                Has.Count.EqualTo(messageType == PubSubDataSetMessageType.KeepAlive ? 0 : 3));
        }

        [Test]
        [TestSpec("7.2.5.4.2")]
        public async Task DataSetMessageWithOnlyMinorVersionResolvesRegisteredMetaDataAsync()
        {
            var registry = new DataSetMetaDataRegistry();
            var key = new DataSetMetaDataKey(
                PublisherId.FromString("MyPublisher"), 0, 102, Uuid.Empty, 672341762);
            registry.Register(in key, new DataSetMetaDataType
            {
                Name = "Location",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "LocationName",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 672341762,
                    MinorVersion = 672341762
                }
            });
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            const string json =
                "{\"PublisherId\":\"MyPublisher\",\"DataSetWriterId\":102,\"SequenceNumber\":25460," +
                "\"MinorVersion\":672341762,\"Timestamp\":\"2021-09-27T18:45:19.555Z\"," +
                "\"Payload\":{\"LocationName\":\"Building A\"}}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant("Building A")));
        }

        [Test]
        [TestSpec("7.2.5.4.2")]
        public async Task VerboseCollapsedAndEnvelopedVariantsRoundTripAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            meta.Fields =
            [
                .. meta.Fields.ToArray()!,
                new FieldMetaData
                {
                    Name = "AnyField",
                    BuiltInType = (byte)BuiltInType.Variant,
                    ValueRank = ValueRanks.Scalar
                }
            ];
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(meta);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                Fields =
                [
                    .. JsonTestUtilities.CreateFields().ToArray()!,
                    new DataSetField { Name = "AnyField", Value = new Variant(2.5) }
                ]
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                PublisherId = PublisherId.FromString("P"),
                DataSetMessages = [dsm]
            };
            ReadOnlyMemory<byte> bytes = await new Opc.Ua.PubSub.Encoding.Json.JsonEncoder()
                .EncodeAsync(message, ctx).ConfigureAwait(false);
            string text = JsonTestUtilities.ToText(bytes);

            PubSubNetworkMessage? result = await new Opc.Ua.PubSub.Encoding.Json.JsonDecoder()
                .TryDecodeAsync(bytes, ctx).ConfigureAwait(false);

            Assert.That(text, Does.Contain("\"IntField\":42"), text);
            Assert.That(text, Does.Contain("\"AnyField\":{\"UaType\":11,\"Value\":2.5}"), text);
            Assert.That(result, Is.Not.Null);
            ArrayOf<DataSetField> fields = result!.DataSetMessages[0].Fields;
            Assert.That(fields[0].Value, Is.EqualTo(new Variant(true)));
            Assert.That(fields[1].Value, Is.EqualTo(new Variant(42)));
            Assert.That(fields[2].Value, Is.EqualTo(new Variant("hello")));
            Assert.That(fields[3].Value, Is.EqualTo(new Variant(2.5)));
            Assert.That(fields[3].Encoding, Is.EqualTo(PubSubFieldEncoding.Variant));
        }

        [Test]
        [TestSpec("7.2.5.4.2")]
        public void LegacyTypeBodyVariantIsNotDecodedAsVariant()
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                "{\"field\":{\"Type\":6,\"Body\":42}}");

            ArrayOf<DataSetField> fields = JsonFieldDecoder.DecodeFields(
                document.RootElement,
                metaData: null,
                JsonEncodingMode.Verbose,
                ServiceMessageContext.CreateEmpty(null!));

            Assert.That(fields[0].Value, Is.Not.EqualTo(new Variant(42)));
        }

        [TestCase(JsonEncodingMode.Verbose, "{\"Code\":1073741824,\"Symbol\":\"Uncertain\"}")]
        [TestCase(JsonEncodingMode.Compact, "{\"Code\":1073741824}")]
        [TestSpec("7.2.5.4.1")]
        public async Task DataSetMessageStatusIsEncodedAsStatusCodeObjectAsync(
            JsonEncodingMode mode,
            string expected)
        {
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(
                JsonTestUtilities.CreateMetaData());
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                Status = StatusCodes.Uncertain,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                ContentMask = JsonDataSetMessageContentMask.DataSetWriterId |
                    JsonDataSetMessageContentMask.MetaDataVersion |
                    JsonDataSetMessageContentMask.Status,
                Fields = JsonTestUtilities.CreateFields()
            };
            var message = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                PublisherId = PublisherId.FromString("P"),
                DataSetMessages = [dsm]
            };
            ReadOnlyMemory<byte> bytes = await new Opc.Ua.PubSub.Encoding.Json.JsonEncoder(mode)
                .EncodeAsync(message, ctx).ConfigureAwait(false);
            string text = JsonTestUtilities.ToText(bytes);

            PubSubNetworkMessage? result = await new Opc.Ua.PubSub.Encoding.Json.JsonDecoder()
                .TryDecodeAsync(bytes, ctx).ConfigureAwait(false);

            Assert.That(text, Does.Contain("\"Status\":" + expected), text);
            Assert.That(result, Is.Not.Null);
            Assert.That(
                ((Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage)result!.DataSetMessages[0]).Status,
                Is.EqualTo((StatusCode)StatusCodes.Uncertain));
        }

        [Test]
        [TestSpec("7.2.5.4.1")]
        public async Task DataSetMessageNumericStatusIsRejectedAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Status\":1073741824," +
                "\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.4.4.2")]
        public async Task GuidShapedStringPublisherIdStaysStringAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string publisherId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"" + publisherId + "\",\"Messages\":[" +
                "{\"DataSetWriterId\":1,\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.PublisherId, Is.EqualTo(PublisherId.FromString(publisherId)));
        }

        private static PubSubNetworkMessageContext NewContextWithMetaData(
            DataSetMetaDataType metaData)
        {
            var registry = new DataSetMetaDataRegistry();
            var key = new DataSetMetaDataKey(
                PublisherId.FromString("P"),
                0,
                1,
                Uuid.Empty,
                metaData.ConfigurationVersion.MajorVersion);
            registry.Register(in key, metaData);
            return JsonTestUtilities.NewContext(registry);
        }

        private static async Task<PubSubNetworkMessage?> DecodeAsync(
            string json,
            PubSubNetworkMessageContext ctx)
        {
            var decoder = new Opc.Ua.PubSub.Encoding.Json.JsonDecoder();
            return await decoder.TryDecodeAsync(Encoding.UTF8.GetBytes(json), ctx)
                .ConfigureAwait(false);
        }
    }
}
