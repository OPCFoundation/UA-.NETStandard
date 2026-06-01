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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;

namespace Pumps
{
    /// <summary>
    /// Hand-written node manager partial that provides the infrastructure
    /// (constructor, address-space load, fluent builder wiring) for the
    /// OPC 40223 Pumps companion specification server.
    /// </summary>
    public partial class PumpNodeManager : DiNodeManager
    {
        /// <summary>
        /// Initialises a new <see cref="PumpNodeManager"/> without
        /// DI-hosting integration.
        /// </summary>
        public PumpNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="PumpNodeManager"/> that
        /// participates in the DI hosting post-setup pipeline.
        /// </summary>
        public PumpNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  global::Opc.Ua.Pumps.Namespaces.Pumps,
                  global::Opc.Ua.Machinery.Namespaces.Machinery)
        {
            // Base class constructor sets SystemContext.NodeIdFactory to
            // itself; our New() override takes over.
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
            // Compose the predefined-node tree from three source-generated
            // models, in dependency order:
            //  - Opc.Ua.Di     (referenced library)
            //  - Opc.Ua.Machinery (source-generated inside this assembly)
            //  - Opc.Ua.Pumps     (source-generated inside this assembly)
            // No runtime XML loading — the NodeSet2 XMLs ship only as
            // <AdditionalFiles> for the source generator. The generated
            // AddOpcUa* extension methods are idempotent and pull in
            // their declared dependencies via [ModelDependencyAttribute],
            // so a direct chain in dependency order is sufficient.
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaPumps(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override async ValueTask OnAddressSpaceReadyAsync(
            CancellationToken cancellationToken)
        {
            // Materialise the single `Pump #1` instance the fluent
            // wiring in PumpNodeManager.Configure.cs expects to find.
            // The instance is a typed `PumpState` (OPC 40223 PumpType)
            // attached as a child of the DI `DeviceSet` root so it is
            // browseable at `Objects > DeviceSet > Pump #1` — alongside
            // any additional pumps declared declaratively via
            // ConfigureDevicesFor in Program.cs.
            await CreatePumpInstanceAsync("Pump #1", cancellationToken)
                .ConfigureAwait(false);

            // Build the fluent wiring surface and invoke Configure.
            ushort nsIndex = (ushort)Server.NamespaceUris.GetIndex(
                global::Opc.Ua.Pumps.Namespaces.Pumps);
            NodeManagerBuilder builder = new NodeManagerBuilder(
                SystemContext,
                this,
                nsIndex,
                browseName => PredefinedNodes.Values.FindByBrowseName(browseName)!,
                nodeId => PredefinedNodes.FindById(nodeId)!,
                typeDefId => PredefinedNodes.Values.FindByTypeDefinition(typeDefId));

            // Attach FluentNodeManagerBase registries (event sources +
            // simulation loops) so the fluent .Publish() and .Simulation()
            // extensions can find them.
            AttachToBuilder(builder);

            Configure(builder);
            builder.Seal();

            m_logger.LogInformation(
                "PumpNodeManager: address space ready ({NodeCount} predefined nodes).",
                PredefinedNodes.Count);

            // PostSetupRunner is invoked automatically by the base
            // DiNodeManager.CreateAddressSpaceAsync after this method
            // returns; no manual invocation needed here.
        }

        /// <summary>
        /// Creates a <see cref="PumpState"/> instance with the supplied
        /// browse name as a child of the DI <c>DeviceSet</c> object and
        /// registers it as a predefined node. The instance carries
        /// <c>PumpType</c> as its TypeDefinitionId so clients see the
        /// full OPC 40223 pump surface; the source-generated factory
        /// materialises mandatory children (Identification) automatically
        /// and optional children (Operational, Maintenance, Events, …)
        /// are populated lazily via the standard <c>NodeState.Initialize</c>
        /// pipeline as soon as the fluent builder queries them.
        /// </summary>
        private async ValueTask CreatePumpInstanceAsync(
            string browseNameText,
            CancellationToken cancellationToken)
        {
            ushort pumpsNs = (ushort)Server.NamespaceUris
                .GetIndex(global::Opc.Ua.Pumps.Namespaces.Pumps);
            var pumpBrowseName = new QualifiedName(browseNameText, pumpsNs);

            NodeState? deviceSet = PredefinedNodes.FindById(NodeId.Create(
                global::Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris));
            if (deviceSet == null)
            {
                m_logger.LogWarning(
                    "DI DeviceSet not found — '{Name}' will not be created.",
                    browseNameText);
                return;
            }

            // Fail-fast on duplicate.
            if (deviceSet.FindChild(SystemContext, pumpBrowseName) != null)
            {
                m_logger.LogDebug(
                    "DeviceSet already contains '{Name}' — skipping recreation.",
                    browseNameText);
                return;
            }

            global::Opc.Ua.Pumps.PumpState pump = SystemContext
                .CreateInstanceOfPumpType(deviceSet, pumpBrowseName);

            pump.NodeId = SystemContext.NodeIdFactory.New(SystemContext, pump);
            // AddChild defaults ReferenceTypeId to HasComponent when null
            // (NodeState.AddChild line 4511-4514); ModellingRuleId defaults
            // to NodeId.Null on every fresh NodeState — no explicit set needed.
            deviceSet.AddChild(pump);

            await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken)
                .ConfigureAwait(false);

            m_pump1 = pump;

            m_logger.LogInformation(
                "Materialised '{Name}' (PumpType) under DeviceSet, NodeId={NodeId}.",
                browseNameText, pump.NodeId);
        }

        /// <summary>
        /// Registers a DI <c>DeviceHealth</c> variable that the
        /// supervision simulation loop should toggle in response to
        /// the simulated cavitation / motor-overheat flags. The
        /// companion-spec PumpType does not itself expose
        /// <c>DeviceHealth</c> (it inherits from
        /// <see cref="global::Opc.Ua.Di.TopologyElementState"/>, not
        /// <see cref="global::Opc.Ua.Di.DeviceState"/>); callers can
        /// attach <c>DeviceHealth</c> to a sibling
        /// <see cref="global::Opc.Ua.Di.DeviceState"/> (e.g. the
        /// declarative <c>Pump #2</c> created in <c>Program.cs</c>)
        /// and register it here to participate in the simulation loop.
        /// </summary>
        /// <param name="health">
        /// The variable to drive; pass <see langword="null"/> to detach.
        /// </param>
        public void RegisterSupervisedDeviceHealth(
            BaseDataVariableState<global::Opc.Ua.Di.DeviceHealthEnumeration>? health)
        {
            m_supervisedDeviceHealth = health;
        }

        /// <summary>Partial wired by the Configure.cs sibling.</summary>
        partial void Configure(INodeManagerBuilder builder);
    }

    /// <summary>
    /// Factory that produces <see cref="PumpNodeManager"/> instances.
    /// When constructed by the DI container via
    /// <c>AddNodeManager&lt;PumpNodeManagerFactory&gt;()</c>, the
    /// post-setup runner is injected and forwarded to every manager
    /// the factory produces, enabling
    /// <c>ConfigureDevicesFor&lt;PumpNodeManager&gt;(...)</c>.
    /// </summary>
    public sealed class PumpNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IDiPostSetupRunner? m_runner;

        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public PumpNodeManagerFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the post-setup runner into
        /// every manager it produces.
        /// </summary>
        public PumpNodeManagerFactory(IDiPostSetupRunner? runner)
        {
            m_runner = runner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            global::Opc.Ua.Pumps.Namespaces.Pumps,
            global::Opc.Ua.Machinery.Namespaces.Machinery,
            global::Opc.Ua.Di.Namespaces.OpcUaDi
        };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to server
            IAsyncNodeManager nm = new PumpNodeManager(server, configuration, m_runner);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nm);
        }
    }
}
