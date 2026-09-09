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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.Tests;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonDecoderV2 = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonEncoderV2 = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpDecoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder;
using UadpEncoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Groups;

[TestFixture]
public sealed class WriterGroupMessageSettingsTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task ConfiguredMessagesSurviveEncodingAndReachTheirFilteredReaderAsync(bool json, bool raw)
    {
        ITelemetryContext telemetry = NUnitTelemetryContext.Create();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var source = new CounterSource();
        var published = new PublishedDataSet(new PublishedDataSetDataType
        {
            Name = "counter",
            DataSetMetaData = source.BuildMetaData()
        }, source);
        var writer = new DataSetWriter(new DataSetWriterDataType
        {
            Name = "writer",
            DataSetWriterId = 42,
            DataSetName = "counter",
            KeyFrameCount = 1,
            DataSetFieldContentMask = (uint)(raw
                ? DataSetFieldContentMask.RawData
                : DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp),
            MessageSettings = json
                ? new ExtensionObject(new JsonDataSetWriterMessageDataType
                {
                    DataSetMessageContentMask = (uint)kJsonDataSetMask
                })
                : new ExtensionObject(new UadpDataSetWriterMessageDataType
                {
                    DataSetMessageContentMask = (uint)kUadpDataSetMask
                })
        }, published, telemetry);
        var group = new WriterGroup(
            new WriterGroupDataType
            {
                Name = "group",
                WriterGroupId = 7,
                PublishingInterval = 100,
                MessageSettings = json
                    ? new ExtensionObject(new JsonWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)kJsonNetworkMask
                    })
                    : new ExtensionObject(new UadpWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)kUadpNetworkMask,
                        GroupVersion = 3
                    })
            },
            [writer],
            new PubSubSchedule(TimeSpan.FromMilliseconds(100), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
            Mock.Of<IPubSubScheduler>(),
            telemetry,
            clock)
        {
            EncodingProfileOverride = json ? Profiles.PubSubMqttJsonTransport : Profiles.PubSubUdpUadpTransport
        };
        await using (group.ConfigureAwait(false))
        {
            PublisherId publisher = json ? PublisherId.FromByte(2) : PublisherId.FromUInt16(2);
            group.PubSubAddressing.PublisherId = publisher;
            var captured = new List<PubSubNetworkMessage>();
            group.PublishSink = (message, _) =>
            {
                captured.Add(message);
                return default;
            };
            group.State.TryEnable();
            group.State.TryMarkOperational();
            writer.State.TryEnable();
            writer.State.TryMarkOperational();

            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            PubSubNetworkMessage sent = captured[0];
            if (json)
            {
                Assert.That(((JsonNetworkMessageV2)sent).ContentMask, Is.EqualTo(kJsonNetworkMask));
                Assert.That(((JsonDataSetMessageV2)sent.DataSetMessages[0]).ContentMask,
                    Is.EqualTo(kJsonDataSetMask));
                Assert.That(sent.DataSetMessages[0].Fields[0].Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
            }
            else
            {
                Assert.That(((UadpNetworkMessageV2)sent).ContentMask, Is.EqualTo(kUadpNetworkMask));
                var dataSet = (UadpDataSetMessageV2)sent.DataSetMessages[0];
                Assert.That(dataSet.ContentMask, Is.EqualTo(kUadpDataSetMask));
                Assert.That(dataSet.FieldEncoding, Is.EqualTo(
                    raw ? PubSubFieldEncoding.RawData : PubSubFieldEncoding.DataValue));
            }
            var registry = new DataSetMetaDataRegistry();
            registry.Register(new DataSetMetaDataKey(
                publisher, json ? (ushort)0 : (ushort)7, 42, Uuid.Empty, 3), source.BuildMetaData());
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.Create(telemetry),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High, clock),
                clock);
            INetworkMessageEncoder encoder = json ? new JsonEncoderV2() : new UadpEncoderV2();
            INetworkMessageDecoder decoder = json ? new JsonDecoderV2() : new UadpDecoderV2();
            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(sent, context).ConfigureAwait(false);
            PubSubNetworkMessage? received = await decoder.TryDecodeAsync(frame, context).ConfigureAwait(false);

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.DataSetMessages.Count, Is.EqualTo(1));
            var sink = new CapturingSink();
            var reader = new DataSetReader(new DataSetReaderDataType
            {
                Name = "reader",
                PublisherId = publisher.ToVariant(),
                WriterGroupId = 7,
                DataSetWriterId = 42,
                DataSetMetaData = source.BuildMetaData()
            }, sink, telemetry, clock);
            reader.State.TryEnable();
            Assert.That(reader.Matches(received, received.DataSetMessages[0]), Is.True,
                "Configured identities must survive encoding; received fields alone do not prove application.");
            await reader.DispatchAsync(received.DataSetMessages[0]).ConfigureAwait(false);

            Assert.That(sink.Values.Count, Is.EqualTo(1));
            Assert.That(sink.Values[0].Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(received.DataSetMessages[0].SequenceNumber, Is.EqualTo(1));
            Assert.That(received.DataSetMessages[0].MetaDataVersion.MajorVersion, Is.EqualTo(3));
            if (!raw)
            {
                Assert.That(sink.Values[0].StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue),
                    json ? System.Text.Encoding.UTF8.GetString(frame.ToArray()) : "UADP must preserve field quality.");
                Assert.That(sink.Values[0].SourceTimestamp, Is.EqualTo(s_sourceTime));
            }
        }
    }

    [TestCase(true, "2", true)]
    [TestCase(false, "2", false)]
    [TestCase(true, "02", false)]
    [TestCase(true, "3", false)]
    [TestCase(true, "", false)]
    public void NumericPublisherFiltersHonorOnlyTheCanonicalJsonWireIdentity(
        bool json, string wireIdentity, bool expected)
    {
        var reader = new DataSetReader(
            new DataSetReaderDataType { PublisherId = Variant.From((ushort)2), DataSetWriterId = 42 },
            new CapturingSink(),
            NUnitTelemetryContext.Create(),
            TimeProvider.System);
        PubSubNetworkMessage network = json
            ? new JsonNetworkMessageV2 { PublisherId = PublisherId.FromString(wireIdentity) }
            : new UadpNetworkMessageV2 { PublisherId = PublisherId.FromString(wireIdentity) };

        Assert.That(reader.Matches(network, new UadpDataSetMessageV2 { DataSetWriterId = 42 }), Is.EqualTo(expected));
    }

    private sealed class CounterSource : IPublishedDataSetSource
    {
        public DataSetMetaDataType BuildMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "counter",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 3, MinorVersion = 1 },
                Fields = [new FieldMetaData
                {
                    Name = "Counter",
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                }]
            };
        }

        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<PublishedDataSetSnapshot>(new PublishedDataSetSnapshot(
                metaData.ConfigurationVersion,
                [new DataSetField
                {
                    Name = "Counter",
                    Value = Variant.From(42),
                    StatusCode = StatusCodes.UncertainLastUsableValue,
                    SourceTimestamp = s_sourceTime
                }],
                s_sourceTime));
        }
    }

    private sealed class CapturingSink : ISubscribedDataSetSink
    {
        public ArrayOf<DataSetField> Values { get; private set; }

        public ValueTask WriteAsync(IReadOnlyList<DataSetField> fields, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values = [.. fields];
            return default;
        }
    }

    private const UadpNetworkMessageContentMask kUadpNetworkMask =
        UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.GroupHeader |
        UadpNetworkMessageContentMask.WriterGroupId | UadpNetworkMessageContentMask.PayloadHeader |
        UadpNetworkMessageContentMask.GroupVersion | UadpNetworkMessageContentMask.SequenceNumber;
    private const UadpDataSetMessageContentMask kUadpDataSetMask =
        UadpDataSetMessageContentMask.SequenceNumber | UadpDataSetMessageContentMask.Timestamp |
        UadpDataSetMessageContentMask.MajorVersion | UadpDataSetMessageContentMask.MinorVersion;
    private const JsonNetworkMessageContentMask kJsonNetworkMask =
        JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader |
        JsonNetworkMessageContentMask.PublisherId;
    private const JsonDataSetMessageContentMask kJsonDataSetMask =
        JsonDataSetMessageContentMask.DataSetWriterId | JsonDataSetMessageContentMask.SequenceNumber |
        JsonDataSetMessageContentMask.MetaDataVersion;
    private static readonly DateTimeUtc s_sourceTime =
        DateTimeUtc.From(new DateTimeOffset(2026, 9, 8, 11, 59, 59, TimeSpan.Zero));
}
