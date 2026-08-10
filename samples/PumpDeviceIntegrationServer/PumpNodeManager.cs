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
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery;
using Opc.Ua.OpenUsd;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;

namespace Pumps
{
    /// <summary>
    /// Runtime options for the pump device-integration sample.
    /// </summary>
    public sealed class PumpDeviceIntegrationOptions
    {
        /// <summary>
        /// Gets or sets how many simulated pump instances are materialised.
        /// </summary>
        public int PumpCount { get; set; } = 2;

        /// <summary>
        /// Gets or sets the interval used by the live simulation loop.
        /// </summary>
        public TimeSpan SimulationInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    }

    /// <summary>
    /// Hand-written node manager partial that provides the infrastructure
    /// (constructor, address-space load, fluent builder wiring) for the
    /// OPC 40223 Pumps companion specification server.
    /// </summary>
    public partial class PumpNodeManager : DiNodeManager
    {
        private readonly PumpDeviceIntegrationOptions m_options;
        private readonly List<PumpState> m_pumpStates = [];

        /// <summary>
        /// Initialises a new <see cref="PumpNodeManager"/> without
        /// DI-hosting integration.
        /// </summary>
        public PumpNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="PumpNodeManager"/> that
        /// participates in the DI hosting post-setup pipeline.
        /// </summary>
        public PumpNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<PumpDeviceIntegrationOptions>? options = null)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  Opc.Ua.Pumps.Namespaces.Pumps,
                  Opc.Ua.Machinery.Namespaces.Machinery,
                  Opc.Ua.OpenUsd.Namespaces.OpenUSD)
        {
            // Base class constructor sets SystemContext.NodeIdFactory to
            // itself; our New() override takes over.
            SystemContext.NodeIdFactory = this;
            m_options = options?.Value ?? new PumpDeviceIntegrationOptions();
            if (m_options.PumpCount < 1 || m_options.PumpCount > 100)
            {
                throw new ArgumentOutOfRangeException(
                    $"{nameof(options)}.{nameof(PumpDeviceIntegrationOptions.PumpCount)}",
                    m_options.PumpCount,
                    "Pump count must be between 1 and 100.");
            }
            if (m_options.SimulationInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    $"{nameof(options)}.{nameof(PumpDeviceIntegrationOptions.SimulationInterval)}",
                    m_options.SimulationInterval,
                    "Simulation interval must be positive.");
            }
        }

        /// <summary>
        /// Gets the registered pump instance NodeIds.
        /// </summary>
        public ArrayOf<NodeId> PumpNodeIds
        {
            get
            {
                var nodeIds = new List<NodeId>(m_pumpStates.Count);
                foreach (PumpState pump in m_pumpStates)
                {
                    nodeIds.Add(pump.NodeId);
                }
                return nodeIds.ToArrayOf();
            }
        }

        internal TimeSpan SimulationInterval => m_options.SimulationInterval;

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;
                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    InstanceNamespaceIndex);
            }

            return node.NodeId;
        }

        /// <summary>
        /// Creates and registers a generated <see cref="PumpState"/>
        /// instance organized by the DI <c>DeviceSet</c>.
        /// </summary>
        /// <param name="pumpBrowseName">Browse name for the pump instance.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The registered generated pump state.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public ValueTask<PumpState> CreatePumpAsync(
            QualifiedName pumpBrowseName,
            CancellationToken cancellationToken = default)
        {
            return MaterialisePumpInstanceAsync(
                pumpBrowseName,
                cancellationToken,
                RegisterPumpSimulation);
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
            nodes.AddOpcUaOpenUsd(context);
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
            CreateFluentBuilder(InstanceNamespaceIndex)
                .Configure(Configure)
                .Seal();
            PreservePumpHistoryReadAccessLevels();

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
            // OpenUSD facility first so the pump representation can reference the stage.
            await MaterialiseOpenUsdFacilityAsync(cancellationToken)
                .ConfigureAwait(false);

            PumpState? firstPump = null;
            for (int pumpNumber = 1; pumpNumber <= m_options.PumpCount; pumpNumber++)
            {
                var pumpBrowseName = new QualifiedName(
                    GetPumpBrowseName(pumpNumber),
                    InstanceNamespaceIndex);
                PumpState pump = await MaterialisePumpInstanceAsync(
                    pumpBrowseName,
                    cancellationToken).ConfigureAwait(false);
                firstPump ??= pump;
            }

            // Every pump is a twin in its own right, so every representation has to
            // be discoverable — a connector finds them through this registry alone.
            OrganiseRepresentations();

            // Plant-level aggregation: composes one full-fidelity pump prim per
            // configured pump, so the rendered scene scales with --pumps N.
            await MaterialisePlantAggregationAsync(cancellationToken).ConfigureAwait(false);

            // Composition demo: a ProductionLine aggregating 1..n pumps (Many), with a
            // dynamically added/removed pump (model-change events) and a cross-server
            // component (federation). See OpenUsdComposition.cs.
            await MaterialiseProductionLineAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a <see cref="PumpState"/> instance with the supplied
        /// browse name, registers it as a predefined node, and links it
        /// from the DI <c>DeviceSet</c> with <c>Organizes</c>. The instance carries
        /// <c>PumpType</c> as its TypeDefinitionId so clients see the
        /// full OPC 40223 pump surface; the source-generated factory
        /// materialises mandatory children (Identification) automatically
        /// and - because a browse name is supplied - rebases the whole
        /// subtree onto per-instance NodeIds minted by <see cref="New"/>.
        /// Optional children that the fluent simulation wires
        /// (Operational/Measurements/{analog states}, Events with the
        /// SupervisionProcessFluid + SupervisionPumpOperation subtrees,
        /// Maintenance) are materialised here via the generator-emitted
        /// <c>AddXxx(context)</c> helpers, which assign per-instance
        /// NodeIds to every node they add before
        /// <c>AddPredefinedNodeAsync</c> recursively registers the
        /// entire subtree.
        /// </summary>
        private async ValueTask<PumpState> MaterialisePumpInstanceAsync(
            QualifiedName pumpBrowseName,
            CancellationToken cancellationToken,
            Action<PumpState>? onRegistered = null)
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

            var pumpNodeId = new NodeId(
                $"{deviceSet.NodeId.IdentifierAsString}_{pumpBrowseName.Name}",
                InstanceNamespaceIndex);
            if (PredefinedNodes.ContainsKey(pumpNodeId))
            {
                m_logger.DeviceSetAlreadyContains(pumpBrowseName.Name);
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "DeviceSet already contains '{0}'.",
                    pumpBrowseName);
            }

            // The DeviceSet is passed as the parent so New() can derive the
            // per-instance NodeIds the factory stamps on the pump and its
            // mandatory children from the parent chain.
            PumpState pump = SystemContext
                .CreateInstanceOfPumpType(deviceSet, pumpBrowseName);
            int pumpNumber = m_pumpStates.Count + 1;
            pump.DisplayName = new LocalizedText(GetPumpDisplayName(pumpNumber));

            pump.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes;
            deviceSet.AddChild(pump);

            MaterialisePumpOptionalChildren(pump);

            // Attach the OpenUSD representation + live bindings before
            // registration. Per-instance NodeIds are already assigned by the
            // generated CreateOrReplace/AddXxx helpers, so the binding source
            // NodeIds captured here are the instance ones.
            AttachOpenUsdRepresentation(pump, pumpNumber);

            await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken)
                .ConfigureAwait(false);
            await AddRootNotifierAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            onRegistered?.Invoke(pump);

            // Variables hand-built onto the pump (rather than materialised by the
            // generated factory) are reachable by browse and read, but a monitored
            // item never samples them unless they are registered in their own
            // right -- register them explicitly so the OpenUSD bindings that use
            // them are live.
            await RegisterOpenUsdSignalsAsync(pump, cancellationToken).ConfigureAwait(false);

            TryAddToMachinesFolder(pump);
            m_pumpStates.Add(pump);

            m_logger.MaterialisedPump(pumpBrowseName.Name, pump.NodeId);
            return pump;
        }

        /// <summary>
        /// Materialises the optional PumpType children that the fluent
        /// simulation in <see cref="Configure"/> wires. Each call to a
        /// generator-emitted <c>AddXxx(context)</c> helper creates the
        /// child, assigns it to the parent's typed property and stamps a
        /// per-instance NodeId on the new node and its descendants; the
        /// parent.AddChild bookkeeping happens inside the helpers
        /// transparently.
        /// </summary>
        private void MaterialisePumpOptionalChildren(
            PumpState pump)
        {
            MaterialiseNameplate(pump.Identification!);

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
            pump.EventNotifier |= EventNotifiers.SubscribeToEvents;
            events.EventNotifier |= EventNotifiers.SubscribeToEvents;
            pump.AddReference(
                Opc.Ua.Types.ReferenceTypeIds.HasNotifier,
                isInverse: false,
                events.NodeId);
            events.AddReference(
                Opc.Ua.Types.ReferenceTypeIds.HasNotifier,
                isInverse: true,
                pump.NodeId);

            events.AddSupervisionProcessFluid(SystemContext);
            events.SupervisionProcessFluid!.AddCavitation(SystemContext);
            PreserveHistoryRead(events.SupervisionProcessFluid.Cavitation);

            events.AddSupervisionPumpOperation(SystemContext);
            events.SupervisionPumpOperation!.AddMotorOverheat(SystemContext);
            PreserveHistoryRead(events.SupervisionPumpOperation.MotorOverheat);

            // Maintenance container — leaf wiring deferred until the
            // typed-accessor generator (FB-3 phase 3) ships materialisable
            // leaves for ConditionBasedMaintenance / BreakdownMaintenance.
            pump.AddMaintenance(SystemContext);
        }

        internal static string GetPumpBrowseName(int pumpNumber)
        {
            return "Pump_" + pumpNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string GetPumpDisplayName(int pumpNumber)
        {
            return "Pump #" + pumpNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void PreserveHistoryRead(BaseVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }

            variable.AccessLevel |= AccessLevels.HistoryRead;
            variable.UserAccessLevel |= AccessLevels.HistoryRead;
            variable.AccessLevelEx |= AccessLevels.HistoryRead;
            variable.OnReadAccessLevel = (
                ISystemContext context,
                NodeState node,
                ref byte value) =>
            {
                value = (byte)(variable.AccessLevel | AccessLevels.HistoryRead);
                return ServiceResult.Good;
            };
        }

        private void PreservePumpHistoryReadAccessLevels()
        {
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is BaseVariableState variable &&
                    node.BrowseName.Name is "Cavitation" or "MotorOverheat")
                {
                    PreserveHistoryRead(variable);
                }
            }
        }

        /// <summary>
        /// Materialises the optional nameplate properties that carry the
        /// PumpX-2000 datasheet identification data. <c>Manufacturer</c>
        /// and <c>SerialNumber</c> are mandatory on
        /// <c>PumpIdentificationType</c> and are already created by the
        /// generated factory; every other field is optional and is added
        /// here through the generator-emitted <c>AddXxx(context)</c>
        /// helpers so each property keeps the browse name, namespace and
        /// DataType declared by the DI, Machinery and Pumps models. The
        /// values themselves are assigned by the fluent
        /// <c>WithProperty</c> wiring (Pump #1) and by the topology-element
        /// builder (Pump #2).
        /// </summary>
        private void MaterialiseNameplate(PumpIdentificationState identification)
        {
            // OPC 10000-100 (DI) nameplate.
            identification.AddManufacturerUri(SystemContext);
            identification.AddModel(SystemContext);
            identification.AddProductCode(SystemContext);
            identification.AddDeviceClass(SystemContext);
            identification.AddHardwareRevision(SystemContext);
            identification.AddSoftwareRevision(SystemContext);
            identification.AddProductInstanceUri(SystemContext);
            identification.AddAssetId(SystemContext);
            identification.AddComponentName(SystemContext);

            // OPC 40001-1 (Machinery) nameplate.
            identification.AddLocation(SystemContext);
            identification.AddYearOfConstruction(SystemContext);
            identification.AddMonthOfConstruction(SystemContext);

            // OPC 40223 (Pumps) nameplate.
            identification.AddDayOfConstruction(SystemContext);
            identification.AddArticleNumber(SystemContext);
            identification.AddOrderProductCode(SystemContext);
            identification.AddTypeOfProduct(SystemContext);
            identification.AddSupplier(SystemContext);
            identification.AddCountryOfOrigin(SystemContext);
            identification.AddFabricationNumber(SystemContext);
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
        private readonly IOptions<PumpDeviceIntegrationOptions>? m_options;

        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public PumpNodeManagerFactory()
            : this(null, null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the post-setup runner into
        /// every manager it produces.
        /// </summary>
        public PumpNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<PumpDeviceIntegrationOptions>? options = null)
        {
            m_runner = runner;
            m_options = options;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Opc.Ua.Pumps.Namespaces.Pumps,
            Opc.Ua.Machinery.Namespaces.Machinery,
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
            IAsyncNodeManager nm = new PumpNodeManager(server, configuration, m_runner, m_options);
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
