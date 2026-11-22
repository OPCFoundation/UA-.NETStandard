// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Monotonic writer identity used to fence stale historian leaders.
    /// </summary>
    public readonly record struct HistorianWriterFence(
        Guid WriterId,
        long Epoch);

    /// <summary>
    /// Acquires and validates a strong-store historian writer fence.
    /// </summary>
    public interface IHistorianFencingAuthority
    {
        /// <summary>
        /// Acquires or renews the local writer fence when this replica is the
        /// elected leader. Returns <c>null</c> while another live writer owns
        /// the historian lease.
        /// </summary>
        ValueTask<HistorianWriterFence?> TryAcquireOrRenewAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Verifies that the supplied fence still owns the unexpired
        /// strong-store writer lease.
        /// </summary>
        ValueTask<bool> IsCurrentAsync(
            HistorianWriterFence fence,
            CancellationToken ct = default);
    }
}
