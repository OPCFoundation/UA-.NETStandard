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

using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for history read operations.
    /// </summary>
    /// <remarks>
    /// The ReferenceServer historizes <c>Scalar_Static_Int32</c>,
    /// <c>Scalar_Static_Float</c> and <c>Scalar_Static_Double</c> via
    /// the fluent <c>HistorianBuilder</c> in
    /// <c>ReferenceNodeManager.EnableHistoryArchiving</c>. Each variable
    /// is seeded with 1001 samples by <c>SeedHistoricalNode</c>; these
    /// tests exercise the wire path against that seed data.
    /// </remarks>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class HistoryAotTests(AotTestFixture fixture)
    {
        private static readonly NodeId s_historizedNodeId
            = NodeId.Parse("ns=2;s=Scalar_Static_Double");

        [Test]
        public async Task HistoryReadRawAsync()
        {
            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.UtcNow.AddHours(-24),
                EndTime = DateTime.UtcNow,
                NumValuesPerNode = 100,
                IsReadModified = false,
                ReturnBounds = false
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = s_historizedNodeId
                }
            ];

            HistoryReadResponse response =
                await fixture.Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    false,
                    nodesToRead,
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count).IsEqualTo(nodesToRead.Count);

            HistoryReadResult result = response.Results[0];
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
            await Assert.That(result.HistoryData.IsNull).IsFalse();
            await Assert.That(
                result.HistoryData.TryGetValue(out HistoryData data))
                .IsTrue();
            await Assert.That(data!.DataValues.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task HistoryReadProcessedAsync()
        {
            var details = new ReadProcessedDetails
            {
                StartTime = DateTime.UtcNow.AddHours(-24),
                EndTime = DateTime.UtcNow,
                ProcessingInterval = 60_000, // 1 minute buckets
                AggregateType = [ObjectIds.AggregateFunction_Average]
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId
                {
                    NodeId = s_historizedNodeId
                }
            ];

            HistoryReadResponse response =
                await fixture.Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    false,
                    nodesToRead,
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(response.Results.Count).IsEqualTo(nodesToRead.Count);

            HistoryReadResult result = response.Results[0];
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
            await Assert.That(
                result.HistoryData.TryGetValue(out HistoryData data))
                .IsTrue();
            await Assert.That(data!.DataValues.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task SharedHistorianAndContinuationCodecsRoundTripAsync()
        {
            using var store = new StrongInMemoryStore();
            using var protector = new AesCbcHmacRecordProtector(CreateKey());
            var election = new StaticTestElection();
            var nodeId = new NodeId("AotSharedHistorian", 2);
            var structuredNodeId =
                new NodeId("AotSharedStructuredHistorian", 2);
            var options = new SharedKeyValueHistorianOptions
            {
                MaxValuesPerPage = 1,
                StructuredNodes =
                [
                    new SharedKeyValueStructuredHistorianNode
                    {
                        NodeId = structuredNodeId,
                        KeySelector = AotInt32KeySelector.Instance
                    }
                ]
            };
            await using var provider = new SharedKeyValueHistorianProvider(
                store,
                fixture.ServerFixture.Server.CurrentInstance.MessageContext,
                protector,
                election,
                options);
            DateTimeUtc start = DateTime.UtcNow.AddMinutes(-1);
            using var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryUpdate,
                RequestLifetime.None);
            var systemContext = new ServerSystemContext(
                fixture.ServerFixture.Server.CurrentInstance,
                operationContext);
            var historianContext = new HistorianOperationContext(
                systemContext,
                operationContext,
                null,
                HistoryUpdateType.Insert);

            HistorianUpdateOutcome<DataValue> inserted = await provider.InsertAsync(
                historianContext,
                nodeId,
                [
                    new DataValue(Variant.From(1), StatusCodes.Good, start, start),
                    new DataValue(
                        Variant.From(2),
                        StatusCodes.Good,
                        start.ToDateTime().AddSeconds(1),
                        start.ToDateTime().AddSeconds(1))
                ],
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> first = await provider.ReadRawAsync(
                historianContext,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = start,
                    EndTime = start.ToDateTime().AddMinutes(1),
                    MaxValues = 1,
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> second = await provider.ReadRawAsync(
                historianContext,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = start,
                    EndTime = start.ToDateTime().AddMinutes(1),
                    MaxValues = 1,
                    IsForward = true
                },
                first.NextToken,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(inserted.OperationResults.Count).IsEqualTo(2);
            await Assert.That(first.Values.Count).IsEqualTo(1);
            await Assert.That(first.NextToken.IsEmpty).IsFalse();
            await Assert.That(second.Values.Count).IsEqualTo(1);
            await Assert.That(second.NextToken.IsEmpty).IsTrue();

            HistorianUpdateOutcome<DataValue> replaced =
                await provider.ReplaceAsync(
                    historianContext,
                    nodeId,
                    [
                        new DataValue(
                            Variant.From(10),
                            StatusCodes.Good,
                            start,
                            start)
                    ],
                    CancellationToken.None).ConfigureAwait(false);
            HistorianPage<ModifiedDataValue> modified =
                await provider.ReadModifiedAsync(
                    historianContext,
                    new HistorianModifiedReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = start,
                        EndTime = start.ToDateTime().AddMinutes(1),
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);
            var annotation = new Annotation
            {
                Message = "AOT annotation",
                UserName = "aot",
                AnnotationTime = start.ToDateTime().AddSeconds(2)
            };
            await provider.InsertAnnotationsAsync(
                historianContext,
                nodeId,
                [annotation],
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<Annotation> annotations =
                await provider.ReadAnnotationsAsync(
                    historianContext,
                    new HistorianAnnotationReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = start,
                        EndTime = start.ToDateTime().AddMinutes(1),
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);
            var eventId = ByteString.From([7, 8, 9]);
            var messageKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName(BrowseNames.Message)],
                Attributes.Value,
                null);
            var eventRecord = new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                start.ToDateTime().AddSeconds(3),
                [
                    new KeyValuePair<string, Variant>(
                        BrowseNames.Message,
                        new Variant(new LocalizedText("AOT event")))
                ])
            {
                QualifiedFields =
                [
                    new KeyValuePair<HistorianEventFieldKey, Variant>(
                        messageKey,
                        new Variant(new LocalizedText("AOT event")))
                ]
            };
            await provider.InsertEventsAsync(
                historianContext,
                nodeId,
                [eventRecord],
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistorianEventRecord> events =
                await provider.ReadEventsAsync(
                    historianContext,
                    new HistorianEventReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = start,
                        EndTime = start.ToDateTime().AddMinutes(1),
                        IsForward = true,
                        Filter = new EventFilter()
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);
            await provider.InsertAsync(
                historianContext,
                structuredNodeId,
                [
                    new DataValue(
                        Variant.From(21),
                        StatusCodes.Good,
                        start,
                        start),
                    new DataValue(
                        Variant.From(22),
                        StatusCodes.Good,
                        start,
                        start)
                ],
                CancellationToken.None).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> structured =
                await provider.ReadRawAsync(
                    historianContext,
                    new HistorianRawReadRequest
                    {
                        NodeId = structuredNodeId,
                        StartTime = start,
                        EndTime = start.ToDateTime().AddMinutes(1),
                        MaxValues = 1,
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);

            await Assert.That(replaced.OldValues.Count).IsEqualTo(1);
            await Assert.That(modified.Values.Count).IsEqualTo(1);
            await Assert.That(annotations.Values.Count).IsEqualTo(1);
            await Assert.That(events.Values.Count).IsEqualTo(1);
            await Assert.That(events.Values[0].EventId).IsEqualTo(eventId);
            await Assert.That(structured.Values.Count).IsEqualTo(1);
            await Assert.That(structured.NextToken.IsEmpty).IsFalse();

            await using var continuationStore =
                new SharedKeyValueHistoryContinuationStore(
                    store,
                    fixture.ServerFixture.Server.CurrentInstance.MessageContext,
                    protector);
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = new NodeId(Guid.NewGuid(), 2),
                CodecId = "aot",
                CodecVersion = 1,
                Payload = ByteString.From([1, 2, 3])
            };
            await continuationStore.StoreAsync(envelope).ConfigureAwait(false);
            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await continuationStore.LoadAsync(
                    envelope.OwnerSessionId).ConfigureAwait(false);
            await Assert.That(loaded.Count).IsEqualTo(1);
            await Assert.That(
                await continuationStore.TryTakeAsync(
                    envelope.OwnerSessionId,
                    envelope.Id).ConfigureAwait(false)).IsTrue();
        }

        private static byte[] CreateKey()
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)i;
            }
            return key;
        }

        private sealed class AotInt32KeySelector :
            IHistorianStructuredDataKeySelector
        {
            public static AotInt32KeySelector Instance { get; } = new();

            public ArrayOf<QualifiedName> UniquenessFields { get; } =
                [new QualifiedName("Value")];

            public bool TryGetUniquenessKey(
                in DataValue value,
                out ByteString uniquenessKey)
            {
                if (!value.WrappedValue.TryGetValue(out int key))
                {
                    uniquenessKey = ByteString.Empty;
                    return false;
                }
                byte[] bytes = new byte[sizeof(int)];
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(bytes, key);
                uniquenessKey = ByteString.From(bytes);
                return true;
            }
        }

        private sealed class StaticTestElection : ILeaderElection
        {
            public bool IsLeader => true;

            public event Action<bool> LeadershipChanged
            {
                add { }
                remove { }
            }

            public ValueTask<bool> TryAcquireOrRenewAsync(
                CancellationToken ct = default)
            {
                return new ValueTask<bool>(true);
            }

            public void Start()
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }

        private sealed class StrongInMemoryStore :
            ISharedKeyValueStore,
            ISharedKeyValueStoreConsistency,
            IDisposable
        {
            public bool IsLinearizable(string key)
            {
                return true;
            }

            public bool IsProcessLocal(string key)
            {
                return false;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(
                string key,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.SetAsync(key, value, ct);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(
                    key,
                    expected,
                    value,
                    ct);
            }

            public ValueTask<bool> DeleteAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            public void Dispose()
            {
                m_inner.Dispose();
            }

            private readonly InMemorySharedKeyValueStore m_inner = new();
        }
    }
}
