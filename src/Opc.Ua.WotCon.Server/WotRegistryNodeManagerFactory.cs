// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// <see cref="IAsyncNodeManagerFactory"/> that produces the stable
    /// <see cref="WotRegistryNodeManager"/> configured with the shared registry
    /// service and materialization coordinator.
    /// </summary>
    public sealed class WotRegistryNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a new factory.
        /// </summary>
        public WotRegistryNodeManagerFactory(
            WotRegistryServerOptions options,
            IWotRegistryService registry,
            WotMaterializationCoordinator coordinator)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Namespaces.WotCon,
            XRegistryWellKnown.XRegistryNamespaceUri
        };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            // CA2000 cannot model ownership transfer through ValueTask<IAsyncNodeManager>.
            // TODO: Remove this suppression when CA2000 recognizes factory ownership transfer.
#pragma warning disable CA2000
            IAsyncNodeManager nodeManager = new WotRegistryNodeManager(
                server,
                configuration,
                m_options,
                m_registry,
                m_coordinator);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly WotRegistryServerOptions m_options;
        private readonly IWotRegistryService m_registry;
        private readonly WotMaterializationCoordinator m_coordinator;
    }
}
