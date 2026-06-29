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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent registration of CRDT active/active replication features on the
    /// <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class ReplicatedServerBuilderExtensions
    {
        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers active/active (multi-writer) replication of the address
        /// space using CRDTs gossiped between replicas. Every replica accepts
        /// writes and converges without a leader; this is an alternative to the
        /// leader-write active/passive <c>UseDistributedAddressSpace</c>.
        /// Configure mutual TLS on the TCP gossip transport when address-space updates cross a network
        /// boundary. Startup fails closed for unauthenticated TCP/UDP gossip unless the deployment explicitly
        /// sets <see cref="ReplicatedGossipOptions.AllowUnauthenticatedGossip"/> for an isolated test fabric.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional CRDT address-space options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseReplicatedAddressSpace(
            this IOpcUaServerBuilder builder,
            Action<ReplicatedAddressSpaceOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new ReplicatedAddressSpaceOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton<IServerStartupTask>(
                sp => new CrdtAddressSpaceStartupTask(sp, options));

            return builder;
        }

        /// <summary>
        /// Extension beyond OPC 10000-4 §6.6: registers active/active session replication. Mirrored session
        /// entries are gossiped between replicas as a CRDT so a client can fail
        /// over to any replica and fast-reconnect. The single-use server nonce
        /// stays on a strongly-consistent store (resolved from the container),
        /// so the cross-replica replay defence is preserved; the authentication
        /// token is never an authenticator on its own.
        /// </summary>
        /// <remarks>
        /// Mirrored session entries contain session nonces and secret material.
        /// Register an <see cref="IRecordProtector"/> to provide at-rest
        /// confidentiality and integrity for those CRDT records; startup fails
        /// closed without one. <c>GossipTlsOptions</c> protects gossip
        /// traffic in transit but does not replace the record protector.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional CRDT session options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseReplicatedSessions(
            this IOpcUaServerBuilder builder,
            Action<ReplicatedSessionOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new ReplicatedSessionOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton<ISessionManagerFactory>(
                sp => new CrdtSessionManagerFactory(sp, options));

            return builder;
        }
    }
}
