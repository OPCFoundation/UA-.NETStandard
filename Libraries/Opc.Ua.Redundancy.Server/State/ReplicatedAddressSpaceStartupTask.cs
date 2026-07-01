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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: server startup task that attaches a
    /// <see cref="ReplicatedAddressSpaceSynchronizer"/>
    /// to every node manager that opts in via <see cref="ILocalAddressSpaceSource"/>,
    /// enabling active/active (multi-writer) replication of its address space.
    /// </summary>
    public sealed class ReplicatedAddressSpaceStartupTask : IServerStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Creates the wiring task.
        /// </summary>
        /// <param name="services">The application service provider (for the transport factory).</param>
        /// <param name="options">The CRDT address-space options.</param>
        public ReplicatedAddressSpaceStartupTask(IServiceProvider services, ReplicatedAddressSpaceOptions options)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ILogger logger = server.Telemetry.CreateLogger<ReplicatedAddressSpaceStartupTask>();

            foreach (INodeManager nodeManager in server.NodeManager.NodeManagers)
            {
                if (nodeManager is not ILocalAddressSpaceSource source)
                {
                    continue;
                }

                ILocalAddressSpace addressSpace = source.CreateLocalAddressSpace();
                ITransport transport = m_options.CreateTransport(m_services, out InMemoryNetwork? defaultNetwork);
                if (defaultNetwork != null)
                {
                    lock (m_lock)
                    {
                        m_defaultNetworks.Add(defaultNetwork);
                    }
                }

                var synchronizer = new ReplicatedAddressSpaceSynchronizer(
                    addressSpace,
                    server.MessageContext,
                    m_options.ReplicaId,
                    transport,
                    m_options.TimeProvider,
                    m_options.CreateReaderOptions(),
                    logger);

                await synchronizer.SeedOrHydrateAsync(cancellationToken).ConfigureAwait(false);
                synchronizer.Start();
                lock (m_lock)
                {
                    m_synchronizers.Add(synchronizer);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ReplicatedAddressSpaceSynchronizer[] synchronizers;
            InMemoryNetwork[] networks;
            lock (m_lock)
            {
                synchronizers = [.. m_synchronizers];
                m_synchronizers.Clear();
                networks = [.. m_defaultNetworks];
                m_defaultNetworks.Clear();
            }

            foreach (ReplicatedAddressSpaceSynchronizer synchronizer in synchronizers)
            {
                await synchronizer.DisposeAsync().ConfigureAwait(false);
            }
            foreach (InMemoryNetwork network in networks)
            {
                await network.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly IServiceProvider m_services;
        private readonly ReplicatedAddressSpaceOptions m_options;
        private readonly Lock m_lock = new();
        private readonly List<ReplicatedAddressSpaceSynchronizer> m_synchronizers = [];
        private readonly List<InMemoryNetwork> m_defaultNetworks = [];
    }
}
