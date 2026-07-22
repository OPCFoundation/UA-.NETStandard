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

#nullable enable
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

        [Test]
        public void ConstructorThrowsWhenSessionIdProviderNull()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new SessionContinuationPoints(null!, 2, 2, null))!;
            Assert.That(ex.ParamName, Is.EqualTo("sessionIdProvider"));
        }

        [Test]
        public void MaxBrowseIsConfigurable()
        {
            SessionContinuationPoints holder = NewHolder(maxBrowse: 3);

            Assert.That(holder.MaxBrowse, Is.EqualTo(3));

            holder.MaxBrowse = 5;
            Assert.That(holder.MaxBrowse, Is.EqualTo(5));
        }

        [Test]
        public void SaveBrowseThrowsOnNullContinuationPoint()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.SaveBrowse(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("continuationPoint"));
        }

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

        [Test]
        public void RestoreBrowseReturnsNullBeforeAnySave()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.That(holder.RestoreBrowse(ToByteString(Guid.NewGuid())), Is.Null);
        }

        [Test]
        public void RestoreBrowseReturnsNullForWrongLength()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveBrowse(NewBrowsePoint());

            Assert.That(holder.RestoreBrowse(new ByteString(new byte[] { 1, 2, 3 })), Is.Null);
        }

        [Test]
        public void RestoreBrowseReturnsNullWhenNotFound()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveBrowse(NewBrowsePoint());

            Assert.That(holder.RestoreBrowse(ToByteString(Guid.NewGuid())), Is.Null);
        }

        [Test]
        public void RemoveBrowseForManagerThrowsOnNullNodeManager()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.RemoveBrowseForManager(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RemoveBrowseForManagerIsNoOpWhenNoBrowsePointsSaved()
        {
            SessionContinuationPoints holder = NewHolder();
            IAsyncNodeManager nodeManager = NewNodeManager(new Mock<INodeManager>().Object);

            Assert.DoesNotThrow(() => holder.RemoveBrowseForManager(nodeManager));
        }

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

        [Test]
        public void SaveHistoryThrowsOnNullContinuationPoint()
        {
            SessionContinuationPoints holder = NewHolder();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => holder.SaveHistory(Guid.NewGuid(), null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("continuationPoint"));
        }

        [Test]
        public void SaveHistoryThenRestoreHistoryReturnsValue()
        {
            SessionContinuationPoints holder = NewHolder();
            var id = Guid.NewGuid();
            object value = new();

            holder.SaveHistory(id, value);

            Assert.That(holder.RestoreHistory(ToByteString(id)), Is.SameAs(value));
            // The point is removed on restore, so a second restore misses.
            Assert.That(holder.RestoreHistory(ToByteString(id)), Is.Null);
        }

        [Test]
        public void SaveHistoryEvictsOldestAndNotifiesStore()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(maxHistory: 1, store: store.Object);

            var evicted = new TrackingDisposable();
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            holder.SaveHistory(id1, evicted);
            holder.SaveHistory(id2, new object());

            Assert.That(evicted.Disposed, Is.True);
            Assert.That(holder.RestoreHistory(ToByteString(id1)), Is.Null);
            Assert.That(holder.RestoreHistory(ToByteString(id2)), Is.Not.Null);
            store.Verify(
                s => s.RemoveContinuationPoint(s_sessionId, ContinuationPointKind.History, id1),
                Times.Once);
        }

        [Test]
        public void SaveHistoryStoresEnvelope()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            ContinuationPointEnvelope? captured = null;
            store
                .Setup(s => s.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback<ContinuationPointEnvelope>(envelope => captured = envelope);

            SessionContinuationPoints holder = NewHolder(store: store.Object);
            var id = Guid.NewGuid();

            holder.SaveHistory(id, new object());

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Id, Is.EqualTo(id));
            Assert.That(captured.OwnerSessionId, Is.EqualTo(s_sessionId));
            Assert.That(captured.Kind, Is.EqualTo(ContinuationPointKind.History));
            Assert.That(captured.BrowseNodeId.IsNull, Is.True);
            Assert.That(captured.ReferenceTypeId.IsNull, Is.True);
        }

        [Test]
        public void RestoreHistoryReturnsNullBeforeAnySave()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.That(holder.RestoreHistory(ToByteString(Guid.NewGuid())), Is.Null);
        }

        [Test]
        public void RestoreHistoryReturnsNullForWrongLength()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveHistory(Guid.NewGuid(), new object());

            Assert.That(holder.RestoreHistory(new ByteString("\t"u8.ToArray())), Is.Null);
        }

        [Test]
        public void RestoreHistoryReturnsNullWhenNotFound()
        {
            SessionContinuationPoints holder = NewHolder();
            holder.SaveHistory(Guid.NewGuid(), new object());

            Assert.That(holder.RestoreHistory(ToByteString(Guid.NewGuid())), Is.Null);
        }

        [Test]
        public async Task LoadMirroredAsyncReturnsEarlyWithoutStoreAsync()
        {
            SessionContinuationPoints holder = NewHolder();

            await holder.LoadMirroredAsync(s_ownerSessionId).ConfigureAwait(false);

            // Nothing was mirrored, so a subsequent restore still misses.
            holder.SaveBrowse(NewBrowsePoint());
            Assert.That(holder.RestoreBrowse(ToByteString(Guid.NewGuid())), Is.Null);
        }

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
            holder.SaveHistory(Guid.NewGuid(), new object());

            Assert.That(holder.RestoreBrowse(ToByteString(browseId)), Is.Null);
            Assert.That(holder.RestoreHistory(ToByteString(historyId)), Is.Null);
            store.Verify(
                s => s.RemoveContinuationPoint(s_ownerSessionId, ContinuationPointKind.Browse, browseId),
                Times.Once);
            store.Verify(
                s => s.RemoveContinuationPoint(s_ownerSessionId, ContinuationPointKind.History, historyId),
                Times.Once);
        }

        [Test]
        public void ClearDisposesBrowseAndHistoryPoints()
        {
            var store = new Mock<IContinuationPointStore>(MockBehavior.Loose);
            SessionContinuationPoints holder = NewHolder(store: store.Object);

            var browseData = new TrackingDisposable();
            var historyValue = new TrackingDisposable();
            ContinuationPoint cp = NewBrowsePoint(data: browseData);
            var historyId = Guid.NewGuid();
            holder.SaveBrowse(cp);
            holder.SaveHistory(historyId, historyValue);

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

        [Test]
        public void ClearWithoutPointsDoesNotThrow()
        {
            SessionContinuationPoints holder = NewHolder();

            Assert.DoesNotThrow(holder.Clear);
        }

        private static SessionContinuationPoints NewHolder(
            int maxBrowse = 10,
            int maxHistory = 10,
            IContinuationPointStore? store = null)
        {
            return new SessionContinuationPoints(() => s_sessionId, maxBrowse, maxHistory, store);
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

        private sealed class TrackingDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
