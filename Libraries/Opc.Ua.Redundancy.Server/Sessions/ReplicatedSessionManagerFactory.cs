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
using System.Threading;
using System.Threading.Tasks;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ISessionManagerFactory"/> that builds a
    /// <see cref="DistributedSessionManager"/> whose mirrored session entries
    /// are replicated active/active by a <see cref="ReplicatedSharedKeyValueStore"/>
    /// (gossip). The single-use server nonce stays on a strongly-consistent
    /// store resolved from the container, preserving cross-replica replay
    /// defence.
    /// </summary>
    /// <remarks>
    /// CRDT session mirroring stores <see cref="SharedSessionEntry"/> records in
    /// a gossip-replicated store. Those records carry session nonces and secret
    /// material, so startup fails closed unless an <see cref="IRecordProtector"/>
    /// is registered. <see cref="GossipTlsOptions"/> protects the transport in
    /// transit; the record protector is still required for at-rest
    /// confidentiality and integrity.
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

            ITransport transport = m_options.CreateTransport(m_services, out InMemoryNetwork? defaultNetwork);
            var entryStore = new ReplicatedSharedKeyValueStore(
                m_options.ReplicaId, transport, m_options.TimeProvider, m_options.CreateReaderOptions());

            IRecordProtector? protector = RecordProtectionGuard.ResolveProtectorOrThrow(
                m_services,
                typeof(ReplicatedSharedKeyValueStore));
            var sessionStore = new SharedKeyValueSessionStore(entryStore, server.MessageContext, protector);

            // The single-use nonce must be strongly consistent — never the CRDT
            // store (whose CompareAndSwapAsync is not supported) and never a
            // per-process fallback when fast reconnect is enabled, otherwise the
            // cross-replica session-restore replay defence is silently defeated.
            ISharedKeyValueStore? nonceStore = m_services.GetService<ISharedKeyValueStore>();
            InMemorySharedKeyValueStore? ownedNonceStore = null;
            if (nonceStore == null)
            {
                if (m_options.Session.EnableFastReconnect)
                {
                    throw new InvalidOperationException(
                        "CRDT active/active fast reconnect requires a strongly-consistent, cross-replica " +
                        "ISharedKeyValueStore for the single-use server-nonce registry. The eventually-" +
                        "consistent CRDT gossip store cannot enforce single-use (CompareAndSwapAsync is not " +
                        "supported), so without a strongly-consistent registry shared by every replica the " +
                        "cross-replica session-restore replay defence is defeated. Register a strongly-" +
                        "consistent backend (for example a Redis ISharedKeyValueStore adapter) shared across " +
                        "the replica set, or disable DistributedSessionOptions.EnableFastReconnect.");
                }

                ownedNonceStore = new InMemorySharedKeyValueStore();
                nonceStore = ownedNonceStore;
            }
            var nonceRegistry = new SharedSingleUseNonceRegistry(nonceStore);

            lock (m_lock)
            {
                m_entryStores.Add(entryStore);
                if (defaultNetwork != null)
                {
                    m_defaultNetworks.Add(defaultNetwork);
                }
                if (ownedNonceStore != null)
                {
                    m_ownedNonceStores.Add(ownedNonceStore);
                }
            }

            return new DistributedSessionManager(
                server,
                configuration,
                sessionStore,
                nonceRegistry,
                serverCertificateProvider,
                m_options.Session,
                timeProvider);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ReplicatedSharedKeyValueStore[] stores;
            InMemoryNetwork[] networks;
            InMemorySharedKeyValueStore[] nonceStores;
            lock (m_lock)
            {
                stores = [.. m_entryStores];
                m_entryStores.Clear();
                networks = [.. m_defaultNetworks];
                m_defaultNetworks.Clear();
                nonceStores = [.. m_ownedNonceStores];
                m_ownedNonceStores.Clear();
            }

            foreach (ReplicatedSharedKeyValueStore store in stores)
            {
                await store.DisposeAsync().ConfigureAwait(false);
            }
            foreach (InMemoryNetwork network in networks)
            {
                await network.DisposeAsync().ConfigureAwait(false);
            }
            foreach (InMemorySharedKeyValueStore nonceStore in nonceStores)
            {
                nonceStore.Dispose();
            }
        }

        private readonly IServiceProvider m_services;
        private readonly ReplicatedSessionOptions m_options;
        private readonly Lock m_lock = new();
        private readonly List<ReplicatedSharedKeyValueStore> m_entryStores = [];
        private readonly List<InMemoryNetwork> m_defaultNetworks = [];
        private readonly List<InMemorySharedKeyValueStore> m_ownedNonceStores = [];
    }
}
