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
    internal sealed class SessionContinuationPoints : ISessionContinuationPoints
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
            if (maxBrowse <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBrowse));
            }
            if (maxHistory <= 0)
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
        /// Gets or sets the maximum number of browse continuation points retained before the oldest is dropped.
        /// </summary>
        public int MaxBrowse { get; set; }

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

            lock (m_lock)
            {
                m_browse ??= [];

                // remove the first continuation point if too many points.
                while (m_browse.Count >= MaxBrowse)
                {
                    ContinuationPoint cp = m_browse[0];
                    m_browse.RemoveAt(0);
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.Browse, cp.Id);
                    cp?.Dispose();
                }

                // add to end of list.
                m_browse.Add(continuationPoint);
            }

            m_store?.StoreContinuationPoint(CreateBrowseEnvelope(continuationPoint));
        }

        /// <summary>
        /// Restores (and removes) a browse continuation point. The caller disposes the returned point.
        /// </summary>
        public ContinuationPoint? RestoreBrowse(ByteString continuationPoint)
        {
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
                    if (m_browse[ii].Id == id)
                    {
                        ContinuationPoint cp = m_browse[ii];
                        m_browse.RemoveAt(ii);
                        m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.Browse, id);
                        return cp;
                    }
                }

                if (m_mirroredBrowseOwners != null &&
                    m_mirroredBrowseOwners.TryGetValue(id, out NodeId ownerSessionId))
                {
                    m_mirroredBrowseOwners.Remove(id);
                    m_store?.RemoveContinuationPoint(ownerSessionId, ContinuationPointKind.Browse, id);
                }

                return null;
            }
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
                    ContinuationPoint continuationPoint = m_browse[ii];
                    if (!ReferenceEquals(continuationPoint.Manager, nodeManager) &&
                        !ReferenceEquals(
                            continuationPoint.Manager.SyncNodeManager,
                            nodeManager.SyncNodeManager))
                    {
                        continue;
                    }

                    m_browse.RemoveAt(ii);
                    removed ??= [];
                    removed.Add(continuationPoint);
                }
            }

            if (removed == null)
            {
                return;
            }

            // Persisting and disposing runs outside the lock, because a continuation point belongs
            // to the NodeManager being retired and its disposal must not block unrelated Browse
            // operations, or re-enter this session while the lock is held.
            foreach (ContinuationPoint continuationPoint in removed)
            {
                m_store?.RemoveContinuationPoint(
                    Id,
                    ContinuationPointKind.Browse,
                    continuationPoint.Id);
                continuationPoint.Dispose();
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
                    if (!IsOwnedBy(continuationPoint.Value, nodeManager))
                    {
                        continue;
                    }

                    m_history.RemoveAt(ii);
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
            foreach (HistoryContinuationPoint continuationPoint in removed)
            {
                m_store?.RemoveContinuationPoint(
                    Id,
                    ContinuationPointKind.History,
                    continuationPoint.Id);
                if (continuationPoint.Portable)
                {
                    TryScheduleHistoryRemoval(
                        continuationPoint.OwnerSessionId,
                        continuationPoint.Id);
                }
                (continuationPoint.Value as IDisposable)?.Dispose();
            }
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

            try
            {
                _ = AddHistoryContinuationPoint(
                    continuationPoint,
                    Id,
                    portable: false);
            }
            catch
            {
                continuationPoint.Dispose();
                throw;
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
                continuationPoint.Dispose();
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
            lock (m_lock)
            {
                if (!TryGetHistoryContinuationPointId(
                        continuationPoint,
                        out Guid id) ||
                    m_history == null)
                {
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
                        return restored.Value;
                    }
                }

                RemoveMirroredHistoryOwner(id);
                return null;
            }
        }

        /// <inheritdoc/>
        public bool ReleaseHistory(ByteString continuationPoint)
        {
            HistoryContinuationPoint? released = null;
            lock (m_lock)
            {
                if (!TryGetHistoryContinuationPointId(
                        continuationPoint,
                        out Guid id) ||
                    m_history == null)
                {
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
            released.Value.Dispose();
            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<IHistoryContinuationPoint?> RestoreHistoryAsync(
            ByteString continuationPoint,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetHistoryContinuationPointId(
                    continuationPoint,
                    out Guid id))
            {
                return null;
            }
            HistoryContinuationPoint? restored = null;
            lock (m_lock)
            {
                if (m_history == null)
                {
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
                        return candidate.Value;
                    }
                    if (candidate.Claiming || m_historyStore == null)
                    {
                        return null;
                    }
                    candidate.Claiming = true;
                    restored = candidate;
                    break;
                }
                if (restored == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return null;
                }
            }

            bool claimed;
            try
            {
                claimed = await m_historyStore!.TryTakeAsync(
                    restored.OwnerSessionId,
                    restored.Id,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ResetHistoryClaim(restored);
                throw;
            }

            bool removed = RemoveClaimedHistory(restored);
            if (!removed)
            {
                return null;
            }
            if (!claimed)
            {
                restored.Value.Dispose();
                return null;
            }
            return restored.Value;
        }

        private void ResetHistoryClaim(HistoryContinuationPoint continuationPoint)
        {
            lock (m_lock)
            {
                List<HistoryContinuationPoint>? history = m_history;
                if (history != null && history.Contains(continuationPoint))
                {
                    continuationPoint.Claiming = false;
                }
            }
        }

        private bool RemoveClaimedHistory(
            HistoryContinuationPoint continuationPoint)
        {
            lock (m_lock)
            {
                List<HistoryContinuationPoint>? history = m_history;
                return history != null && history.Remove(continuationPoint);
            }
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
            lock (m_lock)
            {
                if (m_closed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSessionClosed,
                        "The session is closed and cannot accept history continuation points.");
                }
                m_history ??= [];
                for (int i = 0; i < m_history.Count; i++)
                {
                    HistoryContinuationPoint existing = m_history[i];
                    if (existing.Id == continuationPoint.Id)
                    {
                        throw new InvalidOperationException(
                            "The history continuation point identifier is already registered.");
                    }
                }

                while (m_history.Count >= m_maxHistory)
                {
                    int evictionIndex = FindEvictableHistoryIndex();
                    if (evictionIndex < 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNoContinuationPoints,
                            "All history continuation slots are being persisted or claimed.");
                    }
                    HistoryContinuationPoint old =
                        m_history[evictionIndex];
                    m_history.RemoveAt(evictionIndex);
                    if (old.Portable)
                    {
                        TryScheduleHistoryRemoval(
                            old.OwnerSessionId,
                            old.Id);
                    }
                    old.Value.Dispose();
                }

                var stored = new HistoryContinuationPoint
                {
                    Id = continuationPoint.Id,
                    OwnerSessionId = ownerSessionId,
                    Portable = portable,
                    PendingPersistence = pendingPersistence,
                    Value = continuationPoint,
                    Timestamp = DateTime.UtcNow
                };
                m_history.Add(stored);
                return stored;
            }
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
                if (!candidate.PendingPersistence && !candidate.Claiming)
                {
                    return i;
                }
            }
            return -1;
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

                lock (m_lock)
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
            List<ContinuationPoint>? browseCPs;
            List<HistoryContinuationPoint>? historyCPs;
            lock (m_lock)
            {
                m_closed = true;
                browseCPs = m_browse;
                m_browse = null;
                historyCPs = m_history;
                m_history = null;
                m_mirroredBrowseOwners = null;
                m_mirroredHistoryOwners = null;
            }

            if (browseCPs != null)
            {
                for (int ii = 0; ii < browseCPs.Count; ii++)
                {
                    ContinuationPoint cp = browseCPs[ii];
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.Browse, cp.Id);
                    cp.Dispose();
                }
            }

            if (historyCPs != null)
            {
                for (int ii = 0; ii < historyCPs.Count; ii++)
                {
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, historyCPs[ii].Id);
                    if (historyCPs[ii].Portable)
                    {
                        TryScheduleHistoryRemoval(
                            historyCPs[ii].OwnerSessionId,
                            historyCPs[ii].Id);
                    }
                    historyCPs[ii].Value.Dispose();
                }
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

        private sealed class HistoryContinuationPoint
        {
            public Guid Id;
            public NodeId OwnerSessionId;
            public bool Portable;
            public bool PendingPersistence;
            public bool Claiming;
            public IHistoryContinuationPoint Value = null!;
            public DateTime Timestamp;
        }

        private readonly Func<NodeId> m_sessionIdProvider;
        private readonly int m_maxHistory;
        private readonly IContinuationPointStore? m_store;
        private readonly IHistoryContinuationPointStore? m_historyStore;
        private readonly IHistoryContinuationPointCodec? m_historyCodec;
        private readonly NamespaceTable m_namespaceUris;
        private readonly Lock m_lock = new();
        private List<ContinuationPoint>? m_browse;
        private List<HistoryContinuationPoint>? m_history;
        private Dictionary<Guid, NodeId>? m_mirroredBrowseOwners;
        private Dictionary<Guid, NodeId>? m_mirroredHistoryOwners;
        private bool m_closed;
    }
}
