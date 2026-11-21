// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Net;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a runtime sink for CRDT gossip peer endpoints. Peer discovery pushes
    /// newly-discovered gossip endpoints here, and the active gossip transports add them to their live peer set
    /// so scale-up is reflected without a restart. The gossip transport is add-only, so decommissioned peers
    /// are not actively removed; they age out as gossip to a dead endpoint simply fails and is retried.
    /// </summary>
    public interface IGossipPeerSink
    {
        /// <summary>
        /// Registers a live transport's add-peer callback. Any endpoints already added to the sink are replayed
        /// to the callback so a transport that starts after discovery still learns the current peers.
        /// </summary>
        /// <param name="addPeer">The transport's add-peer callback.</param>
        void Register(System.Action<IPEndPoint> addPeer);

        /// <summary>
        /// Adds a gossip peer endpoint to every registered transport.
        /// </summary>
        /// <param name="endpoint">The peer endpoint.</param>
        void AddPeer(IPEndPoint endpoint);

        /// <summary>
        /// Adds several gossip peer endpoints to every registered transport.
        /// </summary>
        /// <param name="endpoints">The peer endpoints.</param>
        void AddPeers(IEnumerable<IPEndPoint> endpoints);
    }
}
