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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    public sealed partial class SessionContinuationOwnershipTests
    {
        [Test]
        public void HistoryOwnershipSurvivesCheckoutResaveAndSharedPointRelease()
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            using var provider = new InMemoryHistorianProvider();
            using HistorianContinuationState first = CreateOwnedHistoryState(
                provider, source.Object, [dependency.Object]);
            using HistorianContinuationState second = CreateOwnedHistoryState(
                provider, unrelated.Object, [dependency.Object]);
            int released = 0;
            holder.HistoryContinuationPointsReleased += () => released++;
            holder.SaveHistory(first);
            holder.SaveHistory(second);
            ByteString oldToken = first.Id.ToByteArray().ToByteString();
            Assert.That(holder.RestoreHistory(oldToken), Is.SameAs(first));
            Assert.That(holder.RestoreHistory(oldToken), Is.Null);
            Assert.That(holder.HasHistoryForManager(source.Object), Is.True);
            Assert.That(holder.HasHistoryForManager(dependency.Object), Is.True);
            Assert.That(released, Is.Zero);
            first.Id = Guid.NewGuid();
            holder.SaveHistory(first);
            Assert.That(holder.RestoreHistory(oldToken), Is.Null);
            Assert.That(holder.RestoreHistory(first.Id.ToByteArray().ToByteString()), Is.SameAs(first));
            first.Dispose();
            first.Dispose();
            Assert.That(holder.HasHistoryForManager(source.Object), Is.False);
            Assert.That(holder.HasHistoryForManager(dependency.Object), Is.True);
            Assert.That(holder.HasHistoryForManager(unrelated.Object), Is.True);
            Assert.That(released, Is.EqualTo(1));
            second.Dispose();
            Assert.That(holder.HasHistoryForManager(dependency.Object), Is.False);
            Assert.That(holder.HasHistoryForManager(unrelated.Object), Is.False);
            Assert.That(released, Is.EqualTo(2));
        }

        [TestCase("Eviction")]
        [TestCase("Clear")]
        [TestCase("Dispose")]
        [TestCase("SaveFailure")]
        [TestCase("RestoreFailure")]
        [TestCase("CleanupFailure")]
        public void HistoryTerminalPathsReleaseEveryOwnerExactlyOnce(string terminal)
        {
            var store = new Mock<IContinuationPointStore>();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, store.Object);
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            var unrelated = new Mock<IAsyncNodeManager>();
            using var provider = new InMemoryHistorianProvider();
            using HistorianContinuationState point = CreateOwnedHistoryState(
                provider, source.Object, [dependency.Object]);
            using HistorianContinuationState replacement = CreateOwnedHistoryState(provider, unrelated.Object);
            int released = 0;
            holder.HistoryContinuationPointsReleased += () => released++;
            if (terminal == "SaveFailure")
            {
                store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                    .Throws<IOException>();
                Assert.That(() => holder.SaveHistory(point), Throws.TypeOf<IOException>());
            }
            else
            {
                holder.SaveHistory(point);
                Assert.That(holder.HasHistoryForManager(source.Object), Is.True);
                Assert.That(holder.HasHistoryForManager(dependency.Object), Is.True);
                switch (terminal)
                {
                    case "Eviction":
                        holder.SaveHistory(replacement);
                        Assert.That(holder.HasHistoryForManager(unrelated.Object), Is.True);
                        Assert.That(holder.RestoreHistory(point.Id.ToByteArray().ToByteString()), Is.Null);
                        break;
                    case "Clear":
                        holder.Clear();
                        break;
                    case "Dispose":
                        point.Dispose();
                        break;
                    case "RestoreFailure":
                        store.Setup(value => value.RemoveContinuationPoint(
                            It.IsAny<NodeId>(), ContinuationPointKind.History, point.Id)).Throws<IOException>();
                        Assert.That(() => holder.RestoreHistory(point.Id.ToByteArray().ToByteString()),
                            Throws.TypeOf<IOException>());
                        break;
                    case "CleanupFailure":
                        store.Setup(value => value.RemoveContinuationPoint(
                            It.IsAny<NodeId>(), ContinuationPointKind.History, point.Id)).Throws<IOException>();
                        Assert.That(holder.Clear, Throws.TypeOf<AggregateException>());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(terminal));
                }
            }
            point.Dispose();
            Assert.That(point.BufferedProcessedOutputs, Is.Null);
            Assert.That(holder.HasHistoryForManager(source.Object), Is.False);
            Assert.That(holder.HasHistoryForManager(dependency.Object), Is.False);
            Assert.That(released, Is.EqualTo(1));
            holder.Clear();
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void HistoryInvalidationLeavesCheckedOutOwnershipUntilRequestDisposes(bool clear, bool dependency)
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var source = new Mock<IAsyncNodeManager>();
            var target = new Mock<IAsyncNodeManager>();
            using var provider = new InMemoryHistorianProvider();
            using HistorianContinuationState point = CreateOwnedHistoryState(
                provider, source.Object, [target.Object]);
            using HistorianContinuationState available = CreateOwnedHistoryState(
                provider, source.Object, [target.Object]);
            holder.SaveHistory(point);
            holder.SaveHistory(available);
            Assert.That(holder.RestoreHistory(point.Id.ToByteArray().ToByteString()), Is.SameAs(point));
            if (clear)
            {
                holder.Clear();
            }
            else
            {
                holder.RemoveForManager(dependency ? target.Object : source.Object);
            }
            Assert.That(available.BufferedProcessedOutputs, Is.Null);
            Assert.That(point.BufferedProcessedOutputs, Is.Not.Null);
            Assert.That(holder.HasHistoryForManager(source.Object), Is.True);
            Assert.That(holder.HasHistoryForManager(target.Object), Is.True);
            if (clear)
            {
                Assert.That(() => holder.SaveHistory(point), Throws.TypeOf<ObjectDisposedException>());
            }
            else
            {
                Assert.That(() => holder.SaveHistory(point),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadContinuationPointInvalid));
            }
            point.Dispose();
            Assert.That(holder.HasHistoryForManager(source.Object), Is.False);
            Assert.That(holder.HasHistoryForManager(target.Object), Is.False);
        }

        [Test]
        public void HistoryCannotBeAdoptedByAnotherSessionOrSavedAfterDisposal()
        {
            var first = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
            var second = new SessionContinuationPoints(() => new NodeId(2), 1, 1, null);
            var source = new Mock<IAsyncNodeManager>();
            using var provider = new InMemoryHistorianProvider();
            using HistorianContinuationState point = CreateOwnedHistoryState(provider, source.Object);
            using HistorianContinuationState disposed = CreateOwnedHistoryState(provider, source.Object);
            first.SaveHistory(point);
            Assert.That(() => second.SaveHistory(point), Throws.TypeOf<InvalidOperationException>());
            Assert.That(first.HasHistoryForManager(source.Object), Is.True);
            Assert.That(second.HasHistoryForManager(source.Object), Is.False);
            disposed.Dispose();
            Assert.That(() => second.SaveHistory(disposed), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(second.HasHistoryForManager(source.Object), Is.False);
            first.Clear();
            second.Clear();
            Assert.That(first.HasHistoryForManager(source.Object), Is.False);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void DisabledHistoryCapacityRejectsWithoutAcquiringOwners(int capacity)
        {
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, capacity, null);
            var source = new Mock<IAsyncNodeManager>();
            using var provider = new InMemoryHistorianProvider();
            using HistorianContinuationState point = CreateOwnedHistoryState(provider, source.Object);
            Assert.That(() => holder.SaveHistory(point),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNoContinuationPoints));
            Assert.That(holder.HasHistoryForManager(source.Object), Is.False);
            Assert.That(point.BufferedProcessedOutputs, Is.Not.Null);
            holder.Clear();
        }

        [Test]
        public async Task ConcurrentHistorySaveAndDisposalCannotRetainOwnersAsync()
        {
            var source = new Mock<IAsyncNodeManager>();
            var dependency = new Mock<IAsyncNodeManager>();
            using var provider = new InMemoryHistorianProvider();
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 1, null);
                using HistorianContinuationState point = CreateOwnedHistoryState(
                    provider, source.Object, [dependency.Object]);
                var readyToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var readyToDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int released = 0;
                bool saved = false;
                holder.HistoryContinuationPointsReleased += () => Interlocked.Increment(ref released);
                Task saving = Task.Run(async () =>
                {
                    readyToSave.TrySetResult(true);
                    await start.Task.ConfigureAwait(false);
                    try
                    {
                        holder.SaveHistory(point);
                        saved = true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Disposal can win before the session acquires ownership.
                    }
                });
                Task disposing = Task.Run(async () =>
                {
                    readyToDispose.TrySetResult(true);
                    await start.Task.ConfigureAwait(false);
                    point.Dispose();
                });
                await Task.WhenAll(readyToSave.Task, readyToDispose.Task).ConfigureAwait(false);
                start.TrySetResult(true);
                await Task.WhenAll(saving, disposing).ConfigureAwait(false);
                Assert.That(holder.HasHistoryForManager(source.Object), Is.False);
                Assert.That(holder.HasHistoryForManager(dependency.Object), Is.False);
                Assert.That(Volatile.Read(ref released), Is.EqualTo(saved ? 1 : 0));
                holder.Clear();
            }
        }

        [Test]
        public async Task MirroredHistoryEnvelopeIsConsumedWithoutInventingOpaqueStateAsync()
        {
            NodeId ownerId = new(201);
            Guid id = Guid.NewGuid();
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.LoadContinuationPointsAsync(ownerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new ContinuationPointEnvelope
                    {
                        Id = id,
                        OwnerSessionId = ownerId,
                        Kind = ContinuationPointKind.History
                    }
                ]);
            var holder = new SessionContinuationPoints(() => new NodeId(202), 1, 1, store.Object);
            await holder.LoadMirroredAsync(ownerId).ConfigureAwait(false);
            Assert.That(holder.RestoreHistory(id.ToByteArray().ToByteString()), Is.Null);
            Assert.That(holder.RestoreHistory(id.ToByteArray().ToByteString()), Is.Null);
            store.Verify(
                value => value.RemoveContinuationPoint(ownerId, ContinuationPointKind.History, id), Times.Once);
            holder.Clear();
        }

        [Test]
        public async Task RestoredHistoryUsesOriginalProviderSourceQueryAndDataSelectionAsync()
        {
            using var original = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero });
            using var replacement = new InMemoryHistorianProvider();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            ServerSystemContext context = CreateHistorySystemContext(holder);
            using OperationContext operation = context.OperationContext;
            NodeId nodeId = new(8100, 1);
            var source = new BaseDataVariableState(null) { NodeId = nodeId, ValueRank = ValueRanks.OneDimension };
            original.Register(nodeId);
            var time = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
            ArrayOf<int> firstValues = [11, 12, 13];
            ArrayOf<int> secondValues = [21, 22, 23];
            await original.InsertAsync(
                new HistorianOperationContext(context, operation, source, HistoryUpdateType.Insert), nodeId,
                [
                    new DataValue(new Variant(firstValues), StatusCodes.Good, time, time.AddSeconds(10)),
                    new DataValue(new Variant(secondValues), StatusCodes.Good, time.AddSeconds(1),
                        time.AddSeconds(11))
                ], CancellationToken.None).ConfigureAwait(false);
            var firstNode = new HistoryReadValueId { NodeId = nodeId, IndexRange = "1:2" };
            Assert.That(ServiceResult.IsGood(HistoryReadValueId.Validate(firstNode)), Is.True);
            var legacy = new Mock<IHistorianProvider>();
            legacy.As<IHistorianDataProvider>().Setup(value => value.ReadRawAsync(
                It.IsAny<HistorianOperationContext>(), It.IsAny<HistorianRawReadRequest>(),
                It.IsAny<HistorianResumeToken>(), It.IsAny<CancellationToken>()))
                .Returns((HistorianOperationContext call, HistorianRawReadRequest request,
                    HistorianResumeToken token, CancellationToken ct) =>
                    original.ReadRawAsync(call, request, token, ct));
            Assert.That(legacy.Object, Is.Not.InstanceOf<IHistorianContinuationDependencies>());
            var first = new HistoryReadResult();
            ServiceResult firstError = await HistorianDispatcher.DispatchRawReadAsync(
                context, legacy.Object, source, firstNode,
                new ReadRawModifiedDetails { StartTime = time, EndTime = time.AddSeconds(2), NumValuesPerNode = 1 },
                TimestampsToReturn.Both, first, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
            var resumed = new HistoryReadResult();
            var incoming = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = first.ContinuationPoint,
                IndexRange = "0",
                DataEncoding = new QualifiedName("UnrelatedEncoding")
            };
            Assert.That(ServiceResult.IsGood(HistoryReadValueId.Validate(incoming)), Is.True);
            ServiceResult resumedError = await HistorianDispatcher.DispatchRawReadAsync(
                context, replacement,
                new BaseDataVariableState(null) { NodeId = nodeId, ValueRank = ValueRanks.Scalar },
                incoming,
                new ReadRawModifiedDetails { StartTime = time.AddDays(1), EndTime = time.AddDays(2) },
                TimestampsToReturn.Server, resumed, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(resumedError), Is.True);
            Assert.That(resumed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(resumed.HistoryData.TryGetValue(out HistoryData values), Is.True);
            Assert.That(values.DataValues.Count, Is.EqualTo(1));
            DataValue value = values.DataValues[0];
            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<int> actual), Is.True);
            ArrayOf<int> expected = [22, 23];
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.SourceTimestamp, Is.EqualTo((DateTimeUtc)time.AddSeconds(1)));
            Assert.That(value.ServerTimestamp, Is.EqualTo((DateTimeUtc)time.AddSeconds(11)));
            Assert.That(resumed.ContinuationPoint.IsEmpty, Is.True);
            holder.Clear();
        }

        [TestCase("Raw")]
        [TestCase("Annotations")]
        [TestCase("Events")]
        [TestCase("Processed")]
        public async Task WrongHistoryReadKindConsumesAndReleasesTheOriginalPointAsync(string targetKind)
        {
            using var provider = new InMemoryHistorianProvider();
            var holder = new SessionContinuationPoints(() => new NodeId(1), 1, 2, null);
            var source = new Mock<IAsyncNodeManager>();
            using HistorianContinuationState point = CreateOwnedHistoryState(
                provider, source.Object,
                kind: targetKind == "Raw" ? HistorianReadKind.Modified : HistorianReadKind.Raw);
            holder.SaveHistory(point);
            ServerSystemContext context = CreateHistorySystemContext(holder);
            using OperationContext operation = context.OperationContext;
            var node = (BaseVariableState)point.SourceNode;
            var incoming = new HistoryReadValueId
            {
                NodeId = point.OriginNodeId,
                ContinuationPoint = point.Id.ToByteArray().ToByteString()
            };
            var result = new HistoryReadResult();
            ServiceResult error = targetKind switch
            {
                "Raw" => await HistorianDispatcher.DispatchRawReadAsync(context, provider, node, incoming,
                    new ReadRawModifiedDetails(), TimestampsToReturn.Both, result, CancellationToken.None)
                    .ConfigureAwait(false),
                "Annotations" => await HistorianDispatcher.DispatchAnnotationReadAsync(
                    context, provider, node, incoming,
                    new ReadRawModifiedDetails(), TimestampsToReturn.Both, result, CancellationToken.None)
                    .ConfigureAwait(false),
                "Events" => await HistorianDispatcher.DispatchEventReadAsync(context, provider, node, incoming,
                    new ReadEventDetails(), TimestampsToReturn.Both, result, CancellationToken.None)
                    .ConfigureAwait(false),
                "Processed" => await HistorianDispatcher.DispatchProcessedReadAsync(context, provider, node, incoming,
                    new ReadProcessedDetails { StartTime = DateTimeUtc.MinValue, EndTime = DateTimeUtc.MaxValue },
                    ObjectIds.AggregateFunction_Average, TimestampsToReturn.Both, result, CancellationToken.None)
                    .ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(targetKind))
            };
            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
            Assert.That(result.HistoryData.IsNull, Is.True);
            Assert.That(holder.HasHistoryForManager(source.Object), Is.False);
            Assert.That(point.BufferedProcessedOutputs, Is.Null);
        }

        private static HistorianContinuationState CreateOwnedHistoryState(
            IHistorianProvider provider,
            IAsyncNodeManager source,
            ArrayOf<IAsyncNodeManager> dependencies = default,
            HistorianReadKind kind = HistorianReadKind.Raw)
        {
            NodeId nodeId = new(8100, 1);
            var point = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                Kind = kind,
                NodeId = nodeId,
                OriginNodeId = nodeId,
                SourceNode = new BaseDataVariableState(null) { NodeId = nodeId, ValueRank = ValueRanks.Scalar },
                ResumeToken = default,
                BufferedProcessedOutputs = [new DataValue(new Variant(42))]
            };
            point.Ownership.Manager = source;
            point.Ownership.SetDependencyOwners(dependencies);
            return point;
        }

        private static ServerSystemContext CreateHistorySystemContext(SessionContinuationPoints holder)
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            session.SetupGet(value => value.ContinuationPoints).Returns(holder);
            session.SetupGet(value => value.Identity).Returns(new UserIdentity());
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None, session.Object);
            return new ServerSystemContext(server.Object, operation);
        }
    }
}
