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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
        /// A resumed read admitted before retirement continues on its captured generation.
        /// </summary>
        [Test]
        public async Task AdmittedHistoryReadDispatchesAfterGenerationRetiresAsync()
        {
            var store = new Mock<IContinuationPointStore>();
            using var harness = new HistoryHarness(maxHistory: 1, store: store.Object);
            var point = new TrackingPoint();
            ByteString token = ToByteString(point.Id);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object);
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Setup(value => value.RemoveContinuationPoint(
                It.IsAny<NodeId>(), ContinuationPointKind.History, point.Id)).Callback(() =>
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(TimeSpan.FromSeconds(60)))
                    {
                        throw new TimeoutException("History mirror removal was not released.");
                    }
                });
            int calls = 0;
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> nodes, IList<HistoryReadResult> results,
                    IList<ServiceResult> errors, CancellationToken _) =>
                {
                    Interlocked.Increment(ref calls);
                    nodes[0].Processed = true;
                    results[0] = new HistoryReadResult { StatusCode = StatusCodes.Good };
                    errors[0] = ServiceResult.Good;
                    return default;
                });
            harness.Points.SaveHistory(point);
            harness.Points.BindHistoryGeneration(token, generation);
            Task<(ArrayOf<HistoryReadResult> values, ArrayOf<DiagnosticInfo> diagnostics)> reading =
                Task.Factory.StartNew(
                    async () => await harness.ReadAsync(1, token).ConfigureAwait(false),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                generation.Gate.Retire();
                Task drain = generation.Gate.WaitForDrainAsync();
                Assert.That(drain.IsCompleted, Is.False);
                Assert.That(calls, Is.Zero);
                Assert.That(point.Disposed, Is.False);

                release.Set();
                (ArrayOf<HistoryReadResult> results, _) =
                    await reading.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await drain.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(point.Disposed, Is.True);
            }
            finally
            {
                release.Set();
                await reading.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a faulted request releases the continuation points it had already saved,
        /// because the client never receives them, while points of earlier requests survive.
        /// </summary>
        [Test]
        public async Task FaultedRequestReleasesItsSavedContinuationPoints()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var prior = new TrackingPoint();
            harness.Points.SaveHistory(prior);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created, failAtOperation: 2);

            await Assert.ThrowsAsync<InvalidOperationException>(
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
        public async Task FaultedRequestReleasesPointSavedWithoutResult()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var saved = new TrackingPoint();
            harness.SaveThenThrow(saved);

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.ReadAsync(1).ConfigureAwait(false));

            Assert.That(saved.Disposed, Is.True);
            Assert.That(harness.Points.RestoreHistory(ToByteString(saved.Id)), Is.Null);
        }

        /// <summary>
        /// A failed request reports point cleanup failures without skipping other saved points or retrying disposal.
        /// </summary>
        [Test]
        public async Task FaultedRequestRetainsSavedPointCleanupFailureAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var requestFailure = new InvalidOperationException("History read failed.");
            var cleanupFailure = new InvalidOperationException("Point cleanup failed.");
            var failed = new Mock<IHistoryContinuationPoint>();
            failed.SetupGet(point => point.Id).Returns(Guid.NewGuid());
            failed.Setup(point => point.Dispose()).Throws(cleanupFailure);
            var other = new TrackingPoint();
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> _,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    context.Session.ContinuationPoints.SaveHistory(failed.Object);
                    context.Session.ContinuationPoints.SaveHistory(other);
                    return new ValueTask(Task.FromException(requestFailure));
                });

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                async () => await harness.ReadAsync(1).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(requestFailure));
            Assert.That(failure.Flatten().InnerExceptions, Has.Exactly(1).SameAs(cleanupFailure));
            Assert.That(other.Disposed, Is.True);
            Assert.Throws<InvalidOperationException>(() => harness.Points.SaveHistory(failed.Object));
            harness.Points.Clear();
            failed.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A resumed read preserves the request error and both result and scoped-point cleanup failures.
        /// </summary>
        [Test]
        public async Task FaultedResumedReadPreservesBothCleanupFailuresAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var requestFailure = new InvalidOperationException("History read failed.");
            var scopeFailure = new InvalidOperationException("Scoped point cleanup failed.");
            var resultFailure = new InvalidOperationException("Result point cleanup failed.");
            var scoped = new Mock<IHistoryContinuationPoint>();
            scoped.SetupGet(point => point.Id).Returns(Guid.NewGuid());
            scoped.Setup(point => point.Dispose()).Throws(scopeFailure);
            var resultPoint = new Mock<IHistoryContinuationPoint>();
            resultPoint.SetupGet(point => point.Id).Returns(Guid.NewGuid());
            resultPoint.Setup(point => point.Dispose()).Throws(resultFailure);
            ByteString token = ToByteString(scoped.Object.Id);
            harness.Points.SaveHistory(scoped.Object);
            harness.Points.BindHistoryGeneration(
                token, NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object));
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    context.Session.ContinuationPoints.SaveHistory(resultPoint.Object);
                    results[0] = new HistoryReadResult { ContinuationPoint = ToByteString(resultPoint.Object.Id) };
                    return new ValueTask(Task.FromException(requestFailure));
                });

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                async () => await harness.ReadAsync(1, token).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.Flatten().InnerExceptions, Has.Count.EqualTo(3));
            Assert.That(failure.Flatten().InnerExceptions, Has.Exactly(1).SameAs(requestFailure));
            Assert.That(failure.Flatten().InnerExceptions, Has.Exactly(1).SameAs(resultFailure));
            Assert.That(failure.Flatten().InnerExceptions, Has.Exactly(1).SameAs(scopeFailure));
            Assert.Throws<InvalidOperationException>(() => harness.Points.SaveHistory(scoped.Object));
            Assert.Throws<InvalidOperationException>(() => harness.Points.SaveHistory(resultPoint.Object));
            harness.Points.Clear();
            scoped.Verify(point => point.Dispose(), Times.Once);
            resultPoint.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A failed read reports an unowned result token without releasing another request's point.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FaultedHistoryReadRetainsUnattributedResultAsync(bool resume)
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var unrelated = new Mock<IHistoryContinuationPoint>();
            unrelated.SetupGet(point => point.Id).Returns(Guid.NewGuid());
            ByteString unrelatedToken = ToByteString(unrelated.Object.Id);
            harness.Points.SaveHistory(unrelated.Object);
            ByteString clientToken = default;
            if (resume)
            {
                var clientPoint = new TrackingPoint();
                clientToken = ToByteString(clientPoint.Id);
                harness.Points.SaveHistory(clientPoint);
                harness.Points.BindHistoryGeneration(
                    clientToken, NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object));
            }
            var requestFailure = new InvalidOperationException("History read failed.");
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    results[0] = new HistoryReadResult { ContinuationPoint = unrelatedToken };
                    return new ValueTask(Task.FromException(requestFailure));
                });

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                async () => await harness.ReadAsync(1, clientToken).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.Flatten().InnerExceptions, Has.Exactly(1).SameAs(requestFailure));
            Assert.That(failure.Flatten().InnerExceptions.Count(exception =>
                exception is InvalidOperationException &&
                exception.Message.Contains(
                    "could not be attributed to this request", StringComparison.Ordinal)), Is.EqualTo(1));
            unrelated.Verify(point => point.Dispose(), Times.Never);
            Assert.That(harness.Points.ReleaseHistory(unrelatedToken), Is.True);
            unrelated.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A failed read must not release a point that another operation saved under the same identifier.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FaultedHistoryReadKeepsSameIdReplacementAsync(bool resume, bool sameInstance)
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            ExecutionContext independentContext = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The test requires execution context flow.");
            Guid id = Guid.NewGuid();
            ByteString token = ToByteString(id);
            var original = new Mock<IHistoryContinuationPoint>();
            original.SetupGet(point => point.Id).Returns(id);
            Mock<IHistoryContinuationPoint> replacement = sameInstance
                ? original
                : new Mock<IHistoryContinuationPoint>();
            if (!sameInstance)
            {
                replacement.SetupGet(point => point.Id).Returns(id);
            }
            var requestFailure = new InvalidOperationException("History read failed.");
            ByteString clientToken = default;
            if (resume)
            {
                var clientPoint = new TrackingPoint();
                clientToken = ToByteString(clientPoint.Id);
                harness.Points.SaveHistory(clientPoint);
                harness.Points.BindHistoryGeneration(
                    clientToken, NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object));
            }
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    context.Session.ContinuationPoints.SaveHistory(original.Object);
                    results[0] = new HistoryReadResult { ContinuationPoint = token };
                    if (sameInstance)
                    {
                        Assert.That(
                            context.Session.ContinuationPoints.RestoreHistory(token),
                            Is.SameAs(original.Object));
                    }
                    else
                    {
                        Assert.That(context.Session.ContinuationPoints.ReleaseHistory(token), Is.True);
                    }
                    ExecutionContext.Run(
                        independentContext,
                        _ => harness.Points.SaveHistory(replacement.Object),
                        null);
                    return new ValueTask(Task.FromException(requestFailure));
                });

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.ReadAsync(1, clientToken).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(requestFailure));
            original.Verify(point => point.Dispose(), sameInstance ? Times.Never() : Times.Once());
            replacement.Verify(point => point.Dispose(), Times.Never);
            Assert.That(harness.Points.ReleaseHistory(token), Is.True);
            replacement.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A replacement saved during failed-request cleanup is not the point being released.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FaultedHistoryCleanupKeepsSameIdReplacementAsync(bool cleanupFails)
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            ExecutionContext independentContext = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The test requires execution context flow.");
            Guid id = Guid.NewGuid();
            ByteString token = ToByteString(id);
            var original = new Mock<IHistoryContinuationPoint>();
            original.SetupGet(point => point.Id).Returns(id);
            var replacement = new Mock<IHistoryContinuationPoint>();
            replacement.SetupGet(point => point.Id).Returns(id);
            var requestFailure = new InvalidOperationException("History read failed.");
            var cleanupFailure = new InvalidOperationException("Point cleanup failed.");
            original.Setup(point => point.Dispose()).Callback(() =>
            {
                ExecutionContext.Run(
                    independentContext,
                    _ => harness.Points.SaveHistory(replacement.Object),
                    null);
                if (cleanupFails)
                {
                    throw cleanupFailure;
                }
            });
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    context.Session.ContinuationPoints.SaveHistory(original.Object);
                    results[0] = new HistoryReadResult { ContinuationPoint = token };
                    return new ValueTask(Task.FromException(requestFailure));
                });

            if (cleanupFails)
            {
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await harness.ReadAsync(1).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(requestFailure));
                Assert.That(failure.Flatten().InnerExceptions, Has.Exactly(1).SameAs(cleanupFailure));
            }
            else
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await harness.ReadAsync(1).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(requestFailure));
            }

            original.Verify(point => point.Dispose(), Times.Once);
            replacement.Verify(point => point.Dispose(), Times.Never);
            Assert.That(harness.Points.ReleaseHistory(token), Is.True);
            replacement.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A successful callback cannot bind or return another operation's replacement point.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task HistoryReadDoesNotBindSameIdReplacementToWrongGenerationAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            ExecutionContext independentContext = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The test requires execution context flow.");
            Guid id = Guid.NewGuid();
            ByteString token = ToByteString(id);
            var original = new Mock<IHistoryContinuationPoint>();
            original.SetupGet(point => point.Id).Returns(id);
            var replacement = new Mock<IHistoryContinuationPoint>();
            replacement.SetupGet(point => point.Id).Returns(id);
            NodeManagerGeneration otherGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> nodes, IList<HistoryReadResult> results,
                    IList<ServiceResult> errors, CancellationToken _) =>
                {
                    context.Session.ContinuationPoints.SaveHistory(original.Object);
                    results[0] = new HistoryReadResult { StatusCode = StatusCodes.Good, ContinuationPoint = token };
                    nodes[0].Processed = true;
                    errors[0] = ServiceResult.Good;
                    Assert.That(context.Session.ContinuationPoints.ReleaseHistory(token), Is.True);
                    ExecutionContext.Run(
                        independentContext,
                        _ =>
                        {
                            harness.Points.SaveHistory(replacement.Object);
                            harness.Points.BindHistoryGeneration(token, otherGeneration);
                        },
                        null);
                    return default;
                });

            (ArrayOf<HistoryReadResult> results, _) = await harness.ReadAsync(1).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            original.Verify(point => point.Dispose(), Times.Once);
            replacement.Verify(point => point.Dispose(), Times.Never);
            IHistoryContinuationPoint? restored = harness.Points.RestoreHistory(token);
            try
            {
                Assert.That(restored, Is.SameAs(replacement.Object));
                Assert.That(
                    harness.Points.GetHistoryGeneration(replacement.Object),
                    Is.SameAs(otherGeneration.Reference));
            }
            finally
            {
                restored?.Dispose();
            }
            replacement.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A resumed read cannot bind a replacement that appeared while its saved point was still in use.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task ResumedHistoryReadDoesNotBindSameIdReplacementAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            ExecutionContext independentContext = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The test requires execution context flow.");
            Guid id = Guid.NewGuid();
            ByteString token = ToByteString(id);
            var original = new Mock<IHistoryContinuationPoint>();
            original.SetupGet(point => point.Id).Returns(id);
            var replacement = new Mock<IHistoryContinuationPoint>();
            replacement.SetupGet(point => point.Id).Returns(id);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object);
            NodeManagerGeneration other = NodeManagerGenerationRegistry.GetOrCreate(Mock.Of<IAsyncNodeManager>());
            harness.Points.SaveHistory(original.Object);
            harness.Points.BindHistoryGeneration(token, generation);
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> nodes, IList<HistoryReadResult> results,
                    IList<ServiceResult> errors, CancellationToken _) =>
                {
                    IHistoryContinuationPoint restored =
                        context.Session.ContinuationPoints.RestoreHistory(token) ??
                        throw new InvalidOperationException("The original history point was not restored.");
                    Assert.That(restored, Is.SameAs(original.Object));
                    context.Session.ContinuationPoints.SaveHistory(restored);
                    Assert.That(context.Session.ContinuationPoints.ReleaseHistory(token), Is.True);
                    ExecutionContext.Run(
                        independentContext,
                        _ =>
                        {
                            harness.Points.SaveHistory(replacement.Object);
                            harness.Points.BindHistoryGeneration(token, other);
                        },
                        null);
                    nodes[0].Processed = true;
                    results[0] = new HistoryReadResult { StatusCode = StatusCodes.Good, ContinuationPoint = token };
                    errors[0] = ServiceResult.Good;
                    return default;
                });

            (ArrayOf<HistoryReadResult> results, _) = await harness.ReadAsync(1, token).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            original.Verify(point => point.Dispose(), Times.Once);
            replacement.Verify(point => point.Dispose(), Times.Never);
            IHistoryContinuationPoint? current = harness.Points.RestoreHistory(token);
            try
            {
                Assert.That(current, Is.SameAs(replacement.Object));
                Assert.That(harness.Points.GetHistoryGeneration(replacement.Object), Is.SameAs(other.Reference));
            }
            finally
            {
                current?.Dispose();
            }
            replacement.Verify(point => point.Dispose(), Times.Once);
        }

        /// <summary>
        /// A later callback cannot return a point saved in an earlier callback's inherited context.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task HistoryReadDoesNotBindSaveFromEarlierCallbackAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var continued = new TrackingPoint();
            var latePoint = new TrackingPoint();
            ByteString continuedToken = ToByteString(continued.Id);
            ByteString lateToken = ToByteString(latePoint.Id);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object);
            harness.Points.SaveHistory(continued);
            harness.Points.BindHistoryGeneration(continuedToken, generation);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? lateSave = null;
            int calls = 0;

            async Task DispatchAsync(
                OperationContext context,
                ArrayOf<HistoryReadValueId> nodes,
                IList<HistoryReadResult> results,
                IList<ServiceResult> errors)
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    lateSave = Task.Run(async () =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                        context.Session.ContinuationPoints.SaveHistory(latePoint);
                    });
                    nodes[0].Processed = true;
                    results[0] = new HistoryReadResult { StatusCode = StatusCodes.Good };
                    errors[0] = ServiceResult.Good;
                    return;
                }

                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                release.TrySetResult(true);
                await (lateSave ?? throw new InvalidOperationException("The earlier callback did not start a save."))
                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                nodes[1].Processed = true;
                results[1] = new HistoryReadResult { StatusCode = StatusCodes.Good, ContinuationPoint = lateToken };
                errors[1] = ServiceResult.Good;
            }

            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> nodes, IList<HistoryReadResult> results,
                    IList<ServiceResult> errors, CancellationToken _) =>
                    new ValueTask(DispatchAsync(context, nodes, results, errors)));
            HistoryReadValueId[] requested =
            [
                new() { NodeId = new NodeId(1, 1), ContinuationPoint = continuedToken },
                new() { NodeId = new NodeId(1, 1) }
            ];
            Task<(ArrayOf<HistoryReadResult> values, ArrayOf<DiagnosticInfo> diagnostics)> reading =
                harness.Master.HistoryReadAsync(
                    harness.Context,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Neither,
                    false,
                    requested.ToArrayOf()).AsTask();
            try
            {
                (ArrayOf<HistoryReadResult> results, _) =
                    await reading.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                Assert.That(calls, Is.EqualTo(2));
                Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(results[1].ContinuationPoint.IsEmpty, Is.True);
                IHistoryContinuationPoint? restored = harness.Points.RestoreHistory(lateToken);
                try
                {
                    Assert.That(restored, Is.SameAs(latePoint));
                    Assert.That(harness.Points.GetHistoryGeneration(latePoint), Is.Null);
                }
                finally
                {
                    restored?.Dispose();
                }
            }
            finally
            {
                release.TrySetResult(true);
                if (lateSave is not null)
                {
                    await lateSave.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                if (!reading.IsCompleted)
                {
                    await reading.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A failed operation cannot release another supplied token, even when the request is mutated afterward.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FaultedResumedReadKeepsOtherClientSuppliedTokenAsync(bool mutateTokenBuffer)
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var requestFailure = new InvalidOperationException("History read failed.");
            var first = new TrackingPoint();
            var other = new TrackingPoint();
            ByteString firstToken = ToByteString(first.Id);
            byte[] otherBytes = other.Id.ToByteArray();
            ByteString otherToken = ByteString.From(otherBytes.AsSpan());
            HistoryReadValueId[] requested =
            [
                new() { NodeId = new NodeId(1, 1), ContinuationPoint = firstToken },
                new() { NodeId = new NodeId(1, 1), ContinuationPoint = new ByteString(otherBytes) }
            ];
            harness.Points.SaveHistory(first);
            harness.Points.SaveHistory(other);
            harness.Points.BindHistoryGeneration(
                firstToken, NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object));
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    if (mutateTokenBuffer)
                    {
                        otherBytes[0] ^= 1;
                    }
                    else
                    {
                        requested[1].ContinuationPoint = ByteString.Empty;
                    }
                    results[0] = new HistoryReadResult { ContinuationPoint = otherToken };
                    return new ValueTask(Task.FromException(requestFailure));
                });

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.Master.HistoryReadAsync(
                    harness.Context,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Neither,
                    false,
                    requested.ToArrayOf()).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(requestFailure));
            Assert.That(other.Disposed, Is.False);
            Assert.That(harness.Points.ReleaseHistory(otherToken), Is.True);
            Assert.That(other.Disposed, Is.True);
        }

        /// <summary>
        /// Custom continuation stores keep supplied opaque tokens and release only newly returned tokens.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FaultedRequestKeepsMutatedOpaqueClientTokenAsync(bool mutateTokenBuffer)
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var store = new Mock<ISessionContinuationPoints>();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
            session.SetupGet(value => value.ContinuationPoints).Returns(store.Object);
            var context = new OperationContext(
                new RequestHeader(), null!, RequestType.HistoryRead, RequestLifetime.None, session.Object);
            byte[] suppliedBytes = [1, 2, 3, 4, 5];
            ByteString supplied = ByteString.From(suppliedBytes.AsSpan());
            ByteString created = ByteString.From(new byte[] { 6, 7, 8 }.AsSpan());
            HistoryReadValueId[] requested =
            [
                new() { NodeId = new NodeId(1, 1) },
                new() { NodeId = new NodeId(1, 1), ContinuationPoint = new ByteString(suppliedBytes) }
            ];
            var requestFailure = new InvalidOperationException("History read failed.");
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    if (mutateTokenBuffer)
                    {
                        suppliedBytes[0] ^= 1;
                    }
                    else
                    {
                        requested[1].ContinuationPoint = ByteString.Empty;
                    }
                    results[0] = new HistoryReadResult { ContinuationPoint = supplied };
                    results[1] = new HistoryReadResult { ContinuationPoint = created };
                    return new ValueTask(Task.FromException(requestFailure));
                });

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.Master.HistoryReadAsync(
                    context,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Neither,
                    false,
                    requested.ToArrayOf()).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(requestFailure));
            store.Verify(value => value.ReleaseHistory(supplied), Times.Never);
            store.Verify(value => value.ReleaseHistory(created), Times.Once);
        }

        /// <summary>
        /// A failed request preserves a supplied identifier even when the holder initially has no entries.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FaultedRequestKeepsSuppliedTokenWithInitiallyEmptyHistoryAsync(
            bool allocatedHistory,
            bool assignResult)
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            if (allocatedHistory)
            {
                var previous = new TrackingPoint();
                harness.Points.SaveHistory(previous);
                Assert.That(harness.Points.ReleaseHistory(ToByteString(previous.Id)), Is.True);
            }
            var point = new Mock<IHistoryContinuationPoint>();
            Guid id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            ByteString token = ToByteString(id);
            HistoryReadValueId[] requested =
            [
                new() { NodeId = new NodeId(1, 1) },
                new() { NodeId = new NodeId(1, 1), ContinuationPoint = token }
            ];
            var requestFailure = new InvalidOperationException("History read failed.");
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> _, IList<HistoryReadResult> results,
                    IList<ServiceResult> _, CancellationToken _) =>
                {
                    context.Session.ContinuationPoints.SaveHistory(point.Object);
                    if (assignResult)
                    {
                        results[0] = new HistoryReadResult { ContinuationPoint = token };
                    }
                    return new ValueTask(Task.FromException(requestFailure));
                });

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.Master.HistoryReadAsync(
                    harness.Context,
                    new ExtensionObject(new ReadRawModifiedDetails()),
                    TimestampsToReturn.Neither,
                    false,
                    requested.ToArrayOf()).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(requestFailure));
            point.Verify(value => value.Dispose(), Times.Never);
            Assert.That(harness.Points.ReleaseHistory(token), Is.True);
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that a faulted request keeps a continuation point whose identifier the client
        /// supplied, even when a node manager saved it again within the request, because the
        /// client still holds it.
        /// </summary>
        [Test]
        public async Task FaultedRequestKeepsClientSuppliedContinuationPoint()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created);
            (ArrayOf<HistoryReadResult> initial, _) = await harness.ReadAsync(1).ConfigureAwait(false);
            TrackingPoint continued = created[0];
            harness.RestoreSaveThenThrow();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.ReadAsync(1, initial[0].ContinuationPoint).ConfigureAwait(false));

            Assert.That(continued.Disposed, Is.False);
            Assert.That(harness.Points.RestoreHistory(ToByteString(continued.Id)), Is.SameAs(continued));
        }

        /// <summary>
        /// Verifies that a faulted request keeps a client supplied continuation point that the
        /// node manager restored, saved again and assigned to a result before it failed.
        /// </summary>
        [Test]
        public async Task FaultedRequestKeepsAssignedClientSuppliedContinuationPointAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created);
            (ArrayOf<HistoryReadResult> initial, _) = await harness.ReadAsync(1).ConfigureAwait(false);
            TrackingPoint continued = created[0];
            harness.RestoreSaveAssignThenThrow();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await harness.ReadAsync(1, initial[0].ContinuationPoint).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(continued.Disposed, Is.False);
            Assert.That(harness.Points.RestoreHistory(ToByteString(continued.Id)), Is.SameAs(continued));
        }

        /// <summary>
        /// Verifies that each fresh opaque point returned by a resumed read retains its issuing generation.
        /// </summary>
        [Test]
        public async Task ResumedHistoryReadBindsEachFreshOpaqueContinuationAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created);

            (ArrayOf<HistoryReadResult> first, _) = await harness.ReadAsync(1).ConfigureAwait(false);
            Assert.That(first[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(first[0].ContinuationPoint.IsEmpty, Is.False);

            (ArrayOf<HistoryReadResult> second, _) = await harness
                .ReadAsync(1, first[0].ContinuationPoint).ConfigureAwait(false);
            Assert.That(second[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(second[0].ContinuationPoint.IsEmpty, Is.False);
            Assert.That(second[0].ContinuationPoint, Is.Not.EqualTo(first[0].ContinuationPoint));
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[0].Disposed, Is.True);
            Assert.That(created[1].Disposed, Is.False);

            (ArrayOf<HistoryReadResult> third, _) = await harness
                .ReadAsync(1, second[0].ContinuationPoint).ConfigureAwait(false);
            Assert.That(third[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(third[0].ContinuationPoint.IsEmpty, Is.False);
            Assert.That(third[0].ContinuationPoint, Is.Not.EqualTo(first[0].ContinuationPoint));
            Assert.That(third[0].ContinuationPoint, Is.Not.EqualTo(second[0].ContinuationPoint));
            Assert.That(created, Has.Count.EqualTo(3));
            Assert.That(created[1].Disposed, Is.True);
            Assert.That(created[2].Disposed, Is.False);

            (ArrayOf<HistoryReadResult> released, _) = await harness
                .ReadAsync(1, third[0].ContinuationPoint, release: true).ConfigureAwait(false);
            Assert.That(released[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(released[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(created[2].Disposed, Is.True);

            (ArrayOf<HistoryReadResult> repeated, _) = await harness
                .ReadAsync(1, third[0].ContinuationPoint).ConfigureAwait(false);
            Assert.That(repeated[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(created, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// Verifies that a fresh point from a resumed read cannot reopen its retired issuing generation.
        /// </summary>
        [Test]
        public async Task RetiredIssuerRejectsFreshOpaqueHistoryContinuationAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 10);
            var created = new List<TrackingPoint>();
            harness.SaveForEveryOperation(created);

            (ArrayOf<HistoryReadResult> first, _) = await harness.ReadAsync(1).ConfigureAwait(false);
            Assert.That(first[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            (ArrayOf<HistoryReadResult> second, _) = await harness
                .ReadAsync(1, first[0].ContinuationPoint).ConfigureAwait(false);
            Assert.That(second[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(second[0].ContinuationPoint.IsEmpty, Is.False);
            Assert.That(second[0].ContinuationPoint, Is.Not.EqualTo(first[0].ContinuationPoint));
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[1].Disposed, Is.False);

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object);
            Assert.That(harness.Points.HasHistoryForGeneration(generation), Is.True);
            generation.Gate.Retire();

            (ArrayOf<HistoryReadResult> rejected, _) = await harness
                .ReadAsync(1, second[0].ContinuationPoint).ConfigureAwait(false);
            Assert.That(rejected[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(rejected[0].ContinuationPoint.IsEmpty, Is.True);
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(created[1].Disposed, Is.True);
            Assert.That(harness.Points.HasHistoryForGeneration(generation), Is.False);
            Assert.That(harness.Points.RestoreHistory(second[0].ContinuationPoint), Is.Null);
            harness.Manager.Verify(value => value.HistoryReadAsync(
                It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// An admitted history read keeps its generation until an unconsumed point finishes disposal.
        /// </summary>
        [Test]
        [Category("Server")]
        [Category("NodeManagerLifecycle")]
        public async Task HistoryPointDisposalRemainsInsideGenerationAdmissionAsync()
        {
            using var harness = new HistoryHarness(maxHistory: 2);
            var point = new BlockingPoint();
            ByteString token = ToByteString(point.Id);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(harness.Manager.Object);
            harness.Points.SaveHistory(point);
            harness.Points.BindHistoryGeneration(token, generation);
            harness.Manager.Setup(value => value.HistoryReadAsync(
                    It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                    It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                    It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, HistoryReadDetails _, TimestampsToReturn _, bool _,
                    ArrayOf<HistoryReadValueId> nodes, IList<HistoryReadResult> _, IList<ServiceResult> errors,
                    CancellationToken _) =>
                {
                    nodes[0].Processed = true;
                    errors[0] = ServiceResult.Good;
                    return default;
                });
            Task<(ArrayOf<HistoryReadResult> values, ArrayOf<DiagnosticInfo> diagnosticInfos)> read =
                Task.Factory.StartNew(
                    async () => await harness.ReadAsync(1, token).ConfigureAwait(false),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
            try
            {
                await point.DisposalEntered.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                generation.Gate.Retire();
                Task drained = generation.Gate.WaitForDrainAsync();
                Assert.That(drained.IsCompleted, Is.False);
                Assert.That(point.DisposeCount, Is.EqualTo(1));

                point.Release();
                (ArrayOf<HistoryReadResult> results, _) =
                    await read.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await drained.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(point.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                point.Release();
                try
                {
                    await read.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                finally
                {
                    if (read.IsCompleted)
                    {
                        point.DisposeGate();
                    }
                }
            }
        }

        private static ByteString ToByteString(Guid id)
        {
            return new ByteString(id.ToByteArray());
        }

        /// <summary>
        /// Holds synchronous point disposal so the test can inspect generation admission.
        /// </summary>
        private sealed class BlockingPoint : IHistoryContinuationPoint
        {
            /// <inheritdoc/>
            public Guid Id { get; } = Guid.NewGuid();

            /// <summary>
            /// Gets the signal raised when disposal begins.
            /// </summary>
            internal Task DisposalEntered => m_entered.Task;

            /// <summary>
            /// Gets the number of disposal calls.
            /// </summary>
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Increment(ref m_disposeCount);
                m_entered.TrySetResult(true);
                if (!m_release.Wait(TimeSpan.FromSeconds(60)))
                {
                    throw new TimeoutException("The test did not release history point disposal.");
                }
            }

            /// <summary>
            /// Releases the held disposal.
            /// </summary>
            internal void Release()
            {
                m_release.Set();
            }

            /// <summary>
            /// Disposes the test gate after the read has joined.
            /// </summary>
            internal void DisposeGate()
            {
                m_release.Dispose();
            }

            // Dispose is the callback held by this gate; DisposeGate runs only after that callback has joined.
            // TODO: Express this split test-storage ownership in a shared blocking-callback fixture.
            [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
                Justification = "The owning test calls DisposeGate after joining the history read.")]
            private readonly ManualResetEventSlim m_release = new();

            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_disposeCount;
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
            public HistoryHarness(int maxHistory, IContinuationPointStore? store = null)
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
                Points = new SessionContinuationPoints(() => new NodeId(99, 1), 10, maxHistory, store);
                var session = new Mock<ISession>();
                session.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
                session.SetupGet(value => value.ContinuationPoints).Returns(Points);
                Context = new OperationContext(
                    new RequestHeader(), null!, RequestType.HistoryRead, RequestLifetime.None, session.Object);
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

            /// <summary>
            /// Makes the node manager continue the client's point, save it again, assign it to the
            /// result and then fail.
            /// </summary>
            public void RestoreSaveAssignThenThrow()
            {
                Manager.Setup(value => value.HistoryReadAsync(
                        It.IsAny<OperationContext>(), It.IsAny<HistoryReadDetails>(),
                        It.IsAny<TimestampsToReturn>(), It.IsAny<bool>(),
                        It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<IList<HistoryReadResult>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, HistoryReadDetails _, TimestampsToReturn _, bool _,
                        ArrayOf<HistoryReadValueId> nodesToRead, IList<HistoryReadResult> results,
                        IList<ServiceResult> _, CancellationToken cancellationToken) =>
                        new ValueTask(RestoreSaveAssignThenThrowAsync(
                            context, nodesToRead, results, cancellationToken)));
            }

            private static async Task RestoreSaveAssignThenThrowAsync(
                OperationContext context,
                ArrayOf<HistoryReadValueId> nodesToRead,
                IList<HistoryReadResult> results,
                CancellationToken cancellationToken)
            {
                IHistoryContinuationPoint point = (await context.Session.ContinuationPoints
                    .RestoreHistoryAsync(nodesToRead[0].ContinuationPoint, cancellationToken)
                    .ConfigureAwait(false))!;
                await context.Session.ContinuationPoints
                    .SaveHistoryAsync(point, cancellationToken).ConfigureAwait(false);
                results[0] = new HistoryReadResult
                {
                    StatusCode = StatusCodes.Good,
                    ContinuationPoint = nodesToRead[0].ContinuationPoint
                };
                throw new OperationCanceledException();
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
                IHistoryContinuationPoint point = (await context.Session.ContinuationPoints
                    .RestoreHistoryAsync(nodesToRead[0].ContinuationPoint, cancellationToken)
                    .ConfigureAwait(false))!;
                await context.Session.ContinuationPoints
                    .SaveHistoryAsync(point!, cancellationToken).ConfigureAwait(false);
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
                ByteString continuationPoint = default,
                bool release = false)
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
                    release,
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
