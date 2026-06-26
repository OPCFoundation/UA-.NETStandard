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
using Crdt;
using Crdt.Transport;

namespace Opc.Ua.Server.Distributed.Crdt
{
    /// <summary>
    /// Shared CRDT gossip configuration: replica identity, time source, the
    /// transport that disseminates state between replicas, and decoding limits.
    /// </summary>
    public abstract class ReplicatedGossipOptions
    {
        /// <summary>
        /// Gets or sets this replica's stable CRDT identity. Defaults to a new
        /// random identity; supply a stable value per replica in production.
        /// </summary>
        public ReplicaId ReplicaId { get; set; } = ReplicaId.New();

        /// <summary>
        /// Gets or sets the time source used by the logical clock.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        /// <summary>
        /// Gets or sets the factory that creates the gossip transport. When
        /// <c>null</c>, an isolated in-process transport is used (single-process
        /// / development only — no cross-node replication); configure
        /// <see cref="UseTcpGossip"/> or <see cref="UseUdpGossip"/> for a real
        /// deployment.
        /// </summary>
        public Func<IServiceProvider, ITransport>? TransportFactory { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of replicated map entries accepted
        /// when decoding received state.
        /// </summary>
        public int MaxEntryCount { get; set; } = 1_000_000;

        /// <summary>
        /// Gets or sets the maximum encoded key/payload size (bytes) accepted
        /// when decoding received state.
        /// </summary>
        public int MaxPayloadBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Configures a TCP gossip transport. Peers added via
        /// <see cref="AddPeer"/> are attached to the created transport.
        /// </summary>
        /// <param name="address">The local bind address.</param>
        /// <param name="port">The local bind port (<c>0</c> for an OS-assigned port).</param>
        /// <param name="gossipInterval">Optional anti-entropy gossip interval.</param>
        /// <param name="tls">Optional TLS / mutual-TLS configuration.</param>
        public void UseTcpGossip(
            IPAddress address,
            int port,
            TimeSpan? gossipInterval = null,
            GossipTlsOptions? tls = null)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            TransportFactory = _ =>
            {
                var transportOptions = new TcpGossipTransportOptions
                {
                    Address = address,
                    Port = port,
                    Tls = tls
                };
                if (gossipInterval.HasValue)
                {
                    transportOptions.GossipInterval = gossipInterval.Value;
                }

                var transport = new TcpGossipTransport(transportOptions);
                transport.AddPeers(m_peers);
                return transport;
            };
        }

        /// <summary>
        /// Configures a UDP datagram gossip transport. Peers added via
        /// <see cref="AddPeer"/> are attached to the created transport.
        /// </summary>
        /// <param name="address">The local bind address.</param>
        /// <param name="port">The local bind port (<c>0</c> for an OS-assigned port).</param>
        /// <param name="gossipInterval">Optional anti-entropy gossip interval.</param>
        public void UseUdpGossip(IPAddress address, int port, TimeSpan? gossipInterval = null)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            TransportFactory = _ =>
            {
                var transport = new UdpGossipTransport(
                    address, port, gossipInterval ?? TimeSpan.FromMilliseconds(500));
                transport.AddPeers(m_peers);
                return transport;
            };
        }

        /// <summary>
        /// Adds a peer endpoint to gossip with. Applied to the transport
        /// created by <see cref="UseTcpGossip"/> / <see cref="UseUdpGossip"/>.
        /// </summary>
        /// <param name="endpoint">The peer endpoint.</param>
        public void AddPeer(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            m_peers.Add(endpoint);
        }

        /// <summary>
        /// Builds the decoding limits for received state.
        /// </summary>
        internal CrdtReaderOptions CreateReaderOptions()
        {
            return new CrdtReaderOptions
            {
                MaxCollectionCount = MaxEntryCount,
                MaxStringBytes = MaxPayloadBytes,
                MaxDepth = CrdtReaderOptions.Default.MaxDepth
            };
        }

        /// <summary>
        /// Creates the transport for one replica. When no factory is
        /// configured, an isolated in-process network is created and returned
        /// via <paramref name="defaultNetwork"/> so the caller can dispose it.
        /// </summary>
        internal ITransport CreateTransport(IServiceProvider services, out InMemoryNetwork? defaultNetwork)
        {
            if (TransportFactory != null)
            {
                defaultNetwork = null;
                return TransportFactory(services);
            }

            var network = new InMemoryNetwork();
            defaultNetwork = network;
            return network.CreateTransport();
        }

        private readonly List<IPEndPoint> m_peers = [];
    }
}
