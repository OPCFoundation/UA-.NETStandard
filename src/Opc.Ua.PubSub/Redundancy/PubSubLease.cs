/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// An acquired leadership lease used to elect the active instance of a
    /// redundant PubSub component.
    /// </summary>
    /// <remarks>
    /// The <see cref="FencingToken"/> is a monotonically increasing value the
    /// store assigns each time the lease changes owner; downstream consumers
    /// can use it to reject stale writes from a superseded active instance
    /// (fencing), per the externalized-state requirements of OPC UA Part 14
    /// §9.1.6.
    /// </remarks>
    /// <param name="LeaseKey">
    /// Stable key identifying the contended resource (for example a writer
    /// group component id).
    /// </param>
    /// <param name="OwnerId">
    /// Identifier of the instance currently holding the lease.
    /// </param>
    /// <param name="FencingToken">
    /// Monotonic token incremented on every ownership change.
    /// </param>
    /// <param name="ExpiresAt">
    /// Absolute UTC time at which the lease expires unless renewed.
    /// </param>
    public readonly record struct PubSubLease(
        string LeaseKey,
        string OwnerId,
        long FencingToken,
        DateTimeOffset ExpiresAt);
}
