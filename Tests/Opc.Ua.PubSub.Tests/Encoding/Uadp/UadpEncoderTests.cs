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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Round-trip tests for every supported UADP NetworkMessage variant.
    /// Validates the encoder produces bytes the decoder can rehydrate
    /// back into an equivalent message.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5")]
    [TestSpec("A.2.2.4")]
    public class UadpEncoderTests
    {
        [Test]
        public async Task BareDataSetMessage_RoundTrips()
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId,
                PublisherId = PublisherId.FromByte(7),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant(42) } ]
                    }
                ]
            };

            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);

            Assert.That(decoded.DataSetMessages, Has.Count.EqualTo(1));
            var ds = (UadpDataSetMessage)decoded.DataSetMessages[0];
            Assert.That(ds.Fields[0].Value, Is.EqualTo(new Variant(42)));
        }

        [Test]
        public async Task GroupHeader_AllOptionalFields_RoundTrip()
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.GroupHeader
                    | UadpNetworkMessageContentMask.WriterGroupId
                    | UadpNetworkMessageContentMask.GroupVersion
                    | UadpNetworkMessageContentMask.NetworkMessageNumber
                    | UadpNetworkMessageContentMask.SequenceNumber,
                PublisherId = PublisherId.FromUInt16(1234),
                WriterGroupId = 5,
                GroupVersion = 0x12345678,
                NetworkMessageNumber = 9,
                SequenceNumber = 42,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant(1.5) } ]
                    }
                ]
            };

            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);

            Assert.That(decoded.WriterGroupId, Is.EqualTo((ushort)5));
            Assert.That(decoded.GroupVersion, Is.EqualTo(0x12345678u));
            Assert.That(decoded.NetworkMessageNumber, Is.EqualTo((ushort)9));
            Assert.That(decoded.SequenceNumber, Is.EqualTo((ushort)42));
        }

        [Test]
        public async Task PayloadHeader_MultipleDataSetMessages_RoundTrip()
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 11,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant((uint)10) } ]
                    },
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 12,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant((uint)20) } ]
                    },
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 13,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant((uint)30) } ]
                    }
                ]
            };

            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);

            Assert.That(decoded.DataSetMessages, Has.Count.EqualTo(3));
            Assert.That(decoded.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)11));
            Assert.That(decoded.DataSetMessages[1].DataSetWriterId, Is.EqualTo((ushort)12));
            Assert.That(decoded.DataSetMessages[2].DataSetWriterId, Is.EqualTo((ushort)13));
        }

        [Test]
        public async Task ExtendedFlags1_DataSetClassId_Timestamp_PicoSeconds_RoundTrip()
        {
            var classId = new Uuid("AABBCCDD-1122-3344-5566-778899AABBCC");
            var ts = new DateTimeUtc(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
            var msg = new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.DataSetClassId
                    | UadpNetworkMessageContentMask.Timestamp
                    | UadpNetworkMessageContentMask.PicoSeconds,
                PublisherId = PublisherId.FromByte(2),
                DataSetClassId = classId,
                Timestamp = ts,
                PicoSeconds = 0x4321,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant(true) } ]
                    }
                ]
            };

            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);

            Assert.That(decoded.DataSetClassId, Is.EqualTo(classId));
            Assert.That(decoded.Timestamp, Is.EqualTo(ts));
            Assert.That(decoded.PicoSeconds, Is.EqualTo((ushort)0x4321));
        }

        [Test]
        public async Task PromotedFields_RoundTrip()
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.PromotedFields,
                PublisherId = PublisherId.FromByte(3),
                PromotedFields =
                [
                    new DataSetField { Value = new Variant((uint)100) },
                    new DataSetField { Value = new Variant("alarm") }
                ],
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant("payload") } ]
                    }
                ]
            };

            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);

            Assert.That(decoded.PromotedFields, Has.Count.EqualTo(2));
            Assert.That(decoded.PromotedFields[0].Value, Is.EqualTo(new Variant((uint)100)));
            Assert.That(decoded.PromotedFields[1].Value, Is.EqualTo(new Variant("alarm")));
        }

        [Test]
        public async Task FieldEncoding_Variant_RoundTrips()
        {
            UadpDataSetMessage decoded = await SingleMessageRoundTripAsync(
                new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    FieldEncoding = PubSubFieldEncoding.Variant,
                    Fields =
                    [
                        new DataSetField { Value = new Variant((short)-7) },
                        new DataSetField { Value = new Variant("hello") },
                        new DataSetField { Value = new Variant(3.14) }
                    ]
                }).ConfigureAwait(false);

            Assert.That(decoded.Fields, Has.Count.EqualTo(3));
            Assert.That(decoded.Fields[0].Value, Is.EqualTo(new Variant((short)-7)));
            Assert.That(decoded.Fields[1].Value, Is.EqualTo(new Variant("hello")));
            Assert.That(decoded.Fields[2].Value, Is.EqualTo(new Variant(3.14)));
        }

        [Test]
        public async Task FieldEncoding_DataValue_RoundTrips()
        {
            var src = new DateTimeUtc(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
            UadpDataSetMessage decoded = await SingleMessageRoundTripAsync(
                new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    FieldEncoding = PubSubFieldEncoding.DataValue,
                    Fields =
                    [
                        new DataSetField
                        {
                            Value = new Variant(99u),
                            StatusCode = (StatusCode)StatusCodes.Good,
                            SourceTimestamp = src
                        }
                    ]
                }).ConfigureAwait(false);

            Assert.That(decoded.Fields[0].Value, Is.EqualTo(new Variant(99u)));
        }

        [Test]
        public async Task FieldEncoding_RawData_RoundTrips()
        {
            // RawData requires DataSetMetaData; register one for the writer.
            var publisherId = PublisherId.FromByte(8);
            ushort writerGroupId = 1;
            ushort dataSetWriterId = 50;
            var classId = new Uuid("11223344-5566-7788-99AA-BBCCDDEEFF00");
            var version = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 0 };
            var meta = new DataSetMetaDataType
            {
                ConfigurationVersion = version,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "scalar1",
                        BuiltInType = (byte)BuiltInType.UInt32,
                        ValueRank = -1
                    },
                    new FieldMetaData
                    {
                        Name = "scalar2",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = -1
                    }
                ]
            };
            var registry = new DataSetMetaDataRegistry();
            var key = new DataSetMetaDataKey(
                publisherId, writerGroupId, dataSetWriterId, classId, 1);
            registry.Register(key, meta);

            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext(registry);

            var msg = new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.GroupHeader
                    | UadpNetworkMessageContentMask.WriterGroupId
                    | UadpNetworkMessageContentMask.PayloadHeader
                    | UadpNetworkMessageContentMask.DataSetClassId,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetClassId = classId,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = dataSetWriterId,
                        FieldEncoding = PubSubFieldEncoding.RawData,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = version,
                        Fields =
                        [
                            new DataSetField { Value = new Variant(123u) },
                            new DataSetField { Value = new Variant(2.5) }
                        ]
                    }
                ]
            };

            var encoder = new UadpEncoder();
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);

            var decoder = new UadpDecoder();
            PubSubNetworkMessage? decodedMsg =
                await decoder.TryDecodeAsync(bytes, context).ConfigureAwait(false);

            Assert.That(decodedMsg, Is.Not.Null);
            var decoded = (UadpNetworkMessage)decodedMsg!;
            var ds = (UadpDataSetMessage)decoded.DataSetMessages[0];
            Assert.That(ds.Fields, Has.Count.EqualTo(2));
            Assert.That(ds.Fields[0].Value, Is.EqualTo(new Variant(123u)));
            Assert.That(ds.Fields[1].Value, Is.EqualTo(new Variant(2.5)));
        }

        [Test]
        public async Task DataSetMessage_AllHeaderOptions_RoundTrip()
        {
            var ts = new DateTimeUtc(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        ContentMask =
                            UadpDataSetMessageContentMask.SequenceNumber
                            | UadpDataSetMessageContentMask.Timestamp
                            | UadpDataSetMessageContentMask.PicoSeconds
                            | UadpDataSetMessageContentMask.Status
                            | UadpDataSetMessageContentMask.MajorVersion
                            | UadpDataSetMessageContentMask.MinorVersion,
                        SequenceNumber = 0xDEAD,
                        Timestamp = ts,
                        PicoSeconds = 0xBEEF,
                        Status = (StatusCode)0x80350000u,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = 2,
                            MinorVersion = 3
                        },
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = [ new DataSetField { Value = new Variant("ok") } ]
                    }
                ]
            };

            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);

            var ds = (UadpDataSetMessage)decoded.DataSetMessages[0];
            Assert.That(ds.SequenceNumber, Is.EqualTo(0xDEADu));
            Assert.That(ds.PicoSeconds, Is.EqualTo((ushort)0xBEEF));
            Assert.That(ds.Status, Is.EqualTo((StatusCode)0x80350000u));
            Assert.That(ds.MetaDataVersion.MajorVersion, Is.EqualTo(2u));
            Assert.That(ds.MetaDataVersion.MinorVersion, Is.EqualTo(3u));
            Assert.That(ds.Timestamp, Is.EqualTo(ts));
        }

        [Test]
        public async Task DataSetMessage_DeltaFrame_RoundTrips()
        {
            UadpDataSetMessage decoded = await SingleMessageRoundTripAsync(
                new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    FieldEncoding = PubSubFieldEncoding.Variant,
                    MessageType = PubSubDataSetMessageType.DeltaFrame,
                    Fields = [ new DataSetField { Value = new Variant(42) } ]
                }).ConfigureAwait(false);

            Assert.That(decoded.MessageType, Is.EqualTo(PubSubDataSetMessageType.DeltaFrame));
            Assert.That(decoded.Fields, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DataSetMessage_KeepAlive_HasNoFields()
        {
            UadpDataSetMessage decoded = await SingleMessageRoundTripAsync(
                new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    FieldEncoding = PubSubFieldEncoding.Variant,
                    MessageType = PubSubDataSetMessageType.KeepAlive,
                    Fields = []
                }).ConfigureAwait(false);

            Assert.That(decoded.MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
            Assert.That(decoded.Fields, Is.Empty);
        }

        [Test]
        public async Task DataSetMessage_Event_RoundTrips()
        {
            UadpDataSetMessage decoded = await SingleMessageRoundTripAsync(
                new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    FieldEncoding = PubSubFieldEncoding.Variant,
                    MessageType = PubSubDataSetMessageType.Event,
                    Fields =
                    [
                        new DataSetField { Value = new Variant("EventTrigger") },
                        new DataSetField { Value = new Variant((ushort)500) }
                    ]
                }).ConfigureAwait(false);

            Assert.That(decoded.MessageType, Is.EqualTo(PubSubDataSetMessageType.Event));
            Assert.That(decoded.Fields, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task EncodeAsync_NullMessage_Throws()
        {
            var encoder = new UadpEncoder();
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            Assert.That(
                async () => await encoder.EncodeAsync(null!, context).ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public async Task EncodeAsync_NullContext_Throws()
        {
            var encoder = new UadpEncoder();
            var msg = new UadpNetworkMessage();
            Assert.That(
                async () => await encoder.EncodeAsync(msg, null!).ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public async Task EncodeAsync_RejectsNonUadpNetworkMessage()
        {
            var encoder = new UadpEncoder();
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var foreign = new ForeignNetworkMessage();
            Assert.That(
                async () => await encoder.EncodeAsync(foreign, context).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public void EncodeAsync_RejectsCancelledToken()
        {
            var encoder = new UadpEncoder();
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            Assert.That(
                async () => await encoder.EncodeAsync(
                    new UadpNetworkMessage(), context, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void EncodeAsync_BadUadpVersion_Throws()
        {
            var encoder = new UadpEncoder();
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var bad = new UadpNetworkMessage { UadpVersion = 2 };
            Assert.That(
                async () => await encoder.EncodeAsync(bad, context).ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Encoder_ExposesProfileAndOverhead()
        {
            var encoder = new UadpEncoder();
            Assert.That(encoder.TransportProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
            Assert.That(encoder.EstimatedHeaderOverhead, Is.GreaterThan(0));
        }

        [Test]
        public async Task ConfiguredSize_PadsPayloadToTarget()
        {
            var dataSetMessage = new UadpDataSetMessage
            {
                DataSetWriterId = 1,
                FieldEncoding = PubSubFieldEncoding.Variant,
                ConfiguredSize = 128,
                Fields = [ new DataSetField { Value = new Variant(1) } ]
            };

            // Padding only changes encoded length; sanity check via raw encode.
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId,
                PublisherId = PublisherId.FromByte(0),
                DataSetMessages = [ dataSetMessage ]
            };
            var encoder = new UadpEncoder();
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);
            Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(128));
        }

        private static async Task<UadpNetworkMessage> RoundTripAsync(UadpNetworkMessage msg)
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);

            var decoder = new UadpDecoder();
            PubSubNetworkMessage? decoded =
                await decoder.TryDecodeAsync(bytes, context).ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded, Is.InstanceOf<UadpNetworkMessage>());
            return (UadpNetworkMessage)decoded!;
        }

        private static async Task<UadpDataSetMessage> SingleMessageRoundTripAsync(
            UadpDataSetMessage ds)
        {
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages = [ ds ]
            };
            UadpNetworkMessage decoded = await RoundTripAsync(msg).ConfigureAwait(false);
            return (UadpDataSetMessage)decoded.DataSetMessages[0];
        }

        private sealed record ForeignNetworkMessage : PubSubNetworkMessage
        {
            public override string TransportProfileUri => "other://transport";
        }
    }
}
