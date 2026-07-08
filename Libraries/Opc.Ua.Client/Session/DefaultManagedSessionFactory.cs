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
