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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Stock DI-based node manager for compiled Robotics models and
    /// application-owned instances.
    /// </summary>
    public sealed class RoboticsNodeManager : DiNodeManager, IRoboticsNodeIdFactory
    {
        private readonly RoboticsServerOptions m_options;
        private readonly ArrayOf<IRoboticsModelProvider> m_providers;
        private readonly RoboticsBuildCoordinator m_buildCoordinator;
        private bool m_addressSpaceReady;

        /// <summary>
        /// Creates a stock manager with default options and the built-in provider.
        /// </summary>
        public RoboticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(
                server,
                configuration,
                new IRoboticsModelProvider[] { new RoboticsModelProvider() },
                new RoboticsServerOptions())
        {
        }

        /// <summary>
        /// Creates a stock manager with explicitly supplied providers and options.
        /// </summary>
        public RoboticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ArrayOf<IRoboticsModelProvider> providers,
            RoboticsServerOptions options,
            IDiPostSetupRunner? postSetupRunner = null)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  RoboticsModelProviderUtilities.GetManagerNamespaceUris(providers, options))
        {
            m_providers = RoboticsModelProviderUtilities.Normalize(providers);
            m_options = RoboticsModelProviderUtilities.ValidateOptions(options, m_providers);
            m_buildCoordinator = RoboticsBuildCoordinator.Get(this);
        }

        /// <summary>
        /// Gets the application-owned instance namespace index that Robotics
        /// instances are created in.
        /// </summary>
        /// <remarks>
        /// This is not the same value as
        /// <see cref="DiNodeManager.InstanceNamespaceIndex"/>, which the base
        /// derives from the server configuration. Robotics lets the
        /// application pick its own instance namespace through
        /// <see cref="RoboticsServerOptions.InstanceNamespaceUri"/>, and
        /// requires it to be registered.
        /// </remarks>
        public ushort RoboticsInstanceNamespaceIndex
        {
            get
            {
                int namespaceIndex = Server.NamespaceUris.GetIndex(
                    m_options.InstanceNamespaceUri);
                if (namespaceIndex < 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The Robotics instance namespace '{0}' is not registered.",
                        m_options.InstanceNamespaceUri);
                }
                return (ushort)namespaceIndex;
            }
        }

        internal int ReservedNodeIdCount =>
            m_buildCoordinator.GetReservedNodeIdCount(RoboticsInstanceNamespaceIndex);

        /// <summary>
        /// Creates a build context for direct, non-hosted configuration.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The address space or required models are not available.
        /// </exception>
        public IRoboticsBuildContext CreateRoboticsBuildContext(
            CancellationToken cancellationToken = default)
        {
            if (!m_addressSpaceReady)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Robotics address space is not available yet.");
            }
            return new RoboticsBuildContext(this, m_options, cancellationToken);
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                return m_buildCoordinator.ReserveNodeId(
                    this,
                    RoboticsInstanceNamespaceIndex,
                    node);
            }
            return node.NodeId;
        }

        /// <inheritdoc/>
        protected override async ValueTask AddPredefinedNodeAsync(
            ISystemContext context,
            NodeState node,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await base.AddPredefinedNodeAsync(context, node, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (!node.NodeId.IsNull &&
                    ReferenceEquals(FindPredefinedNode(node.NodeId), node))
                {
                    m_buildCoordinator.ReleaseNodeId(node.NodeId, node);
                }
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            for (int ii = 0; ii < m_providers.Count; ii++)
            {
                m_providers[ii].AddPredefinedNodes(nodes, context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask OnAddressSpaceReadyAsync(
            CancellationToken cancellationToken)
        {
            m_addressSpaceReady = true;
            return default;
        }
    }
}
