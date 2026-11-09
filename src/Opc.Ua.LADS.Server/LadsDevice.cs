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
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.LADS.Server.StateMachines;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.Fluent;

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
                LadsDeviceStateValue.Initialization,
                this);
            DeviceState.StateChangedInternal += state =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, state);
                }
                UpdateItemState();
            };
            DeviceState.StateChanged += state => StateChanged?.Invoke(state);
            DeviceState.StateChangedInternalAsync += async (state, cancellationToken) =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, state);
                }
                await UpdateItemStateAsync(cancellationToken).ConfigureAwait(false);
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
                MachineryItemStateValue.NotAvailable,
                this);

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
                MachineryOperationModeValue.None,
                this);
            OperationModeMachine.StateChangedInternal += mode =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, mode);
                }
                OperationModeChanged?.Invoke(mode);
            };
            OperationModeMachine.StateChangedAsync += (mode, _) =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Name, mode);
                }
                OperationModeChanged?.Invoke(mode);
                return default;
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

        /// <inheritdoc/>
        public IAssetHandle? Asset { get; private set; }

        /// <inheritdoc/>
        public NodeId LockNodeId => Node.Lock?.NodeId ?? NodeId.Null;

        /// <inheritdoc/>
        public bool RequiresReadinessConfirmation => Volatile.Read(ref m_requiresReadinessConfirmation);

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
                bool started = Volatile.Read(ref m_powerOnStarted);
                return TimeSpan.FromTicks(Interlocked.Read(ref m_restoredPowerOnTicks)) +
                    (started
                        ? Runtime.TimeProvider.GetElapsedTime(Interlocked.Read(ref m_powerOnTimestamp))
                        : TimeSpan.Zero);
            }
        }

        /// <inheritdoc/>
        public event Action<LadsDeviceStateValue>? StateChanged;

        /// <inheritdoc/>
        public event Action<MachineryOperationModeValue>? OperationModeChanged;

        /// <summary>
        /// Raised after an asynchronous item-state change requests the shared item-state notification gate.
        /// </summary>
        internal event Action? ItemStateNotificationWaitStarting;

        /// <summary>
        /// Gets whether failed storage compensation has quarantined the device.
        /// </summary>
        internal bool IsStorageQuarantined => Volatile.Read(ref m_storageFailure) != null;

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
            RequireStorageHealthy();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RequireStorageHealthy();
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
                RequireStorageHealthy();
                Reject(result, "SetOperationMode");
                if (!await OperationModeMachine.TransitionToAsync(mode, CancellationToken.None)
                    .ConfigureAwait(false))
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
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                uint cycles = (uint)Interlocked.Increment(ref m_cycles);
                LadsRuntime.AssignValue(Counters.OperationCycleCounter, Variant.From(cycles));
            }
            Counters.OperationCycleCounter?.ClearChangeMasks(Runtime.Context, false);
        }

        private async ValueTask IncrementOperationCyclesAsync()
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                uint cycles = (uint)Interlocked.Increment(ref m_cycles);
                LadsRuntime.AssignValue(Counters.OperationCycleCounter, Variant.From(cycles));
            }
            if (Counters.OperationCycleCounter is { } counter)
            {
                await counter.ClearChangeMasksAsync(Runtime.Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adds the duration of a program run to the operation duration.
        /// </summary>
        public void AddOperationDuration(TimeSpan duration)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                if (duration <= TimeSpan.Zero)
                {
                    return;
                }
                long ticks = Interlocked.Add(ref m_operationTicks, duration.Ticks);
                LadsRuntime.AssignValue(
                    Counters.OperationDuration, Variant.From(TimeSpan.FromTicks(ticks).TotalMilliseconds));
            }
            Counters.OperationDuration?.ClearChangeMasks(Runtime.Context, false);
        }

        private async ValueTask AddOperationDurationAsync(TimeSpan duration)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                if (duration <= TimeSpan.Zero)
                {
                    return;
                }
                long ticks = Interlocked.Add(ref m_operationTicks, duration.Ticks);
                LadsRuntime.AssignValue(
                    Counters.OperationDuration, Variant.From(TimeSpan.FromTicks(ticks).TotalMilliseconds));
            }
            if (Counters.OperationDuration is { } operationDuration)
            {
                await operationDuration.ClearChangeMasksAsync(Runtime.Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask SaveStoredDataAsync(CancellationToken cancellationToken = default)
        {
            RequireStorageHealthy();
            await m_storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RequireStorageHealthy();
                await Runtime.Store.SaveAsync(StoreIdentity, CaptureStoredData(), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_storeGate.Release();
            }
        }

        /// <summary>
        /// Saves prepared data and publishes nodes before committing in-memory records and counters.
        /// Other device checkpoints cannot observe a partly applied program-data change.
        /// </summary>
        internal async ValueTask CommitProgramDataAsync(
            LadsStoredFunctionalUnitData unit,
            bool completed,
            TimeSpan duration,
            Action commit,
            CancellationToken cancellationToken,
            Func<ValueTask>? publish = null,
            Func<ValueTask>? compensate = null,
            LadsFunctionalUnit? quarantineOwner = null,
            bool stateAdmissionGateHeld = false)
        {
            RequireStorageHealthy();
            await m_storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool storeGateHeld = true;
            try
            {
                RequireStorageHealthy();
                LadsStoredDeviceData previous = CaptureStoredData();
                LadsStoredDeviceData snapshot = CaptureStoredData(unit);
                snapshot.OperationCycles = completed
                    ? unchecked(snapshot.OperationCycles + 1) : snapshot.OperationCycles;
                snapshot.OperationDurationMilliseconds += duration.TotalMilliseconds;
                await Runtime.Store.SaveAsync(StoreIdentity, snapshot, cancellationToken).ConfigureAwait(false);
                try
                {
                    if (publish != null)
                    {
                        await publish().ConfigureAwait(false);
                    }
                }
                catch (Exception publicationFailure)
                {
                    var failures = new List<Exception> { publicationFailure };
                    if (compensate != null)
                    {
                        try
                        {
                            await compensate().ConfigureAwait(false);
                        }
                        catch (Exception compensationFailure)
                        {
                            failures.Add(compensationFailure);
                        }
                    }
                    try
                    {
                        await Runtime.Store.SaveAsync(StoreIdentity, previous, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception storageFailure)
                    {
                        failures.Add(storageFailure);
                    }
                    if (failures.Count > 1)
                    {
                        LadsFunctionalUnit? admittedStart = MarkStorageQuarantined(failures);
                        m_storeGate.Release();
                        storeGateHeld = false;
                        throw await QuarantineAsync(
                            failures, quarantineOwner, stateAdmissionGateHeld, admittedStart).ConfigureAwait(false);
                    }
                    throw;
                }
                commit();
                if (completed)
                {
                    await IncrementOperationCyclesAsync().ConfigureAwait(false);
                }
                await AddOperationDurationAsync(duration).ConfigureAwait(false);
            }
            finally
            {
                if (storeGateHeld)
                {
                    m_storeGate.Release();
                }
            }
        }

        /// <summary>
        /// Captures a device record, optionally substituting a prepared unit transaction.
        /// </summary>
        private LadsStoredDeviceData CaptureStoredData(LadsStoredFunctionalUnitData? replacement = null)
        {
            LadsFunctionalUnit[] units;
            LadsMaintenanceTask[] tasks;
            lock (m_lock)
            {
                units = [.. m_units];
                tasks = [.. m_tasks];
            }
            var storedUnits = new List<LadsStoredFunctionalUnitData>();
            foreach (LadsFunctionalUnit unit in units)
            {
                if (unit.ProgramManager is LadsProgramManager programs)
                {
                    storedUnits.Add(replacement != null && replacement.Name == unit.Name
                        ? replacement
                        : programs.CaptureStoredData());
                }
            }
            var maintenance = new LadsStoredMaintenanceData[tasks.Length];
            for (int index = 0; index < tasks.Length; index++)
            {
                maintenance[index] = tasks[index].CaptureStoredData();
            }
            return new LadsStoredDeviceData
            {
                Identity = StoreIdentity,
                FunctionalUnits = storedUnits.ToArray(),
                MaintenanceTasks = maintenance,
                OperationCycles = OperationCycleCounter,
                OperationDurationMilliseconds = OperationDuration.TotalMilliseconds,
                PowerOnDurationMilliseconds = PowerOnDuration.TotalMilliseconds
            };
        }

        /// <summary>
        /// Restores only stored records before this device is published.
        /// Interrupted runs remain records; no executor or hardware command is invoked.
        /// </summary>
        internal async ValueTask RestoreStoredDataAsync(CancellationToken cancellationToken)
        {
            LadsStoredDeviceData? stored = await Runtime.Store.LoadAsync(StoreIdentity, cancellationToken)
                .ConfigureAwait(false);
            if (stored == null)
            {
                return;
            }
            if (stored.SchemaVersion != 1 || !string.Equals(stored.Identity, StoreIdentity, StringComparison.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError, "The LADS store identity is invalid.");
            }
            Interlocked.Exchange(ref m_cycles, unchecked((int)stored.OperationCycles));
            Interlocked.Exchange(ref m_operationTicks, TimeSpan.FromMilliseconds(
                stored.OperationDurationMilliseconds).Ticks);
            Interlocked.Exchange(ref m_restoredPowerOnTicks, TimeSpan.FromMilliseconds(
                stored.PowerOnDurationMilliseconds).Ticks);
            Runtime.Update(Counters.OperationCycleCounter, Variant.From(stored.OperationCycles));
            Runtime.Update(Counters.OperationDuration, Variant.From(stored.OperationDurationMilliseconds));
            ArrayOf<LadsStoredFunctionalUnitData> units = stored.FunctionalUnits;
            for (int index = 0; index < units.Count; index++)
            {
                LadsStoredFunctionalUnitData data = units[index];
                LadsFunctionalUnit? unit;
                lock (m_lock)
                {
                    unit = m_units.Find(candidate => candidate.Name == data.Name);
                }
                if (unit == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Stored functional unit '{0}' is not configured.", data.Name);
                }
                await unit.EnsureProgramManager().RestoreStoredDataAsync(data, cancellationToken)
                    .ConfigureAwait(false);
                if (data.InterruptedRunId != null)
                {
                    Volatile.Write(ref m_requiresReadinessConfirmation, true);
                }
            }
            ArrayOf<LadsStoredMaintenanceData> tasks = stored.MaintenanceTasks;
            for (int index = 0; index < tasks.Count; index++)
            {
                LadsStoredMaintenanceData data = tasks[index];
                LadsMaintenanceTask? task;
                lock (m_lock)
                {
                    task = m_tasks.Find(candidate => candidate.Name == data.Name);
                }
                if (task == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Stored maintenance task '{0}' is not configured.", data.Name);
                }
                task.RestoreStoredData(data);
            }
        }

        /// <summary>
        /// Gets the stable data identity independently of session namespace indexes.
        /// </summary>
        internal string StoreIdentity => m_storeIdentity ??= Node.Identification?.ProductInstanceUri?.Value is
            { Length: > 0 } uri
            ? uri
            : (Runtime.NamespaceUris.GetString(Runtime.InstanceNamespaceIndex) ??
                throw new InvalidOperationException("The LADS instance namespace is not registered.")) + "#" + Name;

        /// <summary>
        /// Rejects a command unless the device is in the Operate state.
        /// </summary>
        /// <exception cref="ServiceResultException"><see cref="StatusCodes.BadInvalidState"/>.</exception>
        public void RequireOperate()
        {
            RequireStorageHealthy();
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
        /// Rejects mutations and checkpoints after an incomplete address-space/storage compensation.
        /// </summary>
        internal void RequireStorageHealthy()
        {
            if (Volatile.Read(ref m_storageFailure) is Exception failure)
            {
                throw new InvalidOperationException(
                    "The LADS device requires restart after a failed stored-data transaction.", failure);
            }
        }

        /// <summary>
        /// Confirms equipment readiness once for the recovered device and checkpoints acknowledgement before any start.
        /// </summary>
        internal async ValueTask ConfirmReadinessAsync(CancellationToken cancellationToken)
        {
            if (!RequiresReadinessConfirmation)
            {
                return;
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, Runtime.Stopping, m_readinessStopping.Token);
            cancellationToken = lifetime.Token;
            await m_readinessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RequireStorageHealthy();
                if (!RequiresReadinessConfirmation)
                {
                    return;
                }
                Func<ILadsDevice, CancellationToken, ValueTask<bool>>? confirm =
                    Runtime.Options.ConfirmInterruptedDeviceReadinessAsync;
                if (confirm == null || !await confirm(this, cancellationToken).ConfigureAwait(false))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The interrupted device '{0}' requires equipment-readiness confirmation before starting.",
                        Name);
                }
                cancellationToken.ThrowIfCancellationRequested();
                await m_storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RequireStorageHealthy();
                    LadsStoredDeviceData snapshot = CaptureStoredData();
                    for (int index = 0; index < snapshot.FunctionalUnits.Count; index++)
                    {
                        snapshot.FunctionalUnits[index].InterruptedRunId = null;
                    }
                    await Runtime.Store.SaveAsync(StoreIdentity, snapshot, cancellationToken).ConfigureAwait(false);
                    LadsFunctionalUnit[] units;
                    lock (m_lock)
                    {
                        units = [.. m_units];
                    }
                    foreach (LadsFunctionalUnit unit in units)
                    {
                        unit.ProgramManager?.AcknowledgeInterruptedExecution();
                    }
                    Volatile.Write(ref m_requiresReadinessConfirmation, false);
                }
                finally
                {
                    m_storeGate.Release();
                }
            }
            finally
            {
                m_readinessGate.Release();
            }
        }

        /// <summary>
        /// Rejects synchronous state writes after the device is published.
        /// </summary>
        internal void EnsureSynchronousStateWriteAllowed()
        {
            if (IsPublished)
            {
                throw new InvalidOperationException(
                    "Synchronous LADS state writes are only supported before device publication.");
            }
        }

        /// <summary>
        /// Completes a functional-unit start sequence while ordered against device Sleep and Shutdown.
        /// </summary>
        internal async ValueTask EnterFunctionalUnitAsync(
            LadsFunctionalUnit unit,
            CancellationToken cancellationToken,
            Func<ValueTask>? continueStarting = null)
        {
            await ConfirmReadinessAsync(cancellationToken).ConfigureAwait(false);
            await AcquireStateAdmissionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RegisterAdmittedStart(unit);
                await EnterFunctionalUnitWithAdmissionHeldAsync(unit, cancellationToken).ConfigureAwait(false);
                if (continueStarting != null)
                {
                    await continueStarting().ConfigureAwait(false);
                }
                RequireStorageHealthy();
            }
            catch (Exception failure) when (IsStorageQuarantined)
            {
                try
                {
                    await unit.ReconcileQuarantinedUnitAsync().ConfigureAwait(false);
                }
                catch (Exception reconciliationFailure)
                {
                    throw new AggregateException(
                        "The functional-unit start and quarantine reconciliation failed.",
                        failure,
                        reconciliationFailure);
                }
                throw;
            }
            finally
            {
                ReleaseStateAdmission(unit);
            }
        }

        /// <summary>
        /// Acquires the gate that orders functional-unit admission against device Sleep and Shutdown.
        /// </summary>
        internal async ValueTask AcquireStateAdmissionAsync(CancellationToken cancellationToken)
        {
            await m_stateAdmissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Records the functional unit whose start currently holds device state admission.
        /// </summary>
        internal void RegisterAdmittedStart(LadsFunctionalUnit unit)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                m_admittedStart = unit;
            }
        }

        /// <summary>
        /// Releases admission and clears the active-start marker as one quarantine ordering point.
        /// A quarantine that records failure first retains this start for reconciliation; a release
        /// that wins first makes the start's state visible before quarantine begins.
        /// </summary>
        internal void ReleaseStateAdmission(LadsFunctionalUnit unit)
        {
            lock (m_mutationLock)
            {
                m_stateAdmissionGate.Release();
                if (ReferenceEquals(m_admittedStart, unit))
                {
                    m_admittedStart = null;
                }
            }
        }

        /// <summary>
        /// Enters Running when the caller already holds the device state-admission gate.
        /// </summary>
        internal async ValueTask EnterFunctionalUnitWithAdmissionHeldAsync(
            LadsFunctionalUnit unit,
            CancellationToken cancellationToken)
        {
            RequireStorageHealthy();
            RequireOperate();
            await unit.EnterRunningAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Changes a machine's state after checking the device's retained storage failure.
        /// </summary>
        internal void SetMachineState(LadsStateMachine machine, uint stateId)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                machine.AssignState(stateId);
            }
        }

        /// <summary>
        /// Commits a guarded transition only while the device remains usable.
        /// </summary>
        internal bool TransitionMachine(LadsStateMachine machine, LadsTransitionDefinition transition)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                return machine.AssignTransition(transition);
            }
        }

        /// <summary>
        /// Sets a function's enabled value.
        /// </summary>
        internal void SetFunctionEnabled(LadsFunction function, bool enabled)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignEnabled(enabled);
            }
        }

        /// <summary>
        /// Sets a control function's normalized target value.
        /// </summary>
        internal void SetControlTarget(LadsControlFunction function, Variant value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                switch (function)
                {
                    case LadsAnalogControlFunction analog:
                        analog.AssignTarget(value);
                        break;
                    case LadsMultiModeAnalogControlFunction multiMode:
                        multiMode.AssignTarget(value);
                        break;
                    case LadsTwoStateDiscreteControlFunction twoState:
                        twoState.AssignTarget(value);
                        break;
                    case LadsMultiStateDiscreteControlFunction multiState:
                        multiState.AssignTarget(value);
                        break;
                    case LadsTimerFunction timer:
                        timer.AssignTarget(value);
                        break;
                    default:
                        throw new InvalidOperationException("The LADS control function has no target value.");
                }
            }
        }

        /// <summary>
        /// Sets a scalar sensor's measured and raw values.
        /// </summary>
        internal void UpdateSensor(LadsAnalogScalarSensorFunction sensor, double value, double raw)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                sensor.AssignReading(value, raw);
            }
        }

        /// <summary>
        /// Sets an array sensor's measured and raw values.
        /// </summary>
        internal void UpdateSensor(
            LadsAnalogArraySensorFunction sensor, ArrayOf<double> values, ArrayOf<double> raw)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                sensor.AssignReading(values, raw);
            }
        }

        /// <summary>
        /// Sets a two-state sensor's reading.
        /// </summary>
        internal void UpdateSensor(LadsTwoStateDiscreteSensorFunction sensor, bool value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                sensor.AssignReading(value);
            }
        }

        /// <summary>
        /// Sets a multi-state sensor's reading.
        /// </summary>
        internal void UpdateSensor(LadsMultiStateDiscreteSensorFunction sensor, uint value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                sensor.AssignReading(value);
            }
        }

        /// <summary>
        /// Sets an analog reading and integrates its totalizer using the supplied monotonic clock sample.
        /// </summary>
        internal void UpdateControlReading(
            LadsAnalogControlFunction function, double value, long timestamp, long frequency)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignReading(value, timestamp, frequency);
            }
        }

        /// <summary>
        /// Sets a two-state control function's reading.
        /// </summary>
        internal void UpdateControlReading(LadsTwoStateDiscreteControlFunction function, bool value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignReading(value);
            }
        }

        /// <summary>
        /// Sets a multi-state control function's reading.
        /// </summary>
        internal void UpdateControlReading(LadsMultiStateDiscreteControlFunction function, uint value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignReading(value);
            }
        }

        /// <summary>
        /// Sets a timer's reading.
        /// </summary>
        internal void UpdateControlReading(LadsTimerFunction function, double value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignReading(value);
            }
        }

        /// <summary>
        /// Resets an analog totalizer to the supplied monotonic clock sample.
        /// </summary>
        internal void ResetTotalizer(LadsAnalogControlFunction function, long timestamp, double value = 0)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignTotalizerReset(timestamp, value);
            }
        }

        /// <summary>
        /// Sets a multi-mode control function's selected mode.
        /// </summary>
        internal void SetControlMode(LadsMultiModeAnalogControlFunction function, uint mode)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                function.AssignMode(mode);
            }
        }

        /// <summary>
        /// Sets a function parameter, such as a compensation or composed target value.
        /// </summary>
        internal void SetFunctionParameter(BaseVariableState? parameter, Variant value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                LadsRuntime.AssignValue(parameter, value);
            }
            parameter?.ClearChangeMasks(Runtime.Context, false);
        }

        /// <summary>
        /// Sets a function parameter and awaits its state notifications.
        /// </summary>
        internal async ValueTask SetFunctionParameterAsync(BaseVariableState? parameter, Variant value)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                LadsRuntime.AssignValue(parameter, value);
            }
            if (parameter is { } variable)
            {
                await variable.ClearChangeMasksAsync(Runtime.Context, false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Records a completed maintenance task.
        /// </summary>
        internal void RecordMaintenanceCompletion(
            LadsMaintenanceTask task, DateTimeUtc completed, MaintenanceTaskResultEnum result)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                task.AssignCompletion(completed, result);
            }
        }

        /// <summary>
        /// Captures a maintenance record after its state and associated metadata have been assigned together.
        /// </summary>
        internal LadsStoredMaintenanceData CaptureMaintenanceData(LadsMaintenanceTask task)
        {
            lock (m_mutationLock)
            {
                return new LadsStoredMaintenanceData
                {
                    Name = task.Name,
                    State = task.State,
                    LastExecutionDate = task.LastExecutionDate,
                    LastResult = task.LastResult,
                    PlannedDate = task.Node.PlannedDate is { } plannedDate ? plannedDate.Value : default,
                    Comment = task.Node.Message is { } message ? message.Value : LocalizedText.Null
                };
            }
        }

        /// <summary>
        /// Updates a maintenance condition without reporting an event during the attribute writes.
        /// </summary>
        internal void SetMaintenanceCondition(LadsMaintenanceTask task, bool active, LocalizedText message)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                task.AssignCondition(Runtime.Context, active, message);
            }
        }

        /// <summary>
        /// Publishes an active program and its retained waiter before its executor is scheduled.
        /// </summary>
        internal void StartProgram(LadsProgramManager programs, LadsProgramManager.ActiveRunEntry active)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                programs.AssignActiveProgram(active);
            }
        }

        /// <summary>
        /// Schedules a program only if quarantine has not already rejected its start.
        /// </summary>
        internal Task ScheduleProgramExecution(Func<Task> execution)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                return Task.Run(execution, CancellationToken.None);
            }
        }

        /// <summary>
        /// Clears a program published before its start failed, including cleanup after quarantine.
        /// </summary>
        internal void ResetActiveProgram(LadsProgramManager programs)
        {
            lock (m_mutationLock)
            {
                programs.AssignActiveProgramReset();
            }
        }

        /// <summary>
        /// Publishes a program's reported step.
        /// </summary>
        internal void SetProgramStep(
            LadsProgramManager programs, LadsProgramRun? run, uint number, string name,
            double runtimeMilliseconds)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                programs.AssignStep(run, number, name, runtimeMilliseconds);
            }
        }

        /// <summary>
        /// Publishes a program's measured processing and pause durations.
        /// </summary>
        internal void SetProgramRuntime(
            LadsProgramManager programs, double runtime, double pause, double stepRuntime)
        {
            lock (m_mutationLock)
            {
                RequireStorageHealthy();
                programs.AssignRuntime(runtime, pause, stepRuntime);
            }
        }

        /// <summary>
        /// Marks a failed execution only while state writes remain admitted; quarantined cleanup still drains.
        /// </summary>
        internal async ValueTask RecordExecutionFailureAsync(LadsFunctionalUnit unit)
        {
            lock (m_mutationLock)
            {
                if (m_storageFailure != null)
                {
                    return;
                }
                unit.Functional.AssignState(LadsFunctionalStateValue.Aborted);
            }
            await unit.Functional.NotifyStateChangedAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Assigns the terminal state for a unit whose start was interrupted by storage quarantine.
        /// </summary>
        internal (bool RunningChanged, bool FunctionalChanged, List<Exception> Failures)
            AssignQuarantinedStartState(LadsFunctionalUnit unit)
        {
            var failures = new List<Exception>();
            bool runningChanged = false;
            bool functionalChanged = false;
            lock (m_mutationLock)
            {
                if (m_storageFailure == null ||
                    unit.Functional.Current is not (
                        LadsFunctionalStateValue.Running or LadsFunctionalStateValue.Aborted))
                {
                    return (runningChanged, functionalChanged, failures);
                }
                try
                {
                    if (unit.Running.Current != LadsRunningStateValue.Idle)
                    {
                        unit.Running.AssignState(LadsRunningStateValue.Idle);
                        runningChanged = true;
                    }
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
                try
                {
                    if (unit.Functional.Current != LadsFunctionalStateValue.Aborted)
                    {
                        unit.Functional.AssignState(LadsFunctionalStateValue.Aborted);
                        functionalChanged = true;
                    }
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            return (runningChanged, functionalChanged, failures);
        }

        /// <summary>
        /// Updates the item state after a functional unit changed its state.
        /// </summary>
        public void OnFunctionalUnitStateChanged()
        {
            UpdateItemState();
        }

        /// <summary>
        /// Updates the item state after a functional unit changed its state asynchronously.
        /// </summary>
        internal ValueTask OnFunctionalUnitStateChangedAsync(CancellationToken cancellationToken)
        {
            return UpdateItemStateAsync(cancellationToken);
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
        /// Selects the existing DI activity service and materializes its standard Lock child.
        /// </summary>
        internal void ConfigureLocking(ILockActivityService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }
            if (m_lockService != null)
            {
                throw new InvalidOperationException("DI locking is already configured for this LADS device.");
            }
            Node.AddLock(Runtime.Context);
            m_lockService = service;
        }

        /// <summary>
        /// Registers subtree request admission before the root is exposed through the node manager.
        /// </summary>
        internal async ValueTask RegisterLockingAsync(CancellationToken cancellationToken)
        {
            if (m_lockService != null)
            {
                m_lockRegistration = await Runtime.Manager.RegisterLockableElementAsync(
                    Node, m_lockService, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Selects the existing asset registry and defers composition until the device root is registered.
        /// </summary>
        internal void ConfigureAssetManagement(
            IAssetManagement assets, INodeBuilder<BaseObjectState> node, Action<IAssetBuilder> configure)
        {
            if (m_assetManagement != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState, "Asset management is already configured for this LADS device.");
            }
            m_assetManagement = assets;
            m_assetNode = node;
            m_configureAsset = configure;
        }

        /// <summary>
        /// Validates opted-in identity and shares the configurable AssetId between its two browse paths.
        /// </summary>
        internal void ValidateAssetManagementConfiguration()
        {
            if (m_assetManagement == null)
            {
                return;
            }
            if (!Uri.TryCreate(Node.Identification?.ProductInstanceUri?.Value, UriKind.Absolute, out _))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "LADS asset-management configuration requires a stable absolute ProductInstanceUri.");
            }
            if (Node.Identification is { } identification && Node.AssetId is { } assetId)
            {
                identification.AssetId = assetId;
                if (!assetId.ReferenceExists(
                    global::Opc.Ua.ReferenceTypeIds.HasProperty, true, identification.NodeId))
                {
                    assetId.AddReference(
                        global::Opc.Ua.ReferenceTypeIds.HasProperty, true, identification.NodeId);
                }
            }
        }

        /// <summary>
        /// Composes selected asset building blocks through the shared registry without changing the LADS object.
        /// </summary>
        internal async ValueTask RegisterAssetManagementAsync(CancellationToken cancellationToken)
        {
            if (m_assetManagement != null)
            {
                Asset = await m_assetManagement.RegisterAssetAsync(
                    m_assetNode!, m_configureAsset, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Withdraws asset discovery before a failed LADS registration removes its owning subtree.
        /// </summary>
        internal ValueTask UnregisterAssetManagementAsync()
        {
            return Asset?.UnregisterAsync(CancellationToken.None) ?? default;
        }

        /// <summary>
        /// Organizes the Operational FunctionalGroups of the functions once the nodes have NodeIds.
        /// </summary>
        internal void OrganizeOperationalGroups()
        {
            LadsFunctionalUnit[] units;
            lock (m_lock)
            {
                units = [.. m_units];
            }
            foreach (LadsFunctionalUnit unit in units)
            {
                unit.OrganizeOperationalGroups();
            }
        }

        /// <summary>
        /// Moves the published device to its initial state and operation mode.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await m_stateAdmissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Interlocked.Exchange(ref m_powerOnTimestamp, Runtime.TimeProvider.GetTimestamp());
                Volatile.Write(ref m_powerOnStarted, true);
                Volatile.Write(ref m_published, true);
                await OperationModeMachine.SetStateAsync(InitialOperationMode, CancellationToken.None)
                    .ConfigureAwait(false);
                LadsDeviceStateValue initial = InitialDeviceState;
                if (initial != LadsDeviceStateValue.Initialization &&
                    (!DeviceState.CanTransitionTo(initial) ||
                    !await DeviceState.TransitionToAsync(initial, CancellationToken.None).ConfigureAwait(false)))
                {
                    await DeviceState.SetStateAsync(initial, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                m_stateAdmissionGate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool>? completion = null;
            Task disposal;
            lock (m_lock)
            {
                if (m_disposeTask == null)
                {
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposeTask = completion.Task;
                }
                disposal = m_disposeTask;
            }
            if (completion != null)
            {
                _ = CompleteDisposalAsync(completion);
            }
            return new ValueTask(disposal);
        }

        /// <summary>
        /// Prevents checkpointing a device whose address-space registration did not finish.
        /// </summary>
        internal void MarkRegistrationFailed()
        {
            Volatile.Write(ref m_published, false);
        }

        /// <summary>
        /// Completes the cached disposal task without invoking owned providers under the device monitor.
        /// </summary>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (Exception failure)
            {
                completion.TrySetException(failure);
            }
        }

        /// <summary>
        /// Releases execution and bindings even when the final stored-data checkpoint fails.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            var failures = new List<Exception>();
            try
            {
                m_readinessStopping.Cancel();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            await m_readinessGate.WaitAsync().ConfigureAwait(false);
            m_readinessGate.Release();
            try
            {
                if (IsPublished)
                {
                    await SaveStoredDataAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            LadsFunctionalUnit[] units;
            lock (m_lock)
            {
                units = [.. m_units];
            }
            foreach (LadsFunctionalUnit unit in units)
            {
                try
                {
                    await unit.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            LadsMaintenanceTask[] tasks;
            lock (m_lock)
            {
                tasks = [.. m_tasks];
            }
            foreach (LadsMaintenanceTask task in tasks)
            {
                try
                {
                    task.Dispose();
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }
            try
            {
                await UnregisterAssetManagementAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            try
            {
                m_lockRegistration?.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            Volatile.Write(ref m_published, false);
            m_gate.Dispose();
            m_storeGate.Dispose();
            m_stateAdmissionGate.Dispose();
            m_itemStateGate.Dispose();
            m_readinessGate.Dispose();
            m_readinessStopping.Dispose();
            if (failures.Count > 0)
            {
                throw new AggregateException("The LADS device checkpoint or cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Moves the device state machine to a target state after the provider accepted it.
        /// </summary>
        private async ValueTask ChangeStateAsync(LadsDeviceStateValue target, CancellationToken cancellationToken)
        {
            RequireStorageHealthy();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool admissionGateHeld = false;
            try
            {
                await m_stateAdmissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                admissionGateHeld = true;
                RequireStorageHealthy();
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
                RequireStorageHealthy();
                Reject(result, "Goto" + target);
                if (!await DeviceState.TransitionToAsync(target, CancellationToken.None).ConfigureAwait(false))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The state change of the device '{0}' was vetoed.",
                        Name);
                }
            }
            finally
            {
                if (admissionGateHeld)
                {
                    m_stateAdmissionGate.Release();
                }
                m_gate.Release();
            }
        }

        /// <summary>
        /// Records the quarantine before the store gate is released and captures any admitted start.
        /// </summary>
        private LadsFunctionalUnit? MarkStorageQuarantined(List<Exception> failures)
        {
            lock (m_mutationLock)
            {
                Volatile.Write(ref m_storageFailure, new AggregateException(failures));
                return m_admittedStart;
            }
        }

        /// <summary>
        /// Cancels active runs when quarantine begins, drains an admitted start outside its unit gate,
        /// and publishes Shutdown only after that unit has reached Aborted/Idle.
        /// </summary>
        private async ValueTask<AggregateException> QuarantineAsync(
            List<Exception> failures,
            LadsFunctionalUnit? quarantineOwner,
            bool stateAdmissionGateHeld,
            LadsFunctionalUnit? admittedStart)
        {
            LadsFunctionalUnit[] units;
            lock (m_lock)
            {
                units = [.. m_units];
            }
            Task? admittedExecution = null;
            foreach (LadsFunctionalUnit unit in units)
            {
                try
                {
                    (Task? completion, Exception? cancellationFailure) =
                        unit.ProgramManager?.RequestQuarantineAbort() ?? (null, null);
                    if (cancellationFailure != null)
                    {
                        failures.Add(cancellationFailure);
                    }
                    if (ReferenceEquals(unit, admittedStart) && !ReferenceEquals(unit, quarantineOwner))
                    {
                        admittedExecution = completion;
                    }
                }
                catch (Exception cancellationFailure)
                {
                    failures.Add(cancellationFailure);
                }
            }

            bool admissionGateAcquired = false;
            if (!stateAdmissionGateHeld)
            {
                await m_stateAdmissionGate.WaitAsync().ConfigureAwait(false);
                admissionGateAcquired = true;
            }
            try
            {
                if (admittedExecution != null)
                {
                    try
                    {
                        await admittedExecution.ConfigureAwait(false);
                    }
                    catch (Exception executionFailure)
                    {
                        failures.Add(executionFailure);
                    }
                }
                var reconcile = new List<LadsFunctionalUnit>(2);
                if (admittedStart != null)
                {
                    reconcile.Add(admittedStart);
                }
                if (quarantineOwner != null && !reconcile.Contains(quarantineOwner))
                {
                    reconcile.Add(quarantineOwner);
                }
                foreach (LadsFunctionalUnit unit in reconcile)
                {
                    try
                    {
                        await unit.ReconcileQuarantinedUnitAsync().ConfigureAwait(false);
                    }
                    catch (Exception stateFailure)
                    {
                        failures.Add(stateFailure);
                    }
                }
            }
            finally
            {
                if (admissionGateAcquired)
                {
                    m_stateAdmissionGate.Release();
                }
            }
            await m_itemStateGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (m_mutationLock)
                {
                    try
                    {
                        ItemStateMachine.AssignState(MachineryItemStateValue.OutOfService);
                    }
                    catch (Exception stateFailure)
                    {
                        failures.Add(stateFailure);
                    }
                    try
                    {
                        DeviceState.AssignState(LadsDeviceStateValue.Shutdown);
                    }
                    catch (Exception stateFailure)
                    {
                        failures.Add(stateFailure);
                    }
                }
                try
                {
                    await ItemStateMachine.NotifyStateChangedAsync().ConfigureAwait(false);
                }
                catch (Exception stateFailure)
                {
                    failures.Add(stateFailure);
                }
            }
            finally
            {
                m_itemStateGate.Release();
            }
            try
            {
                await DeviceState.NotifyStateChangedAsync().ConfigureAwait(false);
            }
            catch (Exception stateFailure)
            {
                failures.Add(stateFailure);
            }
            var failure = new AggregateException(
                "LADS publication compensation failed; the device requires restart.", failures);
            Volatile.Write(ref m_storageFailure, failure);
            return failure;
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
            EnsureSynchronousStateWriteAllowed();
            MachineryItemStateValue target = GetItemStateTarget();
            if (ItemStateMachine.Current == target)
            {
                return;
            }
            if (!ItemStateMachine.CanTransitionTo(target) || !ItemStateMachine.TransitionTo(target))
            {
                ItemStateMachine.SetState(target);
            }
        }

        /// <summary>
        /// Derives the Machinery item state after an asynchronous device or functional-unit state change.
        /// </summary>
        private async ValueTask UpdateItemStateAsync(CancellationToken cancellationToken)
        {
            Task wait = m_itemStateGate.WaitAsync(cancellationToken);
            ItemStateNotificationWaitStarting?.Invoke();
            await wait.ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_storageFailure) != null)
                {
                    return;
                }
                MachineryItemStateValue target = GetItemStateTarget();
                if (ItemStateMachine.Current == target)
                {
                    return;
                }
                if (!ItemStateMachine.CanTransitionTo(target) ||
                    !await ItemStateMachine.TransitionToAsync(target, cancellationToken).ConfigureAwait(false))
                {
                    await ItemStateMachine.SetStateAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_itemStateGate.Release();
            }
        }

        /// <summary>
        /// Gets the Machinery item state derived from the device and functional-unit states.
        /// </summary>
        private MachineryItemStateValue GetItemStateTarget()
        {
            switch (DeviceState.Current)
            {
                case LadsDeviceStateValue.Shutdown:
                    return MachineryItemStateValue.OutOfService;
                case LadsDeviceStateValue.Operate:
                    foreach (ILadsFunctionalUnit unit in FunctionalUnits)
                    {
                        if (unit.State == LadsFunctionalStateValue.Running)
                        {
                            return MachineryItemStateValue.Executing;
                        }
                    }
                    return MachineryItemStateValue.NotExecuting;
                default:
                    return MachineryItemStateValue.NotAvailable;
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
        /// Adds a building block to the MachineryBuildingBlocks folder and points the device's
        /// <c>HasAddIn</c> reference, which the type declares next to the folder, at the instance
        /// instead of the declaration.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
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

        /// <summary>Orders local writes and the transition to an unusable device.</summary>
        private readonly Lock m_mutationLock = new();
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly SemaphoreSlim m_stateAdmissionGate = new(1, 1);
        private readonly SemaphoreSlim m_itemStateGate = new(1, 1);
        private readonly List<LadsFunctionalUnit> m_units = [];
        private readonly List<LadsMaintenanceTask> m_tasks = [];
        private LadsFunctionalUnit? m_admittedStart;
        private ILadsDeviceProvider? m_provider;

        /// <summary>The injected registry selected only for opted-in asset-management features.</summary>
        private IAssetManagement? m_assetManagement;

        /// <summary>The selected existing DI locking service; never owned by the device.</summary>
        private ILockActivityService? m_lockService;

        /// <summary>The device's UA request admission and session-close subscription.</summary>
        private IDisposable? m_lockRegistration;

        /// <summary>The existing LADS object viewed through its owner's fluent builder.</summary>
        private INodeBuilder<BaseObjectState>? m_assetNode;

        /// <summary>The selected asset building blocks, applied during registration.</summary>
        private Action<IAssetBuilder>? m_configureAsset;

        /// <summary>The gate serializing equipment-readiness confirmation across all device start paths.</summary>
        private readonly SemaphoreSlim m_readinessGate = new(1, 1);

        /// <summary>Cancels pending equipment confirmation before owned resources are released.</summary>
        private readonly CancellationTokenSource m_readinessStopping = new();

        /// <summary>Whether restored pending execution still requires equipment confirmation.</summary>
        private bool m_requiresReadinessConfirmation;

        private bool m_published;
        private int m_cycles;
        private long m_operationTicks;
        private long m_powerOnTimestamp;
        private bool m_powerOnStarted;
        private long m_restoredPowerOnTicks;
        private readonly SemaphoreSlim m_storeGate = new(1, 1);

        /// <summary>The key fixed before the first registration load.</summary>
        private string? m_storeIdentity;

        /// <summary>The transaction failure that prevents further mutations until restart.</summary>
        private Exception? m_storageFailure;

        /// <summary>The shared checkpoint and resource-release completion.</summary>
        private Task? m_disposeTask;
    }
}
