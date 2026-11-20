/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Loads the client <see cref="ApplicationConfiguration"/> eagerly
    /// during host start when
    /// <see cref="OpcUaClientOptions.LoadConfigurationOnStart"/> is set,
    /// the client-side twin of
    /// <see cref="ReverseConnectManagerHostedService"/>. Awaiting
    /// <c>GetAsync</c> here guarantees that when <c>host.StartAsync()</c>
    /// returns, the configuration document is loaded and validated, the
    /// application-instance certificate is ensured, and
    /// <see cref="OpcUaClientOptions.Configuration"/> is readable - which
    /// user-interface hosts need before any session exists. A load failure
    /// fails the host start instead of surfacing on the first connect.
    /// </summary>
    internal sealed class ClientConfigurationLoaderHostedService : IHostedService
    {
        /// <summary>
        /// Initializes the hosted service with the resolved client options.
        /// </summary>
        /// <param name="options">The resolved client options carrying the
        /// configuration provider.</param>
        public ClientConfigurationLoaderHostedService(OpcUaClientOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (m_options.ConfigurationProvider is not { } provider)
            {
                // An explicit Configuration was supplied; there is nothing
                // left to load.
                return;
            }

            // Completes validation and application-instance certificate
            // setup; a lazily loaded supplied configuration document
            // (ConfigurationFile / ConfigurationStream) also becomes
            // available here, so publish it on the resolved options exactly
            // as the session connect path does.
            ApplicationConfiguration configuration = await provider
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
            m_options.Configuration ??= configuration;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private readonly OpcUaClientOptions m_options;
    }
}
