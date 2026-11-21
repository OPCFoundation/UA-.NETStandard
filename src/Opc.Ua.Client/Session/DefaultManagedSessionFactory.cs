// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default DI-backed managed-session factory.
    /// </summary>
    public sealed class DefaultManagedSessionFactory : IManagedSessionFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public DefaultManagedSessionFactory(IServiceProvider serviceProvider)
        {
            m_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <inheritdoc/>
        public Task<ManagedSession> ConnectAsync(
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default)
        {
            return ConnectAsync(endpoint, _ => { }, ct);
        }

        /// <inheritdoc/>
        public Task<ManagedSession> ConnectAsync(
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            OpcUaClientOptions options = m_serviceProvider.GetRequiredService<OpcUaClientOptions>();
            ManagedSessionOptions sessionOptions = options.Session with { Endpoint = endpoint };
            IManagedSessionConnector connector =
                m_serviceProvider.GetRequiredService<IManagedSessionConnector>();
            return connector.ConnectAsync(m_serviceProvider, sessionOptions, configure, ct);
        }

        /// <inheritdoc/>
        public Task<ManagedSession> ConnectReverseAsync(
            ReverseConnectManager manager,
            Uri serverUri,
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default)
        {
            return ConnectReverseAsync(manager, serverUri, endpoint, _ => { }, ct);
        }

        /// <inheritdoc/>
        public Task<ManagedSession> ConnectReverseAsync(
            ReverseConnectManager manager,
            Uri serverUri,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default)
        {
            if (manager is null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (serverUri is null)
            {
                throw new ArgumentNullException(nameof(serverUri));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            return ConnectAsync(
                endpoint,
                builder =>
                {
                    builder.UseReverseConnect(manager, serverUri);
                    configure(builder);
                },
                ct);
        }

        private readonly IServiceProvider m_serviceProvider;
    }
}
