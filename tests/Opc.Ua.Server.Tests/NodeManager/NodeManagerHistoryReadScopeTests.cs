/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises the lifetime of history-read scopes without starting a server.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerHistoryReadScopeTests
    {
        /// <summary>
        /// A result can bind only a point saved by its own dispatch.
        /// </summary>
        [Test]
        public void HistoryResultBindingExcludesEarlierRequestSaves()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 3, null);
            var earlier = new TrackingPoint(Guid.NewGuid());
            var current = new TrackingPoint(Guid.NewGuid());
            var earlierToken = new ByteString(earlier.Id.ToByteArray());
            var currentToken = new ByteString(current.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using IDisposable request = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IHistoryContinuationPoint? restored = null;
            IHistoryContinuationPoint? restoredCurrent = null;
            try
            {
                points.SaveHistory(earlier);
                using IDisposable dispatch = points.BeginHistoryDispatch(request, generation);
                points.SaveHistory(current);

                Assert.That(points.TryBindHistoryResult(request, dispatch, earlierToken, generation), Is.False);
                Assert.That(points.TryBindHistoryResult(request, dispatch, currentToken, generation), Is.True);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                restored = points.RestoreHistory(earlierToken);
                Assert.That(restored, Is.SameAs(earlier));
                Assert.That(points.GetHistoryGeneration(earlier), Is.Null);
                restoredCurrent = points.RestoreHistory(currentToken);
                Assert.That(restoredCurrent, Is.SameAs(current));
                Assert.That(points.GetHistoryGeneration(current), Is.SameAs(generation.Reference));
            }
            finally
            {
                restored?.Dispose();
                restoredCurrent?.Dispose();
                request.Dispose();
                points.Clear();
            }
            Assert.That(earlier.DisposeCount, Is.EqualTo(1));
            Assert.That(current.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Reusing an identifier for distinct admissions in one dispatch is ambiguous.
        /// </summary>
        [Test]
        public void HistoryResultBindingRejectsAmbiguousSameIdAdmissions()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var original = new TrackingPoint(Guid.NewGuid());
            var replacement = new TrackingPoint(original.Id);
            var token = new ByteString(original.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using IDisposable request = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IHistoryContinuationPoint? restored = null;
            try
            {
                using IDisposable dispatch = points.BeginHistoryDispatch(request, generation);
                points.SaveHistory(original);
                Assert.That(points.ReleaseHistory(token), Is.True);
                points.SaveHistory(replacement);

                Assert.That(points.TryBindHistoryResult(request, dispatch, token, generation), Is.False);
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
                restored = points.RestoreHistory(token);
                Assert.That(restored, Is.SameAs(replacement));
            }
            finally
            {
                restored?.Dispose();
                request.Dispose();
                points.Clear();
            }
            Assert.That(original.DisposeCount, Is.EqualTo(1));
            Assert.That(replacement.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Repeated admissions of the same point bind its latest saved incarnation.
        /// </summary>
        [Test]
        public void HistoryResultBindingAcceptsRepeatedAdmissionOfSamePoint()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using IDisposable request = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IHistoryContinuationPoint? restored = null;
            try
            {
                using IDisposable dispatch = points.BeginHistoryDispatch(request, generation);
                points.SaveHistory(point);
                restored = points.RestoreHistory(token);
                Assert.That(restored, Is.SameAs(point));
                points.SaveHistory(point);
                restored = null;

                Assert.That(points.TryBindHistoryResult(request, dispatch, token, generation), Is.True);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                restored = points.RestoreHistory(token);
                Assert.That(restored, Is.SameAs(point));
                Assert.That(points.GetHistoryGeneration(point), Is.SameAs(generation.Reference));
            }
            finally
            {
                restored?.Dispose();
                request.Dispose();
                points.Clear();
            }
            Assert.That(point.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A result cannot replace an already captured issuer or use an ended request.
        /// </summary>
        [Test]
        public void HistoryResultBindingPreservesIssuerAndRejectsEndedRequest()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(Mock.Of<IAsyncNodeManager>());
            NodeManagerGeneration other = NodeManagerGenerationRegistry.GetOrCreate(Mock.Of<IAsyncNodeManager>());
            using IDisposable request = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IHistoryContinuationPoint? restored = null;
            try
            {
                using IDisposable dispatch = points.BeginHistoryDispatch(request, original);
                points.SaveHistory(point);
                points.BindHistoryGeneration(token, original);

                Assert.That(points.TryBindHistoryResult(request, dispatch, token, other), Is.False);
                Assert.That(points.TryBindHistoryResult(request, dispatch, token, original), Is.True);
                request.Dispose();
                Assert.That(points.TryBindHistoryResult(request, dispatch, token, original), Is.False);
                Assert.That(() => points.BeginHistoryDispatch(request, original), Throws.InvalidOperationException);
                Assert.That(points.HasHistoryForGeneration(original), Is.True);
                Assert.That(points.HasHistoryForGeneration(other), Is.False);
                restored = points.RestoreHistory(token);
                Assert.That(restored, Is.SameAs(point));
                Assert.That(points.GetHistoryGeneration(point), Is.SameAs(original.Reference));
            }
            finally
            {
                restored?.Dispose();
                request.Dispose();
                points.Clear();
            }
            Assert.That(point.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A closed callback cannot bind a point while its request is still active.
        /// </summary>
        [Test]
        public void HistoryResultBindingRejectsClosedDispatch()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using IDisposable request = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IDisposable dispatch = points.BeginHistoryDispatch(request, generation);
            try
            {
                points.SaveHistory(point);
                dispatch.Dispose();

                Assert.That(points.TryBindHistoryResult(request, dispatch, token, generation), Is.False);
                IHistoryContinuationPoint? restored = points.RestoreHistory(token);
                try
                {
                    Assert.That(restored, Is.SameAs(point));
                    Assert.That(points.GetHistoryGeneration(point), Is.Null);
                }
                finally
                {
                    restored?.Dispose();
                }
            }
            finally
            {
                dispatch.Dispose();
                request.Dispose();
                points.Clear();
            }
            Assert.That(point.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A late save inherited from one callback cannot be bound to a later callback.
        /// </summary>
        [Test]
        public async Task HistoryResultBindingRejectsEarlierCallbackContextAsync()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 3, null);
            var earlier = new TrackingPoint(Guid.NewGuid());
            var current = new TrackingPoint(Guid.NewGuid());
            var earlierToken = new ByteString(earlier.Id.ToByteArray());
            var currentToken = new ByteString(current.Id.ToByteArray());
            NodeManagerGeneration firstGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            NodeManagerGeneration secondGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using IDisposable request = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task earlierSave;
            using (points.BeginHistoryDispatch(request, firstGeneration))
            {
                earlierSave = Task.Run(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    points.SaveHistory(earlier);
                });
            }

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                using IDisposable later = points.BeginHistoryDispatch(request, secondGeneration);
                release.TrySetResult(true);
                await earlierSave.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                points.SaveHistory(current);

                Assert.That(
                    points.TryBindHistoryResult(request, later, earlierToken, secondGeneration),
                    Is.False);
                Assert.That(
                    points.TryBindHistoryResult(request, later, currentToken, secondGeneration),
                    Is.True);
                points.ReleaseSavedHistory(request);
                Assert.That(earlier.DisposeCount, Is.EqualTo(1));
                Assert.That(current.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await earlierSave.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                finally
                {
                    request.Dispose();
                    points.Clear();
                }
            }
        }

        /// <summary>
        /// A captured context cannot restore a disposed point or steal a newer point with the same token.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExpiredScopeCannotRestoreOrStealHistoryPointAsync(bool asyncRestore)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var original = new TrackingPoint(Guid.NewGuid());
            var replacement = new TrackingPoint(original.Id);
            var token = new ByteString(original.Id.ToByteArray());
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, original);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IHistoryContinuationPoint?> child = Task.Run(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return asyncRestore
                    ? await points.RestoreHistoryAsync(token).ConfigureAwait(false)
                    : points.RestoreHistory(token);
            });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                scope.Dispose();
                Assert.That(original.DisposeCount, Is.EqualTo(1));
                points.SaveHistory(replacement);
                release.TrySetResult(true);

                IHistoryContinuationPoint? captured = await child.WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                Assert.That(captured, Is.Null);
                IHistoryContinuationPoint? restored = points.RestoreHistory(token);
                Assert.That(restored, Is.SameAs(replacement));
                restored?.Dispose();
                Assert.That(replacement.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                scope.Dispose();
                release.TrySetResult(true);
                IHistoryContinuationPoint? captured = await child.WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                captured?.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// Nested scopes restore their own points and return authority to the enclosing scope.
        /// </summary>
        [Test]
        public void NestedScopesRestoreExactPoints()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var outerPoint = new TrackingPoint(Guid.NewGuid());
            var innerPoint = new TrackingPoint(Guid.NewGuid());
            var outerToken = new ByteString(outerPoint.Id.ToByteArray());
            var innerToken = new ByteString(innerPoint.Id.ToByteArray());
            using (points.BeginHistoryRead(outerToken, outerPoint))
            {
                using (points.BeginHistoryRead(innerToken, innerPoint))
                {
                    Assert.That(points.RestoreHistory(innerToken), Is.SameAs(innerPoint));
                }
                Assert.That(points.RestoreHistory(outerToken), Is.SameAs(outerPoint));
            }
            Assert.That(innerPoint.DisposeCount, Is.Zero);
            Assert.That(outerPoint.DisposeCount, Is.Zero);
            innerPoint.Dispose();
            outerPoint.Dispose();
            points.Clear();
        }

        /// <summary>
        /// A failed scope-owned cleanup retains the exact point and never repeats its disposal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FailedScopeDisposalRetainsPointWithoutRetry(bool restoredFromSession)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new Mock<IHistoryContinuationPoint>();
            var id = Guid.NewGuid();
            var failure = new InvalidOperationException("History point cleanup failed.");
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(failure);
            var token = new ByteString(id.ToByteArray());
            NodeManagerGeneration? generation = null;
            if (restoredFromSession)
            {
                var manager = new Mock<IAsyncNodeManager>();
                generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
                points.SaveHistory(point.Object);
                points.BindHistoryGeneration(token, generation);
                Assert.That(points.RestoreHistory(token), Is.SameAs(point.Object));
            }
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point.Object);

            InvalidOperationException disposalFailure = Assert.Throws<InvalidOperationException>(scope.Dispose)!;
            Assert.That(disposalFailure, Is.SameAs(failure));
            Assert.That(scope.Point, Is.Null);
            if (generation is not null)
            {
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
            }
            InvalidOperationException saveFailure = Assert.Throws<InvalidOperationException>(
                () => points.SaveHistory(point.Object))!;
            Assert.That(saveFailure.InnerException, Is.SameAs(failure));
            scope.Dispose();
            points.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Closing a scope does not dispose a point another operation has saved again.
        /// </summary>
        [Test]
        public void ScopeClosureDoesNotDisposeResavedPoint()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            points.SaveHistory(point);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            points.SaveHistory(point);

            scope.Dispose();

            Assert.That(point.DisposeCount, Is.Zero);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            point.Dispose();
            Assert.That(point.DisposeCount, Is.EqualTo(1));
            points.Clear();
        }

        /// <summary>
        /// A released or invalidated saved point remains owned until its active read ends.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase("Release")]
        [TestCase("Clear")]
        [TestCase("Retire")]
        public void ResavedHistoryReadDefersCleanupUntilScopeCloses(string operation)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            try
            {
                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                points.SaveHistory(point);
                switch (operation)
                {
                    case "Release":
                        Assert.That(points.ReleaseHistory(token), Is.True);
                        break;
                    case "Clear":
                        points.Clear();
                        break;
                    case "Retire":
                        points.RemoveHistoryForGeneration(generation);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown history cleanup operation.");
                }

                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                Assert.That(points.RestoreHistory(token), Is.Null);

                scope.Dispose();

                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                scope.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// A deferred cleanup failure remains associated with the exact point and is not retried.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase("Release")]
        [TestCase("Clear")]
        [TestCase("Retire")]
        public void ResavedHistoryReadRetainsDeferredCleanupFailure(string operation)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var failure = new InvalidOperationException("History point cleanup failed.");
            var point = new FailingPoint(Guid.NewGuid(), failure);
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            try
            {
                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                points.SaveHistory(point);
                switch (operation)
                {
                    case "Release":
                        Assert.That(points.ReleaseHistory(token), Is.True);
                        break;
                    case "Clear":
                        points.Clear();
                        break;
                    case "Retire":
                        points.RemoveHistoryForGeneration(generation);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown history cleanup operation.");
                }

                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                InvalidOperationException? observed = Assert.Throws<InvalidOperationException>(scope.Dispose);
                Assert.That(observed, Is.SameAs(failure));
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
            }
            finally
            {
                scope.Dispose();
                points.Clear();
            }
            Assert.That(point.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A rejected save does not dispose a point still used by its history read.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedHistoryResaveDefersActivePointCleanupAsync(bool asynchronous)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            try
            {
                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                points.Clear();
                ServiceResultException? failure = null;
                try
                {
                    if (asynchronous)
                    {
                        await points.SaveHistoryAsync(point).ConfigureAwait(false);
                    }
                    else
                    {
                        points.SaveHistory(point);
                    }
                }
                catch (ServiceResultException exception)
                {
                    failure = exception;
                }
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);

                scope.Dispose();

                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                scope.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// Retiring the exact generation prevents a checked-out opaque point from being saved again.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredCheckedOutHistoryCannotBeResavedAsync(bool asynchronous)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            try
            {
                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                points.RemoveHistoryForGeneration(generation);
                ServiceResultException? failure = null;
                try
                {
                    if (asynchronous)
                    {
                        await points.SaveHistoryAsync(point).ConfigureAwait(false);
                    }
                    else
                    {
                        points.SaveHistory(point);
                    }
                }
                catch (ServiceResultException exception)
                {
                    failure = exception;
                }
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);

                scope.Dispose();

                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
                Assert.That(points.RestoreHistory(token), Is.Null);
            }
            finally
            {
                scope.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// A failed history cleanup remains observable without preventing independent cleanup or being retried.
        /// </summary>
        [Test]
        public void RetiredHistoryCleanupFailureRemainsObservableDuringIndependentCleanup()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var cleanupFailure = new InvalidOperationException("History point cleanup failed.");
            var failed = new FailingPoint(Guid.NewGuid(), cleanupFailure);
            var independent = new TrackingPoint(Guid.NewGuid());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(failed);
            points.BindHistoryGeneration(new ByteString(failed.Id.ToByteArray()), generation);
            try
            {
                AggregateException? first = Assert.Throws<AggregateException>(
                    () => points.RemoveHistoryForGeneration(generation));
                Assert.That(first!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(cleanupFailure));
                Assert.That(failed.DisposeCount, Is.EqualTo(1));

                points.SaveHistory(independent);
                points.BindHistoryGeneration(new ByteString(independent.Id.ToByteArray()), generation);
                InvalidOperationException? retained = Assert.Throws<InvalidOperationException>(
                    () => points.RemoveHistoryForGeneration(generation));
                Assert.That(retained, Is.SameAs(cleanupFailure));
                Assert.That(independent.DisposeCount, Is.EqualTo(1));
                Assert.That(failed.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);

                retained = Assert.Throws<InvalidOperationException>(
                    () => points.RemoveHistoryForGeneration(generation));
                Assert.That(retained, Is.SameAs(cleanupFailure));
                Assert.That(failed.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                points.Clear();
            }
        }

        /// <summary>
        /// A saved point cannot be checked out again while its previous read still owns it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResavedHistoryReadCannotBeCheckedOutTwiceAsync(bool asynchronous)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            points.SaveHistory(point);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            try
            {
                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                points.SaveHistory(point);
                IHistoryContinuationPoint? second = asynchronous
                    ? await points.RestoreHistoryAsync(token).ConfigureAwait(false)
                    : points.RestoreHistory(token);
                Assert.That(second, Is.Null);
                Assert.That(point.DisposeCount, Is.Zero);

                scope.Dispose();

                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            }
            finally
            {
                scope.Dispose();
                points.Clear();
                if (point.DisposeCount == 0)
                {
                    point.Dispose();
                }
            }
        }

        /// <summary>
        /// Restoration owns the point before a synchronous mirror removal can block or fail.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RestoredHistoryReadOwnsPointBeforeMirrorRemovalCompletesAsync(bool failMirror)
        {
            var store = new Mock<IContinuationPointStore>();
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, store.Object);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            var failure = new IOException("History mirror removal failed.");
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
                    if (failMirror)
                    {
                        throw failure;
                    }
                });
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Task<Task<SessionContinuationPoints.HistoryReadScope>> restorationWorker = Task.Factory.StartNew(
                async () => await points.RestoreHistoryReadAsync(token, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Task<SessionContinuationPoints.HistoryReadScope> restoring = restorationWorker.Unwrap();
            SessionContinuationPoints.HistoryReadScope? scope = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                generation.Gate.Retire();
                Task drain = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                points.Clear();
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(drain.IsCompleted, Is.False);

                release.Set();
                if (failMirror)
                {
                    IOException? observed = await Assert.ThrowsAsync<IOException>(
                        async () => await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(observed, Is.SameAs(failure));
                }
                else
                {
                    scope = await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    Assert.That(scope.Point, Is.SameAs(point));
                    Assert.That(point.DisposeCount, Is.Zero);
                    Assert.That(drain.IsCompleted, Is.False);
                    scope.Dispose();
                }
                await drain.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                release.Set();
                try
                {
                    scope ??= await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (IOException) when (failMirror)
                {
                }
                scope?.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// Physical point disposal waits until both its read and an in-flight mirror removal have ended.
        /// </summary>
        [Test]
        public async Task ReleasedHistoryReadWaitsForMirrorCleanupBeforeDisposalAsync()
        {
            var store = new Mock<IContinuationPointStore>();
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, store.Object);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockRemoval = 0;
            store.Setup(value => value.RemoveContinuationPoint(
                It.IsAny<NodeId>(), ContinuationPointKind.History, point.Id)).Callback(() =>
                {
                    if (Volatile.Read(ref blockRemoval) != 0)
                    {
                        entered.TrySetResult(true);
                        if (!release.Wait(TimeSpan.FromSeconds(60)))
                        {
                            throw new TimeoutException("History mirror cleanup was not released.");
                        }
                    }
                });
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            SessionContinuationPoints.HistoryReadScope scope =
                await points.RestoreHistoryReadAsync(token, CancellationToken.None).ConfigureAwait(false);
            points.ActivateHistoryRead(scope);
            Assert.That(scope.Point, Is.SameAs(point));
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            points.SaveHistory(point);
            Volatile.Write(ref blockRemoval, 1);
            Task<bool> releasing = Task.Run(() => points.ReleaseHistory(token));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                generation.Gate.Retire();
                Task drain = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                scope.Dispose();
                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                Assert.That(drain.IsCompleted, Is.False);

                release.Set();
                Assert.That(await releasing.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false), Is.True);
                await drain.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                release.Set();
                await releasing.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                scope.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// A failed deferred disposal is retained before its transferred generation admission drains.
        /// </summary>
        [Test]
        public async Task DeferredHistoryReadFailureIsRetainedBeforeGenerationDrainsAsync()
        {
            var store = new Mock<IContinuationPointStore>();
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, store.Object);
            var point = new Mock<IHistoryContinuationPoint>();
            var id = Guid.NewGuid();
            var token = new ByteString(id.ToByteArray());
            var failure = new InvalidOperationException("History disposal failed.");
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(failure);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int blockRemoval = 0;
            store.Setup(value => value.RemoveContinuationPoint(
                It.IsAny<NodeId>(), ContinuationPointKind.History, id)).Callback(() =>
                {
                    if (Volatile.Read(ref blockRemoval) != 0)
                    {
                        entered.TrySetResult(true);
                        if (!release.Wait(TimeSpan.FromSeconds(60)))
                        {
                            throw new TimeoutException("History mirror cleanup was not released.");
                        }
                    }
                });
            points.SaveHistory(point.Object);
            points.BindHistoryGeneration(token, generation);
            SessionContinuationPoints.HistoryReadScope scope =
                await points.RestoreHistoryReadAsync(token, CancellationToken.None).ConfigureAwait(false);
            points.ActivateHistoryRead(scope);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point.Object));
            points.SaveHistory(point.Object);
            Volatile.Write(ref blockRemoval, 1);
            Task<bool> releasing = Task.Run(() => points.ReleaseHistory(token));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                generation.Gate.Retire();
                Task drain = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                scope.Dispose();
                Assert.That(drain.IsCompleted, Is.False);
                point.Verify(value => value.Dispose(), Times.Never);

                release.Set();
                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await releasing.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(observed, Is.SameAs(failure));
                await drain.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                ArrayOf<NodeManagerCleanupFailure> retained = NodeManagerCleanupRecovery.CaptureFailures(generation);
                Assert.That(retained.Count, Is.EqualTo(1));
                Assert.That(retained[0].ResourceKind, Is.EqualTo("HistoryContinuation"));
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                point.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                release.Set();
                try
                {
                    await releasing.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (InvalidOperationException observed) when (ReferenceEquals(observed, failure))
                {
                }
                scope.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// A retired generation rejects a history restore and releases the session-owned point without checkout.
        /// </summary>
        [Test]
        public async Task RetiredHistoryReadDoesNotCheckOutPointAsync()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            generation.Gate.Retire();
            using (SessionContinuationPoints.HistoryReadScope scope =
                await points.RestoreHistoryReadAsync(token, CancellationToken.None).ConfigureAwait(false))
            {
                Assert.That(scope.Point, Is.Null);
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
            }
            points.RemoveHistoryForGeneration(generation);
            Assert.That(point.DisposeCount, Is.EqualTo(1));
            Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            points.Clear();
        }

        /// <summary>
        /// An admitted portable claim keeps its generation alive until invalidated cleanup finishes.
        /// </summary>
        /// <exception cref="IOException"></exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredHistoryReadDrainsPortableClaimAsync(bool failClaim)
        {
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            var store = new Mock<IHistoryContinuationPointStore>();
            var codec = new Mock<IHistoryContinuationPointCodec>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new IOException("History claim failed.");
            codec.Setup(value => value.EncodeAsync(
                It.IsAny<NodeId>(), point, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryContinuationPointEnvelope?>(new HistoryContinuationPointEnvelope
                {
                    Id = point.Id,
                    OwnerSessionId = new NodeId(1, 1),
                    CodecId = "test",
                    CodecVersion = 1,
                    Payload = ByteString.Empty
                }));
            store.Setup(value => value.StoreAsync(
                It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            store.Setup(value => value.TryTakeAsync(
                It.IsAny<NodeId>(), point.Id, It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<bool>(ClaimAsync()));
            var points = new SessionContinuationPoints(
                () => new NodeId(1, 1), 1, 2, null, store.Object, codec.Object, new NamespaceTable());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            await points.SaveHistoryAsync(point).ConfigureAwait(false);
            points.BindHistoryGeneration(token, generation);
            Task<SessionContinuationPoints.HistoryReadScope> restoring =
                points.RestoreHistoryReadAsync(token, CancellationToken.None).AsTask();
            SessionContinuationPoints.HistoryReadScope? scope = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                generation.Gate.Retire();
                points.RemoveHistoryForGeneration(generation);
                Task drain = generation.WaitForQuiescenceAsync(CancellationToken.None).AsTask();
                Assert.That(drain.IsCompleted, Is.False);
                Assert.That(point.DisposeCount, Is.Zero);

                release.TrySetResult(true);
                if (failClaim)
                {
                    IOException? observed = await Assert.ThrowsAsync<IOException>(
                        async () => await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(observed, Is.SameAs(failure));
                }
                else
                {
                    scope = await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    Assert.That(scope.Point, Is.Null);
                    Assert.That(drain.IsCompleted, Is.False);
                    scope.Dispose();
                }
                await drain.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    scope ??= await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (IOException) when (failClaim)
                {
                }
                scope?.Dispose();
                points.Clear();
            }

            async Task<bool> ClaimAsync()
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (failClaim)
                {
                    throw failure;
                }
                return true;
            }
        }

        /// <summary>
        /// A portable restore captures its token before awaiting the store's claim.
        /// </summary>
        [Test]
        public async Task PortableHistoryReadCapturesTokenBeforeClaimCompletesAsync()
        {
            var point = new TrackingPoint(Guid.NewGuid());
            var store = new Mock<IHistoryContinuationPointStore>();
            var codec = new Mock<IHistoryContinuationPointCodec>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            codec.Setup(value => value.EncodeAsync(
                It.IsAny<NodeId>(), point, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistoryContinuationPointEnvelope?>(new HistoryContinuationPointEnvelope
                {
                    Id = point.Id,
                    OwnerSessionId = new NodeId(1, 1),
                    CodecId = "test",
                    CodecVersion = 1,
                    Payload = ByteString.Empty
                }));
            store.Setup(value => value.StoreAsync(
                It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            store.Setup(value => value.TryTakeAsync(
                It.IsAny<NodeId>(), point.Id, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<bool>(release.Task);
                });
            var points = new SessionContinuationPoints(
                () => new NodeId(1, 1), 1, 2, null, store.Object, codec.Object, new NamespaceTable());
            await points.SaveHistoryAsync(point).ConfigureAwait(false);
            byte[] mutableBytes = point.Id.ToByteArray();
            var original = new ByteString((byte[])mutableBytes.Clone());
            var mutable = new ByteString(mutableBytes);
            Task<SessionContinuationPoints.HistoryReadScope> restoring =
                points.RestoreHistoryReadAsync(mutable, CancellationToken.None).AsTask();
            SessionContinuationPoints.HistoryReadScope? scope = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                mutableBytes[0] ^= 0x80;
                release.TrySetResult(true);
                scope = await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Assert.That(scope.TryRestore(mutable, true, out _, out bool expired), Is.False);
                Assert.That(expired, Is.False);
                Assert.That(scope.TryRestore(original, true, out IHistoryContinuationPoint? restored, out expired),
                    Is.True);
                Assert.That(restored, Is.SameAs(point));
                Assert.That(expired, Is.False);
                scope.Dispose();
                Assert.That(point.DisposeCount, Is.Zero);
                restored?.Dispose();
                Assert.That(point.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                scope ??= await restoring.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                scope.Dispose();
                if (point.DisposeCount == 0)
                {
                    point.Dispose();
                }
                points.Clear();
            }
        }

        /// <summary>
        /// Restoration and the read scope use the same token snapshot even if its backing memory changes.
        /// </summary>
        [Test]
        public async Task HistoryReadUsesOneTokenSnapshotForLookupAndScopeAsync()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var original = new TrackingPoint(Guid.NewGuid());
            var replacement = new TrackingPoint(Guid.NewGuid());
            var originalToken = new ByteString(original.Id.ToByteArray());
            var replacementToken = new ByteString(replacement.Id.ToByteArray());
            using var memory = new MutatingTokenMemory(original.Id.ToByteArray(), replacement.Id.ToByteArray());
            var mutableToken = new ByteString(memory.Token);
            points.SaveHistory(original);
            points.SaveHistory(replacement);
            SessionContinuationPoints.HistoryReadScope? scope = null;
            IHistoryContinuationPoint? transferred = null;
            IHistoryContinuationPoint? restoredReplacement = null;
            try
            {
                scope = await points.RestoreHistoryReadAsync(mutableToken, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(scope.Point, Is.SameAs(original));

                _ = mutableToken.Span[0];
                Assert.That(scope.TryRestore(mutableToken, true, out _, out bool expired), Is.False);
                Assert.That(expired, Is.False);
                Assert.That(scope.TryRestore(originalToken, true, out transferred, out expired), Is.True);
                Assert.That(transferred, Is.SameAs(original));
                Assert.That(expired, Is.False);
                scope.Dispose();

                restoredReplacement = points.RestoreHistory(replacementToken);
                Assert.That(restoredReplacement, Is.SameAs(replacement));
            }
            finally
            {
                scope?.Dispose();
                transferred?.Dispose();
                restoredReplacement?.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// A deferred rejected cleanup does not dispose a historian state owned by another session.
        /// </summary>
        [Test]
        public void DeferredCleanupPreservesAnotherSessionsHistorianState()
        {
            var owner = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var other = new SessionContinuationPoints(() => new NodeId(2, 1), 1, 2, null);
            var data = new Mock<IDisposable>();
            var state = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                NodeId = new NodeId("historized", 1),
                Provider = Mock.Of<IHistorianProvider>(),
                Kind = HistorianReadKind.Raw,
                ResumeToken = default
            };
            state.Ownership.Data = data.Object;
            var token = new ByteString(state.Id.ToByteArray());
            owner.SaveHistory(state);
            SessionContinuationPoints.HistoryReadScope scope = other.BeginHistoryRead(token, state);
            try
            {
                other.DisposeHistoryReadPoint(state, checkSessionOwnership: true);
                scope.Dispose();
                data.Verify(value => value.Dispose(), Times.Never);
                Assert.That(owner.ReleaseHistory(token), Is.True);
                data.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                scope.Dispose();
                other.Clear();
                owner.Clear();
            }
        }

        /// <summary>
        /// Retiring a claimed historian state does not dispose its data while the read still owns it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RetiredHistorianClaimDefersCleanupUntilScopeCloses(bool failCleanup)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var failure = new InvalidOperationException("Historian data cleanup failed.");
            var data = new Mock<IDisposable>();
            int disposals = 0;
            data.Setup(value => value.Dispose()).Callback(() =>
            {
                disposals++;
                if (failCleanup)
                {
                    throw failure;
                }
            });
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            var state = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                NodeId = new NodeId("historized", 1),
                Provider = Mock.Of<IHistorianProvider>(),
                Kind = HistorianReadKind.Raw,
                ResumeToken = default
            };
            state.Ownership.Generation = generation.Reference;
            state.Ownership.Data = data.Object;
            var token = new ByteString(state.Id.ToByteArray());
            points.SaveHistory(state);
            Assert.That(points.RestoreHistory(token), Is.SameAs(state));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, state);
            try
            {
                Assert.That(points.RestoreHistory(token), Is.SameAs(state));
                var claim = new HistorianContinuationClaim(points, state, logger: null);
                claim.Retire();

                Assert.That(disposals, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                if (failCleanup)
                {
                    InvalidOperationException? observed = Assert.Throws<InvalidOperationException>(scope.Dispose);
                    Assert.That(observed, Is.SameAs(failure));
                }
                else
                {
                    scope.Dispose();
                }
                Assert.That(disposals, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.EqualTo(failCleanup));
            }
            finally
            {
                scope.Dispose();
                points.Clear();
            }
            Assert.That(disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// A concurrent session cleanup retains an externally disposing historian state until disposal finishes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentHistorianDisposalRetainsOwnerUntilCompletionAsync(bool failCleanup)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var failure = new InvalidOperationException("Historian data cleanup failed.");
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var data = new Mock<IDisposable>();
            int disposals = 0;
            data.Setup(value => value.Dispose()).Callback(() =>
            {
                Interlocked.Increment(ref disposals);
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(60)))
                {
                    throw new TimeoutException("Historian data cleanup was not released.");
                }
                if (failCleanup)
                {
                    throw failure;
                }
            });
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            var state = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                NodeId = new NodeId("historized", 1),
                Provider = Mock.Of<IHistorianProvider>(),
                Kind = HistorianReadKind.Raw,
                ResumeToken = default
            };
            state.Ownership.Generation = generation.Reference;
            state.Ownership.Data = data.Object;
            points.SaveHistory(state);
            Task<InvalidOperationException?> disposing = Task.Factory.StartNew<InvalidOperationException?>(
                () =>
                {
                    try
                    {
                        state.Dispose();
                        return null;
                    }
                    catch (InvalidOperationException exception)
                    {
                        return exception;
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                points.Clear();
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                Assert.That(disposing.IsCompleted, Is.False);
                Assert.That(Volatile.Read(ref disposals), Is.EqualTo(1));

                release.Set();
                InvalidOperationException? observed = await disposing.WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                if (failCleanup)
                {
                    Assert.That(observed, Is.SameAs(failure));
                }
                else
                {
                    Assert.That(observed, Is.Null);
                }
                Assert.That(points.HasHistoryForGeneration(generation), Is.EqualTo(failCleanup));
                Assert.That(Volatile.Read(ref disposals), Is.EqualTo(1));
            }
            finally
            {
                release.Set();
                await disposing.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                points.Clear();
            }
        }

        /// <summary>
        /// A captured context cannot resolve the restored historian provider after the scope closes.
        /// </summary>
        [Test]
        public async Task ExpiredScopeDoesNotResolveHistorianProviderAsync()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var nodeId = new NodeId("historized", 1);
            IHistorianProvider provider = Mock.Of<IHistorianProvider>();
            var point = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                Provider = provider,
                Kind = HistorianReadKind.Raw,
                ResumeToken = default
            };
            var token = new ByteString(point.Id.ToByteArray());
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IHistorianProvider?> child = Task.Run(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return points.GetRestoredHistoryProvider(nodeId);
            });
            try
            {
                Assert.That(points.GetRestoredHistoryProvider(nodeId), Is.SameAs(provider));
                Assert.That(points.GetRestoredHistoryProvider(new NodeId("other", 1)), Is.Null);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                scope.Dispose();
                release.TrySetResult(true);
                Assert.That(await child.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false), Is.Null);
            }
            finally
            {
                scope.Dispose();
                release.TrySetResult(true);
                await child.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                points.Clear();
            }
        }

        /// <summary>
        /// Scope closure and restoration agree on whether ownership transferred to the caller.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentRestoreAndClosureHaveOnePointOwnerAsync(bool asyncRestore)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<IHistoryContinuationPoint?> restore = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                return asyncRestore
                    ? await points.RestoreHistoryAsync(token).ConfigureAwait(false)
                    : points.RestoreHistory(token);
            });
            var close = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                scope.Dispose();
            });
            try
            {
                start.TrySetResult(true);
                IHistoryContinuationPoint? restored = await restore.WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                await close.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                if (restored is null)
                {
                    Assert.That(point.DisposeCount, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(restored, Is.SameAs(point));
                    Assert.That(point.DisposeCount, Is.Zero);
                    restored.Dispose();
                    Assert.That(point.DisposeCount, Is.EqualTo(1));
                }
            }
            finally
            {
                start.TrySetResult(true);
                await Task.WhenAll(restore, close).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                IHistoryContinuationPoint? retained = await restore.ConfigureAwait(false);
                if (retained is not null && point.DisposeCount == 0)
                {
                    retained.Dispose();
                }
                scope.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// An expired captured context does not keep a successfully closed session or point alive.
        /// </summary>
        [Test]
        public void CapturedContextDoesNotRetainSuccessfullyClosedScopeOwners()
        {
            (ExecutionContext captured,
                WeakReference<SessionContinuationPoints> session,
                WeakReference<TrackingPoint> point,
                WeakReference<byte[]> tokenBuffer) = CaptureClosedScope();

            CollectScopeOwners();

            Assert.That(IsScopeOwnerAlive(session), Is.False);
            Assert.That(IsScopeOwnerAlive(point), Is.False);
            Assert.That(IsScopeOwnerAlive(tokenBuffer), Is.False);
            GC.KeepAlive(captured);
        }

        /// <summary>
        /// An expired captured request scope does not retain its session continuation holder.
        /// </summary>
        [Test]
        public void CapturedContextDoesNotRetainClosedHistoryRequestOwner()
        {
            (ExecutionContext captured, WeakReference<SessionContinuationPoints> session) =
                CaptureClosedRequestScope();

            CollectScopeOwners();

            Assert.That(IsScopeOwnerAlive(session), Is.False);
            GC.KeepAlive(captured);
        }

        /// <summary>
        /// A captured closed nested request does not keep its enclosing request scope alive.
        /// </summary>
        [Test]
        public void CapturedNestedContextDoesNotRetainClosedEnclosingHistoryRequest()
        {
            (ExecutionContext captured, WeakReference<IDisposable> outer) = CaptureClosedNestedRequestScope();

            CollectScopeOwners();

            Assert.That(IsScopeOwnerAlive(outer), Is.False);
            GC.KeepAlive(captured);
        }

        /// <summary>
        /// A saved successor does not retain the closed request that reserved its slot.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SavedHistoryDoesNotRetainClosedReservationRequest(bool failedRequest)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 1, null);
            try
            {
                (WeakReference<IDisposable> request, ByteString token) =
                    CaptureClosedReservationRequest(points, failedRequest);

                CollectScopeOwners();

                Assert.That(IsScopeOwnerAlive(request), Is.False);
                IHistoryContinuationPoint? retained = points.RestoreHistory(token);
                Assert.That(retained, Is.Not.Null);
                retained!.Dispose();
                GC.KeepAlive(points);
            }
            finally
            {
                points.Clear();
            }
        }

        /// <summary>
        /// Closed dispatch contexts do not retain their requests, holders, or issuing generations.
        /// </summary>
        [Test]
        public void CapturedNestedDispatchDoesNotRetainClosedOwners()
        {
            (ExecutionContext captured,
                WeakReference<SessionContinuationPoints> session,
                WeakReference<IDisposable> outer,
                WeakReference<NodeManagerGeneration> generation) = CaptureClosedNestedDispatch();

            CollectScopeOwners();

            Assert.That(IsScopeOwnerAlive(session), Is.False);
            Assert.That(IsScopeOwnerAlive(outer), Is.False);
            Assert.That(IsScopeOwnerAlive(generation), Is.False);
            GC.KeepAlive(captured);
        }

        /// <summary>
        /// The session does not retain an opaque point after ownership is transferred to its caller.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CallerOwnedOpaqueHistoryCanBeCollectedAfterDisposal(bool throughReadScope)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            try
            {
                WeakReference<TrackingPoint> point = CaptureDisposedCallerOwnedHistory(
                    points, generation, throughReadScope);

                CollectScopeOwners();

                Assert.That(IsScopeOwnerAlive(point), Is.False);
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
                GC.KeepAlive(points);
            }
            finally
            {
                points.Clear();
            }
        }

        /// <summary>
        /// A failed session-owned disposal retains its exact point even when the caller releases it.
        /// </summary>
        [Test]
        public void FailedHistoryCleanupRetainsPointAfterCallerReleasesIt()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            try
            {
                WeakReference<FailingPoint> point = CaptureFailedSessionOwnedHistory(points, generation);

                CollectScopeOwners();

                Assert.That(point.TryGetTarget(out FailingPoint? retained), Is.True);
                Assert.That(retained!.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                GC.KeepAlive(points);
            }
            finally
            {
                points.Clear();
            }
        }

        /// <summary>
        /// Retirement invalidates a caller-owned point without taking its disposal ownership.
        /// </summary>
        [Test]
        public void RetiredCallerOwnedOpaqueHistoryIsInvalidatedWithoutDisposal()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            try
            {
                points.SaveHistory(point);
                points.BindHistoryGeneration(token, generation);
                Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                points.RemoveHistoryForGeneration(generation);

                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.GetHistoryGeneration(point), Is.SameAs(generation.Reference));
                ServiceResultException failure = Assert.Throws<ServiceResultException>(
                    () => points.SaveHistory(point))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                Assert.That(point.DisposeCount, Is.Zero);
                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                points.Clear();
                Assert.That(point.DisposeCount, Is.Zero);
                point.Dispose();
                Assert.That(point.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                try
                {
                    points.Clear();
                }
                finally
                {
                    if (point.DisposeCount == 0)
                    {
                        point.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Reacquiring a caller-owned point preserves its exact generation across synchronous and async saves.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ResavedCallerOwnedOpaqueHistoryKeepsItsGenerationAsync(bool asyncRestore, bool asyncSave)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            try
            {
                points.SaveHistory(point);
                points.BindHistoryGeneration(token, generation);
                IHistoryContinuationPoint? restored = asyncRestore
                    ? await points.RestoreHistoryAsync(token).ConfigureAwait(false)
                    : points.RestoreHistory(token);
                Assert.That(restored, Is.SameAs(point));
                Assert.That(points.GetHistoryGeneration(point), Is.SameAs(generation.Reference));

                if (asyncSave)
                {
                    await points.SaveHistoryAsync(point).ConfigureAwait(false);
                }
                else
                {
                    points.SaveHistory(point);
                }

                Assert.That(points.HasHistoryForGeneration(generation), Is.True);
                Assert.That(points.ReleaseHistory(token), Is.True);
                Assert.That(point.DisposeCount, Is.EqualTo(1));
                Assert.That(points.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                try
                {
                    points.Clear();
                }
                finally
                {
                    if (point.DisposeCount == 0)
                    {
                        point.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// A closed scope distinguishes its exact token from other valid history tokens.
        /// </summary>
        [TestCase(0)]
        [TestCase(8)]
        public void ExpiredScopeRejectsItsExactTokenWithoutBlockingOthers(int changedOffset)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            byte[] tokenBytes = point.Id.ToByteArray();
            var token = new ByteString(tokenBytes);
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            scope.Dispose();

            Assert.That(scope.TryRestore(token, false, out IHistoryContinuationPoint? restored, out bool expired),
                Is.False);
            Assert.That(restored, Is.Null);
            Assert.That(expired, Is.True);

            byte[] otherBytes = (byte[])tokenBytes.Clone();
            otherBytes[changedOffset] ^= 0x80;
            Assert.That(scope.TryRestore(new ByteString(otherBytes), false, out restored, out expired), Is.False);
            Assert.That(restored, Is.Null);
            Assert.That(expired, Is.False);

            Assert.That(scope.TryRestore(
                new ByteString(new byte[] { 1, 2, 3 }), false, out restored, out expired), Is.False);
            Assert.That(expired, Is.True);
            points.Clear();
        }

        /// <summary>
        /// Scope identity is not changed by a caller mutating the token's backing buffer.
        /// </summary>
        [TestCase(0)]
        [TestCase(8)]
        public void ScopeUsesTokenCapturedBeforeBackingBufferMutation(int changedOffset)
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            byte[] mutableBytes = point.Id.ToByteArray();
            byte[] originalBytes = (byte[])mutableBytes.Clone();
            var original = new ByteString(new ReadOnlyMemory<byte>(originalBytes));
            var mutable = new ByteString(new ReadOnlyMemory<byte>(mutableBytes));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(mutable, point);
            IHistoryContinuationPoint? transferred = null;
            try
            {
                mutableBytes[changedOffset] ^= 0x80;
                Assert.That(scope.TryRestore(mutable, false, out var changed, out bool expired), Is.False);
                Assert.That(changed, Is.Null);
                Assert.That(expired, Is.False);

                Assert.That(scope.TryRestore(original, false, out transferred, out expired), Is.True);
                Assert.That(transferred, Is.SameAs(point));
                Assert.That(expired, Is.False);
                scope.Dispose();

                Assert.That(scope.TryRestore(original, false, out var restored, out expired), Is.False);
                Assert.That(restored, Is.Null);
                Assert.That(expired, Is.True);
                Assert.That(scope.TryRestore(mutable, false, out restored, out expired), Is.False);
                Assert.That(restored, Is.Null);
                Assert.That(expired, Is.False);
                Assert.That(point.DisposeCount, Is.Zero);
            }
            finally
            {
                scope.Dispose();
                transferred?.Dispose();
                points.Clear();
            }
        }

        /// <summary>
        /// An active scope does not retain the request buffer containing its token.
        /// </summary>
        [Test]
        public void ActiveScopeDoesNotRetainTokenBuffer()
        {
            (SessionContinuationPoints.HistoryReadScope scope, WeakReference<byte[]> tokenBuffer) =
                CreateActiveScopeWithTokenBuffer();
            try
            {
                CollectScopeOwners();
                Assert.That(IsScopeOwnerAlive(tokenBuffer), Is.False);
                Assert.That(scope.Point, Is.Not.Null);
            }
            finally
            {
                scope.Dispose();
            }
        }

        /// <summary>
        /// A failed point remains checked out and closing the expired scope does not retry disposal.
        /// </summary>
        [Test]
        public void FailedScopeRetainsCheckedOutPointWithoutRetry()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var failure = new InvalidOperationException("History point cleanup failed.");
            var point = new FailingPoint(Guid.NewGuid(), failure);
            var token = new ByteString(point.Id.ToByteArray());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);

            InvalidOperationException? observed = Assert.Throws<InvalidOperationException>(scope.Dispose);
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(scope.Point, Is.Null);
            Assert.That(points.HasHistoryForGeneration(generation), Is.True);
            Assert.That(scope.TryRestore(token, false, out IHistoryContinuationPoint? restored, out bool expired),
                Is.False);
            Assert.That(restored, Is.Null);
            Assert.That(expired, Is.True);
            Assert.That(points.RestoreHistory(token), Is.Null);
            scope.Dispose();
            Assert.That(point.DisposeCount, Is.EqualTo(1));
            points.Clear();
        }

        /// <summary>
        /// Creates an active scope backed by a separately collectible request buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (SessionContinuationPoints.HistoryReadScope Scope, WeakReference<byte[]> TokenBuffer)
            CreateActiveScopeWithTokenBuffer()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            byte[] tokenBuffer = new byte[1024 * 1024];
            point.Id.ToByteArray().CopyTo(tokenBuffer, 128);
            var token = new ByteString(new ReadOnlyMemory<byte>(tokenBuffer, 128, 16));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            return (scope, new WeakReference<byte[]>(tokenBuffer));
        }

        /// <summary>
        /// Captures an execution context before closing its history scope.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            ExecutionContext Context,
            WeakReference<SessionContinuationPoints> Session,
            WeakReference<TrackingPoint> Point,
            WeakReference<byte[]> TokenBuffer) CaptureClosedScope()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var point = new TrackingPoint(Guid.NewGuid());
            byte[] tokenBuffer = new byte[1024 * 1024];
            point.Id.ToByteArray().CopyTo(tokenBuffer, 128);
            var token = new ByteString(new ReadOnlyMemory<byte>(tokenBuffer, 128, 16));
            SessionContinuationPoints.HistoryReadScope scope = points.BeginHistoryRead(token, point);
            ExecutionContext context = ExecutionContext.Capture()
                ?? throw new InvalidOperationException("The execution context is not available.");
            scope.Dispose();
            Assert.That(point.DisposeCount, Is.EqualTo(1));
            return (
                context,
                new WeakReference<SessionContinuationPoints>(points),
                new WeakReference<TrackingPoint>(point),
                new WeakReference<byte[]>(tokenBuffer));
        }

        /// <summary>
        /// Captures an execution context before closing its history request.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (ExecutionContext Context, WeakReference<SessionContinuationPoints> Session)
            CaptureClosedRequestScope()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            IDisposable scope = points.BeginHistoryRequest([new HistoryReadValueId()]);
            ExecutionContext context = ExecutionContext.Capture()
                ?? throw new InvalidOperationException("The execution context is not available.");
            scope.Dispose();
            return (context, new WeakReference<SessionContinuationPoints>(points));
        }

        /// <summary>
        /// Captures a nested request context and closes both request scopes.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (ExecutionContext Context, WeakReference<IDisposable> Outer) CaptureClosedNestedRequestScope()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            IDisposable outer = points.BeginHistoryRequest([new HistoryReadValueId()]);
            IDisposable inner = points.BeginHistoryRequest([new HistoryReadValueId()]);
            ExecutionContext context = ExecutionContext.Capture()
                ?? throw new InvalidOperationException("The execution context is not available.");
            inner.Dispose();
            outer.Dispose();
            return (context, new WeakReference<IDisposable>(outer));
        }

        /// <summary>
        /// Saves a continued client point using a reservation and closes its request.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (WeakReference<IDisposable> Request, ByteString Token) CaptureClosedReservationRequest(
            SessionContinuationPoints points,
            bool failedRequest)
        {
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            points.SaveHistory(point);
            IDisposable request = points.BeginHistoryRequest(
                [new HistoryReadValueId { ContinuationPoint = token }]);
            Assert.That(points.RestoreHistory(token), Is.SameAs(point));
            points.SaveHistory(point);
            if (failedRequest)
            {
                points.ReleaseSavedHistory(request);
            }
            request.Dispose();
            Assert.That(point.DisposeCount, Is.Zero);
            return (new WeakReference<IDisposable>(request), token);
        }

        /// <summary>
        /// Captures nested dispatch contexts before closing their requests.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            ExecutionContext Context,
            WeakReference<SessionContinuationPoints> Session,
            WeakReference<IDisposable> Outer,
            WeakReference<NodeManagerGeneration> Generation) CaptureClosedNestedDispatch()
        {
            var points = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            IDisposable outer = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IDisposable outerDispatch = points.BeginHistoryDispatch(outer, generation);
            IDisposable inner = points.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            IDisposable innerDispatch = points.BeginHistoryDispatch(inner, generation);
            ExecutionContext context = ExecutionContext.Capture()
                ?? throw new InvalidOperationException("The execution context is not available.");
            innerDispatch.Dispose();
            inner.Dispose();
            outerDispatch.Dispose();
            outer.Dispose();
            points.Clear();
            return (
                context,
                new WeakReference<SessionContinuationPoints>(points),
                new WeakReference<IDisposable>(outer),
                new WeakReference<NodeManagerGeneration>(generation));
        }

        /// <summary>
        /// Transfers an opaque point to its caller and releases the caller's reference.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<TrackingPoint> CaptureDisposedCallerOwnedHistory(
            SessionContinuationPoints points,
            NodeManagerGeneration generation,
            bool throughReadScope)
        {
            var point = new TrackingPoint(Guid.NewGuid());
            var token = new ByteString(point.Id.ToByteArray());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            IHistoryContinuationPoint? restored = points.RestoreHistory(token);
            Assert.That(restored, Is.SameAs(point));
            if (throughReadScope)
            {
                using (points.BeginHistoryRead(token, point))
                {
                    Assert.That(points.RestoreHistory(token), Is.SameAs(point));
                }
                Assert.That(points.GetHistoryGeneration(point), Is.SameAs(generation.Reference));
            }
            restored!.Dispose();
            Assert.That(point.DisposeCount, Is.EqualTo(1));
            return new WeakReference<TrackingPoint>(point);
        }

        /// <summary>
        /// Starts and retains one failed session-owned cleanup without returning the point itself.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<FailingPoint> CaptureFailedSessionOwnedHistory(
            SessionContinuationPoints points,
            NodeManagerGeneration generation)
        {
            var failure = new InvalidOperationException("History point cleanup failed.");
            var point = new FailingPoint(Guid.NewGuid(), failure);
            var token = new ByteString(point.Id.ToByteArray());
            points.SaveHistory(point);
            points.BindHistoryGeneration(token, generation);
            Assert.That(() => points.ReleaseHistory(token), Throws.Exception.SameAs(failure));
            Assert.That(point.DisposeCount, Is.EqualTo(1));
            return new WeakReference<FailingPoint>(point);
        }

        /// <summary>
        /// Checks a weak target without extending its lifetime into a collection cycle.
        /// </summary>
        /// <typeparam name="T">The type of the weakly referenced scope owner.</typeparam>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsScopeOwnerAlive<T>(WeakReference<T> reference)
            where T : class
        {
            return reference.TryGetTarget(out _);
        }

        /// <summary>
        /// Collects after the scope producer has returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectScopeOwners()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>
        /// Changes caller-owned token bytes on their second span access.
        /// </summary>
        private sealed class MutatingTokenMemory(byte[] initial, byte[] replacement) : MemoryManager<byte>
        {
            /// <summary>
            /// Gets the token memory without reading its contents.
            /// </summary>
            public Memory<byte> Token => CreateMemory(initial.Length);

            /// <inheritdoc/>
            public override Span<byte> GetSpan()
            {
                if (Interlocked.Increment(ref m_accesses) == 2)
                {
                    replacement.CopyTo(initial, 0);
                }
                return initial;
            }

            /// <inheritdoc/>
            public override MemoryHandle Pin(int elementIndex = 0)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void Unpin()
            {
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
            }

            private int m_accesses;
        }

        /// <summary>
        /// Records the identity and disposal of a test-owned history continuation point.
        /// </summary>
        private sealed class TrackingPoint(Guid id) : IHistoryContinuationPoint
        {
            /// <inheritdoc/>
            public Guid Id { get; } = id;

            /// <summary>
            /// Gets the number of disposal calls.
            /// </summary>
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Increment(ref m_disposeCount);
            }

            private int m_disposeCount;
        }

        /// <summary>
        /// Records a failed point cleanup without allowing a retry to succeed.
        /// </summary>
        private sealed class FailingPoint(Guid id, Exception failure) : IHistoryContinuationPoint
        {
            /// <inheritdoc/>
            public Guid Id { get; } = id;

            /// <summary>
            /// Gets the number of disposal calls.
            /// </summary>
            public int DisposeCount { get; private set; }

            /// <inheritdoc/>
            public void Dispose()
            {
                DisposeCount++;
                throw failure;
            }
        }
    }
}
