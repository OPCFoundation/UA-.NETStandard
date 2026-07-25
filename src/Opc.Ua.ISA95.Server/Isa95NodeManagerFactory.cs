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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.Server;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Creates ISA-95 node managers for the hosted server.
    /// </summary>
    public sealed class Isa95NodeManagerFactory : IAsyncNodeManagerFactory
    {
        public Isa95NodeManagerFactory(
            Isa95ServerOptions options,
            Isa95ServerProviders providers)
            : this(options, providers, [])
        {
        }

        public Isa95NodeManagerFactory(
            Isa95ServerOptions options,
            Isa95ServerProviders providers,
            IEnumerable<IIsa95ModelConfigurator> configurators)
        {
            m_options = options;
            m_providers = providers;
            m_configurators = [.. configurators];
        }

        public ArrayOf<string> NamespacesUris =>
        [
            m_options.InstanceNamespaceUri,
            Namespaces.ISA95,
            JobControl.V1.Namespaces.ISA95JobControlV1,
            JobControl.V2.Namespaces.ISA95JobControlV2
        ];

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the node manager is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager manager =
                new Isa95NodeManager(
                    server,
                    configuration,
                    m_options,
                    m_providers,
                    m_configurators);
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly Isa95ServerOptions m_options;
        private readonly Isa95ServerProviders m_providers;
        private readonly IIsa95ModelConfigurator[] m_configurators;
    }
}
