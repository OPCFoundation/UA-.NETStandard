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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using JsonEncoderV2 = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Unit tests for the experimental Avro transcoding support: projection to the Avro mapping,
    /// encoding classification, and the progressive schema generation / reset lifecycle.
    /// </summary>
    [TestFixture]
    public class AvroTranscodingTests
    {
        private static AvroNetworkMessage NewAvroMessage(
            uint majorVersion = 1,
            params DataSetField[] fields)
        {
            return new AvroNetworkMessage
            {
                PublisherId = PublisherId.FromByte(3),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 55,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = majorVersion,
                            MinorVersion = 0
                        },
                        Fields = fields
                    }
                ]
            };
        }

        [Test]
        public void AvroTransportProfileUri_RoundTripsThroughClassification()
        {
            string uri = TranscodeEncoding.Avro.ToTransportProfileUri();

            Assert.That(uri, Is.EqualTo(AvroNetworkMessage.PubSubMqttAvroTransport));
            Assert.That(uri.FromTransportProfileUri(), Is.EqualTo(TranscodeEncoding.Avro));
        }

        [Test]
        public void EncodingOf_AvroNetworkMessage_ReturnsAvro()
        {
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(9)));

            Assert.That(message.EncodingOf(), Is.EqualTo(TranscodeEncoding.Avro));
        }

        [Test]
        public void SchemaCache_TryParseKey_RoundTripsToKeyAndRejectsInvalid()
        {
            var raw = ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
            string key = SchemaCache.ToKey(raw);

            Assert.That(SchemaCache.TryParseKey(key, out ByteString parsed), Is.True);
            Assert.That(parsed.Span.SequenceEqual(raw.Span), Is.True);

            Assert.That(SchemaCache.TryParseKey(null, out _), Is.False);
            Assert.That(SchemaCache.TryParseKey("zzzzzzzzzzzzzzzz", out _), Is.False);
        }

        [Test]
        public void FromTransportProfileUri_ClassifiesEncodingFamily()
        {
            Assert.That(
                ((string)null!).FromTransportProfileUri(),
                Is.EqualTo(TranscodeEncoding.Uadp));
            Assert.That(
                Profiles.PubSubUdpUadpTransport.FromTransportProfileUri(),
                Is.EqualTo(TranscodeEncoding.Uadp));
            Assert.That(
                Profiles.PubSubMqttJsonTransport.FromTransportProfileUri(),
                Is.EqualTo(TranscodeEncoding.Json));
            Assert.That(
                AvroNetworkMessage.PubSubMqttAvroTransport.FromTransportProfileUri(),
                Is.EqualTo(TranscodeEncoding.Avro));
        }

        [Test]
        public void Project_UadpToAvro_PreservesIdentityAndFields()
        {
            var source = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Avro, TranscodeTargetOptions.Default, NewContext());

            var avro = (AvroNetworkMessage)projected;
            Assert.That(avro.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(avro.WriterGroupId, Is.EqualTo((ushort)7));
            Assert.That(avro.DataSetMessages[0], Is.InstanceOf<AvroDataSetMessage>());
            Assert.That(avro.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)55));
            Assert.That(avro.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public void Project_AvroToAvro_IdentityReturnsSameInstance()
        {
            AvroNetworkMessage source = NewAvroMessage(1, Field("a", new Variant(1)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Avro, TranscodeTargetOptions.Default, NewContext());

            Assert.That(projected, Is.SameAs(source));
        }

        [Test]
        public void Project_AvroToAvro_FieldEncodingOption_RebuildsAndPreservesFieldContentMask()
        {
            var source = new AvroNetworkMessage
            {
                PublisherId = PublisherId.FromByte(3),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 55,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        FieldContentMask = DataSetFieldContentMask.StatusCode,
                        Fields = [Field("x", new Variant(9))]
                    }
                ]
            };
            var options = new TranscodeTargetOptions { FieldEncoding = PubSubFieldEncoding.RawData };

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Avro, options, NewContext());

            Assert.That(projected, Is.Not.SameAs(source));
            var dsm = (AvroDataSetMessage)projected.DataSetMessages[0];
            Assert.That(dsm.FieldContentMask, Is.EqualTo(DataSetFieldContentMask.StatusCode));
            Assert.That(dsm.Fields[0].Encoding, Is.EqualTo(PubSubFieldEncoding.RawData));
            Assert.That(dsm.Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public async Task AvroEncoder_AnnouncesSchemaOncePerShape()
        {
            var encoder = new AvroNetworkMessageEncoder();
            TranscodeContext context = NewContext();
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(9)));

            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null,
                "The first encode should announce the schema.");

            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Null,
                "The second encode of the same shape should not re-announce.");
        }

        [Test]
        public async Task AvroEncoder_MetaDataVersionChange_ReAnnouncesSchema()
        {
            var encoder = new AvroNetworkMessageEncoder();
            TranscodeContext context = NewContext();

            await encoder.EncodeAsync(
                NewAvroMessage(1, Field("x", new Variant(9))), context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null);

            await encoder.EncodeAsync(
                NewAvroMessage(2, Field("x", new Variant(9))), context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null,
                "A DataSet MetaData version change should re-announce the schema.");
        }

        [Test]
        public async Task AvroEncoder_SchemaCacheReset_ReAnnouncesSchema()
        {
            var encoder = new AvroNetworkMessageEncoder();
            TranscodeContext context = NewContext();
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(9)));

            await encoder.EncodeAsync(message, context.EncodingContext);
            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Null);

            encoder.SchemaCache.Reset();

            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null,
                "After a schema-cache reset the schema should be announced again.");
        }

        [Test]
        public async Task AvroEncode_Decode_RoundTripsFields()
        {
            var encoder = new AvroNetworkMessageEncoder();
            var decoder = new AvroNetworkMessageDecoder();
            TranscodeContext context = NewContext();
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(42)));

            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(
                message, context.EncodingContext);
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(
                frame, context.EncodingContext);

            Assert.That(decoded, Is.InstanceOf<AvroNetworkMessage>());
            Assert.That(decoded!.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(decoded.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)55));
            Assert.That(
                decoded.DataSetMessages[0].Fields[0].Value,
                Is.EqualTo(new Variant(42)));
        }

        [Test]
        public async Task Transcode_UadpToAvro_ProducesDecodableAvroFrame()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Avro };
            var transcoder = new PubSubTranscoder(spec, TranscodingTestUtilities.Encoders(), context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Dropped, Is.False);
            Assert.That(result.Frames.Count, Is.EqualTo(1));
            Assert.That(result.Messages[0], Is.InstanceOf<AvroNetworkMessage>());

            AvroNetworkMessage decoded = await DecodeAvroAsync(result.Frames[0], context)
                .ConfigureAwait(false);
            Assert.That(decoded.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(
                decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public void Project_AvroToUadp_PreservesIdentityAndFields()
        {
            AvroNetworkMessage source = NewAvroMessage(1, Field("x", new Variant(9)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Uadp, TranscodeTargetOptions.Default, NewContext());

            var uadp = (UadpNetworkMessageV2)projected;
            Assert.That(uadp.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(uadp.WriterGroupId, Is.EqualTo((ushort)7));
            Assert.That(uadp.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public void Project_AvroToJson_PreservesIdentityAndFields()
        {
            AvroNetworkMessage source = NewAvroMessage(1, Field("x", new Variant(9)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Json, TranscodeTargetOptions.Default, NewContext());

            var json = (JsonNetworkMessageV2)projected;
            Assert.That(json.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(json.WriterGroupId, Is.EqualTo((ushort)7));
            Assert.That(json.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public async Task JsonTranscodeDefaultDisabledDoesNotAnnounce()
        {
            JsonEncoderV2 jsonEncoder = new()
            {
                SchemaProvider = new DeterministicJsonSchemaProvider()
            };
            TranscodeContext context = NewContext();
            var encoders = new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal)
            {
                [jsonEncoder.TransportProfileUri] = jsonEncoder
            };
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            var transcoder = new PubSubTranscoder(spec, encoders, context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9))) with
            {
                MetaData = NewMetaData(includeSecondField: false)
            };

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Dropped, Is.False);
                Assert.That(result.Messages[0], Is.InstanceOf<JsonNetworkMessageV2>());
                Assert.That(jsonEncoder.EnableSchemaExchange, Is.False);
                Assert.That(jsonEncoder.LastSchemaAnnouncement, Is.Null);
            });
        }

        [Test]
        public async Task JsonTranscodeEnabledAnnouncesProgressivelyAndReannouncesOnSchemaChange()
        {
            JsonEncoderV2 jsonEncoder = new()
            {
                EnableSchemaExchange = true,
                SchemaProvider = new DeterministicJsonSchemaProvider(),
                DestinationId = "transcode-json-route"
            };
            TranscodeContext context = NewContext();
            var encoders = new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal)
            {
                [jsonEncoder.TransportProfileUri] = jsonEncoder
            };
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            var transcoder = new PubSubTranscoder(spec, encoders, context);
            UadpNetworkMessageV2 first = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9))) with
            {
                MetaData = NewMetaData(includeSecondField: false)
            };
            UadpNetworkMessageV2 changed = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9))) with
            {
                MetaData = NewMetaData(includeSecondField: true)
            };

            _ = await transcoder.TranscodeAsync(new TranscodeInput(first)).ConfigureAwait(false);
            JsonSchemaAnnouncement? firstAnnouncement = jsonEncoder.LastSchemaAnnouncement;
            _ = await transcoder.TranscodeAsync(new TranscodeInput(first)).ConfigureAwait(false);
            JsonSchemaAnnouncement? repeatAnnouncement = jsonEncoder.LastSchemaAnnouncement;
            _ = await transcoder.TranscodeAsync(new TranscodeInput(changed)).ConfigureAwait(false);
            JsonSchemaAnnouncement? changedAnnouncement = jsonEncoder.LastSchemaAnnouncement;

            Assert.Multiple(() =>
            {
                Assert.That(firstAnnouncement, Is.Not.Null);
                Assert.That(repeatAnnouncement, Is.Null);
                Assert.That(changedAnnouncement, Is.Not.Null);
                Assert.That(changedAnnouncement!.SchemaId, Is.Not.EqualTo(firstAnnouncement!.SchemaId));
            });
        }

        private static DataSetMetaDataType NewMetaData(bool includeSecondField)
        {
            FieldMetaData[] fields = includeSecondField
                ?
                [
                    new FieldMetaData
                    {
                        Name = "x",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "y",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
                :
                [
                    new FieldMetaData
                    {
                        Name = "x",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ];
            return new DataSetMetaDataType
            {
                Name = "TranscodeDataSet",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 },
                Fields = fields
            };
        }

        private sealed class DeterministicJsonSchemaProvider : IDataSetJsonSchemaProvider
        {
            public string CreateJsonSchema(DataSetMetaDataType metaData, bool verbose = false)
            {
                using System.IO.MemoryStream stream = new();
                using (System.Text.Json.Utf8JsonWriter writer = new(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "object");
                    writer.WriteBoolean("verbose", verbose);
                    writer.WriteStartArray("fields");
                    if (!metaData.Fields.IsNull)
                    {
                        for (int i = 0; i < metaData.Fields.Count; i++)
                        {
                            writer.WriteStringValue(metaData.Fields[i].Name ?? string.Empty);
                        }
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
