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
        /// Verifies that browse cleanup matches the underlying synchronous manager across different adapter instances.
        /// </summary>
        [Test]
        public void RemoveBrowseForManagerMatchesBySyncNodeManagerWhenManagerInstancesDiffer()
        {
            SessionContinuationPoints holder = NewHolder();
            INodeManager sharedSyncManager = new Mock<INodeManager>().Object;
            IAsyncNodeManager creatingManager = NewNodeManager(sharedSyncManager);
            IAsyncNodeManager lookupManager = NewNodeManager(sharedSyncManager);

            ContinuationPoint cp = NewBrowsePoint();
            cp.Manager = creatingManager;
            holder.SaveBrowse(cp);

            holder.RemoveBrowseForManager(lookupManager);

            Assert.That(holder.RestoreBrowse(ToByteString(cp.Id)), Is.Null);
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
