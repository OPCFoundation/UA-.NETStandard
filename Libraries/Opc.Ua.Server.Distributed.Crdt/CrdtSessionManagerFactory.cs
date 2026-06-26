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

namespace Opc.Ua.Server.Distributed.Crdt
{
    /// <summary>
    /// An <see cref="ISessionManagerFactory"/> that builds a
    /// <see cref="DistributedSessionManager"/> whose mirrored session entries
    /// are replicated active/active by a <see cref="CrdtSharedKeyValueStore"/>
    /// (gossip). The single-use server nonce stays on a strongly-consistent
    /// store resolved from the container, preserving cross-replica replay
    /// defence.
    /// </summary>
    public sealed class CrdtSessionManagerFactory : ISessionManagerFactory, IAsyncDisposable
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="services">The application service provider.</param>
        /// <param name="options">The CRDT session options.</param>
        public CrdtSessionManagerFactory(IServiceProvider services, CrdtSessionOptions options)
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
            var entryStore = new CrdtSharedKeyValueStore(
                m_options.ReplicaId, transport, m_options.TimeProvider, m_options.CreateReaderOptions());

            IRecordProtector? protector = m_services.GetService<IRecordProtector>();
            var sessionStore = new SharedKeyValueSessionStore(entryStore, server.MessageContext, protector);

            // The single-use nonce must be strongly consistent — never the CRDT
            // store. Use a registered shared store (e.g. the address-space
            // backend or a Redis adapter) or an in-process fallback.
            ISharedKeyValueStore? nonceStore = m_services.GetService<ISharedKeyValueStore>();
            InMemorySharedKeyValueStore? ownedNonceStore = null;
            if (nonceStore == null)
            {
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
            CrdtSharedKeyValueStore[] stores;
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

            foreach (CrdtSharedKeyValueStore store in stores)
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
        private readonly CrdtSessionOptions m_options;
        private readonly Lock m_lock = new();
        private readonly List<CrdtSharedKeyValueStore> m_entryStores = [];
        private readonly List<InMemoryNetwork> m_defaultNetworks = [];
        private readonly List<InMemorySharedKeyValueStore> m_ownedNonceStores = [];
    }
}
