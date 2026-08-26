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
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Server;

namespace Opc.Ua.Aas.Server.V2
{
    /// <summary>
    /// Factory for the AAS V2 environment NodeManager.
    /// </summary>
    public sealed class AasV2EnvironmentNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes a factory.
        /// </summary>
        public AasV2EnvironmentNodeManagerFactory(
            AasServerOptions options,
            IAasV2EnvironmentProvider environmentProvider,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            IAasEnvironmentProjectionHost projectionHost)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_environmentProvider = environmentProvider ?? throw new ArgumentNullException(nameof(environmentProvider));
            m_valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
            m_operationHandler = operationHandler ?? throw new ArgumentNullException(nameof(operationHandler));
            m_projectionHost = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new[] { m_options.ControlNamespaceUri };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000
            IAsyncNodeManager nodeManager = new AasV2EnvironmentNodeManager(
                server,
                configuration,
                m_options,
                m_environmentProvider,
                m_valueProvider,
                m_operationHandler,
                m_projectionHost);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly AasServerOptions m_options;
        private readonly IAasV2EnvironmentProvider m_environmentProvider;
        private readonly IAasValueProvider m_valueProvider;
        private readonly IAasOperationHandler m_operationHandler;
        private readonly IAasEnvironmentProjectionHost m_projectionHost;
    }
}
