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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.PADIM;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.Fluent;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Opens the <see cref="MachineryBuildScope"/> of a new machine below the
    /// build context's <c>Machines</c> folder and returns its
    /// <see cref="MachineBuilder{TState}"/> — for a plain
    /// <c>BaseObjectType</c> machine or for an adopted companion-specification
    /// instance.
    /// </summary>
    internal static class MachineBuilder
    {
        public static IMachineBuilder<BaseObjectState> CreatePlain(
            MachineryBuildContext context,
            QualifiedName browseName)
        {
            var scope = new MachineryBuildScope(context, context.MachinesFolder, browseName);
            try
            {
                var machine = new BaseObjectState(null)
                {
                    SymbolicName = browseName.Name ?? string.Empty,
                    BrowseName = browseName,
                    DisplayName = new LocalizedText(browseName.Name),
                    TypeDefinitionId = Opc.Ua.Types.ObjectTypeIds.BaseObjectType,
                    ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes,
                    EventNotifier = EventNotifiers.None
                };
                machine.NodeId = context.Context.NodeIdFactory!.New(context.Context, machine);
                return new MachineBuilder<BaseObjectState>(scope, machine);
            }
            catch
            {
                scope.Abort();
                throw;
            }
        }

        public static IMachineBuilder<TState> Adopt<TState>(
            MachineryBuildContext context,
            TState machine,
            QualifiedName browseName)
            where TState : BaseObjectState
        {
            var scope = new MachineryBuildScope(context, context.MachinesFolder, browseName);
            try
            {
                machine.SymbolicName = browseName.Name ?? string.Empty;
                machine.BrowseName = browseName;
                if (machine.DisplayName.IsNull)
                {
                    machine.DisplayName = new LocalizedText(browseName.Name);
                }
                machine.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes;
                machine.ModellingRuleId = NodeId.Null;
                if (machine.NodeId.IsNull)
                {
                    machine.NodeId = context.Context.NodeIdFactory!.New(context.Context, machine);
                }
                return new MachineBuilder<TState>(scope, machine);
            }
            catch
            {
                scope.Abort();
                throw;
            }
        }
    }

    /// <summary>
    /// Implements <see cref="IMachineBuilder{TState}"/>: stages the OPC 40001
    /// building blocks, process values, job management, energy and result
    /// management of one machine in its <see cref="MachineryBuildScope"/>, and
    /// registers the staged tree with the node manager in
    /// <see cref="BuildAsync(CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TState">
    /// The machine's state type — a plain <see cref="BaseObjectState"/>, or an
    /// adopted companion-specification instance.
    /// </typeparam>
    internal sealed class MachineBuilder<TState> :
        MachineryNodeBuilder<TState>,
        IMachineBuilder<TState>
        where TState : BaseObjectState
    {
        public MachineBuilder(MachineryBuildScope scope, TState machine)
            : base(scope, machine)
        {
            scope.AttachRoot(machine);
            m_blocks = new MachineryItemBlocks(scope, machine);
        }

        public IMachineBuilder<TState> WithIdentification(
            Action<MachineryIdentificationData> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddIdentification(asMachine: true, configure);
            return this;
        }

        public IMachineBuilder<TState> WithMonitoring(Action<IMonitoringBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddMonitoring(configure);
            return this;
        }

        public IMachineBuilder<TState> WithComponents(
            Action<IMachineComponentsBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "Components");
            MachineComponentsState components = MachineryBuilderUtilities.AddAddIn(
                Scope.Context,
                State,
                m_blocks.MachineryName(MachineryBrowseNames.Components),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfMachineComponentsType(parent, name));
            m_blocks.ReferenceBuildingBlock(components);
            configure(new MachineComponentsBuilder(Scope, components));
            return this;
        }

        public IMachineBuilder<TState> WithMachineryEquipment(
            Action<IMachineryEquipmentBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "MachineryEquipment");
            MachineryEquipmentFolderState folder = MachineryBuilderUtilities.AddAddIn(
                Scope.Context,
                State,
                m_blocks.MachineryName(MachineryBrowseNames.MachineryEquipment),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfMachineryEquipmentFolderType(parent, name));
            m_blocks.ReferenceBuildingBlock(folder);
            configure(new MachineryEquipmentBuilder(Scope, folder));
            return this;
        }

        public IMachineBuilder<TState> WithNotifications(
            Action<IMachineryNotificationsBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.BuildingBlocks, "Notifications");
            if (m_notifications != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The machine already declares a Notifications add-in.");
            }
            NotificationsState notifications = MachineryBuilderUtilities.AddAddIn(
                Scope.Context,
                State,
                m_blocks.MachineryName(MachineryBrowseNames.Notifications),
                static (ctx, parent, name) =>
                    ctx.CreateInstanceOfNotificationsType(parent, name));
            notifications.EventNotifier = EventNotifiers.SubscribeToEvents;
            m_blocks.ReferenceBuildingBlock(notifications);
            m_notifications = new MachineryNotificationsBuilder(Scope, notifications);
            configure?.Invoke(m_notifications);
            Scope.RecordFacet(MachineryFacet.Notifications);
            return this;
        }

        public IMachineBuilder<TState> WithOperationCounters(
            Action<IMachineryOperationCounterBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddOperationCounters(configure);
            return this;
        }

        public IMachineBuilder<TState> WithLifetimeCounters(
            Action<IMachineryLifetimeCounterBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_blocks.AddLifetimeCounters(configure);
            return this;
        }

        public IMachineBuilder<TState> WithProcessValue(
            QualifiedName browseName,
            Action<IProcessValueBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.ProcessValues, "ProcessValue");
            QualifiedName name = Qualify(browseName);
            if (!m_processValueNames.Add(name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The machine already declares a process value named '{0}'.",
                    name);
            }
            configure(ProcessValueBuilder.Create(Scope, State, name));
            return this;
        }

        public IMachineBuilder<TState> WithProcessValueDevice(
            QualifiedName browseName,
            Action<MachineryIdentificationData> configureIdentification)
        {
            if (configureIdentification == null)
            {
                throw new ArgumentNullException(nameof(configureIdentification));
            }
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.ProcessValues, "ProcessValueDevice");
            if (m_processValueDevice != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The machine already declares a process-value device object.");
            }

            QualifiedName name = Qualify(browseName);
            BaseObjectState device = MachineryBuilderUtilities.AddComponentChild(
                Scope.Context,
                State,
                name,
                static (_, parent, _) => new BaseObjectState(parent)
                {
                    TypeDefinitionId = Opc.Ua.Types.ObjectTypeIds.BaseObjectType
                });
            MachineryBuilderUtilities.AssignInstanceNodeIds(Scope.Context, device);

            // The nameplate is the same MachineryComponentIdentificationType a
            // machine component carries, so it goes through the same writer.
            var blocks = new MachineryItemBlocks(Scope, device);
            blocks.AddIdentification(asMachine: false, configureIdentification);

            ushort padimNamespaceIndex = MachineryBuilderUtilities.NamespaceIndex(
                Scope.Context,
                Opc.Ua.PADIM.Namespaces.PADIM);
            device.AddReference(
                Opc.Ua.Types.ReferenceTypeIds.HasInterface,
                false,
                new NodeId(Opc.Ua.PADIM.ObjectTypes.ISignalSetType, padimNamespaceIndex));

            SignalSetState signalSet = MachineryBuilderUtilities.AddComponentChild(
                Scope.Context,
                device,
                new QualifiedName(
                    Opc.Ua.PADIM.BrowseNames.SignalSet,
                    padimNamespaceIndex),
                static (ctx, parent, n) => ctx.CreateInstanceOfSignalSetType(parent, n));
            MachineryBuilderUtilities.AssignInstanceNodeIds(Scope.Context, signalSet);

            m_processValueDevice = device;
            m_processValueSignalSet = signalSet;

            // Linked once the whole machine is built, so the call order between
            // WithProcessValue and WithProcessValueDevice does not matter.
            Scope.PostRegistrationActions.Add(LinkProcessValueDeviceAsync);
            Scope.RecordFacet(MachineryFacet.ProcessValuesDeviceObject);
            Scope.RecordFacet(MachineryFacet.ProcessValuesSimpleDeviceInfo);
            return this;
        }

        /// <summary>
        /// Points the device object's <c>SignalSet</c> at every process value
        /// the machine declares.
        /// </summary>
        /// <remarks>
        /// A plain forward reference, not a child: OPC 40001-2 puts the process
        /// values on the machine, and a node has one place in the hierarchy.
        /// </remarks>
        private ValueTask LinkProcessValueDeviceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_processValueSignalSet == null)
            {
                return default;
            }
            foreach (QualifiedName processValueName in m_processValueNames)
            {
                if (State.FindChild(Scope.Context, processValueName) is not BaseInstanceState
                    processValue)
                {
                    continue;
                }
                m_processValueSignalSet.AddReference(
                    Opc.Ua.Types.ReferenceTypeIds.HasComponent,
                    false,
                    processValue.NodeId);
            }
            return default;
        }

        public IMachineBuilder<TState> WithJobManagement(
            Action<IJobManagementBuilder>? configure = null)
        {
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.Jobs, "JobManagement");
            if (m_jobManagement != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The machine already declares a JobManagement object.");
            }
            m_jobManagement = JobManagementBuilder.Create(Scope, State);

            // OPC 40001-3's own conformance unit is worded like the OPC 40001-1
            // building blocks: the JobManagement AddIn has to be reachable
            // through the organizer folder as well.
            m_blocks.ReferenceBuildingBlock(m_jobManagement.State);
            configure?.Invoke(m_jobManagement);
            return this;
        }

        public IMachineBuilder<TState> WithEnergy(Action<IMachineryEnergyBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.Energy, "Energy");

            // OPC 40001-4 extends the OPC 40001-1 Monitoring block rather than
            // adding one of its own, so the monitoring add-in and its
            // Consumption folder are materialised here when the machine does
            // not already publish them. Both calls are idempotent.
            configure(new MachineryEnergyBuilder(Scope, m_blocks.EnsureConsumption()));
            return this;
        }

        public IMachineBuilder<TState> WithResultManagement(
            Action<IResultManagementBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Scope.EnsureMutable();
            Scope.EnsurePart(MachineryParts.Result, "ResultManagement");
            if (m_results != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The machine already declares a ResultManagement object.");
            }
            m_results = ResultManagementBuilder.Create(Scope, State);
            configure(m_results);
            return this;
        }

        public new IMachineBuilder<TState> Configure(Action<TState, ISystemContext> configure)
        {
            base.Configure(configure);
            return this;
        }

        public async ValueTask<IMachineHandle<TState>> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            using IDisposable lease = MachineryBuildScope.AcquireBuildLease(Scope.BuildContext);
            MachineryBuilderUtilities.ClearModellingRules(Scope.Context, State);
            await Scope.RegisterAsync(cancellationToken).ConfigureAwait(false);
            return new MachineHandle<TState>(
                this,
                State,
                m_blocks.ItemState,
                m_blocks.OperationMode,
                m_results?.Publisher,
                m_notifications);
        }

        internal NotificationsState? NotificationsState => m_notifications?.State;

        private QualifiedName Qualify(QualifiedName browseName)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "A Machinery browse name must not be empty.");
            }
            return browseName.NamespaceIndex == 0
                ? new QualifiedName(browseName.Name, BuildContext.InstanceNamespaceIndex)
                : browseName;
        }

        private readonly MachineryItemBlocks m_blocks;
        private readonly HashSet<QualifiedName> m_processValueNames = [];
        private BaseObjectState? m_processValueDevice;
        private SignalSetState? m_processValueSignalSet;
        private MachineryNotificationsBuilder? m_notifications;
        private JobManagementBuilder? m_jobManagement;
        private ResultManagementBuilder? m_results;
    }

    /// <summary>
    /// Implements <see cref="IMachineHandle{TState}"/>: the runtime surface
    /// <see cref="MachineBuilder{TState}.BuildAsync(CancellationToken)"/>
    /// returns, carrying the registered state, its state-machine controllers
    /// and its result and notification publishers.
    /// </summary>
    /// <typeparam name="TState">The machine's state type.</typeparam>
    internal sealed class MachineHandle<TState> : IMachineHandle<TState>
        where TState : BaseObjectState
    {
        public MachineHandle(
            MachineBuilder<TState> builder,
            TState state,
            IMachineryItemStateController? itemState,
            IMachineryOperationModeController? operationMode,
            IMachineryResultPublisher? results,
            IMachineryNotificationPublisher? notifications)
        {
            m_builder = builder;
            State = state;
            ItemState = itemState;
            OperationMode = operationMode;
            Results = results;
            Notifications = notifications;
        }

        public TState State { get; }

        public NodeId NodeId => State.NodeId;

        public IMachineryItemStateController? ItemState { get; }

        public IMachineryOperationModeController? OperationMode { get; }

        public IMachineryResultPublisher? Results { get; }

        public IMachineryNotificationPublisher? Notifications { get; }

        public INodeBuilder<TState> AsNode()
        {
            return m_builder.AsNode();
        }

        private readonly MachineBuilder<TState> m_builder;
    }
}
