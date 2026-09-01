/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
