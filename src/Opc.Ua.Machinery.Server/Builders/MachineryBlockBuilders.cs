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
using System.Collections.Generic;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery.Server.StateMachines;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Shared placement state for the building blocks of one machinery item —
    /// a machine or one of its components.
    /// </summary>
    internal sealed class MachineryItemBlocks
    {
        public MachineryItemBlocks(MachineryBuildScope scope, NodeState item)
        {
            Scope = scope;
            Item = item;
            Context = scope.Context;
            MachineryNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                Context,
                Opc.Ua.Machinery.Namespaces.Machinery);
            DiNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                Context,
                Opc.Ua.Di.Namespaces.OpcUaDi);
        }

        public MachineryBuildScope Scope { get; }

        public NodeState Item { get; }

        public ISystemContext Context { get; }

        public ushort MachineryNamespaceIndex { get; }

        public ushort DiNamespaceIndex { get; }

        public IMachineryItemStateController? ItemState { get; private set; }

        public IMachineryOperationModeController? OperationMode { get; private set; }

        /// <summary>
        /// Gets the OPC 40001-1 <c>MachineryBuildingBlocks</c> organizer of
        /// this machinery item, creating it on first use.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 40001-1 §7.1 puts every building block below a
        /// <c>FolderType</c> object named <c>MachineryBuildingBlocks</c> in the
        /// Machinery namespace, referenced from the machinery item with
        /// <c>HasComponent</c>, and the blocks themselves with
        /// <c>HasAddIn</c> from that folder. This is not cosmetic: the
        /// conformance units for the item state, the operation mode, the two
        /// counters, monitoring, equipment, notifications and OPC 40001-3 job
        /// management are all worded as "has this AddIn <em>under its
        /// MachineryBuildingBlocks folder</em>", so a server without the folder
        /// satisfies none of them.
        /// </para>
        /// <para>
        /// The model declares no type for it — the folder exists only at
        /// instance level, identified by its browse name — which is why it is
        /// assembled here rather than by a generated factory.
        /// </para>
        /// </remarks>
        public FolderState BuildingBlocks
        {
            get
            {
                return m_buildingBlocks ??= MachineryBuilderUtilities.AddFolderChild(
                    Context,
                    Item,
                    MachineryName(MachineryBuildingBlocksBrowseName),
                    Opc.Ua.Types.ReferenceTypeIds.HasComponent);
            }
        }

        /// <summary>
        /// The browse name OPC 40001-1 §7.1 gives the building-block organizer.
        /// </summary>
        public const string MachineryBuildingBlocksBrowseName = "MachineryBuildingBlocks";

        /// <summary>
        /// References a building block from the organizer folder without
        /// duplicating it.
        /// </summary>
        /// <remarks>
        /// OPC 40001-1 §7.3 is explicit that the second path must reach the
        /// same node rather than a copy, so only references are added: the
        /// block keeps whatever parent it was created under — the machinery
        /// item for most blocks, <c>Monitoring/Status</c> for the two state
        /// machines.
        /// </remarks>
        /// <param name="block">The building block to reference.</param>
        public void ReferenceBuildingBlock(BaseInstanceState block)
        {
            if (block == null)
            {
                throw new ArgumentNullException(nameof(block));
            }
            FolderState folder = BuildingBlocks;
            if (folder.ReferenceExists(
                    MachineryBuilderUtilities.HasAddIn,
                    false,
                    block.NodeId))
            {
                return;
            }
            folder.AddReference(MachineryBuilderUtilities.HasAddIn, false, block.NodeId);
            block.AddReference(MachineryBuilderUtilities.HasAddIn, true, folder.NodeId);
            Scope.RecordFacet(MachineryFacet.BuildingBlockOrganization);
        }

        public QualifiedName MachineryName(string name)
        {
            return new QualifiedName(name, MachineryNamespaceIndex);
        }

        public QualifiedName DiName(string name)
        {
            return new QualifiedName(name, DiNamespaceIndex);
        }

        public MonitoringState AddMonitoring(Action<IMonitoringBuilder> configure)
        {
            MonitoringState monitoring = EnsureMonitoring();
            var builder = new MonitoringBuilder(this, monitoring);
            configure(builder);
            ItemState ??= builder.ItemStateController;
            OperationMode ??= builder.OperationModeController;
            return monitoring;
        }

        /// <summary>
        /// Returns the machinery item's <c>Monitoring</c> add-in, creating it
        /// on first use. Idempotent, because OPC 40001-4 energy and an
        /// explicit <c>WithMonitoring</c> both need it and either may come
        /// first.
        /// </summary>
        public MonitoringState EnsureMonitoring()
        {
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "Monitoring");
            if (m_monitoring != null)
            {
                return m_monitoring;
            }
            MonitoringState monitoring = MachineryBuilderUtilities.AddAddIn(
                Context,
                Item,
                MachineryName(MachineryBrowseNames.Monitoring),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfMonitoringType(parent, name));
            ReferenceBuildingBlock(monitoring);
            Scope.RecordFacet(MachineryFacet.Monitoring);
            m_monitoring = monitoring;
            return monitoring;
        }

        /// <summary>
        /// Returns the <c>Monitoring/Consumption</c> folder OPC 40001-4 hangs
        /// its resources under, creating the monitoring add-in with it when
        /// needed.
        /// </summary>
        public FolderState EnsureConsumption()
        {
            MonitoringState monitoring = EnsureMonitoring();
            monitoring.AddConsumption(Context);
            return monitoring.Consumption!;
        }

        public MachineryOperationCounterState AddOperationCounters(
            Action<IMachineryOperationCounterBuilder> configure)
        {
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "OperationCounters");
            // MachineryOperationCounterType's DefaultInstanceBrowseName is
            // 2:OperationCounters — the Device Integration namespace, not the
            // Machinery one: OPC 40001-1 reuses the DI functional group here
            // so a client written against OPC 10000-100 finds it. Every
            // conformance unit is worded "using the DefaultInstanceBrowseName",
            // so the namespace is load-bearing.
            MachineryOperationCounterState counters = MachineryBuilderUtilities.AddAddIn(
                Context,
                Item,
                DiName(DiBrowseNames.OperationCounters),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfMachineryOperationCounterType(parent, name));
            ReferenceBuildingBlock(counters);
            configure(new MachineryOperationCounterBuilder(this, counters));
            Scope.RecordFacet(MachineryFacet.OperationCounters);
            return counters;
        }

        public MachineryLifetimeCounterState AddLifetimeCounters(
            Action<IMachineryLifetimeCounterBuilder> configure)
        {
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "LifetimeCounters");
            MachineryLifetimeCounterState counters = MachineryBuilderUtilities.AddAddIn(
                Context,
                Item,
                MachineryName(MachineryBrowseNames.LifetimeCounters),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfMachineryLifetimeCounterType(parent, name));
            ReferenceBuildingBlock(counters);
            configure(new MachineryLifetimeCounterBuilder(this, counters));
            Scope.RecordFacet(MachineryFacet.LifetimeCounters);
            return counters;
        }

        /// <summary>
        /// Adds the identification add-in. <c>MachineIdentificationType</c> and
        /// <c>MachineryComponentIdentificationType</c> both derive from
        /// <c>MachineryItemIdentificationType</c>, which in turn derives from
        /// the Device Integration <c>FunctionalGroupType</c> — so the add-in is
        /// a functional group, and its browse name comes from the DI namespace.
        /// </summary>
        public MachineryItemIdentificationState AddIdentification(
            bool asMachine,
            Action<MachineryIdentificationData> configure)
        {
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "Identification");

            var data = new MachineryIdentificationData();
            configure(data);
            data.Validate();

            QualifiedName browseName = DiName(DiBrowseNames.Identification);
            MachineryItemIdentificationState identification = asMachine
                ? MachineryBuilderUtilities.AddAddIn(
                    Context,
                    Item,
                    browseName,
                    static (ctx, parent, name) =>
                        ctx.CreateInstanceOfMachineIdentificationType(parent, name))
                : MachineryBuilderUtilities.AddAddIn(
                    Context,
                    Item,
                    browseName,
                    static (ctx, parent, name) =>
                        ctx.CreateInstanceOfMachineryComponentIdentificationType(parent, name));

            // Identification is in Table 12's "may be referenced" column: the
            // direct HasAddIn from the item stays the primary path, and the
            // organizer references the same node in addition.
            ReferenceBuildingBlock(identification);
            WriteIdentification(identification, data, asMachine);
            Scope.RecordFacet(
                asMachine
                    ? MachineryFacet.MachineIdentification
                    : MachineryFacet.ComponentIdentification);
            if (data.Writable)
            {
                if (asMachine)
                {
                    Scope.RecordFacet(MachineryFacet.MachineIdentificationWritable);
                }
                else
                {
                    Scope.RecordFacet(MachineryFacet.ComponentIdentificationMandatory);
                    Scope.RecordFacet(MachineryFacet.ComponentIdentificationWritable);
                }
            }
            return identification;
        }

        /// <summary>
        /// Leaves a nameplate member writable for clients.
        /// </summary>
        /// <remarks>
        /// Both access levels have to say so: a client checks
        /// <c>UserAccessLevel</c>, and the server enforces on
        /// <c>AccessLevel</c>.
        /// </remarks>
        private static void MakeWritable(BaseVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
        }

        private void WriteIdentification(
            MachineryItemIdentificationState identification,
            MachineryIdentificationData data,
            bool asMachine)
        {
            // Manufacturer and SerialNumber are mandatory in OPC 40001-1, so
            // the generated factory has already materialised them and emits no
            // Add* helper for them.
            identification.Manufacturer!.Value = data.Manufacturer;
            identification.SerialNumber!.Value = data.SerialNumber!;

            if (data.ManufacturerUri != null)
            {
                identification.AddManufacturerUri(Context, v => v.Value = data.ManufacturerUri);
            }
            if (!data.Model.IsNull)
            {
                identification.AddModel(Context, v => v.Value = data.Model);
            }
            if (data.ProductCode != null)
            {
                identification.AddProductCode(Context, v => v.Value = data.ProductCode);
            }
            if (data.HardwareRevision != null)
            {
                identification.AddHardwareRevision(Context, v => v.Value = data.HardwareRevision);
            }
            if (data.SoftwareRevision != null)
            {
                identification.AddSoftwareRevision(Context, v => v.Value = data.SoftwareRevision);
            }
            if (data.DeviceClass != null)
            {
                identification.AddDeviceClass(Context, v => v.Value = data.DeviceClass);
            }
            if (data.AssetId != null || data.Writable)
            {
                identification.AddAssetId(Context, v => v.Value = data.AssetId!);
            }
            if (!data.ComponentName.IsNull || data.Writable)
            {
                identification.AddComponentName(Context, v => v.Value = data.ComponentName);
            }
            if (data.Writable)
            {
                // The model declares all three with
                // AccessLevel = CurrentRead | CurrentWrite; this is what makes
                // the instance honour that rather than publish a read-only copy.
                MakeWritable(identification.AssetId);
                MakeWritable(identification.ComponentName);
            }
            if (data.InitialOperationDate.HasValue)
            {
                identification.AddInitialOperationDate(
                    Context,
                    v => v.Value = data.InitialOperationDate.Value);
            }
            if (data.YearOfConstruction.HasValue)
            {
                identification.AddYearOfConstruction(
                    Context,
                    v => v.Value = data.YearOfConstruction.Value);
            }
            if (data.MonthOfConstruction.HasValue)
            {
                identification.AddMonthOfConstruction(
                    Context,
                    v => v.Value = data.MonthOfConstruction.Value);
            }

            if (asMachine && identification is MachineIdentificationState machine)
            {
                if (data.ProductInstanceUri == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "OPC 40001-1 requires ProductInstanceUri on a machine identification.");
                }
                machine.ProductInstanceUri!.Value = data.ProductInstanceUri!;
                if (data.Location != null || data.Writable)
                {
                    machine.AddLocation(Context, v => v.Value = data.Location!);
                }
                if (data.Writable)
                {
                    MakeWritable(machine.Location);
                }
            }
            else
            {
                if (data.ProductInstanceUri != null)
                {
                    identification.AddProductInstanceUri(
                        Context,
                        v => v.Value = data.ProductInstanceUri);
                }
                if (data.DeviceRevision != null &&
                    identification is MachineryComponentIdentificationState component)
                {
                    component.AddDeviceRevision(Context, v => v.Value = data.DeviceRevision);
                }
            }
        }

        internal void SetControllers(
            IMachineryItemStateController? itemState,
            IMachineryOperationModeController? operationMode)
        {
            ItemState ??= itemState;
            OperationMode ??= operationMode;
        }

        private FolderState? m_buildingBlocks;
        private MonitoringState? m_monitoring;
    }

    /// <summary>
    /// Implements <see cref="IMonitoringBuilder"/> over the OPC 40001-1
    /// <c>Monitoring</c> add-in of one machinery item, adding the optional
    /// <c>Status</c>, <c>Health</c>, <c>Process</c> and <c>Consumption</c>
    /// children on demand and reporting the controllers of the state machines
    /// it creates back to <see cref="MachineryItemBlocks"/>.
    /// </summary>
    internal sealed class MonitoringBuilder : IMonitoringBuilder
    {
        public MonitoringBuilder(MachineryItemBlocks blocks, MonitoringState state)
        {
            m_blocks = blocks;
            State = state;
        }

        public MonitoringState State { get; }

        public IMachineryItemStateController? ItemStateController { get; private set; }

        public IMachineryOperationModeController? OperationModeController { get; private set; }

        public IMonitoringBuilder WithMachineryItemState(
            MachineryItemStateValue initialState = MachineryItemStateValue.NotAvailable)
        {
            FolderState status = EnsureStatus();
            MachineryItemState_StateMachineState stateMachine =
                MachineryBuilderUtilities.AddComponentChild(
                    m_blocks.Context,
                    status,
                    m_blocks.MachineryName(MachineryBrowseNames.MachineryItemState),
                    static (ctx, parent, name) =>
                        ctx.CreateInstanceOfMachineryItemState_StateMachineType(parent, name));
            stateMachine.AddLastTransition(m_blocks.Context);
            m_blocks.ReferenceBuildingBlock(stateMachine);
            ItemStateController = new MachineryItemStateController(
                stateMachine,
                m_blocks.Context,
                m_blocks.MachineryNamespaceIndex,
                initialState);
            m_blocks.SetControllers(ItemStateController, null);
            m_blocks.Scope.RecordFacet(MachineryFacet.MachineryItemState);
            return this;
        }

        public IMonitoringBuilder WithOperationMode(
            MachineryOperationModeValue initialMode = MachineryOperationModeValue.None)
        {
            FolderState status = EnsureStatus();
            MachineryOperationModeStateMachineState stateMachine =
                MachineryBuilderUtilities.AddComponentChild(
                    m_blocks.Context,
                    status,
                    m_blocks.MachineryName(MachineryBrowseNames.MachineryOperationMode),
                    static (ctx, parent, name) =>
                        ctx.CreateInstanceOfMachineryOperationModeStateMachineType(parent, name));
            stateMachine.AddLastTransition(m_blocks.Context);
            m_blocks.ReferenceBuildingBlock(stateMachine);
            OperationModeController = new MachineryOperationModeController(
                stateMachine,
                m_blocks.Context,
                m_blocks.MachineryNamespaceIndex,
                initialMode);
            m_blocks.SetControllers(null, OperationModeController);
            m_blocks.Scope.RecordFacet(MachineryFacet.OperationMode);
            return this;
        }

        public IMonitoringBuilder WithStacklight(Action<BasicStacklightState>? configure = null)
        {
            FolderState status = EnsureStatus();
            BasicStacklightState stacklight = MachineryBuilderUtilities.AddComponentChild(
                m_blocks.Context,
                status,
                m_blocks.MachineryName(MachineryBrowseNames.Stacklight),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfBasicStacklightType(parent, name));
            configure?.Invoke(stacklight);
            return this;
        }

        public IMonitoringBuilder WithHealth(Action<IMachineryHealthBuilder>? configure = null)
        {
            m_blocks.Scope.EnsureMutable();
            State.AddHealth(m_blocks.Context);
            var builder = new MachineryHealthBuilder(m_blocks, State.Health!);
            configure?.Invoke(builder);
            return this;
        }

        public IMonitoringBuilder WithProcess(Action<FolderState>? configure = null)
        {
            State.AddProcess(m_blocks.Context);
            configure?.Invoke(State.Process!);
            return this;
        }

        public IMonitoringBuilder WithConsumption(Action<FolderState>? configure = null)
        {
            State.AddConsumption(m_blocks.Context);
            configure?.Invoke(State.Consumption!);
            return this;
        }

        private FolderState EnsureStatus()
        {
            m_blocks.Scope.EnsureMutable();
            State.AddStatus(m_blocks.Context);
            return State.Status!;
        }

        private readonly MachineryItemBlocks m_blocks;
    }

    /// <summary>
    /// Implements <see cref="IMachineryHealthBuilder"/> over the
    /// <c>Monitoring/Health</c> folder, materialising the Device Integration
    /// <c>DeviceHealth</c> variable and <c>DeviceHealthAlarms</c> folder that
    /// the generated factory leaves out as optional children.
    /// </summary>
    internal sealed class MachineryHealthBuilder : IMachineryHealthBuilder
    {
        public MachineryHealthBuilder(MachineryItemBlocks blocks, FolderState state)
        {
            m_blocks = blocks;
            State = state;
            DeviceHealth = EnsureDeviceHealth();
            DeviceHealthAlarms = EnsureDeviceHealthAlarms();
        }

        public FolderState State { get; }

        public BaseDataVariableState<DeviceHealthEnumeration> DeviceHealth { get; }

        public FolderState DeviceHealthAlarms { get; }

        public IMachineryHealthBuilder WithDeviceHealth(DeviceHealthEnumeration deviceHealth)
        {
            m_blocks.Scope.EnsureMutable();
            DeviceHealth.Value = deviceHealth;
            return this;
        }

        /// <summary>
        /// Materialises the DI <c>DeviceHealth</c> variable. The generated
        /// <c>AddHealth</c> runs the folder factory with
        /// <c>forInstance: true</c>, which skips every optional child, so the
        /// variable has to be built here — with the DI-qualified browse name
        /// and the DI enumeration as its data type, exactly as the type
        /// declares it.
        /// </summary>
        private BaseDataVariableState<DeviceHealthEnumeration> EnsureDeviceHealth()
        {
            var browseName = m_blocks.DiName(DiBrowseNames.DeviceHealth);
            BaseDataVariableState<DeviceHealthEnumeration>? existing =
                MachineryBuilderUtilities
                    .FindChild<BaseDataVariableState<DeviceHealthEnumeration>>(
                        m_blocks.Context,
                        State,
                        browseName);
            if (existing != null)
            {
                return existing;
            }

            BaseDataVariableState<DeviceHealthEnumeration> variable =
                BaseDataVariableState<DeviceHealthEnumeration>
                    .With<EnumerationBuilder<DeviceHealthEnumeration>>(State);
            variable.SymbolicName = DiBrowseNames.DeviceHealth;
            variable.BrowseName = browseName;
            variable.DisplayName = new LocalizedText(DiBrowseNames.DeviceHealth);
            variable.TypeDefinitionId = Opc.Ua.VariableTypeIds.BaseDataVariableType;
            variable.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            variable.DataType = NodeId.Create(
                Opc.Ua.Di.DataTypes.DeviceHealthEnumeration,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                m_blocks.Context.NamespaceUris);
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            variable.ModellingRuleId = NodeId.Null;
            variable.Value = DeviceHealthEnumeration.NORMAL;
            variable.NodeId = m_blocks.Context.NodeIdFactory!.New(
                m_blocks.Context,
                variable);
            State.AddChild(variable);
            return variable;
        }

        private FolderState EnsureDeviceHealthAlarms()
        {
            var browseName = m_blocks.DiName(DiBrowseNames.DeviceHealthAlarms);
            return MachineryBuilderUtilities.FindChild<FolderState>(
                    m_blocks.Context,
                    State,
                    browseName) ??
                MachineryBuilderUtilities.AddFolderChild(
                    m_blocks.Context,
                    State,
                    browseName,
                    Opc.Ua.ReferenceTypeIds.HasComponent);
        }

        private readonly MachineryItemBlocks m_blocks;
    }

    /// <summary>
    /// Implements <see cref="IMachineComponentsBuilder"/> over the OPC 40001-1
    /// <c>Components</c> add-in, instantiating each component from the
    /// <c>&lt;Component&gt;</c> placeholder and refusing a browse name the
    /// machine already uses.
    /// </summary>
    internal sealed class MachineComponentsBuilder : IMachineComponentsBuilder
    {
        public MachineComponentsBuilder(MachineryBuildScope scope, MachineComponentsState state)
        {
            m_scope = scope;
            State = state;
        }

        public MachineComponentsState State { get; }

        public IMachineComponentsBuilder AddComponent(
            QualifiedName browseName,
            Action<IMachineComponentBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_scope.EnsureMutable();
            QualifiedName name = Qualify(browseName);
            if (!m_names.Add(name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The machine already declares a component named '{0}'.",
                    name);
            }
            BaseObjectState component = State.AddComponent_Placeholder(m_scope.Context, name);
            component.ModellingRuleId = NodeId.Null;
            MachineryBuilderUtilities.AssignInstanceNodeIds(m_scope.Context, component);
            configure(new MachineComponentBuilder(m_scope, component));
            m_scope.RecordFacet(MachineryFacet.Components);
            return this;
        }

        private QualifiedName Qualify(QualifiedName browseName)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A machine component must carry a browse name.");
            }
            return browseName.NamespaceIndex == 0
                ? new QualifiedName(
                    browseName.Name,
                    m_scope.BuildContext.InstanceNamespaceIndex)
                : browseName;
        }

        private readonly MachineryBuildScope m_scope;
        private readonly HashSet<QualifiedName> m_names = [];
    }

    /// <summary>
    /// Implements <see cref="IMachineComponentBuilder"/> for one machine
    /// component. A component is a machinery item in its own right, so it gets
    /// its own <see cref="MachineryItemBlocks"/> for its identification,
    /// monitoring and counters.
    /// </summary>
    internal sealed class MachineComponentBuilder : IMachineComponentBuilder
    {
        public MachineComponentBuilder(MachineryBuildScope scope, BaseObjectState state)
        {
            State = state;
            m_blocks = new MachineryItemBlocks(scope, state);
        }

        public BaseObjectState State { get; }

        public IMachineComponentBuilder WithIdentification(
            Action<MachineryIdentificationData> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            RemoveDeclaredIdentification();
            m_blocks.AddIdentification(asMachine: false, configure);
            return this;
        }

        public IMachineComponentBuilder WithMonitoring(Action<IMonitoringBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddMonitoring(configure);
            return this;
        }

        public IMachineComponentBuilder WithOperationCounters(
            Action<IMachineryOperationCounterBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddOperationCounters(configure);
            return this;
        }

        public IMachineComponentBuilder WithLifetimeCounters(
            Action<IMachineryLifetimeCounterBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddLifetimeCounters(configure);
            return this;
        }

        /// <summary>
        /// The placeholder factory materialises the mandatory
        /// <c>Identification</c> child as the base
        /// <c>MachineryItemIdentificationType</c>. OPC 40001-1 wants a
        /// component to publish the narrower
        /// <c>MachineryComponentIdentificationType</c>, so the declared child
        /// is dropped before the typed one replaces it.
        /// </summary>
        private void RemoveDeclaredIdentification()
        {
            MachineryItemIdentificationState? declared =
                MachineryBuilderUtilities.FindChild<MachineryItemIdentificationState>(
                    m_blocks.Context,
                    State,
                    m_blocks.DiName(DiBrowseNames.Identification));
            if (declared != null)
            {
                State.RemoveChild(declared);
            }
        }

        private readonly MachineryItemBlocks m_blocks;
    }

    /// <summary>
    /// Implements <see cref="IMachineryEquipmentBuilder"/> over the
    /// OPC 40001-1 <c>MachineryEquipment</c> folder, instantiating each piece
    /// of equipment from the <c>&lt;MachineryEquipment&gt;</c> placeholder
    /// with its mandatory <c>MachineryEquipmentTypeId</c> already written.
    /// </summary>
    internal sealed class MachineryEquipmentBuilder : IMachineryEquipmentBuilder
    {
        public MachineryEquipmentBuilder(
            MachineryBuildScope scope,
            MachineryEquipmentFolderState state)
        {
            m_scope = scope;
            State = state;
            m_diNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Di.Namespaces.OpcUaDi);
            m_machineryNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                scope.Context,
                Opc.Ua.Machinery.Namespaces.Machinery);
        }

        public MachineryEquipmentFolderState State { get; }

        public IMachineryEquipmentBuilder AddEquipment(
            QualifiedName browseName,
            string equipmentTypeId,
            Action<IMachineryEquipmentItemBuilder>? configure = null)
        {
            if (string.IsNullOrEmpty(equipmentTypeId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "OPC 40001-1 requires MachineryEquipmentTypeId on every equipment.");
            }
            m_scope.EnsureMutable();
            QualifiedName name = browseName.NamespaceIndex == 0
                ? new QualifiedName(
                    browseName.Name,
                    m_scope.BuildContext.InstanceNamespaceIndex)
                : browseName;
            if (!m_names.Add(name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The machine already declares equipment named '{0}'.",
                    name);
            }

            BaseObjectState equipment =
                State.AddMachineryEquipment_Placeholder(m_scope.Context, name);
            equipment.ModellingRuleId = NodeId.Null;
            MachineryBuilderUtilities.AssignInstanceNodeIds(m_scope.Context, equipment);
            var builder = new MachineryEquipmentItemBuilder(
                m_scope,
                equipment,
                m_diNamespaceIndex,
                m_machineryNamespaceIndex);
            builder.WriteEquipmentTypeId(equipmentTypeId);
            configure?.Invoke(builder);
            m_scope.RecordFacet(MachineryFacet.MachineryEquipment);
            return this;
        }

        private readonly MachineryBuildScope m_scope;
        private readonly HashSet<QualifiedName> m_names = [];
        private readonly ushort m_diNamespaceIndex;
        private readonly ushort m_machineryNamespaceIndex;
    }

    /// <summary>
    /// Implements <see cref="IMachineryEquipmentItemBuilder"/> for one piece
    /// of machinery equipment, materialising the optional properties and the
    /// <c>EquipmentLife</c> variable that the placeholder factory does not
    /// create.
    /// </summary>
    internal sealed class MachineryEquipmentItemBuilder : IMachineryEquipmentItemBuilder
    {
        public MachineryEquipmentItemBuilder(
            MachineryBuildScope scope,
            BaseObjectState state,
            ushort diNamespaceIndex,
            ushort machineryNamespaceIndex)
        {
            m_scope = scope;
            State = state;
            m_diNamespaceIndex = diNamespaceIndex;
            m_machineryNamespaceIndex = machineryNamespaceIndex;
        }

        public BaseObjectState State { get; }

        public IMachineryEquipmentItemBuilder WithDescription(LocalizedText description)
        {
            WriteProperty(
                new QualifiedName(MachineryBrowseNames.Description, m_machineryNamespaceIndex),
                Opc.Ua.Types.DataTypeIds.LocalizedText,
                Variant.From(description));
            return this;
        }

        public IMachineryEquipmentItemBuilder WithIdentification(
            Action<MachineryIdentificationData> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var data = new MachineryIdentificationData();
            configure(data);
            data.Validate();

            // The equipment placeholder declares only this subset as instance
            // properties. Manufacturer and the revisions reach an equipment
            // through the Device Integration IVendorNameplateType interface it
            // carries, not as children, so writing them here would invent
            // nodes the model does not declare.
            WriteString(DiBrowseNames.SerialNumber, data.SerialNumber);
            WriteString(DiBrowseNames.ManufacturerUri, data.ManufacturerUri);
            WriteString(DiBrowseNames.AssetId, data.AssetId);
            WriteString(DiBrowseNames.DeviceClass, data.DeviceClass);
            WriteLocalizedText(DiBrowseNames.Model, data.Model);
            WriteLocalizedText(DiBrowseNames.ComponentName, data.ComponentName);
            if (data.Location != null)
            {
                WriteProperty(
                    new QualifiedName(
                        MachineryBrowseNames.Location,
                        m_machineryNamespaceIndex),
                    Opc.Ua.Types.DataTypeIds.String,
                    Variant.From(data.Location));
            }
            return this;
        }

        public IMachineryEquipmentItemBuilder WithEquipmentLife(
            double remaining,
            double startValue,
            double limitValue = 0,
            EUInformation? engineeringUnits = null,
            ArrayOf<double> warningValues = default)
        {
            m_scope.EnsureMutable();
            var browseName = new QualifiedName(
                MachineryBrowseNames.EquipmentLife,
                m_machineryNamespaceIndex);
            LifetimeVariableState life =
                MachineryBuilderUtilities.FindChild<LifetimeVariableState>(
                    m_scope.Context,
                    State,
                    browseName) ??
                MachineryBuilderUtilities.AddComponentChild(
                    m_scope.Context,
                    State,
                    browseName,
                    static (ctx, parent, name) =>
                        ctx.CreateInstanceOfLifetimeVariableType(parent, name));

            life.WrappedValue = Variant.From(remaining);

            // StartValue and LimitValue are mandatory on LifetimeVariableType,
            // so CreateInstanceOfLifetimeVariableType has already built them
            // and there is no Add* helper to call.
            life.StartValue!.WrappedValue = Variant.From(startValue);
            life.LimitValue!.WrappedValue = Variant.From(limitValue);
            if (engineeringUnits != null)
            {
                life.EngineeringUnits!.Value = engineeringUnits;
            }
            if (!warningValues.IsNull && warningValues.Count > 0)
            {
                life.AddWarningValues(
                    m_scope.Context,
                    v => v.WrappedValue = Variant.From(warningValues));
            }
            m_scope.RecordFacet(MachineryFacet.EquipmentLife);
            return this;
        }

        internal void WriteEquipmentTypeId(string equipmentTypeId)
        {
            WriteProperty(
                new QualifiedName(
                    MachineryBrowseNames.MachineryEquipmentTypeId,
                    m_machineryNamespaceIndex),
                Opc.Ua.Types.DataTypeIds.String,
                Variant.From(equipmentTypeId));
        }

        private void WriteString(string browseName, string? value)
        {
            if (value == null)
            {
                return;
            }
            WriteProperty(
                new QualifiedName(browseName, m_diNamespaceIndex),
                Opc.Ua.Types.DataTypeIds.String,
                Variant.From(value));
        }

        private void WriteLocalizedText(string browseName, LocalizedText value)
        {
            if (value.IsNull)
            {
                return;
            }
            WriteProperty(
                new QualifiedName(browseName, m_diNamespaceIndex),
                Opc.Ua.Types.DataTypeIds.LocalizedText,
                Variant.From(value));
        }

        /// <summary>
        /// Writes an equipment property, materialising it when the placeholder
        /// factory did not — every property but
        /// <c>MachineryEquipmentTypeId</c> is optional on
        /// <c>&lt;MachineryEquipment&gt;</c>, so the factory creates only that
        /// one.
        /// </summary>
        private void WriteProperty(QualifiedName browseName, NodeId dataTypeId, Variant value)
        {
            m_scope.EnsureMutable();
            BaseVariableState? property =
                MachineryBuilderUtilities.FindChild<BaseVariableState>(
                    m_scope.Context,
                    State,
                    browseName);
            if (property == null)
            {
                property = CreateProperty(browseName, dataTypeId);
            }
            property.WrappedValue = value;
            property.ModellingRuleId = NodeId.Null;
        }

        private PropertyState CreateProperty(QualifiedName browseName, NodeId dataTypeId)
        {
            var property = new PropertyState(State)
            {
                SymbolicName = browseName.Name ?? string.Empty,
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name),
                TypeDefinitionId = Opc.Ua.Types.VariableTypeIds.PropertyType,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasProperty,
                DataType = dataTypeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate,
                ModellingRuleId = NodeId.Null
            };
            property.NodeId = m_scope.Context.NodeIdFactory!.New(m_scope.Context, property);
            State.AddChild(property);
            return property;
        }

        private readonly MachineryBuildScope m_scope;
        private readonly ushort m_diNamespaceIndex;
        private readonly ushort m_machineryNamespaceIndex;
    }

    /// <summary>
    /// Implements <see cref="IMachineryOperationCounterBuilder"/> over the
    /// OPC 40001-1 <c>OperationCounters</c> functional group, adding its
    /// optional <c>PowerOnDuration</c>, <c>OperationDuration</c> and
    /// <c>OperationCycleCounter</c> variables.
    /// </summary>
    internal sealed class MachineryOperationCounterBuilder : IMachineryOperationCounterBuilder
    {
        public MachineryOperationCounterBuilder(
            MachineryItemBlocks blocks,
            MachineryOperationCounterState state)
        {
            m_blocks = blocks;
            State = state;
        }

        public MachineryOperationCounterState State { get; }

        public IMachineryOperationCounterBuilder WithPowerOnDuration(double hours)
        {
            State.AddPowerOnDuration(m_blocks.Context, v => v.Value = hours);
            return this;
        }

        public IMachineryOperationCounterBuilder WithOperationDuration(double hours)
        {
            State.AddOperationDuration(m_blocks.Context, v => v.Value = hours);
            return this;
        }

        public IMachineryOperationCounterBuilder WithOperationCycleCounter(Variant value)
        {
            State.AddOperationCycleCounter(m_blocks.Context, v => v.WrappedValue = value);
            return this;
        }

        private readonly MachineryItemBlocks m_blocks;
    }

    /// <summary>
    /// Implements <see cref="IMachineryLifetimeCounterBuilder"/> over the
    /// OPC 40001-1 <c>LifetimeCounters</c> add-in, instantiating each Device
    /// Integration <c>LifetimeVariableType</c> variable from the
    /// <c>&lt;LifetimeVariable&gt;</c> placeholder.
    /// </summary>
    internal sealed class MachineryLifetimeCounterBuilder : IMachineryLifetimeCounterBuilder
    {
        public MachineryLifetimeCounterBuilder(
            MachineryItemBlocks blocks,
            MachineryLifetimeCounterState state)
        {
            m_blocks = blocks;
            State = state;
        }

        public MachineryLifetimeCounterState State { get; }

        public IMachineryLifetimeCounterBuilder AddLifetimeVariable(
            QualifiedName browseName,
            double startValue,
            double remaining,
            ArrayOf<double> warningValues = default,
            double limitValue = 0)
        {
            m_blocks.Scope.EnsureMutable();
            QualifiedName name = browseName.NamespaceIndex == 0
                ? new QualifiedName(
                    browseName.Name,
                    m_blocks.Scope.BuildContext.InstanceNamespaceIndex)
                : browseName;
            LifetimeVariableState variable =
                State.AddLifetimeVariable_Placeholder(m_blocks.Context, name);
            variable.ModellingRuleId = NodeId.Null;
            MachineryBuilderUtilities.AssignInstanceNodeIds(m_blocks.Context, variable);
            variable.WrappedValue = Variant.From(remaining);
            // StartValue and LimitValue are mandatory on LifetimeVariableType,
            // so the factory created them and there is no Add* helper.
            variable.StartValue!.WrappedValue = Variant.From(startValue);
            variable.LimitValue!.WrappedValue = Variant.From(limitValue);
            if (!warningValues.IsNull && warningValues.Count > 0)
            {
                variable.AddWarningValues(
                    m_blocks.Context,
                    v => v.WrappedValue = Variant.From(warningValues));
            }
            return this;
        }

        private readonly MachineryItemBlocks m_blocks;
    }
}
