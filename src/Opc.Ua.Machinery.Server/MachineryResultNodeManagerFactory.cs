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

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Creates stand-alone <see cref="MachineryResultNodeManager"/> instances.
    /// </summary>
    public sealed class MachineryResultNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory.
        /// </summary>
        /// <param name="options">The result-server options.</param>
        /// <param name="store">The store the methods read from.</param>
        public MachineryResultNodeManagerFactory(
            MachineryResultServerOptions options,
            IMachineryResultStore store)
        {
            m_options = options ?? throw new System.ArgumentNullException(nameof(options));
            m_options.Validate();
            m_store = store ?? throw new System.ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Gets the created managers, so a hosted service can push results into
        /// them once the server is running.
        /// </summary>
        public MachineryResultNodeManager? Manager { get; private set; }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            new string[]
            {
                m_options.InstanceNamespaceUri,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult
            };

        /// <inheritdoc/>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the node manager is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var nodeManager = new MachineryResultNodeManager(
                server,
                configuration,
                m_options,
                m_store);
            Manager = nodeManager;
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly MachineryResultServerOptions m_options;
        private readonly IMachineryResultStore m_store;
    }
}
