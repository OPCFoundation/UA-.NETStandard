// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a source of redundant-server peers. Implementations resolve the
    /// current peer set from a mechanism (static configuration, DNS, an LDS(-ME), Kubernetes, …) and raise
    /// <see cref="PeersChanged"/> whenever the set changes so the redundancy metadata (and, when present, the
    /// CRDT gossip fabric) can be updated dynamically at runtime. Static configuration is the fallback when no
    /// dynamic mechanism is available.
    /// </summary>
    public interface IPeerDiscovery
    {
        /// <summary>
        /// Raised when the discovered peer set changes between refreshes.
        /// </summary>
        event Action<ArrayOf<DiscoveredPeer>>? PeersChanged;

        /// <summary>
        /// Gets the most recently discovered peer set.
        /// </summary>
        ArrayOf<DiscoveredPeer> Peers { get; }

        /// <summary>
        /// Refreshes the peer set once, raising <see cref="PeersChanged"/> if it changed.
        /// </summary>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The discovered peer set.</returns>
        ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default);
    }
}
