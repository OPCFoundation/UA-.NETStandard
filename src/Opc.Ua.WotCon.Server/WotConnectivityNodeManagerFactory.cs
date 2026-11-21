// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// <see cref="IAsyncNodeManagerFactory"/> that produces
    /// <see cref="WotConnectivityNodeManager"/> instances configured with a
    /// shared <see cref="WotConnectivityServerOptions"/>.
    /// </summary>
    public sealed class WotConnectivityNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a new factory using the supplied options.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
        public WotConnectivityNodeManagerFactory(WotConnectivityServerOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            m_options.AssetNamespaceUri,
            Namespaces.WotCon
        };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The node manager is owned by the MasterNodeManager once registered.
#pragma warning disable CA2000 // Dispose objects before losing scope
            return new ValueTask<IAsyncNodeManager>(
                new WotConnectivityNodeManager(server, configuration, m_options));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        private readonly WotConnectivityServerOptions m_options;
    }
}
