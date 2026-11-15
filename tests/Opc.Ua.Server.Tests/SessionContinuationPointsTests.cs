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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Direct unit tests for <see cref="SessionContinuationPoints"/> — the per-session holder
    /// for browse and history continuation points. The holder is driven directly (no live
    /// server) to exercise save/restore, capacity eviction, mirrored-owner cleanup, envelope
    /// creation and disposal on clear.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class SessionContinuationPointsTests
    {
        private static readonly NodeId s_sessionId = new(1000);
        private static readonly NodeId s_ownerSessionId = new(2000);

        /// <summary>
        /// Verifies that continuation storage construction rejects a null session-identifier provider.
        /// </summary>
        [Test]
        public void ConstructorThrowsWhenSessionIdProviderNull()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new SessionContinuationPoints(null!, 2, 2, null))!;
            Assert.That(ex.ParamName, Is.EqualTo("sessionIdProvider"));
        }

        /// <summary>
        /// Verifies that the maximum number of browse continuations can be configured.
        /// </summary>
        [Test]
        public void MaxBrowseIsConfigurable()
        {
            SessionContinuationPoints holder = NewHolder(maxBrowse: 3);

            Assert.That(holder.MaxBrowse, Is.EqualTo(3));

            holder.MaxBrowse = 5;
            Assert.That(holder.MaxBrowse, Is.EqualTo(5));
        }

        /// <summary>
        /// Verifies that saving a null browse continuation is rejected.
        /// </summary>
        [Test]
        public void SaveBrowseThrowsOnNullContinuationPoint()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.SaveBrowse(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("continuationPoint"));
        }

        /// <summary>
        /// Verifies that saving and restoring a browse continuation returns the same instance.
        /// </summary>
        [Test]
        public void SaveBrowseThenRestoreBrowseReturnsSamePoint()
        {
            SessionContinuationPoints holder = NewHolder();
            ContinuationPoint cp = NewBrowsePoint();

            holder.SaveBrowse(cp);
            ContinuationPoint? restored = holder.RestoreBrowse(ToByteString(cp.Id));

            Assert.That(restored, Is.SameAs(cp));
            // The point is removed on restore, so a second restore misses.
            Assert.That(holder.RestoreBrowse(ToByteString(cp.Id)), Is.Null);
        }

        /// <summary>
        /// Verifies that saving a browse continuation evicts the oldest entry and notifies the backing store.
        /// </summary>
        [Test]
        public void SaveBrowseEvictsOldestAndNotifiesStore()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(maxBrowse: 1, store: store.Object);

            var evicted = new TrackingDisposable();
            ContinuationPoint cp1 = NewBrowsePoint(data: evicted);
            ContinuationPoint cp2 = NewBrowsePoint();
            ContinuationPoint cp3 = NewBrowsePoint();

            holder.SaveBrowse(cp1);
            holder.SaveBrowse(cp2);
            holder.SaveBrowse(cp3);

            Assert.That(evicted.Disposed, Is.True);
            Assert.That(holder.RestoreBrowse(ToByteString(cp1.Id)), Is.Null);
            Assert.That(holder.RestoreBrowse(ToByteString(cp2.Id)), Is.Null);
            Assert.That(holder.RestoreBrowse(ToByteString(cp3.Id)), Is.SameAs(cp3));
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.Browse, cp1.Id),
                Times.Once);
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.Browse, cp2.Id),
                Times.Once);
        }

        /// <summary>
        /// Verifies that browse continuation eviction occurs when the configured count limit is reached.
        /// </summary>
        [Test]
        public void SaveBrowseEvictsWhenCountReachesConfiguredLimit()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(maxBrowse: 2, store: store.Object);

            var evicted = new TrackingDisposable();
            ContinuationPoint cp1 = NewBrowsePoint(data: evicted);
            ContinuationPoint cp2 = NewBrowsePoint();
            ContinuationPoint cp3 = NewBrowsePoint();

            holder.SaveBrowse(cp1);
            holder.SaveBrowse(cp2);
            holder.SaveBrowse(cp3);

            Assert.That(evicted.Disposed, Is.True);
            Assert.That(holder.RestoreBrowse(ToByteString(cp1.Id)), Is.Null);
            Assert.That(holder.RestoreBrowse(ToByteString(cp2.Id)), Is.SameAs(cp2));
            Assert.That(holder.RestoreBrowse(ToByteString(cp3.Id)), Is.SameAs(cp3));
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.Browse, cp1.Id),
                Times.Once);
        }

        /// <summary>
        /// Verifies that persisted browse envelopes normalize node identifiers.
        /// </summary>
        [Test]
        public void SaveBrowseStoresEnvelopeWithNormalizedNodeIds()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            ContinuationPointEnvelope? captured = null;
            store
                .Setup(s => s.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback<ContinuationPointEnvelope>(envelope => captured = envelope);

            SessionContinuationPoints holder = NewHolder(store: store.Object);
            ContinuationPoint cp = NewBrowsePoint();
            cp.RequestedNodeId = new NodeId(5);
            // ReferenceTypeId is left null so both NormalizeNodeId branches are exercised.

            holder.SaveBrowse(cp);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Id, Is.EqualTo(cp.Id));
            Assert.That(captured.OwnerSessionId, Is.EqualTo(s_sessionId));
            Assert.That(captured.Kind, Is.EqualTo(ContinuationPointKind.Browse));
            Assert.That(captured.BrowseNodeId, Is.EqualTo(new NodeId(5)));
            Assert.That(captured.ReferenceTypeId.IsNull, Is.True);
        }

        /// <summary>
        /// Verifies that browse restoration returns null before any continuation is saved.
        /// </summary>
        [Test]
        public void RestoreBrowseReturnsNullBeforeAnySave()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.That(holder.RestoreBrowse(ToByteString(Guid.NewGuid())), Is.Null);
        }

        /// <summary>
        /// Verifies that browse restoration returns null for an identifier with the wrong length.
        /// </summary>
        [Test]
        public void RestoreBrowseReturnsNullForWrongLength()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveBrowse(NewBrowsePoint());

            Assert.That(holder.RestoreBrowse(new ByteString(new byte[] { 1, 2, 3 })), Is.Null);
        }

        /// <summary>
        /// Verifies that browse restoration returns null for an unknown continuation.
        /// </summary>
        [Test]
        public void RestoreBrowseReturnsNullWhenNotFound()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveBrowse(NewBrowsePoint());

            Assert.That(holder.RestoreBrowse(ToByteString(Guid.NewGuid())), Is.Null);
        }

        /// <summary>
        /// Verifies that browse cleanup rejects a null node manager.
        /// </summary>
        [Test]
        public void RemoveBrowseForManagerThrowsOnNullNodeManager()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.RemoveBrowseForManager(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        /// <summary>
        /// Verifies that browse cleanup without saved points has no effect.
        /// </summary>
        [Test]
        public void RemoveBrowseForManagerIsNoOpWhenNoBrowsePointsSaved()
        {
            SessionContinuationPoints holder = NewHolder();
            IAsyncNodeManager nodeManager = NewNodeManager(new Mock<INodeManager>().Object);

            Assert.DoesNotThrow(() => holder.RemoveBrowseForManager(nodeManager));
        }

        /// <summary>
        /// Verifies that browse cleanup removes matching node-manager points and notifies the store.
        /// </summary>
        [Test]
        public void RemoveBrowseForManagerRemovesMatchingManagerAndNotifiesStore()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(store: store.Object);

            IAsyncNodeManager matchingManager = NewNodeManager(new Mock<INodeManager>().Object);
            IAsyncNodeManager otherManager = NewNodeManager(new Mock<INodeManager>().Object);

            var evicted = new TrackingDisposable();
            ContinuationPoint matchingCp = NewBrowsePoint(data: evicted);
            matchingCp.Manager = matchingManager;
            ContinuationPoint otherCp = NewBrowsePoint();
            otherCp.Manager = otherManager;

            holder.SaveBrowse(matchingCp);
            holder.SaveBrowse(otherCp);

            holder.RemoveBrowseForManager(matchingManager);

            Assert.That(evicted.Disposed, Is.True);
            Assert.That(holder.RestoreBrowse(ToByteString(matchingCp.Id)), Is.Null);
            Assert.That(holder.RestoreBrowse(ToByteString(otherCp.Id)), Is.SameAs(otherCp));
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.Browse, matchingCp.Id),
                Times.Once);
        }

        /// <summary>
        /// Verifies that browse cleanup does not confuse distinct adapter generations sharing a synchronous manager.
        /// </summary>
        [Test]
        public void RemoveBrowseForManagerDoesNotMatchByUnderlyingSyncNodeManager()
        {
            SessionContinuationPoints holder = NewHolder();
            INodeManager sharedSyncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager creatingManager = NewNodeManager(sharedSyncManager);
            IAsyncNodeManager lookupManager = NewNodeManager(sharedSyncManager);

            ContinuationPoint cp = NewBrowsePoint();
            cp.Manager = creatingManager;
            holder.SaveBrowse(cp);

            holder.RemoveBrowseForManager(lookupManager);

            Assert.That(holder.RestoreBrowse(ToByteString(cp.Id)), Is.SameAs(cp));
        }

        /// <summary>
        /// Cleanup cannot remove continuation points from a different candidate with the same slot and number.
        /// </summary>
        [Test]
        public void RemoveForGenerationPreservesPointsFromAnotherSameNumberCandidate()
        {
            SessionContinuationPoints holder = NewHolder();
            var slot = new NodeManagerSlot();
            IAsyncNodeManager manager = NewNodeManager(new Mock<INodeManager>().Object);
            var retired = new NodeManagerGeneration(slot, 2, manager, NodeManagerGenerationState.Prepared);
            var owner = new NodeManagerGeneration(slot, 2, manager, NodeManagerGenerationState.Prepared);
            var browseData = new TrackingDisposable();
            ContinuationPoint browse = NewBrowsePoint(browseData);
            browse.Generation = owner.Reference;
            var history = new TrackingDisposable();

            try
            {
                holder.SaveBrowse(browse);
                holder.SaveHistory(history);
                holder.BindHistoryGeneration(ToByteString(history.Id), owner);

                Assert.That(holder.HasBrowseForGeneration(owner), Is.True);
                Assert.That(holder.HasHistoryForGeneration(owner), Is.True);
                Assert.That(holder.HasBrowseForGeneration(retired), Is.False);
                Assert.That(holder.HasHistoryForGeneration(retired), Is.False);

                holder.RemoveForGeneration(retired);
                Assert.That(browseData.Disposed, Is.False);
                Assert.That(history.Disposed, Is.False);
                Assert.That(holder.HasBrowseForGeneration(owner), Is.True);
                Assert.That(holder.HasHistoryForGeneration(owner), Is.True);

                holder.RemoveForGeneration(owner);
                Assert.That(browseData.Disposed, Is.True);
                Assert.That(history.Disposed, Is.True);
                Assert.That(holder.HasBrowseForGeneration(owner), Is.False);
                Assert.That(holder.HasHistoryForGeneration(owner), Is.False);
                Assert.That(holder.RestoreBrowse(ToByteString(browse.Id)), Is.Null);
                Assert.That(holder.RestoreHistory(ToByteString(history.Id)), Is.Null);
            }
            finally
            {
                holder.Clear();
            }
        }

        /// <summary>
        /// Verifies that saving a null history continuation is rejected.
        /// </summary>
        [Test]
        public void SaveHistoryThrowsOnNullContinuationPoint()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.SaveHistory(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("continuationPoint"));
        }

        /// <summary>
        /// Verifies that a saved history continuation can be restored.
        /// </summary>
        [Test]
        public void SaveHistoryThenRestoreHistoryReturnsValue()
        {
            SessionContinuationPoints holder = NewHolder();
            var id = Guid.NewGuid();
            var value = new TrackingDisposable(id);

            holder.SaveHistory(value);

            Assert.That(holder.RestoreHistory(ToByteString(id)), Is.SameAs(value));
            // The point is removed on restore, so a second restore misses.
            Assert.That(holder.RestoreHistory(ToByteString(id)), Is.Null);
        }

        /// <summary>
        /// A portable point transferred to its caller can be saved again with its original generation.
        /// </summary>
        [Test]
        public async Task PortableCallerOwnedHistoryRetainsGenerationWhenResavedAsync()
        {
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            var point = new TrackingDisposable(Guid.NewGuid());
            var token = ToByteString(point.Id);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            try
            {
                await holder.SaveHistoryAsync(point).ConfigureAwait(false);
                holder.BindHistoryGeneration(token, generation);
                IHistoryContinuationPoint? restored = await holder.RestoreHistoryAsync(token).ConfigureAwait(false);
                Assert.That(restored, Is.SameAs(point));
                Assert.That(holder.GetHistoryGeneration(point), Is.SameAs(generation.Reference));

                await holder.SaveHistoryAsync(point).ConfigureAwait(false);

                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                Assert.That(holder.ReleaseHistory(token), Is.True);
                Assert.That(point.Disposed, Is.True);
                Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
                store.Verify(value => value.TryTakeAsync(
                    s_sessionId, point.Id, It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                holder.Clear();
                if (!point.Disposed)
                {
                    point.Dispose();
                }
            }
        }

        /// <summary>
        /// Mirror and cleanup failures are both observable while the exact failed point remains retained.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task HistoryRestoreRetainsCleanupFailureAfterMirrorFailureAsync(
            bool asyncRestore,
            bool cleanupFails)
        {
            var id = Guid.NewGuid();
            var mirrorFailure = new InvalidOperationException("mirror removal failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var store = new Mock<IContinuationPointStore>();
            int removalCount = 0;
            store.Setup(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id))
                .Callback(() =>
                {
                    if (Interlocked.Increment(ref removalCount) == 1)
                    {
                        throw mirrorFailure;
                    }
                });
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            if (cleanupFails)
            {
                point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            }
            SessionContinuationPoints holder = NewHolder(store: store.Object);
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            holder.SaveHistory(point.Object);
            ByteString token = ToByteString(id);
            holder.BindHistoryGeneration(token, generation);

            Exception failure = await Assert.CatchAsync<Exception>(async () =>
            {
                if (asyncRestore)
                {
                    await holder.RestoreHistoryAsync(token).ConfigureAwait(false);
                }
                else
                {
                    holder.RestoreHistory(token);
                }
            }).ConfigureAwait(false) ??
                throw new AssertionException("The mirror removal did not fail.");

            if (cleanupFails)
            {
                Assert.That(failure, Is.TypeOf<AggregateException>());
                var aggregate = (AggregateException)failure;
                Assert.That(aggregate.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(aggregate.InnerExceptions[0], Is.SameAs(mirrorFailure));
                Assert.That(aggregate.InnerExceptions[1], Is.SameAs(cleanupFailure));
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                InvalidOperationException resaveFailure = Assert.Throws<InvalidOperationException>(
                    () => holder.SaveHistory(point.Object))!;
                Assert.That(resaveFailure.InnerException, Is.SameAs(cleanupFailure));
            }
            else
            {
                Assert.That(failure, Is.SameAs(mirrorFailure));
                Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
            }
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that a failed mirror removal and a failed release are both reported without retrying disposal.
        /// </summary>
        [Test]
        public void ReleaseHistoryRetainsFailureWhenMirrorRemovalAlsoFails()
        {
            var id = Guid.NewGuid();
            var mirrorFailure = new InvalidOperationException("mirror removal failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id))
                .Throws(mirrorFailure);
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            SessionContinuationPoints holder = NewHolder(store: store.Object);
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            holder.SaveHistory(point.Object);
            holder.BindHistoryGeneration(ToByteString(id), generation);

            AggregateException failure = Assert.Throws<AggregateException>(
                () => holder.ReleaseHistory(ToByteString(id)))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.SameAs(mirrorFailure));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanupFailure));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            Assert.That(holder.ReleaseHistory(ToByteString(id)), Is.False);
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A nonconforming portable store cannot hide a scheduling failure or stop point cleanup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReleasePortableHistoryReportsSchedulingAndDisposalFailuresAsync(bool serviceError)
        {
            var id = Guid.NewGuid();
            Exception schedulingFailure = serviceError
                ? new ServiceResultException(StatusCodes.BadUnexpectedError, "history removal scheduling failed")
                : new InvalidOperationException("history removal scheduling failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            store.Setup(value => value.ScheduleRemove(s_sessionId, id)).Throws(schedulingFailure);
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false);
            holder.BindHistoryGeneration(ToByteString(id), generation);

            AggregateException failure = Assert.Throws<AggregateException>(
                () => holder.ReleaseHistory(ToByteString(id)))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.SameAs(schedulingFailure));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanupFailure));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            holder.Clear();
            store.Verify(value => value.ScheduleRemove(s_sessionId, id), Times.Once);
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that a released continuation with a failed disposal cannot be saved or disposed again.
        /// </summary>
        [Test]
        public void FailedReleasedHistoryCannotBeSavedOrDisposedAgain()
        {
            var id = Guid.NewGuid();
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            SessionContinuationPoints holder = NewHolder();
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            holder.SaveHistory(point.Object);
            holder.BindHistoryGeneration(ToByteString(id), generation);

            InvalidOperationException releaseFailure = Assert.Throws<InvalidOperationException>(
                () => holder.ReleaseHistory(ToByteString(id)))!;
            InvalidOperationException saveFailure = Assert.Throws<InvalidOperationException>(
                () => holder.SaveHistory(point.Object))!;

            Assert.That(releaseFailure, Is.SameAs(cleanupFailure));
            Assert.That(saveFailure.InnerException, Is.SameAs(cleanupFailure));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that a continuation cannot be saved again while its release is disposing it.
        /// </summary>
        [Test]
        public void ReleasedHistoryCannotBeSavedDuringDisposal()
        {
            var id = Guid.NewGuid();
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            SessionContinuationPoints holder = NewHolder();
            point.Setup(value => value.Dispose()).Callback(() =>
            {
                Assert.That(
                    () => holder.SaveHistory(point.Object),
                    Throws.TypeOf<InvalidOperationException>());
            });
            holder.SaveHistory(point.Object);

            Assert.That(holder.ReleaseHistory(ToByteString(id)), Is.True);

            point.Verify(value => value.Dispose(), Times.Once);
            holder.Clear();
        }

        /// <summary>
        /// Verifies that successful disposal is not repeated when only mirror removal fails.
        /// </summary>
        [Test]
        public void ReleaseHistoryDisposesOnceWhenMirrorRemovalFails()
        {
            var id = Guid.NewGuid();
            var mirrorFailure = new InvalidOperationException("mirror removal failed");
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id))
                .Throws(mirrorFailure);
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            SessionContinuationPoints holder = NewHolder(store: store.Object);
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            holder.SaveHistory(point.Object);
            holder.BindHistoryGeneration(ToByteString(id), generation);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => holder.ReleaseHistory(ToByteString(id)))!;

            Assert.That(failure, Is.SameAs(mirrorFailure));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A failed eviction retains the original point and does not retry disposal during session clear.
        /// </summary>
        [Test]
        public void FailedHistoryEvictionRetainsExactPoint()
        {
            var id = Guid.NewGuid();
            var cleanupFailure = new InvalidOperationException("evicted continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            var replacement = new TrackingDisposable(Guid.NewGuid());
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            holder.SaveHistory(point.Object);
            holder.BindHistoryGeneration(ToByteString(id), generation);

            AggregateException failure = Assert.Throws<AggregateException>(() => holder.SaveHistory(replacement))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(failure.InnerExceptions[0], Is.SameAs(cleanupFailure));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            Assert.That(replacement.Disposed, Is.True);
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Admission, mirror rollback, and disposal failures remain observable without retrying a failed point.
        /// </summary>
        [Test]
        public void FailedHistorySaveRetainsAllCleanupFailures()
        {
            var id = Guid.NewGuid();
            var rollbackFailure = new InvalidOperationException("mirror rollback failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id))
                .Throws(rollbackFailure);
            var rejected = new Mock<IHistoryContinuationPoint>();
            rejected.SetupGet(value => value.Id).Returns(id);
            rejected.Setup(value => value.Dispose()).Throws(cleanupFailure);
            SessionContinuationPoints? holder = null;
            store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback(() => holder!.Clear());
            holder = NewHolder(store: store.Object);

            AggregateException failure = Assert.Throws<AggregateException>(
                () => holder.SaveHistory(rejected.Object))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(3));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(failure.InnerExceptions[1], Is.SameAs(rollbackFailure));
            Assert.That(failure.InnerExceptions[2], Is.SameAs(cleanupFailure));
            ServiceResultException repeated = Assert.Throws<ServiceResultException>(
                () => holder.SaveHistory(rejected.Object))!;
            Assert.That(repeated.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            holder.Clear();
            store.Verify(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id),
                Times.Once);
            rejected.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A failed resave cannot reclaim an opaque point already transferred to its caller.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FailedHistoryResavePreservesCallerOwnedPointAsync(bool asyncSave, bool failPersistence)
        {
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            SessionContinuationPoints holder = NewHolder(
                historyStore: failPersistence ? store.Object : null,
                historyCodec: failPersistence ? codec.Object : null);
            var point = new Mock<IHistoryContinuationPoint>();
            Guid id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            ByteString token = ToByteString(id);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            holder.SaveHistory(point.Object);
            holder.BindHistoryGeneration(token, generation);
            IHistoryContinuationPoint? restored = await holder.RestoreHistoryAsync(token).ConfigureAwait(false);
            Assert.That(restored, Is.SameAs(point.Object));
            var saveFailure = new ServiceResultException(StatusCodes.BadInternalError, "portable save failed");
            if (failPersistence)
            {
                store.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask(Task.FromException(saveFailure)));
            }
            else
            {
                holder.Clear();
            }

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (asyncSave)
                {
                    await holder.SaveHistoryAsync(restored!).ConfigureAwait(false);
                }
                else
                {
                    holder.SaveHistory(restored!);
                }
            }).ConfigureAwait(false);

            if (failPersistence)
            {
                Assert.That(failure, Is.SameAs(saveFailure));
                store.Verify(value => value.ScheduleRemove(s_sessionId, id), Times.Once);
            }
            else
            {
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            }
            Assert.That(holder.GetHistoryGeneration(restored!), Is.SameAs(generation.Reference));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            point.Verify(value => value.Dispose(), Times.Never);
            if (failPersistence)
            {
                store.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask));
                await holder.SaveHistoryAsync(restored!).ConfigureAwait(false);
                Assert.That(holder.ReleaseHistory(token), Is.True);
                Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
            }
            else
            {
                holder.RemoveHistoryForGeneration(generation);
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                point.Verify(value => value.Dispose(), Times.Never);
                restored!.Dispose();
            }
            point.Verify(value => value.Dispose(), Times.Once);
            holder.Clear();
        }

        /// <summary>
        /// An eviction failure rolls back a resave without reclaiming the caller's exact point or issuer.
        /// </summary>
        [Test]
        public void FailedHistoryEvictionPreservesCallerOwnedPoint()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            var borrowed = new Mock<IHistoryContinuationPoint>();
            Guid id = Guid.NewGuid();
            borrowed.SetupGet(value => value.Id).Returns(id);
            ByteString token = ToByteString(id);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            holder.SaveHistory(borrowed.Object);
            holder.BindHistoryGeneration(token, generation);
            Assert.That(holder.RestoreHistory(token), Is.SameAs(borrowed.Object));
            var cleanupFailure = new InvalidOperationException("evicted continuation cleanup failed");
            var evicted = new Mock<IHistoryContinuationPoint>();
            evicted.SetupGet(value => value.Id).Returns(Guid.NewGuid());
            evicted.Setup(value => value.Dispose()).Throws(cleanupFailure);
            holder.SaveHistory(evicted.Object);

            AggregateException failure = Assert.Throws<AggregateException>(
                () => holder.SaveHistory(borrowed.Object))!;

            Assert.That(failure.Flatten().InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(failure.Flatten().InnerExceptions[0], Is.SameAs(cleanupFailure));
            Assert.That(holder.GetHistoryGeneration(borrowed.Object), Is.SameAs(generation.Reference));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            borrowed.Verify(value => value.Dispose(), Times.Never);
            holder.SaveHistory(borrowed.Object);
            Assert.That(holder.ReleaseHistory(token), Is.True);
            Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
            holder.Clear();
            borrowed.Verify(value => value.Dispose(), Times.Once);
            evicted.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Retirement rejection attempts every cleanup before releasing the exact session-owned point.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredHistoryReadRetainsAllCleanupFailuresAsync(bool portable)
        {
            Guid id = Guid.NewGuid();
            var mirrorFailure = new InvalidOperationException("mirror removal failed");
            var schedulingFailure = new InvalidOperationException("portable removal scheduling failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            var store = new Mock<IContinuationPointStore>();
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            SessionContinuationPoints holder = NewHolder(
                store: store.Object,
                historyStore: portable ? historyStore.Object : null,
                historyCodec: portable ? codec.Object : null);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            var cleanupOrder = new List<string>();
            store.Setup(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id))
                .Callback(() =>
                {
                    Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                    cleanupOrder.Add("mirror");
                })
                .Throws(mirrorFailure);
            historyStore.Setup(value => value.ScheduleRemove(s_sessionId, id))
                .Callback(() =>
                {
                    Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                    cleanupOrder.Add("portable");
                })
                .Throws(schedulingFailure);
            point.Setup(value => value.Dispose()).Callback(() =>
            {
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                Assert.That(holder.RestoreHistory(ToByteString(id)), Is.Null);
                cleanupOrder.Add("point");
                throw cleanupFailure;
            });
            await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false);
            holder.BindHistoryGeneration(ToByteString(id), generation);
            generation.Gate.Retire();

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            {
                using SessionContinuationPoints.HistoryReadScope scope =
                    await holder.RestoreHistoryReadAsync(ToByteString(id), CancellationToken.None)
                        .ConfigureAwait(false);
            }).ConfigureAwait(false);

            Exception[] expectedFailures = portable
                ? [schedulingFailure, mirrorFailure, cleanupFailure]
                : [mirrorFailure, cleanupFailure];
            Assert.That(failure!.Flatten().InnerExceptions, Is.EqualTo(expectedFailures));
            string[] expectedOrder = portable ? ["portable", "mirror", "point"] : ["mirror", "point"];
            Assert.That(cleanupOrder, Is.EqualTo(expectedOrder));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            Assert.That(holder.RestoreHistory(ToByteString(id)), Is.Null);
            using SessionContinuationPoints.HistoryReadScope repeated =
                await holder.RestoreHistoryReadAsync(ToByteString(id), CancellationToken.None).ConfigureAwait(false);
            Assert.That(repeated.Point, Is.Null);
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
            historyStore.Verify(
                value => value.TryTakeAsync(s_sessionId, id, It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A duplicate save cannot overwrite or remove the mirror of an already saved continuation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateHistorySaveDoesNotModifyExistingMirror(bool sameInstance)
        {
            var id = Guid.NewGuid();
            var store = new Mock<IContinuationPointStore>();
            var original = new TrackingDisposable(id);
            var duplicate = sameInstance ? original : new TrackingDisposable(id);
            SessionContinuationPoints holder = NewHolder(store: store.Object);
            holder.SaveHistory(original);

            Assert.That(() => holder.SaveHistory(duplicate), Throws.InvalidOperationException);
            store.Verify(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()), Times.Once);
            store.Verify(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id),
                Times.Never);
            Assert.That(original.Disposed, Is.False);
            Assert.That(duplicate.Disposed, Is.EqualTo(!sameInstance));

            holder.Clear();
            Assert.That(original.Disposed, Is.True);
        }

        /// <summary>
        /// A concurrent save cannot dispose a point while its synchronous mirror is being written.
        /// </summary>
        [Test]
        public async Task MirroredHistorySaveReservesExactPointDuringStoreAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback(() =>
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(TimeSpan.FromMinutes(1)))
                    {
                        throw new TimeoutException("The test did not release the mirror store.");
                    }
                });
            var point = new TrackingDisposable();
            var duplicate = new TrackingDisposable(point.Id);
            SessionContinuationPoints holder = NewHolder(store: store.Object);
            Task save = Task.Factory.StartNew(
                () => holder.SaveHistory(point),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await holder.SaveHistoryAsync(point).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(point.Disposed, Is.False);
                await Assert.ThatAsync(
                    async () => await holder.SaveHistoryAsync(duplicate).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(duplicate.Disposed, Is.True);
                store.Verify(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()), Times.Once);
                store.Verify(value => value.RemoveContinuationPoint(
                    s_sessionId, ContinuationPointKind.History, point.Id), Times.Never);
            }
            finally
            {
                release.Set();
                await save.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                holder.Clear();
            }
            Assert.That(point.Disposed, Is.True);
        }

        /// <summary>
        /// A load that began before a mirrored save cannot dispose the point reserved by that save.
        /// </summary>
        [Test]
        public async Task MirroredLoadDoesNotDisposePointReservedByConcurrentSaveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var releaseStore = new ManualResetEventSlim();
            var decodeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decoded = new TaskCompletionSource<IHistoryContinuationPoint?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var storeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var point = new Mock<IHistoryContinuationPoint>();
            var id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = s_sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.Empty
            };
            var store = new Mock<IContinuationPointStore>();
            store.Setup(value => value.LoadContinuationPointsAsync(s_sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback(() =>
                {
                    storeEntered.TrySetResult(true);
                    if (!releaseStore.Wait(TimeSpan.FromMinutes(1)))
                    {
                        throw new TimeoutException("The test did not release the mirror store.");
                    }
                });
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            historyStore.Setup(value => value.LoadAsync(s_sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([envelope]);
            codec.Setup(value => value.DecodeAsync(envelope, It.IsAny<CancellationToken>()))
                .Callback(() => decodeEntered.TrySetResult(true))
                .Returns(() => new ValueTask<IHistoryContinuationPoint?>(decoded.Task));
            SessionContinuationPoints holder = NewHolder(
                store: store.Object, historyStore: historyStore.Object, historyCodec: codec.Object);
            Task load = holder.LoadMirroredAsync(s_sessionId).AsTask();
            Task? save = null;
            try
            {
                await decodeEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                save = Task.Factory.StartNew(
                    () => holder.SaveHistory(point.Object),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                await storeEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                decoded.TrySetResult(point.Object);
                await Assert.ThatAsync(
                    async () => await load.ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                point.Verify(value => value.Dispose(), Times.Never);
            }
            finally
            {
                decoded.TrySetResult(point.Object);
                releaseStore.Set();
                try
                {
                    if (save is not null)
                    {
                        await save.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        await load.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException failure)
                    {
                        Assert.That(failure.Message, Is.EqualTo("The history continuation is already being saved."));
                    }
                    finally
                    {
                        holder.Clear();
                    }
                }
            }
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A rejected mirrored load retains its cleanup failure and reports each independent error.
        /// </summary>
        [Test]
        public async Task RejectedMirroredLoadReportsAllCleanupFailuresAsync()
        {
            var id = Guid.NewGuid();
            var schedulingFailure = new NotSupportedException("history removal scheduling failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = s_sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.Empty
            };
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            historyStore.Setup(value => value.LoadAsync(s_sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([envelope]);
            historyStore.Setup(value => value.ScheduleRemove(s_sessionId, id)).Throws(schedulingFailure);
            SessionContinuationPoints holder = NewHolder(historyStore: historyStore.Object, historyCodec: codec.Object);
            codec.Setup(value => value.DecodeAsync(envelope, It.IsAny<CancellationToken>()))
                .Callback(holder.Clear)
                .ReturnsAsync(point.Object);

            AggregateException failure = (await Assert.ThrowsAsync<AggregateException>(
                async () => await holder.LoadMirroredAsync(s_sessionId).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(3));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(failure.InnerExceptions[1], Is.SameAs(schedulingFailure));
            Assert.That(failure.InnerExceptions[2], Is.SameAs(cleanupFailure));
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Rollback addresses the owner captured when the mirror was written, even if the session id changes.
        /// </summary>
        [Test]
        public void FailedHistorySaveRollsBackOriginalMirrorOwner()
        {
            var id = Guid.NewGuid();
            NodeId currentOwner = s_sessionId;
            var store = new Mock<IContinuationPointStore>();
            var point = new TrackingDisposable(id);
            SessionContinuationPoints? holder = null;
            store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback(() =>
                {
                    currentOwner = s_ownerSessionId;
                    holder!.Clear();
                });
            holder = new SessionContinuationPoints(() => currentOwner, 10, 10, store.Object);

            Assert.That(() => holder.SaveHistory(point), Throws.TypeOf<ServiceResultException>());
            store.Verify(value => value.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id),
                Times.Once);
            store.Verify(value => value.RemoveContinuationPoint(s_ownerSessionId, ContinuationPointKind.History, id),
                Times.Never);
            Assert.That(point.Disposed, Is.True);
        }

        /// <summary>
        /// A failed portable save retains its exact failed point and reports independent rollback errors.
        /// </summary>
        [Test]
        public async Task FailedHistoryPersistenceRetainsAllCleanupFailuresAsync()
        {
            var id = Guid.NewGuid();
            var persistenceFailure = new InvalidOperationException("history persistence failed");
            var schedulingFailure = new NotSupportedException("history removal scheduling failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> historyCodec) =
                NewPersistingHistoryMocks();
            historyStore.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(persistenceFailure)));
            historyStore.Setup(value => value.ScheduleRemove(s_sessionId, id)).Throws(schedulingFailure);
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            SessionContinuationPoints holder = NewHolder(
                historyStore: historyStore.Object, historyCodec: historyCodec.Object);

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                async () => await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(3));
            Assert.That(failure.InnerExceptions[0], Is.SameAs(persistenceFailure));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(schedulingFailure));
            Assert.That(failure.InnerExceptions[2], Is.SameAs(cleanupFailure));
            InvalidOperationException? repeated = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(repeated!.InnerException, Is.SameAs(cleanupFailure));
            holder.Clear();
            historyStore.Verify(value => value.ScheduleRemove(s_sessionId, id), Times.Once);
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Closing or retiring an owner defers point disposal until pending persistence exits.
        /// </summary>
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        public async Task PendingHistoryPersistenceRetainsPointUntilCallbackExitsAsync(
            bool closeSession,
            bool blockStore,
            bool encodeEnvelope)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseEncoding = new TaskCompletionSource<HistoryContinuationPointEnvelope?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseStore = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var point = new Mock<IHistoryContinuationPoint>();
            var id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = s_sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.Empty
            };
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            codec.Setup(value => value.EncodeAsync(
                    It.IsAny<NodeId>(), point.Object, It.IsAny<CancellationToken>()))
                .Returns<NodeId, IHistoryContinuationPoint, CancellationToken>((_, _, _) =>
                {
                    if (blockStore)
                    {
                        return new ValueTask<HistoryContinuationPointEnvelope?>(envelope);
                    }
                    entered.TrySetResult(true);
                    return new ValueTask<HistoryContinuationPointEnvelope?>(releaseEncoding.Task);
                });
            store.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns<HistoryContinuationPointEnvelope, CancellationToken>((_, _) =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask(releaseStore.Task);
                });
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            Task save = holder.SaveHistoryAsync(point.Object, timeout.Token).AsTask();
            StatusCode expectedStatus = closeSession
                ? StatusCodes.BadSessionClosed
                : StatusCodes.BadContinuationPointInvalid;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                holder.BindHistoryGeneration(ToByteString(id), generation);
                if (closeSession)
                {
                    holder.Clear();
                    holder.Clear();
                }
                else
                {
                    holder.RemoveHistoryForGeneration(generation);
                    holder.RemoveHistoryForGeneration(generation);
                }
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                point.Verify(value => value.Dispose(), Times.Never);
                Assert.That(save.IsCompleted, Is.False);

                releaseEncoding.TrySetResult(encodeEnvelope ? envelope : null);
                releaseStore.TrySetResult(true);
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await save.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.StatusCode, Is.EqualTo(expectedStatus));
                Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
            }
            finally
            {
                releaseEncoding.TrySetResult(encodeEnvelope ? envelope : null);
                releaseStore.TrySetResult(true);
                try
                {
                    await save.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (ServiceResultException failure) when (failure.StatusCode == expectedStatus)
                {
                }
                finally
                {
                    holder.Clear();
                }
            }

            point.Verify(value => value.Dispose(), Times.Once);
            store.Verify(value => value.StoreAsync(
                It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()),
                blockStore ? Times.Once() : Times.Never());
            store.Verify(value => value.ScheduleRemove(s_sessionId, id),
                blockStore ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// A failed request retains its pending save until persistence exits and then releases it.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public async Task FailedHistoryRequestDrainsPendingSaveAsync(
            bool blockStore,
            bool encodeEnvelope,
            bool cleanupFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseEncoding = new TaskCompletionSource<HistoryContinuationPointEnvelope?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseStore = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupFailure = new InvalidOperationException("Continuation cleanup failed.");
            var point = new Mock<IHistoryContinuationPoint>();
            Guid id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            if (cleanupFails)
            {
                point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            }
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = s_sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.Empty
            };
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            codec.Setup(value => value.EncodeAsync(
                    It.IsAny<NodeId>(), point.Object, It.IsAny<CancellationToken>()))
                .Returns<NodeId, IHistoryContinuationPoint, CancellationToken>((_, _, _) =>
                {
                    if (blockStore)
                    {
                        return new ValueTask<HistoryContinuationPointEnvelope?>(envelope);
                    }
                    entered.TrySetResult(true);
                    return new ValueTask<HistoryContinuationPointEnvelope?>(releaseEncoding.Task);
                });
            store.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns<HistoryContinuationPointEnvelope, CancellationToken>((_, _) =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask(releaseStore.Task);
                });
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            IDisposable request = holder.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            Task save = holder.SaveHistoryAsync(point.Object, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                holder.BindHistoryGeneration(ToByteString(id), generation);
                holder.ReleaseSavedHistory(request);
                request.Dispose();
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                Assert.That(save.IsCompleted, Is.False);
                point.Verify(value => value.Dispose(), Times.Never);

                releaseEncoding.TrySetResult(encodeEnvelope ? envelope : null);
                releaseStore.TrySetResult(true);
                if (cleanupFails)
                {
                    AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                        async () => await save.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
                    Assert.That(failure.InnerExceptions[0], Is.InstanceOf<ServiceResultException>());
                    Assert.That(((ServiceResultException)failure.InnerExceptions[0]).StatusCode,
                        Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                    Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanupFailure));
                }
                else
                {
                    ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await save.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
                }
                Assert.That(holder.HasHistoryForGeneration(generation), Is.EqualTo(cleanupFails));
            }
            finally
            {
                releaseEncoding.TrySetResult(encodeEnvelope ? envelope : null);
                releaseStore.TrySetResult(true);
                try
                {
                    await save.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (ServiceResultException failure)
                    when (failure.StatusCode == StatusCodes.BadContinuationPointInvalid)
                {
                }
                catch (AggregateException failure) when (cleanupFails &&
                    failure.InnerExceptions.Count == 2 &&
                    failure.InnerExceptions[0] is ServiceResultException serviceFailure &&
                    serviceFailure.StatusCode == StatusCodes.BadContinuationPointInvalid &&
                    ReferenceEquals(failure.InnerExceptions[1], cleanupFailure))
                {
                }
                finally
                {
                    request.Dispose();
                    try
                    {
                        holder.Clear();
                    }
                    catch (AggregateException failure) when (cleanupFails &&
                        failure.Flatten().InnerExceptions.Count == 1 &&
                        ReferenceEquals(failure.Flatten().InnerExceptions[0], cleanupFailure))
                    {
                    }
                }
            }

            point.Verify(value => value.Dispose(), Times.Once);
            store.Verify(value => value.StoreAsync(
                It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()),
                blockStore ? Times.Once() : Times.Never());
            store.Verify(value => value.ScheduleRemove(s_sessionId, id),
                blockStore ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// A failed portable claim releases the local point once and retains a disposal failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPortableClaimRetainsFailedPointCleanupAsync(bool cleanupFails)
        {
            var id = Guid.NewGuid();
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            if (cleanupFails)
            {
                point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            }
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            store.Setup(value => value.TryTakeAsync(s_sessionId, id, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false);
            holder.BindHistoryGeneration(ToByteString(id), generation);

            if (cleanupFails)
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await holder.RestoreHistoryAsync(ToByteString(id)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(cleanupFailure));
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                InvalidOperationException? rejected = Assert.Throws<InvalidOperationException>(
                    () => holder.SaveHistory(point.Object));
                Assert.That(rejected!.InnerException, Is.SameAs(cleanupFailure));
            }
            else
            {
                Assert.That(await holder.RestoreHistoryAsync(ToByteString(id)).ConfigureAwait(false), Is.Null);
                Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
            }
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// An invalidated portable claim finishes its store callback before releasing the local point.
        /// </summary>
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        public async Task InvalidatedPortableClaimDoesNotTransferPointAsync(
            bool closeSession,
            bool claimed,
            bool cleanupFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            var id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            if (cleanupFails)
            {
                point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            }
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            store.Setup(value => value.TryTakeAsync(s_sessionId, id, It.IsAny<CancellationToken>()))
                .Returns<NodeId, Guid, CancellationToken>((_, _, _) =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<bool>(result.Task);
                });
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false);
            holder.BindHistoryGeneration(ToByteString(id), generation);
            Task<IHistoryContinuationPoint?> restore = holder.RestoreHistoryAsync(ToByteString(id), timeout.Token)
                .AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (closeSession)
                {
                    holder.Clear();
                    holder.Clear();
                }
                else
                {
                    holder.RemoveHistoryForGeneration(generation);
                    holder.RemoveHistoryForGeneration(generation);
                }
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                Assert.That(restore.IsCompleted, Is.False);
                point.Verify(value => value.Dispose(), Times.Never);
                result.TrySetResult(claimed);

                if (cleanupFails)
                {
                    InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await restore.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(failure, Is.SameAs(cleanupFailure));
                    Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                }
                else
                {
                    Assert.That(await restore.WaitAsync(timeout.Token).ConfigureAwait(false), Is.Null);
                    Assert.That(holder.HasHistoryForGeneration(generation), Is.False);
                }
            }
            finally
            {
                result.TrySetResult(claimed);
                try
                {
                    await restore.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (InvalidOperationException failure) when (ReferenceEquals(failure, cleanupFailure))
                {
                }
                finally
                {
                    holder.Clear();
                }
            }

            point.Verify(value => value.Dispose(), Times.Once);
            store.Verify(value => value.ScheduleRemove(s_sessionId, id), Times.Never);
        }

        /// <summary>
        /// A failed request retains its claimed point until the portable claim finishes.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        public async Task FailedHistoryRequestDrainsPendingClaimAsync(
            bool claimed,
            bool cleanupFails,
            bool claimFails)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ExecutionContext independentContext = ExecutionContext.Capture() ??
                throw new InvalidOperationException("The test requires execution context flow.");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseClaim = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var claimFailure = new NotSupportedException("Portable claim failed.");
            var cleanupFailure = new InvalidOperationException("Continuation cleanup failed.");
            var point = new Mock<IHistoryContinuationPoint>();
            Guid id = Guid.NewGuid();
            point.SetupGet(value => value.Id).Returns(id);
            if (cleanupFails)
            {
                point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            }
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            store.Setup(value => value.TryTakeAsync(s_sessionId, id, It.IsAny<CancellationToken>()))
                .Returns<NodeId, Guid, CancellationToken>((_, _, _) =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<bool>(releaseClaim.Task);
                });
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            IDisposable request = holder.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
            Task<IHistoryContinuationPoint?>? restore = null;
            Exception? observedFailure = null;
            try
            {
                await holder.SaveHistoryAsync(point.Object, timeout.Token).ConfigureAwait(false);
                holder.BindHistoryGeneration(ToByteString(id), generation);
                ExecutionContext.Run(
                    independentContext,
                    _ => restore = holder.RestoreHistoryAsync(ToByteString(id), timeout.Token).AsTask(),
                    null);
                Task<IHistoryContinuationPoint?> pending = restore ??
                    throw new InvalidOperationException("The test did not start the claim.");
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                holder.ReleaseSavedHistory(request);
                request.Dispose();
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
                Assert.That(pending.IsCompleted, Is.False);
                point.Verify(value => value.Dispose(), Times.Never);

                if (claimFails)
                {
                    releaseClaim.TrySetException(claimFailure);
                }
                else
                {
                    releaseClaim.TrySetResult(claimed);
                }
                if (claimFails && cleanupFails)
                {
                    AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                        async () => await pending.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(
                        failure!.Flatten().InnerExceptions,
                        Is.EquivalentTo(new Exception[] { claimFailure, cleanupFailure }));
                    observedFailure = failure;
                }
                else if (claimFails)
                {
                    NotSupportedException? failure = await Assert.ThrowsAsync<NotSupportedException>(
                        async () => await pending.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(failure, Is.SameAs(claimFailure));
                    observedFailure = failure;
                }
                else if (cleanupFails)
                {
                    InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await pending.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(failure, Is.SameAs(cleanupFailure));
                    observedFailure = failure;
                }
                else
                {
                    Assert.That(await pending.WaitAsync(timeout.Token).ConfigureAwait(false), Is.Null);
                }
                Assert.That(holder.HasHistoryForGeneration(generation), Is.EqualTo(cleanupFails));
            }
            finally
            {
                if (claimFails)
                {
                    releaseClaim.TrySetException(claimFailure);
                }
                else
                {
                    releaseClaim.TrySetResult(claimed);
                }
                try
                {
                    if (restore is not null)
                    {
                        IHistoryContinuationPoint? escaped =
                            await restore.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        escaped?.Dispose();
                    }
                }
                catch (Exception failure) when (ReferenceEquals(failure, observedFailure))
                {
                }
                finally
                {
                    request.Dispose();
                    try
                    {
                        holder.Clear();
                    }
                    catch (AggregateException failure) when (cleanupFails &&
                        failure.Flatten().InnerExceptions.Count == 1 &&
                        ReferenceEquals(failure.Flatten().InnerExceptions[0], cleanupFailure))
                    {
                    }
                }
            }

            point.Verify(value => value.Dispose(), Times.Once);
            store.Verify(value => value.ScheduleRemove(s_sessionId, id),
                claimFails ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// A failed claim and failed invalidated-point cleanup both remain observable.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedInvalidatedClaimPreservesClaimAndCleanupFailuresAsync(bool closeSession)
        {
            var id = Guid.NewGuid();
            var claimFailure = new NotSupportedException("portable claim failed");
            var cleanupFailure = new InvalidOperationException("continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            SessionContinuationPoints holder = NewHolder(historyStore: store.Object, historyCodec: codec.Object);
            NodeManagerGeneration generation =
                NodeManagerGenerationRegistry.GetOrCreate(NewNodeManager(Mock.Of<INodeManager>()));
            await holder.SaveHistoryAsync(point.Object).ConfigureAwait(false);
            holder.BindHistoryGeneration(ToByteString(id), generation);
            store.Setup(value => value.TryTakeAsync(s_sessionId, id, It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    if (closeSession)
                    {
                        holder.Clear();
                    }
                    else
                    {
                        holder.RemoveHistoryForGeneration(generation);
                    }
                })
                .Returns(() => new ValueTask<bool>(Task.FromException<bool>(claimFailure)));

            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                async () => await holder.RestoreHistoryAsync(ToByteString(id)).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(
                failure!.Flatten().InnerExceptions,
                Is.EquivalentTo(new Exception[] { claimFailure, cleanupFailure }));
            Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A failed request-saved point remains owned instead of being forgotten or disposed again.
        /// </summary>
        [Test]
        public void FailedSavedHistoryCleanupRetainsExactPoint()
        {
            var id = Guid.NewGuid();
            var cleanupFailure = new InvalidOperationException("request continuation cleanup failed");
            var point = new Mock<IHistoryContinuationPoint>();
            point.SetupGet(value => value.Id).Returns(id);
            point.Setup(value => value.Dispose()).Throws(cleanupFailure);
            SessionContinuationPoints holder = NewHolder();
            IAsyncNodeManager manager = NewNodeManager(Mock.Of<INodeManager>());
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
            using (IDisposable request = holder.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty))
            {
                holder.SaveHistory(point.Object);
                holder.BindHistoryGeneration(ToByteString(id), generation);

                AggregateException failure = Assert.Throws<AggregateException>(
                    () => holder.ReleaseSavedHistory(request))!;

                Assert.That(failure.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(failure.InnerExceptions[0], Is.SameAs(cleanupFailure));
                Assert.That(holder.HasHistoryForGeneration(generation), Is.True);
            }
            holder.Clear();
            point.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that asynchronous history saving evicts the oldest point and schedules its durable removal.
        /// </summary>
        [Test]
        public async Task SaveHistoryAsyncEvictsOldestAndSchedulesRemovalAsync()
        {
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> historyCodec) =
                NewPersistingHistoryMocks();
            SessionContinuationPoints holder = NewHolder(
                maxHistory: 1,
                historyStore: historyStore.Object,
                historyCodec: historyCodec.Object);

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var evicted = new TrackingDisposable(id1);

            await holder.SaveHistoryAsync(evicted).ConfigureAwait(false);
            await holder.SaveHistoryAsync(new TrackingDisposable(id2)).ConfigureAwait(false);

            Assert.That(evicted.Disposed, Is.True);
            Assert.That(holder.RestoreHistory(ToByteString(id1)), Is.Null);
            Assert.That(
                await holder.RestoreHistoryAsync(ToByteString(id2)).ConfigureAwait(false),
                Is.Not.Null);
            historyStore.Verify(
                s => s.ScheduleRemove(s_sessionId, id1),
                Times.Once);
        }

        /// <summary>
        /// Verifies that asynchronous history saving persists a continuation envelope.
        /// </summary>
        [Test]
        public async Task SaveHistoryAsyncStoresEnvelopeAsync()
        {
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> historyCodec) =
                NewPersistingHistoryMocks();
            HistoryContinuationPointEnvelope? captured = null;
            historyStore
                .Setup(s => s.StoreAsync(It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Callback<HistoryContinuationPointEnvelope, CancellationToken>((envelope, _) => captured = envelope)
                .Returns(default(ValueTask));

            SessionContinuationPoints holder = NewHolder(
                historyStore: historyStore.Object, historyCodec: historyCodec.Object);
            var id = Guid.NewGuid();

            await holder.SaveHistoryAsync(new TrackingDisposable(id)).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Id, Is.EqualTo(id));
            Assert.That(captured.OwnerSessionId, Is.EqualTo(s_sessionId));
        }

        /// <summary>
        /// Verifies that history restoration returns null before any continuation is saved.
        /// </summary>
        [Test]
        public void RestoreHistoryReturnsNullBeforeAnySave()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.That(holder.RestoreHistory(ToByteString(Guid.NewGuid())), Is.Null);
        }

        /// <summary>
        /// Verifies that history restoration returns null for an identifier with the wrong length.
        /// </summary>
        [Test]
        public void RestoreHistoryReturnsNullForWrongLength()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveHistory(new TrackingDisposable(Guid.NewGuid()));

            Assert.That(holder.RestoreHistory(new ByteString("\t"u8.ToArray())), Is.Null);
        }

        /// <summary>
        /// Verifies that history restoration returns null for an unknown continuation.
        /// </summary>
        [Test]
        public void RestoreHistoryReturnsNullWhenNotFound()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveHistory(new TrackingDisposable(Guid.NewGuid()));

            Assert.That(holder.RestoreHistory(ToByteString(Guid.NewGuid())), Is.Null);
        }

        /// <summary>
        /// Verifies that mirrored continuation loading performs no work without a backing store.
        /// </summary>
        [Test]
        public async Task LoadMirroredAsyncReturnsEarlyWithoutStoreAsync()
        {
            SessionContinuationPoints holder = NewHolder();

            await holder.LoadMirroredAsync(s_ownerSessionId).ConfigureAwait(false);

            // Nothing was mirrored, so a subsequent restore still misses.
            holder.SaveBrowse(NewBrowsePoint());
            Assert.That(holder.RestoreBrowse(ToByteString(Guid.NewGuid())), Is.Null);
        }

        /// <summary>
        /// Verifies that mirrored continuation loading performs no work for a null owner.
        /// </summary>
        [Test]
        public async Task LoadMirroredAsyncReturnsEarlyForNullOwnerAsync()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(store: store.Object);

            await holder.LoadMirroredAsync(NodeId.Null).ConfigureAwait(false);

            store.Verify(
                s => s.LoadContinuationPointsAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that mirrored loading consumes both browse and history continuation envelopes.
        /// </summary>
        [Test]
        public async Task LoadMirroredAsyncConsumesMirroredBrowseAndHistoryAsync()
        {
            var browseId = Guid.NewGuid();
            var historyId = Guid.NewGuid();
            ArrayOf<ContinuationPointEnvelope> envelopes =
            [
                new ContinuationPointEnvelope
                {
                    Id = browseId,
                    OwnerSessionId = s_ownerSessionId,
                    Kind = ContinuationPointKind.Browse
                },
                new ContinuationPointEnvelope
                {
                    Id = historyId,
                    OwnerSessionId = s_ownerSessionId,
                    Kind = ContinuationPointKind.History
                }
            ];

            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            store
                .Setup(s => s.LoadContinuationPointsAsync(s_ownerSessionId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ContinuationPointEnvelope>>(envelopes));

            SessionContinuationPoints holder = NewHolder(store: store.Object);
            await holder.LoadMirroredAsync(s_ownerSessionId).ConfigureAwait(false);

            // Populate the local lists so restore reaches the mirrored-owner lookup.
            holder.SaveBrowse(NewBrowsePoint());
            holder.SaveHistory(new TrackingDisposable(Guid.NewGuid()));

            Assert.That(holder.RestoreBrowse(ToByteString(browseId)), Is.Null);
            Assert.That(holder.RestoreHistory(ToByteString(historyId)), Is.Null);
            store.Verify(
                s => s.RemoveContinuationPoint(s_ownerSessionId, ContinuationPointKind.Browse, browseId),
                Times.Once);
            store.Verify(
                s => s.RemoveContinuationPoint(s_ownerSessionId, ContinuationPointKind.History, historyId),
                Times.Once);
        }

        /// <summary>
        /// Verifies that clearing mirrored-only continuation points removes the records under their original owners.
        /// </summary>
        [Test]
        public async Task ClearRemovesMirroredOnlyPointsUsingOriginalOwnersAsync()
        {
            var originalOwner = new NodeId("original-owner", 1);
            var browseId = Guid.NewGuid();
            var historyId = Guid.NewGuid();
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            store
                .Setup(s => s.LoadContinuationPointsAsync(originalOwner, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ContinuationPointEnvelope>>(
                [
                    new ContinuationPointEnvelope
                    {
                        Id = browseId,
                        OwnerSessionId = originalOwner,
                        Kind = ContinuationPointKind.Browse
                    },
                    new ContinuationPointEnvelope
                    {
                        Id = historyId,
                        OwnerSessionId = originalOwner,
                        Kind = ContinuationPointKind.History
                    }
                ]));

            SessionContinuationPoints holder = NewHolder(store: store.Object);
            await holder.LoadMirroredAsync(originalOwner).ConfigureAwait(false);
            holder.Clear();
            holder.Clear();

            store.Verify(
                s => s.RemoveContinuationPoint(
                    originalOwner,
                    ContinuationPointKind.Browse,
                    browseId),
                Times.Once);
            store.Verify(
                s => s.RemoveContinuationPoint(
                    originalOwner,
                    ContinuationPointKind.History,
                    historyId),
                Times.Once);
        }

        /// <summary>
        /// Verifies that a mirrored load completing after close cleans up instead of repopulating owner maps.
        /// </summary>
        [Test]
        public async Task LateMirroredLoadAfterClearCleansUpOriginalOwnersAsync()
        {
            var originalOwner = new NodeId("late-owner", 1);
            var browseId = Guid.NewGuid();
            var loadStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLoad = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            store
                .Setup(s => s.LoadContinuationPointsAsync(originalOwner, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    loadStarted.TrySetResult(true);
                    await releaseLoad.Task.ConfigureAwait(false);
                    return new ArrayOf<ContinuationPointEnvelope>(
                        new[]
                        {
                            new ContinuationPointEnvelope
                            {
                                Id = browseId,
                                OwnerSessionId = originalOwner,
                                Kind = ContinuationPointKind.Browse
                            }
                        });
                });

            SessionContinuationPoints holder = NewHolder(store: store.Object);
            Task load = holder.LoadMirroredAsync(originalOwner).AsTask();
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            holder.Clear();
            releaseLoad.TrySetResult(true);
            await load.ConfigureAwait(false);
            holder.Clear();

            store.Verify(
                s => s.RemoveContinuationPoint(
                    originalOwner,
                    ContinuationPointKind.Browse,
                    browseId),
                Times.Once);
        }

        /// <summary>
        /// Verifies that clearing continuation storage disposes browse and history points.
        /// </summary>
        [Test]
        public void ClearDisposesBrowseAndHistoryPoints()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(store: store.Object);

            var browseData = new TrackingDisposable(Guid.NewGuid());
            var historyId = Guid.NewGuid();
            var historyValue = new TrackingDisposable(historyId);
            ContinuationPoint cp = NewBrowsePoint(data: browseData);
            holder.SaveBrowse(cp);
            holder.SaveHistory(historyValue);

            holder.Clear();

            Assert.That(browseData.Disposed, Is.True);
            Assert.That(historyValue.Disposed, Is.True);
            Assert.That(holder.RestoreBrowse(ToByteString(cp.Id)), Is.Null);
            Assert.That(holder.RestoreHistory(ToByteString(historyId)), Is.Null);
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.Browse, cp.Id),
                Times.Once);
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, historyId),
                Times.Once);
        }

        /// <summary>
        /// Verifies that clearing empty continuation storage does not throw.
        /// </summary>
        [Test]
        public void ClearWithoutPointsDoesNotThrow()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.DoesNotThrow(holder.Clear);
        }

        /// <summary>
        /// Verifies that history cleanup rejects a null node manager.
        /// </summary>
        [Test]
        public void RemoveHistoryForManagerThrowsOnNullNodeManager()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.RemoveHistoryForManager(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        /// <summary>
        /// Verifies that history cleanup without saved points has no effect.
        /// </summary>
        [Test]
        public void RemoveHistoryForManagerIsNoOpWhenNoHistoryPointsSaved()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.DoesNotThrow(
                () => holder.RemoveHistoryForManager(NewNodeManager(Mock.Of<INodeManager>())));
        }

        /// <summary>
        /// Verifies that manager-specific cleanup preserves history continuations not attributable to that manager.
        /// </summary>
        [Test]
        public void RemoveHistoryForManagerLeavesPointsOfOtherOwners()
        {
            SessionContinuationPoints holder = NewHolder();
            var id = Guid.NewGuid();

            // A continuation point that does not record a provider cannot be attributed to a
            // NodeManager, so it is left alone rather than dropped on a guess.
            holder.SaveHistory(new TrackingDisposable(id));

            holder.RemoveHistoryForManager(NewNodeManager(Mock.Of<INodeManager>()));

            Assert.That(holder.RestoreHistory(ToByteString(id)), Is.Not.Null);
        }

        /// <summary>
        /// Verifies that a HistoryRead request that needs more continuation points than the
        /// limit gets Bad_NoContinuationPoints for the overflow instead of evicting a point it
        /// created itself (Part 4 §7.9), and that the points become evictable once the request ends.
        /// </summary>
        [Test]
        public void HistoryRequestDoesNotEvictItsOwnContinuationPoints()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 2);
            var first = new TrackingDisposable();
            var second = new TrackingDisposable();
            var third = new TrackingDisposable();

            using (holder.BeginHistoryRequest([new HistoryReadValueId(), new HistoryReadValueId(), new HistoryReadValueId()]))
            {
                holder.SaveHistory(first);
                holder.SaveHistory(second);
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => holder.SaveHistory(third))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
            }

            Assert.That(first.Disposed, Is.False);
            Assert.That(second.Disposed, Is.False);
            Assert.That(third.Disposed, Is.True);

            // a later request may free the points of this (now prior) request.
            var next = new TrackingDisposable();
            holder.SaveHistory(next);
            Assert.That(first.Disposed, Is.True);
            Assert.That(holder.RestoreHistory(ToByteString(second.Id)), Is.SameAs(second));
            Assert.That(holder.RestoreHistory(ToByteString(next.Id)), Is.SameAs(next));
        }

        /// <summary>
        /// Verifies that a new read in a HistoryRead request cannot evict a point that a later
        /// operation of the same request continues: continuing a halted operation never fails for
        /// lack of continuation points, so the new read gets Bad_NoContinuationPoints (Part 4 §7.9).
        /// </summary>
        [Test]
        public void HistoryRequestKeepsTheContinuedPointFromEviction()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            var held = new TrackingDisposable();
            holder.SaveHistory(held);
            var newRead = new TrackingDisposable();

            using (holder.BeginHistoryRequest(
            [
                new HistoryReadValueId(),
                new HistoryReadValueId { ContinuationPoint = ToByteString(held.Id) }
            ]))
            {
                ServiceResultException ex = Assert.Throws<ServiceResultException>(
                    () => holder.SaveHistory(newRead))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
                Assert.That(newRead.Disposed, Is.True);

                Assert.That(holder.RestoreHistory(ToByteString(held.Id)), Is.SameAs(held));
                Assert.That(held.Disposed, Is.False);
            }
        }

        /// <summary>
        /// Verifies that an in-flight HistoryRead does not keep the points of another, already
        /// completed request of the session from being freed: at the limit a new request evicts
        /// the prior request's point instead of failing with Bad_NoContinuationPoints (Part 4 §7.9).
        /// </summary>
        [Test]
        public async Task InFlightHistoryRequestDoesNotPinPointsOfCompletedRequestsAsync()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            var completedRead = new TrackingDisposable();
            var newRead = new TrackingDisposable();

            // request A stays in flight on its own asynchronous flow (e.g. a slow historian).
            IDisposable slowRequest = await Task.Run(
                () => holder.BeginHistoryRequest([new HistoryReadValueId()])).ConfigureAwait(false);
            try
            {
                // request B saves a point and returns its response.
                await Task.Run(() =>
                {
                    using (holder.BeginHistoryRequest([new HistoryReadValueId()]))
                    {
                        holder.SaveHistory(completedRead);
                    }
                }).ConfigureAwait(false);

                // request C needs a slot: B's point is from a prior request and is freed.
                await Task.Run(() =>
                {
                    using (holder.BeginHistoryRequest([new HistoryReadValueId()]))
                    {
                        holder.SaveHistory(newRead);
                    }
                }).ConfigureAwait(false);

                Assert.That(completedRead.Disposed, Is.True);
                Assert.That(newRead.Disposed, Is.False);
                Assert.That(holder.RestoreHistory(ToByteString(newRead.Id)), Is.SameAs(newRead));
            }
            finally
            {
                slowRequest.Dispose();
            }
        }

        /// <summary>
        /// Verifies that a point saved after its HistoryRead has returned (e.g. by background
        /// work of a node manager) is not pinned by the ended request and stays evictable.
        /// </summary>
        [Test]
        public async Task SaveAfterHistoryRequestEndedIsNotPinnedAsync()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            var lateSave = new TrackingDisposable();
            var next = new TrackingDisposable();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task background;

            using (holder.BeginHistoryRequest([new HistoryReadValueId()]))
            {
                // the background work captures the request's execution context.
                background = Task.Run(async () =>
                {
                    await release.Task.ConfigureAwait(false);
                    holder.SaveHistory(lateSave);
                });
            }

            release.SetResult(true);
            await background.ConfigureAwait(false);

            holder.SaveHistory(next);
            Assert.That(lateSave.Disposed, Is.True);
            Assert.That(holder.RestoreHistory(ToByteString(next.Id)), Is.SameAs(next));
        }

        /// <summary>
        /// Verifies that a HistoryRead request which restored a point to continue an operation
        /// keeps its slot until it saves the successor: a concurrent request cannot take it, so
        /// continuing the halted operation does not fail with Bad_NoContinuationPoints (Part 4 §7.9).
        /// </summary>
        [Test]
        public async Task ContinuedHistoryOperationKeepsItsSlotFromConcurrentRequestAsync()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            var held = new TrackingDisposable();
            holder.SaveHistory(held);
            var successor = new TrackingDisposable();
            var concurrent = new TrackingDisposable();
            var restored = new TaskCompletionSource<IHistoryContinuationPoint?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var concurrentSaved = new TaskCompletionSource<StatusCode>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var successorSaved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // request A continues the held point and awaits its historian before saving the successor.
            Task<StatusCode> requestA = Task.Run(async () =>
            {
                try
                {
                    using (holder.BeginHistoryRequest(
                        [new HistoryReadValueId { ContinuationPoint = ToByteString(held.Id) }]))
                    {
                        restored.SetResult(holder.RestoreHistory(ToByteString(held.Id)));
                        _ = await concurrentSaved.Task.ConfigureAwait(false);
                        holder.SaveHistory(successor);
                        return (StatusCode)StatusCodes.Good;
                    }
                }
                catch (ServiceResultException e)
                {
                    return e.StatusCode;
                }
                finally
                {
                    successorSaved.TrySetResult(true);
                }
            });

            Assert.That(await restored.Task.ConfigureAwait(false), Is.SameAs(held));

            // request B starts a new read while A is in flight and stays in flight itself.
            Task requestB = Task.Run(async () =>
            {
                using (holder.BeginHistoryRequest([new HistoryReadValueId()]))
                {
                    try
                    {
                        holder.SaveHistory(concurrent);
                        concurrentSaved.SetResult(StatusCodes.Good);
                    }
                    catch (ServiceResultException e)
                    {
                        concurrentSaved.SetResult(e.StatusCode);
                    }
                    await successorSaved.Task.ConfigureAwait(false);
                }
            });

            StatusCode resultA = await requestA.ConfigureAwait(false);
            await requestB.ConfigureAwait(false);
            StatusCode concurrentResult = await concurrentSaved.Task.ConfigureAwait(false);

            Assert.That(resultA, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(concurrentResult, Is.EqualTo((StatusCode)StatusCodes.BadNoContinuationPoints));
            Assert.That(concurrent.Disposed, Is.True);
            Assert.That(successor.Disposed, Is.False);
            Assert.That(holder.RestoreHistory(ToByteString(successor.Id)), Is.SameAs(successor));
        }

        /// <summary>
        /// Verifies that a successor which consumed the reserved slot of a continued operation
        /// gives the reservation back when persisting it fails: a concurrent request cannot take
        /// the slot before the request restores the continuation it claimed (Part 4 §7.9).
        /// </summary>
        [Test]
        public async Task FailedSuccessorPersistenceKeepsTheReservedSlotForTheRestorationAsync()
        {
            var held = new TrackingDisposable();
            var successor = new TrackingDisposable();
            var concurrent = new TrackingDisposable();
            (Mock<IHistoryContinuationPointStore> historyStore, Mock<IHistoryContinuationPointCodec> historyCodec) =
                NewPersistingHistoryMocks();
            historyStore
                .Setup(s => s.StoreAsync(
                    It.Is<HistoryContinuationPointEnvelope>(e => e.Id == successor.Id),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(
                    new ServiceResultException(StatusCodes.BadInternalError, "store failed"))));
            SessionContinuationPoints holder = NewHolder(
                maxHistory: 1,
                historyStore: historyStore.Object,
                historyCodec: historyCodec.Object);
            holder.SaveHistory(held);

            var successorFailed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var concurrentSaved = new TaskCompletionSource<StatusCode>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var restorationSaved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // request A continues the held point; persisting its successor fails, so it puts
            // the claimed continuation back, as HistorianContinuationClaim.DisposeAsync does.
            Task<StatusCode> requestA = Task.Run(async () =>
            {
                try
                {
                    using (holder.BeginHistoryRequest(
                        [new HistoryReadValueId { ContinuationPoint = ToByteString(held.Id) }]))
                    {
                        Assert.That(holder.RestoreHistory(ToByteString(held.Id)), Is.SameAs(held));
                        Assert.That(
                            async () => await holder.SaveHistoryAsync(successor).ConfigureAwait(false),
                            Throws.TypeOf<ServiceResultException>());
                        successorFailed.SetResult(true);
                        _ = await concurrentSaved.Task.ConfigureAwait(false);
                        await holder.SaveHistoryAsync(held).ConfigureAwait(false);
                        return (StatusCode)StatusCodes.Good;
                    }
                }
                catch (ServiceResultException e)
                {
                    return e.StatusCode;
                }
                finally
                {
                    successorFailed.TrySetResult(false);
                    restorationSaved.TrySetResult(true);
                }
            });

            // request B saves a point between the failed persistence and the restoration and
            // stays in flight, so its point is pinned.
            Task requestB = Task.Run(async () =>
            {
                _ = await successorFailed.Task.ConfigureAwait(false);
                using (holder.BeginHistoryRequest([new HistoryReadValueId()]))
                {
                    try
                    {
                        holder.SaveHistory(concurrent);
                        concurrentSaved.SetResult(StatusCodes.Good);
                    }
                    catch (ServiceResultException e)
                    {
                        concurrentSaved.SetResult(e.StatusCode);
                    }
                    await restorationSaved.Task.ConfigureAwait(false);
                }
            });

            StatusCode resultA = await requestA.ConfigureAwait(false);
            await requestB.ConfigureAwait(false);
            StatusCode concurrentResult = await concurrentSaved.Task.ConfigureAwait(false);

            Assert.That(resultA, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(concurrentResult, Is.EqualTo((StatusCode)StatusCodes.BadNoContinuationPoints));
            Assert.That(successor.Disposed, Is.True);
            Assert.That(concurrent.Disposed, Is.True);
            Assert.That(held.Disposed, Is.False);
            Assert.That(
                await holder.RestoreHistoryAsync(ToByteString(held.Id)).ConfigureAwait(false),
                Is.SameAs(held));
        }

        /// <summary>
        /// Closing an earlier request does not clear a later request's reservation on the same entry.
        /// </summary>
        [Test]
        public async Task ClosingEarlierRequestPreservesLaterHistoryReservationAsync()
        {
            var held = new TrackingDisposable();
            var competing = new TrackingDisposable();
            var storeFailure = new ServiceResultException(StatusCodes.BadInternalError, "store failed");
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            var firstSaved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var storeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishStore = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Callback(() => storeEntered.TrySetResult(true))
                .Returns(() => new ValueTask(finishStore.Task));
            SessionContinuationPoints holder = NewHolder(
                maxHistory: 1, historyStore: store.Object, historyCodec: codec.Object);
            ByteString token = ToByteString(held.Id);
            holder.SaveHistory(held);

            Task first = Task.Run(async () =>
            {
                using IDisposable request = holder.BeginHistoryRequest(
                    [new HistoryReadValueId { ContinuationPoint = token }]);
                Assert.That(holder.RestoreHistory(token), Is.SameAs(held));
                holder.SaveHistory(held);
                firstSaved.TrySetResult(true);
                await finishFirst.Task.ConfigureAwait(false);
            });
            Task second = Task.CompletedTask;
            try
            {
                await firstSaved.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                second = Task.Run(async () =>
                {
                    using IDisposable request = holder.BeginHistoryRequest(
                        [new HistoryReadValueId { ContinuationPoint = token }]);
                    Assert.That(holder.RestoreHistory(token), Is.SameAs(held));
                    ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await holder.SaveHistoryAsync(held).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(failure, Is.SameAs(storeFailure));
                    secondFailed.TrySetResult(true);
                    await finishSecond.Task.ConfigureAwait(false);
                });
                await storeEntered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                finishFirst.TrySetResult(true);
                await first.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                finishStore.TrySetException(storeFailure);
                await secondFailed.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                ServiceResultException failure = Assert.Throws<ServiceResultException>(
                    () => holder.SaveHistory(competing))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
                Assert.That(competing.Disposed, Is.True);
            }
            finally
            {
                finishFirst.TrySetResult(true);
                finishStore.TrySetResult(false);
                finishSecond.TrySetResult(true);
                try
                {
                    await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                finally
                {
                    holder.Clear();
                }
            }
        }

        /// <summary>
        /// A later unreserved save cannot restore the reservation consumed by an earlier save.
        /// </summary>
        [Test]
        public async Task FailedUnreservedResaveDoesNotRestoreEarlierReservationAsync()
        {
            var held = new TrackingDisposable();
            var competing = new TrackingDisposable();
            var storeFailure = new ServiceResultException(StatusCodes.BadInternalError, "store failed");
            (Mock<IHistoryContinuationPointStore> store, Mock<IHistoryContinuationPointCodec> codec) =
                NewPersistingHistoryMocks();
            store.Setup(value => value.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(Task.FromException(storeFailure)));
            SessionContinuationPoints holder = NewHolder(
                maxHistory: 1, historyStore: store.Object, historyCodec: codec.Object);
            ByteString token = ToByteString(held.Id);
            holder.SaveHistory(held);
            var firstSaved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task first = Task.Run(async () =>
            {
                using IDisposable request = holder.BeginHistoryRequest(
                    [new HistoryReadValueId { ContinuationPoint = token }]);
                Assert.That(holder.RestoreHistory(token), Is.SameAs(held));
                holder.SaveHistory(held);
                firstSaved.TrySetResult(true);
                await finishFirst.Task.ConfigureAwait(false);
            });
            try
            {
                await firstSaved.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await Task.Run(async () =>
                {
                    using IDisposable request = holder.BeginHistoryRequest(ArrayOf<HistoryReadValueId>.Empty);
                    Assert.That(holder.RestoreHistory(token), Is.SameAs(held));
                    ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await holder.SaveHistoryAsync(held).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(failure, Is.SameAs(storeFailure));
                }).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                holder.SaveHistory(competing);
                Assert.That(competing.Disposed, Is.False);
                Assert.That(holder.ReleaseHistory(ToByteString(competing.Id)), Is.True);
                Assert.That(competing.Disposed, Is.True);
            }
            finally
            {
                finishFirst.TrySetResult(true);
                try
                {
                    await first.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                finally
                {
                    holder.Clear();
                }
            }
        }

        /// <summary>
        /// Verifies that the slot reserved for a continued operation is released when its
        /// request ends without saving a successor (the operation completed).
        /// </summary>
        [Test]
        public void ReservationForContinuedOperationIsReleasedWhenRequestEnds()
        {
            SessionContinuationPoints holder = NewHolder(maxHistory: 1);
            var held = new TrackingDisposable();
            holder.SaveHistory(held);

            using (holder.BeginHistoryRequest(
                [new HistoryReadValueId { ContinuationPoint = ToByteString(held.Id) }]))
            {
                Assert.That(holder.RestoreHistory(ToByteString(held.Id)), Is.SameAs(held));
            }

            var next = new TrackingDisposable();
            holder.SaveHistory(next);
            Assert.That(next.Disposed, Is.False);
            Assert.That(holder.RestoreHistory(ToByteString(next.Id)), Is.SameAs(next));
        }

        private static SessionContinuationPoints NewHolder(
            int maxBrowse = 10,
            int maxHistory = 10,
            IContinuationPointStore? store = null,
            IHistoryContinuationPointStore? historyStore = null,
            IHistoryContinuationPointCodec? historyCodec = null)
        {
            return new SessionContinuationPoints(
                () => s_sessionId,
                maxBrowse,
                maxHistory,
                store,
                historyStore,
                historyCodec,
                new NamespaceTable());
        }

        /// <summary>
        /// Creates a loose <see cref="IHistoryContinuationPointStore"/>/<see cref="IHistoryContinuationPointCodec"/>
        /// pair that round-trips a history continuation point through a no-op envelope, so
        /// <see cref="SessionContinuationPoints.SaveHistoryAsync"/> completes the point as portable.
        /// </summary>
        private static (Mock<IHistoryContinuationPointStore> Store, Mock<IHistoryContinuationPointCodec> Codec)
            NewPersistingHistoryMocks()
        {
            var historyCodec = new Mock<IHistoryContinuationPointCodec>(MockBehavior.Loose);
            historyCodec
                .Setup(c => c.EncodeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<IHistoryContinuationPoint>(),
                    It.IsAny<CancellationToken>()))
                .Returns<NodeId, IHistoryContinuationPoint, CancellationToken>(
                    (ownerSessionId, continuationPoint, _) =>
                        new ValueTask<HistoryContinuationPointEnvelope?>(
                            new HistoryContinuationPointEnvelope
                            {
                                Id = continuationPoint.Id,
                                OwnerSessionId = ownerSessionId,
                                CodecId = "test",
                                CodecVersion = 1,
                                Payload = ByteString.Empty
                            }));

            var historyStore = new Mock<IHistoryContinuationPointStore>(MockBehavior.Loose);
            historyStore
                .Setup(s => s.StoreAsync(
                    It.IsAny<HistoryContinuationPointEnvelope>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            historyStore
                .Setup(s => s.TryTakeAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));

            return (historyStore, historyCodec);
        }

        private static IAsyncNodeManager NewNodeManager(INodeManager syncNodeManager)
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.Setup(m => m.SyncNodeManager).Returns(syncNodeManager);
            return nodeManager.Object;
        }

        private static ContinuationPoint NewBrowsePoint(IDisposable? data = null)
        {
            return new ContinuationPoint
            {
                Id = Guid.NewGuid(),
                Data = data
            };
        }

        private static ByteString ToByteString(Guid id)
        {
            return new ByteString(id.ToByteArray());
        }

        private sealed class TrackingDisposable : IHistoryContinuationPoint
        {
            public TrackingDisposable(Guid? id = null)
            {
                Id = id ?? Guid.NewGuid();
            }

            public Guid Id { get; }

            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
