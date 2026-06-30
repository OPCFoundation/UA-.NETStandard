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
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: fluent selection of the shared-store consistency model (strong Raft vs.
    /// eventual CRDT complemented by Raft) on the <see cref="IOpcUaServerBuilder"/>.
    /// </summary>
    public static class RedundancyConsistencyBuilderExtensions
    {
        /// <summary>
        /// Registers the consistency-mode-appropriate <see cref="ISharedKeyValueStore"/>, the shared
        /// <see cref="IRaftConsensus"/> replica, and (by default) a native <see cref="RaftLeaderElection"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In <see cref="RedundancyConsistencyMode.Strong"/> mode the shared store is a linearizable
        /// <see cref="RaftSharedKeyValueStore"/>. In <see cref="RedundancyConsistencyMode.Eventual"/> mode (the
        /// default) it is a <see cref="HybridSharedKeyValueStore"/> that routes bulk keys to the CRDT bulk store and
        /// the strong-prefix keyspaces (single-use nonces, lease, election) to Raft.
        /// </para>
        /// <para>
        /// Call this <b>before</b> <see cref="DistributedServerBuilderExtensions.UseDistributedAddressSpace"/> /
        /// <see cref="DistributedServerBuilderExtensions.UseDistributedSessions"/>: those use
        /// <c>TryAddSingleton</c> for the same services, so the consistency registration wins and the distributed
        /// features compose over the chosen store and election.
        /// </para>
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional consistency options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseRedundancyConsistency(
            this IOpcUaServerBuilder builder,
            Action<RedundancyConsistencyOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new RedundancyConsistencyOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton<IRaftConsensus>(sp =>
                options.RaftConsensusFactory?.Invoke(sp) ?? RaftCsConsensus.CreateSingleNode(options.NodeId));

            builder.Services.TryAddSingleton<ISharedKeyValueStore>(sp =>
                CreateStore(sp, options));

            if (options.UseRaftLeaderElection)
            {
                builder.Services.TryAddSingleton<ILeaderElection>(sp =>
                    new RaftLeaderElection(
                        sp.GetRequiredService<IRaftConsensus>(),
                        sp.GetService<ILoggerFactory>()?.CreateLogger<RaftLeaderElection>()));
            }

            return builder;
        }

        /// <summary>
        /// Registers the shared store and election for the given consistency <paramref name="mode"/> using defaults.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="mode">The consistency model to use.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseRedundancyConsistency(
            this IOpcUaServerBuilder builder,
            RedundancyConsistencyMode mode)
        {
            return builder.UseRedundancyConsistency(options => options.Mode = mode);
        }

        private static ISharedKeyValueStore CreateStore(
            IServiceProvider sp,
            RedundancyConsistencyOptions options)
        {
            IRaftConsensus consensus = sp.GetRequiredService<IRaftConsensus>();

            // CA2000: this is a DI factory; ownership of every store created
            // here transfers to the returned ISharedKeyValueStore (the Hybrid
            // owns the Raft + bulk stores via ownsStores: true) and ultimately
            // to the container, which disposes the singleton.
#pragma warning disable CA2000
            // The consensus replica is a container-owned singleton shared with
            // the leader election, so the store must not dispose it.
            var raftStore = new RaftSharedKeyValueStore(consensus, ownsConsensus: false);
            if (options.Mode == RedundancyConsistencyMode.Strong)
            {
                return raftStore;
            }

            ISharedKeyValueStore bulk = options.BulkStoreFactory?.Invoke(sp)
                ?? new InMemorySharedKeyValueStore();
            return new HybridSharedKeyValueStore(
                bulk,
                raftStore,
                options.StrongKeyPrefixes,
                ownsStores: true);
#pragma warning restore CA2000
        }
    }
}
