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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Coverage for UADP edge-case detection at the encode/decode level —
    /// out-of-order sequence numbers, delta-before-keyframe, MajorVersion
    /// mismatch reporting.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5")]
    public class UadpEdgeCasesTests
    {
        [Test]
        public async Task OutOfOrderSequenceNumbers_DecoderReportsRawOrder()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();

            ushort[] order = [5, 3, 4];
            var decoded = new UadpNetworkMessage?[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                var msg = new UadpNetworkMessage
                {
                    ContentMask = UadpNetworkMessageContentMask.PublisherId |
                        UadpNetworkMessageContentMask.GroupHeader |
                        UadpNetworkMessageContentMask.WriterGroupId |
                        UadpNetworkMessageContentMask.SequenceNumber,
                    PublisherId = PublisherId.FromByte(1),
                    WriterGroupId = 100,
                    SequenceNumber = order[i],
                    DataSetMessages =
                    [
                        new UadpDataSetMessage
                        {
                            DataSetWriterId = 10,
                            FieldEncoding = PubSubFieldEncoding.Variant,
                            Fields = [new DataSetField { Value = (Variant)42 }]
                        }
                    ]
                };
                ReadOnlyMemory<byte> bytes =
                    await encoder.EncodeAsync(msg, context).ConfigureAwait(false);
                decoded[i] = (UadpNetworkMessage?)UadpDecoder.Decode(bytes, context);
            }

            Assert.That(decoded[0]!.SequenceNumber, Is.EqualTo((ushort)5));
            Assert.That(decoded[1]!.SequenceNumber, Is.EqualTo((ushort)3));
            Assert.That(decoded[2]!.SequenceNumber, Is.EqualTo((ushort)4));
            // The decoder makes the raw order observable to a higher
            // layer that can then flag the regression.
            Assert.That(decoded[1]!.SequenceNumber < decoded[0]!.SequenceNumber,
                Is.True, "Out-of-order sequence is observable post-decode.");
        }

        [Test]
        public async Task DeltaFrameMessageType_RoundTrips_AsDelta()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 11,
                        MessageType = PubSubDataSetMessageType.DeltaFrame,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)1 }]
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);
            var decoded = (UadpNetworkMessage?)UadpDecoder.Decode(bytes, context);
            Assert.That(decoded, Is.Not.Null);
            var dsm = (UadpDataSetMessage)decoded!.DataSetMessages[0];
            Assert.That(dsm.MessageType, Is.EqualTo(PubSubDataSetMessageType.DeltaFrame));
        }

        [Test]
        public async Task EventMessageType_RoundTrips_AsEvent()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(2),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 22,
                        MessageType = PubSubDataSetMessageType.Event,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)"event" }]
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);
            var decoded = (UadpNetworkMessage?)UadpDecoder.Decode(bytes, context);
            Assert.That(decoded, Is.Not.Null);
            var dsm = (UadpDataSetMessage)decoded!.DataSetMessages[0];
            Assert.That(dsm.MessageType, Is.EqualTo(PubSubDataSetMessageType.Event));
        }

        [Test]
        public async Task MajorVersionMismatch_IncrementsResolverErrors()
        {
            // Register meta for major version 1 then decode a frame
            // that announces major version 2 with RawData encoding.
            var registry = new DataSetMetaDataRegistry();
            var registeredMeta = new DataSetMetaDataType
            {
                Name = "MV1",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "f0",
                        BuiltInType = (byte)BuiltInType.UInt32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
            var diag = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            PubSubNetworkMessageContext encodeCtx =
                UadpTestUtilities.NewContext(registry, diag);

            registry.Register(
                new DataSetMetaDataKey(
                    PublisherId.FromByte(1), 7, 100,
                    (Uuid)Guid.Empty, 1),
                registeredMeta);

            // Encode with version 1 (matches registered metadata, RawData OK).
            var encoder = new UadpEncoder();
            var matchingMsg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = 1, MinorVersion = 0
                        },
                        FieldEncoding = PubSubFieldEncoding.RawData,
                        Fields = [new DataSetField { Value = (Variant)123u }]
                    }
                ]
            };
            ReadOnlyMemory<byte> matchingBytes =
                await encoder.EncodeAsync(matchingMsg, encodeCtx).ConfigureAwait(false);

            // Decode (matching) → counter NOT incremented.
            long resolverBefore =
                diag.Read(PubSubDiagnosticsCounterKind.ResolverErrors);
            PubSubNetworkMessage? matchDecoded =
                UadpDecoder.Decode(matchingBytes, encodeCtx);
            Assert.That(matchDecoded, Is.Not.Null);
            long resolverAfterMatch =
                diag.Read(PubSubDiagnosticsCounterKind.ResolverErrors);
            Assert.That(resolverAfterMatch, Is.EqualTo(resolverBefore));

            // Now construct a frame whose announced MajorVersion = 2
            // (no registered metadata for that version).
            var mismatchMsg = matchingMsg with
            {
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = 2, MinorVersion = 0
                        },
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)123u }]
                    }
                ]
            };
            ReadOnlyMemory<byte> mismatchBytes =
                await encoder.EncodeAsync(mismatchMsg, encodeCtx).ConfigureAwait(false);

            PubSubNetworkMessage? mismatchDecoded =
                UadpDecoder.Decode(mismatchBytes, encodeCtx);
            // Decode still succeeds for Variant encoding, but the resolver
            // increment fires whenever we walked the registry and found a
            // major-version mismatch.
            Assert.That(mismatchDecoded, Is.Not.Null);
            long resolverAfterMismatch =
                diag.Read(PubSubDiagnosticsCounterKind.ResolverErrors);
            Assert.That(resolverAfterMismatch,
                Is.GreaterThan(resolverAfterMatch),
                "ResolverErrors should increment when MajorVersion does not " +
                "match the registered metadata.");
        }

        [Test]
        public async Task ReceivedNetworkMessages_CounterIncrements()
        {
            var diag = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(diagnostics: diag);
            var encoder = new UadpEncoder();

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
                        Fields = [new DataSetField { Value = (Variant)1 }]
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);

            long sentBefore = diag.Read(
                PubSubDiagnosticsCounterKind.SentNetworkMessages);
            long recvBefore = diag.Read(
                PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);

            await encoder.EncodeAsync(msg, context).ConfigureAwait(false);
            _ = UadpDecoder.Decode(bytes, context);

            Assert.That(
                diag.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.GreaterThan(sentBefore));
            Assert.That(
                diag.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages),
                Is.GreaterThan(recvBefore));
        }

        [Test]
        public void ReceivedInvalidNetworkMessages_CounterIncrements_OnMalformed()
        {
            var diag = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(diagnostics: diag);

            long before = diag.Read(
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);

            // Invalid version (high nibble carries no flags, low nibble = 7).
            _ = UadpDecoder.Decode(new byte[] { 0x07 }, context);
            // Truncated header (header expects more bytes than provided).
            _ = UadpDecoder.Decode(new byte[] { 0xF1 }, context);

            long after = diag.Read(
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
            Assert.That(after, Is.EqualTo(before + 2));
        }

        [Test]
        public void EmptyFrame_NoCounterIncrement()
        {
            var diag = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(diagnostics: diag);

            long invalidBefore = diag.Read(
                PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
            long recvBefore = diag.Read(
                PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);

            _ = UadpDecoder.Decode(ReadOnlyMemory<byte>.Empty, context);

            Assert.That(
                diag.Read(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages),
                Is.EqualTo(invalidBefore));
            Assert.That(
                diag.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages),
                Is.EqualTo(recvBefore));
        }

        [Test]
        public async Task SentDataSetMessages_CounterTracksCount()
        {
            var diag = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(diagnostics: diag);

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)1 }]
                    },
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 2,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = (Variant)2 }]
                    }
                ]
            };
            long before = diag.Read(
                PubSubDiagnosticsCounterKind.SentDataSetMessages);
            await new UadpEncoder()
                .EncodeAsync(msg, context).ConfigureAwait(false);
            long after = diag.Read(
                PubSubDiagnosticsCounterKind.SentDataSetMessages);
            Assert.That(after - before, Is.EqualTo(2));
        }

        [Test]
        public async Task KeepAliveMessage_HasNoFields_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId,
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        MessageType = PubSubDataSetMessageType.KeepAlive,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = []
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await encoder.EncodeAsync(msg, context).ConfigureAwait(false);
            var decoded = (UadpNetworkMessage?)UadpDecoder.Decode(bytes, context);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(((PubSubDataSetMessage[]?)decoded!.DataSetMessages) ?? [], Has.Length.EqualTo(1));
            var dsm = (UadpDataSetMessage)decoded.DataSetMessages[0];
            Assert.That(dsm.MessageType,
                Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
            Assert.That(((DataSetField[]?)dsm.Fields) ?? [], Is.Empty);
        }
    }
}
