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
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Redundancy.Client
{
    /// <summary>
    /// Dependency-injection registration for the CRDT components used by client
    /// replica sets (the cross-process gossip store that shares the leader's
    /// session secrets between cooperating clients).
    /// </summary>
    public static class ClientRedundancyServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a CRDT-backed <see cref="ISharedKeyValueStore"/> for a client
        /// replica set, gossiping over the supplied transport. The same shared
        /// <see cref="CrdtSharedKeyValueStore"/> is used by the server side, so a
        /// single CRDT implementation backs both.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddCrdtClientSharedStore(
            this IServiceCollection services,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport> transportFactory)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (transportFactory == null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }

            services.TryAddSingleton<ISharedKeyValueStore>(sp =>
            {
                return new CrdtSharedKeyValueStore(
                    replicaId,
                    transportFactory(sp),
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    CrdtReaderOptions.Default);
            });
            return services;
        }

        /// <summary>
        /// Registers a strongly-consistent (Raft-backed) <see cref="ISharedKeyValueStore"/> and a native
        /// <see cref="RaftLeaderElection"/> for a client replica set, both sharing one <see cref="IRaftConsensus"/>
        /// replica. Unlike <see cref="AddCrdtClientSharedStore"/>, the linearizable store gives the promoted follower
        /// a consistent view of the leader's session secrets, and the election decides the primary client without a
        /// separate leader-election registration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="consensusFactory">
        /// Optional factory for the Raft consensus replica. When <c>null</c>, a single-node
        /// <see cref="InProcessRaftConsensus"/> is used (the external multi-node Raft engine plugs in here).
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddRaftClientSharedStore(
            this IServiceCollection services,
            Func<IServiceProvider, IRaftConsensus>? consensusFactory = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IRaftConsensus>(sp =>
                consensusFactory?.Invoke(sp) ?? RaftCsConsensus.CreateSingleNode());

            services.TryAddSingleton<ISharedKeyValueStore>(sp =>
                new RaftSharedKeyValueStore(sp.GetRequiredService<IRaftConsensus>(), ownsConsensus: false));

            services.TryAddSingleton<ILeaderElection>(sp =>
                new RaftLeaderElection(
                    sp.GetRequiredService<IRaftConsensus>(),
                    sp.GetService<ILoggerFactory>()?.CreateLogger<RaftLeaderElection>()));

            return services;
        }

        /// <summary>
        /// Registers the consistency-mode-appropriate <see cref="ISharedKeyValueStore"/> and a native
        /// <see cref="RaftLeaderElection"/> for a client replica set:
        /// <see cref="RedundancyConsistencyMode.Strong"/> uses a <see cref="RaftSharedKeyValueStore"/>;
        /// <see cref="RedundancyConsistencyMode.Eventual"/> uses a <see cref="HybridSharedKeyValueStore"/> over a CRDT
        /// bulk store (gossiped on <paramref name="crdtTransportFactory"/>) and a Raft strong store.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="mode">The consistency model.</param>
        /// <param name="replicaId">This client's stable CRDT identity (used in eventual mode).</param>
        /// <param name="crdtTransportFactory">
        /// The CRDT gossip transport factory; required for <see cref="RedundancyConsistencyMode.Eventual"/>.
        /// </param>
        /// <param name="raftConsensusFactory">
        /// Optional Raft consensus replica factory (defaults to a single-node <see cref="InProcessRaftConsensus"/>).
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>, or <paramref name="crdtTransportFactory"/> is <c>null</c> in
        /// eventual mode.
        /// </exception>
        public static IServiceCollection AddRedundantClientSharedStore(
            this IServiceCollection services,
            RedundancyConsistencyMode mode,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport>? crdtTransportFactory = null,
            Func<IServiceProvider, IRaftConsensus>? raftConsensusFactory = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (mode == RedundancyConsistencyMode.Eventual && crdtTransportFactory == null)
            {
                throw new ArgumentNullException(
                    nameof(crdtTransportFactory),
                    "Eventual mode requires a CRDT transport factory for the bulk store.");
            }

            services.TryAddSingleton<IRaftConsensus>(sp =>
                raftConsensusFactory?.Invoke(sp) ?? RaftCsConsensus.CreateSingleNode());

            services.TryAddSingleton<ISharedKeyValueStore>(sp =>
                CreateClientStore(sp, mode, replicaId, crdtTransportFactory));

            services.TryAddSingleton<ILeaderElection>(sp =>
                new RaftLeaderElection(
                    sp.GetRequiredService<IRaftConsensus>(),
                    sp.GetService<ILoggerFactory>()?.CreateLogger<RaftLeaderElection>()));

            return services;
        }

        private static ISharedKeyValueStore CreateClientStore(
            IServiceProvider sp,
            RedundancyConsistencyMode mode,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport>? crdtTransportFactory)
        {
            IRaftConsensus consensus = sp.GetRequiredService<IRaftConsensus>();

            // CA2000: this is a DI factory; ownership of every store created here
            // transfers to the returned ISharedKeyValueStore (the Hybrid owns the
            // Raft + CRDT stores via ownsStores: true) and to the container.
#pragma warning disable CA2000
            var raftStore = new RaftSharedKeyValueStore(consensus, ownsConsensus: false);
            if (mode == RedundancyConsistencyMode.Strong)
            {
                return raftStore;
            }

            var crdtStore = new CrdtSharedKeyValueStore(
                replicaId,
                crdtTransportFactory!(sp),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                CrdtReaderOptions.Default);
            return new HybridSharedKeyValueStore(crdtStore, raftStore, default, ownsStores: true);
#pragma warning restore CA2000
        }
    }
}
