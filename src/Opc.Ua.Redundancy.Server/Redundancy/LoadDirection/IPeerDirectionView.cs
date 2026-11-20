/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Reads the current, fresh direction inputs (health <c>ServiceLevel</c> + load weight) for every member of the
    /// <c>RedundantServerSet</c> from the shared store. Stale records are aged out so a partitioned or lagging view
    /// fails safe (the direction policy then returns the local Server).
    /// </summary>
    public interface IPeerDirectionView
    {
        /// <summary>
        /// Returns the fresh per-peer direction records (including the local Server). A member is included only when
        /// its health record is within the staleness window; a stale load record is reported as
        /// <see cref="PeerDirectionRecord.LoadKnown"/> = <c>false</c>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ArrayOf<PeerDirectionRecord>> GetPeersAsync(CancellationToken cancellationToken = default);
    }
}
