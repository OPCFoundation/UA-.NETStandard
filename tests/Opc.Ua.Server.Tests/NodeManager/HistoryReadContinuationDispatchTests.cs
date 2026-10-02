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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies how the HistoryRead dispatch manages the session's history continuation points
    /// across the operations of one request (Part 4 §7.9).
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class HistoryReadContinuationDispatchTests
    {
        /// <summary>
        /// Verifies that a request with more operations than MaxHistoryContinuationPoints gets
        /// Bad_NoContinuationPoints for the overflow and keeps the points it returns.
        /// </summary>
        [Test]
        public async Task OverflowingRequestReportsNoContinuationPointsWithoutEvictingItsOwnAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 2);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created);

            (ArrayOf<HistoryReadResult> results, _) = await harness.ReadAsync(3).ConfigureAwait(false);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[2].StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
            Assert.That(results[2].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(created[0].Disposed, Is.False);
            Assert.That(created[1].Disposed, Is.False);
            Assert.That(harness.Points.RestoreHistory(results[0].ContinuationPoint), Is.SameAs(created[0]));
            Assert.That(harness.Points.RestoreHistory(results[1].ContinuationPoint), Is.SameAs(created[1]));
        }

        /// <summary>
        /// Verifies that a request may still free a continuation point from a prior request.
        /// </summary>
        [Test]
        public async Task RequestEvictsContinuationPointsFromPriorRequestsAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 1);
            var prior = new TrackingPoint();
            harness.Points.SaveHistory(prior);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created);

            (ArrayOf<HistoryReadResult> results, _) = await harness.ReadAsync(1).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(prior.Disposed, Is.True);
        }

        /// <summary>
        /// Verifies that a faulted request releases the continuation points it had already saved,
        /// because the client never receives them, while points of earlier requests survive.
        /// </summary>
        [Test]
        public void FaultedRequestReleasesItsSavedContinuationPoints()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var prior = new TrackingPoint();
            harness.Points.SaveHistory(prior);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created, failAtOperation: 2);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.ReadAsync(3).ConfigureAwait(false));

            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[0].Disposed, Is.True);
            Assert.That(created[1].Disposed, Is.True);
            Assert.That(prior.Disposed, Is.False);
            Assert.That(harness.Points.RestoreHistory(ToByteString(created[0].Id)), Is.Null);
            Assert.That(harness.Points.RestoreHistory(ToByteString(prior.Id)), Is.SameAs(prior));
        }

        /// <summary>
        /// Verifies that a faulted request releases a continuation point a node manager saved
        /// before it failed without assigning the point to a result, since the client never
        /// receives its identifier (Part 4 §7.9).
        /// </summary>
        [Test]
        public void FaultedRequestReleasesPointSavedWithoutResult()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var saved = new TrackingPoint();
            harness.SaveThenThrow(saved);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.ReadAsync(1).ConfigureAwait(false));

            Assert.That(saved.Disposed, Is.True);
            Assert.That(harness.Points.RestoreHistory(ToByteString(saved.Id)), Is.Null);
        }

        /// <summary>
        /// Verifies that a faulted request keeps a continuation point whose identifier the client
        /// supplied, even when a node manager saved it again within the request, because the
        /// client still holds it.
        /// </summary>
        [Test]
        public void FaultedRequestKeepsClientSuppliedContinuationPoint()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var continued = new TrackingPoint();
            harness.Points.SaveHistory(continued);
            harness.RestoreSaveThenThrow();

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.ReadAsync(1, ToByteString(continued.Id)).ConfigureAwait(false));

            Assert.That(continued.Disposed, Is.False);
            Assert.That(harness.Points.RestoreHistory(ToByteString(continued.Id)), Is.SameAs(continued));
        }

        private static ByteString ToByteString(Guid id)
        {
            return new ByteString(id.ToByteArray());
        }

        /// <summary>
        /// A history continuation point that records its disposal.
        /// </summary>
        private sealed class TrackingPoint : IHistoryContinuationPoint
        {
            public Guid Id { get; } = Guid.NewGuid();

            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        /// <summary>
        /// Supplies a master manager with one mocked node manager and a session continuation store.
        /// </summary>
        private sealed class HistoryHarness : IDisposable
        {
            public HistoryHarness(int maxHistory)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var handle = new NodeHandle { NodeId = new NodeId(1, 1) };
                m_nodeId = handle.NodeId;
                var metadata = new NodeMetadata(handle, handle.NodeId) { NodeClass = NodeClass.Variable };
                Manager.SetupGet(value => value.NamespaceUris).Returns([DeterministicServerMock.TestNamespaceUri]);
                Manager.Setup(value => value.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(metadata);
                Manager.Setup(value => value.GetManagerHandleAsync(
                        It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(handle);
                Master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null,
                    Manager.Object);
                Points = new SessionContinuationPoints(() => new NodeId(99, 1), 10, maxHistory, null);
                var session = new Mock<ISession>();
                session.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
                session.SetupGet(value => value.ContinuationPoints).Returns(Points);
                Context = new OperationContext(
                    new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None, session.Object);
            }

            public Mock<IAsyncNodeManager> Manager { get; } = new();

            public MasterNodeManager Master { get; }

            public SessionContinuationPoints Points { get; }

            public OperationContext Context { get; }

            /// <summary>
            /// Makes the node manager save a new continuation point for every operation, the way a
            /// historian does when a node has more values than requested; optionally throws before
            /// handling the given operation.
            /// </summary>
            public void SaveForEveryOperation(List<TrackingPoint> created, int failAtOperation = -1)
            {
                Manager.Setup(value => value.HistoryReadAsync(
                        It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                        It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                        It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                        ArrayOf<HistoryReadValueId> nodesToRead, IList<HistoryReadResult> results,
                        IList<ServiceResult> errors, CancellationToken cancellationToken) =>
                        new ValueTask(SaveAllAsync(
                            context, nodesToRead, results, errors, created, failAtOperation, cancellationToken)));
            }

            /// <summary>
            /// Makes the node manager save the given point and then observe cancellation before
            /// it assigns the point to a result.
            /// </summary>
            public void SaveThenThrow(TrackingPoint point)
            {
                Manager.Setup(value => value.HistoryReadAsync(
                        It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                        It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                        It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                        ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> _,
                        IList<ServiceResult> _, CancellationToken cancellationToken) =>
                        new ValueTask(SaveThenThrowAsync(context, point, cancellationToken)));
            }

            /// <summary>
            /// Makes the node manager continue the client's point, save the same point again and
            /// then observe cancellation before it assigns the point to a result.
            /// </summary>
            public void RestoreSaveThenThrow()
            {
                Manager.Setup(value => value.HistoryReadAsync(
                        It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                        It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                        It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                        ArrayOf<HistoryReadValueId> nodesToRead, IList<HistoryReadResult> _,
                        IList<ServiceResult> _, CancellationToken cancellationToken) =>
                        new ValueTask(RestoreSaveThenThrowAsync(context, nodesToRead, cancellationToken)));
            }

            private static async Task SaveThenThrowAsync(
                OperationContext context,
                TrackingPoint point,
                CancellationToken cancellationToken)
            {
                await context.Session.ContinuationPoints
                    .SaveHistoryAsync(point, cancellationToken).ConfigureAwait(false);
                throw new OperationCanceledException();
            }

            private static async Task RestoreSaveThenThrowAsync(
                OperationContext context,
                ArrayOf<HistoryReadValueId> nodesToRead,
                CancellationToken cancellationToken)
            {
                IHistoryContinuationPoint point = await context.Session.ContinuationPoints
                    .RestoreHistoryAsync(nodesToRead[0].ContinuationPoint, cancellationToken)
                    .ConfigureAwait(false);
                await context.Session.ContinuationPoints
                    .SaveHistoryAsync(point, cancellationToken).ConfigureAwait(false);
                throw new OperationCanceledException();
            }

            private static async Task SaveAllAsync(
                OperationContext context,
                ArrayOf<HistoryReadValueId> nodesToRead,
                IList<HistoryReadResult> results,
                IList<ServiceResult> errors,
                List<TrackingPoint> created,
                int failAtOperation,
                CancellationToken cancellationToken)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    if (ii == failAtOperation)
                    {
                        throw new InvalidOperationException("node manager failure");
                    }
                    nodesToRead[ii].Processed = true;
                    var point = new TrackingPoint();
                    created.Add(point);
                    try
                    {
                        await context.Session.ContinuationPoints
                            .SaveHistoryAsync(point, cancellationToken).ConfigureAwait(false);
                        results[ii] = new HistoryReadResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = ToByteString(point.Id)
                        };
                        errors[ii] = ServiceResult.Good;
                    }
                    catch (ServiceResultException e)
                    {
                        results[ii] = new HistoryReadResult { StatusCode = e.StatusCode };
                        errors[ii] = e.StatusCode;
                    }
                }
            }

            public ValueTask<(ArrayOf<HistoryReadResult> values, ArrayOf<DiagnosticInfo> diagnosticInfos)> ReadAsync(
                int count,
                ByteString continuationPoint = default)
            {
                var nodesToRead = new HistoryReadValueId[count];
                for (int ii = 0; ii < count; ii++)
                {
                    nodesToRead[ii] = new HistoryReadValueId
                    {
                        NodeId = m_nodeId,
                        ContinuationPoint = continuationPoint
                    };
                }
                return Master.HistoryReadAsync(
                    Context,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Neither,
                    false,
                    nodesToRead.ToArrayOf());
            }

            public void Dispose()
            {
                Points.Clear();
                Context.Dispose();
                Master.Dispose();
                m_queues.Dispose();
            }

            private readonly NodeId m_nodeId;
            private readonly MonitoredItemQueueFactory m_queues;
        }
    }
}
