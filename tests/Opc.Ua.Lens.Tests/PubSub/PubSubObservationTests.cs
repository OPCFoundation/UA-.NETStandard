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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Transcoding;
using UaLens.Plugins.PubSub;
using JsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using JsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;

namespace UaLens.Tests.PubSub;

[TestFixture]
public sealed class PubSubObservationTests
{
    [Test]
    public async Task LocalSinkRetainsValuesQualityAndTimestampsWithoutAnyUaSession()
    {
        var store = new PubSubObservationStore(2);
        DataSetField field = PubSubTestRuntime.Field(42) with { StatusCode = StatusCodes.UncertainLastUsableValue };
        await store.WriteAsync([field]).ConfigureAwait(false);
        PubSubObservationSnapshot snapshot = store.Snapshot();

        Assert.That(snapshot.AcceptedDataSets, Is.EqualTo(1));
        Assert.That(snapshot.Values, Has.Count.EqualTo(1));
        Assert.That(snapshot.Values[0].Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(42));
        Assert.That(snapshot.Values[0].Status.Code, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
        Assert.That(snapshot.Values[0].SourceTimestamp, Is.EqualTo(field.SourceTimestamp));
        Assert.That(snapshot.Messages.Count, Is.Zero, "A sink callback does not fabricate a wire envelope.");
    }

    [Test]
    public async Task InvalidLaterFieldCannotPartiallyUpdateLocalState()
    {
        var store = new PubSubObservationStore(2);
        await store.WriteAsync([PubSubTestRuntime.Field(7)]).ConfigureAwait(false);
        await Assert.ThatAsync(async () =>
            await store.WriteAsync([PubSubTestRuntime.Field(8), null!]).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        PubSubObservationSnapshot snapshot = store.Snapshot();
        Assert.That(snapshot.Values[0].Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(7));
        Assert.That(snapshot.AcceptedDataSets, Is.EqualTo(1));
        Assert.That(snapshot.RejectedDataSets, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellationDuringPreparationCannotCommitAnyField()
    {
        var store = new PubSubObservationStore(2);
        await store.WriteAsync([PubSubTestRuntime.Field(7)]).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var fields = new CancelingFields(cancellation);

        await Assert.ThatAsync(async () => await store.WriteAsync(fields, cancellation.Token).ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(store.Snapshot().AcceptedDataSets, Is.EqualTo(1));
        Assert.That(store.Snapshot().Values[0].Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(7));
    }

    [Test]
    public async Task DeltaAndKeepAlivePreserveTheCompleteLocalSnapshot()
    {
        var store = new PubSubObservationStore(2);
        await store.WriteAsync([PubSubTestRuntime.Field(1, "First"), PubSubTestRuntime.Field(2, "Second")])
            .ConfigureAwait(false);
        await store.WriteAsync([PubSubTestRuntime.Field(9, "Second", 1)]).ConfigureAwait(false);
        await store.WriteAsync([]).ConfigureAwait(false);

        PubSubObservationSnapshot snapshot = store.Snapshot();
        Assert.That(snapshot.Values, Has.Count.EqualTo(2));
        Assert.That(snapshot.Values[0].Value.TryGetValue(out int first), Is.True);
        Assert.That(snapshot.Values[1].Value.TryGetValue(out int second), Is.True);
        Assert.That(first, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(9));
        Assert.That(snapshot.AcceptedDataSets, Is.EqualTo(3));
    }

    [Test]
    public async Task DeltaWithoutAKeyFrameIsRejectedRatherThanInventingMissingValues()
    {
        var store = new PubSubObservationStore(2);
        await Assert.ThatAsync(async () => await store.WriteAsync([PubSubTestRuntime.Field(9, "Second", 1)])
            .ConfigureAwait(false), Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
        Assert.That(store.Snapshot().Values.Count, Is.Zero);
        Assert.That(store.Snapshot().AcceptedDataSets, Is.Zero);
    }

    [Test]
    public async Task ConcurrentSnapshotsNeverExposeHalfAReceivedVector()
    {
        var store = new PubSubObservationStore(4);
        await Task.WhenAll(Enumerable.Range(1, 32).Select(sequence => Task.Run(async () =>
        {
            await store.WriteAsync([PubSubTestRuntime.Field(sequence, "A"), PubSubTestRuntime.Field(sequence, "B")])
                .ConfigureAwait(false);
            PubSubObservationSnapshot snapshot = store.Snapshot();
            Assert.That(snapshot.Values[0].Value.TryGetValue(out int first), Is.True);
            Assert.That(snapshot.Values[1].Value.TryGetValue(out int second), Is.True);
            Assert.That(first, Is.EqualTo(second));
        }))).ConfigureAwait(false);
        Assert.That(store.Snapshot().AcceptedDataSets, Is.EqualTo(32));
    }

    [Test]
    public async Task WireRetentionAndSequenceEvidenceStayBoundedAndSeparateFromLocalAcceptance()
    {
        var store = new PubSubObservationStore(2);
        for (uint sequence = 1; sequence <= 4; sequence++)
        {
            await store.OnReceivedAsync(new ReceivedNetworkMessage
            {
                Message = CreateUadpMessage(sequence),
                FrameSecured = true
            }).ConfigureAwait(false);
        }
        PubSubObservationSnapshot snapshot = store.Snapshot();
        Assert.That(snapshot.Messages, Has.Count.EqualTo(2));
        Assert.That(snapshot.Messages[0].Sequence, Is.EqualTo(4));
        Assert.That(snapshot.Messages[0].Writer, Is.EqualTo(1));
        Assert.That(snapshot.Messages[0].WriterGroup, Is.EqualTo(100));
        Assert.That(snapshot.Messages[0].Security, Does.Contain("verified"));
        Assert.That(snapshot.EvictedMessages, Is.EqualTo(2));
        Assert.That(snapshot.AcceptedDataSets, Is.Zero);
        Assert.That(snapshot.Values.Count, Is.Zero);
    }

    [Test]
    public async Task MissingWireSequenceRemainsUnknown()
    {
        var store = new PubSubObservationStore(2);
        UadpNetworkMessage message = CreateUadpMessage(0) with
        {
            DataSetMessages = [new UadpDataSetMessage { DataSetWriterId = 1 }]
        };
        await store.OnReceivedAsync(new ReceivedNetworkMessage { Message = message }).ConfigureAwait(false);
        Assert.That(store.Snapshot().Messages[0].Sequence, Is.Null);
        Assert.That(store.Snapshot().Messages[0].MetadataVersion, Is.EqualTo("not carried"));
    }

    [Test]
    public void MetadataUpdatesReplaceVersionsAndEvictRegistryEntriesBeyondTheBudget()
    {
        var store = new PubSubObservationStore(2);
        var registry = new DataSetMetaDataRegistry();
        registry.MetaDataChanged += (_, change) => store.ObserveMetadata(registry, change);
        for (ushort publisher = 1; publisher <= 40; publisher++)
        {
            var metadata = new DataSetMetaDataType
            {
                Name = "Sample",
                Fields = [new FieldMetaData { Name = "Value", BuiltInType = (byte)BuiltInType.Int32 }],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1 }
            };
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(publisher), 100, 1, Uuid.Empty, 1), metadata);
        }
        Assert.That(registry.Keys, Has.Count.EqualTo(32));
        Assert.That(store.Snapshot().Metadata, Has.Count.EqualTo(32));
        Assert.That(store.Snapshot().MetadataEvictions, Is.EqualTo(8));

        var changed = new DataSetMetaDataType
        {
            Name = "Changed",
            Fields = [new FieldMetaData { Name = "Renamed", BuiltInType = (byte)BuiltInType.Double }],
            ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 2, MinorVersion = 7 }
        };
        registry.Register(new DataSetMetaDataKey(PublisherId.FromUInt16(40), 100, 1, Uuid.Empty, 2), changed);
        PubSubMetadataRow row = store.Snapshot().Metadata.ToList().Single(item => item.Name == "Changed");
        Assert.That(row.MajorVersion, Is.EqualTo(2));
        Assert.That(row.MinorVersion, Is.EqualTo(7));
        Assert.That(row.Fields, Does.Contain("Renamed"));
        Assert.That(store.Snapshot().MetadataUpdates, Is.EqualTo(41));
    }

    [Test]
    public async Task OversizedValuesAndEvidenceAreBoundedWithoutRetainingTheOriginalPayload()
    {
        var store = new PubSubObservationStore(2);
        await store.WriteAsync([new DataSetField { Name = "Text", Value = new Variant(new string('x', 100000)) }])
            .ConfigureAwait(false);
        for (int i = 0; i < 200; i++)
        {
            store.RecordEvidence("Test", StatusCodes.BadDecodingError, new string('e', 2000));
        }
        PubSubObservationSnapshot snapshot = store.Snapshot();
        Assert.That(snapshot.Values[0].Truncated, Is.True);
        Assert.That(snapshot.Values[0].Text, Has.Length.LessThanOrEqualTo(513));
        Assert.That(snapshot.TruncatedValues, Is.EqualTo(1));
        Assert.That(snapshot.Evidence, Has.Count.EqualTo(128));
        Assert.That(snapshot.Evidence.Contains(entry => entry.Detail.Length > 513), Is.False);
    }

    [Test]
    public async Task RealDecoderRejectsMalformedFramesWithVisibleEvidenceAndCounters()
    {
        var store = new PubSubObservationStore(2);
        var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
        PubSubNetworkMessageContext context = CreateContext(diagnostics);
        var decoder = new PubSubObservedDecoder(new UadpDecoder(), store, 1500);

        PubSubNetworkMessage? message = await decoder.TryDecodeAsync(new byte[] { 0, 0, 0 }, context)
            .ConfigureAwait(false);
        Assert.That(message, Is.Null);
        Assert.That(diagnostics.Read(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages), Is.GreaterThan(0));
        Assert.That(store.Snapshot().Evidence.Contains(entry => entry.Status.Code == StatusCodes.BadDecodingError),
            Is.True);
        Assert.That(store.Snapshot().Values.Count, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExistingUadpAndJsonCodecsProduceUsefulObservedDataWithoutNetworkTraffic(bool json)
    {
        var store = new PubSubObservationStore(2);
        var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
        PubSubNetworkMessageContext context = CreateContext(diagnostics, json);
        INetworkMessageEncoder encoder = json ? new JsonEncoder() : new UadpEncoder();
        INetworkMessageDecoder decoder = json ? new JsonDecoder() : new UadpDecoder();
        PubSubNetworkMessage source = json
            ? new JsonNetworkMessage
            {
                PublisherId = PublisherId.FromUInt16(1),
                WriterGroupId = 100,
                DataSetMessages =
                [
                    new JsonDataSetMessage
                    {
                        DataSetWriterId = 1,
                        SequenceNumber = 3,
                        ContentMask = JsonDataSetMessageContentMask.DataSetWriterId |
                            JsonDataSetMessageContentMask.SequenceNumber |
                            JsonDataSetMessageContentMask.MetaDataVersion,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                        Fields = [PubSubTestRuntime.Field(42)]
                    }
                ]
            }
            : CreateUadpMessage(3);
        ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(source, context).ConfigureAwait(false);
        var observed = new PubSubObservedDecoder(decoder, store, 1500);
        PubSubNetworkMessage? decoded = await observed.TryDecodeAsync(frame, context).ConfigureAwait(false);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.DataSetMessages, Has.Count.EqualTo(1));
        PubSubConfiguration configuration = json
            ? PubSubTestRuntime.Configuration with
            {
                Profile = PubSubProfile.MqttJson,
                Endpoint = "mqtts://broker.example:8883",
                Topic = "sample",
                BrokerAuthentication = PubSubBrokerAuthentication.Anonymous
            }
            : PubSubTestRuntime.Configuration;
        DataSetReaderDataType readerConfiguration = PubSubStackConfiguration.Build(configuration)
            .Connections[0].ReaderGroups[0].DataSetReaders[0];
        var reader = new DataSetReader(
            readerConfiguration, store, DefaultTelemetry.Create(static _ => { }), TimeProvider.System);
        Assert.That(reader.Matches(decoded, decoded.DataSetMessages[0]), Is.True);
        await store.WriteAsync(decoded.DataSetMessages[0].Fields.ToList()).ConfigureAwait(false);
        Assert.That(store.Snapshot().Values[0].Value.TryGetValue(out int value), Is.True);
        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public async Task DiscoveryBudgetRejectsFurtherResponsesBeforeTheApplicationCanAccumulateThem()
    {
        var store = new PubSubObservationStore(2);
        var inner = new Mock<INetworkMessageDecoder>();
        inner.SetupGet(decoder => decoder.TransportProfileUri).Returns(Profiles.PubSubUdpUadpTransport);
        inner.Setup(decoder => decoder.TryDecodeAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<PubSubNetworkMessageContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<PubSubNetworkMessage?>(new UadpDiscoveryResponseMessage
            {
                DiscoveryType = UadpDiscoveryType.DataSetMetaData
            }));
        var decoder = new PubSubObservedDecoder(inner.Object, store, 1500);
        PubSubNetworkMessageContext context = CreateContext(new PubSubDiagnostics(PubSubDiagnosticsLevel.High));
        store.BeginDiscovery();
        int accepted = 0;
        for (int i = 0; i < 66; i++)
        {
            if (await decoder.TryDecodeAsync(ReadOnlyMemory<byte>.Empty, context).ConfigureAwait(false) is not null)
            {
                accepted++;
            }
        }
        Assert.That(accepted, Is.EqualTo(64));
        Assert.That(store.Snapshot().DiscoveryDrops, Is.EqualTo(2));
    }

    private static PubSubNetworkMessageContext CreateContext(IPubSubDiagnostics diagnostics, bool json = false)
    {
        var registry = new DataSetMetaDataRegistry();
        PublisherId publisher = json ? PublisherId.From(new Variant((byte)1)) : PublisherId.FromUInt16(1);
        registry.Register(new DataSetMetaDataKey(publisher, json ? (ushort)0 : (ushort)100, 1, Uuid.Empty, 1),
            new DataSetMetaDataType
            {
                Name = "Sample",
                Fields = [new FieldMetaData
                {
                    Name = "Value",
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                }],
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1 }
            });
        return new PubSubNetworkMessageContext(
            ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { })),
            registry, diagnostics, TimeProvider.System);
    }

    private static UadpNetworkMessage CreateUadpMessage(uint sequence)
    {
        return new UadpNetworkMessage
        {
            PublisherId = PublisherId.FromUInt16(1),
            WriterGroupId = 100,
            ContentMask = UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.GroupHeader | UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.PayloadHeader,
            DataSetMessages =
            [
                new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    SequenceNumber = sequence,
                    ContentMask = UadpDataSetMessageContentMask.SequenceNumber |
                        UadpDataSetMessageContentMask.MajorVersion,
                    MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                    Fields = [PubSubTestRuntime.Field(42)]
                }
            ]
        };
    }

    private sealed class CancelingFields(CancellationTokenSource cancellation) : IReadOnlyList<DataSetField>
    {
        public int Count => 2;

        public DataSetField this[int index]
        {
            get
            {
                if (index == 1)
                {
                    cancellation.Cancel();
                }
                return PubSubTestRuntime.Field(9);
            }
        }

        public IEnumerator<DataSetField> GetEnumerator()
        {
            return Enumerable.Range(0, Count).Select(index => this[index]).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
