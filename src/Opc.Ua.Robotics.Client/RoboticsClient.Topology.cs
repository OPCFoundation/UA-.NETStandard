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

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using RoboticsBrowseNames = Opc.Ua.Robotics.BrowseNames;
using UaBrowseNames = Opc.Ua.BrowseNames;

namespace Opc.Ua.Robotics.Client
{
    public sealed partial class RoboticsClient
    {
        /// <summary>
        /// Enumerates MotionDeviceSystem entries below the DI DeviceSet.
        /// </summary>
        public async IAsyncEnumerable<MotionDeviceSystemEntry> EnumerateMotionDeviceSystemsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int ns = Session.NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);
            if (ns < 0)
            {
                yield break;
            }
            var wantedType = new NodeId(RoboticsModel.MotionDeviceSystemType, (ushort)ns);
            ArrayOf<ReferenceDescription> references = await BrowseObjectsAsync(
                Topology.DeviceSetId,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                NodeId typeDefinition = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull && !typeDefinition.IsNull &&
                    await Session.NodeCache.IsTypeOfAsync(typeDefinition, wantedType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    yield return new MotionDeviceSystemEntry(
                        nodeId,
                        reference.BrowseName,
                        reference.DisplayName,
                        typeDefinition);
                }
            }
        }

        /// <summary>
        /// Reads a focused topology snapshot rooted at one MotionDeviceSystem.
        /// </summary>
        public async Task<RoboticsTopologySnapshot> ReadSystemAsync(
            NodeId system,
            CancellationToken cancellationToken = default)
        {
            MotionDeviceSystemSnapshot systemSnapshot = await ReadSystemNodeAsync(system, cancellationToken)
                .ConfigureAwait(false);
            var controllers = new List<ControllerSnapshot>();
            var motionDevices = new List<MotionDeviceSnapshot>();
            var axes = new List<AxisSnapshot>();
            var loads = new List<LoadSnapshot>();
            var powerTrains = new List<PowerTrainSnapshot>();
            var motors = new List<MotorSnapshot>();
            var gears = new List<GearSnapshot>();
            var drives = new List<DriveSnapshot>();
            var seenDrives = new HashSet<NodeId>();
            var safetyStates = new List<SafetyStateSnapshot>();
            var taskControls = new List<TaskControlSnapshot>();
            var taskModules = new List<TaskModuleSnapshot>();

            for (int ii = 0; ii < systemSnapshot.ControllerIds.Count; ii++)
            {
                ControllerSnapshot controller = await ReadControllerAsync(
                    systemSnapshot.ControllerIds[ii], cancellationToken).ConfigureAwait(false);
                controllers.Add(controller);
                for (int jj = 0; jj < controller.TaskControlIds.Count; jj++)
                {
                    TaskControlSnapshot taskControl = await ReadTaskControlAsync(
                        controller.TaskControlIds[jj], cancellationToken).ConfigureAwait(false);
                    taskControls.Add(taskControl);
                    for (int kk = 0; kk < taskControl.TaskModuleIds.Count; kk++)
                    {
                        taskModules.Add(await ReadTaskModuleAsync(
                            taskControl.TaskModuleIds[kk], cancellationToken).ConfigureAwait(false));
                    }
                }
            }
            for (int ii = 0; ii < systemSnapshot.MotionDeviceIds.Count; ii++)
            {
                MotionDeviceSnapshot motionDevice = await ReadMotionDeviceAsync(
                    systemSnapshot.MotionDeviceIds[ii], cancellationToken).ConfigureAwait(false);
                motionDevices.Add(motionDevice);
                if (!motionDevice.FlangeLoadId.IsNull)
                {
                    loads.Add(await ReadLoadAsync(motionDevice.FlangeLoadId, cancellationToken).ConfigureAwait(false));
                }
                for (int jj = 0; jj < motionDevice.AxisIds.Count; jj++)
                {
                    AxisSnapshot axis = await ReadAxisAsync(motionDevice.AxisIds[jj], cancellationToken)
                        .ConfigureAwait(false);
                    axes.Add(axis);
                    if (!axis.AdditionalLoadId.IsNull)
                    {
                        loads.Add(await ReadLoadAsync(axis.AdditionalLoadId, cancellationToken).ConfigureAwait(false));
                    }
                }
                for (int jj = 0; jj < motionDevice.PowerTrainIds.Count; jj++)
                {
                    PowerTrainSnapshot powerTrain = await ReadPowerTrainAsync(
                        motionDevice.PowerTrainIds[jj], cancellationToken).ConfigureAwait(false);
                    powerTrains.Add(powerTrain);
                    for (int kk = 0; kk < powerTrain.MotorIds.Count; kk++)
                    {
                        MotorSnapshot motor = await ReadMotorAsync(
                            powerTrain.MotorIds[kk], cancellationToken).ConfigureAwait(false);
                        motors.Add(motor);
                        ArrayOf<NodeId> driveIds = await ReadDriveIdsAsync(
                            powerTrain.MotorIds[kk], cancellationToken).ConfigureAwait(false);
                        for (int dd = 0; dd < driveIds.Count; dd++)
                        {
                            // One drive can drive several motors; report it once.
                            if (seenDrives.Add(driveIds[dd]))
                            {
                                drives.Add(await ReadDriveAsync(driveIds[dd], cancellationToken)
                                    .ConfigureAwait(false));
                            }
                        }
                    }
                    for (int kk = 0; kk < powerTrain.GearIds.Count; kk++)
                    {
                        gears.Add(await ReadGearAsync(powerTrain.GearIds[kk], cancellationToken)
                            .ConfigureAwait(false));
                    }
                }
            }
            for (int ii = 0; ii < systemSnapshot.SafetyStateIds.Count; ii++)
            {
                safetyStates.Add(await ReadSafetyStateAsync(
                    systemSnapshot.SafetyStateIds[ii], cancellationToken).ConfigureAwait(false));
            }

            var relationshipNodes = new List<NodeId> { system };
            relationshipNodes.AddRange(systemSnapshot.ControllerIds);
            relationshipNodes.AddRange(systemSnapshot.MotionDeviceIds);
            relationshipNodes.AddRange(systemSnapshot.SafetyStateIds);
            for (int ii = 0; ii < controllers.Count; ii++)
            {
                relationshipNodes.AddRange(controllers[ii].TaskControlIds);
            }
            for (int ii = 0; ii < motionDevices.Count; ii++)
            {
                relationshipNodes.AddRange(motionDevices[ii].AxisIds);
                relationshipNodes.AddRange(motionDevices[ii].PowerTrainIds);
            }
            for (int ii = 0; ii < powerTrains.Count; ii++)
            {
                relationshipNodes.AddRange(powerTrains[ii].MotorIds);
                relationshipNodes.AddRange(powerTrains[ii].GearIds);
            }
            for (int ii = 0; ii < drives.Count; ii++)
            {
                relationshipNodes.Add(drives[ii].Identification.NodeId);
            }

            return new RoboticsTopologySnapshot
            {
                Systems = [systemSnapshot],
                Controllers = controllers.ToArrayOf(),
                MotionDevices = motionDevices.ToArrayOf(),
                Axes = axes.ToArrayOf(),
                Loads = loads.ToArrayOf(),
                PowerTrains = powerTrains.ToArrayOf(),
                Motors = motors.ToArrayOf(),
                Gears = gears.ToArrayOf(),
                Drives = drives.ToArrayOf(),
                SafetyStates = safetyStates.ToArrayOf(),
                TaskControls = taskControls.ToArrayOf(),
                TaskModules = taskModules.ToArrayOf(),
                Relationships = await ReadRelationshipsAsync(relationshipNodes, cancellationToken).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Reads a Controller snapshot.
        /// </summary>
        public async Task<ControllerSnapshot> ReadControllerAsync(
            NodeId controller,
            CancellationToken cancellationToken = default)
        {
            ControllerTypeClient proxy = new(Session, controller, Telemetry);
            FolderTypeClient? taskControls = await proxy.GetTaskControlsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            FolderTypeClient? components = await proxy.GetComponentsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            SystemOperationTypeClient? operation = await proxy.GetSystemOperationAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            NodeId operationId = operation?.ObjectId ?? NodeId.Null;
            (NodeId currentStateId, RoboticsOperationState? currentState) = await ReadOperationStateAsync(
                operationId,
                RoboticsBrowseNames.SystemOperationStateMachine,
                s_systemOperationStates,
                cancellationToken).ConfigureAwait(false);
            return new ControllerSnapshot
            {
                Identification = await ReadIdentificationAsync(controller, cancellationToken).ConfigureAwait(false),
                TaskControlIds = taskControls == null
                    ? []
                    : await BrowseChildNodeIdsAsync(taskControls.ObjectId, cancellationToken).ConfigureAwait(false),
                ComponentIds = components == null
                    ? []
                    : await BrowseChildNodeIdsAsync(components.ObjectId, cancellationToken).ConfigureAwait(false),
                SystemOperationId = operationId,
                CurrentStateId = currentStateId,
                CurrentState = currentState
            };
        }

        /// <summary>
        /// Reads a MotionDevice snapshot.
        /// </summary>
        public async Task<MotionDeviceSnapshot> ReadMotionDeviceAsync(
            NodeId motionDevice,
            CancellationToken cancellationToken = default)
        {
            MotionDeviceTypeClient proxy = new(Session, motionDevice, Telemetry);
            FolderTypeClient? axes = await proxy.GetAxesAsync(Telemetry, cancellationToken).ConfigureAwait(false);
            FolderTypeClient? powerTrains = await proxy.GetPowerTrainsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            FolderTypeClient? additional = await proxy.GetAdditionalComponentsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            LoadTypeClient? flangeLoad = await proxy.GetFlangeLoadAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            NodeId category = await ResolveChildAsync(
                motionDevice, MemberPath(RoboticsBrowseNames.MotionDeviceCategory), cancellationToken)
                .ConfigureAwait(false);
            NodeId speed = await ResolveChildAsync(
                motionDevice, ParameterPath(RoboticsBrowseNames.SpeedOverride), cancellationToken)
                .ConfigureAwait(false);
            return new MotionDeviceSnapshot
            {
                Identification = await ReadIdentificationAsync(motionDevice, cancellationToken).ConfigureAwait(false),
                Category = await ReadEnumValueAsync<MotionDeviceCategoryEnumeration>(category, cancellationToken)
                    .ConfigureAwait(false),
                SpeedOverride = speed.IsNull ? DataValue.Null : await Session.ReadValueAsync(speed, cancellationToken)
                    .ConfigureAwait(false),
                SpeedOverrideId = speed,
                AxisIds = axes == null ? [] : await BrowseChildNodeIdsAsync(axes.ObjectId, cancellationToken)
                    .ConfigureAwait(false),
                PowerTrainIds = powerTrains == null
                    ? []
                    : await BrowseChildNodeIdsAsync(powerTrains.ObjectId, cancellationToken).ConfigureAwait(false),
                AdditionalComponentIds = additional == null
                    ? []
                    : await BrowseChildNodeIdsAsync(additional.ObjectId, cancellationToken).ConfigureAwait(false),
                FlangeLoadId = flangeLoad?.ObjectId ?? NodeId.Null
            };
        }

        /// <summary>
        /// Reads an Axis snapshot.
        /// </summary>
        public async Task<AxisSnapshot> ReadAxisAsync(
            NodeId axis,
            CancellationToken cancellationToken = default)
        {
            AxisTypeClient proxy = new(Session, axis, Telemetry);
            LoadTypeClient? load = await proxy.GetAdditionalLoadAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            NodeId motionProfile = await ResolveChildAsync(
                axis, MemberPath(RoboticsBrowseNames.MotionProfile), cancellationToken).ConfigureAwait(false);
            return new AxisSnapshot
            {
                Identification = await ReadIdentificationAsync(axis, cancellationToken).ConfigureAwait(false),
                MotionProfile = await ReadEnumValueAsync<AxisMotionProfileEnumeration>(
                    motionProfile, cancellationToken).ConfigureAwait(false),
                State = await ReadAxisStateAsync(axis, cancellationToken).ConfigureAwait(false),
                AdditionalLoadId = load?.ObjectId ?? NodeId.Null
            };
        }

        /// <summary>
        /// Reads a SafetyState snapshot.
        /// </summary>
        public async Task<SafetyStateSnapshot> ReadSafetyStateAsync(
            NodeId safetyState,
            CancellationToken cancellationToken = default)
        {
            SafetyStateTypeClient proxy = new(Session, safetyState, Telemetry);
            FolderTypeClient? emergency = await proxy.GetEmergencyStopFunctionsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            FolderTypeClient? protective = await proxy.GetProtectiveStopFunctionsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            (NodeId emergencyStopId, DataValue emergencyStop) = await ReadChildAsync(
                safetyState, ParameterPath(RoboticsBrowseNames.EmergencyStop), cancellationToken)
                .ConfigureAwait(false);
            (NodeId operationalModeId, DataValue operationalMode) = await ReadChildAsync(
                safetyState, ParameterPath(RoboticsBrowseNames.OperationalMode), cancellationToken)
                .ConfigureAwait(false);
            (NodeId protectiveStopId, DataValue protectiveStop) = await ReadChildAsync(
                safetyState, ParameterPath(RoboticsBrowseNames.ProtectiveStop), cancellationToken)
                .ConfigureAwait(false);
            return new SafetyStateSnapshot
            {
                Identification = await ReadIdentificationAsync(safetyState, cancellationToken).ConfigureAwait(false),
                EmergencyStop = emergencyStop,
                EmergencyStopId = emergencyStopId,
                OperationalMode = operationalMode,
                OperationalModeId = operationalModeId,
                ProtectiveStop = protectiveStop,
                ProtectiveStopId = protectiveStopId,
                EmergencyStopFunctions = emergency == null
                    ? []
                    : await ReadSafetyFunctionsAsync(emergency.ObjectId, cancellationToken).ConfigureAwait(false),
                ProtectiveStopFunctions = protective == null
                    ? []
                    : await ReadSafetyFunctionsAsync(protective.ObjectId, cancellationToken).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Reads a TaskControl snapshot.
        /// </summary>
        public async Task<TaskControlSnapshot> ReadTaskControlAsync(
            NodeId taskControl,
            CancellationToken cancellationToken = default)
        {
            TaskControlTypeClient proxy = new(Session, taskControl, Telemetry);
            TaskControlOperationTypeClient? operation = await proxy.GetTaskControlOperationAsync(
                Telemetry, cancellationToken).ConfigureAwait(false);
            FolderTypeClient? modules = await proxy.GetTaskModulesAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            (NodeId executionModeId, DataValue executionMode) = await ReadChildAsync(
                taskControl, ParameterPath(RoboticsBrowseNames.ExecutionMode), cancellationToken).ConfigureAwait(false);
            (NodeId programLoadedId, DataValue programLoaded) = await ReadChildAsync(
                taskControl, ParameterPath(RoboticsBrowseNames.TaskProgramLoaded), cancellationToken)
                .ConfigureAwait(false);
            (NodeId programNameId, DataValue programName) = await ReadChildAsync(
                taskControl, ParameterPath(RoboticsBrowseNames.TaskProgramName), cancellationToken)
                .ConfigureAwait(false);
            NodeId operationId = operation?.ObjectId ?? NodeId.Null;
            (NodeId currentStateId, RoboticsOperationState? currentState) = await ReadOperationStateAsync(
                operationId,
                RoboticsBrowseNames.TaskControlStateMachine,
                s_taskControlStates,
                cancellationToken).ConfigureAwait(false);
            return new TaskControlSnapshot
            {
                Identification = await ReadIdentificationAsync(taskControl, cancellationToken).ConfigureAwait(false),
                ExecutionMode = executionMode,
                ExecutionModeId = executionModeId,
                TaskProgramLoaded = programLoaded,
                TaskProgramLoadedId = programLoadedId,
                TaskProgramName = programName,
                TaskProgramNameId = programNameId,
                TaskControlOperationId = operationId,
                TaskModuleIds = modules == null
                    ? []
                    : await BrowseChildNodeIdsAsync(modules.ObjectId, cancellationToken).ConfigureAwait(false),
                CurrentStateId = currentStateId,
                CurrentState = currentState
            };
        }

        private async Task<LoadSnapshot> ReadLoadAsync(NodeId load, CancellationToken cancellationToken)
        {
            NodeId mass = await ResolveChildAsync(load, MemberPath(RoboticsBrowseNames.Mass), cancellationToken)
                .ConfigureAwait(false);
            return new LoadSnapshot
            {
                NodeId = load,
                Mass = mass.IsNull ? DataValue.Null : await Session.ReadValueAsync(mass, cancellationToken)
                    .ConfigureAwait(false),
                MassId = mass,
                CenterOfMass = await ReadChildValueAsync(
                    load, MemberPath(RoboticsBrowseNames.CenterOfMass), cancellationToken).ConfigureAwait(false),
                Inertia = await ReadChildValueAsync(load, MemberPath(RoboticsBrowseNames.Inertia), cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        private async Task<PowerTrainSnapshot> ReadPowerTrainAsync(
            NodeId powerTrain,
            CancellationToken cancellationToken)
        {
            // PowerTrainType declares its motors and gears as <MotorIdentifier> and
            // <GearIdentifier> placeholders: a server exposes any number of them
            // under their own browse names ("Motor1", ...), so they are found by
            // their TypeDefinition rather than by a browse name.
            ArrayOf<ReferenceDescription> components = await BrowseObjectsAsync(
                powerTrain, Opc.Ua.ReferenceTypeIds.HasComponent, cancellationToken).ConfigureAwait(false);
            return new PowerTrainSnapshot
            {
                Identification = await ReadIdentificationAsync(powerTrain, cancellationToken).ConfigureAwait(false),
                MotorIds = await FilterByTypeAsync(components, ObjectTypes.MotorType, cancellationToken)
                    .ConfigureAwait(false),
                GearIds = await FilterByTypeAsync(components, ObjectTypes.GearType, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Returns the targets of <paramref name="references"/> whose TypeDefinition is,
        /// or derives from, the Robotics ObjectType <paramref name="typeIdentifier"/>.
        /// </summary>
        private async Task<ArrayOf<NodeId>> FilterByTypeAsync(
            ArrayOf<ReferenceDescription> references,
            uint typeIdentifier,
            CancellationToken cancellationToken)
        {
            int ns = Session.NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);
            if (ns < 0)
            {
                return [];
            }
            var wantedType = new NodeId(typeIdentifier, (ushort)ns);
            var nodes = new List<NodeId>();
            for (int ii = 0; ii < references.Count; ii++)
            {
                NodeId typeDefinition = ExpandedNodeId.ToNodeId(
                    references[ii].TypeDefinition, Session.NamespaceUris);
                NodeId nodeId = ExpandedNodeId.ToNodeId(references[ii].NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull && !typeDefinition.IsNull &&
                    await Session.NodeCache.IsTypeOfAsync(typeDefinition, wantedType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    nodes.Add(nodeId);
                }
            }
            return nodes.ToArrayOf();
        }

        /// <summary>
        /// Returns the drives of a motor. MotorType declares them as the
        /// <c>&lt;DriveIdentifier&gt;</c> placeholder behind the <c>IsDrivenBy</c>
        /// reference (a HierarchicalReferences subtype, OPC 40010-1 8.5), so they
        /// are the forward IsDrivenBy targets.
        /// </summary>
        private async Task<ArrayOf<NodeId>> ReadDriveIdsAsync(
            NodeId motor,
            CancellationToken cancellationToken)
        {
            int ns = Session.NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);
            if (ns < 0)
            {
                return [];
            }
            ArrayOf<ReferenceDescription> references = await BrowseObjectsAsync(
                motor, new NodeId(ReferenceTypes.IsDrivenBy, (ushort)ns), cancellationToken).ConfigureAwait(false);
            var nodes = new List<NodeId>(references.Count);
            for (int ii = 0; ii < references.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(references[ii].NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    nodes.Add(nodeId);
                }
            }
            return nodes.ToArrayOf();
        }

        private async Task<MotorSnapshot> ReadMotorAsync(NodeId motor, CancellationToken cancellationToken)
        {
            QualifiedName[] temperature = ParameterPath(RoboticsBrowseNames.MotorTemperature);
            (ArrayOf<NodeId> nodes, ArrayOf<DataValue> values) = await ReadChildrenAsync(
                motor,
                [
                    temperature,
                    UaChildPath(temperature, UaBrowseNames.EngineeringUnits),
                    UaChildPath(temperature, UaBrowseNames.EURange),
                    ParameterPath(RoboticsBrowseNames.BrakeReleased),
                    ParameterPath(RoboticsBrowseNames.EffectiveLoadRate)
                ],
                cancellationToken).ConfigureAwait(false);
            return new MotorSnapshot
            {
                Identification = await ReadIdentificationAsync(motor, cancellationToken).ConfigureAwait(false),
                MotorTemperature = values[0],
                MotorTemperatureId = nodes[0],
                MotorTemperatureEngineering = new RoboticsEngineeringValue
                {
                    EngineeringUnits = ToStructure<EUInformation>(values[1]),
                    Range = ToStructure<Range>(values[2])
                },
                BrakeReleased = values[3],
                BrakeReleasedId = nodes[3],
                EffectiveLoadRate = values[4],
                EffectiveLoadRateId = nodes[4]
            };
        }

        private async Task<GearSnapshot> ReadGearAsync(NodeId gear, CancellationToken cancellationToken)
        {
            (NodeId pitchId, DataValue pitch) = await ReadChildAsync(
                gear, MemberPath(RoboticsBrowseNames.Pitch), cancellationToken).ConfigureAwait(false);
            return new GearSnapshot
            {
                Identification = await ReadIdentificationAsync(gear, cancellationToken).ConfigureAwait(false),
                Pitch = pitch,
                PitchId = pitchId
            };
        }

        private async Task<DriveSnapshot> ReadDriveAsync(NodeId drive, CancellationToken cancellationToken)
        {
            return new DriveSnapshot
            {
                Identification = await ReadIdentificationAsync(drive, cancellationToken).ConfigureAwait(false)
            };
        }

        private async Task<TaskModuleSnapshot> ReadTaskModuleAsync(
            NodeId taskModule,
            CancellationToken cancellationToken)
        {
            (NodeId isReferencedId, DataValue isReferenced) = await ReadChildAsync(
                taskModule, MemberPath(RoboticsBrowseNames.IsReferenced), cancellationToken).ConfigureAwait(false);
            return new TaskModuleSnapshot
            {
                NodeId = taskModule,
                Name = await ReadChildStringAsync(taskModule, MemberPath(RoboticsBrowseNames.Name), cancellationToken)
                    .ConfigureAwait(false),
                Version = await ReadChildStringAsync(
                    taskModule, MemberPath(RoboticsBrowseNames.Version), cancellationToken).ConfigureAwait(false),
                IsReferenced = isReferenced,
                IsReferencedId = isReferencedId
            };
        }

        /// <summary>
        /// Streams axis telemetry snapshots using the managed session default subscription.
        /// </summary>
        public IAsyncEnumerable<AxisStateSnapshot> ObserveAxisAsync(
            NodeId axis,
            CancellationToken cancellationToken = default)
        {
            return ObserveAxisAsync(axis, GetDefaultStreaming(Session), cancellationToken);
        }

        /// <summary>
        /// Streams safety-state snapshots using the managed session default subscription.
        /// </summary>
        public IAsyncEnumerable<SafetyStateSnapshot> ObserveSafetyAsync(
            NodeId safetyState,
            CancellationToken cancellationToken = default)
        {
            return ObserveSafetyAsync(safetyState, GetDefaultStreaming(Session), cancellationToken);
        }

        /// <summary>
        /// Streams axis telemetry snapshots over the supplied subscription.
        /// </summary>
        public async IAsyncEnumerable<AxisStateSnapshot> ObserveAxisAsync(
            NodeId axis,
            IStreamingSubscription streaming,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            List<NodeId> nodes = Resolved(await ResolveChildrenAsync(axis, AxisStatePaths(), cancellationToken)
                .ConfigureAwait(false));
            if (nodes.Count == 0)
            {
                yield break;
            }
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                nodes, null, cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadAxisStateAsync(axis, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Streams safety-state snapshots over the supplied subscription.
        /// </summary>
        public async IAsyncEnumerable<SafetyStateSnapshot> ObserveSafetyAsync(
            NodeId safetyState,
            IStreamingSubscription streaming,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            List<NodeId> nodes = Resolved(await ResolveChildrenAsync(
                safetyState,
                [
                    ParameterPath(RoboticsBrowseNames.EmergencyStop),
                    ParameterPath(RoboticsBrowseNames.OperationalMode),
                    ParameterPath(RoboticsBrowseNames.ProtectiveStop)
                ],
                cancellationToken).ConfigureAwait(false));
            if (nodes.Count == 0)
            {
                yield break;
            }
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                nodes, null, cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadSafetyStateAsync(safetyState, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<MotionDeviceSystemSnapshot> ReadSystemNodeAsync(
            NodeId system,
            CancellationToken cancellationToken)
        {
            MotionDeviceSystemTypeClient proxy = new(Session, system, Telemetry);
            FolderTypeClient? controllers = await proxy.GetControllersAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            FolderTypeClient? motionDevices = await proxy.GetMotionDevicesAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            FolderTypeClient? safetyStates = await proxy.GetSafetyStatesAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            return new MotionDeviceSystemSnapshot
            {
                Identification = await ReadIdentificationAsync(system, cancellationToken).ConfigureAwait(false),
                ControllerIds = controllers == null
                    ? []
                    : await BrowseChildNodeIdsAsync(controllers.ObjectId, cancellationToken).ConfigureAwait(false),
                MotionDeviceIds = motionDevices == null
                    ? []
                    : await BrowseChildNodeIdsAsync(motionDevices.ObjectId, cancellationToken).ConfigureAwait(false),
                SafetyStateIds = safetyStates == null
                    ? []
                    : await BrowseChildNodeIdsAsync(safetyStates.ObjectId, cancellationToken).ConfigureAwait(false)
            };
        }

        private async Task<AxisStateSnapshot> ReadAxisStateAsync(
            NodeId axis,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(axis, AxisStatePaths(), cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadValuesAsync(nodes.ToList(), cancellationToken)
                .ConfigureAwait(false);
            return new AxisStateSnapshot
            {
                ActualPosition = values.Count > 0 ? values[0] : DataValue.Null,
                ActualPositionId = nodes[0],
                ActualSpeed = values.Count > 1 ? values[1] : DataValue.Null,
                ActualSpeedId = nodes[1],
                ActualAcceleration = values.Count > 2 ? values[2] : DataValue.Null,
                ActualAccelerationId = nodes[2]
            };
        }

        private async Task<ArrayOf<SafetyFunctionSnapshot>> ReadSafetyFunctionsAsync(
            NodeId folder,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> functionIds = await BrowseChildNodeIdsAsync(folder, cancellationToken)
                .ConfigureAwait(false);
            var snapshots = new List<SafetyFunctionSnapshot>(functionIds.Count);
            for (int ii = 0; ii < functionIds.Count; ii++)
            {
                NodeId functionId = functionIds[ii];
                (NodeId activeId, DataValue active) = await ReadChildAsync(
                    functionId, MemberPath(RoboticsBrowseNames.Active), cancellationToken).ConfigureAwait(false);
                (NodeId enabledId, DataValue enabled) = await ReadChildAsync(
                    functionId, MemberPath(RoboticsBrowseNames.Enabled), cancellationToken).ConfigureAwait(false);
                snapshots.Add(new SafetyFunctionSnapshot
                {
                    NodeId = functionId,
                    Name = await ReadChildStringAsync(
                        functionId, MemberPath(RoboticsBrowseNames.Name), cancellationToken).ConfigureAwait(false),
                    Active = active,
                    ActiveId = activeId,
                    Enabled = enabled,
                    EnabledId = enabledId
                });
            }
            return snapshots.ToArrayOf();
        }

        private async Task<RoboticsComponentIdentification> ReadIdentificationAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> properties = await ResolveChildrenAsync(
                nodeId,
                [
                    DiPropertyPath(DiBrowseNames.ComponentName),
                    DiPropertyPath(DiBrowseNames.AssetId),
                    DiPropertyPath(DiBrowseNames.Manufacturer),
                    DiPropertyPath(DiBrowseNames.Model),
                    DiPropertyPath(DiBrowseNames.ProductCode),
                    DiPropertyPath(DiBrowseNames.SerialNumber),
                    DiPropertyPath(DiBrowseNames.DeviceManual),
                    DiPropertyPath(DiBrowseNames.HardwareRevision),
                    DiPropertyPath(DiBrowseNames.SoftwareRevision),
                    DiPropertyPath(DiBrowseNames.ManufacturerUri),
                    DiPropertyPath(DiBrowseNames.ProductInstanceUri)
                ],
                cancellationToken).ConfigureAwait(false);
            var readIds = new List<ReadValueId>
            {
                new() { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new() { NodeId = nodeId, AttributeId = Attributes.DisplayName }
            };
            for (int ii = 0; ii < properties.Count; ii++)
            {
                if (!properties[ii].IsNull)
                {
                    readIds.Add(new ReadValueId { NodeId = properties[ii], AttributeId = Attributes.Value });
                }
            }
            ArrayOf<ReadValueId> nodesToRead = readIds.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodesToRead);

            QualifiedName browseName = response.Results.Count > 0 &&
                response.Results[0].WrappedValue.TryGetValue(out QualifiedName qn)
                    ? qn
                    : QualifiedName.Null;
            LocalizedText componentName = response.Results.Count > 1 &&
                response.Results[1].WrappedValue.TryGetValue(out LocalizedText lt)
                    ? lt
                    : LocalizedText.Null;
            var values = new Dictionary<NodeId, DataValue>();
            int index = 2;
            for (int ii = 0; ii < properties.Count; ii++)
            {
                if (!properties[ii].IsNull && index < response.Results.Count)
                {
                    values[properties[ii]] = response.Results[index++];
                }
            }
            return new RoboticsComponentIdentification
            {
                NodeId = nodeId,
                BrowseName = browseName,
                ComponentName = ReadLocalized(properties, values, 0, componentName),
                AssetId = ReadString(properties, values, 1),
                Manufacturer = ReadLocalized(properties, values, 2, LocalizedText.Null),
                Model = ReadLocalized(properties, values, 3, LocalizedText.Null),
                ProductCode = ReadString(properties, values, 4),
                SerialNumber = ReadString(properties, values, 5),
                DeviceManual = ReadString(properties, values, 6),
                HardwareRevision = ReadString(properties, values, 7),
                SoftwareRevision = ReadString(properties, values, 8),
                ManufacturerUri = ReadString(properties, values, 9),
                ProductInstanceUri = ReadString(properties, values, 10)
            };
        }

        private async Task<RoboticsRelationshipSnapshot> ReadRelationshipsAsync(
            IReadOnlyList<NodeId> sources,
            CancellationToken cancellationToken)
        {
            int ns = Session.NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);
            if (ns < 0)
            {
                return new RoboticsRelationshipSnapshot();
            }
            var controls = new List<RoboticsRelationshipEntry>();
            var requires = new List<RoboticsRelationshipEntry>();
            var moves = new List<RoboticsRelationshipEntry>();
            var isDrivenBy = new List<RoboticsRelationshipEntry>();
            var hasSlave = new List<RoboticsRelationshipEntry>();
            var isConnectedTo = new List<RoboticsRelationshipEntry>();
            var hasSafetyStates = new List<RoboticsRelationshipEntry>();
            await AddRelationshipsAsync(sources, ReferenceTypes.Controls, controls, cancellationToken)
                .ConfigureAwait(false);
            await AddRelationshipsAsync(sources, ReferenceTypes.Requires, requires, cancellationToken)
                .ConfigureAwait(false);
            await AddRelationshipsAsync(sources, ReferenceTypes.Moves, moves, cancellationToken)
                .ConfigureAwait(false);
            await AddRelationshipsAsync(sources, ReferenceTypes.IsDrivenBy, isDrivenBy, cancellationToken)
                .ConfigureAwait(false);
            await AddRelationshipsAsync(sources, ReferenceTypes.HasSlave, hasSlave, cancellationToken)
                .ConfigureAwait(false);
            await AddRelationshipsAsync(sources, ReferenceTypes.IsConnectedTo, isConnectedTo, cancellationToken)
                .ConfigureAwait(false);
            await AddRelationshipsAsync(sources, ReferenceTypes.HasSafetyStates, hasSafetyStates, cancellationToken)
                .ConfigureAwait(false);
            return new RoboticsRelationshipSnapshot
            {
                Controls = controls.ToArrayOf(),
                Requires = requires.ToArrayOf(),
                Moves = moves.ToArrayOf(),
                IsDrivenBy = isDrivenBy.ToArrayOf(),
                HasSlave = hasSlave.ToArrayOf(),
                IsConnectedTo = isConnectedTo.ToArrayOf(),
                HasSafetyStates = hasSafetyStates.ToArrayOf()
            };
        }

        private async Task AddRelationshipsAsync(
            IReadOnlyList<NodeId> sources,
            uint referenceTypeIdentifier,
            List<RoboticsRelationshipEntry> entries,
            CancellationToken cancellationToken)
        {
            int ns = Session.NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);
            if (ns < 0)
            {
                return;
            }
            var referenceType = new NodeId(referenceTypeIdentifier, (ushort)ns);
            for (int ii = 0; ii < sources.Count; ii++)
            {
                NodeId source = sources[ii];
                if (source.IsNull)
                {
                    continue;
                }
                ArrayOf<ReferenceDescription> references = await BrowseReferencesAsync(
                    source, referenceType, BrowseDirection.Both, cancellationToken).ConfigureAwait(false);
                for (int jj = 0; jj < references.Count; jj++)
                {
                    ReferenceDescription reference = references[jj];
                    NodeId target = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    if (target.IsNull)
                    {
                        continue;
                    }
                    entries.Add(new RoboticsRelationshipEntry
                    {
                        SourceId = source,
                        ReferenceTypeId = referenceType,
                        TargetId = target,
                        IsInverse = !reference.IsForward
                    });
                }
            }
        }

        private async Task<ArrayOf<NodeId>> ResolveChildrenAsync(
            NodeId parent,
            QualifiedName[][] browseNames,
            CancellationToken cancellationToken)
        {
            var paths = new List<BrowsePath>(browseNames.Length);
            for (int ii = 0; ii < browseNames.Length; ii++)
            {
                paths.Add(CreateBrowsePath(parent, browseNames[ii]));
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                paths.ToArrayOf(),
                cancellationToken).ConfigureAwait(false);
            var results = new List<NodeId>(browseNames.Length);
            for (int ii = 0; ii < response.Results.Count; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                results.Add(StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0
                    ? ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris)
                    : NodeId.Null);
            }
            while (results.Count < browseNames.Length)
            {
                results.Add(NodeId.Null);
            }
            return results.ToArrayOf();
        }

        private async ValueTask<NodeId> ResolveChildAsync(
            NodeId parent,
            QualifiedName[] browseNames,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(parent, [browseNames], cancellationToken)
                .ConfigureAwait(false);
            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        private static BrowsePath CreateBrowsePath(NodeId parent, QualifiedName[] browseNames)
        {
            var elements = new RelativePathElement[browseNames.Length];
            for (int ii = 0; ii < browseNames.Length; ii++)
            {
                elements[ii] = new RelativePathElement
                {
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = browseNames[ii]
                };
            }
            return new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
            };
        }

        /// <summary>
        /// The path of a DI identification property (<c>Manufacturer</c>,
        /// <c>SerialNumber</c>, ...), which hangs directly below its component.
        /// </summary>
        private QualifiedName[] DiPropertyPath(string diBrowseName)
        {
            return [new QualifiedName(diBrowseName, NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi))];
        }

        /// <summary>
        /// The path of a Robotics member that hangs directly below its component
        /// (<c>MotionProfile</c>, <c>Pitch</c>, <c>Name</c>, ...).
        /// </summary>
        private QualifiedName[] MemberPath(string roboticsBrowseName)
        {
            return
            [
                new QualifiedName(roboticsBrowseName, NamespaceIndexOf(global::Opc.Ua.Robotics.Namespaces.Robotics))
            ];
        }

        /// <summary>
        /// The path of a process value (<c>ActualPosition</c>, <c>EmergencyStop</c>,
        /// <c>TaskProgramName</c>, ...), which OPC 40010-1 places below the DI
        /// <c>ParameterSet</c> of its component.
        /// </summary>
        private QualifiedName[] ParameterPath(string roboticsBrowseName)
        {
            return
            [
                new QualifiedName(DiBrowseNames.ParameterSet, NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi)),
                new QualifiedName(roboticsBrowseName, NamespaceIndexOf(global::Opc.Ua.Robotics.Namespaces.Robotics))
            ];
        }

        private QualifiedName[][] AxisStatePaths()
        {
            return
            [
                ParameterPath(RoboticsBrowseNames.ActualPosition),
                ParameterPath(RoboticsBrowseNames.ActualSpeed),
                ParameterPath(RoboticsBrowseNames.ActualAcceleration)
            ];
        }

        /// <summary>
        /// The path of a child with a standard (namespace 0) browse name, such as
        /// <c>EngineeringUnits</c>, <c>EURange</c> or <c>CurrentState</c>, below the
        /// node at <paramref name="path"/>.
        /// </summary>
        private static QualifiedName[] UaChildPath(QualifiedName[] path, string browseName)
        {
            return [.. path, new QualifiedName(browseName)];
        }

        /// <summary>
        /// The session's index of a namespace; an index no server uses when the
        /// namespace is unknown, so paths through it resolve to nothing.
        /// </summary>
        private ushort NamespaceIndexOf(string namespaceUri)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            return index < 0 ? ushort.MaxValue : (ushort)index;
        }

        private static List<NodeId> Resolved(ArrayOf<NodeId> nodes)
        {
            var resolved = new List<NodeId>(nodes.Count);
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    resolved.Add(nodes[ii]);
                }
            }
            return resolved;
        }

        private async Task<DataValue> ReadChildValueAsync(
            NodeId parent,
            QualifiedName[] browseNames,
            CancellationToken cancellationToken)
        {
            (_, DataValue value) = await ReadChildAsync(parent, browseNames, cancellationToken).ConfigureAwait(false);
            return value;
        }

        /// <summary>
        /// Reads the variable at <paramref name="browseNames"/> below
        /// <paramref name="parent"/> together with its NodeId; both are null when the
        /// path does not resolve.
        /// </summary>
        private async Task<(NodeId NodeId, DataValue Value)> ReadChildAsync(
            NodeId parent,
            QualifiedName[] browseNames,
            CancellationToken cancellationToken)
        {
            NodeId nodeId = await ResolveChildAsync(parent, browseNames, cancellationToken).ConfigureAwait(false);
            return nodeId.IsNull
                ? (nodeId, DataValue.Null)
                : (nodeId, await Session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Resolves several paths below <paramref name="parent"/> in one request and
        /// reads the variables found in another. A path that does not resolve has a
        /// null NodeId and reads as <see cref="DataValue.Null"/>; a failed read keeps
        /// the status the server returned.
        /// </summary>
        private async Task<(ArrayOf<NodeId> NodeIds, ArrayOf<DataValue> Values)> ReadChildrenAsync(
            NodeId parent,
            QualifiedName[][] browseNames,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(parent, browseNames, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<DataValue> read = await ReadValuesAsync(Resolved(nodes), cancellationToken)
                .ConfigureAwait(false);
            var values = new DataValue[nodes.Count];
            int next = 0;
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                values[ii] = nodes[ii].IsNull || next >= read.Count ? DataValue.Null : read[next++];
            }
            return (nodes, values.ToArrayOf());
        }

        /// <summary>
        /// Reads the <c>CurrentState</c> variable of the operation state machine
        /// <paramref name="stateMachineBrowseName"/> below <paramref name="operation"/>.
        /// The state is told by the NodeId in <c>CurrentState/Id</c>, whatever language
        /// the server localizes <c>CurrentState</c> to: the Idle, Ready or Executing
        /// state the Robotics model declares on the machine type
        /// (<paramref name="typeStates"/>), or a state object the server instantiated
        /// below the machine. The state name is used only when the server exposes no
        /// <c>CurrentState/Id</c>; an Id that is unreadable or names another state
        /// yields no state, as do a missing machine and a failed <c>CurrentState</c>
        /// read, so the rest of the snapshot is still returned.
        /// </summary>
        private async Task<(NodeId CurrentStateId, RoboticsOperationState? CurrentState)> ReadOperationStateAsync(
            NodeId operation,
            string stateMachineBrowseName,
            OperationStateIds typeStates,
            CancellationToken cancellationToken)
        {
            if (operation.IsNull)
            {
                return (NodeId.Null, null);
            }
            QualifiedName[] machine = MemberPath(stateMachineBrowseName);
            QualifiedName[] currentState = UaChildPath(machine, UaBrowseNames.CurrentState);
            (ArrayOf<NodeId> nodes, ArrayOf<DataValue> values) = await ReadChildrenAsync(
                operation,
                [currentState, UaChildPath(currentState, UaBrowseNames.Id)],
                cancellationToken).ConfigureAwait(false);
            if (nodes[0].IsNull || !StatusCode.IsGood(values[0].StatusCode))
            {
                return (nodes[0], null);
            }
            if (nodes[1].IsNull)
            {
                return (nodes[0], ToOperationState(values[0]));
            }
            if (!StatusCode.IsGood(values[1].StatusCode) ||
                !values[1].WrappedValue.TryGetValue(out NodeId stateId) ||
                stateId.IsNull)
            {
                return (nodes[0], null);
            }
            RoboticsOperationState? state = ToOperationState(stateId, typeStates);
            if (state != null)
            {
                return (nodes[0], state);
            }
            ArrayOf<NodeId> states = await ResolveChildrenAsync(
                operation,
                [
                    [.. machine, .. MemberPath(RoboticsBrowseNames.Idle)],
                    [.. machine, .. MemberPath(RoboticsBrowseNames.Ready)],
                    [.. machine, .. MemberPath(RoboticsBrowseNames.Executing)]
                ],
                cancellationToken).ConfigureAwait(false);
            return (nodes[0], ToOperationState(stateId, states[0], states[1], states[2]));
        }

        /// <summary>
        /// The operation state the model state <paramref name="stateId"/> is, the
        /// numeric ids of <paramref name="typeStates"/> mapped to the index the
        /// session gives the Robotics namespace; null when it is none of them.
        /// </summary>
        private RoboticsOperationState? ToOperationState(NodeId stateId, OperationStateIds typeStates)
        {
            int ns = Session.NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);
            if (ns < 0)
            {
                return null;
            }
            return ToOperationState(
                stateId,
                new NodeId(typeStates.Idle, (ushort)ns),
                new NodeId(typeStates.Ready, (ushort)ns),
                new NodeId(typeStates.Executing, (ushort)ns));
        }

        /// <summary>
        /// The operation state <paramref name="stateId"/> names among the given state
        /// nodes, a null one never matching; null when it names none of them.
        /// </summary>
        private static RoboticsOperationState? ToOperationState(
            NodeId stateId,
            NodeId idle,
            NodeId ready,
            NodeId executing)
        {
            if (!idle.IsNull && stateId == idle)
            {
                return RoboticsOperationState.Idle;
            }
            if (!ready.IsNull && stateId == ready)
            {
                return RoboticsOperationState.Ready;
            }
            if (!executing.IsNull && stateId == executing)
            {
                return RoboticsOperationState.Executing;
            }
            return null;
        }

        private async Task<string?> ReadChildStringAsync(
            NodeId parent,
            QualifiedName[] browseNames,
            CancellationToken cancellationToken)
        {
            DataValue value = await ReadChildValueAsync(parent, browseNames, cancellationToken).ConfigureAwait(false);
            return value.WrappedValue.TryGetValue(out string text) ? text : null;
        }

        private async Task<T> ReadEnumValueAsync<T>(NodeId nodeId, CancellationToken cancellationToken)
            where T : struct
        {
            if (nodeId.IsNull)
            {
                return default;
            }
            DataValue value = await Session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (value.WrappedValue.TryGetValue(out int intValue))
            {
                return (T)System.Enum.ToObject(typeof(T), intValue);
            }
            if (value.WrappedValue.TryGetValue(out uint uintValue))
            {
                return (T)System.Enum.ToObject(typeof(T), uintValue);
            }
            return default;
        }

        /// <summary>
        /// The operation state a <c>CurrentState</c> value names, by the state names
        /// OPC 40010-1 gives both operation state machines; null when the value is
        /// missing or bad, or names another state.
        /// </summary>
        private static RoboticsOperationState? ToOperationState(in DataValue value)
        {
            if (!StatusCode.IsGood(value.StatusCode) ||
                !value.WrappedValue.TryGetValue(out LocalizedText state))
            {
                return null;
            }
            return state.Text switch
            {
                RoboticsBrowseNames.Idle => RoboticsOperationState.Idle,
                RoboticsBrowseNames.Ready => RoboticsOperationState.Ready,
                RoboticsBrowseNames.Executing => RoboticsOperationState.Executing,
                _ => null
            };
        }

        /// <summary>
        /// The structure a value holds, decoded with the session's message context;
        /// null when the value is missing, bad or of another type.
        /// </summary>
        /// <typeparam name="T">The generated structure type, such as <see cref="EUInformation"/>.</typeparam>
        private T? ToStructure<T>(in DataValue value)
            where T : class, IEncodeable, new()
        {
            return StatusCode.IsGood(value.StatusCode) &&
                value.WrappedValue.TryGetStructure<T>(Session.MessageContext, out T? structure)
                ? structure
                : null;
        }

        private async Task<ArrayOf<DataValue>> ReadValuesAsync(
            List<NodeId> nodeIds,
            CancellationToken cancellationToken)
        {
            var nodes = new List<ReadValueId>(nodeIds.Count);
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                nodes.Add(new ReadValueId { NodeId = nodeIds[ii], AttributeId = Attributes.Value });
            }
            if (nodes.Count == 0)
            {
                return [];
            }
            ArrayOf<ReadValueId> nodesToRead = nodes.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodesToRead,
                cancellationToken).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodesToRead);
            return response.Results;
        }

        private async Task<ArrayOf<NodeId>> BrowseChildNodeIdsAsync(
            NodeId parent,
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> references = await BrowseObjectsAsync(parent, cancellationToken)
                .ConfigureAwait(false);
            var nodes = new List<NodeId>(references.Count);
            for (int ii = 0; ii < references.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(references[ii].NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    nodes.Add(nodeId);
                }
            }
            return nodes.ToArrayOf();
        }

        private Task<ArrayOf<ReferenceDescription>> BrowseObjectsAsync(
            NodeId parent,
            CancellationToken cancellationToken)
        {
            return BrowseObjectsAsync(parent, Opc.Ua.ReferenceTypeIds.HierarchicalReferences, cancellationToken);
        }

        private Task<ArrayOf<ReferenceDescription>> BrowseObjectsAsync(
            NodeId parent,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            return BrowseReferencesAsync(
                parent,
                referenceTypeId,
                BrowseDirection.Forward,
                cancellationToken,
                (uint)NodeClass.Object);
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseReferencesAsync(
            NodeId parent,
            NodeId referenceTypeId,
            BrowseDirection direction,
            CancellationToken cancellationToken,
            uint nodeClassMask = 0)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(
                    null,
                    null,
                    [parent],
                    0,
                    direction,
                    referenceTypeId,
                    true,
                    nodeClassMask,
                    cancellationToken).ConfigureAwait(false);
            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                return [];
            }
            return results.Count > 0 ? results[0] : [];
        }

        private static LocalizedText ReadLocalized(
            ArrayOf<NodeId> properties,
            Dictionary<NodeId, DataValue> values,
            int propertyIndex,
            LocalizedText fallback)
        {
            if (propertyIndex < properties.Count && !properties[propertyIndex].IsNull &&
                values.TryGetValue(properties[propertyIndex], out DataValue value) &&
                value.WrappedValue.TryGetValue(out LocalizedText text))
            {
                return text;
            }
            return fallback;
        }

        private static string? ReadString(
            ArrayOf<NodeId> properties,
            Dictionary<NodeId, DataValue> values,
            int propertyIndex)
        {
            if (propertyIndex < properties.Count && !properties[propertyIndex].IsNull &&
                values.TryGetValue(properties[propertyIndex], out DataValue value) &&
                value.WrappedValue.TryGetValue(out string text))
            {
                return text;
            }
            return null;
        }

        /// <summary>
        /// The Idle, Ready and Executing states the Robotics model declares on the
        /// SystemOperationStateMachineType.
        /// </summary>
        private static readonly OperationStateIds s_systemOperationStates = new(
            SystemOperationStateMachineTypeIds.StateIds.Idle,
            SystemOperationStateMachineTypeIds.StateIds.Ready,
            SystemOperationStateMachineTypeIds.StateIds.Executing);

        /// <summary>
        /// The Idle, Ready and Executing states the Robotics model declares on the
        /// TaskControlStateMachineType.
        /// </summary>
        private static readonly OperationStateIds s_taskControlStates = new(
            TaskControlStateMachineTypeIds.StateIds.Idle,
            TaskControlStateMachineTypeIds.StateIds.Ready,
            TaskControlStateMachineTypeIds.StateIds.Executing);

        /// <summary>
        /// The numeric ids of the Idle, Ready and Executing states of an operation
        /// state machine type in the Robotics namespace.
        /// </summary>
        /// <param name="Idle">The id of the Idle state.</param>
        /// <param name="Ready">The id of the Ready state.</param>
        /// <param name="Executing">The id of the Executing state.</param>
        private readonly record struct OperationStateIds(uint Idle, uint Ready, uint Executing);
    }
}
