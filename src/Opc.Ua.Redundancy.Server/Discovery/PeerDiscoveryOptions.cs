/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for the <see cref="IPeerDiscovery"/> refresh loop that keeps
    /// the redundant server set and the CRDT gossip fabric current at runtime.
    /// </summary>
    public sealed class PeerDiscoveryOptions
    {
        /// <summary>
        /// Gets or sets how often the peer set is refreshed. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
}
