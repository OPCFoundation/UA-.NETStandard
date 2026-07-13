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

using System;
using System.Collections.Generic;
using System.Net;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the default <see cref="IGossipPeerSink"/>. It fans added endpoints out
    /// to every registered live transport and replays already-added endpoints to transports that register after
    /// the fact, so discovery and transport startup can happen in any order. Registered as a singleton so the
    /// replicated address-space and session transports and the discovery loop share one instance.
    /// </summary>
    public sealed class GossipPeerRegistry : IGossipPeerSink
    {
        /// <inheritdoc/>
        public void Register(Action<IPEndPoint> addPeer)
        {
            if (addPeer == null)
            {
                throw new ArgumentNullException(nameof(addPeer));
            }

            IPEndPoint[] replay;
            lock (m_lock)
            {
                m_sinks.Add(addPeer);
                replay = [.. m_endpoints];
            }

            foreach (IPEndPoint endpoint in replay)
            {
                addPeer(endpoint);
            }
        }

        /// <inheritdoc/>
        public void AddPeer(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            Action<IPEndPoint>[] sinks;
            lock (m_lock)
            {
                if (!m_endpoints.Add(endpoint))
                {
                    return;
                }
                sinks = [.. m_sinks];
            }

            foreach (Action<IPEndPoint> sink in sinks)
            {
                sink(endpoint);
            }
        }

        /// <inheritdoc/>
        public void AddPeers(IEnumerable<IPEndPoint> endpoints)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            foreach (IPEndPoint endpoint in endpoints)
            {
                AddPeer(endpoint);
            }
        }

        private readonly System.Threading.Lock m_lock = new();
        private readonly List<Action<IPEndPoint>> m_sinks = [];
        private readonly HashSet<IPEndPoint> m_endpoints = [];
    }
}
