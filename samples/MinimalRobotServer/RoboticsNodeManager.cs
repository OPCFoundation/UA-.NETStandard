/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.OpenUsd;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Robotics
{
    /// <summary>
    /// Hand-written node manager that provides the infrastructure (constructor,
    /// address-space load, fluent builder wiring) for the OPC 40010 Robotics
    /// companion specification server, then materialises a robot cell bound to the
    /// draft OPC UA — OpenUSD Bindings companion model (see OpenUsdRepresentation.cs
    /// and RobotCell.cs).
    /// </summary>
    public partial class RoboticsNodeManager : DiNodeManager
    {
        internal const string RoboticsNamespaceUri = "http://opcfoundation.org/UA/Robotics/";
        internal const string IaNamespaceUri = "http://opcfoundation.org/UA/IA/";

        /// <summary>
        /// Initialises a new <see cref="RoboticsNodeManager"/> without DI-hosting
        /// integration.
        /// </summary>
        public RoboticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="RoboticsNodeManager"/> that participates in
        /// the DI hosting post-setup pipeline.
        /// </summary>
        public RoboticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  RoboticsNamespaceUri,
                  IaNamespaceUri,
                  Opc.Ua.OpenUsd.Namespaces.OpenUSD)
        {
            // Base class constructor sets SystemContext.NodeIdFactory to itself; our
            // New() override takes over.
            SystemContext.NodeIdFactory = this;
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;
                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // Compose the predefined-node tree:
            //   - Opc.Ua.Di + Opc.Ua.IA + Opc.Ua.Robotics: the OPC 40010 Robotics
            //     type system, loaded via Opc.Ua.Robotics.Server.AddRoboticsTypeSystem
            //     (the DI, IA and Robotics source-generated loaders, in dependency
            //     order).
            //   - Opc.Ua.OpenUsd: the source-generated OpenUSD companion loader
            //     (from the referenced Opc.Ua.OpenUsd library).
            // This server builds instances from BaseObjectState + the numeric type
            // NodeIds in RoboticsModel, so it does not depend on the generated
            // Robotics/IA typed classes (the loaders populate the address space).
            var nodes = new NodeStateCollection();
            nodes.AddRoboticsTypeSystem(context);
            nodes.AddOpcUaOpenUsd(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }


        /// <inheritdoc/>
        protected override async ValueTask OnAddressSpaceReadyAsync(
            CancellationToken cancellationToken)
        {
            // Configuration phase 1 (async): materialise the predefined instances that
            // Configure(builder) will wire.
            await ConfigureInstancesAsync(cancellationToken)
                .ConfigureAwait(false);

            // Configuration phase 2 (sync): wire fluent callbacks (the simulation tick
            // that animates the axes and toggles the emergency-stop).
            ushort nsIndex = (ushort)Server.NamespaceUris.GetIndex(RoboticsNamespaceUri);
            CreateFluentBuilder(nsIndex)
                .Configure(Configure)
                .Seal();

            m_logger.RoboticsAddressSpaceReady(PredefinedNodes.Count);
        }

        /// <summary>
        /// Materialises the OpenUSD facility and the robot cell (system + robots +
        /// axes + representations + composition) that the fluent <see cref="Configure"/>
        /// wiring animates.
        /// </summary>
        private async ValueTask ConfigureInstancesAsync(
            CancellationToken cancellationToken)
        {
            // OpenUSD facility first so representations can reference the stage.
            await MaterialiseOpenUsdFacilityAsync(cancellationToken)
                .ConfigureAwait(false);

            // The robot cell: a MotionDeviceSystem composed of two 6-axis robots,
            // each composed of 6 axes, plus safety/command signals and a dynamically
            // attached gripper tool. See RobotCell.cs.
            await MaterialiseRobotCellAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Recursively walks the children of <paramref name="parent"/> and assigns
        /// per-instance NodeIds via the active <see cref="ISystemContext.NodeIdFactory"/>.
        /// Required after calling generator-emitted <c>CreateInstanceOf…</c> factories
        /// or <c>AddXxx(context)</c> helpers which stamp the TYPE NodeId on every new
        /// child.
        /// </summary>
        private void AssignChildNodeIds(NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = SystemContext.NodeIdFactory.New(
                    SystemContext, child);
                AssignChildNodeIds(child);
            }
        }

        private List<BaseInstanceState> EnumerateChildren(NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            return children;
        }

        /// <summary>
        /// Partial wired by the Configure.cs sibling.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder);
    }

    /// <summary>
    /// Factory that produces <see cref="RoboticsNodeManager"/> instances. When
    /// constructed by the DI container via
    /// <c>AddNodeManager&lt;RoboticsNodeManagerFactory&gt;()</c>, the post-setup
    /// runner is injected and forwarded to every manager the factory produces.
    /// </summary>
    public sealed class RoboticsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IDiPostSetupRunner? m_runner;

        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public RoboticsNodeManagerFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the post-setup runner into every manager
        /// it produces.
        /// </summary>
        public RoboticsNodeManagerFactory(IDiPostSetupRunner? runner)
        {
            m_runner = runner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            RoboticsNodeManager.RoboticsNamespaceUri,
            RoboticsNodeManager.IaNamespaceUri,
            Opc.Ua.Di.Namespaces.OpcUaDi,
            Opc.Ua.OpenUsd.Namespaces.OpenUSD
        };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to server
            IAsyncNodeManager nm = new RoboticsNodeManager(server, configuration, m_runner);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nm);
        }
    }

    internal static partial class RoboticsNodeManagerLog
    {
        [LoggerMessage(EventId = MinimalRobotServerEventIds.RoboticsNodeManager + 1,
            Level = LogLevel.Information,
            Message = "RoboticsNodeManager: address space ready ({NodeCount} predefined nodes).")]
        public static partial void RoboticsAddressSpaceReady(this ILogger logger, int nodeCount);
    }
}
