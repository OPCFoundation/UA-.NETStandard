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
 *
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
        public async ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NetworkModelNodeManager? manager = null;
            try
            {
                manager = new NetworkModelNodeManager(server, configuration, m_provider, m_authorizeMappingEdits);
                IAsyncNodeManager result = manager;
                manager = null;
                return result;
            }
            finally
            {
                if (manager is not null)
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
            }
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
