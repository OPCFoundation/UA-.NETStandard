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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Opt-in address-space factory over an authoritative endpoint. Register this
    /// factory through the existing server DI or NodeManagerLifecycle infrastructure.
    /// </summary>
    public sealed class XRegistryBridgeNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates an opt-in native projection factory borrowing the authoritative endpoint.
        /// </summary>
        public XRegistryBridgeNodeManagerFactory(
            IXRegistryEndpoint endpoint, XRegistryBridgeNativeOptions options)
        {
            endpoint.ThrowIfNull(nameof(endpoint));
            options.ThrowIfNull(nameof(options));
            options.Validate();
            m_endpoint = endpoint;
            m_options = options;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
        [
            m_options.NamespaceUri,
            XRegistryWellKnown.XRegistryNamespaceUri,
            XRegistryBridgeNativeOptions.ExperimentalNamespaceUri
        ];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server, ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            server.ThrowIfNull(nameof(server));
            configuration.ThrowIfNull(nameof(configuration));
            cancellationToken.ThrowIfCancellationRequested();
            // Ownership passes to the server lifecycle, not this factory.
            // TODO: Remove when CA2000 understands ValueTask ownership transfer.
#pragma warning disable CA2000
            return new ValueTask<IAsyncNodeManager>(
                new XRegistryBridgeNodeManager(server, configuration, m_endpoint, m_options));
#pragma warning restore CA2000
        }

        private readonly IXRegistryEndpoint m_endpoint;
        private readonly XRegistryBridgeNativeOptions m_options;
    }
}
