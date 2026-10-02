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
        // 9 characters but 18 UTF-8 bytes: MaxStringLength counts bytes.
        [TestCase("{\"MessageType\":\"ua-data\",\"MessageId\":\"ééééééééé\",\"Messages\":[]}")]
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
        [TestSpec("6.2.3.2.6")]
        public async Task DecodePayloadWithFieldsAppendedByMinorVersionIsAcceptedAsync()
        {
            // Table 11: appending fields only bumps the MinorVersion, so the
            // Subscriber's older metadata of the same MajorVersion still applies.
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(
                JsonTestUtilities.CreateMetaData());
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"MetaDataVersion\":{\"MajorVersion\":1,\"MinorVersion\":5},\"Payload\":{" +
                "\"BoolField\":true,\"IntField\":1,\"StringField\":\"s\",\"Appended\":2.5}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            ArrayOf<DataSetField> fields = result.DataSetMessages[0].Fields;
            Assert.That(fields, Has.Count.EqualTo(4));
            Assert.That(fields[1].Value, Is.EqualTo(new Variant(1)));
            Assert.That(fields[2].Value, Is.EqualTo(new Variant("s")));
            Assert.That(fields[3].Name, Is.EqualTo("Appended"));
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.Zero);
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

        [TestCase(9u, false)]
        [TestCase(10u, true)]
        [TestCase(12u, true)]
        [TestSpec("6.2.3.2.6")]
        public async Task MinorVersionOnlyMessageOlderThanRegisteredMajorVersionIsNotDecodedAsync(
            uint minorVersion,
            bool decoded)
        {
            // Table 11: a MajorVersion change sets the MinorVersion to the same
            // value, so a message MinorVersion below the registered MajorVersion
            // was produced with an older layout.
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            meta.ConfigurationVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 10,
                MinorVersion = 10
            };
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(meta);
            string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"MinorVersion\":" + minorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"Payload\":{\"BoolField\":true,\"IntField\":1,\"StringField\":\"s\"}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(decoded ? 1 : 0));
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ResolverErrors),
                Is.EqualTo(decoded ? 0 : 1));
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
        [TestSpec("6.3.2.3.1")]
        public void DeprecatedReversibleTypeBodyVariantIsDecoded()
        {
            // Table 112 FieldEncoding1=True/FieldEncoding2=False still defines
            // the deprecated ReversibleFieldEncoding.
            using var document = System.Text.Json.JsonDocument.Parse(
                "{\"field\":{\"Type\":6,\"Body\":42},\"dv\":{\"Value\":{\"Type\":12,\"Body\":\"x\"}," +
                "\"SourceTimestamp\":\"2021-09-27T11:32:38.349Z\"}}");

            ArrayOf<DataSetField> fields = JsonFieldDecoder.DecodeFields(
                document.RootElement,
                metaData: null,
                JsonEncodingMode.Verbose,
                ServiceMessageContext.CreateEmpty(null!));

            Assert.That(fields[0].Value, Is.EqualTo(new Variant(42)));
            Assert.That(fields[1].Value, Is.EqualTo(new Variant("x")));
            Assert.That(fields[1].Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
        }

        [Test]
        [TestSpec("6.3.2.3.1")]
        public async Task DeprecatedReversibleTypeBodyPayloadIsDecodedAsync()
        {
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(
                JsonTestUtilities.CreateMetaData());
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"Payload\":{\"BoolField\":{\"Type\":1,\"Body\":true},\"IntField\":{\"Type\":6,\"Body\":5}," +
                "\"StringField\":{\"Type\":12,\"Body\":\"s\"}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(result.DataSetMessages[0].Fields[1].Value, Is.EqualTo(new Variant(5)));
        }

        [Test]
        [TestSpec("7.2.5.4.2")]
        public async Task VerboseEnumerationFieldIsEncodedAsNameValueStringAsync()
        {
            var enumType = new NodeId(3001, 1);
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            meta.Fields =
            [
                .. meta.Fields.ToArray()!,
                new FieldMetaData
                {
                    Name = "State",
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = enumType,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "ServerState",
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = DataTypeIds.ServerState,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "States",
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = enumType,
                    ValueRank = ValueRanks.OneDimension
                }
            ];
            meta.EnumDataTypes =
            [
                new EnumDescription
                {
                    DataTypeId = enumType,
                    Name = new QualifiedName("MachineState", 1),
                    EnumDefinition = new EnumDefinition
                    {
                        Fields =
                        [
                            new EnumField { Name = "Running", Value = 0 },
                            new EnumField { Name = "Suspended", Value = 3 }
                        ]
                    }
                }
            ];
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(meta);
            ArrayOf<int> states = [0, 3, 7];
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                Fields =
                [
                    .. JsonTestUtilities.CreateFields().ToArray()!,
                    new DataSetField { Name = "State", Value = new Variant(3) },
                    new DataSetField { Name = "ServerState", Value = new Variant(3) },
                    new DataSetField { Name = "States", Value = new Variant(states) }
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

            Assert.That(text, Does.Contain("\"State\":\"Suspended_3\""), text);
            // Without a known name the numeric value is encoded as a JSON string.
            Assert.That(text, Does.Contain("\"ServerState\":\"3\""), text);
            Assert.That(text, Does.Contain("\"States\":[\"Running_0\",\"Suspended_3\",\"7\"]"), text);
            Assert.That(result, Is.Not.Null, text);
            ArrayOf<DataSetField> fields = result!.DataSetMessages[0].Fields;
            Assert.That(fields[3].Value, Is.EqualTo(new Variant(3)));
            Assert.That(fields[4].Value, Is.EqualTo(new Variant(3)));
            Assert.That(fields[5].Value, Is.EqualTo(new Variant(states)));
        }

        [Test]
        [TestSpec("7.2.5.4.3")]
        public async Task VerboseFieldsWithoutUaTypeAreTypedByFieldMetaDataAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            meta.Fields =
            [
                NewField("LocationName", BuiltInType.String, ValueRanks.Scalar),
                NewField("Suspended", BuiltInType.Int32, ValueRanks.Scalar, DataTypeIds.ServerState),
                NewField("Range", BuiltInType.ExtensionObject, ValueRanks.Scalar, DataTypeIds.Range),
                NewField("Matrix", BuiltInType.Int32, ValueRanks.TwoDimensions),
                NewField("AnyScalar", BuiltInType.Int32, ValueRanks.Any),
                NewField("ScalarOrArray", BuiltInType.Int32, ValueRanks.ScalarOrOneDimension),
                NewField("Ranges", BuiltInType.ExtensionObject, ValueRanks.OneDimension, DataTypeIds.Range)
            ];
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(meta);
            // Part 14 7.2.5.4.2 second example and Table 186 / Table 187 rows.
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"Payload\":{\"LocationName\":{\"Value\":\"Building A\"," +
                "\"Status\":{\"Code\":1073741824,\"Symbol\":\"Uncertain\"}," +
                "\"SourceTimestamp\":\"2021-09-27T11:32:38.349925Z\"}," +
                "\"Suspended\":\"Suspended_3\"," +
                "\"Range\":{\"Low\":1,\"High\":2}," +
                "\"Matrix\":{\"Value\":[1,2,3,4],\"Dimensions\":[2,2]}," +
                "\"AnyScalar\":{\"Value\":11}," +
                "\"ScalarOrArray\":[1,2,3,4]," +
                "\"Ranges\":[{\"Low\":1,\"High\":2}]}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            ArrayOf<DataSetField> fields = result.DataSetMessages[0].Fields;
            Assert.Multiple(() =>
            {
                Assert.That(fields[0].Value, Is.EqualTo(new Variant("Building A")));
                Assert.That(fields[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
                Assert.That(fields[0].SourceTimestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
                Assert.That(fields[0].Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
                Assert.That(fields[1].Value, Is.EqualTo(new Variant(3)));
                Assert.That(fields[2].Value.TryGetValue(out ExtensionObject range), Is.True);
                Assert.That(range.IsNull, Is.False);
                Assert.That(range.TypeId.IsNull, Is.False);
                Assert.That(fields[3].Value.TypeInfo.ValueRank, Is.EqualTo(2));
                Assert.That(fields[4].Value, Is.EqualTo(new Variant(11)));
                Assert.That(fields[5].Value.GetInt32Array(), Has.Count.EqualTo(4));
                Assert.That(fields[6].Value.IsNull, Is.False);
                Assert.That(fields[6].Value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
            });
        }

        [Test]
        [TestSpec("7.2.5.4.3")]
        public async Task VerboseAbstractFieldWithoutUaTypeIsRejectedAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            meta.Fields = [NewField("Any", BuiltInType.Variant, ValueRanks.Scalar)];
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(meta);
            const string json =
                "{\"MessageType\":\"ua-data\",\"PublisherId\":\"P\",\"Messages\":[{\"DataSetWriterId\":1," +
                "\"Payload\":{\"Any\":{\"Value\":\"Apple\"}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.Zero);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.FailedDataSetMessages),
                Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.5.4.3")]
        public async Task VerboseDataValueFieldOmitsUaTypeForConcreteTypeAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            PubSubNetworkMessageContext ctx = NewContextWithMetaData(meta);
            var sourceTimestamp = (DateTimeUtc)new DateTime(2025, 5, 26, 11, 20, 7, 951, DateTimeKind.Utc);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                Fields =
                [
                    new DataSetField
                    {
                        Name = "BoolField",
                        Value = new Variant(true),
                        Encoding = PubSubFieldEncoding.DataValue
                    },
                    new DataSetField
                    {
                        Name = "IntField",
                        Value = new Variant(1234),
                        SourceTimestamp = sourceTimestamp,
                        Encoding = PubSubFieldEncoding.DataValue
                    },
                    new DataSetField
                    {
                        Name = "StringField",
                        Value = new Variant("Apple"),
                        Encoding = PubSubFieldEncoding.DataValue
                    }
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

            Assert.That(text, Does.Contain(
                "\"IntField\":{\"Value\":1234,\"SourceTimestamp\":\"2025-05-26T11:20:07.951Z\"}"), text);
            Assert.That(text, Does.Not.Contain("UaType"), text);
            Assert.That(result, Is.Not.Null, text);
            ArrayOf<DataSetField> fields = result!.DataSetMessages[0].Fields;
            Assert.That(fields[1].Value, Is.EqualTo(new Variant(1234)));
            Assert.That(fields[1].SourceTimestamp, Is.EqualTo(sourceTimestamp));
            Assert.That(fields[2].Value, Is.EqualTo(new Variant("Apple")));
            Assert.That(fields[2].Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
        }

        private static FieldMetaData NewField(
            string name,
            BuiltInType builtInType,
            int valueRank,
            NodeId dataType = default)
        {
            return new FieldMetaData
            {
                Name = name,
                BuiltInType = (byte)builtInType,
                ValueRank = valueRank,
                DataType = dataType
            };
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
        public async Task DataSetMessageLegacyNumericStatusIsAcceptedAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Status\":1073741824," +
                "\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(
                ((Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage)result.DataSetMessages[0]).Status,
                Is.EqualTo((StatusCode)StatusCodes.Uncertain));
        }

        [TestCase("\"Uncertain\"")]
        [TestCase("[1073741824]")]
        [TestCase("-1")]
        [TestSpec("7.2.5.4.1")]
        public async Task DataSetMessageMalformedStatusIsRejectedAsync(string status)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Status\":" + status +
                ",\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7}}}]}";

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

        [TestCase("2021-09-27T18:45:19.555Z", "2021-09-27T18:45:19.5550000Z")]
        [TestCase("2021-09-27T18:45:19.555", "2021-09-27T18:45:19.5550000Z")]
        [TestCase("2021-09-27T20:45:19.555+02:00", "2021-09-27T18:45:19.5550000Z")]
        [TestCase("9/27/2021", null)]
        [TestSpec("7.2.5.4.1")]
        public async Task DataSetMessageTimestampIsParsedAsIso8601UtcAsync(string wire, string? expected)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            string json =
                "{\"MessageType\":\"ua-data\",\"Messages\":[{\"DataSetWriterId\":1,\"Timestamp\":\"" + wire +
                "\",\"Payload\":{\"a\":{\"UaType\":6,\"Value\":7}}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            DateTimeUtc timestamp =
                ((Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage)result!.DataSetMessages[0]).Timestamp;
            if (expected is null)
            {
                Assert.That(timestamp, Is.EqualTo(DateTimeUtc.MinValue));
                return;
            }
            Assert.That(
                ((DateTime)timestamp).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(expected));
        }

        [Test]
        [TestSpec("7.2.5.6")]
        public async Task ActionRequestEnvelopeWithResponseEntryIsRejectedAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string json =
                "{\"MessageId\":\"a\",\"MessageType\":\"ua-action-request\",\"PublisherId\":\"P\"," +
                "\"Messages\":[{\"DataSetWriterId\":1,\"RequestId\":1}," +
                "{\"DataSetWriterId\":1,\"RequestId\":2,\"Status\":{\"Code\":2147483648}}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(JsonTestUtilities.Read(ctx,
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.5.6")]
        public async Task ActionResponseEnvelopeDecodesEveryEntryAsResponseAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            const string json =
                "{\"MessageId\":\"a\",\"MessageType\":\"ua-action-response\",\"PublisherId\":\"P\"," +
                "\"Messages\":[{\"DataSetWriterId\":1,\"RequestId\":7}]}";

            PubSubNetworkMessage? result = await DecodeAsync(json, ctx).ConfigureAwait(false);

            var action = result as Opc.Ua.PubSub.Encoding.Json.JsonActionNetworkMessage;
            Assert.That(action, Is.Not.Null);
            Assert.That(action!.Messages, Has.Count.EqualTo(1));
            Assert.That(action.Messages[0].TryGetValue(out Opc.Ua.JsonActionResponseMessage? response), Is.True);
            Assert.That(response!.RequestId, Is.EqualTo((ushort)7));
            Assert.That(action.MessageId, Is.EqualTo("a"));
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
