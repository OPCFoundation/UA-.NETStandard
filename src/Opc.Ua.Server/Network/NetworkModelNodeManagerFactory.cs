// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Creates a Part 22 address-space manager for one application-owned network provider.
    /// </summary>
    public sealed class NetworkModelNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes a factory with a provider and explicit mapping-edit authorization policy.
        /// </summary>
        /// <param name="provider">The application-owned network model provider.</param>
        /// <param name="authorizeMappingEdits">Returns whether the caller may edit mappings.</param>
        public NetworkModelNodeManagerFactory(
            INetworkModelProvider provider,
            Func<ISystemContext, bool> authorizeMappingEdits)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_authorizeMappingEdits = authorizeMappingEdits
                ?? throw new ArgumentNullException(nameof(authorizeMappingEdits));
        }

        /// <summary>
        /// Gets the OPC UA namespace owned by dynamically materialized network instances.
        /// </summary>
        public ArrayOf<string> NamespacesUris => [NetworkModelNodeManager.NamespaceUri];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // No fallible work follows construction; the caller owns the returned manager.
            // TODO: teach ownership analysis about the ValueTask result transfer.
#pragma warning disable CA2000
            return new ValueTask<IAsyncNodeManager>(
                new NetworkModelNodeManager(server, configuration, m_provider, m_authorizeMappingEdits));
#pragma warning restore CA2000
        }

        /// <summary>
        /// Provider retained for each manager created by this factory.
        /// </summary>
        private readonly INetworkModelProvider m_provider;

        /// <summary>
        /// Application policy evaluated for each mapping-method invocation.
        /// </summary>
        private readonly Func<ISystemContext, bool> m_authorizeMappingEdits;
    }
}
