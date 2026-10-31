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

namespace Opc.Ua.LADS.Client
{
    public sealed partial class LadsClient
    {
        /// <summary>
        /// Reads a snapshot of a LADS device: identification, health, the device
        /// and Machinery state machines and the names of its functional units.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public async ValueTask<LadsDeviceSnapshot> ReadDeviceAsync(
            NodeId deviceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(deviceId, nameof(deviceId));
            string displayName = await ReadDisplayNameAsync(deviceId, cancellationToken).ConfigureAwait(false);
            LadsIdentification identification = await ReadIdentificationAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            var machines = new List<QualifiedName[]?>
            {
                new[] { Lads(BrowseNames.DeviceState) },
                MachineryPath(Opc.Ua.Machinery.BrowseNames.MachineryItemState),
                MachineryPath(Opc.Ua.Machinery.BrowseNames.MachineryOperationMode)
            };
            LadsStateSnapshot?[] states = await ReadStatesAsync(deviceId, machines, cancellationToken)
                .ConfigureAwait(false);
            DataValue[] health = await ReadPathsAsync(
                deviceId, [[Di(Opc.Ua.Di.BrowseNames.DeviceHealth)]], cancellationToken).ConfigureAwait(false);
            ArrayOf<LadsFunctionalUnit> units = await GetFunctionalUnitsAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);
            var unitNames = new string[units.Count];
            for (int ii = 0; ii < unitNames.Length; ii++)
            {
                unitNames[ii] = units[ii].Name;
            }
            return new LadsDeviceSnapshot(deviceId, displayName, identification)
            {
                DeviceState = states[0],
                MachineryItemState = states[1],
                OperationMode = states[2],
                DeviceHealth = AsVariant(health[0]),
                FunctionalUnitNames = unitNames.ToArrayOf()
            };
        }

        /// <summary>
        /// Reads the identification of a device, functional unit or component. A
        /// property of the <c>Identification</c> object takes precedence over the
        /// same property directly on the node.
        /// </summary>
        /// <param name="nodeId">The NodeId of the device, functional unit or component.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The identification; properties the server does not publish are <c>null</c>.</returns>
        public async ValueTask<LadsIdentification> ReadIdentificationAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(nodeId, nameof(nodeId));
            QualifiedName identification = Di(Opc.Ua.Di.BrowseNames.Identification);
            var paths = new List<QualifiedName[]>();
            var positions = new int[s_identificationNames.Length];
            for (int ii = 0; ii < s_identificationNames.Length; ii++)
            {
                (string namespaceUri, string name) = s_identificationNames[ii];
                if (TryName(namespaceUri, name) is not { } property)
                {
                    positions[ii] = -1;
                    continue;
                }
                positions[ii] = paths.Count;
                paths.Add([identification, property]);
                paths.Add([property]);
            }
            DataValue[] values = await ReadPathsAsync(nodeId, paths, cancellationToken).ConfigureAwait(false);
            string? Get(int index)
            {
                int position = positions[index];
                return position < 0 ? null : AsString(values[position]) ?? AsString(values[position + 1]);
            }
            return new LadsIdentification
            {
                Manufacturer = Get(0),
                Model = Get(1),
                SerialNumber = Get(2),
                SoftwareRevision = Get(3),
                HardwareRevision = Get(4),
                DeviceRevision = Get(5),
                AssetId = Get(6),
                ComponentName = Get(7),
                ProductInstanceUri = Get(8),
                ManufacturerUri = Get(9),
                ProductCode = Get(10),
                DeviceManual = Get(11),
                OperationalLocation = Get(12),
                HierarchicalLocation = Get(13)
            };
        }

        /// <summary>
        /// Reads a snapshot of a functional unit: its state machines, the progress
        /// of its active program and, optionally, the snapshots of its functions.
        /// </summary>
        /// <param name="functionalUnitId">The NodeId of the functional unit.</param>
        /// <param name="includeFunctions">Whether to read the functions too.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public async ValueTask<LadsFunctionalUnitSnapshot> ReadFunctionalUnitAsync(
            NodeId functionalUnitId,
            bool includeFunctions = true,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionalUnitId, nameof(functionalUnitId));
            string displayName = await ReadDisplayNameAsync(functionalUnitId, cancellationToken)
                .ConfigureAwait(false);
            QualifiedName state = Lads(BrowseNames.FunctionalUnitState);
            LadsStateSnapshot?[] states = await ReadStatesAsync(
                functionalUnitId,
                [[state], [state, Lads(BrowseNames.RunningStateMachine)]],
                cancellationToken).ConfigureAwait(false);
            NodeId programManager = await ResolveAsync(
                functionalUnitId, cancellationToken, Lads(BrowseNames.ProgramManager)).ConfigureAwait(false);
            LadsActiveProgram? activeProgram = programManager.IsNull
                ? null
                : await ReadActiveProgramAsync(functionalUnitId, cancellationToken).ConfigureAwait(false);
            var functions = new List<LadsFunctionSnapshot>();
            if (includeFunctions)
            {
                ArrayOf<LadsFunction> found = await GetFunctionsAsync(
                    functionalUnitId, true, cancellationToken).ConfigureAwait(false);
                for (int ii = 0; ii < found.Count; ii++)
                {
                    functions.Add(await ReadFunctionAsync(found[ii], cancellationToken).ConfigureAwait(false));
                }
            }
            return new LadsFunctionalUnitSnapshot(functionalUnitId, displayName)
            {
                State = states[0],
                RunningState = states[1],
                HasProgramManager = !programManager.IsNull,
                ActiveProgram = activeProgram,
                Functions = functions.ToArrayOf()
            };
        }

        /// <summary>
        /// Reads a snapshot of a function: its standard type and every variable of
        /// <see cref="LadsFunctionMembers.All"/> it exposes.
        /// </summary>
        /// <param name="functionId">The NodeId of the function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public async ValueTask<LadsFunctionSnapshot> ReadFunctionAsync(
            NodeId functionId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(functionId, nameof(functionId));
            string displayName = await ReadDisplayNameAsync(functionId, cancellationToken).ConfigureAwait(false);
            List<ReferenceDescription> types = await BrowseAsync(
                functionId, Ua.ReferenceTypeIds.HasTypeDefinition, NodeClass.ObjectType, cancellationToken)
                .ConfigureAwait(false);
            LadsFunctionKind kind = types.Count == 0
                ? LadsFunctionKind.Unknown
                : await ClassifyFunctionAsync(types[0].NodeId, cancellationToken).ConfigureAwait(false);
            ArrayOf<LadsFunctionValue> values = await ReadFunctionValuesAsync(functionId, cancellationToken)
                .ConfigureAwait(false);
            return new LadsFunctionSnapshot(functionId, displayName, kind, values);
        }

        /// <summary>
        /// Reads the current state of a state machine.
        /// </summary>
        /// <param name="stateMachineId">The NodeId of the state machine.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The state, or <c>null</c> when the node has no <c>CurrentState</c>.</returns>
        public async ValueTask<LadsStateSnapshot?> ReadStateAsync(
            NodeId stateMachineId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(stateMachineId, nameof(stateMachineId));
            LadsStateSnapshot?[] states = await ReadStatesAsync(stateMachineId, [[]], cancellationToken)
                .ConfigureAwait(false);
            return states[0];
        }

        /// <summary>
        /// Gets the maintenance tasks of a device's <c>Maintenance</c> set.
        /// </summary>
        /// <param name="deviceId">The NodeId of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The maintenance tasks.</returns>
        public async ValueTask<ArrayOf<LadsMaintenanceTask>> GetMaintenanceTasksAsync(
            NodeId deviceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(deviceId, nameof(deviceId));
            NodeId set = await ResolveAsync(deviceId, cancellationToken, Lads(BrowseNames.Maintenance))
                .ConfigureAwait(false);
            var tasks = new List<LadsMaintenanceTask>();
            foreach (ReferenceDescription reference in await BrowseAsync(set, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                NodeId taskId = ToNodeId(reference.NodeId);
                if (taskId.IsNull ||
                    !await IsTypeOfAsync(reference.TypeDefinition, ObjectTypeIds.MaintenanceTaskType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                tasks.Add(await ReadMaintenanceTaskAsync(
                    taskId, reference.BrowseName.Name ?? string.Empty, DisplayNameOf(reference), cancellationToken)
                    .ConfigureAwait(false));
            }
            return tasks.ToArrayOf();
        }

        /// <summary>
        /// Reads the <c>OperationCounters</c> of a device, functional unit or component.
        /// </summary>
        /// <param name="nodeId">The NodeId of the device, functional unit or component.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The counters, or <c>null</c> when the node has none.</returns>
        public async ValueTask<LadsOperationCounters?> ReadOperationCountersAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNull(nodeId, nameof(nodeId));
            NodeId counters = await ResolveAsync(
                nodeId, cancellationToken, Di(Opc.Ua.Di.BrowseNames.OperationCounters)).ConfigureAwait(false);
            if (counters.IsNull)
            {
                return null;
            }
            DataValue[] values = await ReadPathsAsync(
                counters,
                [
                    [Di(Opc.Ua.Di.BrowseNames.PowerOnDuration)],
                    [Di(Opc.Ua.Di.BrowseNames.OperationDuration)],
                    [Di(Opc.Ua.Di.BrowseNames.OperationCycleCounter)],
                    [Lads(BrowseNames.LifeTime)]
                ],
                cancellationToken).ConfigureAwait(false);
            return new LadsOperationCounters(counters)
            {
                PowerOnDuration = AsDuration(values[0]),
                OperationDuration = AsDuration(values[1]),
                OperationCycleCounter = AsUInt64(values[2]),
                LifeTime = AsVariant(values[3])
            };
        }

        /// <summary>
        /// Reads a snapshot of a function whose kind and name are known.
        /// </summary>
        internal async ValueTask<LadsFunctionSnapshot> ReadFunctionAsync(
            LadsFunction function,
            CancellationToken cancellationToken)
        {
            ArrayOf<LadsFunctionValue> values = await ReadFunctionValuesAsync(function.NodeId, cancellationToken)
                .ConfigureAwait(false);
            return new LadsFunctionSnapshot(function.NodeId, function.DisplayName, function.Kind, values);
        }

        /// <summary>
        /// Resolves the variables of <see cref="LadsFunctionMembers.All"/> a function exposes.
        /// </summary>
        internal async ValueTask<List<(string Name, NodeId NodeId)>> ResolveFunctionMembersAsync(
            NodeId functionId,
            CancellationToken cancellationToken)
        {
            ArrayOf<string> members = LadsFunctionMembers.All;
            var paths = new QualifiedName[members.Count][];
            for (int ii = 0; ii < paths.Length; ii++)
            {
                paths[ii] = MemberPath(members[ii]);
            }
            NodeId[] nodes = await ResolveAsync(functionId, paths, cancellationToken).ConfigureAwait(false);
            var resolved = new List<(string, NodeId)>();
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    resolved.Add((members[ii], nodes[ii]));
                }
            }
            NodeId controllerModeSet = await ResolveAsync(
                functionId, cancellationToken, Lads(BrowseNames.ControllerModeSet)).ConfigureAwait(false);
            if (!controllerModeSet.IsNull)
            {
                List<ReferenceDescription> modes = await BrowseAsync(
                    controllerModeSet, NodeClass.Object, cancellationToken).ConfigureAwait(false);
                var modePaths = new List<QualifiedName[]>(modes.Count * 2);
                var modeNames = new List<string>(modes.Count * 2);
                foreach (ReferenceDescription mode in modes)
                {
                    string modeName = mode.BrowseName.Name ?? string.Empty;
                    if (modeName.Length == 0)
                    {
                        continue;
                    }
                    modePaths.Add(
                    [
                        Lads(BrowseNames.ControllerModeSet),
                        mode.BrowseName,
                        Lads(BrowseNames.CurrentValue)
                    ]);
                    modeNames.Add($"{BrowseNames.ControllerModeSet}/{modeName}/{BrowseNames.CurrentValue}");
                    modePaths.Add(
                    [
                        Lads(BrowseNames.ControllerModeSet),
                        mode.BrowseName,
                        Lads(BrowseNames.TargetValue)
                    ]);
                    modeNames.Add($"{BrowseNames.ControllerModeSet}/{modeName}/{BrowseNames.TargetValue}");
                }
                NodeId[] modeNodes = await ResolveAsync(functionId, modePaths, cancellationToken).ConfigureAwait(false);
                for (int ii = 0; ii < modeNodes.Length; ii++)
                {
                    if (!modeNodes[ii].IsNull)
                    {
                        resolved.Add((modeNames[ii], modeNodes[ii]));
                    }
                }
            }
            return resolved;
        }

        /// <summary>
        /// Converts a relative path of LADS browse names to a browse path; the
        /// state machine members <c>CurrentState</c> and <c>Id</c> are in the
        /// OPC UA namespace.
        /// </summary>
        internal QualifiedName[] MemberPath(string member)
        {
            string[] segments = member.Split('/');
            var path = new QualifiedName[segments.Length];
            for (int ii = 0; ii < segments.Length; ii++)
            {
                path[ii] = segments[ii] is Ua.BrowseNames.CurrentState or Ua.BrowseNames.Id
                    ? new QualifiedName(segments[ii], 0)
                    : Lads(segments[ii]);
            }
            return path;
        }

        /// <summary>
        /// Reads the display name of a node, falling back to its NodeId.
        /// </summary>
        internal async ValueTask<string> ReadDisplayNameAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            DataValue[] values = await ReadAttributesAsync([nodeId], Attributes.DisplayName, cancellationToken)
                .ConfigureAwait(false);
            return AsString(values[0]) ?? nodeId.ToString();
        }

        /// <summary>
        /// Reads the current states of state machines below a node.
        /// </summary>
        /// <param name="start">The node the paths start at.</param>
        /// <param name="machines">The paths of the state machines; <c>null</c> to skip one.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>One state per path; <c>null</c> where the state machine is absent.</returns>
        internal async ValueTask<LadsStateSnapshot?[]> ReadStatesAsync(
            NodeId start,
            IReadOnlyList<QualifiedName[]?> machines,
            CancellationToken cancellationToken)
        {
            var currentState = new QualifiedName(Ua.BrowseNames.CurrentState, 0);
            var id = new QualifiedName(Ua.BrowseNames.Id, 0);
            var paths = new List<QualifiedName[]>();
            foreach (QualifiedName[]? machine in machines)
            {
                QualifiedName[] path = machine ?? [];
                paths.Add([.. path, currentState]);
                paths.Add([.. path, currentState, id]);
            }
            NodeId[] nodes = await ResolveAsync(start, paths, cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadValuesAsync(nodes, cancellationToken).ConfigureAwait(false);
            NodeId[] machineIds = await ResolveMachinesAsync(start, machines, cancellationToken).ConfigureAwait(false);
            var states = new LadsStateSnapshot?[machines.Count];
            for (int ii = 0; ii < states.Length; ii++)
            {
                if (machines[ii] is null || nodes[2 * ii].IsNull)
                {
                    continue;
                }
                states[ii] = new LadsStateSnapshot(
                    machineIds[ii],
                    AsString(values[2 * ii]) ?? string.Empty,
                    AsNodeId(values[(2 * ii) + 1]));
            }
            return states;
        }

        /// <summary>
        /// Gets a browse path in the Machinery namespace, or <c>null</c> when the
        /// server does not publish it.
        /// </summary>
        internal QualifiedName[]? MachineryPath(string name)
        {
            return TryName(Opc.Ua.Machinery.Namespaces.Machinery, name) is { } qualified ? [qualified] : null;
        }

        private async ValueTask<NodeId[]> ResolveMachinesAsync(
            NodeId start,
            IReadOnlyList<QualifiedName[]?> machines,
            CancellationToken cancellationToken)
        {
            var paths = new List<QualifiedName[]>();
            var positions = new int[machines.Count];
            for (int ii = 0; ii < machines.Count; ii++)
            {
                positions[ii] = -1;
                if (machines[ii] is { Length: > 0 } path)
                {
                    positions[ii] = paths.Count;
                    paths.Add(path);
                }
            }
            NodeId[] resolved = await ResolveAsync(start, paths, cancellationToken).ConfigureAwait(false);
            var results = new NodeId[machines.Count];
            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = positions[ii] >= 0
                    ? resolved[positions[ii]]
                    : machines[ii] is null ? NodeId.Null : start;
            }
            return results;
        }

        private async ValueTask<ArrayOf<LadsFunctionValue>> ReadFunctionValuesAsync(
            NodeId functionId,
            CancellationToken cancellationToken)
        {
            List<(string Name, NodeId NodeId)> members = await ResolveFunctionMembersAsync(
                functionId, cancellationToken).ConfigureAwait(false);
            var nodes = new NodeId[members.Count];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = members[ii].NodeId;
            }
            DataValue[] values = await ReadValuesAsync(nodes, cancellationToken).ConfigureAwait(false);
            var result = new LadsFunctionValue[members.Count];
            for (int ii = 0; ii < result.Length; ii++)
            {
                result[ii] = new LadsFunctionValue(members[ii].Name, members[ii].NodeId, values[ii]);
            }
            return result.ToArrayOf();
        }

        private async ValueTask<LadsMaintenanceTask> ReadMaintenanceTaskAsync(
            NodeId taskId,
            string name,
            string displayName,
            CancellationToken cancellationToken)
        {
            var paths = new List<QualifiedName[]>
            {
                new[] { Lads(BrowseNames.LastExecutionDate) },
                new[] { Lads(BrowseNames.RecurrencePeriod) },
                new[] { Lads(BrowseNames.LastOperatingTime) },
                new[] { Lads(BrowseNames.NextOperatingTime) },
                new[] { Lads(BrowseNames.LastOperatingCycles) },
                new[] { Lads(BrowseNames.NextOperatingCycles) }
            };
            QualifiedName[]? maintenanceState = null;
            if (TryName(Opc.Ua.AMB.Namespaces.AMB, Opc.Ua.AMB.BrowseNames.PlannedDate) is { } plannedDate)
            {
                paths.Add([plannedDate]);
                paths.Add([Amb(Opc.Ua.AMB.BrowseNames.EstimatedDowntime)]);
                maintenanceState = [Amb(Opc.Ua.AMB.BrowseNames.MaintenanceState)];
            }
            DataValue[] values = await ReadPathsAsync(taskId, paths, cancellationToken).ConfigureAwait(false);
            LadsStateSnapshot?[] state = await ReadStatesAsync(taskId, [maintenanceState], cancellationToken)
                .ConfigureAwait(false);
            return new LadsMaintenanceTask(taskId, name)
            {
                DisplayName = displayName,
                State = state[0],
                LastExecutionDate = AsDate(values[0]),
                RecurrencePeriod = AsDuration(values[1]),
                LastOperatingTime = AsDuration(values[2]),
                NextOperatingTime = AsDuration(values[3]),
                LastOperatingCycles = AsUInt32(values[4]),
                NextOperatingCycles = AsUInt32(values[5]),
                PlannedDate = values.Length > 6 ? AsDate(values[6]) : default,
                EstimatedDowntime = values.Length > 7 ? AsDuration(values[7]) : null
            };
        }

        private static readonly (string NamespaceUri, string Name)[] s_identificationNames =
        [
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.Manufacturer),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.Model),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.SerialNumber),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.SoftwareRevision),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.HardwareRevision),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.DeviceRevision),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.AssetId),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.ComponentName),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.ProductInstanceUri),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.ManufacturerUri),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.ProductCode),
            (Opc.Ua.Di.Namespaces.OpcUaDi, Opc.Ua.Di.BrowseNames.DeviceManual),
            (Opc.Ua.AMB.Namespaces.AMB, "OperationalLocation"),
            (Opc.Ua.AMB.Namespaces.AMB, "HierarchicalLocation")
        ];
    }
}
