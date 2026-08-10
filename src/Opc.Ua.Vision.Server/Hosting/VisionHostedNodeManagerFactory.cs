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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Vision.Server.Hosting
{
    internal sealed class VisionHostedNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public VisionHostedNodeManagerFactory(
            ArrayOf<IVisionModelProvider> providers,
            VisionServerOptions options,
            IVisionPostSetupRunner? runner,
            IServiceProvider services)
        {
            m_providers = VisionNodeManager.NormalizeProviders(providers);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_runner = runner;
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public ArrayOf<string> NamespacesUris =>
            VisionNodeManager.GetNamespaceUris(m_providers, m_options).ToArrayOf();

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager manager = new VisionNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_runner,
                m_services);
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly ArrayOf<IVisionModelProvider> m_providers;
        private readonly VisionServerOptions m_options;
        private readonly IVisionPostSetupRunner? m_runner;
        private readonly IServiceProvider m_services;
    }
}
