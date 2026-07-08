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
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;

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
        /// <see cref="ReplicatedSharedKeyValueStore"/> is used by the server side, so a
        /// single CRDT implementation backs both.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddCrdtClientSharedStore(
            this IServiceCollection services,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport> transportFactory
        )
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (transportFactory == null)
            {
                throw new ArgumentNullException(nameof(transportFactory));
            }

            services.TryAddSingleton<ISharedKeyValueStore>(sp => new ReplicatedSharedKeyValueStore(
                replicaId,
                transportFactory(sp),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                CrdtReaderOptions.Default
            ));
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
            Func<IServiceProvider, IRaftConsensus>? consensusFactory = null
        )
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton(sp => consensusFactory?.Invoke(sp) ?? DefaultRaftConsensus.CreateSingleNode());

            services.TryAddSingleton<ISharedKeyValueStore>(sp => new RaftSharedKeyValueStore(
                sp.GetRequiredService<IRaftConsensus>(),
                ownsConsensus: false
            ));

            services.TryAddSingleton<ILeaderElection>(sp => new RaftLeaderElection(
                sp.GetRequiredService<IRaftConsensus>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger<RaftLeaderElection>()
            ));

            return services;
        }

        /// <summary>
        /// Registers the consistency-mode-appropriate <see cref="ISharedKeyValueStore"/> and a native
        /// <see cref="RaftLeaderElection"/> for a client replica set:
        /// <see cref="RedundancyConsistencyMode.Strong"/> uses a <see cref="RaftSharedKeyValueStore"/>;
        /// <see cref="RedundancyConsistencyMode.Eventual"/> uses a <see cref="HybridSharedKeyValueStore"/> over a CRDT
        /// bulk store (gossiped on <paramref name="replicatedTransportFactory"/>) and a Raft strong store.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="mode">The consistency model.</param>
        /// <param name="replicaId">This client's stable CRDT identity (used in eventual mode).</param>
        /// <param name="replicatedTransportFactory">
        /// The CRDT gossip transport factory; required for <see cref="RedundancyConsistencyMode.Eventual"/>.
        /// </param>
        /// <param name="raftConsensusFactory">
        /// Optional Raft consensus replica factory (defaults to a single-node <see cref="InProcessRaftConsensus"/>).
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is <c>null</c>, or <paramref name="replicatedTransportFactory"/> is <c>null</c> in
        /// eventual mode.
        /// </exception>
        public static IServiceCollection AddRedundantClientSharedStore(
            this IServiceCollection services,
            RedundancyConsistencyMode mode,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport>? replicatedTransportFactory = null,
            Func<IServiceProvider, IRaftConsensus>? raftConsensusFactory = null
        )
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (mode == RedundancyConsistencyMode.Eventual && replicatedTransportFactory == null)
            {
                throw new ArgumentNullException(
                    nameof(replicatedTransportFactory),
                    "Eventual mode requires a CRDT transport factory for the bulk store."
                );
            }

            services.TryAddSingleton(sp => raftConsensusFactory?.Invoke(sp) ?? DefaultRaftConsensus.CreateSingleNode());

            services.TryAddSingleton(sp => CreateClientStore(sp, mode, replicaId, replicatedTransportFactory));

            services.TryAddSingleton<ILeaderElection>(sp => new RaftLeaderElection(
                sp.GetRequiredService<IRaftConsensus>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger<RaftLeaderElection>()
            ));

            return services;
        }

        /// <summary>
        /// Registers a transparent <see cref="ISession"/> facade backed by client replica coordination.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static IServiceCollection AddRedundantClientSession(
            this IServiceCollection services,
            Action<RedundantClientSessionOptions> configure
        )
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new RedundantClientSessionOptions();
            configure(options);
            if (options.CreateSessionAsync == null)
            {
                throw new ArgumentException(
                    "RedundantClientSessionOptions.CreateSessionAsync must be set.",
                    nameof(configure)
                );
            }

            services.TryAddSingleton(sp =>
            {
                var replicaOptions = new ClientReplicaOptions
                {
                    NodeId = options.NodeId,
                    Mode = options.Mode,
                    CreateSessionAsync = options.CreateSessionAsync,
                    ConfigureLeaderAsync = options.ConfigureLeaderAsync
                };

                var coordinator = new ClientReplicaCoordinator(
                    replicaOptions,
                    sp.GetRequiredService<ILeaderElection>(),
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    sp.GetService<IRecordProtector>() ?? NullRecordProtector.Instance,
                    sp.GetRequiredService<ITelemetryContext>()
                );
                return new RedundantClientSession(coordinator);
            });
            services.TryAddSingleton<ISession>(sp => sp.GetRequiredService<RedundantClientSession>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, RedundantClientSessionHostedService>()
            );
            return services;
        }

        private static ISharedKeyValueStore CreateClientStore(
            IServiceProvider sp,
            RedundancyConsistencyMode mode,
            ReplicaId replicaId,
            Func<IServiceProvider, ITransport>? replicatedTransportFactory
        )
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

            var replicatedStore = new ReplicatedSharedKeyValueStore(
                replicaId,
                replicatedTransportFactory!(sp),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                CrdtReaderOptions.Default
            );
            return new HybridSharedKeyValueStore(replicatedStore, raftStore, default, ownsStores: true);
#pragma warning restore CA2000
        }

        private sealed class RedundantClientSessionHostedService : IHostedService
        {
            public RedundantClientSessionHostedService(RedundantClientSession session)
            {
                m_session = session ?? throw new ArgumentNullException(nameof(session));
            }

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                await m_session.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }

            private readonly RedundantClientSession m_session;
        }
    }
}
