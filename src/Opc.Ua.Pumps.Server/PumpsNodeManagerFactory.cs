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
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Produces <see cref="PumpsNodeManager"/> instances.
    /// </summary>
    /// <remarks>
    /// Constructed by the container through <c>AddPumpsServer</c>, the factory
    /// receives the Device Integration post-setup runner and forwards it to
    /// every manager it produces, which is what makes
    /// <c>ConfigureDevicesFor&lt;PumpsNodeManager&gt;</c> and
    /// <c>ConfigurePumps</c> work.
    /// </remarks>
    public sealed class PumpsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IDiPostSetupRunner? m_runner;
        private readonly IOptions<PumpsServerOptions>? m_options;

        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public PumpsNodeManagerFactory()
            : this(null, null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the post-setup runner and options
        /// into every manager it produces.
        /// </summary>
        /// <param name="runner">The DI post-setup runner.</param>
        /// <param name="options">The manager options.</param>
        public PumpsNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<PumpsServerOptions>? options = null)
        {
            m_runner = runner;
            m_options = options;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                PumpsServerOptions options = m_options?.Value ?? new PumpsServerOptions();
                var uris = new System.Collections.Generic.List<string>
                {
                    Namespaces.Pumps,
                    Opc.Ua.Machinery.Namespaces.Machinery,
                    Opc.Ua.Di.Namespaces.OpcUaDi
                };
                if (options.LoadIndustrialAutomationModel)
                {
                    uris.Add(Opc.Ua.IA.Namespaces.IA);
                }
                foreach (string uri in options.AdditionalNamespaceUris)
                {
                    uris.Add(uri);
                }
                return uris.ToArray().ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to the server
            IAsyncNodeManager manager = new PumpsNodeManager(
                server,
                configuration,
                m_runner,
                m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }
}
