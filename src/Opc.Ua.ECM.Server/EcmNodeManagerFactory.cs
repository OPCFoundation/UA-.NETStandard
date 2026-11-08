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
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Creates ECM node managers for server hosting.
    /// </summary>
    public sealed class EcmNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory with optional DI post-setup and companion providers.
        /// </summary>
        public EcmNodeManagerFactory(
            IDiPostSetupRunner? runner = null,
            ArrayOf<IDiModelProvider> providers = default)
        {
            m_runner = runner;
            m_providers = providers;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                var uris = new List<string> { Namespaces.ECM, IA.Namespaces.IA };
                ArrayOf<string> contributed = new DiNodeManagerFactory(m_runner, m_providers).NamespacesUris;
                for (int ii = 0; ii < contributed.Count; ii++)
                {
                    if (!uris.Contains(contributed[ii]))
                    {
                        uris.Add(contributed[ii]);
                    }
                }
                return uris.ToArray();
            }
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EcmNodeManager? manager = null;
            ValueTask<IAsyncNodeManager> result;
            try
            {
                manager = new EcmNodeManager(server, configuration, m_runner, m_providers);
                result = new ValueTask<IAsyncNodeManager>(manager);
                manager = null;
            }
            finally
            {
                manager?.Dispose();
            }
            return result;
        }

        /// <summary>The post-setup runner.</summary>
        private readonly IDiPostSetupRunner? m_runner;

        /// <summary>The additional companion providers.</summary>
        private readonly ArrayOf<IDiModelProvider> m_providers;
    }
}
