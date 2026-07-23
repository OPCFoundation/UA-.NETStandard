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
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Creates stock <see cref="RoboticsNodeManager"/> instances.
    /// </summary>
    public sealed class RoboticsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly RoboticsServerOptions m_options;
        private readonly ArrayOf<IRoboticsModelProvider> m_providers;
        private readonly IDiPostSetupRunner? m_postSetupRunner;

        /// <summary>
        /// Creates a factory with default options and the built-in provider.
        /// </summary>
        public RoboticsNodeManagerFactory()
            : this(
                new IRoboticsModelProvider[] { new RoboticsModelProvider() },
                new RoboticsServerOptions())
        {
        }

        /// <summary>
        /// Creates a factory with explicitly supplied providers and options.
        /// </summary>
        public RoboticsNodeManagerFactory(
            ArrayOf<IRoboticsModelProvider> providers,
            RoboticsServerOptions options,
            IDiPostSetupRunner? postSetupRunner = null)
        {
            m_options = RoboticsModelProviderUtilities.ValidateOptions(options);
            m_providers = RoboticsModelProviderUtilities.Normalize(providers);
            m_postSetupRunner = postSetupRunner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            RoboticsModelProviderUtilities.GetFactoryNamespaceUris(m_providers, m_options);

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
            IAsyncNodeManager nodeManager = new RoboticsNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_postSetupRunner);
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }
    }
}
