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
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: server startup task that attaches a
    /// <see cref="ReplicatedAddressSpaceSynchronizer"/>
    /// to the shared partitions opted in via <see cref="ILocalAddressSpaceSource"/>,
    /// enabling active/active (multi-writer) replication of its address space.
    /// </summary>
    public sealed class ReplicatedAddressSpaceStartupTask : IServerStartupTask, IServerPreStartupTask, IAsyncDisposable
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
        public ValueTask OnServerStartingAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (server is not INodeIdFactoryProvider { NodeIdFactory: ReplicaNodeIdFactory identity })
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Active/active replica startup requires an explicit fixed namespace and NodeId identity policy.");
            }
            identity.ValidateNamespaces(server.MessageContext.NamespaceUris);
            if (identity.UsesWriterAssignedIds)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Active/active replicas cannot independently allocate writer-assigned identities.");
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            await OnServerStartingAsync(server, cancellationToken).ConfigureAwait(false);
            var identity = (ReplicaNodeIdFactory)((INodeIdFactoryProvider)server).NodeIdFactory!;
            ILogger logger = server.DefaultSystemContext.Telemetry.CreateLogger<ReplicatedAddressSpaceStartupTask>();

            List<AddressSpaceRegistration> registrations = AddressSpaceRegistration.Create(server, identity);
            var addressSpace = new ReplicaLocalAddressSpace(server.DefaultSystemContext, registrations);
            lock (m_lock)
            {
                m_addressSpaces.Add(addressSpace);
            }
            try
            {
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
                    logger,
                    identity,
                    addressSpace.OwnsNode);

                lock (m_lock)
                {
                    m_synchronizers.Add(synchronizer);
                }
                await synchronizer.SeedOrHydrateAsync(cancellationToken).ConfigureAwait(false);
                synchronizer.Start();
                m_identity = identity;
                identity.SetAddressSpaceRebinder(server, async (current, token) =>
                {
                    List<AddressSpaceRegistration> nextRegistrations = AddressSpaceRegistration.Create(current, identity);
                    var next = new ReplicaLocalAddressSpace(current.DefaultSystemContext, nextRegistrations);
                    lock (m_lock)
                    {
                        m_addressSpaces.Add(next);
                    }
                    await synchronizer.RebindAsync(next, next.OwnsNode, token).ConfigureAwait(false);
                    ReplicaLocalAddressSpace[] previous;
                    lock (m_lock)
                    {
                        previous = [.. m_addressSpaces];
                        m_addressSpaces.Clear();
                        m_addressSpaces.Add(next);
                    }
                    foreach (ReplicaLocalAddressSpace old in previous)
                    {
                        if (!ReferenceEquals(old, next))
                        {
                            old.Dispose();
                        }
                    }
                });
                identity.SetNodeManagerPreparer((current, next, previous, token) =>
                    synchronizer.HydratePreparedAsync(
                        AddressSpaceRegistration.CreatePrepared(current, identity, next, previous),
                        token));
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_identity?.ClearAddressSpaceRebinder();
            ReplicatedAddressSpaceSynchronizer[] synchronizers;
            InMemoryNetwork[] networks;
            ReplicaLocalAddressSpace[] addressSpaces;
            lock (m_lock)
            {
                synchronizers = [.. m_synchronizers];
                m_synchronizers.Clear();
                networks = [.. m_defaultNetworks];
                m_defaultNetworks.Clear();
                addressSpaces = [.. m_addressSpaces];
                m_addressSpaces.Clear();
            }

            foreach (ReplicatedAddressSpaceSynchronizer synchronizer in synchronizers)
            {
                await synchronizer.DisposeAsync().ConfigureAwait(false);
            }
            foreach (InMemoryNetwork network in networks)
            {
                await network.DisposeAsync().ConfigureAwait(false);
            }
            foreach (ReplicaLocalAddressSpace addressSpace in addressSpaces)
            {
                addressSpace.Dispose();
            }
        }

        private readonly IServiceProvider m_services;
        private readonly ReplicatedAddressSpaceOptions m_options;
        private readonly Lock m_lock = new();
        private readonly List<ReplicatedAddressSpaceSynchronizer> m_synchronizers = [];
        private readonly List<InMemoryNetwork> m_defaultNetworks = [];
        private readonly List<ReplicaLocalAddressSpace> m_addressSpaces = [];
        private ReplicaNodeIdFactory? m_identity;
    }
}
