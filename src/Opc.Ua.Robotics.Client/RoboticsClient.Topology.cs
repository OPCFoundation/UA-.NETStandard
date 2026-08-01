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
                        NodeId driveId = await ResolveChildAsync(
                            motor.Identification.NodeId,
                            RoboticsBrowseNames.DriveIdentifier_Placeholder,
                            cancellationToken).ConfigureAwait(false);
                        if (!driveId.IsNull)
                        {
                            drives.Add(await ReadDriveAsync(driveId, cancellationToken).ConfigureAwait(false));
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
            return new ControllerSnapshot
            {
                Identification = await ReadIdentificationAsync(controller, cancellationToken).ConfigureAwait(false),
                TaskControlIds = taskControls == null
                    ? []
                    : await BrowseChildNodeIdsAsync(taskControls.ObjectId, cancellationToken).ConfigureAwait(false),
                ComponentIds = components == null
                    ? []
                    : await BrowseChildNodeIdsAsync(components.ObjectId, cancellationToken).ConfigureAwait(false)
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
                motionDevice, RoboticsBrowseNames.MotionDeviceCategory, cancellationToken).ConfigureAwait(false);
            NodeId speed = await ResolveChildAsync(
                motionDevice, RoboticsBrowseNames.SpeedOverride, cancellationToken).ConfigureAwait(false);
            return new MotionDeviceSnapshot
            {
                Identification = await ReadIdentificationAsync(motionDevice, cancellationToken).ConfigureAwait(false),
                Category = await ReadEnumValueAsync<MotionDeviceCategoryEnumeration>(category, cancellationToken)
                    .ConfigureAwait(false),
                SpeedOverride = speed.IsNull ? DataValue.Null : await Session.ReadValueAsync(speed, cancellationToken)
                    .ConfigureAwait(false),
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
                axis, RoboticsBrowseNames.MotionProfile, cancellationToken).ConfigureAwait(false);
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
            return new SafetyStateSnapshot
            {
                Identification = await ReadIdentificationAsync(safetyState, cancellationToken).ConfigureAwait(false),
                EmergencyStop = await ReadChildValueAsync(
                    safetyState, RoboticsBrowseNames.EmergencyStop, cancellationToken).ConfigureAwait(false),
                OperationalMode = await ReadChildValueAsync(
                    safetyState, RoboticsBrowseNames.OperationalMode, cancellationToken).ConfigureAwait(false),
                ProtectiveStop = await ReadChildValueAsync(
                    safetyState, RoboticsBrowseNames.ProtectiveStop, cancellationToken).ConfigureAwait(false),
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
            return new TaskControlSnapshot
            {
                Identification = await ReadIdentificationAsync(taskControl, cancellationToken).ConfigureAwait(false),
                ExecutionMode = await ReadChildValueAsync(
                    taskControl, RoboticsBrowseNames.ExecutionMode, cancellationToken).ConfigureAwait(false),
                TaskProgramLoaded = await ReadChildValueAsync(
                    taskControl, RoboticsBrowseNames.TaskProgramLoaded, cancellationToken).ConfigureAwait(false),
                TaskProgramName = await ReadChildValueAsync(
                    taskControl, RoboticsBrowseNames.TaskProgramName, cancellationToken).ConfigureAwait(false),
                TaskControlOperationId = operation?.ObjectId ?? NodeId.Null,
                TaskModuleIds = modules == null
                    ? []
                    : await BrowseChildNodeIdsAsync(modules.ObjectId, cancellationToken).ConfigureAwait(false)
            };
        }

        private async Task<LoadSnapshot> ReadLoadAsync(NodeId load, CancellationToken cancellationToken)
        {
            NodeId mass = await ResolveChildAsync(load, RoboticsBrowseNames.Mass, cancellationToken)
                .ConfigureAwait(false);
            return new LoadSnapshot
            {
                NodeId = load,
                Mass = mass.IsNull ? DataValue.Null : await Session.ReadValueAsync(mass, cancellationToken)
                    .ConfigureAwait(false),
                CenterOfMass = await ReadChildValueAsync(
                    load, RoboticsBrowseNames.CenterOfMass, cancellationToken).ConfigureAwait(false),
                Inertia = await ReadChildValueAsync(load, RoboticsBrowseNames.Inertia, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        private async Task<PowerTrainSnapshot> ReadPowerTrainAsync(
            NodeId powerTrain,
            CancellationToken cancellationToken)
        {
            PowerTrainTypeClient proxy = new(Session, powerTrain, Telemetry);
            MotorTypeClient? motor = await proxy.GetMotorIdentifier_PlaceholderAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            GearTypeClient? gear = await proxy.GetGearIdentifier_PlaceholderAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            return new PowerTrainSnapshot
            {
                Identification = await ReadIdentificationAsync(powerTrain, cancellationToken).ConfigureAwait(false),
                MotorIds = motor == null ? [] : [motor.ObjectId],
                GearIds = gear == null ? [] : [gear.ObjectId]
            };
        }

        private async Task<MotorSnapshot> ReadMotorAsync(NodeId motor, CancellationToken cancellationToken)
        {
            return new MotorSnapshot
            {
                Identification = await ReadIdentificationAsync(motor, cancellationToken).ConfigureAwait(false)
            };
        }

        private async Task<GearSnapshot> ReadGearAsync(NodeId gear, CancellationToken cancellationToken)
        {
            return new GearSnapshot
            {
                Identification = await ReadIdentificationAsync(gear, cancellationToken).ConfigureAwait(false),
                Pitch = await ReadChildValueAsync(gear, RoboticsBrowseNames.Pitch, cancellationToken)
                    .ConfigureAwait(false)
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
            return new TaskModuleSnapshot
            {
                NodeId = taskModule,
                Name = await ReadChildStringAsync(taskModule, RoboticsBrowseNames.Name, cancellationToken)
                    .ConfigureAwait(false),
                Version = await ReadChildStringAsync(taskModule, "Version", cancellationToken).ConfigureAwait(false),
                IsReferenced = await ReadChildValueAsync(
                    taskModule, RoboticsBrowseNames.IsReferenced, cancellationToken).ConfigureAwait(false)
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
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(
                axis,
                [
                    RoboticsBrowseNames.ActualPosition,
                    RoboticsBrowseNames.ActualSpeed,
                    RoboticsBrowseNames.ActualAcceleration
                ],
                cancellationToken).ConfigureAwait(false);
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                nodes.ToList(), null, cancellationToken).ConfigureAwait(false))
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
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(
                safetyState,
                [
                    RoboticsBrowseNames.EmergencyStop,
                    RoboticsBrowseNames.OperationalMode,
                    RoboticsBrowseNames.ProtectiveStop
                ],
                cancellationToken).ConfigureAwait(false);
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                nodes.ToList(), null, cancellationToken).ConfigureAwait(false))
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
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(
                axis,
                [
                    RoboticsBrowseNames.ActualPosition,
                    RoboticsBrowseNames.ActualSpeed,
                    RoboticsBrowseNames.ActualAcceleration
                ],
                cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadValuesAsync(nodes.ToList(), cancellationToken)
                .ConfigureAwait(false);
            return new AxisStateSnapshot
            {
                ActualPosition = values.Count > 0 ? values[0] : DataValue.Null,
                ActualSpeed = values.Count > 1 ? values[1] : DataValue.Null,
                ActualAcceleration = values.Count > 2 ? values[2] : DataValue.Null
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
                snapshots.Add(new SafetyFunctionSnapshot
                {
                    NodeId = functionId,
                    Name = await ReadChildStringAsync(
                        functionId, RoboticsBrowseNames.Name, cancellationToken).ConfigureAwait(false),
                    Active = await ReadChildValueAsync(
                        functionId, RoboticsBrowseNames.Active, cancellationToken).ConfigureAwait(false),
                    Enabled = await ReadChildValueAsync(
                        functionId, RoboticsBrowseNames.Enabled, cancellationToken).ConfigureAwait(false)
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
                    DiBrowseNames.ComponentName,
                    DiBrowseNames.AssetId,
                    DiBrowseNames.Manufacturer,
                    DiBrowseNames.Model,
                    DiBrowseNames.ProductCode,
                    DiBrowseNames.SerialNumber,
                    DiBrowseNames.DeviceManual
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
                DeviceManual = ReadString(properties, values, 6)
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
            IReadOnlyList<string> browseNames,
            CancellationToken cancellationToken)
        {
            var paths = new List<BrowsePath>(browseNames.Count);
            for (int ii = 0; ii < browseNames.Count; ii++)
            {
                paths.Add(CreateBrowsePath(parent, browseNames[ii]));
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                paths.ToArrayOf(),
                cancellationToken).ConfigureAwait(false);
            var results = new List<NodeId>(browseNames.Count);
            for (int ii = 0; ii < response.Results.Count; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                results.Add(StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0
                    ? ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris)
                    : NodeId.Null);
            }
            while (results.Count < browseNames.Count)
            {
                results.Add(NodeId.Null);
            }
            return results.ToArrayOf();
        }

        private async ValueTask<NodeId> ResolveChildAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveChildrenAsync(parent, [browseName], cancellationToken)
                .ConfigureAwait(false);
            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        private BrowsePath CreateBrowsePath(NodeId parent, string browseName)
        {
            return new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName)
                        }
                    ]
                }
            };
        }

        private async Task<DataValue> ReadChildValueAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            NodeId nodeId = await ResolveChildAsync(parent, browseName, cancellationToken).ConfigureAwait(false);
            return nodeId.IsNull
                ? DataValue.Null
                : await Session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string?> ReadChildStringAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            DataValue value = await ReadChildValueAsync(parent, browseName, cancellationToken).ConfigureAwait(false);
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
    }
}
