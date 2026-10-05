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
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Produces the <see cref="AmbNodeManager"/> of a server.
    /// </summary>
    /// <remarks>
    /// Constructed by the container through <c>AddAssetManagement</c>, the
    /// factory hands every manager the shared <see cref="AssetManagement"/>
    /// and the delegates registered with <c>ConfigureAssetManagement</c>.
    /// </remarks>
    public sealed class AmbNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="assetManagement">The registry the manager serves.</param>
        /// <param name="services">
        /// The service provider the <c>ConfigureAssetManagement</c> delegates
        /// resolve their services from; <see langword="null"/> without hosting.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="assetManagement"/> is <c>null</c>.</exception>
        public AmbNodeManagerFactory(AssetManagement assetManagement, IServiceProvider? services = null)
        {
            m_assetManagement = assetManagement ?? throw new ArgumentNullException(nameof(assetManagement));
            m_services = services;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => m_assetManagement.Options.GetNamespaceUris().ToArrayOf();

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            AmbSetupRunner? runner = m_services == null
                ? null
                : new AmbSetupRunner(m_services, m_services.GetServices<AmbSetupConfigurator>());
#pragma warning disable CA2000 // ownership transferred to the server
            IAsyncNodeManager manager = new AmbNodeManager(server, configuration, m_assetManagement, runner);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly AssetManagement m_assetManagement;
        private readonly IServiceProvider? m_services;
    }
}
