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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Encapsulates a session's browse and history continuation points: their in-memory lists, the mirrored-owner
    /// bookkeeping used by a redundant standby, and the optional <see cref="IContinuationPointStore"/> that persists
    /// them for cross-replica takeover. Keeping this here lets <see cref="Session"/> delegate through a small surface
    /// (save/restore/load/clear) instead of managing the store, lists, and dictionaries inline.
    /// </summary>
    internal sealed class SessionContinuationPoints :
        ISessionContinuationPoints,
        ISessionContinuationPointLifecycle,
        ISessionHistoryContinuationPointLifecycle
    {
        /// <summary>
        /// Creates the continuation-point holder for a session.
        /// </summary>
        /// <param name="sessionIdProvider">Returns the owning session's id (read lazily so it is current).</param>
        /// <param name="maxBrowse">The maximum number of browse continuation points retained.</param>
        /// <param name="maxHistory">The maximum number of history continuation points retained.</param>
        /// <param name="store">
        /// Optional store that mirrors continuation points across a <c>RedundantServerSet</c>; <c>null</c> when the
        /// server is not distributed.
        /// </param>
        public SessionContinuationPoints(
            Func<NodeId> sessionIdProvider,
            int maxBrowse,
            int maxHistory,
            IContinuationPointStore? store)
        {
            m_sessionIdProvider = sessionIdProvider ?? throw new ArgumentNullException(nameof(sessionIdProvider));
            MaxBrowse = maxBrowse;
            m_maxHistory = maxHistory;
            m_store = store;
        }

        /// <summary>
        /// Gets or sets the maximum number of available Browse points before the oldest is dropped.
        /// Checked-out points retain ownership but do not occupy an available cache slot.
        /// </summary>
        public int MaxBrowse { get; set; }

        /// <inheritdoc/>
        public event Action? BrowseContinuationPointsReleased;

        /// <inheritdoc/>
        public event Action? HistoryContinuationPointsReleased;

        /// <inheritdoc/>
        public bool HasBrowseForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            lock (m_lock)
            {
                return m_browse?.Exists(point => IsOwnedBy(point.Point, nodeManager)) == true;
            }
        }

        /// <summary>
        /// Saves a browse continuation point, dropping the oldest when the limit is exceeded.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="continuationPoint"/> is <c>null</c>.</exception>
        public void SaveBrowse(ContinuationPoint continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            var evicted = new List<ContinuationPoint>();
            lock (m_lock)
            {
                if (m_cleared)
                {
                    throw new ObjectDisposedException(nameof(SessionContinuationPoints));
                }
                if (MaxBrowse <= 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNoContinuationPoints);
                }
                m_browse ??= [];
                BrowseContinuationPoint? entry = m_browse.Find(
                    point => ReferenceEquals(point.Point, continuationPoint));
                if (entry is null)
                {
                    entry = new BrowseContinuationPoint(continuationPoint);
                    continuationPoint.SetOwnerRelease(() => ReleaseBrowse(entry));
                }
                else
                {
                    if (entry.Invalidated)
                    {
                        throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                    }
                    m_browse.Remove(entry);
                    if (entry.Available)
                    {
                        m_availableBrowse--;
                    }
                }
                while (m_availableBrowse >= MaxBrowse)
                {
                    BrowseContinuationPoint oldest = m_browse.Find(point => point.Available)!;
                    oldest.Available = false;
                    oldest.Invalidated = true;
                    m_availableBrowse--;
                    evicted.Add(oldest.Point);
                }
                entry.Available = true;
                m_availableBrowse++;
                m_browse.Add(entry);
            }
            try
            {
                DisposeBrowsePoints(evicted);
                m_store?.StoreContinuationPoint(CreateBrowseEnvelope(continuationPoint));
            }
            catch
            {
                continuationPoint.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Restores an available Browse point without releasing its source ownership.
        /// The caller disposes the returned point or saves its next page.
        /// </summary>
        public ContinuationPoint? RestoreBrowse(ByteString continuationPoint)
        {
            ContinuationPoint? restored = null;
            lock (m_lock)
            {
                if (m_browse == null)
                {
                    return null;
                }

                if (continuationPoint.Length != 16)
                {
                    return null;
                }

                var id = new Guid(continuationPoint.ToArray());

                for (int ii = 0; ii < m_browse.Count; ii++)
                {
                    BrowseContinuationPoint entry = m_browse[ii];
                    if (entry.Available && entry.Point.Id == id)
                    {
                        entry.Available = false;
                        m_availableBrowse--;
                        restored = entry.Point;
                        break;
                    }
                }

                if (restored is null && m_mirroredBrowseOwners != null &&
                    m_mirroredBrowseOwners.TryGetValue(id, out NodeId ownerSessionId))
                {
                    m_mirroredBrowseOwners.Remove(id);
                    m_store?.RemoveContinuationPoint(ownerSessionId, ContinuationPointKind.Browse, id);
                }
            }
            if (restored is not null)
            {
                try
                {
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.Browse, restored.Id);
                }
                catch
                {
                    restored.Dispose();
                    throw;
                }
            }
            return restored;
        }

        /// <inheritdoc/>
        public void RemoveForManager(IAsyncNodeManager nodeManager)
        {
            RemoveBrowseForManager(nodeManager);
            RemoveHistoryForManager(nodeManager);
        }

        public void RemoveBrowseForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            List<ContinuationPoint>? removed = null;
            lock (m_lock)
            {
                if (m_browse == null)
                {
                    return;
                }

                for (int ii = m_browse.Count - 1; ii >= 0; ii--)
                {
                    BrowseContinuationPoint entry = m_browse[ii];
                    if (!IsOwnedBy(entry.Point, nodeManager))
                    {
                        continue;
                    }
                    entry.Invalidated = true;
                    if (entry.Available)
                    {
                        entry.Available = false;
                        m_availableBrowse--;
                        removed ??= [];
                        removed.Add(entry.Point);
                    }
                }
            }

            if (removed == null)
            {
                return;
            }

            // Persisting and disposing runs outside the lock, because a continuation point belongs
            // to the NodeManager being retired and its disposal must not block unrelated Browse
            // operations, or re-enter this session while the lock is held.
            DisposeBrowsePoints(removed);
        }

        private static bool IsOwnedBy(ContinuationPoint point, IAsyncNodeManager nodeManager)
        {
            return point.RequiresManager(nodeManager);
        }

        private void ReleaseBrowse(BrowseContinuationPoint entry)
        {
            bool removed;
            lock (m_lock)
            {
                removed = m_browse?.Remove(entry) == true;
                if (removed && entry.Available)
                {
                    m_availableBrowse--;
                }
            }
            if (removed)
            {
                BrowseContinuationPointsReleased?.Invoke();
            }
        }

        private void DisposeBrowsePoints(List<ContinuationPoint> points)
        {
            List<Exception>? failures = null;
            foreach (ContinuationPoint point in points)
            {
                try
                {
                    try
                    {
                        m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.Browse, point.Id);
                    }
                    finally
                    {
                        point.Dispose();
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures is not null)
            {
                throw new AggregateException("Browse continuation cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Drops and disposes the history continuation points that belong to a NodeManager which
        /// is being retired, so its state is released with it instead of lingering until the
        /// Session closes or the history limit evicts it.
        /// </summary>
        /// <param name="nodeManager">The NodeManager being retired.</param>
        /// <exception cref="ArgumentNullException"><paramref name="nodeManager"/> is <c>null</c>.</exception>
        public void RemoveHistoryForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            List<HistoryContinuationPoint>? removed = null;
            lock (m_lock)
            {
                if (m_history == null)
                {
                    return;
                }

                for (int ii = m_history.Count - 1; ii >= 0; ii--)
                {
                    HistoryContinuationPoint continuationPoint = m_history[ii];
                    if (continuationPoint.Value is not Historian.HistorianContinuationState state ||
                        !state.Ownership.RequiresManager(nodeManager))
                    {
                        continue;
                    }

                    continuationPoint.Invalidated = true;
                    if (continuationPoint.Available)
                    {
                        continuationPoint.Available = false;
                        m_availableHistory--;
                        (removed ??= []).Add(continuationPoint);
                    }
                }
            }

            if (removed == null)
            {
                return;
            }

            DisposeHistoryPoints(removed);
        }

        /// <inheritdoc/>
        public bool HasHistoryForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            lock (m_lock)
            {
                return m_history?.Exists(entry => entry.Value is Historian.HistorianContinuationState state &&
                    state.Ownership.RequiresManager(nodeManager)) == true;
            }
        }

        /// <summary>
        /// Saves a history continuation point, dropping the oldest when the limit is reached. The
        /// dropped point is disposed, as is every point still held when the session is cleared.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="continuationPoint"/> is <c>null</c>.</exception>
        public void SaveHistory(IHistoryContinuationPoint continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            var evicted = new List<HistoryContinuationPoint>();
            lock (m_lock)
            {
                if (m_cleared)
                {
                    throw new ObjectDisposedException(nameof(SessionContinuationPoints));
                }
                if (m_maxHistory <= 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNoContinuationPoints);
                }
                m_history ??= [];
                HistoryContinuationPoint? entry = m_history.Find(
                    point => ReferenceEquals(point.Value, continuationPoint));
                if (entry is null)
                {
                    entry = new HistoryContinuationPoint(continuationPoint);
                    if (continuationPoint is Historian.HistorianContinuationState state)
                    {
                        state.Ownership.SetOwnerRelease(() => ReleaseHistory(entry));
                    }
                }
                else
                {
                    if (entry.Invalidated)
                    {
                        throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                    }
                    m_history.Remove(entry);
                    if (entry.Available)
                    {
                        m_availableHistory--;
                    }
                }
                while (m_availableHistory >= m_maxHistory)
                {
                    HistoryContinuationPoint oldest = m_history.Find(point => point.Available)!;
                    oldest.Available = false;
                    oldest.Invalidated = true;
                    m_availableHistory--;
                    evicted.Add(oldest);
                    if (oldest.Value is not Historian.HistorianContinuationState)
                    {
                        m_history.Remove(oldest);
                    }
                }
                entry.Id = continuationPoint.Id;
                entry.Available = true;
                m_availableHistory++;
                m_history.Add(entry);
            }
            try
            {
                DisposeHistoryPoints(evicted);
                m_store?.StoreContinuationPoint(CreateHistoryEnvelope(continuationPoint.Id));
                if (continuationPoint is Historian.HistorianContinuationState state)
                {
                    state.Saved = true;
                }
            }
            catch
            {
                ReleaseHistoryPoint(continuationPoint);
                throw;
            }
        }

        /// <summary>
        /// Restores (and removes) a previously saved history continuation point, or <c>null</c> when not found.
        /// </summary>
        public IHistoryContinuationPoint? RestoreHistory(ByteString continuationPoint)
        {
            if (m_historyRead.Value is { } current && current.Token == continuationPoint && !current.Consumed)
            {
                current.Consumed = true;
                return current.Point;
            }
            IHistoryContinuationPoint? restored = null;
            lock (m_lock)
            {
                m_history ??= [];

                if (continuationPoint.Length != 16)
                {
                    return null;
                }

                var id = new Guid(continuationPoint.ToArray());

                for (int ii = 0; ii < m_history.Count; ii++)
                {
                    HistoryContinuationPoint cp = m_history[ii];

                    if (cp.Available && cp.Id == id)
                    {
                        cp.Available = false;
                        m_availableHistory--;
                        if (cp.Value is Historian.HistorianContinuationState state)
                        {
                            state.Saved = false;
                        }
                        else
                        {
                            m_history.RemoveAt(ii);
                        }
                        restored = cp.Value;
                        break;
                    }
                }

                if (restored is null && m_mirroredHistoryOwners != null &&
                    m_mirroredHistoryOwners.TryGetValue(id, out NodeId ownerSessionId))
                {
                    m_mirroredHistoryOwners.Remove(id);
                    m_store?.RemoveContinuationPoint(ownerSessionId, ContinuationPointKind.History, id);
                }
            }
            if (restored is not null)
            {
                try
                {
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, restored.Id);
                }
                catch
                {
                    restored.Dispose();
                    throw;
                }
            }
            return restored;
        }

        internal HistoryReadScope BeginHistoryRead(ByteString token)
        {
            IHistoryContinuationPoint? point = RestoreHistory(token);
            var scope = new HistoryReadScope(this, m_historyRead.Value, token, point);
            m_historyRead.Value = scope;
            return scope;
        }

        internal bool IsCapturedHistoryPoint(ByteString token)
        {
            if (token.Length != 16)
            {
                return false;
            }
            var id = new Guid(token.ToArray());
            lock (m_lock)
            {
                return m_history?.Exists(entry => entry.Available && entry.Id == id &&
                    entry.Value is Historian.HistorianContinuationState state &&
                    state.Ownership.HasCapturedDependencies) == true;
            }
        }

        internal Historian.IHistorianProvider? GetRestoredHistoryProvider(NodeId nodeId)
        {
            return m_historyRead.Value?.Point is Historian.HistorianContinuationState state && state.NodeId == nodeId
                ? state.Provider
                : null;
        }

        private void ReleaseHistory(HistoryContinuationPoint entry)
        {
            bool removed;
            lock (m_lock)
            {
                removed = m_history?.Remove(entry) == true;
                if (removed && entry.Available)
                {
                    m_availableHistory--;
                }
            }
            if (removed)
            {
                HistoryContinuationPointsReleased?.Invoke();
            }
        }

        private void ReleaseHistoryPoint(IHistoryContinuationPoint point)
        {
            if (point is not Historian.HistorianContinuationState)
            {
                lock (m_lock)
                {
                    HistoryContinuationPoint? entry = m_history?.Find(item => ReferenceEquals(item.Value, point));
                    if (entry is not null)
                    {
                        m_history!.Remove(entry);
                        if (entry.Available)
                        {
                            m_availableHistory--;
                        }
                    }
                }
            }
            point.Dispose();
        }

        private void DisposeHistoryPoints(List<HistoryContinuationPoint> points)
        {
            List<Exception>? failures = null;
            foreach (HistoryContinuationPoint point in points)
            {
                try
                {
                    try
                    {
                        m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, point.Id);
                    }
                    finally
                    {
                        ReleaseHistoryPoint(point.Value);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures is not null)
            {
                throw new AggregateException("History continuation cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Loads mirrored continuation-point envelopes for a session restored on a backup replica, recording the
        /// original owner so the entry can be cleaned from the shared store when it is consumed.
        /// </summary>
        /// <param name="ownerSessionId">The original owner session id from the active replica.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask LoadMirroredAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default)
        {
            if (m_store == null || ownerSessionId.IsNull)
            {
                return;
            }

            ArrayOf<ContinuationPointEnvelope> envelopes = await m_store
                .LoadContinuationPointsAsync(ownerSessionId, cancellationToken)
                .ConfigureAwait(false);

            lock (m_lock)
            {
                foreach (ContinuationPointEnvelope envelope in envelopes)
                {
                    switch (envelope.Kind)
                    {
                        case ContinuationPointKind.Browse:
                            m_mirroredBrowseOwners ??= [];
                            m_mirroredBrowseOwners[envelope.Id] = envelope.OwnerSessionId;
                            break;
                        case ContinuationPointKind.History:
                            m_mirroredHistoryOwners ??= [];
                            m_mirroredHistoryOwners[envelope.Id] = envelope.OwnerSessionId;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Removes and disposes all continuation points (called when the session is closed or discarded).
        /// </summary>
        public void Clear()
        {
            var browseCPs = new List<ContinuationPoint>();
            var historyCPs = new List<HistoryContinuationPoint>();
            lock (m_lock)
            {
                m_cleared = true;
                if (m_browse is not null)
                {
                    foreach (BrowseContinuationPoint entry in m_browse)
                    {
                        entry.Invalidated = true;
                        if (entry.Available)
                        {
                            entry.Available = false;
                            browseCPs.Add(entry.Point);
                        }
                    }
                }
                m_availableBrowse = 0;
                if (m_history is not null)
                {
                    foreach (HistoryContinuationPoint entry in m_history)
                    {
                        entry.Invalidated = true;
                        if (entry.Available)
                        {
                            entry.Available = false;
                            historyCPs.Add(entry);
                        }
                    }
                }
                m_availableHistory = 0;
            }

            try
            {
                DisposeBrowsePoints(browseCPs);
            }
            finally
            {
                DisposeHistoryPoints(historyCPs);
            }
        }

        private ContinuationPointEnvelope CreateBrowseEnvelope(ContinuationPoint continuationPoint)
        {
            return new ContinuationPointEnvelope
            {
                Id = continuationPoint.Id,
                OwnerSessionId = Id,
                Kind = ContinuationPointKind.Browse,
                BrowseNodeId = NormalizeNodeId(continuationPoint.RequestedNodeId),
                View = continuationPoint.View,
                MaxResultsToReturn = continuationPoint.MaxResultsToReturn,
                BrowseDirection = continuationPoint.BrowseDirection,
                ReferenceTypeId = NormalizeNodeId(continuationPoint.ReferenceTypeId),
                IncludeSubtypes = continuationPoint.IncludeSubtypes,
                NodeClassMask = continuationPoint.NodeClassMask,
                ResultMask = continuationPoint.ResultMask,
                Index = continuationPoint.Index
            };
        }

        private ContinuationPointEnvelope CreateHistoryEnvelope(Guid id)
        {
            return new ContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = Id,
                Kind = ContinuationPointKind.History,
                BrowseNodeId = NodeId.Null,
                ReferenceTypeId = NodeId.Null
            };
        }

        private static NodeId NormalizeNodeId(NodeId nodeId)
        {
            return nodeId.IsNull ? NodeId.Null : nodeId;
        }

        private NodeId Id => m_sessionIdProvider();

        private sealed class BrowseContinuationPoint(ContinuationPoint point)
        {
            public ContinuationPoint Point { get; } = point;
            public bool Available { get; set; }
            public bool Invalidated { get; set; }
        }

        internal sealed class HistoryReadScope(
            SessionContinuationPoints owner,
            HistoryReadScope? previous,
            ByteString token,
            IHistoryContinuationPoint? point) : IDisposable
        {
            public ByteString Token { get; } = token;

            public IHistoryContinuationPoint? Point { get; } = point;

            public bool Consumed { get; set; }

            public void Dispose()
            {
                owner.m_historyRead.Value = previous;
                if (Point is Historian.HistorianContinuationState { Saved: false } || !Consumed)
                {
                    Point?.Dispose();
                }
            }
        }

        private sealed class HistoryContinuationPoint(IHistoryContinuationPoint value)
        {
            public Guid Id { get; set; } = value.Id;
            public IHistoryContinuationPoint Value { get; } = value;
            public bool Available { get; set; }
            public bool Invalidated { get; set; }
        }

        private readonly Func<NodeId> m_sessionIdProvider;
        private readonly int m_maxHistory;
        private readonly IContinuationPointStore? m_store;
        private readonly Lock m_lock = new();
        private List<BrowseContinuationPoint>? m_browse;
        private int m_availableBrowse;
        private bool m_cleared;
        private List<HistoryContinuationPoint>? m_history;
        private int m_availableHistory;
        private readonly AsyncLocal<HistoryReadScope?> m_historyRead = new();
        private Dictionary<Guid, NodeId>? m_mirroredBrowseOwners;
        private Dictionary<Guid, NodeId>? m_mirroredHistoryOwners;
    }
}
