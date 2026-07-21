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
                  Opc.Ua.Pumps.Namespaces.Pumps,
                  Opc.Ua.Machinery.Namespaces.Machinery)
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

        /// <summary>
        /// Creates and registers a generated <see cref="PumpState"/>
        /// instance below the DI <c>DeviceSet</c>.
        /// </summary>
        /// <param name="pumpBrowseName">Browse name for the pump instance.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The registered generated pump state.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public ValueTask<PumpState> CreatePumpAsync(
            QualifiedName pumpBrowseName,
            CancellationToken cancellationToken = default)
        {
            return MaterialisePumpInstanceAsync(pumpBrowseName, cancellationToken);
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
            // Configuration phase 1 (async): materialise the
            // predefined instances that Configure(builder) will wire.
            // Mirrors the synchronous fluent Configure(builder) but
            // runs first so the builder has typed nodes available.
            await ConfigureInstancesAsync(cancellationToken)
                .ConfigureAwait(false);

            // Configuration phase 2 (sync): wire fluent callbacks
            // against the predefined nodes.
            ushort nsIndex = (ushort)Server.NamespaceUris.GetIndex(
                Opc.Ua.Pumps.Namespaces.Pumps);
            CreateFluentBuilder(nsIndex)
                .Configure(Configure)
                .Seal();

            m_logger.PumpAddressSpaceReady(PredefinedNodes.Count);

            // PostSetupRunner is invoked automatically by the base
            // DiNodeManager.CreateAddressSpaceAsync after this method
            // returns; no manual invocation needed here.
        }

        /// <summary>
        /// Materialises the predefined instances that the fluent
        /// <see cref="Configure"/> wiring expects to find. Runs as
        /// the async phase of <see cref="OnAddressSpaceReadyAsync"/>
        /// before the synchronous fluent builder pass.
        /// </summary>
        /// <remarks>
        /// Cannot use
        /// <see cref="DiNodeManager.CreateDeviceAsync{TDevice}(QualifiedName, NodeId, Func{NodeState, TDevice}, NodeState?, CancellationToken)"/>
        /// here because <c>PumpType</c> in OPC 40223 derives from the
        /// Machinery <c>MachineType</c>, not from the DI
        /// <c>ComponentType</c> hierarchy that
        /// <c>CreateDeviceAsync</c> requires
        /// (<c>where TDevice : ComponentState</c>). The materialisation
        /// therefore goes through
        /// <see cref="CreatePumpAsync(QualifiedName, CancellationToken)"/>
        /// which composes the same primitives
        /// (<see cref="SystemContext"/> +
        /// <see cref="CustomNodeManager2.AddPredefinedNodeAsync(ISystemContext, NodeState, CancellationToken)"/>)
        /// directly.
        /// </remarks>
        private async ValueTask ConfigureInstancesAsync(
            CancellationToken cancellationToken)
        {
            ushort pumpsNs = (ushort)Server.NamespaceUris
                .GetIndex(Opc.Ua.Pumps.Namespaces.Pumps);
            var pumpBrowseName = new QualifiedName("Pump #1", pumpsNs);
            await MaterialisePumpInstanceAsync(pumpBrowseName, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a <see cref="PumpState"/> instance with the supplied
        /// browse name as a child of the DI <c>DeviceSet</c> object and
        /// registers it as a predefined node. The instance carries
        /// <c>PumpType</c> as its TypeDefinitionId so clients see the
        /// full OPC 40223 pump surface; the source-generated factory
        /// materialises mandatory children (Identification) automatically.
        /// Optional children that the fluent simulation wires
        /// (Operational/Measurements/{analog states}, Events with the
        /// SupervisionProcessFluid + SupervisionPumpOperation subtrees,
        /// Maintenance) are materialised here via the generator-emitted
        /// <c>AddXxx(context)</c> helpers; each new node gets a
        /// per-instance NodeId via <see cref="AssignChildNodeIds"/>
        /// before <c>AddPredefinedNodeAsync</c> recursively registers
        /// the entire subtree.
        /// </summary>
        private async ValueTask<PumpState> MaterialisePumpInstanceAsync(
            QualifiedName pumpBrowseName,
            CancellationToken cancellationToken)
        {
            NodeState? deviceSet = PredefinedNodes.FindById(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris));
            if (deviceSet == null)
            {
                m_logger.DiDeviceSetNotFound(pumpBrowseName.Name);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DI DeviceSet is not available.");
            }

            if (deviceSet.FindChild(SystemContext, pumpBrowseName) != null)
            {
                m_logger.DeviceSetAlreadyContains(pumpBrowseName.Name);
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "DeviceSet already contains '{0}'.",
                    pumpBrowseName);
            }

            PumpState pump = SystemContext
                .CreateInstanceOfPumpType(deviceSet, pumpBrowseName);

            pump.NodeId = SystemContext.NodeIdFactory.New(SystemContext, pump);

            MaterialisePumpOptionalChildren(pump);

            // AddChild defaults ReferenceTypeId to HasComponent when null
            // (NodeState.AddChild line 4511-4514); ModellingRuleId defaults
            // to NodeId.Null on every fresh NodeState — no explicit set needed.
            deviceSet.AddChild(pump);

            // Walk the whole pump subtree assigning per-instance NodeIds
            // BEFORE AddPredefinedNodeAsync uses them as the PredefinedNodes
            // dictionary key. The generator's AddXxx helpers stamp the
            // TYPE NodeId on every new child; without this walk every
            // instance of PumpType would collide on those NodeIds.
            AssignChildNodeIds(pump);

            await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken)
                .ConfigureAwait(false);

            m_logger.MaterialisedPump(pumpBrowseName.Name, pump.NodeId);
            return pump;
        }

        /// <summary>
        /// Materialises the optional PumpType children that the fluent
        /// simulation in <see cref="Configure"/> wires. Each call to a
        /// generator-emitted <c>AddXxx(context)</c> helper creates the
        /// child and assigns it to the parent's typed property; the
        /// parent.AddChild bookkeeping happens inside the helpers
        /// transparently.
        /// </summary>
        private void MaterialisePumpOptionalChildren(
            PumpState pump)
        {
            pump.AddOperational(SystemContext);
            OperationalGroupState operational = pump.Operational!;
            operational.AddMeasurements(SystemContext);
            MeasurementsState measurements = operational.Measurements!;

            // Analog measurements wired by Configure.WithMeasurements.
            measurements
                .AddDifferentialPressure(SystemContext)
                .AddFluidTemperature(SystemContext)
                .AddBearingTemperature(SystemContext)
                .AddPumpPowerInput(SystemContext)
                .AddMassFlow(SystemContext)
                .AddPumpEfficiency(SystemContext)
                .AddLevel(SystemContext)
                // Discrete count exposed via Configure.WithMaintenance.
                .AddNumberOfStarts(SystemContext);

            // Supervision subtree wired by Configure.WithSupervision —
            // Cavitation under SupervisionProcessFluid, MotorOverheat
            // under SupervisionPumpOperation.
            pump.AddEvents(SystemContext);
            SupervisionState events = pump.Events!;
            events.AddSupervisionProcessFluid(SystemContext);
            events.SupervisionProcessFluid!.AddCavitation(SystemContext);

            events.AddSupervisionPumpOperation(SystemContext);
            events.SupervisionPumpOperation!.AddMotorOverheat(SystemContext);

            // Maintenance container — leaf wiring deferred until the
            // typed-accessor generator (FB-3 phase 3) ships materialisable
            // leaves for ConditionBasedMaintenance / BreakdownMaintenance.
            pump.AddMaintenance(SystemContext);
        }

        /// <summary>
        /// Recursively walks the children of <paramref name="parent"/>
        /// and assigns per-instance NodeIds via the active
        /// <see cref="ISystemContext.NodeIdFactory"/>. Required after
        /// calling generator-emitted <c>AddXxx(context)</c> helpers
        /// which stamp the TYPE NodeId on every new child.
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

        /// <summary>
        /// Partial wired by the Configure.cs sibling.
        /// </summary>
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
            Opc.Ua.Pumps.Namespaces.Pumps,
            Opc.Ua.Machinery.Namespaces.Machinery,
            Opc.Ua.Di.Namespaces.OpcUaDi
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

    internal static partial class PumpNodeManagerLog
    {
        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 1,
            Level = LogLevel.Information,
            Message = "PumpNodeManager: address space ready ({NodeCount} predefined nodes).")]
        public static partial void PumpAddressSpaceReady(this ILogger logger, int nodeCount);

        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 2, Level = LogLevel.Warning,
            Message = "DI DeviceSet not found — '{Name}' will not be created.")]
        public static partial void DiDeviceSetNotFound(this ILogger logger, string? name);

        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 3, Level = LogLevel.Debug,
            Message = "DeviceSet already contains '{Name}' — skipping recreation.")]
        public static partial void DeviceSetAlreadyContains(this ILogger logger, string? name);

        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.PumpNodeManager + 4,
            Level = LogLevel.Information,
            Message = "Materialised '{Name}' (PumpType) under DeviceSet, NodeId={NodeId}.")]
        public static partial void MaterialisedPump(this ILogger logger, string? name, NodeId nodeId);
    }
}
