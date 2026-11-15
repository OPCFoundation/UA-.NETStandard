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
using System.Buffers.Binary;
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
        ISessionContinuationPoints
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
            return HasBrowseForGeneration(NodeManagerGenerationRegistry.GetOrCreate(nodeManager));
        }

        internal bool HasBrowseForGeneration(NodeManagerGeneration generation)
        {
            lock (m_lock)
            {
                return m_browse?.Exists(point => IsOwnedBy(point.Point, generation)) == true;
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

                if (restored is null &&
                    m_mirroredBrowseOwners != null &&
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
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            RemoveForGeneration(NodeManagerGenerationRegistry.GetOrCreate(nodeManager));
        }

        internal void RemoveForGeneration(NodeManagerGeneration generation)
        {
            var failures = new List<Exception>();
            try
            {
                RemoveBrowseForGeneration(generation);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            try
            {
                RemoveHistoryForGeneration(generation);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("Continuation cleanup for the NodeManager failed.", failures);
            }
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
            RemoveBrowseForGeneration(NodeManagerGenerationRegistry.GetOrCreate(nodeManager));
        }

        internal void RemoveBrowseForGeneration(NodeManagerGeneration generation)
        {
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
                    if (!IsOwnedBy(entry.Point, generation))
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

        /// <summary>
        /// Determines whether a continuation point requires the specified node manager.
        /// </summary>
        /// <param name="point">The continuation point.</param>
        /// <param name="generation">The generation represented by the continuation point.</param>
        /// <returns>True if the continuation point was issued by the generation.</returns>
        private static bool IsOwnedBy(ContinuationPoint point, NodeManagerGeneration generation)
        {
            return point.Generation is { } reference && reference.Identifies(generation);
        }

        /// <summary>
        /// Removes a browse continuation point from the session and raises the released notification.
        /// </summary>
        /// <param name="entry">The browse continuation point.</param>
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

        /// <summary>
        /// Removes browse continuation points from the store and disposes them, aggregating failures.
        /// </summary>
        /// <param name="points">The continuation points to dispose.</param>
        private void DisposeBrowsePoints(List<ContinuationPoint> points)
        {
            List<Exception>? failures = null;
            foreach (ContinuationPoint point in points)
            {
                try
                {
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.Browse, point.Id);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
                try
                {
                    point.Dispose();
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
            RemoveHistoryForGeneration(NodeManagerGenerationRegistry.GetOrCreate(nodeManager));
        }

        internal void RemoveHistoryForGeneration(NodeManagerGeneration generation)
        {
            List<HistoryContinuationPoint>? removed = null;
            List<Exception>? failures = null;
            lock (m_lock)
            {
                PruneCallerOwnedHistory();
                foreach (HistoryContinuationPoint checkedOut in m_checkedOutHistory)
                {
                    if (!IsOwnedBy(checkedOut, generation))
                    {
                        continue;
                    }
                    checkedOut.Invalidated = true;
                    if (checkedOut.CleanupFailure is { } failure)
                    {
                        (failures ??= []).Add(failure);
                    }
                    if (checkedOut.GetValueOrNull() is Historian.HistorianContinuationState state)
                    {
                        state.Invalidate();
                    }
                }
                for (int ii = (m_history?.Count ?? 0) - 1; ii >= 0; ii--)
                {
                    HistoryContinuationPoint continuationPoint = m_history![ii];
                    if (!IsOwnedBy(continuationPoint, generation))
                    {
                        continue;
                    }

                    if (continuationPoint.Value is Historian.HistorianContinuationState state)
                    {
                        state.Invalidate();
                    }
                    continuationPoint.Invalidated = true;
                    if (continuationPoint.PendingPersistence || continuationPoint.Claiming)
                    {
                        continue;
                    }
                    m_history.RemoveAt(ii);
                    TrackHistoryCleanup(continuationPoint);
                    removed ??= [];
                    removed.Add(continuationPoint);
                }
            }

            if (removed is not null)
            {
                // Persisting and disposing runs outside the lock, for the same reason as the Browse
                // continuation points: the state belongs to the NodeManager being retired.
                try
                {
                    DisposeHistoryPoints(removed);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }

            if (failures is { Count: 1 })
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures is { Count: > 1 })
            {
                throw new AggregateException("History continuation cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Reports whether the holder recorded the exact generation as an owner of a history continuation.
        /// A point without a recorded generation is left alone rather than dropped on a guess.
        /// </summary>
        private static bool IsOwnedBy(
            HistoryContinuationPoint continuationPoint,
            NodeManagerGeneration generation)
        {
            return continuationPoint.Generation is { } reference &&
                reference.Identifies(generation);
        }

        /// <inheritdoc/>
        public bool HasHistoryForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            return HasHistoryForGeneration(NodeManagerGenerationRegistry.GetOrCreate(nodeManager));
        }

        internal bool HasHistoryForGeneration(NodeManagerGeneration generation)
        {
            lock (m_lock)
            {
                PruneCallerOwnedHistory();
                return m_history?.Exists(entry => IsOwnedBy(entry, generation)) == true ||
                    m_checkedOutHistory.Exists(entry => IsOwnedBy(entry, generation));
            }
        }

        /// <summary>
        /// Begins a history read scope for a continuation token in the current asynchronous flow.
        /// </summary>
        /// <param name="token">The continuation point token.</param>
        /// <param name="point">The restored continuation point, or null.</param>
        /// <returns>The scope to dispose when the read ends.</returns>
        internal HistoryReadScope BeginHistoryRead(ByteString token, IHistoryContinuationPoint? point)
        {
            if (point is not null)
            {
                lock (m_lock)
                {
                    HistoryContinuationPoint? entry =
                        m_history?.Find(candidate => ReferenceEquals(candidate.Value, point)) ??
                        FindCheckedOutHistory(point);
                    if (entry is null)
                    {
                        entry = new HistoryContinuationPoint
                        {
                            Id = point.Id,
                            Value = point,
                            Generation = point is Historian.HistorianContinuationState state
                                ? state.Ownership.Generation
                                : null
                        };
                        m_checkedOutHistory.Add(entry);
                    }
                    entry.Value = point;
                    AdmitHistoryRead(entry);
                }
            }
            var scope = new HistoryReadScope(this, m_historyRead.Value, token, point);
            m_historyRead.Value = scope;
            return scope;
        }

        /// <summary>
        /// Restores a point and reserves its read ownership before another operation can release it.
        /// </summary>
        /// <param name="token">The continuation point token.</param>
        /// <param name="cancellationToken">The token used to cancel restoration.</param>
        /// <returns>A scope that the caller activates before invoking the node manager.</returns>
        internal async ValueTask<HistoryReadScope> RestoreHistoryReadAsync(
            ByteString token,
            CancellationToken cancellationToken)
        {
            ByteString capturedToken = token.Length == 16 ? token.Copy() : token;
            var scope = new HistoryReadScope(this, m_historyRead.Value, capturedToken, point: null);
            try
            {
                IHistoryContinuationPoint? point = await RestoreHistoryCoreAsync(
                    capturedToken, scope, cancellationToken).ConfigureAwait(false);
                scope.SetRestoredPoint(point);
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Activates a restored read scope in the caller's asynchronous flow.
        /// </summary>
        /// <param name="scope">The scope returned by <see cref="RestoreHistoryReadAsync"/>.</param>
        /// <exception cref="InvalidOperationException">
        /// The scope is inactive, belongs to another holder or does not follow the current flow's read scope.
        /// </exception>
        internal void ActivateHistoryRead(HistoryReadScope scope)
        {
            if (!scope.CanActivate(this, m_historyRead.Value))
            {
                throw new InvalidOperationException("The history read scope cannot be activated in this flow.");
            }
            m_historyRead.Value = scope;
        }

        /// <summary>
        /// Associates a saved continuation token with the generation that produced it.
        /// </summary>
        /// <param name="token">The continuation point token.</param>
        /// <param name="generation">The issuing generation.</param>
        internal void BindHistoryGeneration(ByteString token, NodeManagerGeneration generation)
        {
            if (!TryGetHistoryContinuationPointId(token, out Guid id))
            {
                return;
            }
            lock (m_lock)
            {
                PruneCallerOwnedHistory();
                HistoryContinuationPoint? entry =
                    m_history?.Find(candidate => candidate.Id == id) ??
                    m_checkedOutHistory.Find(candidate => candidate.Id == id);
                if (entry is null)
                {
                    return;
                }
                entry.Generation = generation.Reference;
                if (entry.GetValueOrNull() is Historian.HistorianContinuationState state)
                {
                    state.Ownership.Generation = generation.Reference;
                }
            }
        }

        /// <summary>
        /// Identifies one manager callback within a history request.
        /// </summary>
        /// <param name="historyRequest">The exact request that owns the dispatch.</param>
        /// <param name="generation">The generation that receives the callback.</param>
        /// <returns>The scope to close after binding the callback's results.</returns>
        /// <exception cref="InvalidOperationException">
        /// The request is not a history request owned by this holder, or it has already ended.
        /// </exception>
        internal IDisposable BeginHistoryDispatch(IDisposable historyRequest, NodeManagerGeneration generation)
        {
            lock (m_lock)
            {
                if (historyRequest is not HistoryRequestScope scope ||
                    !ReferenceEquals(scope.Owner, this) ||
                    scope.Ended)
                {
                    throw new InvalidOperationException("The history request is no longer active.");
                }
                var dispatch = new HistoryDispatchScope(scope, generation.Reference, s_currentHistoryDispatch.Value);
                s_currentHistoryDispatch.Value = dispatch;
                return dispatch;
            }
        }

        /// <summary>
        /// Binds a returned token only to an exact admission made by this manager callback.
        /// </summary>
        /// <param name="historyRequest">The request that owns the dispatch.</param>
        /// <param name="historyDispatch">The exact callback that returned the token.</param>
        /// <param name="token">The continuation token returned by the manager.</param>
        /// <param name="generation">The issuing generation.</param>
        /// <returns>Whether the exact saved point was still available and could be bound.</returns>
        internal bool TryBindHistoryResult(
            IDisposable historyRequest,
            IDisposable historyDispatch,
            ByteString token,
            NodeManagerGeneration generation)
        {
            if (!TryGetHistoryContinuationPointId(token, out Guid id))
            {
                return false;
            }
            lock (m_lock)
            {
                if (historyRequest is not HistoryRequestScope scope ||
                    !ReferenceEquals(scope.Owner, this) ||
                    scope.Ended ||
                    historyDispatch is not HistoryDispatchScope dispatch ||
                    !dispatch.CanBind(scope, generation) ||
                    !scope.TryFindSavedFor(id, dispatch, out SavedHistoryPoint saved))
                {
                    return false;
                }
                HistoryContinuationPoint entry = saved.Entry;
                if (entry.Id != id ||
                    entry.SaveSequence != saved.SaveSequence ||
                    m_history?.Contains(entry) != true ||
                    entry.Invalidated ||
                    entry.PendingPersistence ||
                    entry.Claiming ||
                    entry.CleanupPending ||
                    entry.CleanupFailure is not null ||
                    (entry.Generation is { } existing && !existing.Identifies(generation)))
                {
                    return false;
                }
                if (entry.Value is Historian.HistorianContinuationState state)
                {
                    if (state.Ownership.Generation is { } issuer && !issuer.Identifies(generation))
                    {
                        return false;
                    }
                    state.Ownership.Generation ??= generation.Reference;
                }
                entry.Generation ??= generation.Reference;
                return true;
            }
        }

        /// <summary>
        /// Gets the issuing generation of a checked-out continuation without retaining it.
        /// </summary>
        /// <param name="point">The continuation point.</param>
        /// <returns>The weak generation identity, or null if it was not captured.</returns>
        internal NodeManagerGenerationReference? GetHistoryGeneration(IHistoryContinuationPoint point)
        {
            if (point is Historian.HistorianContinuationState state &&
                state.Ownership.Generation is { } ownership)
            {
                return ownership;
            }
            lock (m_lock)
            {
                return FindCheckedOutHistory(point)?.Generation;
            }
        }

        /// <summary>
        /// Gets the provider of the history continuation being resumed for a node.
        /// </summary>
        /// <param name="nodeId">The node being read.</param>
        /// <returns>The provider, or null if no matching continuation is being resumed.</returns>
        internal Historian.IHistorianProvider? GetRestoredHistoryProvider(NodeId nodeId)
        {
            return m_historyRead.Value?.GetRestoredProvider(nodeId);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A failed resave preserves ownership already transferred to an opaque point's caller.
        /// </remarks>
        public void SaveHistory(IHistoryContinuationPoint continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            bool persisted = false;
            bool reserved = false;
            Guid reservedId = default;
            NodeId ownerSessionId = NodeId.Null;
            try
            {
                if (continuationPoint is Historian.HistorianContinuationState state)
                {
                    state.ValidateSessionOwner(this);
                }
                if (m_store != null)
                {
                    reservedId = continuationPoint.Id;
                    ownerSessionId = Id;
                    ReserveMirroredHistorySave(continuationPoint, reservedId);
                    reserved = true;
                    m_store.StoreContinuationPoint(CreateHistoryEnvelope(reservedId, ownerSessionId));
                    persisted = true;
                }
                _ = AddHistoryContinuationPoint(
                    continuationPoint,
                    reserved ? ownerSessionId : Id,
                    portable: false,
                    mirroredSaveReservation: reserved ? reservedId : null);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                var cleanupFailures = new List<Exception>();
                if (persisted)
                {
                    try
                    {
                        m_store!.RemoveContinuationPoint(
                            ownerSessionId, ContinuationPointKind.History, reservedId);
                    }
                    catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                    {
                        cleanupFailures.Add(cleanupFailure);
                    }
                }
                if (reserved)
                {
                    ReleaseMirroredHistorySave(reservedId);
                    reserved = false;
                }
                try
                {
                    DisposeUnretainedHistoryPoint(continuationPoint);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    cleanupFailures.Add(cleanupFailure);
                }
                if (cleanupFailures.Count != 0)
                {
                    cleanupFailures.Insert(0, failure);
                    throw new AggregateException("History continuation save and cleanup failed.", cleanupFailures);
                }
                throw;
            }
            finally
            {
                if (reserved)
                {
                    ReleaseMirroredHistorySave(reservedId);
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A failed resave preserves ownership already transferred to an opaque point's caller.
        /// </remarks>
        public async ValueTask SaveHistoryAsync(
            IHistoryContinuationPoint continuationPoint,
            CancellationToken cancellationToken = default)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            HistoryContinuationPoint local;
            bool callerOwned;
            try
            {
                (local, callerOwned) = AddHistoryContinuationPoint(
                    continuationPoint,
                    Id,
                    portable: false,
                    pendingPersistence:
                        m_historyStore != null && m_historyCodec != null);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                try
                {
                    DisposeUnretainedHistoryPoint(continuationPoint);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "History continuation save and cleanup failed.", failure, cleanupFailure);
                }
                throw;
            }
            if (m_historyStore == null || m_historyCodec == null)
            {
                return;
            }

            HistoryContinuationPointEnvelope? envelope = null;
            bool storeAttempted = false;
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
                ValidatePendingHistory(local);
                storeAttempted = true;
                await m_historyStore
                    .StoreAsync(envelope, cancellationToken)
                    .ConfigureAwait(false);
                CompletePendingHistory(local, portable: true);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                var cleanupFailures = new List<Exception>();
                if (storeAttempted && envelope != null)
                {
                    try
                    {
                        ScheduleHistoryRemoval(
                            envelope.OwnerSessionId,
                            envelope.Id);
                    }
                    catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                    {
                        cleanupFailures.Add(cleanupFailure);
                    }
                }
                bool removed;
                lock (m_lock)
                {
                    removed = m_history?.Remove(local) == true;
                    if (removed)
                    {
                        if (callerOwned)
                        {
                            local.PendingPersistence = false;
                            TrackCheckedOutHistory(local, includeOpaque: true, callerOwned: true);
                        }
                        else
                        {
                            TrackHistoryCleanup(local);
                        }
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
                if (removed && !callerOwned)
                {
                    try
                    {
                        DisposeReleasedHistory(local);
                    }
                    catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                    {
                        cleanupFailures.Add(cleanupFailure);
                    }
                }
                if (cleanupFailures.Count != 0)
                {
                    cleanupFailures.Insert(0, failure);
                    throw new AggregateException("History continuation save and cleanup failed.", cleanupFailures);
                }
                throw;
            }
        }

        /// <summary>
        /// Restores (and removes) a previously saved history continuation point, or <c>null</c> when not found.
        /// </summary>
        public IHistoryContinuationPoint? RestoreHistory(ByteString continuationPoint)
        {
            if (m_historyRead.Value is { } current)
            {
                if (current.TryRestore(continuationPoint, requirePoint: false, out var scopedPoint, out bool expired))
                {
                    return scopedPoint;
                }
                if (expired)
                {
                    return null;
                }
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
                            restored.PendingPersistence ||
                            restored.Claiming ||
                            restored.ActiveReads != 0)
                        {
                            return null;
                        }
                        m_history.RemoveAt(i);
                        point = restored.Value;
                        TrackCheckedOutHistory(restored, includeOpaque: true, callerOwned: true);
                        ReserveForContinuedOperation(id);
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
                    TrackHistoryCleanup(candidate);
                    released = candidate;
                    break;
                }
                if (released == null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return false;
                }
            }
            CleanupReleasedHistory(released);
            return true;
        }

        /// <summary>
        /// Removes a released point's mirrors and disposes its exact value.
        /// </summary>
        /// <param name="released">The point removed from the available history list.</param>
        /// <exception cref="AggregateException">
        /// More than one step fails while removing the point's portable or local mirrors and disposing the point.
        /// </exception>
        private void CleanupReleasedHistory(HistoryContinuationPoint released)
        {
            var failures = new List<Exception>();
            try
            {
                if (released.Portable)
                {
                    ScheduleHistoryRemoval(released.OwnerSessionId, released.Id);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            try
            {
                m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, released.Id);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            try
            {
                DisposeReleasedHistory(released);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("History continuation cleanup failed.", failures);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IHistoryContinuationPoint?> RestoreHistoryAsync(
            ByteString continuationPoint,
            CancellationToken cancellationToken = default)
        {
            return RestoreHistoryCoreAsync(continuationPoint, readScope: null, cancellationToken);
        }

        /// <summary>
        /// Restores a point and optionally reserves its exact entry for a history read.
        /// </summary>
        /// <param name="continuationPoint">The token supplied by the caller.</param>
        /// <param name="readScope">The read owner, or null for a caller-owned restore.</param>
        /// <param name="cancellationToken">The token used to cancel a portable claim.</param>
        /// <returns>The restored point, or null if it is unavailable.</returns>
        /// <exception cref="AggregateException">
        /// Removing the local mirror or claiming the portable point fails,
        /// and the corresponding read-ownership or claim cleanup also fails.
        /// </exception>
        private async ValueTask<IHistoryContinuationPoint?> RestoreHistoryCoreAsync(
            ByteString continuationPoint,
            HistoryReadScope? readScope,
            CancellationToken cancellationToken)
        {
            bool admitRead = readScope is not null;
            if (m_historyRead.Value is { } current)
            {
                if (admitRead)
                {
                    if (current.MatchesToken(continuationPoint))
                    {
                        return null;
                    }
                }
                else
                {
                    if (current.TryRestore(
                        continuationPoint, requirePoint: true, out var scopedPoint, out bool expired))
                    {
                        return scopedPoint;
                    }
                    if (expired)
                    {
                        return null;
                    }
                }
            }
            if (!TryGetHistoryContinuationPointId(
                    continuationPoint,
                    out Guid id))
            {
                return null;
            }
            HistoryContinuationPoint? restored = null;
            HistoryContinuationPoint? rejected = null;
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
                    if (candidate.PendingPersistence || candidate.ActiveReads != 0)
                    {
                        return null;
                    }
                    if (candidate.Portable && (candidate.Claiming || m_historyStore == null))
                    {
                        return null;
                    }
                    if (readScope is not null &&
                        candidate.Generation is { } generationReference &&
                        (!generationReference.TryGetGeneration(out NodeManagerGeneration? generation) ||
                            generation is null ||
                            !readScope.TryAdmitGeneration(generation)))
                    {
                        candidate.Invalidated = true;
                        m_history.RemoveAt(i);
                        TrackHistoryCleanup(candidate);
                        rejected = candidate;
                        break;
                    }
                    if (!candidate.Portable)
                    {
                        m_history.RemoveAt(i);
                        localPoint = candidate.Value;
                        TrackCheckedOutHistory(candidate, includeOpaque: true, callerOwned: !admitRead);
                        ReserveForContinuedOperation(id);
                        if (admitRead)
                        {
                            AdmitHistoryRead(candidate);
                        }
                        break;
                    }
                    candidate.Claiming = true;
                    restored = candidate;
                    break;
                }
                if (restored == null && localPoint == null && rejected is null)
                {
                    RemoveMirroredHistoryOwner(id);
                    return null;
                }
            }

            if (rejected is not null)
            {
                CleanupReleasedHistory(rejected);
                return null;
            }
            if (localPoint != null)
            {
                try
                {
                    RemoveLocalHistoryMirror(localPoint);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    if (admitRead)
                    {
                        try
                        {
                            EndHistoryRead(localPoint, disposePoint: false, readScope);
                        }
                        catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                        {
                            throw new AggregateException(
                                "History continuation restoration and cleanup failed.", failure, cleanupFailure);
                        }
                    }
                    throw;
                }
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
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                try
                {
                    ResetHistoryClaim(portablePoint);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "History continuation claim and cleanup failed.", failure, cleanupFailure);
                }
                throw;
            }

            IHistoryContinuationPoint claimedPoint = portablePoint.Value;
            bool removed = RemoveClaimedHistory(
                portablePoint,
                reserve: claimed,
                admitRead: admitRead && claimed,
                out bool invalidated);
            if (!removed)
            {
                return null;
            }
            if (invalidated)
            {
                DisposeReleasedHistory(portablePoint);
                return null;
            }
            if (!claimed)
            {
                DisposeHistoryReadPoint(claimedPoint);
                return null;
            }
            return claimedPoint;
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
                        continuationPoint.Invalidated ||
                        continuationPoint.Value is Historian.HistorianContinuationState { IsInvalidated: true })
                    {
                        history.Remove(continuationPoint);
                        TrackHistoryCleanup(continuationPoint);
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
            bool reserve,
            bool admitRead,
            out bool invalidated)
        {
            lock (m_lock)
            {
                List<HistoryContinuationPoint>? history = m_history;
                if (history == null || !history.Remove(continuationPoint))
                {
                    invalidated = false;
                    return false;
                }
                invalidated = m_closed ||
                    continuationPoint.Invalidated ||
                    continuationPoint.Value is Historian.HistorianContinuationState { IsInvalidated: true };
                if (invalidated)
                {
                    TrackHistoryCleanup(continuationPoint);
                }
                else
                {
                    TrackCheckedOutHistory(
                        continuationPoint, includeOpaque: true, callerOwned: reserve && !admitRead);
                    if (admitRead)
                    {
                        AdmitHistoryRead(continuationPoint);
                    }
                }
                if (reserve && !invalidated)
                {
                    ReserveForContinuedOperation(continuationPoint.Id);
                }
                return true;
            }
        }

        /// <summary>
        /// Tracks a history continuation point that is checked out for an operation.
        /// </summary>
        /// <param name="entry">The checked out entry.</param>
        /// <param name="includeOpaque">Whether to track opaque points as well as historian states.</param>
        /// <param name="callerOwned">Whether an opaque point has been transferred to its caller.</param>
        private void TrackCheckedOutHistory(
            HistoryContinuationPoint entry,
            bool includeOpaque = false,
            bool callerOwned = false)
        {
            if (entry.Value is Historian.HistorianContinuationState state)
            {
                state.Saved = false;
            }
            if ((includeOpaque || entry.ActiveReads != 0 || entry.Value is Historian.HistorianContinuationState) &&
                !m_checkedOutHistory.Contains(entry))
            {
                m_checkedOutHistory.Add(entry);
            }
            if (callerOwned &&
                entry.ActiveReads == 0 &&
                entry.Value is not Historian.HistorianContinuationState)
            {
                entry.TransferToCaller();
            }
        }

        /// <summary>
        /// Finds an exact checked-out point without retaining points transferred to callers.
        /// </summary>
        private HistoryContinuationPoint? FindCheckedOutHistory(IHistoryContinuationPoint point)
        {
            HistoryContinuationPoint? found = null;
            for (int ii = m_checkedOutHistory.Count - 1; ii >= 0; ii--)
            {
                HistoryContinuationPoint entry = m_checkedOutHistory[ii];
                IHistoryContinuationPoint? value = entry.GetValueOrNull();
                if (entry.IsCallerOwned && value is null)
                {
                    m_checkedOutHistory.RemoveAt(ii);
                }
                else if (ReferenceEquals(value, point))
                {
                    found = entry;
                }
            }
            return found;
        }

        /// <summary>
        /// Removes metadata for caller-owned points that no longer have an external owner.
        /// </summary>
        private void PruneCallerOwnedHistory()
        {
            for (int ii = m_checkedOutHistory.Count - 1; ii >= 0; ii--)
            {
                HistoryContinuationPoint entry = m_checkedOutHistory[ii];
                if (entry.IsCallerOwned && entry.GetValueOrNull() is null)
                {
                    m_checkedOutHistory.RemoveAt(ii);
                }
            }
        }

        /// <summary>
        /// Retains an exact point while cleanup runs outside the session lock.
        /// </summary>
        /// <param name="entry">The point removed from the available history list.</param>
        private void TrackHistoryCleanup(HistoryContinuationPoint entry)
        {
            TrackCheckedOutHistory(entry, includeOpaque: true);
            entry.CleanupPending = true;
            entry.CleanupReady = false;
        }

        /// <summary>
        /// Reserves an exact entry for one history read while holding the session lock.
        /// </summary>
        /// <param name="entry">The entry being admitted.</param>
        /// <exception cref="InvalidOperationException">
        /// The point already has an active read, pending cleanup or an unresolved cleanup failure.
        /// </exception>
        private static void AdmitHistoryRead(HistoryContinuationPoint entry)
        {
            if (entry.ActiveReads != 0 || entry.CleanupPending || entry.CleanupFailure is not null)
            {
                throw new InvalidOperationException("The history continuation cannot be admitted for another read.");
            }
            entry.ActiveReads = 1;
        }

        /// <summary>
        /// Ends a read and starts any cleanup that was deferred until the read completed.
        /// </summary>
        /// <param name="point">The exact point used by the completed read.</param>
        /// <param name="disposePoint">Whether an unsaved point must be disposed.</param>
        /// <param name="scope">The read owner whose admission may need to follow deferred cleanup.</param>
        /// <exception cref="InvalidOperationException">
        /// The point is no longer retained, its exclusive read ownership was lost,
        /// or deferred cleanup already owns a generation admission.
        /// </exception>
        private void EndHistoryRead(
            IHistoryContinuationPoint point,
            bool disposePoint,
            HistoryReadScope? scope)
        {
            HistoryContinuationPoint entry;
            bool cleanup = false;
            lock (m_lock)
            {
                entry = m_history?.Find(candidate => ReferenceEquals(candidate.Value, point)) ??
                    FindCheckedOutHistory(point) ??
                    throw new InvalidOperationException("The active history continuation was not retained.");
                if (entry.ActiveReads != 1)
                {
                    throw new InvalidOperationException("The history continuation read ownership was lost.");
                }
                entry.ActiveReads = 0;
                bool saved = m_history?.Contains(entry) == true;
                bool saving = IsMirroredHistorySaveReserved(point);
                if (entry.CleanupPending)
                {
                    cleanup = entry.CleanupReady && !entry.CleanupInProgress;
                    if (!entry.CleanupReady)
                    {
                        if (entry.CleanupGenerationLease is not null)
                        {
                            throw new InvalidOperationException("The history cleanup already owns an admission.");
                        }
                        entry.CleanupGenerationLease = scope?.DetachGenerationLease();
                    }
                }
                else if (disposePoint && !saved && !saving)
                {
                    entry.CleanupPending = true;
                    cleanup = true;
                }
                else if (!saved && !saving && entry.CleanupFailure is null)
                {
                    if (!disposePoint && point is not Historian.HistorianContinuationState)
                    {
                        entry.TransferToCaller();
                    }
                    else
                    {
                        m_checkedOutHistory.Remove(entry);
                    }
                }
            }
            if (cleanup)
            {
                DisposeReleasedHistory(entry);
            }
        }

        /// <summary>
        /// Arbitrates direct disposal of a historian state owned by this session.
        /// </summary>
        /// <param name="state">The exact historian state being disposed.</param>
        /// <returns>True when this session owns or has already started its cleanup.</returns>
        internal bool TryDisposeHistorianState(Historian.HistorianContinuationState state)
        {
            HistoryContinuationPoint? entry;
            bool wasSaved;
            lock (m_lock)
            {
                entry = m_history?.Find(candidate => ReferenceEquals(candidate.Value, state)) ??
                    FindCheckedOutHistory(state);
                if (entry is null)
                {
                    return false;
                }
                if (entry.CleanupPending || entry.CleanupFailure is not null)
                {
                    return true;
                }
                if (entry.PendingPersistence || entry.Claiming)
                {
                    entry.Invalidated = true;
                    return true;
                }
                wasSaved = m_history?.Remove(entry) == true;
                if (wasSaved)
                {
                    TrackHistoryCleanup(entry);
                }
                else
                {
                    entry.CleanupPending = true;
                }
            }
            if (wasSaved)
            {
                CleanupReleasedHistory(entry);
            }
            else
            {
                DisposeReleasedHistory(entry);
            }
            return true;
        }

        /// <summary>
        /// Retains a read-scoped point and its original exception when disposal fails.
        /// </summary>
        /// <param name="point">The exact continuation held by the completed read.</param>
        /// <param name="checkSessionOwnership">Whether caller and other-session ownership must be preserved.</param>
        /// <exception cref="InvalidOperationException">Cleanup of this exact point is already pending.</exception>
        internal void DisposeHistoryReadPoint(
            IHistoryContinuationPoint point,
            bool checkSessionOwnership = false)
        {
            HistoryContinuationPoint entry;
            lock (m_lock)
            {
                if (m_history?.Exists(candidate => ReferenceEquals(candidate.Value, point)) == true ||
                    IsMirroredHistorySaveReserved(point))
                {
                    return;
                }
                HistoryContinuationPoint? tracked = FindCheckedOutHistory(point);
                if (checkSessionOwnership && tracked?.IsCallerOwned == true)
                {
                    return;
                }
                if (tracked is null)
                {
                    tracked = new HistoryContinuationPoint
                    {
                        Value = point,
                        Generation = point is Historian.HistorianContinuationState state
                            ? state.Ownership.Generation
                            : null
                    };
                    m_checkedOutHistory.Add(tracked);
                }
                if (tracked.CleanupFailure is { } failure)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
                if (tracked.CleanupPending)
                {
                    throw new InvalidOperationException("The history continuation is being cleaned up.");
                }
                tracked.Value = point;
                tracked.CleanupPending = true;
                entry = tracked;
            }
            DisposeReleasedHistory(entry, checkSessionOwnership);
        }

        /// <summary>
        /// Disposes a released continuation once and retains a failed resource for recovery.
        /// </summary>
        /// <param name="entry">The released continuation.</param>
        /// <param name="checkSessionOwnership">Whether a historian state must belong to this session.</param>
        private void DisposeReleasedHistory(
            HistoryContinuationPoint entry,
            bool checkSessionOwnership = false)
        {
            bool verifySessionOwnership;
            lock (m_lock)
            {
                if (!entry.CleanupPending || entry.CleanupInProgress)
                {
                    return;
                }
                entry.CheckCleanupSessionOwnership |= checkSessionOwnership;
                entry.CleanupReady = true;
                if (entry.ActiveReads != 0)
                {
                    return;
                }
                entry.CleanupInProgress = true;
                verifySessionOwnership = entry.CheckCleanupSessionOwnership;
            }
            try
            {
                try
                {
                    if (entry.Value is Historian.HistorianContinuationState state)
                    {
                        if (verifySessionOwnership)
                        {
                            state.DisposeUnlessOwnedByAnotherSession(this);
                        }
                        else
                        {
                            state.DisposeForSession();
                        }
                    }
                    else
                    {
                        entry.Value.Dispose();
                    }
                }
                catch (Exception failure)
                {
                    lock (m_lock)
                    {
                        entry.CleanupPending = false;
                        entry.CleanupReady = false;
                        entry.CleanupInProgress = false;
                        entry.CleanupFailure = failure;
                        if (!m_checkedOutHistory.Contains(entry))
                        {
                            m_checkedOutHistory.Add(entry);
                        }
                        if (entry.Generation?.TryGetGeneration(out NodeManagerGeneration? generation) == true &&
                            generation is not null)
                        {
                            generation.RetainHistoryCleanupFailure(
                                entry.Value, failure, () => ConfirmHistoryCleanup(entry, failure));
                        }
                    }
                    throw;
                }
                bool notify;
                lock (m_lock)
                {
                    entry.CleanupPending = false;
                    entry.CleanupReady = false;
                    entry.CleanupInProgress = false;
                    notify = m_checkedOutHistory.Remove(entry) && entry.ReleaseNotificationPending;
                }
                if (notify)
                {
                    HistoryContinuationPointsReleased?.Invoke();
                }
            }
            finally
            {
                GenerationGate.Lease? deferredLease;
                lock (m_lock)
                {
                    deferredLease = entry.CleanupGenerationLease;
                    entry.CleanupGenerationLease = null;
                }
                deferredLease?.Dispose();
            }
        }

        /// <summary>
        /// Releases the exact failed history entry after the host verifies its resource cleanup.
        /// </summary>
        /// <param name="entry">The retained continuation entry.</param>
        /// <param name="failure">The original failure associated with the verified resource.</param>
        /// <returns>The release notification to invoke after the failure is removed.</returns>
        /// <exception cref="InvalidOperationException">
        /// The failure changed, a read or cleanup is active, or this holder no longer retains the failed entry.
        /// </exception>
        private Action? ConfirmHistoryCleanup(HistoryContinuationPoint entry, Exception failure)
        {
            lock (m_lock)
            {
                if (!ReferenceEquals(entry.CleanupFailure, failure) ||
                    entry.ActiveReads != 0 ||
                    entry.CleanupPending ||
                    entry.CleanupInProgress ||
                    !m_checkedOutHistory.Contains(entry))
                {
                    throw new InvalidOperationException("The retained history cleanup has changed.");
                }
                entry.CleanupFailure = null;
                m_checkedOutHistory.Remove(entry);
                return entry.ReleaseNotificationPending
                    ? HistoryContinuationPointsReleased
                    : null;
            }
        }

        /// <summary>
        /// Removes the local store mirror of a history continuation point.
        /// </summary>
        /// <param name="point">The continuation point, or null.</param>
        /// <exception cref="AggregateException">
        /// Removing the point's local store mirror fails and disposing the point during rollback also fails.
        /// </exception>
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
            catch (Exception mirrorFailure) when (mirrorFailure is not OutOfMemoryException)
            {
                try
                {
                    DisposeHistoryReadPoint(point);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "History continuation mirror removal and cleanup failed.",
                        mirrorFailure,
                        cleanupFailure);
                }
                throw;
            }
        }

        /// <summary>
        /// Removes a history continuation point from the session and raises the released notification.
        /// </summary>
        /// <param name="entry">The history continuation point.</param>
        private void ReleaseHistory(HistoryContinuationPoint entry)
        {
            bool removed;
            lock (m_lock)
            {
                removed = m_history?.Remove(entry) == true;
                if ((entry.CleanupPending || entry.CleanupFailure is not null) &&
                    m_checkedOutHistory.Contains(entry))
                {
                    entry.ReleaseNotificationPending = true;
                }
                else
                {
                    removed |= m_checkedOutHistory.Remove(entry);
                }
            }
            if (removed)
            {
                HistoryContinuationPointsReleased?.Invoke();
            }
        }

        /// <summary>
        /// Disposes a history continuation point that is not retained by this session.
        /// </summary>
        /// <param name="point">The continuation point.</param>
        private void DisposeUnretainedHistoryPoint(IHistoryContinuationPoint point)
        {
            lock (m_lock)
            {
                PruneCallerOwnedHistory();
                if (m_checkedOutHistory.Exists(entry =>
                    ReferenceEquals(entry.GetValueOrNull(), point) &&
                    (entry.CleanupPending || entry.CleanupFailure is not null)))
                {
                    return;
                }
            }
            DisposeHistoryReadPoint(point, checkSessionOwnership: true);
        }

        /// <summary>
        /// Removes history continuation points from the store and disposes them, aggregating failures.
        /// </summary>
        /// <param name="points">The continuation points to dispose.</param>
        private void DisposeHistoryPoints(List<HistoryContinuationPoint> points)
        {
            List<Exception>? failures = null;
            foreach (HistoryContinuationPoint point in points)
            {
                try
                {
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, point.Id);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
                if (point.Portable)
                {
                    try
                    {
                        ScheduleHistoryRemoval(point.OwnerSessionId, point.Id);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        (failures ??= []).Add(failure);
                    }
                }
                try
                {
                    DisposeReleasedHistory(point);
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

        /// <summary>
        /// Reserves an identifier and exact point before writing its synchronous mirror.
        /// </summary>
        private void ReserveMirroredHistorySave(IHistoryContinuationPoint point, Guid id)
        {
            lock (m_lock)
            {
                ValidateHistoryAdmission(point, id, null);
                (m_pendingMirroredHistorySaves ??= []).Add(id, point);
            }
        }

        /// <summary>
        /// Releases the reservation after mirror rollback or successful admission.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// No pending mirrored-history save reservation exists for the supplied identifier.
        /// </exception>
        private void ReleaseMirroredHistorySave(Guid id)
        {
            lock (m_lock)
            {
                Dictionary<Guid, IHistoryContinuationPoint>? pending = m_pendingMirroredHistorySaves;
                if (pending is null || !pending.Remove(id))
                {
                    throw new InvalidOperationException("The history continuation save reservation was lost.");
                }
                if (pending.Count == 0)
                {
                    m_pendingMirroredHistorySaves = null;
                }
            }
        }

        /// <summary>
        /// Checks exact point identity while the session lock is held.
        /// </summary>
        private bool IsMirroredHistorySaveReserved(IHistoryContinuationPoint point)
        {
            if (m_pendingMirroredHistorySaves is not null)
            {
                foreach (IHistoryContinuationPoint reserved in m_pendingMirroredHistorySaves.Values)
                {
                    if (ReferenceEquals(reserved, point))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Validates admission while holding the session lock and honors the exact mirror-save reservation.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The session is closed (<c>BadSessionClosed</c>),
        /// or the point has been invalidated (<c>BadContinuationPointInvalid</c>).
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The point has an unresolved cleanup failure, cleanup is pending, the identifier is already registered,
        /// or the mirror-save reservation is invalid or held by another save.
        /// </exception>
        private void ValidateHistoryAdmission(
            IHistoryContinuationPoint point,
            Guid id,
            Guid? mirroredSaveReservation)
        {
            if (m_closed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSessionClosed,
                    "The session is closed and cannot accept history continuation points.");
            }
            if (point is Historian.HistorianContinuationState { IsInvalidated: true })
            {
                throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
            }
            if (point is Historian.HistorianContinuationState ownedState)
            {
                ownedState.ValidateSessionOwner(this);
            }
            HistoryContinuationPoint? previous = FindCheckedOutHistory(point);
            if (previous?.CleanupFailure is { } retainedFailure)
            {
                throw new InvalidOperationException(
                    "The history continuation has an unresolved cleanup failure.", retainedFailure);
            }
            if (previous?.CleanupPending == true)
            {
                throw new InvalidOperationException("The history continuation is being cleaned up.");
            }
            if (previous?.Invalidated == true)
            {
                throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
            }
            if (m_history?.Exists(entry => entry.Id == id) == true)
            {
                throw new InvalidOperationException(
                    "The history continuation point identifier is already registered.");
            }

            bool ownsReservation = false;
            if (mirroredSaveReservation is { } reservation &&
                reservation == id &&
                m_pendingMirroredHistorySaves is { } pending &&
                pending.TryGetValue(id, out IHistoryContinuationPoint? reserved))
            {
                ownsReservation = ReferenceEquals(reserved, point);
            }
            if (mirroredSaveReservation is not null && !ownsReservation)
            {
                throw new InvalidOperationException("The history continuation save reservation is invalid.");
            }
            if (!ownsReservation &&
                (m_pendingMirroredHistorySaves?.ContainsKey(id) == true || IsMirroredHistorySaveReserved(point)))
            {
                throw new InvalidOperationException("The history continuation is already being saved.");
            }
        }

        /// <summary>
        /// Admits an exact point while remembering caller ownership for a failed save rollback.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The retained point's save sequence has reached <see cref="ulong.MaxValue"/>.
        /// </exception>
        private (HistoryContinuationPoint Point, bool CallerOwned) AddHistoryContinuationPoint(
            IHistoryContinuationPoint continuationPoint,
            NodeId ownerSessionId,
            bool portable,
            bool pendingPersistence = false,
            Guid? mirroredSaveReservation = null)
        {
            var evicted = new List<HistoryContinuationPoint>();
            HistoryContinuationPoint? admittedPoint = null;
            bool callerOwned = false;
            Exception? admissionFailure = null;
            Exception? cleanupFailure = null;
            try
            {
                lock (m_lock)
                {
                    Guid id = continuationPoint.Id;
                    ValidateHistoryAdmission(continuationPoint, id, mirroredSaveReservation);
                    m_history ??= [];
                    HistoryRequestScope? current = GetCurrentHistoryRequest();
                    HistoryContinuationPoint? stored = FindCheckedOutHistory(continuationPoint);
                    callerOwned = stored?.IsCallerOwned == true;
                    if (stored?.SaveSequence == ulong.MaxValue)
                    {
                        throw new InvalidOperationException("The history continuation save sequence is exhausted.");
                    }
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
                        TrackHistoryCleanup(old);
                        evicted.Add(old);
                    }
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
                        stored.Value = continuationPoint;
                    }
                    stored.SaveSequence++;
                    stored.Id = id;
                    stored.OwnerSessionId = ownerSessionId;
                    stored.Portable = portable;
                    stored.PendingPersistence = pendingPersistence;
                    stored.Invalidated = false;
                    stored.Claiming = false;
                    stored.Timestamp = DateTime.UtcNow;
                    stored.ReservedBy = null;
                    stored.Generation = continuationPoint is Historian.HistorianContinuationState generationState
                        ? generationState.Ownership.Generation
                        : stored.Generation;
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
                        HistoryDispatchScope? dispatch = s_currentHistoryDispatch.Value;
                        // A flowed context keeps its original issuer even after that callback exits.
                        current.RecordSaved(
                            stored,
                            dispatch is not null && dispatch.BelongsTo(current) ? dispatch : null);
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
                        TrackCheckedOutHistory(admittedPoint, includeOpaque: true, callerOwned: callerOwned);
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
            return (admittedPoint!, callerOwned);
        }

        private void CompletePendingHistory(
            HistoryContinuationPoint continuationPoint,
            bool portable)
        {
            lock (m_lock)
            {
                ValidatePendingHistoryLocked(continuationPoint);
                continuationPoint.Portable = portable;
                continuationPoint.PendingPersistence = false;
                continuationPoint.ReservedBy = null;
            }
        }

        /// <summary>
        /// Checks that a pending save can still publish its continuation.
        /// </summary>
        /// <param name="continuationPoint">The point whose persistence is in progress.</param>
        private void ValidatePendingHistory(HistoryContinuationPoint continuationPoint)
        {
            lock (m_lock)
            {
                ValidatePendingHistoryLocked(continuationPoint);
            }
        }

        /// <summary>
        /// Validates a pending save while the session lock is held.
        /// </summary>
        /// <param name="continuationPoint">The point whose persistence is in progress.</param>
        /// <exception cref="ServiceResultException">
        /// The session closed during persistence (<c>BadSessionClosed</c>), or the point was invalidated,
        /// is no longer pending persistence or is no longer retained (<c>BadContinuationPointInvalid</c>).
        /// </exception>
        private void ValidatePendingHistoryLocked(HistoryContinuationPoint continuationPoint)
        {
            if (m_closed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSessionClosed,
                    "The session closed while the history continuation was being persisted.");
            }
            if (continuationPoint.Invalidated ||
                continuationPoint.Value is Historian.HistorianContinuationState { IsInvalidated: true } ||
                !continuationPoint.PendingPersistence ||
                m_history?.Contains(continuationPoint) != true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadContinuationPointInvalid,
                    "The history continuation was invalidated while it was being persisted.");
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
                    if (!m_closed && m_history is { } history)
                    {
                        foreach (HistoryContinuationPoint candidate in history)
                        {
                            if (candidate.Id == id)
                            {
                                scope.Pin(candidate);
                                break;
                            }
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
        /// Reports whether the current request saved a continuation with the supplied identifier.
        /// </summary>
        /// <param name="token">The continuation token.</param>
        /// <returns>True when the request recorded that identifier.</returns>
        internal bool IsSavedByCurrentHistoryRequest(ByteString token)
        {
            if (!TryGetHistoryContinuationPointId(token, out Guid id))
            {
                return false;
            }
            lock (m_lock)
            {
                return GetCurrentHistoryRequest()?.HasSavedId(id) == true;
            }
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
                List<SavedHistoryPoint>? saved = scope.TakeSaved();
                if (saved == null)
                {
                    return;
                }
                scope.RetainSavedIds(saved);
                if (m_history == null)
                {
                    return;
                }
                foreach (SavedHistoryPoint savedPoint in saved)
                {
                    HistoryContinuationPoint candidate = savedPoint.Entry;
                    if (scope.IsClientSupplied(savedPoint.Id) ||
                        candidate.SaveSequence != savedPoint.SaveSequence)
                    {
                        continue;
                    }
                    int index = m_history.IndexOf(candidate);
                    if (index < 0)
                    {
                        continue;
                    }
                    if (candidate.PendingPersistence || candidate.Claiming)
                    {
                        candidate.Invalidated = true;
                        continue;
                    }
                    m_history.RemoveAt(index);
                    TrackHistoryCleanup(candidate);
                    (released ??= []).Add(candidate);
                }
            }

            if (released == null)
            {
                return;
            }
            DisposeHistoryPoints(released);
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
                scope.ReleaseClosedState();
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
                m_owner = owner;
                m_previous = previous;
            }

            /// <summary>
            /// The holder whose points this scope pins.
            /// </summary>
            public SessionContinuationPoints? Owner => Volatile.Read(ref m_owner);

            /// <summary>
            /// Gets the request identity without exposing the request or its holder.
            /// </summary>
            public HistoryRequestIdentity Identity { get; } = new();

            /// <summary>
            /// Set once the request has returned its response; a late save is not pinned then.
            /// </summary>
            public bool Ended { get; set; }

            /// <summary>
            /// The slots this request holds for the successors of the points it restored to
            /// continue operations the client supplied.
            /// </summary>
            public int Reservations { get; set; }

            /// <summary>
            /// Records a point the request saved, so a faulted request can release it.
            /// </summary>
            /// <param name="continuationPoint">The exact saved entry.</param>
            /// <param name="dispatch">The callback whose context admitted it, if one was inherited.</param>
            public void RecordSaved(HistoryContinuationPoint continuationPoint, HistoryDispatchScope? dispatch)
            {
                (m_saved ??= []).Add(new SavedHistoryPoint(
                    continuationPoint,
                    continuationPoint.Id,
                    continuationPoint.SaveSequence,
                    dispatch));
            }

            /// <summary>
            /// Finds the latest admission of one unambiguous point from the exact callback.
            /// </summary>
            /// <param name="id">The saved identifier.</param>
            /// <param name="dispatch">The callback that returned the identifier.</param>
            /// <param name="saved">The matching saved admission.</param>
            /// <returns>Whether one exact entry was admitted by that callback.</returns>
            public bool TryFindSavedFor(Guid id, HistoryDispatchScope dispatch, out SavedHistoryPoint saved)
            {
                saved = default;
                if (m_saved is null)
                {
                    return false;
                }
                bool found = false;
                foreach (SavedHistoryPoint candidate in m_saved)
                {
                    if (candidate.Id != id || !ReferenceEquals(candidate.Dispatch, dispatch))
                    {
                        continue;
                    }
                    if (found && !ReferenceEquals(saved.Entry, candidate.Entry))
                    {
                        return false;
                    }
                    saved = candidate;
                    found = true;
                }
                return found;
            }

            public void Pin(HistoryContinuationPoint continuationPoint)
            {
                continuationPoint.Pins++;
                (m_pinned ??= []).Add(continuationPoint);
            }

            /// <summary>
            /// Returns and forgets the points the request saved.
            /// </summary>
            public List<SavedHistoryPoint>? TakeSaved()
            {
                List<SavedHistoryPoint>? saved = m_saved;
                m_saved = null;
                return saved;
            }

            /// <summary>
            /// Keeps saved identifiers available while the failed request releases its results.
            /// </summary>
            /// <param name="saved">The saved point admissions.</param>
            public void RetainSavedIds(List<SavedHistoryPoint> saved)
            {
                HashSet<Guid> identifiers = m_releasedIds ??= [];
                foreach (SavedHistoryPoint point in saved)
                {
                    identifiers.Add(point.Id);
                }
            }

            /// <summary>
            /// Reports whether this request recorded a save for the identifier.
            /// </summary>
            /// <param name="id">The identifier to find.</param>
            /// <returns>True when this request recorded the identifier.</returns>
            public bool HasSavedId(Guid id)
            {
                if (m_releasedIds?.Contains(id) == true)
                {
                    return true;
                }
                if (m_saved != null)
                {
                    foreach (SavedHistoryPoint point in m_saved)
                    {
                        if (point.Id == id)
                        {
                            return true;
                        }
                    }
                }
                return false;
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

            /// <summary>
            /// Releases this request's pins and its completed reservation ownership.
            /// </summary>
            public void UnpinAll()
            {
                if (m_pinned == null)
                {
                    return;
                }
                foreach (HistoryContinuationPoint continuationPoint in m_pinned)
                {
                    continuationPoint.Pins--;
                    if (ReferenceEquals(continuationPoint.ReservedBy, this))
                    {
                        continuationPoint.ReservedBy = null;
                    }
                }
                m_pinned = null;
            }

            /// <summary>
            /// Releases request-owned state after its reservations and pins have been removed.
            /// </summary>
            public void ReleaseClosedState()
            {
                m_clientSupplied = null;
                m_releasedIds = null;
                Volatile.Write(ref m_owner, null);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    HistoryRequestScope? previous = Interlocked.Exchange(ref m_previous, null);
                    if (ReferenceEquals(s_currentHistoryRequest.Value, this))
                    {
                        s_currentHistoryRequest.Value = previous;
                    }
                    SessionContinuationPoints? owner = Owner;
                    owner?.EndHistoryRequest(this);
                }
            }

            private SessionContinuationPoints? m_owner;
            private HistoryRequestScope? m_previous;
            private List<HistoryContinuationPoint>? m_pinned;
            private List<SavedHistoryPoint>? m_saved;
            private HashSet<Guid>? m_clientSupplied;
            private HashSet<Guid>? m_releasedIds;
            private int m_disposed;
        }

        /// <summary>
        /// Identifies a history request without retaining its session or saved entries.
        /// </summary>
        private sealed class HistoryRequestIdentity;

        /// <summary>
        /// Identifies one callback without retaining its NodeManager in captured execution contexts.
        /// </summary>
        private sealed class HistoryDispatchScope : IDisposable
        {
            /// <summary>
            /// Creates an identity for one callback and its issuing generation.
            /// </summary>
            public HistoryDispatchScope(
                HistoryRequestScope request,
                NodeManagerGenerationReference generation,
                HistoryDispatchScope? previous)
            {
                m_requestIdentity = request.Identity;
                m_generation = generation;
                m_previous = previous;
            }

            /// <summary>
            /// Reports whether a save belongs to the request that created this callback.
            /// </summary>
            public bool BelongsTo(HistoryRequestScope request)
            {
                return ReferenceEquals(m_requestIdentity, request.Identity);
            }

            /// <summary>
            /// Reports whether this active callback can bind a result for the specified generation.
            /// </summary>
            public bool CanBind(HistoryRequestScope request, NodeManagerGeneration generation)
            {
                return Volatile.Read(ref m_disposed) == 0 &&
                    BelongsTo(request) &&
                    m_generation.Identifies(generation);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0 &&
                    ReferenceEquals(s_currentHistoryDispatch.Value, this))
                {
                    s_currentHistoryDispatch.Value = m_previous;
                }
            }

            private readonly HistoryRequestIdentity m_requestIdentity;
            private readonly NodeManagerGenerationReference m_generation;
            private readonly HistoryDispatchScope? m_previous;
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
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            CleanupRejectedMirroredHistory(continuationPoint, localEnvelope, failure);
                            throw;
                        }
                    }
                    if (!transferred)
                    {
                        CleanupRejectedMirroredHistory(continuationPoint, localEnvelope);
                        continue;
                    }
                    try
                    {
                        _ = AddHistoryContinuationPoint(
                            continuationPoint,
                            localEnvelope.OwnerSessionId,
                            portable: true);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        CleanupRejectedMirroredHistory(continuationPoint, localEnvelope, failure);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Cleans up a rejected mirrored history point without disposing an instance retained by another save.
        /// </summary>
        /// <exception cref="AggregateException">
        /// Mirror removal or point disposal fails together with another cleanup failure
        /// or the supplied original failure.
        /// </exception>
        private void CleanupRejectedMirroredHistory(
            IHistoryContinuationPoint point,
            HistoryContinuationPointEnvelope envelope,
            Exception? originalFailure = null)
        {
            var failures = new List<Exception>();
            try
            {
                ScheduleHistoryRemoval(envelope.OwnerSessionId, envelope.Id);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            try
            {
                DisposeUnretainedHistoryPoint(point);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            if (failures.Count == 0)
            {
                return;
            }
            if (originalFailure is not null)
            {
                failures.Insert(0, originalFailure);
            }
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            throw new AggregateException("Mirrored history continuation cleanup failed.", failures);
        }

        private bool ContainsHistoryContinuationPoint(Guid id)
        {
            PruneCallerOwnedHistory();
            if (m_pendingMirroredHistorySaves?.ContainsKey(id) == true ||
                m_checkedOutHistory.Exists(entry => entry.Id == id))
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
        /// <exception cref="AggregateException">
        /// Removing mirrored entries or disposing browse or history continuation points fails.
        /// </exception>
        public void Clear()
        {
            var browseCPs = new List<ContinuationPoint>();
            var historyCPs = new List<HistoryContinuationPoint>();
            var mirrored = new List<(NodeId OwnerSessionId, ContinuationPointKind Kind, Guid Id)>();
            lock (m_lock)
            {
                m_closed = true;
                PruneCallerOwnedHistory();
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
                    if (entry.GetValueOrNull() is Historian.HistorianContinuationState state)
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
                    entry.Invalidated = true;
                    if (entry.Claiming || entry.PendingPersistence)
                    {
                        continue;
                    }
                    m_history.RemoveAt(ii);
                    TrackHistoryCleanup(entry);
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

            List<Exception>? failures = null;
            foreach ((NodeId ownerSessionId, ContinuationPointKind kind, Guid id) in mirrored)
            {
                try
                {
                    m_store?.RemoveContinuationPoint(ownerSessionId, kind, id);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }
            try
            {
                DisposeBrowsePoints(browseCPs);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                (failures ??= []).Add(failure);
            }
            try
            {
                DisposeHistoryPoints(historyCPs);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                (failures ??= []).Add(failure);
            }
            if (failures is not null)
            {
                throw new AggregateException("Session continuation cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Creates the store envelope for a history continuation point.
        /// </summary>
        /// <param name="id">The continuation point identifier.</param>
        /// <param name="ownerSessionId">The captured session owner.</param>
        /// <returns>The envelope.</returns>
        private static ContinuationPointEnvelope CreateHistoryEnvelope(Guid id, NodeId ownerSessionId)
        {
            return new ContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = NormalizeNodeId(ownerSessionId),
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

        private void ScheduleHistoryRemoval(
            NodeId ownerSessionId,
            Guid id)
        {
            m_historyStore?.ScheduleRemove(ownerSessionId, id);
        }

        private NodeId Id => m_sessionIdProvider();

        /// <summary>
        /// Tracks a browse continuation point owned by the session.
        /// </summary>
        /// <param name="point">The continuation point.</param>
        private sealed class BrowseContinuationPoint(ContinuationPoint point)
        {
            /// <summary>
            /// Gets the continuation point.
            /// </summary>
            public ContinuationPoint Point { get; } = point;

            /// <summary>
            /// Gets or sets whether the point counts against the available browse limit.
            /// </summary>
            public bool Available { get; set; }

            /// <summary>
            /// Gets or sets whether the point was invalidated.
            /// </summary>
            public bool Invalidated { get; set; }
        }

        /// <summary>
        /// Scopes a history read of a continuation token and disposes the point unless it was saved again.
        /// </summary>
        internal sealed class HistoryReadScope : IDisposable
        {
            /// <summary>
            /// Initializes a history-read scope.
            /// </summary>
            /// <param name="owner">The session continuation points.</param>
            /// <param name="previous">The enclosing scope, or null.</param>
            /// <param name="token">The continuation point token.</param>
            /// <param name="point">The restored continuation point, or null.</param>
            public HistoryReadScope(
                SessionContinuationPoints owner,
                HistoryReadScope? previous,
                ByteString token,
                IHistoryContinuationPoint? point)
            {
                m_owner = owner;
                m_previous = previous;
                m_point = point;
                if (token.Length == 16)
                {
                    ReadOnlySpan<byte> bytes = token.Span;
                    m_tokenFirst = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
                    m_tokenSecond = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
                    m_hasToken = true;
                }
            }

            /// <summary>
            /// Gets the restored continuation point while this scope is active, or null.
            /// </summary>
            public IHistoryContinuationPoint? Point
            {
                get
                {
                    lock (m_stateLock)
                    {
                        return m_active ? m_point : null;
                    }
                }
            }

            /// <summary>
            /// Gets the exact generation admitted for this restore while the scope is active.
            /// </summary>
            internal NodeManagerGeneration? Generation
            {
                get
                {
                    lock (m_stateLock)
                    {
                        return m_active ? m_generation : null;
                    }
                }
            }

            /// <summary>
            /// Reserves the issuing generation before the point leaves session ownership.
            /// </summary>
            /// <param name="generation">The point's issuing generation.</param>
            /// <returns>Whether the generation accepted this read.</returns>
            /// <exception cref="InvalidOperationException">
            /// This scope is inactive or already owns a generation,
            /// or successful generation admission returns no lease.
            /// </exception>
            internal bool TryAdmitGeneration(NodeManagerGeneration generation)
            {
                lock (m_stateLock)
                {
                    if (!m_active || m_generation is not null)
                    {
                        throw new InvalidOperationException("The history read scope cannot admit another generation.");
                    }
                    GenerationGate.Lease? lease = null;
                    try
                    {
                        if (!generation.Gate.TryEnter(out lease))
                        {
                            return false;
                        }
                        m_generationLease = lease ??
                            throw new InvalidOperationException("The history read admission has no lease.");
                        lease = null;
                        m_generation = generation;
                        return true;
                    }
                    finally
                    {
                        lease?.Dispose();
                    }
                }
            }

            /// <summary>
            /// Transfers admission to cleanup that is still waiting for its store callback.
            /// </summary>
            /// <returns>The admission, or null if this scope has none.</returns>
            internal GenerationGate.Lease? DetachGenerationLease()
            {
                lock (m_stateLock)
                {
                    GenerationGate.Lease? lease = m_generationLease;
                    m_generationLease = null;
                    return lease;
                }
            }

            /// <summary>
            /// Associates the restored point before this scope is made visible to the caller.
            /// </summary>
            /// <param name="point">The restored continuation point, or null.</param>
            internal void SetRestoredPoint(IHistoryContinuationPoint? point)
            {
                m_point = point;
            }

            /// <summary>
            /// Checks that the scope is active and is being installed over its captured parent.
            /// </summary>
            /// <param name="owner">The holder activating the scope.</param>
            /// <param name="current">The current scope in the activating flow.</param>
            /// <returns>True when the scope can be activated in this flow.</returns>
            internal bool CanActivate(SessionContinuationPoints owner, HistoryReadScope? current)
            {
                lock (m_stateLock)
                {
                    return m_active && ReferenceEquals(m_owner, owner) && ReferenceEquals(m_previous, current);
                }
            }

            /// <summary>
            /// Resolves the restored provider only while this scope is active.
            /// </summary>
            /// <param name="nodeId">The source node identifier.</param>
            /// <returns>The matching provider, or null.</returns>
            internal Historian.IHistorianProvider? GetRestoredProvider(NodeId nodeId)
            {
                lock (m_stateLock)
                {
                    return m_active &&
                        m_point is Historian.HistorianContinuationState state &&
                        (state.NodeId == nodeId ||
                            state.OriginNodeId == nodeId ||
                            (state.SourceNode is { } source && source.NodeId == nodeId))
                        ? state.Provider
                        : null;
                }
            }

            /// <summary>
            /// Transfers the exact point while this read scope remains active.
            /// </summary>
            /// <param name="token">The continuation token requested by the caller.</param>
            /// <param name="requirePoint">Whether a null point should fall through to the session store.</param>
            /// <param name="restored">The point transferred by this scope, if any.</param>
            /// <param name="expired">Whether the matching scope has already ended.</param>
            /// <returns>Whether this scope handled the restore.</returns>
            internal bool TryRestore(
                ByteString token,
                bool requirePoint,
                out IHistoryContinuationPoint? restored,
                out bool expired)
            {
                restored = null;
                expired = false;
                lock (m_stateLock)
                {
                    if (token.Length != 16)
                    {
                        // Invalid tokens cannot identify another stored history point.
                        expired = !m_active;
                        return false;
                    }
                    ReadOnlySpan<byte> bytes = token.Span;
                    bool matches = m_hasToken &&
                        BinaryPrimitives.ReadUInt64LittleEndian(bytes) == m_tokenFirst &&
                        BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]) == m_tokenSecond;
                    if (!m_active)
                    {
                        expired = matches;
                        return false;
                    }
                    if (!matches)
                    {
                        return false;
                    }
                    if (m_consumed || (requirePoint && m_point is null))
                    {
                        return false;
                    }
                    m_consumed = true;
                    restored = m_point;
                    return true;
                }
            }

            /// <summary>
            /// Checks whether a nested read addresses this scope's captured token.
            /// </summary>
            /// <param name="token">The continuation token requested by the caller.</param>
            /// <returns>True when the token is the same as this scope's token.</returns>
            internal bool MatchesToken(ByteString token)
            {
                if (!m_hasToken || token.Length != 16)
                {
                    return false;
                }
                ReadOnlySpan<byte> bytes = token.Span;
                return BinaryPrimitives.ReadUInt64LittleEndian(bytes) == m_tokenFirst &&
                    BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]) == m_tokenSecond;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                bool disposePoint;
                SessionContinuationPoints owner;
                HistoryReadScope? previous;
                IHistoryContinuationPoint? point;
                lock (m_stateLock)
                {
                    if (!m_active)
                    {
                        return;
                    }
                    owner = m_owner ?? throw new InvalidOperationException("The history read scope has no owner.");
                    previous = m_previous;
                    point = m_point;
                    m_active = false;
                    disposePoint = point is Historian.HistorianContinuationState || !m_consumed;
                }
                if (ReferenceEquals(owner.m_historyRead.Value, this))
                {
                    owner.m_historyRead.Value = previous;
                }
                try
                {
                    if (point is not null)
                    {
                        owner.EndHistoryRead(point, disposePoint, this);
                    }
                }
                finally
                {
                    GenerationGate.Lease? generationLease;
                    lock (m_stateLock)
                    {
                        m_owner = null;
                        m_previous = null;
                        m_point = null;
                        m_generation = null;
                        generationLease = m_generationLease;
                        m_generationLease = null;
                    }
                    generationLease?.Dispose();
                }
            }

            private readonly Lock m_stateLock = new();
            private SessionContinuationPoints? m_owner;
            private HistoryReadScope? m_previous;
            private IHistoryContinuationPoint? m_point;

            /// <summary>The issuing generation admitted for the current read.</summary>
            private NodeManagerGeneration? m_generation;

            /// <summary>The admission retained until read or deferred cleanup completes.</summary>
            private GenerationGate.Lease? m_generationLease;
            private readonly ulong m_tokenFirst;
            private readonly ulong m_tokenSecond;
            private readonly bool m_hasToken;
            private bool m_active = true;
            private bool m_consumed;
        }

        /// <summary>
        /// Identifies one admission of an exact history point to the session.
        /// </summary>
        private readonly struct SavedHistoryPoint(
            HistoryContinuationPoint entry,
            Guid id,
            ulong saveSequence,
            HistoryDispatchScope? dispatch)
        {
            /// <summary>
            /// Gets the exact point entry.
            /// </summary>
            public HistoryContinuationPoint Entry { get; } = entry;

            /// <summary>
            /// Gets the identifier recorded at admission.
            /// </summary>
            public Guid Id { get; } = id;

            /// <summary>
            /// Gets the entry's admission sequence.
            /// </summary>
            public ulong SaveSequence { get; } = saveSequence;

            /// <summary>
            /// Gets the exact callback whose execution context admitted this point.
            /// </summary>
            public HistoryDispatchScope? Dispatch { get; } = dispatch;
        }

        private sealed class HistoryContinuationPoint
        {
            /// <summary>
            /// Gets or retains the exact point while the session owns it.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// No strongly retained or still-reachable caller-owned point is available.
            /// </exception>
            /// <exception cref="ArgumentNullException">The assigned continuation point is null.</exception>
            public IHistoryContinuationPoint Value
            {
                get => GetValueOrNull()
                    ?? throw new InvalidOperationException("The caller-owned history point is no longer available.");
                set
                {
                    m_value = value ?? throw new ArgumentNullException(nameof(value));
                    m_callerOwnedValue = null;
                }
            }

            /// <summary>
            /// Gets whether the point was transferred without a disposal callback.
            /// </summary>
            public bool IsCallerOwned => m_callerOwnedValue is not null;

            /// <summary>
            /// Gets the exact point while its owner still retains it.
            /// </summary>
            /// <returns>The point, or null if its caller no longer retains it.</returns>
            public IHistoryContinuationPoint? GetValueOrNull()
            {
                if (m_value is { } value)
                {
                    return value;
                }
                if (m_callerOwnedValue is { } reference &&
                    reference.TryGetTarget(out IHistoryContinuationPoint? transferred))
                {
                    return transferred;
                }
                return null;
            }

            /// <summary>
            /// Retains only a weak identity after transferring an opaque point to its caller.
            /// </summary>
            public void TransferToCaller()
            {
                IHistoryContinuationPoint value = Value;
                m_callerOwnedValue = new WeakReference<IHistoryContinuationPoint>(value);
                m_value = null;
            }

            public Guid Id;
            public NodeId OwnerSessionId;
            public bool Portable;
            public bool PendingPersistence;

            /// <summary>
            /// Whether its issuing generation or session invalidated this point.
            /// </summary>
            public bool Invalidated;

            public bool Claiming;
            public int Pins;

            /// <summary>
            /// The number of active read owners of this exact entry.
            /// </summary>
            public int ActiveReads;
            public DateTime Timestamp;
            public NodeManagerGenerationReference? Generation;

            /// <summary>
            /// The sequence of the latest save of this exact entry.
            /// </summary>
            public ulong SaveSequence;

            /// <summary>
            /// Whether cleanup owns this exact point, including while it waits for an active read.
            /// </summary>
            public bool CleanupPending;

            /// <summary>
            /// Whether store cleanup has reached the point where physical disposal may begin.
            /// </summary>
            public bool CleanupReady;

            /// <summary>
            /// Whether a caller is currently performing the one physical disposal attempt.
            /// </summary>
            public bool CleanupInProgress;

            /// <summary>
            /// The read admission transferred to cleanup while its store callback is pending.
            /// </summary>
            public GenerationGate.Lease? CleanupGenerationLease;

            /// <summary>
            /// Whether a deferred cleanup must preserve another session's historian ownership.
            /// </summary>
            public bool CheckCleanupSessionOwnership;

            /// <summary>
            /// Whether a continuation owner requested notification during its pending release.
            /// </summary>
            public bool ReleaseNotificationPending;

            /// <summary>
            /// The original failure from an attempted release of this exact point.
            /// </summary>
            public Exception? CleanupFailure;

            /// <summary>
            /// The request whose reserved slot this point consumed when it was saved, so the
            /// reservation can be given back if the point is dropped because persisting it failed.
            /// </summary>
            public HistoryRequestScope? ReservedBy;

            private IHistoryContinuationPoint? m_value;
            private WeakReference<IHistoryContinuationPoint>? m_callerOwnedValue;
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
        private Dictionary<Guid, IHistoryContinuationPoint>? m_pendingMirroredHistorySaves;
        private Dictionary<Guid, NodeId>? m_mirroredBrowseOwners;
        private Dictionary<Guid, NodeId>? m_mirroredHistoryOwners;
        private int m_reservedHistory;
        private bool m_closed;

        /// <summary>
        /// The HistoryRead request of the current asynchronous flow; it pins the history
        /// continuation points saved while it is in flight.
        /// </summary>
        private static readonly AsyncLocal<HistoryRequestScope?> s_currentHistoryRequest = new();

        /// <summary>
        /// The exact manager callback whose execution context is saving a history point.
        /// </summary>
        private static readonly AsyncLocal<HistoryDispatchScope?> s_currentHistoryDispatch = new();
    }
}
