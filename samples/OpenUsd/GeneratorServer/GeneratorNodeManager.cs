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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Generators;
using Opc.Ua.Machinery;
using Opc.Ua.OpenUsd;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;

namespace Generators
{
    /// <summary>
    /// Runtime options for the generator-set device-integration sample.
    /// </summary>
    public sealed class GeneratorDeviceIntegrationOptions
    {
        /// <summary>
        /// Gets or sets how many simulated generator sets are materialised.
        /// </summary>
        public int GeneratorCount { get; set; } = 2;

        /// <summary>
        /// Gets or sets the interval used by the live simulation loop.
        /// </summary>
        public TimeSpan SimulationInterval { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Gets or sets whether one set develops faults on a slow rotation.
        /// </summary>
        /// <remarks>
        /// On by default. A set running to its datasheet stays well inside every
        /// trip point, so without a deliberate excursion the protection alarms, the
        /// shutdown class and the Fault branch of the state machine are all code
        /// that never runs. Turn it off to watch a plant that is purely healthy.
        /// </remarks>
        public bool InjectFaults { get; set; } = true;
    }

    /// <summary>
    /// Node manager for the Generators companion-specification server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This partial carries the infrastructure - construction, address-space load
    /// and instance materialisation. The datasheet-driven simulation, the twelve
    /// state operating state machine and the protection alarms live in
    /// <c>GeneratorNodeManager.Configure.cs</c>; the OpenUSD twin lives in
    /// <c>OpenUsdRepresentation.cs</c>.
    /// </para>
    /// <para>
    /// Unlike the pump sample - whose <c>PumpType</c> derives from the Machinery
    /// <c>MachineType</c> and so cannot use the DI device helpers -
    /// <c>GeneratorSetType</c> derives from the DI <c>DeviceType</c>, so sets are
    /// created through <see cref="DiNodeManager.CreateDeviceAsync{TDevice}"/> and
    /// get the DI nameplate, registration and topology wiring for free.
    /// </para>
    /// </remarks>
    public partial class GeneratorNodeManager : DiNodeManager
    {
        private readonly GeneratorDeviceIntegrationOptions m_options;
        private readonly List<GeneratorSetState> m_generatorSets = [];

        /// <summary>
        /// Wires the simulation, state machines and alarms against the
        /// materialised instances. Implemented in
        /// <c>GeneratorNodeManager.Configure.cs</c>.
        /// </summary>
        /// <param name="builder">The active fluent builder.</param>
        partial void Configure(INodeManagerBuilder builder);

        /// <summary>
        /// Initialises a new <see cref="GeneratorNodeManager"/> without DI-hosting
        /// integration.
        /// </summary>
        /// <param name="server">The server the manager belongs to.</param>
        /// <param name="configuration">The application configuration.</param>
        public GeneratorNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Initialises a new <see cref="GeneratorNodeManager"/> that participates in
        /// the DI hosting post-setup pipeline.
        /// </summary>
        /// <param name="server">The server the manager belongs to.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="postSetupRunner">Optional DI post-setup pipeline.</param>
        /// <param name="options">Optional sample options.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The configured generator count or simulation interval is out of range.
        /// </exception>
        public GeneratorNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<GeneratorDeviceIntegrationOptions>? options = null)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  Opc.Ua.Generators.Namespaces.Generators,
                  Opc.Ua.Machinery.Namespaces.Machinery,
                  Opc.Ua.OpenUsd.Namespaces.OpenUSD)
        {
            // The base constructor points SystemContext.NodeIdFactory at itself;
            // the New() override below takes over so every instance child gets a
            // NodeId derived from its parent rather than the type-level one.
            SystemContext.NodeIdFactory = this;
            m_options = options?.Value ?? new GeneratorDeviceIntegrationOptions();
            if (m_options.GeneratorCount is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    $"{nameof(options)}.{nameof(GeneratorDeviceIntegrationOptions.GeneratorCount)}",
                    m_options.GeneratorCount,
                    "Generator count must be between 1 and 100.");
            }
            if (m_options.SimulationInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    $"{nameof(options)}.{nameof(GeneratorDeviceIntegrationOptions.SimulationInterval)}",
                    m_options.SimulationInterval,
                    "Simulation interval must be positive.");
            }
        }

        /// <summary>
        /// Gets the registered generator-set instance NodeIds.
        /// </summary>
        public ArrayOf<NodeId> GeneratorNodeIds
        {
            get
            {
                var nodeIds = new List<NodeId>(m_generatorSets.Count);
                foreach (GeneratorSetState set in m_generatorSets)
                {
                    nodeIds.Add(set.NodeId);
                }
                return nodeIds.ToArrayOf();
            }
        }

        /// <summary>
        /// Gets the interval the simulation loop ticks at.
        /// </summary>
        internal TimeSpan SimulationInterval => m_options.SimulationInterval;

        /// <summary>
        /// Gets whether the fault schedule is running.
        /// </summary>
        internal bool InjectFaults => m_options.InjectFaults;

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState { Parent: not null } instance)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;
                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    InstanceNamespaceIndex);
            }
            return node.NodeId;
        }

        /// <summary>
        /// Creates and registers a generator set organised by the DI
        /// <c>DeviceSet</c>, wired into the running simulation.
        /// </summary>
        /// <param name="browseName">Browse name for the new set.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The registered generator set.</returns>
        public ValueTask<GeneratorSetState> CreateGeneratorSetAsync(
            QualifiedName browseName,
            CancellationToken cancellationToken = default)
        {
            return MaterialiseGeneratorSetAsync(
                browseName,
                m_generatorSets.Count + 1,
                cancellationToken,
                RegisterGeneratorSimulation);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // Compose the predefined-node tree from the source-generated models in
            // dependency order. No runtime XML loading: the NodeSet2 XMLs ship only
            // as <AdditionalFiles> for the source generator. The generated AddOpcUa*
            // methods are idempotent and pull in their declared dependencies through
            // [ModelDependencyAttribute], so a direct chain is sufficient.
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaGenerators(context);
            nodes.AddOpcUaOpenUsd(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override async ValueTask OnAddressSpaceReadyAsync(
            CancellationToken cancellationToken)
        {
            // Phase 1 (async): materialise the instances the fluent Configure pass
            // expects to find, so the builder has typed nodes to wire against.
            await ConfigureInstancesAsync(cancellationToken).ConfigureAwait(false);

            // Phase 2 (sync): wire the simulation, state machines and alarms.
            CreateFluentBuilder(InstanceNamespaceIndex)
                .Configure(Configure)
                .Seal();

            m_logger.GeneratorAddressSpaceReady(PredefinedNodes.Count, m_generatorSets.Count);
        }

        /// <summary>
        /// Materialises the configured generator sets and the OpenUSD facility.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async ValueTask ConfigureInstancesAsync(CancellationToken cancellationToken)
        {
            // The OpenUSD facility comes first so each set's representation has a
            // stage to reference.
            await MaterialiseOpenUsdFacilityAsync(cancellationToken).ConfigureAwait(false);

            for (int number = 1; number <= m_options.GeneratorCount; number++)
            {
                var browseName = new QualifiedName(
                    GetGeneratorBrowseName(number),
                    InstanceNamespaceIndex);
                await MaterialiseGeneratorSetAsync(browseName, number, cancellationToken)
                    .ConfigureAwait(false);
            }

            await OrganiseRepresentationsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates one generator set, materialises the optional subsystems the
        /// simulation drives, attaches its OpenUSD twin and registers the whole
        /// subtree.
        /// </summary>
        /// <param name="browseName">Browse name for the set.</param>
        /// <param name="setNumber">One-based unit number, used for the nameplate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="onRegistered">Invoked once the set is registered.</param>
        /// <returns>The registered generator set.</returns>
        private async ValueTask<GeneratorSetState> MaterialiseGeneratorSetAsync(
            QualifiedName browseName,
            int setNumber,
            CancellationToken cancellationToken,
            Action<GeneratorSetState>? onRegistered = null)
        {
            IDeviceBuilder<GeneratorSetState> builder = await CreateDeviceAsync(
                browseName,
                NodeId.Create(
                    Opc.Ua.Generators.ObjectTypes.GeneratorSetType,
                    Opc.Ua.Generators.Namespaces.Generators,
                    Server.NamespaceUris),
                parent => SystemContext.CreateInstanceOfGeneratorSetType(parent, browseName),
                parent: null,
                cancellationToken).ConfigureAwait(false);

            GeneratorSetState set = builder.Device;
            set.DisplayName = new LocalizedText(GetGeneratorDisplayName(setNumber));

            MaterialiseOptionalChildren(set);
            MaterialiseProtectionAlarms(set);
            AttachOpenUsdRepresentation(set, setNumber);

            await AddPredefinedNodeAsync(SystemContext, set, cancellationToken)
                .ConfigureAwait(false);
            await AddRootNotifierAsync(set, cancellationToken).ConfigureAwait(false);

            // The nameplate is written after the subtree is registered. Each
            // identification property registers itself as it is created, so writing
            // beforehand lets the subsequent subtree registration replace those
            // nodes with freshly materialised, valueless ones - the properties then
            // browse but read BadNotReadable.
            WriteNameplate(builder, setNumber);

            onRegistered?.Invoke(set);

            // Variables hand-built onto the set (rather than materialised by the
            // generated factory) browse and read correctly, but a monitored item
            // never samples them unless they are registered in their own right.
            await RegisterOpenUsdSignalsAsync(set, cancellationToken).ConfigureAwait(false);

            m_generatorSets.Add(set);
            m_logger.MaterialisedGeneratorSet(browseName.Name, set.NodeId);
            return set;
        }

        /// <summary>
        /// Writes the datasheet nameplate onto the DI identification properties.
        /// </summary>
        /// <param name="builder">Device builder for the set.</param>
        /// <param name="setNumber">One-based unit number.</param>
        /// <remarks>
        /// Unit-level fields are derived from the set number so that
        /// <c>--generators N</c> yields a consistent nameplate per instance rather
        /// than N copies of the same serial number.
        /// </remarks>
        private static void WriteNameplate(IDeviceBuilder<GeneratorSetState> builder, int setNumber)
        {
            builder.WithIdentification(id =>
            {
                id.Manufacturer = new LocalizedText(GeneratorDatasheet.Identity.Manufacturer);
                id.ManufacturerUri = GeneratorDatasheet.Identity.ManufacturerUri;
                id.Model = new LocalizedText(GeneratorDatasheet.Identity.Model);
                id.ProductCode = GeneratorDatasheet.Identity.ProductCode;
                id.HardwareRevision = GeneratorDatasheet.Identity.HardwareRevision;
                id.SoftwareRevision = GeneratorDatasheet.Identity.SoftwareRevision;
                id.DeviceRevision = GeneratorDatasheet.Identity.DeviceRevision;
                id.SerialNumber = GetSerialNumber(setNumber);
                id.DeviceManual = "https://simgen.example.com/genx-500/manual";
                id.ProductInstanceUri = FormattableString.Invariant($"urn:simgen.example.com:GenX-500:{GetSerialNumber(setNumber)}");
            });

            // Nameplate properties the generated factory already materialised keep
            // the declaration's access level, which grants no read. Only properties
            // the builder creates itself come out readable, so the ones that were
            // already there have to be opened explicitly or they browse but answer
            // BadNotReadable.
            GeneratorSetState set = builder.Device;
            MakeReadable(
                set.Manufacturer, set.ManufacturerUri, set.Model, set.ProductCode,
                set.HardwareRevision, set.SoftwareRevision, set.DeviceRevision,
                set.SerialNumber, set.DeviceManual, set.ProductInstanceUri,
                set.RevisionCounter);
        }

        /// <summary>
        /// Grants current-read access to variables that carry a static value.
        /// </summary>
        /// <param name="variables">The variables to open for reading.</param>
        private static void MakeReadable(params BaseVariableState?[] variables)
        {
            foreach (BaseVariableState? variable in variables)
            {
                if (variable == null)
                {
                    continue;
                }
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
                variable.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            }
        }

        /// <summary>
        /// Materialises the optional <c>GeneratorSetType</c> children the simulation
        /// drives. Mandatory children - Identification, OperatingState, OperatingMode,
        /// Engine, Alternator and Controller - are created by the generated factory.
        /// </summary>
        /// <param name="set">The generator set to extend.</param>
        private void MaterialiseOptionalChildren(GeneratorSetState set)
        {
            set.AddMachineryBuildingBlocks(SystemContext)
                .AddFuelSystem(SystemContext)
                .AddCoolingSystem(SystemContext)
                .AddLubricationSystem(SystemContext)
                .AddStartingSystem(SystemContext)
                .AddEmissionsStandard(SystemContext)
                .AddApplication(SystemContext)
                .AddGeneratorBreakerClosed(SystemContext)
                .AddGeneratorBreakerAvailable(SystemContext)
                .AddRemoteStartInput(SystemContext)
                .AddRunRequest(SystemContext)
                .AddLoadInhibit(SystemContext)
                .AddAvailableToLoad(SystemContext)
                .AddStart(SystemContext)
                .AddStop(SystemContext)
                .AddEmergencyStop(SystemContext)
                .AddResetFaults(SystemContext)
                .AddSetOperatingMode(SystemContext)
                .AddStartTest(SystemContext);

            MaterialiseSubsystemChildren(set);
        }

        /// <summary>
        /// Materialises the optional subsystem members the simulation publishes.
        /// </summary>
        /// <param name="set">The generator set to extend.</param>
        /// <remarks>
        /// The specification makes most telemetry optional so the type fits both a
        /// bare air-cooled residential set and a fully instrumented industrial one.
        /// This sample models the instrumented case, so it opts in explicitly rather
        /// than assuming the factory materialises them.
        /// </remarks>
        private void MaterialiseSubsystemChildren(GeneratorSetState set)
        {
            set.Engine!
                .AddPercentLoad(SystemContext)
                .AddOilPressure(SystemContext)
                .AddOilTemperature(SystemContext)
                .AddCoolantTemperature(SystemContext)
                .AddFuelRate(SystemContext)
                .AddExhaustGasTemperature(SystemContext)
                .AddIntakeManifoldTemperature(SystemContext)
                .AddNumberOfStarts(SystemContext)
                .AddDisplacement(SystemContext)
                .AddCylinderCount(SystemContext)
                .AddRatedSpeed(SystemContext)
                .AddAspiration(SystemContext);

            // Three-phase set: L1 is mandatory, L2 and L3 are not.
            set.Alternator!
                .AddL2(SystemContext)
                .AddL3(SystemContext)
                .AddAverageLineToLineVoltage(SystemContext)
                .AddAverageLineToNeutralVoltage(SystemContext)
                .AddAverageCurrent(SystemContext)
                .AddTotalReactivePower(SystemContext)
                .AddTotalApparentPower(SystemContext)
                .AddAveragePowerFactor(SystemContext)
                .AddTotalRealEnergy(SystemContext)
                .AddLoadPercent(SystemContext)
                .AddWindingTemperature1(SystemContext)
                .AddConnection(SystemContext)
                .AddExcitationType(SystemContext)
                .AddNumberOfPoles(SystemContext);

            foreach (AlternatorPhaseState phase in new[]
            {
                set.Alternator!.L1!, set.Alternator!.L2!, set.Alternator!.L3!,
            })
            {
                phase.AddLineToNeutralVoltage(SystemContext)
                    .AddLineToLineVoltage(SystemContext)
                    .AddRealPower(SystemContext)
                    .AddReactivePower(SystemContext)
                    .AddApparentPower(SystemContext)
                    .AddPowerFactor(SystemContext);
            }

            set.FuelSystem!
                .AddFuelLevel(SystemContext)
                .AddFuelVolume(SystemContext)
                .AddFuelConsumptionRate(SystemContext)
                .AddRuntimeRemaining(SystemContext)
                .AddTotalFuelConsumed(SystemContext);

            set.CoolingSystem!
                .AddCoolantTemperature(SystemContext)
                .AddCoolantLevel(SystemContext)
                .AddAmbientTemperature(SystemContext)
                .AddCoolingMethod(SystemContext)
                .AddRadiatorFanRunning(SystemContext);

            set.LubricationSystem!
                .AddOilPressure(SystemContext)
                .AddOilTemperature(SystemContext)
                .AddOilLevel(SystemContext);

            set.StartingSystem!
                .AddBatteryChargingCurrent(SystemContext)
                .AddBatteryChargerActive(SystemContext)
                .AddStartAttempts(SystemContext);
        }

        /// <summary>
        /// Returns the browse name for a one-based generator set number.
        /// </summary>
        /// <param name="setNumber">One-based unit number.</param>
        /// <returns>The browse name.</returns>
        internal static string GetGeneratorBrowseName(int setNumber)
        {
            return FormattableString.Invariant($"GeneratorSet_{setNumber}");
        }

        /// <summary>
        /// Returns the display name for a one-based generator set number.
        /// </summary>
        /// <param name="setNumber">One-based unit number.</param>
        /// <returns>The display name.</returns>
        internal static string GetGeneratorDisplayName(int setNumber)
        {
            return FormattableString.Invariant($"Generator Set {setNumber}");
        }

        /// <summary>
        /// Returns the datasheet serial number for a one-based generator set number.
        /// </summary>
        /// <param name="setNumber">One-based unit number.</param>
        /// <returns>The serial number.</returns>
        internal static string GetSerialNumber(int setNumber)
        {
            return FormattableString.Invariant($"SG-500-{setNumber:D3}");
        }
    }

    /// <summary>
    /// Creates the <see cref="GeneratorNodeManager"/> for the DI hosting pipeline.
    /// </summary>
    public sealed class GeneratorNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IDiPostSetupRunner? m_postSetupRunner;
        private readonly IOptions<GeneratorDeviceIntegrationOptions>? m_options;

        /// <summary>
        /// Initialises a factory without DI-hosting integration.
        /// </summary>
        public GeneratorNodeManagerFactory()
        {
        }

        /// <summary>
        /// Initialises a factory that participates in the DI hosting pipeline.
        /// </summary>
        /// <param name="postSetupRunner">The DI post-setup pipeline.</param>
        /// <param name="options">Sample options.</param>
        public GeneratorNodeManagerFactory(
            IDiPostSetupRunner? postSetupRunner,
            IOptions<GeneratorDeviceIntegrationOptions>? options)
        {
            m_postSetupRunner = postSetupRunner;
            m_options = options;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Opc.Ua.Generators.Namespaces.Generators,
            Opc.Ua.Machinery.Namespaces.Machinery,
            Opc.Ua.OpenUsd.Namespaces.OpenUSD,
        }.ToArrayOf();

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to server
            IAsyncNodeManager nodeManager =
                new GeneratorNodeManager(server, configuration, m_postSetupRunner, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="GeneratorNodeManager"/>.
    /// </summary>
    internal static partial class GeneratorNodeManagerLog    {
        [LoggerMessage(
            EventId = GeneratorServerEventIds.GeneratorNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Generator address space ready: {NodeCount} nodes, {SetCount} generator set(s).")]
        public static partial void GeneratorAddressSpaceReady(
            this ILogger logger, int nodeCount, int setCount);

        [LoggerMessage(
            EventId = GeneratorServerEventIds.GeneratorNodeManager + 1,
            Level = LogLevel.Information,
            Message = "Materialised generator set '{BrowseName}' as {NodeId}.")]
        public static partial void MaterialisedGeneratorSet(
            this ILogger logger, string? browseName, NodeId nodeId);
    }
}
