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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: single-call configuration for an active/active (multi-writer)
    /// redundant server. It captures the CRDT gossip settings once and wires both the replicated address space
    /// and the replicated session store from them, so a deployment selects active/active with one call rather
    /// than configuring <see cref="ReplicatedServerBuilderExtensions.UseReplicatedAddressSpace"/> and
    /// <see cref="ReplicatedServerBuilderExtensions.UseReplicatedSessions"/> separately.
    /// </summary>
    /// <remarks>
    /// Session gossip runs on <see cref="GossipPort"/> + 1 by convention, so peers are reached at their
    /// address-space port for the address space and that port + 1 for sessions. The individual
    /// <c>UseReplicated*</c> methods remain available for advanced setups that need to diverge from these
    /// defaults.
    /// </remarks>
    public sealed class ActiveActiveRedundancyOptions
    {
        /// <summary>
        /// Gets or sets this replica's stable CRDT identity. Supply a stable value per replica in production.
        /// </summary>
        public ReplicaId ReplicaId { get; set; } = ReplicaId.New();

        /// <summary>
        /// Gets or sets the local bind address for the gossip transport. Defaults to <see cref="IPAddress.Any"/>.
        /// </summary>
        public IPAddress BindAddress { get; set; } = IPAddress.Any;

        /// <summary>
        /// Gets or sets the local address-space gossip port. Session gossip uses this port + 1.
        /// </summary>
        public int GossipPort { get; set; } = 4840;

        /// <summary>
        /// Gets or sets the optional anti-entropy gossip interval.
        /// </summary>
        public TimeSpan? GossipInterval { get; set; }

        /// <summary>
        /// Gets or sets the mutual-TLS configuration for the gossip transport. Required for production unless
        /// <see cref="AllowUnauthenticatedGossip"/> is explicitly enabled.
        /// </summary>
        public GossipTlsOptions? Tls { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether gossip may start without authenticated transport. Leave at
        /// the secure default (<c>false</c>) for production; set <c>true</c> only for isolated development or
        /// test fabrics.
        /// </summary>
        public bool AllowUnauthenticatedGossip { get; set; }

        /// <summary>
        /// Gets or sets the time source used by the logical clock.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        /// <summary>
        /// Gets or sets a value indicating whether mirrored sessions may fast-reconnect after failover. The
        /// safe default (<c>false</c>) re-authenticates on failover.
        /// </summary>
        public bool EnableFastReconnect { get; set; }

        /// <summary>
        /// Gets the static address-space gossip peer endpoints. Session gossip reaches each peer at its port
        /// + 1. Leave empty and configure peer discovery for a dynamically-scaled deployment.
        /// </summary>
        public IList<IPEndPoint> Peers { get; } = [];

        /// <summary>
        /// Adds a static gossip peer endpoint (address-space port; session gossip uses port + 1).
        /// </summary>
        /// <param name="endpoint">The peer endpoint.</param>
        /// <returns>These options, for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public ActiveActiveRedundancyOptions AddPeer(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            Peers.Add(endpoint);
            return this;
        }
    }
}
