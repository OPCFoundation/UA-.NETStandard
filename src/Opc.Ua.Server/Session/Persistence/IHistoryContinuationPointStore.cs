/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Durable single-use store for portable HistoryRead continuation points.
    /// </summary>
    public interface IHistoryContinuationPointStore
    {
        /// <summary>
        /// Persists a continuation point before it is returned to a client.
        /// </summary>
        ValueTask StoreAsync(
            HistoryContinuationPointEnvelope envelope,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically claims a continuation point for one resume operation.
        /// </summary>
        ValueTask<bool> TryTakeAsync(
            NodeId ownerSessionId,
            Guid id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Schedules removal of a continuation point without blocking a
        /// synchronous session cleanup path. Implementations must not throw;
        /// cleanup failures are reported through their diagnostics channel.
        /// </summary>
        void ScheduleRemove(
            NodeId ownerSessionId,
            Guid id);

        /// <summary>
        /// Loads every portable continuation owned by a mirrored session.
        /// </summary>
        ValueTask<ArrayOf<HistoryContinuationPointEnvelope>> LoadAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional server-internal access to the configured history continuation store.
    /// </summary>
    public interface IHistoryContinuationPointStoreProvider
    {
        /// <summary>
        /// Configured portable HistoryRead continuation store, if any.
        /// </summary>
        IHistoryContinuationPointStore? HistoryContinuationPointStore { get; }
    }
}
