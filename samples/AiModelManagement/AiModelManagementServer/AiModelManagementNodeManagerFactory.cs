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
using AiModelManagement.Bridge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server;

namespace AiModelManagement.Server
{
    /// <summary>
    /// Produces the AI node manager for the hosting pipeline.
    /// </summary>
    /// <remarks>
    /// The factory is what the DI container resolves, so it is where the backends,
    /// options and logging arrive. The node manager itself is constructed per Server
    /// and owned by it.
    /// </remarks>
    public sealed class AiModelManagementNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly InferenceBackends m_backends;
        private readonly IOptions<AiModelManagementOptions>? m_options;
        private readonly IOptions<InferenceBackendOptions>? m_backendOptions;
        private readonly ILogger<AiModelManagementNodeManager>? m_logger;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        public AiModelManagementNodeManagerFactory(
            InferenceBackends backends,
            IOptions<AiModelManagementOptions>? options = null,
            IOptions<InferenceBackendOptions>? backendOptions = null,
            ILogger<AiModelManagementNodeManager>? logger = null)
        {
            m_backends = backends;
            m_options = options;
            m_backendOptions = backendOptions;
            m_logger = logger;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Opc.Ua.AiModelManagement.Namespaces.AI,
            Opc.Ua.AiModelManagement.Namespaces.xRegistry
        };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to the server
            IAsyncNodeManager manager = new AiModelManagementNodeManager(
                server,
                configuration,
                m_backends,
                m_options,
                m_backendOptions,
                m_logger);
#pragma warning restore CA2000

            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }
}
