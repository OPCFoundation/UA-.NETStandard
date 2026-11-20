// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a read/write value cache backed by a distributed
    /// <see cref="INodeStateStore"/>.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 §6.6.2.2 requires identical NodeIds and AddressSpaces in a <c>RedundantServerSet</c>, but it does
    /// not standardize how process values are cached between replicas. This provider lets variable callbacks cache the
    /// last value they observed and serve it with a freshness bound from the shared store.
    /// </remarks>
    public interface IDistributedValueCache
    {
        /// <summary>
        /// Writes the latest value for a node into the cache.
        /// </summary>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask CacheAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default);

        /// <summary>
        /// Reads the last cached value and reports whether it is still fresh
        /// (its source timestamp is within <paramref name="maxAge"/>).
        /// </summary>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="maxAge">The freshness bound.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>Fresh = true</c> with the value when a value exists and is
        /// within <paramref name="maxAge"/>; otherwise the value may still be
        /// returned (when present) but <c>Fresh = false</c>.
        /// </returns>
        ValueTask<(bool Fresh, DataValue Value)> TryGetAsync(
            NodeId nodeId,
            TimeSpan maxAge,
            CancellationToken ct = default);
    }
}
