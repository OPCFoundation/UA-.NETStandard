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
    internal sealed class SessionContinuationPoints
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
                while (m_browse.Count > MaxBrowse)
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

        public void RemoveBrowseForManager(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

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
                    m_store?.RemoveContinuationPoint(
                        Id,
                        ContinuationPointKind.Browse,
                        continuationPoint.Id);
                    continuationPoint.Dispose();
                }
            }
        }

        /// <summary>
        /// Saves a history continuation point, dropping the oldest when the limit is reached. A point that implements
        /// <see cref="IDisposable"/> is disposed when it is dropped or the session is cleared.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="continuationPoint"/> is <c>null</c>.</exception>
        public void SaveHistory(Guid id, object continuationPoint)
        {
            if (continuationPoint == null)
            {
                throw new ArgumentNullException(nameof(continuationPoint));
            }

            lock (m_lock)
            {
                m_history ??= [];

                // remove existing continuation point if space needed.
                while (m_history.Count >= m_maxHistory)
                {
                    HistoryContinuationPoint oldCP = m_history[0];
                    m_history.RemoveAt(0);
                    m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, oldCP.Id);
                    (oldCP.Value as IDisposable)?.Dispose();
                }

                // create the cp.
                var cp = new HistoryContinuationPoint
                {
                    Id = id,
                    Value = continuationPoint,
                    Timestamp = DateTime.UtcNow
                };

                m_history.Add(cp);
            }

            m_store?.StoreContinuationPoint(CreateHistoryEnvelope(id));
        }

        /// <summary>
        /// Restores (and removes) a previously saved history continuation point, or <c>null</c> when not found.
        /// </summary>
        public object? RestoreHistory(ByteString continuationPoint)
        {
            lock (m_lock)
            {
                if (m_history == null)
                {
                    return null;
                }

                if (continuationPoint.Length != 16)
                {
                    return null;
                }

                var id = new Guid(continuationPoint.ToArray());

                for (int ii = 0; ii < m_history.Count; ii++)
                {
                    HistoryContinuationPoint cp = m_history[ii];

                    if (cp.Id == id)
                    {
                        m_history.RemoveAt(ii);
                        m_store?.RemoveContinuationPoint(Id, ContinuationPointKind.History, id);
                        return cp.Value;
                    }
                }

                if (m_mirroredHistoryOwners != null &&
                    m_mirroredHistoryOwners.TryGetValue(id, out NodeId ownerSessionId))
                {
                    m_mirroredHistoryOwners.Remove(id);
                    m_store?.RemoveContinuationPoint(ownerSessionId, ContinuationPointKind.History, id);
                }

                return null;
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
            List<ContinuationPoint>? browseCPs;
            List<HistoryContinuationPoint>? historyCPs;
            lock (m_lock)
            {
                browseCPs = m_browse;
                m_browse = null;
                historyCPs = m_history;
                m_history = null;
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
                    (historyCPs[ii].Value as IDisposable)?.Dispose();
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

        private sealed class HistoryContinuationPoint
        {
            public Guid Id;
            public object? Value;
            public DateTime Timestamp;
        }

        private readonly Func<NodeId> m_sessionIdProvider;
        private readonly int m_maxHistory;
        private readonly IContinuationPointStore? m_store;
        private readonly Lock m_lock = new();
        private List<ContinuationPoint>? m_browse;
        private List<HistoryContinuationPoint>? m_history;
        private Dictionary<Guid, NodeId>? m_mirroredBrowseOwners;
        private Dictionary<Guid, NodeId>? m_mirroredHistoryOwners;
    }
}
