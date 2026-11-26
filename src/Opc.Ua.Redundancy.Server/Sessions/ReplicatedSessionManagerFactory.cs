// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ISessionManagerFactory"/> that builds a
    /// <see cref="DistributedSessionManager"/> whose mirrored session entries
    /// are either replicated by gossip as metadata or committed through an independent authority
    /// when fast reconnect is enabled.
    /// </summary>
    /// <remarks>
    /// CRDT session mirroring stores <see cref="SharedSessionEntry"/> records in
    /// a gossip-replicated store. Those records carry session nonces and secret
    /// material, so startup fails closed unless an <see cref="IRecordProtector"/>
    /// is registered. <see cref="GossipTlsOptions"/> protects the transport in
    /// transit; the record protector is still required for at-rest
    /// confidentiality and integrity. Fast reconnect does not use a CRDT record as
    /// authority for activation.
    /// </remarks>
    public sealed class ReplicatedSessionManagerFactory : ISessionManagerFactory, IAsyncDisposable
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="services">The application service provider.</param>
        /// <param name="options">The CRDT session options.</param>
        public ReplicatedSessionManagerFactory(IServiceProvider services, ReplicatedSessionOptions options)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ISessionManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider timeProvider,
            Func<string, Certificate?> serverCertificateProvider)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (m_options.Session.EnableFastReconnect)
            {
                FreshnessProtectedKeyValueStore? freshStore =
                    m_services.GetService<FreshnessProtectedKeyValueStore>() ??
                    m_services.GetService<ISharedKeyValueStore>() as FreshnessProtectedKeyValueStore;
                if (freshStore == null)
                {
                    throw new InvalidOperationException(
                        "Fast reconnect requires a committed session store backed by an independent freshness authority.");
                }

                return CreateSessionManager(
                    server, configuration, timeProvider, serverCertificateProvider, freshStore,
                    m_services.GetRequiredService<IRecordProtector>());
            }

            IRecordProtector? protector = RecordProtectionGuard.ResolveProtectorOrThrow(
                m_services,
                typeof(ReplicatedSharedKeyValueStore));
            ITransport transport = m_options.CreateTransport(m_services, out InMemoryNetwork? defaultNetwork);
            var entryStore = new ReplicatedSharedKeyValueStore(
                m_options.ReplicaId,
                transport,
                m_options.TimeProvider,
                m_options.CreateReaderOptions(),
                m_services.GetService<ITelemetryContext>()?.CreateLogger<ReplicatedSharedKeyValueStore>() ??
                    NullLogger<ReplicatedSharedKeyValueStore>.Instance);

            lock (m_lock)
            {
                m_entryStores.Add(entryStore);
                if (defaultNetwork != null)
                {
                    m_defaultNetworks.Add(defaultNetwork);
                }
            }

            return CreateSessionManager(
                server, configuration, timeProvider, serverCertificateProvider, entryStore, protector);
        }

        private DistributedSessionManager CreateSessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider timeProvider,
            Func<string, Certificate?> serverCertificateProvider,
            ISharedKeyValueStore entries,
            IRecordProtector? protector)
        {
            return new DistributedSessionManager(
                server,
                configuration,
                new SharedKeyValueSessionStore(entries, server.MessageContext, protector),
                serverCertificateProvider,
                m_options.Session,
                timeProvider);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ReplicatedSharedKeyValueStore[] stores;
            InMemoryNetwork[] networks;
            lock (m_lock)
            {
                stores = [.. m_entryStores];
                m_entryStores.Clear();
                networks = [.. m_defaultNetworks];
                m_defaultNetworks.Clear();
            }

            foreach (ReplicatedSharedKeyValueStore store in stores)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
            foreach (InMemoryNetwork network in networks)
            {
                await network.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly IServiceProvider m_services;
        private readonly ReplicatedSessionOptions m_options;
        private readonly Lock m_lock = new();
        private readonly List<ReplicatedSharedKeyValueStore> m_entryStores = [];
        private readonly List<InMemoryNetwork> m_defaultNetworks = [];
    }
}
