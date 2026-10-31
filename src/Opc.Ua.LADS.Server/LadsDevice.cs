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
using Microsoft.Extensions.Logging;
using Opc.Ua.LADS.Server.StateMachines;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The handle of a LADS device: drives the device state machine, the Machinery building blocks and
    /// the operation counters, and owns the functional units and maintenance tasks.
    /// </summary>
    internal sealed class LadsDevice : ILadsDevice, IAsyncDisposable
    {
        /// <summary>
        /// Creates the device object under the DeviceSet. The node is attached when it is registered.
        /// </summary>
        public LadsDevice(LadsRuntime runtime, string name)
        {
            Runtime = runtime;
            ISystemContext context = runtime.Context;
            Node = LadsRuntime.Named(
                context.CreateInstanceOfLADSDeviceType(runtime.DeviceSet, runtime.InstanceName(name)),
                global::Opc.Ua.ReferenceTypeIds.HasComponent);
            if (Node.RevisionCounter != null)
            {
                Node.RevisionCounter.Value = 0;
            }
            LADSDeviceStateMachineState deviceState = Node.DeviceState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The device state machine is missing.");
            deviceState.AddGotoOperate(context).AddGotoSleep(context).AddGotoShutdown(context);
            DeviceState = new LadsStateMachine<LadsDeviceStateValue>(
                deviceState,
                context,
                LadsStateMaps.Device,
                LadsDeviceStateValue.Initialization);
            DeviceState.StateChanged += state =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, state);
                }
                UpdateItemState();
                StateChanged?.Invoke(state);
            };
            Runtime.Bind(deviceState.GotoOperate, (_, ct) => GotoOperateAsync(ct));
            Runtime.Bind(deviceState.GotoSleep, (_, ct) => GotoSleepAsync(ct));
            Runtime.Bind(deviceState.GotoShutdown, (_, ct) => GotoShutdownAsync(ct));

            Node.AddMachineryBuildingBlocks(context);
            FolderState blocks = Node.MachineryBuildingBlocks!;
            ushort machinery = runtime.MachineryNamespaceIndex;
            ushort di = runtime.IndexOf(global::Opc.Ua.Di.Server.DiNodeManager.DiNamespaceUri);

            MachineryItemState_StateMachineState itemState = AddBlock(
                blocks,
                Objects.LADSDeviceType_MachineryBuildingBlocks_MachineryItemState,
                context.CreateInstanceOfMachineryItemState_StateMachineType(
                    blocks,
                    new QualifiedName(global::Opc.Ua.Machinery.BrowseNames.MachineryItemState, machinery)));
            ItemStateMachine = new LadsStateMachine<MachineryItemStateValue>(
                itemState,
                context,
                LadsStateMaps.ItemState,
                MachineryItemStateValue.NotAvailable);

            LADSOperationModeStateMachineState operationMode = AddBlock(
                blocks,
                Objects.LADSDeviceType_MachineryBuildingBlocks_MachineryOperationMode,
                context.CreateInstanceOfLADSOperationModeStateMachineType(
                    blocks,
                    new QualifiedName(global::Opc.Ua.Machinery.BrowseNames.MachineryOperationMode, machinery)));
            operationMode.AddGotoProcessing(context).AddGotoMaintenance(context).AddGotoSetup(context);
            OperationModeMachine = new LadsStateMachine<MachineryOperationModeValue>(
                operationMode,
                context,
                LadsStateMaps.OperationMode,
                MachineryOperationModeValue.None);
            OperationModeMachine.StateChanged += mode =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, mode);
                }
                OperationModeChanged?.Invoke(mode);
            };
            Runtime.Bind(
                operationMode.GotoProcessing,
                (_, ct) => SetOperationModeAsync(MachineryOperationModeValue.Processing, ct));
            Runtime.Bind(
                operationMode.GotoMaintenance,
                (_, ct) => SetOperationModeAsync(MachineryOperationModeValue.Maintenance, ct));
            Runtime.Bind(
                operationMode.GotoSetup,
                (_, ct) => SetOperationModeAsync(MachineryOperationModeValue.Setup, ct));

            Counters = AddBlock(
                blocks,
                Objects.LADSDeviceType_MachineryBuildingBlocks_OperationCounters,
                context.CreateInstanceOfLADSOperationCountersType(
                    blocks,
                    new QualifiedName(global::Opc.Ua.Machinery.BrowseNames.OperationCounters, di)));
            if (Counters.OperationCycleCounter == null)
            {
                Counters.AddOperationCycleCounter(context);
            }
            if (Counters.OperationDuration == null)
            {
                Counters.AddOperationDuration(context);
            }
            if (Counters.PowerOnDuration == null)
            {
                Counters.AddPowerOnDuration(context);
            }
            Counters.OperationCycleCounter!.Value = Variant.From(0u);
            Counters.OperationDuration!.Value = 0;
            Counters.PowerOnDuration!.Value = 0;
            Counters.PowerOnDuration.OnSimpleReadValue = ReadPowerOnDuration;

            if (Node.Identification != null)
            {
                if (!blocks.ReferenceExists(
                    global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn,
                    false,
                    Node.Identification.NodeId))
                {
                    blocks.AddReference(
                        global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn,
                        false,
                        Node.Identification.NodeId);
                }
                if (!Node.Identification.ReferenceExists(
                    global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn,
                    true,
                    blocks.NodeId))
                {
                    Node.Identification.AddReference(
                        global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn,
                        true,
                        blocks.NodeId);
                }
            }
        }

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        public LadsRuntime Runtime { get; }

        /// <summary>
        /// Gets the device node.
        /// </summary>
        public LADSDeviceState Node { get; }

        /// <summary>
        /// Gets the device state machine.
        /// </summary>
        public LadsStateMachine<LadsDeviceStateValue> DeviceState { get; }

        /// <summary>
        /// Gets the Machinery item state machine.
        /// </summary>
        public LadsStateMachine<MachineryItemStateValue> ItemStateMachine { get; }

        /// <summary>
        /// Gets the Machinery operation mode state machine.
        /// </summary>
        public LadsStateMachine<MachineryOperationModeValue> OperationModeMachine { get; }

        /// <summary>
        /// Gets the operation counters.
        /// </summary>
        public LADSOperationCountersState Counters { get; }

        /// <summary>
        /// Gets the provider of the device.
        /// </summary>
        public ILadsDeviceProvider Provider => m_provider ?? Runtime.DeviceProvider;

        /// <summary>
        /// Gets whether the device is published in the address space.
        /// </summary>
        public bool IsPublished => Volatile.Read(ref m_published);

        /// <summary>
        /// Gets or sets the state the device enters when it is published.
        /// </summary>
        public LadsDeviceStateValue InitialDeviceState { get; set; } = LadsDeviceStateValue.Operate;

        /// <summary>
        /// Gets or sets the operation mode the device enters when it is published.
        /// </summary>
        public MachineryOperationModeValue InitialOperationMode { get; set; } =
            MachineryOperationModeValue.Processing;

        /// <inheritdoc/>
        public string Name => Node.BrowseName.Name ?? string.Empty;

        /// <inheritdoc/>
        public NodeId NodeId => Node.NodeId;

        /// <inheritdoc/>
        public LadsDeviceStateValue State => DeviceState.Current;

        /// <inheritdoc/>
        public MachineryOperationModeValue OperationMode => OperationModeMachine.Current;

        /// <inheritdoc/>
        public MachineryItemStateValue ItemState => ItemStateMachine.Current;

        /// <inheritdoc/>
        public ArrayOf<ILadsFunctionalUnit> FunctionalUnits
        {
            get
            {
                lock (m_lock)
                {
                    var units = new ILadsFunctionalUnit[m_units.Count];
                    for (int ii = 0; ii < units.Length; ii++)
                    {
                        units[ii] = m_units[ii];
                    }
                    return units;
                }
            }
        }

        /// <inheritdoc/>
        public ArrayOf<ILadsMaintenanceTask> MaintenanceTasks
        {
            get
            {
                lock (m_lock)
                {
                    var tasks = new ILadsMaintenanceTask[m_tasks.Count];
                    for (int ii = 0; ii < tasks.Length; ii++)
                    {
                        tasks[ii] = m_tasks[ii];
                    }
                    return tasks;
                }
            }
        }

        /// <inheritdoc/>
        public uint OperationCycleCounter => (uint)Volatile.Read(ref m_cycles);

        /// <inheritdoc/>
        public TimeSpan OperationDuration => TimeSpan.FromTicks(Interlocked.Read(ref m_operationTicks));

        /// <inheritdoc/>
        public TimeSpan PowerOnDuration
        {
            get
            {
                long started = Interlocked.Read(ref m_powerOnTimestamp);
                return started == 0 ? TimeSpan.Zero : Runtime.TimeProvider.GetElapsedTime(started);
            }
        }

        /// <inheritdoc/>
        public event Action<LadsDeviceStateValue>? StateChanged;

        /// <inheritdoc/>
        public event Action<MachineryOperationModeValue>? OperationModeChanged;

        /// <inheritdoc/>
        public ILadsFunctionalUnit GetFunctionalUnit(string name)
        {
            lock (m_lock)
            {
                foreach (LadsFunctionalUnit unit in m_units)
                {
                    if (unit.Name == name)
                    {
                        return unit;
                    }
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotFound,
                "The device '{0}' has no functional unit '{1}'.",
                Name,
                name);
        }

        /// <inheritdoc/>
        public ILadsMaintenanceTask GetMaintenanceTask(string name)
        {
            lock (m_lock)
            {
                foreach (LadsMaintenanceTask task in m_tasks)
                {
                    if (task.Name == name)
                    {
                        return task;
                    }
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotFound,
                "The device '{0}' has no maintenance task '{1}'.",
                Name,
                name);
        }

        /// <inheritdoc/>
        public ValueTask GotoOperateAsync(CancellationToken cancellationToken = default)
        {
            return ChangeStateAsync(LadsDeviceStateValue.Operate, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask GotoSleepAsync(CancellationToken cancellationToken = default)
        {
            return ChangeStateAsync(LadsDeviceStateValue.Sleep, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask GotoShutdownAsync(CancellationToken cancellationToken = default)
        {
            return ChangeStateAsync(LadsDeviceStateValue.Shutdown, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask SetOperationModeAsync(
            MachineryOperationModeValue mode,
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (OperationModeMachine.Current == mode)
                {
                    return;
                }
                if (!OperationModeMachine.CanTransitionTo(mode))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The device '{0}' cannot change its operation mode from {1} to {2}.",
                        Name,
                        OperationModeMachine.Current,
                        mode);
                }
                ServiceResult result = await Provider
                    .OnOperationModeChangingAsync(this, mode, cancellationToken)
                    .ConfigureAwait(false);
                Reject(result, "SetOperationMode");
                if (!OperationModeMachine.TransitionTo(mode))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The operation mode change of the device '{0}' was vetoed.",
                        Name);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void IncrementOperationCycles()
        {
            uint cycles = (uint)Interlocked.Increment(ref m_cycles);
            Runtime.Update(Counters.OperationCycleCounter, Variant.From(cycles));
        }

        /// <summary>
        /// Adds the duration of a program run to the operation duration.
        /// </summary>
        public void AddOperationDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return;
            }
            long ticks = Interlocked.Add(ref m_operationTicks, duration.Ticks);
            Runtime.Update(Counters.OperationDuration, Variant.From(TimeSpan.FromTicks(ticks).TotalMilliseconds));
        }

        /// <summary>
        /// Rejects a command unless the device is in the Operate state.
        /// </summary>
        /// <exception cref="ServiceResultException"><see cref="StatusCodes.BadInvalidState"/>.</exception>
        public void RequireOperate()
        {
            if (DeviceState.Current != LadsDeviceStateValue.Operate)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The device '{0}' is in the state {1}, not Operate.",
                    Name,
                    DeviceState.Current);
            }
        }

        /// <summary>
        /// Updates the item state after a functional unit changed its state.
        /// </summary>
        public void OnFunctionalUnitStateChanged()
        {
            UpdateItemState();
        }

        /// <summary>
        /// Adds a functional unit.
        /// </summary>
        public LadsFunctionalUnit AddFunctionalUnit(string name)
        {
            LadsRuntime.ValidateName(name, nameof(name));
            ISystemContext context = Runtime.Context;
            FunctionalUnitSetState set = Node.FunctionalUnitSet ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The functional unit set is missing.");
            if (Runtime.FindChild<BaseInstanceState>(set, name) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The device '{0}' already has a functional unit '{1}'.",
                    Name,
                    name);
            }
            FunctionalUnitObjectState node = LadsRuntime.Named(
                context.CreateInstanceOfFunctionalUnitType(set, Runtime.InstanceName(name)),
                global::Opc.Ua.ReferenceTypeIds.HasComponent);
            set.AddChild(node);
            var unit = new LadsFunctionalUnit(this, node);
            lock (m_lock)
            {
                m_units.Add(unit);
            }
            return unit;
        }

        /// <summary>
        /// Adds a maintenance task.
        /// </summary>
        public LadsMaintenanceTask AddMaintenanceTask(string name, LadsMaintenanceTaskOptions options)
        {
            LadsRuntime.ValidateName(name, nameof(name));
            ISystemContext context = Runtime.Context;
            if (Node.Maintenance == null)
            {
                Node.AddMaintenance(context);
            }
            MaintenanceSetState set = Node.Maintenance!;
            if (Runtime.FindChild<BaseInstanceState>(set, name) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The device '{0}' already has a maintenance task '{1}'.",
                    Name,
                    name);
            }
            MaintenanceTaskState node = LadsRuntime.Named(
                context.CreateInstanceOfMaintenanceTaskType(set, Runtime.InstanceName(name)),
                global::Opc.Ua.ReferenceTypeIds.HasComponent);
            set.AddChild(node);
            var task = new LadsMaintenanceTask(this, node, options);
            lock (m_lock)
            {
                m_tasks.Add(task);
            }
            return task;
        }

        /// <summary>
        /// Adds a component.
        /// </summary>
        public void AddComponent(string name, LadsIdentification identification)
        {
            LadsRuntime.ValidateName(name, nameof(name));
            ISystemContext context = Runtime.Context;
            if (Node.Components == null)
            {
                Node.AddComponents(context);
            }
            LADSComponentsState components = Node.Components!;
            if (Runtime.FindChild<BaseInstanceState>(components, name) != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The device '{0}' already has a component '{1}'.",
                    Name,
                    name);
            }
            LADSComponentState component = LadsRuntime.Named(
                context.CreateInstanceOfLADSComponentType(components, Runtime.InstanceName(name)),
                global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn);
            LadsIdentificationWriter.Apply(context, component, component.Identification, identification);
            components.AddChild(component);
        }

        /// <summary>
        /// Overrides the provider of the device.
        /// </summary>
        public void SetProvider(ILadsDeviceProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Moves the published device to its initial state and operation mode.
        /// </summary>
        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref m_powerOnTimestamp, Math.Max(1, Runtime.TimeProvider.GetTimestamp()));
            Volatile.Write(ref m_published, true);
            OperationModeMachine.SetState(InitialOperationMode);
            LadsDeviceStateValue initial = InitialDeviceState;
            if (initial != LadsDeviceStateValue.Initialization)
            {
                if (!DeviceState.CanTransitionTo(initial) || !DeviceState.TransitionTo(initial))
                {
                    DeviceState.SetState(initial);
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            LadsFunctionalUnit[] units;
            lock (m_lock)
            {
                units = [.. m_units];
            }
            foreach (LadsFunctionalUnit unit in units)
            {
                await unit.DisposeAsync().ConfigureAwait(false);
            }
            LadsMaintenanceTask[] tasks;
            lock (m_lock)
            {
                tasks = [.. m_tasks];
            }
            foreach (LadsMaintenanceTask task in tasks)
            {
                task.Dispose();
            }
            Volatile.Write(ref m_published, false);
            m_gate.Dispose();
        }

        /// <summary>
        /// Moves the device state machine to a target state after the provider accepted it.
        /// </summary>
        private async ValueTask ChangeStateAsync(LadsDeviceStateValue target, CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!DeviceState.CanTransitionTo(target))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The device '{0}' cannot move from {1} to {2}.",
                        Name,
                        DeviceState.Current,
                        target);
                }
                if (target is LadsDeviceStateValue.Sleep or LadsDeviceStateValue.Shutdown)
                {
                    foreach (ILadsFunctionalUnit unit in FunctionalUnits)
                    {
                        if (unit.State is not (LadsFunctionalStateValue.Stopped or LadsFunctionalStateValue.Aborted))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadInvalidState,
                                "The device '{0}' cannot move to {1} while the functional unit '{2}' is {3}.",
                                Name,
                                target,
                                unit.Name,
                                unit.State);
                        }
                    }
                }
                ServiceResult result = await Provider
                    .OnDeviceStateChangingAsync(this, target, cancellationToken)
                    .ConfigureAwait(false);
                Reject(result, "Goto" + target);
                if (!DeviceState.TransitionTo(target))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The state change of the device '{0}' was vetoed.",
                        Name);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Throws when a provider rejected a command.
        /// </summary>
        private void Reject(ServiceResult result, string command)
        {
            if (ServiceResult.IsBad(result))
            {
                Runtime.Logger.CommandRejected(Name, command, result.StatusCode);
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Derives the Machinery item state from the device and functional unit states.
        /// </summary>
        private void UpdateItemState()
        {
            MachineryItemStateValue target;
            switch (DeviceState.Current)
            {
                case LadsDeviceStateValue.Shutdown:
                    target = MachineryItemStateValue.OutOfService;
                    break;
                case LadsDeviceStateValue.Operate:
                    target = MachineryItemStateValue.NotExecuting;
                    foreach (ILadsFunctionalUnit unit in FunctionalUnits)
                    {
                        if (unit.State == LadsFunctionalStateValue.Running)
                        {
                            target = MachineryItemStateValue.Executing;
                            break;
                        }
                    }
                    break;
                default:
                    target = MachineryItemStateValue.NotAvailable;
                    break;
            }
            lock (m_itemLock)
            {
                if (ItemStateMachine.Current == target)
                {
                    return;
                }
                if (!ItemStateMachine.CanTransitionTo(target) || !ItemStateMachine.TransitionTo(target))
                {
                    ItemStateMachine.SetState(target);
                }
            }
        }

        /// <summary>
        /// Computes the power on duration when a client reads it.
        /// </summary>
        private ServiceResult ReadPowerOnDuration(ISystemContext context, NodeState node, ref Variant value)
        {
            value = Variant.From(PowerOnDuration.TotalMilliseconds);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Attaches a building block to the MachineryBuildingBlocks folder.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
        /// <summary>
        /// Adds a building block to the folder and points the device's <c>HasAddIn</c> reference,
        /// which the type declares next to the folder, at the instance instead of the declaration.
        /// </summary>
        private T AddBlock<T>(FolderState folder, uint declarationId, T block)
            where T : BaseInstanceState
        {
            block.DisplayName = new LocalizedText(block.BrowseName.Name);
            block.ReferenceTypeId = global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn;
            folder.AddChild(block);
            Node.RemoveReference(
                global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn,
                false,
                new NodeId(declarationId, Runtime.LadsNamespaceIndex));
            if (!Node.ReferenceExists(global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn, false, block.NodeId))
            {
                Node.AddReference(global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn, false, block.NodeId);
                block.AddReference(global::Opc.Ua.Types.ReferenceTypeIds.HasAddIn, true, Node.NodeId);
            }
            return block;
        }

        private readonly Lock m_lock = new();
        private readonly Lock m_itemLock = new();
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly List<LadsFunctionalUnit> m_units = [];
        private readonly List<LadsMaintenanceTask> m_tasks = [];
        private ILadsDeviceProvider? m_provider;
        private bool m_published;
        private int m_cycles;
        private long m_operationTicks;
        private long m_powerOnTimestamp;
    }
}
