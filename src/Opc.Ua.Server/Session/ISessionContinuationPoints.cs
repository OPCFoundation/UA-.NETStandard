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
    /// The continuation points a session is holding on behalf of its client, for browses
    /// and for historical reads.
    /// </summary>
    /// <remarks>
    /// A continuation point survives between service calls, so the session owns the
    /// lifetime: saved points can be dropped when the per-session limit is reached, during
    /// immediate retirement, and when the session closes. Continuation points do not extend
    /// the lifetime of the generation that issued them.
    /// </remarks>
    public interface ISessionContinuationPoints
    {
        /// <summary>
        /// The number of browse continuation points the session will hold before it starts
        /// dropping the oldest.
        /// </summary>
        int MaxBrowse { get; }

        /// <summary>
        /// Saves a browse continuation point, dropping the oldest when the limit is reached.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        void SaveBrowse(ContinuationPoint continuationPoint);

        /// <summary>
        /// Restores and removes an available browse continuation point. The caller must
        /// dispose it or save its next page.
        /// </summary>
        /// <param name="continuationPoint">The identifier the client returned.</param>
        /// <returns>The continuation point, or <c>null</c> when it is not held.</returns>
        ContinuationPoint? RestoreBrowse(ByteString continuationPoint);

        /// <summary>
        /// Saves a history continuation point, dropping and disposing the oldest when the
        /// limit is reached. Ownership transfers to the session when this method is called;
        /// the implementation disposes the point if it cannot retain it.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        void SaveHistory(IHistoryContinuationPoint continuationPoint);

        /// <summary>
        /// Saves and durably mirrors a portable history continuation point. Ownership transfers
        /// to the session when this method is called; the implementation disposes the point if
        /// persistence fails.
        /// </summary>
        ValueTask SaveHistoryAsync(
            IHistoryContinuationPoint continuationPoint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores and removes an available history continuation point. The caller must
        /// dispose it or save its next page.
        /// </summary>
        /// <param name="continuationPoint">The identifier the client returned.</param>
        /// <returns>The continuation point, or <c>null</c> when it is not held.</returns>
        IHistoryContinuationPoint? RestoreHistory(ByteString continuationPoint);

        /// <summary>
        /// Releases and disposes a history continuation point without resuming it.
        /// Portable records are scheduled for durable removal.
        /// </summary>
        bool ReleaseHistory(ByteString continuationPoint);

        /// <summary>
        /// Atomically claims and removes a portable history continuation point, transferring
        /// ownership to the caller. The caller must dispose it, save a successor, or restore it
        /// when the resumed operation does not complete.
        /// </summary>
        ValueTask<IHistoryContinuationPoint?> RestoreHistoryAsync(
            ByteString continuationPoint,
            CancellationToken cancellationToken = default);

    }
}
