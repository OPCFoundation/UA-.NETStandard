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
