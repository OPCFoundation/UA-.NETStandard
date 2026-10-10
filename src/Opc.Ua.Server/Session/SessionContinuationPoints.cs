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
using System.Runtime.ExceptionServices;
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
        /// Creates a local-only continuation-point holder.
        /// </summary>
        public SessionContinuationPoints(
            Func<NodeId> sessionIdProvider,
            int maxBrowse,
            int maxHistory,
            IContinuationPointStore? store)
            : this(
                sessionIdProvider,
                maxBrowse,
                maxHistory,
                store,
                historyStore: null,
                historyCodec: null,
                new NamespaceTable())
        {
        }

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
        /// <param name="historyStore">
        /// Optional durable store for portable HistoryRead continuation points.
        /// </param>
        /// <param name="historyCodec">
        /// Codec that translates portable history continuation state to opaque payloads.
        /// </param>
        /// <param name="namespaceUris">
        /// Server namespace table used to associate history points with node managers.
        /// </param>
        public SessionContinuationPoints(
            Func<NodeId> sessionIdProvider,
            int maxBrowse,
            int maxHistory,
            IContinuationPointStore? store,
            IHistoryContinuationPointStore? historyStore,
            IHistoryContinuationPointCodec? historyCodec,
            NamespaceTable namespaceUris)
        {
            m_sessionIdProvider = sessionIdProvider ?? throw new ArgumentNullException(nameof(sessionIdProvider));
            if (maxBrowse < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBrowse));
            }
            if (maxHistory < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHistory));
            }
            MaxBrowse = maxBrowse;
            m_maxHistory = maxHistory;
            m_store = store;
            m_historyStore = historyStore;
            m_historyCodec = historyCodec;
            m_namespaceUris = namespaceUris ??
                throw new ArgumentNullException(nameof(namespaceUris));
        }

        /// <summary>
        /// Gets or sets the maximum number of available browse points before the oldest is dropped.
        /// Checked-out points retain ownership but do not occupy an available cache slot.
        /// A value of zero means that no limit is imposed.
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
        /// Persists a browse point before transferring ownership, retaining checked-out generation owners.
        /// </summary>
        public void SaveBrowse(ContinuationPoint continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            var evicted = new List<ContinuationPoint>();
            ContinuationPointEnvelope? envelope = m_store != null ? CreateBrowseEnvelope(continuationPoint) : null;
            bool persisted = false;
            bool admitted = false;
            try
            {
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    }
                }
                if (envelope != null)
                {
                    m_store!.StoreContinuationPoint(envelope);
                    persisted = true;
                }
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
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
                    while (MaxBrowse > 0 && m_availableBrowse >= MaxBrowse)
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
                    admitted = true;
                }
            }
            finally
            {
                try
                {
                    if (persisted && !admitted)
                    {
                        m_store!.RemoveContinuationPoint(
                            envelope!.OwnerSessionId, ContinuationPointKind.Browse, envelope.Id);
                    }
                }
                finally
                {
                    DisposeBrowsePoints(evicted);
                }
            }
        }

        /// <summary>
        /// Restores (and removes) a browse continuation point. The caller disposes the returned point.
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

        /// <summary>
        /// Removes and disposes browse continuation points owned by the specified node manager.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
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
        /// Invalidates saved and checked-out history sources, disposing only points not executing in a request.
        /// </summary>
        public void RemoveHistoryForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            List<HistoryContinuationPoint>? removed = null;
            lock (m_lock)
            {
                foreach (HistoryContinuationPoint checkedOut in m_checkedOutHistory)
                {
                    if (IsOwnedBy(checkedOut.Value, nodeManager) &&
                        checkedOut.Value is Historian.HistorianContinuationState state)
                    {
                        state.Invalidate();
                    }
                }
                for (int ii = (m_history?.Count ?? 0) - 1; ii >= 0; ii--)
                {
                    HistoryContinuationPoint continuationPoint = m_history![ii];
                    if (!IsOwnedBy(continuationPoint.Value, nodeManager))
                    {
                        continue;
                    }

                    if (continuationPoint.Value is Historian.HistorianContinuationState state)
                    {
                        state.Invalidate();
                    }
                    if (continuationPoint.Claiming)
                    {
                        continue;
                    }
                    m_history.RemoveAt(ii);
                    TrackCheckedOutHistory(continuationPoint);
                    removed ??= [];
                    removed.Add(continuationPoint);
                }
            }

            if (removed == null)
            {
                return;
            }

            // Persisting and disposing runs outside the lock, for the same reason as the Browse
            // continuation points: the state belongs to the NodeManager being retired.
            DisposeHistoryPoints(removed);
        }

        /// <summary>
        /// Reports whether a history continuation point was produced by the given NodeManager.
        /// Only the built-in historian state records its provider, so a continuation point from a
        /// custom implementation is left alone rather than dropped on a guess.
        /// </summary>
        private bool IsOwnedBy(
            IHistoryContinuationPoint continuationPoint,
            IAsyncNodeManager nodeManager)
        {
            if (continuationPoint is not Historian.HistorianContinuationState state)
            {
                return false;
            }
            if (state.Ownership.Manager is not null || state.Ownership.HasCapturedDependencies)
            {
                return state.Ownership.RequiresManager(nodeManager);
            }
            string? namespaceUri = m_namespaceUris.GetString(
                state.NodeId.NamespaceIndex);
            if (namespaceUri == null)
            {
                return false;
            }
            foreach (string ownedNamespace in nodeManager.NamespaceUris)
            {
                if (string.Equals(
                    ownedNamespace,
                    namespaceUri,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
                return m_history?.Exists(entry => IsOwnedBy(entry.Value, nodeManager)) == true ||
                    m_checkedOutHistory.Exists(entry => IsOwnedBy(entry.Value, nodeManager));
            }
        }

        internal HistoryReadScope BeginHistoryRead(ByteString token, IHistoryContinuationPoint? point)
        {
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
                return m_history?.Exists(entry => !entry.PendingPersistence && !entry.Claiming && entry.Id == id &&
                    entry.Value is Historian.HistorianContinuationState state &&
                    state.Ownership.HasCapturedDependencies) == true;
            }
        }

        internal Historian.IHistorianProvider? GetRestoredHistoryProvider(NodeId nodeId)
        {
            return m_historyRead.Value?.Point is Historian.HistorianContinuationState state &&
                (state.NodeId == nodeId || state.OriginNodeId == nodeId ||
                    (state.SourceNode is { } source && source.NodeId == nodeId))
                ? state.Provider
                : null;
        }

        /// <inheritdoc/>
        public void SaveHistory(IHistoryContinuationPoint continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            bool persisted = false;
            bool admitted = false;
            try
            {
                if (continuationPoint is Historian.HistorianContinuationState state)
                {
                    state.ValidateSessionOwner(this);
                }
                if (m_store != null)
                {
                    m_store.StoreContinuationPoint(CreateHistoryEnvelope(continuationPoint.Id));
                    persisted = true;
                }
                _ = AddHistoryContinuationPoint(
                    continuationPoint,
                    Id,
                    portable: false);
                admitted = true;
            }
            finally
            {
                if (!admitted)
                {
                    try
                    {
                        if (persisted)
                        {
                            m_store!.RemoveContinuationPoint(Id, ContinuationPointKind.History, continuationPoint.Id);
                        }
                    }
                    finally
                    {
                        DisposeUnretainedHistoryPoint(continuationPoint);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask SaveHistoryAsync(
            IHistoryContinuationPoint continuationPoint,
            CancellationToken cancellationToken = default)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            // Captured owners remain local; the codec independently validates whether the cursor is portable.
            if (continuationPoint is Historian.HistorianContinuationState { Ownership.HasCapturedDependencies: true })
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch
                {
                    DisposeUnretainedHistoryPoint(continuationPoint);
                    throw;
                }
            }

            HistoryContinuationPoint local;
            try
            {
                local = AddHistoryContinuationPoint(
                    continuationPoint,
                    Id,
                    portable: false,
                    pendingPersistence:
                        m_historyStore != null && m_historyCodec != null);
            }
            catch
            {
                DisposeUnretainedHistoryPoint(continuationPoint);
                throw;
            }
            if (m_historyStore == null || m_historyCodec == null)
            {
                return;
            }

            HistoryContinuationPointEnvelope? envelope = null;
            try
            {
                envelope = await m_historyCodec
                    .EncodeAsync(Id, continuationPoint, cancellationToken)
                    .ConfigureAwait(false);
                if (envelope == null)
                {
                    CompletePendingHistory(local, portable: false);
                    return;
                }
                await m_historyStore
                    .StoreAsync(envelope, cancellationToken)
                    .ConfigureAwait(false);
                if (!CompletePendingHistory(local, portable: true))
                {
                    TryScheduleHistoryRemoval(
                        envelope.OwnerSessionId,
                        envelope.Id);
                    throw new ServiceResultException(
                        StatusCodes.BadSessionClosed,
                        "The session released the history continuation while it was being persisted.");
                }
            }
            catch
            {
                if (envelope != null)
                {
                    TryScheduleHistoryRemoval(
                        envelope.OwnerSessionId,
                        envelope.Id);
                }
                bool removed;
                lock (m_lock)
                {
                    removed = m_history?.Remove(local) == true;
                    if (removed)
                    {
                        TrackCheckedOutHistory(local);
                    }

                    // The point consumed the slot its request reserved to continue an operation;
                    // give it back in the same step, so a concurrent save cannot take it before
                    // the request restores the continuation it claimed (Part 4 §7.9). A request
                    // that has already ended released its reservations and gets nothing back.
                    HistoryRequestScope? reservedBy = local.ReservedBy;
                    local.ReservedBy = null;
                    if (removed && reservedBy != null && !reservedBy.Ended)
                    {
                        reservedBy.Reservations++;
                        m_reservedHistory++;
                    }
                }
                if (removed)
                {
                    continuationPoint.Dispose();
                }
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
            IHistoryContinuationPoint? point = null;
            lock (m_lock)
            {
                if (!TryGetHistoryContinuationPointId(
                        continuationPoint,
                        out Guid id))
                {
                    return null;
                }
                if (m_history == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return null;
                }
                for (int i = 0; i < m_history.Count; i++)
                {
                    HistoryContinuationPoint restored = m_history[i];
                    if (restored.Id == id)
                    {
                        if (restored.Portable ||
                            restored.PendingPersistence)
                        {
                            return null;
                        }
                        m_history.RemoveAt(i);
                        TrackCheckedOutHistory(restored);
                        ReserveForContinuedOperation(id);
                        point = restored.Value;
                        break;
                    }
                }

                if (point == null)
                {
                    RemoveMirroredHistoryOwner(id);
                }
            }
            RemoveLocalHistoryMirror(point);
            return point;
        }

        /// <inheritdoc/>
        public bool ReleaseHistory(ByteString continuationPoint)
        {
            HistoryContinuationPoint? released = null;
            lock (m_lock)
            {
                if (!TryGetHistoryContinuationPointId(
                        continuationPoint,
                        out Guid id))
                {
                    return false;
                }
                if (m_history == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return false;
                }
                for (int i = 0; i < m_history.Count; i++)
                {
                    HistoryContinuationPoint candidate = m_history[i];
                    if (candidate.Id != id)
                    {
                        continue;
                    }
                    if (candidate.Claiming ||
                        candidate.PendingPersistence)
                    {
                        return false;
                    }
                    m_history.RemoveAt(i);
                    TrackCheckedOutHistory(candidate);
                    released = candidate;
                    break;
                }
                if (released == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return false;
                }
            }
            if (released.Portable)
            {
                TryScheduleHistoryRemoval(
                    released.OwnerSessionId,
                    released.Id);
            }
            try
            {
                RemoveLocalHistoryMirror(released.Value);
            }
            finally
            {
                released.Value.Dispose();
            }
            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<IHistoryContinuationPoint?> RestoreHistoryAsync(
            ByteString continuationPoint,
            CancellationToken cancellationToken = default)
        {
            if (m_historyRead.Value is { Point: not null } current &&
                current.Token == continuationPoint && !current.Consumed)
            {
                current.Consumed = true;
                return current.Point;
            }
            if (!TryGetHistoryContinuationPointId(
                    continuationPoint,
                    out Guid id))
            {
                return null;
            }
            HistoryContinuationPoint? restored = null;
            IHistoryContinuationPoint? localPoint = null;
            lock (m_lock)
            {
                if (m_history == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return null;
                }
                for (int i = 0; i < m_history.Count; i++)
                {
                    HistoryContinuationPoint candidate = m_history[i];
                    if (candidate.Id != id)
                    {
                        continue;
                    }
                    if (candidate.PendingPersistence)
                    {
                        return null;
                    }
                    if (!candidate.Portable)
                    {
                        m_history.RemoveAt(i);
                        TrackCheckedOutHistory(candidate);
                        ReserveForContinuedOperation(id);
                        localPoint = candidate.Value;
                        break;
                    }
                    if (candidate.Claiming || m_historyStore == null)
                    {
                        return null;
                    }
                    candidate.Claiming = true;
                    restored = candidate;
                    break;
                }
                if (restored == null && localPoint == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return null;
                }
            }

            if (localPoint != null)
            {
                RemoveLocalHistoryMirror(localPoint);
                return localPoint;
            }
            HistoryContinuationPoint portablePoint = restored!;
            bool claimed;
            try
            {
                claimed = await m_historyStore!.TryTakeAsync(
                    portablePoint.OwnerSessionId,
                    portablePoint.Id,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ResetHistoryClaim(portablePoint);
                throw;
            }

            bool removed = RemoveClaimedHistory(portablePoint, reserve: claimed);
            if (!removed)
            {
                return null;
            }
            if (!claimed)
            {
                portablePoint.Value.Dispose();
                return null;
            }
            return portablePoint.Value;
        }

        private void ResetHistoryClaim(HistoryContinuationPoint continuationPoint)
        {
            bool release = false;
            lock (m_lock)
            {
                List<HistoryContinuationPoint>? history = m_history;
                if (history != null && history.Contains(continuationPoint))
                {
                    continuationPoint.Claiming = false;
                    if (m_closed ||
                        continuationPoint.Value is Historian.HistorianContinuationState { IsInvalidated: true })
                    {
                        history.Remove(continuationPoint);
                        TrackCheckedOutHistory(continuationPoint);
                        release = true;
                    }
                }
            }
            if (release)
            {
                DisposeHistoryPoints([continuationPoint]);
            }
        }

        private bool RemoveClaimedHistory(
            HistoryContinuationPoint continuationPoint,
            bool reserve)
        {
            lock (m_lock)
            {
                List<HistoryContinuationPoint>? history = m_history;
                if (history == null || !history.Remove(continuationPoint))
                {
                    return false;
                }
                TrackCheckedOutHistory(continuationPoint);
                if (reserve)
                {
                    ReserveForContinuedOperation(continuationPoint.Id);
                }
                return true;
            }
        }

        private void TrackCheckedOutHistory(HistoryContinuationPoint entry)
        {
            if (entry.Value is Historian.HistorianContinuationState state)
            {
                state.Saved = false;
                m_checkedOutHistory.Add(entry);
            }
        }

        private void RemoveLocalHistoryMirror(IHistoryContinuationPoint? point)
        {
            if (point == null)
            {
                return;
            }
            try
            {
                m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, point.Id);
            }
            catch
            {
                point.Dispose();
                throw;
            }
        }

        private void ReleaseHistory(HistoryContinuationPoint entry)
        {
            bool removed;
            lock (m_lock)
            {
                removed = m_history?.Remove(entry) == true;
                removed |= m_checkedOutHistory.Remove(entry);
            }
            if (removed)
            {
                HistoryContinuationPointsReleased?.Invoke();
            }
        }

        private void DisposeUnretainedHistoryPoint(IHistoryContinuationPoint point)
        {
            if (point is Historian.HistorianContinuationState state)
            {
                state.DisposeUnlessOwnedByAnotherSession(this);
            }
            else
            {
                point.Dispose();
            }
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
                        if (point.Portable)
                        {
                            TryScheduleHistoryRemoval(point.OwnerSessionId, point.Id);
                        }
                    }
                    finally
                    {
                        point.Value.Dispose();
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
        /// Keeps the slot of a continuation point that the current HistoryRead request restored
        /// to continue an operation the client supplied, so that a concurrent request cannot take
        /// it before the operation saves its successor: continuing a halted operation must not fail
        /// for lack of continuation points (Part 4 §7.9). The reservation is consumed by the next
        /// point the request saves, or released when the request ends. Called under the lock.
        /// </summary>
        private void ReserveForContinuedOperation(Guid id)
        {
            if (m_maxHistory == 0)
            {
                return;
            }
            HistoryRequestScope? current = GetCurrentHistoryRequest();
            if (current == null || !current.IsClientSupplied(id))
            {
                return;
            }
            current.Reservations++;
            m_reservedHistory++;
        }

        /// <summary>
        /// Returns the in-flight HistoryRead request of the calling asynchronous flow that
        /// belongs to this holder, or <c>null</c>.
        /// </summary>
        private HistoryRequestScope? GetCurrentHistoryRequest()
        {
            HistoryRequestScope? current = s_currentHistoryRequest.Value;
            return current != null &&
                ReferenceEquals(current.Owner, this) &&
                !current.Ended
                ? current
                : null;
        }

        private static bool TryGetHistoryContinuationPointId(
            ByteString continuationPoint,
            out Guid id)
        {
            if (continuationPoint.Length != 16)
            {
                id = Guid.Empty;
                return false;
            }
            id = new Guid(continuationPoint.ToArray());
            return true;
        }

        private void RemoveMirroredHistoryOwner(Guid id)
        {
            if (m_mirroredHistoryOwners != null &&
                m_mirroredHistoryOwners.TryGetValue(
                    id,
                    out NodeId ownerSessionId))
            {
                m_mirroredHistoryOwners.Remove(id);
                m_store?.RemoveContinuationPoint(
                    ownerSessionId,
                    ContinuationPointKind.History,
                    id);
            }
        }

        private HistoryContinuationPoint AddHistoryContinuationPoint(
            IHistoryContinuationPoint continuationPoint,
            NodeId ownerSessionId,
            bool portable,
            bool pendingPersistence = false)
        {
            var evicted = new List<HistoryContinuationPoint>();
            HistoryContinuationPoint? admittedPoint = null;
            Exception? admissionFailure = null;
            Exception? cleanupFailure = null;
            try
            {
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSessionClosed,
                            "The session is closed and cannot accept history continuation points.");
                    }
                    if (continuationPoint is Historian.HistorianContinuationState { IsInvalidated: true })
                    {
                        throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                    }
                    if (continuationPoint is Historian.HistorianContinuationState ownedState)
                    {
                        ownedState.ValidateSessionOwner(this);
                    }
                    m_history ??= [];
                    for (int i = 0; i < m_history.Count; i++)
                    {
                        if (m_history[i].Id == continuationPoint.Id)
                        {
                            throw new InvalidOperationException(
                                "The history continuation point identifier is already registered.");
                        }
                    }
                    HistoryRequestScope? current = GetCurrentHistoryRequest();
                    bool useReservation = current != null && current.Reservations > 0;
                    while (!useReservation &&
                        m_maxHistory > 0 &&
                        m_history.Count + m_reservedHistory >= m_maxHistory)
                    {
                        int evictionIndex = FindEvictableHistoryIndex();
                        if (evictionIndex < 0)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNoContinuationPoints,
                                "All history continuation slots are being persisted, claimed, used or reserved.");
                        }
                        HistoryContinuationPoint old = m_history[evictionIndex];
                        m_history.RemoveAt(evictionIndex);
                        TrackCheckedOutHistory(old);
                        evicted.Add(old);
                    }
                    HistoryContinuationPoint? stored = m_checkedOutHistory.Find(
                        entry => ReferenceEquals(entry.Value, continuationPoint));
                    if (stored is null)
                    {
                        stored = new HistoryContinuationPoint { Value = continuationPoint };
                        if (continuationPoint is Historian.HistorianContinuationState state)
                        {
                            state.SetOwnerRelease(this, () => ReleaseHistory(stored));
                        }
                    }
                    else
                    {
                        m_checkedOutHistory.Remove(stored);
                    }
                    stored.Id = continuationPoint.Id;
                    stored.OwnerSessionId = ownerSessionId;
                    stored.Portable = portable;
                    stored.PendingPersistence = pendingPersistence;
                    stored.Claiming = false;
                    stored.Timestamp = DateTime.UtcNow;
                    m_history.Add(stored);
                    if (continuationPoint is Historian.HistorianContinuationState savedState)
                    {
                        savedState.Saved = true;
                    }

                    // Only the request receiving this token pins it; restored client tokens reserve a slot.
                    if (current != null)
                    {
                        if (useReservation)
                        {
                            current.Reservations--;
                            m_reservedHistory--;
                            stored.ReservedBy = current;
                        }
                        current.Pin(stored);
                        current.RecordSaved(stored);
                    }
                    admittedPoint = stored;
                }
            }
            catch (Exception exception)
            {
                admissionFailure = exception;
            }
            try
            {
                DisposeHistoryPoints(evicted);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
                lock (m_lock)
                {
                    if (admittedPoint != null && m_history?.Remove(admittedPoint) == true)
                    {
                        TrackCheckedOutHistory(admittedPoint);
                        HistoryRequestScope? reservedBy = admittedPoint.ReservedBy;
                        admittedPoint.ReservedBy = null;
                        if (reservedBy != null && !reservedBy.Ended)
                        {
                            reservedBy.Reservations++;
                            m_reservedHistory++;
                        }
                    }
                }
            }
            if (admissionFailure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "History continuation admission and eviction cleanup failed.",
                    admissionFailure,
                    cleanupFailure);
            }
            if (admissionFailure != null)
            {
                ExceptionDispatchInfo.Capture(admissionFailure).Throw();
            }
            if (cleanupFailure != null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
            return admittedPoint!;
        }

        private bool CompletePendingHistory(
            HistoryContinuationPoint continuationPoint,
            bool portable)
        {
            lock (m_lock)
            {
                if (m_history?.Contains(continuationPoint) != true)
                {
                    return false;
                }
                continuationPoint.Portable = portable;
                continuationPoint.PendingPersistence = false;
                continuationPoint.ReservedBy = null;
                return true;
            }
        }

        private int FindEvictableHistoryIndex()
        {
            if (m_history == null)
            {
                return -1;
            }
            for (int i = 0; i < m_history.Count; i++)
            {
                HistoryContinuationPoint candidate = m_history[i];
                if (!candidate.PendingPersistence &&
                    !candidate.Claiming &&
                    candidate.Pins == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Marks the start of a HistoryRead request. Until the returned scope is disposed, the
        /// history continuation points the request continues and every point the request saves
        /// are excluded from eviction, so the limit makes a further operation fail with
        /// Bad_NoContinuationPoints instead of dropping a point of the same request or one the
        /// request is about to continue (Part 4 §7.9: only points from prior requests are freed
        /// automatically, and continuing a halted operation never fails for lack of points).
        /// </summary>
        /// <param name="nodesToRead">The operations of the request.</param>
        /// <returns>The scope to dispose once the response has been produced.</returns>
        /// <remarks>
        /// The scope becomes the current HistoryRead of the calling asynchronous flow, so the
        /// points saved by the node managers the request awaits are attributed to it. This
        /// method is synchronous on purpose: the value set here stays in the caller's
        /// execution context until the scope is disposed.
        /// </remarks>
        internal IDisposable BeginHistoryRequest(ArrayOf<HistoryReadValueId> nodesToRead)
        {
            var scope = new HistoryRequestScope(this, s_currentHistoryRequest.Value);
            s_currentHistoryRequest.Value = scope;
            if (!HasHistoryContinuationPoint(nodesToRead))
            {
                return scope;
            }
            lock (m_lock)
            {
                if (m_closed || m_history == null)
                {
                    return scope;
                }
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    HistoryReadValueId? nodeToRead = nodesToRead[ii];
                    if (nodeToRead == null ||
                        !TryGetHistoryContinuationPointId(
                            nodeToRead.ContinuationPoint,
                            out Guid id))
                    {
                        continue;
                    }
                    scope.RecordClientSupplied(id);
                    foreach (HistoryContinuationPoint candidate in m_history)
                    {
                        if (candidate.Id == id)
                        {
                            scope.Pin(candidate);
                            break;
                        }
                    }
                }
            }
            return scope;
        }

        private static bool HasHistoryContinuationPoint(ArrayOf<HistoryReadValueId> nodesToRead)
        {
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                HistoryReadValueId? nodeToRead = nodesToRead[ii];
                if (nodeToRead != null && nodeToRead.ContinuationPoint.Length == 16)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Releases the history continuation points a faulted HistoryRead request saved, since
        /// the response that would return them to the client is never sent (Part 4 §7.9). The
        /// points are tracked by the request scope, so a point a node manager saved before it
        /// failed without assigning it to a result is released as well. A point whose identifier
        /// the client supplied in the request is kept, since the client still holds it.
        /// </summary>
        /// <param name="historyRequest">The scope returned by <see cref="BeginHistoryRequest"/>.</param>
        internal void ReleaseSavedHistory(IDisposable historyRequest)
        {
            if (historyRequest is not HistoryRequestScope scope ||
                !ReferenceEquals(scope.Owner, this))
            {
                return;
            }

            List<HistoryContinuationPoint>? released = null;
            lock (m_lock)
            {
                List<HistoryContinuationPoint>? saved = scope.TakeSaved();
                if (saved == null || m_history == null)
                {
                    return;
                }
                foreach (HistoryContinuationPoint candidate in saved)
                {
                    // a point still being persisted or claimed is cleaned up by that operation.
                    if (scope.IsClientSupplied(candidate.Id) ||
                        candidate.PendingPersistence ||
                        candidate.Claiming ||
                        !m_history.Remove(candidate))
                    {
                        continue;
                    }
                    TrackCheckedOutHistory(candidate);
                    (released ??= []).Add(candidate);
                }
            }

            if (released == null)
            {
                return;
            }
            foreach (HistoryContinuationPoint continuationPoint in released)
            {
                if (continuationPoint.Portable)
                {
                    TryScheduleHistoryRemoval(
                        continuationPoint.OwnerSessionId,
                        continuationPoint.Id);
                }
                continuationPoint.Value.Dispose();
            }
        }

        private void EndHistoryRequest(HistoryRequestScope scope)
        {
            lock (m_lock)
            {
                scope.Ended = true;
                scope.UnpinAll();
                _ = scope.TakeSaved();
                m_reservedHistory -= scope.Reservations;
                scope.Reservations = 0;
            }
        }

        /// <summary>
        /// Tracks the history continuation points pinned by one in-flight HistoryRead request.
        /// All members except the owner and the previous scope are accessed under the owner's lock.
        /// </summary>
        private sealed class HistoryRequestScope : IDisposable
        {
            public HistoryRequestScope(
                SessionContinuationPoints owner,
                HistoryRequestScope? previous)
            {
                Owner = owner;
                m_previous = previous;
            }

            /// <summary>
            /// The holder whose points this scope pins.
            /// </summary>
            public SessionContinuationPoints Owner { get; }

            /// <summary>
            /// Set once the request has returned its response; a late save is not pinned then.
            /// </summary>
            public bool Ended { get; set; }

            /// <summary>
            /// The slots this request holds for the successors of the points it restored to
            /// continue operations the client supplied.
            /// </summary>
            public int Reservations { get; set; }

            public void Pin(HistoryContinuationPoint continuationPoint)
            {
                continuationPoint.Pins++;
                (m_pinned ??= []).Add(continuationPoint);
            }

            /// <summary>
            /// Records a point the request saved, so a faulted request can release it.
            /// </summary>
            public void RecordSaved(HistoryContinuationPoint continuationPoint)
            {
                (m_saved ??= []).Add(continuationPoint);
            }

            /// <summary>
            /// Returns and forgets the points the request saved.
            /// </summary>
            public List<HistoryContinuationPoint>? TakeSaved()
            {
                List<HistoryContinuationPoint>? saved = m_saved;
                m_saved = null;
                return saved;
            }

            /// <summary>
            /// Records a continuation point identifier the client supplied in the request.
            /// </summary>
            public void RecordClientSupplied(Guid id)
            {
                (m_clientSupplied ??= []).Add(id);
            }

            /// <summary>
            /// Reports whether the client supplied the identifier in the request.
            /// </summary>
            public bool IsClientSupplied(Guid id)
            {
                return m_clientSupplied?.Contains(id) == true;
            }

            public void UnpinAll()
            {
                if (m_pinned == null)
                {
                    return;
                }
                foreach (HistoryContinuationPoint continuationPoint in m_pinned)
                {
                    continuationPoint.Pins--;
                }
                m_pinned = null;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    if (ReferenceEquals(s_currentHistoryRequest.Value, this))
                    {
                        s_currentHistoryRequest.Value = m_previous;
                    }
                    Owner.EndHistoryRequest(this);
                }
            }

            private readonly HistoryRequestScope? m_previous;
            private List<HistoryContinuationPoint>? m_pinned;
            private List<HistoryContinuationPoint>? m_saved;
            private HashSet<Guid>? m_clientSupplied;
            private int m_disposed;
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
            if (ownerSessionId.IsNull)
            {
                return;
            }

            if (m_store != null)
            {
                ArrayOf<ContinuationPointEnvelope> envelopes = await m_store
                    .LoadContinuationPointsAsync(ownerSessionId, cancellationToken)
                    .ConfigureAwait(false);

                List<ContinuationPointEnvelope>? lateEnvelopes = null;
                lock (m_lock)
                {
                    if (m_closed)
                    {
                        lateEnvelopes = [];
                        foreach (ContinuationPointEnvelope envelope in envelopes)
                        {
                            if (envelope.Kind is ContinuationPointKind.Browse or ContinuationPointKind.History)
                            {
                                lateEnvelopes.Add(envelope);
                            }
                        }
                    }
                    else
                    {
                        foreach (ContinuationPointEnvelope envelope in envelopes)
                        {
                            switch (envelope.Kind)
                            {
                                case ContinuationPointKind.Browse:
                                    m_mirroredBrowseOwners ??= [];
                                    m_mirroredBrowseOwners[envelope.Id] =
                                        envelope.OwnerSessionId;
                                    break;
                                case ContinuationPointKind.History:
                                    m_mirroredHistoryOwners ??= [];
                                    m_mirroredHistoryOwners[envelope.Id] =
                                        envelope.OwnerSessionId;
                                    break;
                            }
                        }
                    }
                }

                if (lateEnvelopes != null)
                {
                    foreach (ContinuationPointEnvelope envelope in lateEnvelopes)
                    {
                        m_store.RemoveContinuationPoint(
                            envelope.OwnerSessionId,
                            envelope.Kind,
                            envelope.Id);
                    }
                    return;
                }
            }

            if (m_historyStore == null || m_historyCodec == null)
            {
                return;
            }

            ArrayOf<HistoryContinuationPointEnvelope> historyEnvelopes =
                await m_historyStore.LoadAsync(ownerSessionId, cancellationToken)
                    .ConfigureAwait(false);
            NodeId localOwnerSessionId = Id;
            for (int i = 0; i < historyEnvelopes.Count; i++)
            {
                HistoryContinuationPointEnvelope envelope = historyEnvelopes[i];
                if (envelope.OwnerSessionId != ownerSessionId ||
                    envelope.Id == Guid.Empty)
                {
                    continue;
                }
                lock (m_lock)
                {
                    if (ContainsHistoryContinuationPoint(envelope.Id))
                    {
                        continue;
                    }
                }
                IHistoryContinuationPoint? continuationPoint = await m_historyCodec
                    .DecodeAsync(envelope, cancellationToken)
                    .ConfigureAwait(false);
                if (continuationPoint != null)
                {
                    bool transferred = localOwnerSessionId == ownerSessionId;
                    HistoryContinuationPointEnvelope localEnvelope = envelope;
                    if (!transferred)
                    {
                        localEnvelope = envelope with
                        {
                            OwnerSessionId = localOwnerSessionId
                        };
                        try
                        {
                            await m_historyStore.StoreAsync(
                                localEnvelope,
                                cancellationToken).ConfigureAwait(false);
                            transferred = await m_historyStore.TryTakeAsync(
                                ownerSessionId,
                                envelope.Id,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            TryScheduleHistoryRemoval(
                                localEnvelope.OwnerSessionId,
                                localEnvelope.Id);
                            continuationPoint.Dispose();
                            throw;
                        }
                    }
                    if (!transferred)
                    {
                        TryScheduleHistoryRemoval(
                            localEnvelope.OwnerSessionId,
                            localEnvelope.Id);
                        continuationPoint.Dispose();
                        continue;
                    }
                    try
                    {
                        _ = AddHistoryContinuationPoint(
                            continuationPoint,
                            localEnvelope.OwnerSessionId,
                            portable: true);
                    }
                    catch
                    {
                        TryScheduleHistoryRemoval(
                            localEnvelope.OwnerSessionId,
                            localEnvelope.Id);
                        continuationPoint.Dispose();
                        throw;
                    }
                }
            }
        }

        private bool ContainsHistoryContinuationPoint(Guid id)
        {
            if (m_checkedOutHistory.Exists(entry => entry.Id == id))
            {
                return true;
            }
            if (m_history == null)
            {
                return false;
            }
            for (int i = 0; i < m_history.Count; i++)
            {
                if (m_history[i].Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes and disposes all continuation points (called when the session is closed or discarded).
        /// </summary>
        public void Clear()
        {
            var browseCPs = new List<ContinuationPoint>();
            var historyCPs = new List<HistoryContinuationPoint>();
            var mirrored = new List<(NodeId OwnerSessionId, ContinuationPointKind Kind, Guid Id)>();
            lock (m_lock)
            {
                m_closed = true;
                if (m_browse != null)
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
                foreach (HistoryContinuationPoint entry in m_checkedOutHistory)
                {
                    if (entry.Value is Historian.HistorianContinuationState state)
                    {
                        state.Invalidate();
                    }
                }
                for (int ii = (m_history?.Count ?? 0) - 1; ii >= 0; ii--)
                {
                    HistoryContinuationPoint entry = m_history![ii];
                    if (entry.Value is Historian.HistorianContinuationState state)
                    {
                        state.Invalidate();
                    }
                    if (entry.Claiming)
                    {
                        continue;
                    }
                    m_history.RemoveAt(ii);
                    TrackCheckedOutHistory(entry);
                    historyCPs.Add(entry);
                }
                if (m_mirroredBrowseOwners != null)
                {
                    foreach (KeyValuePair<Guid, NodeId> pair in m_mirroredBrowseOwners)
                    {
                        mirrored.Add((pair.Value, ContinuationPointKind.Browse, pair.Key));
                    }
                }
                if (m_mirroredHistoryOwners != null)
                {
                    foreach (KeyValuePair<Guid, NodeId> pair in m_mirroredHistoryOwners)
                    {
                        mirrored.Add((pair.Value, ContinuationPointKind.History, pair.Key));
                    }
                }
                m_mirroredBrowseOwners = null;
                m_mirroredHistoryOwners = null;
            }

            try
            {
                foreach ((NodeId ownerSessionId, ContinuationPointKind kind, Guid id) in mirrored)
                {
                    m_store?.RemoveContinuationPoint(ownerSessionId, kind, id);
                }
            }
            finally
            {
                try
                {
                    DisposeBrowsePoints(browseCPs);
                }
                finally
                {
                    DisposeHistoryPoints(historyCPs);
                }
            }
        }

        private ContinuationPointEnvelope CreateHistoryEnvelope(Guid id)
        {
            return new ContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = NormalizeNodeId(Id),
                Kind = ContinuationPointKind.History
            };
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

        private static NodeId NormalizeNodeId(NodeId nodeId)
        {
            return nodeId.IsNull ? NodeId.Null : nodeId;
        }

        private void TryScheduleHistoryRemoval(
            NodeId ownerSessionId,
            Guid id)
        {
            try
            {
                m_historyStore?.ScheduleRemove(ownerSessionId, id);
            }
            catch (InvalidOperationException)
            {
                // Cleanup must never abort session teardown or eviction.
            }
            catch (ServiceResultException)
            {
                // Cleanup must never abort session teardown or eviction.
            }
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

        private sealed class HistoryContinuationPoint
        {
            public Guid Id;
            public NodeId OwnerSessionId;
            public bool Portable;
            public bool PendingPersistence;
            public bool Claiming;
            public int Pins;
            public IHistoryContinuationPoint Value = null!;
            public DateTime Timestamp;

            /// <summary>
            /// The request whose reserved slot this point consumed when it was saved, so the
            /// reservation can be given back if the point is dropped because persisting it failed.
            /// </summary>
            public HistoryRequestScope? ReservedBy;
        }

        private readonly Func<NodeId> m_sessionIdProvider;
        private readonly int m_maxHistory;
        private readonly IContinuationPointStore? m_store;
        private readonly IHistoryContinuationPointStore? m_historyStore;
        private readonly IHistoryContinuationPointCodec? m_historyCodec;
        private readonly NamespaceTable m_namespaceUris;
        private readonly Lock m_lock = new();
        private List<BrowseContinuationPoint>? m_browse;
        private int m_availableBrowse;
        private readonly List<HistoryContinuationPoint> m_checkedOutHistory = [];
        private readonly AsyncLocal<HistoryReadScope?> m_historyRead = new();
        private List<HistoryContinuationPoint>? m_history;
        private Dictionary<Guid, NodeId>? m_mirroredBrowseOwners;
        private Dictionary<Guid, NodeId>? m_mirroredHistoryOwners;
        private int m_reservedHistory;
        private bool m_closed;

        /// <summary>
        /// The HistoryRead request of the current asynchronous flow; it pins the history
        /// continuation points saved while it is in flight.
        /// </summary>
        private static readonly AsyncLocal<HistoryRequestScope?> s_currentHistoryRequest = new();
    }
}
