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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using RoboticsBrowseNames = Opc.Ua.Robotics.BrowseNames;

namespace Opc.Ua.Robotics.Client.Tests
{
    /// <summary>
    /// Tests for the high-level Robotics facade added for topology, operations, streaming, and DI.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    public sealed class RoboticsClientFacadeTests
    {
        [Test]
        public async Task EnumerateMotionDeviceSystemsStreamsMatchesAndUsesBrowseNext()
        {
            RoboticsSessionHarness harness = new();
            harness.AddBrowse(
                harness.DeviceSetId,
                [harness.Ref(harness.SystemId, "System", RoboticsModel.MotionDeviceSystemType)]);
            harness.EnableBrowseContinuationFor(harness.DeviceSetId);
            harness.NodeCache.Setup(c => c.IsTypeOfAsync(
                It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));

            List<MotionDeviceSystemEntry> entries = [];
            await foreach (MotionDeviceSystemEntry entry in harness.Client.EnumerateMotionDeviceSystemsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.SystemId));
            harness.Session.Verify(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task EnumerateMotionDeviceSystemsReturnsEmptyWhenNamespaceAbsent()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            var client = new RoboticsClient(session.Object, new Mock<ITelemetryContext>().Object);

            List<MotionDeviceSystemEntry> entries = [];
            await foreach (MotionDeviceSystemEntry entry in client.EnumerateMotionDeviceSystemsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries, Is.Empty);
        }

        [Test]
        public async Task ReadSystemPopulatesTopologySnapshotsAndRelationships()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();

            RoboticsTopologySnapshot snapshot = await h.Client.ReadSystemAsync(h.SystemId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Systems[0].Identification.ComponentName.Text, Is.EqualTo("System"));
                Assert.That(snapshot.Systems[0].Identification.SerialNumber, Is.EqualTo("SystemSerial"),
                    "identification comes from the DI namespace, not the namespace-0 decoy");
                Assert.That(snapshot.Systems[0].Identification.Manufacturer.Text, Is.EqualTo("OPC"));
                Assert.That(
                    snapshot.MotionDevices[0].Category,
                    Is.EqualTo(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT));
                Assert.That(
                    snapshot.MotionDevices[0].SpeedOverride.WrappedValue.TryGetValue(out double speed),
                    Is.True);
                Assert.That(speed, Is.EqualTo(0.75d), "SpeedOverride comes from the ParameterSet");
                Assert.That(snapshot.Axes[0].MotionProfile, Is.EqualTo(AxisMotionProfileEnumeration.ROTARY));
                Assert.That(snapshot.Gears[0].Pitch.WrappedValue.TryGetValue(out double pitch), Is.True);
                Assert.That(pitch, Is.EqualTo(1.0d));
                Assert.That(snapshot.SafetyStates[0].EmergencyStop.WrappedValue.TryGetValue(out bool estop), Is.True);
                Assert.That(estop, Is.False);
                Assert.That(
                    snapshot.TaskControls[0].TaskProgramName.WrappedValue.TryGetValue(out string program),
                    Is.True);
                Assert.That(program, Is.EqualTo("Program"));
                Assert.That(snapshot.TaskModules[0].Version, Is.EqualTo("1.0"));
                Assert.That(snapshot.Controllers[0].TaskControlIds.ToList(), Does.Contain(h.TaskControlId));
                Assert.That(snapshot.MotionDevices[0].AxisIds.ToList(), Does.Contain(h.AxisId));
                Assert.That(snapshot.MotionDevices[0].FlangeLoadId, Is.EqualTo(h.FlangeLoadId));
                Assert.That(
                    snapshot.Axes[0].State.ActualPosition.WrappedValue.TryGetValue(out double position),
                    Is.True);
                Assert.That(position, Is.EqualTo(12.5d));
                Assert.That(snapshot.Loads.ToList().Any(l => l.NodeId == h.FlangeLoadId), Is.True);
                // Motors, gears and drives are placeholder instances found by type
                // (motors/gears) and by the IsDrivenBy reference (drives).
                Assert.That(snapshot.PowerTrains[0].MotorIds.ToList(), Is.EqualTo(new[] { h.MotorId }));
                Assert.That(snapshot.PowerTrains[0].GearIds.ToList(), Is.EqualTo(new[] { h.GearId }));
                Assert.That(snapshot.Motors, Has.Count.EqualTo(1));
                Assert.That(snapshot.Motors[0].Identification.NodeId, Is.EqualTo(h.MotorId));
                Assert.That(snapshot.Gears, Has.Count.EqualTo(1));
                Assert.That(snapshot.Gears[0].Identification.NodeId, Is.EqualTo(h.GearId));
                Assert.That(snapshot.Drives, Has.Count.EqualTo(1));
                Assert.That(snapshot.Drives[0].Identification.NodeId, Is.EqualTo(h.DriveId));
                Assert.That(snapshot.SafetyStates[0].EmergencyStopFunctions[0].Name, Is.EqualTo("EStop"));
                Assert.That(snapshot.TaskControls[0].TaskModuleIds.ToList(), Does.Contain(h.TaskModuleId));
                Assert.That(snapshot.TaskModules[0].Name, Is.EqualTo("Module"));
                Assert.That(snapshot.Relationships.Controls, Has.Count.EqualTo(1));
                Assert.That(snapshot.Relationships.HasSafetyStates, Has.Count.EqualTo(1));
                Assert.That(snapshot.Relationships.Moves, Has.Count.EqualTo(1));
                Assert.That(snapshot.Relationships.Requires, Has.Count.EqualTo(1));
                Assert.That(snapshot.Relationships.HasSlave, Has.Count.EqualTo(1));
                Assert.That(snapshot.Relationships.IsDrivenBy, Has.Count.EqualTo(1));
                Assert.That(snapshot.Relationships.IsConnectedTo, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task PerNodeSnapshotReadersPopulateExpectedFields()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);
            MotionDeviceSnapshot motion = await h.Client.ReadMotionDeviceAsync(h.MotionDeviceId).ConfigureAwait(false);
            AxisSnapshot axis = await h.Client.ReadAxisAsync(h.AxisId).ConfigureAwait(false);
            SafetyStateSnapshot safety = await h.Client.ReadSafetyStateAsync(h.SafetyId).ConfigureAwait(false);
            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.ComponentIds.ToList(), Does.Contain(h.ControllerComponentId));
                Assert.That(motion.SpeedOverride.WrappedValue.TryGetValue(out double speed), Is.True);
                Assert.That(speed, Is.EqualTo(0.75d));
                Assert.That(axis.MotionProfile, Is.EqualTo(AxisMotionProfileEnumeration.ROTARY));
                Assert.That(safety.ProtectiveStopFunctions[0].Name, Is.EqualTo("PStop"));
                Assert.That(task.TaskControlOperationId, Is.EqualTo(h.TaskControlOperationId));
            });
        }

        [Test]
        public async Task SnapshotsCarryTheNodeIdsOfTheVariablesTheyRead()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            QualifiedName parameterSet = h.Di(DiBrowseNames.ParameterSet);
            NodeId speedOverrideDecoy = h.Resolve(
                h.MotionDeviceId, RoboticsSessionHarness.Ua(RoboticsBrowseNames.SpeedOverride));

            RoboticsTopologySnapshot snapshot = await h.Client.ReadSystemAsync(h.SystemId).ConfigureAwait(false);

            SafetyStateSnapshot safety = snapshot.SafetyStates[0];
            TaskControlSnapshot task = snapshot.TaskControls[0];
            SafetyFunctionSnapshot estop = safety.EmergencyStopFunctions[0];
            Assert.Multiple(() =>
            {
                // The ParameterSet variables, not the decoys of the same name
                // directly below the axis and the motion device.
                Assert.That(snapshot.Axes[0].State.ActualPositionId, Is.EqualTo(h.AxisPositionId));
                Assert.That(snapshot.Axes[0].State.ActualSpeedId, Is.EqualTo(h.AxisSpeedId));
                Assert.That(snapshot.Axes[0].State.ActualAccelerationId, Is.EqualTo(h.AxisAccelerationId));
                Assert.That(
                    snapshot.MotionDevices[0].SpeedOverrideId,
                    Is.EqualTo(h.Resolve(h.MotionDeviceId, parameterSet, h.Rob(RoboticsBrowseNames.SpeedOverride))));
                Assert.That(snapshot.MotionDevices[0].SpeedOverrideId, Is.Not.EqualTo(speedOverrideDecoy));
                Assert.That(
                    safety.EmergencyStopId,
                    Is.EqualTo(h.Resolve(h.SafetyId, parameterSet, h.Rob(RoboticsBrowseNames.EmergencyStop))));
                Assert.That(
                    safety.OperationalModeId,
                    Is.EqualTo(h.Resolve(h.SafetyId, parameterSet, h.Rob(RoboticsBrowseNames.OperationalMode))));
                Assert.That(
                    safety.ProtectiveStopId,
                    Is.EqualTo(h.Resolve(h.SafetyId, parameterSet, h.Rob(RoboticsBrowseNames.ProtectiveStop))));
                Assert.That(
                    estop.ActiveId,
                    Is.EqualTo(h.Resolve(h.EmergencyFunctionId, h.Rob(RoboticsBrowseNames.Active))));
                Assert.That(
                    estop.EnabledId,
                    Is.EqualTo(h.Resolve(h.EmergencyFunctionId, h.Rob(RoboticsBrowseNames.Enabled))));
                Assert.That(
                    task.ExecutionModeId,
                    Is.EqualTo(h.Resolve(h.TaskControlId, parameterSet, h.Rob(RoboticsBrowseNames.ExecutionMode))));
                Assert.That(
                    task.TaskProgramLoadedId,
                    Is.EqualTo(h.Resolve(
                        h.TaskControlId, parameterSet, h.Rob(RoboticsBrowseNames.TaskProgramLoaded))));
                Assert.That(
                    task.TaskProgramNameId,
                    Is.EqualTo(h.Resolve(h.TaskControlId, parameterSet, h.Rob(RoboticsBrowseNames.TaskProgramName))));
                Assert.That(
                    snapshot.TaskModules[0].IsReferencedId,
                    Is.EqualTo(h.Resolve(h.TaskModuleId, h.Rob(RoboticsBrowseNames.IsReferenced))));
                Assert.That(
                    snapshot.Loads.ToList().Single(l => l.NodeId == h.FlangeLoadId).MassId,
                    Is.EqualTo(h.Resolve(h.FlangeLoadId, h.Rob(RoboticsBrowseNames.Mass))));
                Assert.That(
                    snapshot.Gears[0].PitchId,
                    Is.EqualTo(h.Resolve(h.GearId, h.Rob(RoboticsBrowseNames.Pitch))));
            });
        }

        [Test]
        public async Task SnapshotsReadTheStateOfTheOperationStateMachines()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);
            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.SystemOperationId, Is.EqualTo(h.SystemOperationId));
                // The CurrentState variable itself: not its Id child, not the machine.
                Assert.That(controller.CurrentStateId, Is.EqualTo(h.SystemCurrentStateId));
                Assert.That(controller.CurrentState, Is.EqualTo(RoboticsOperationState.Executing));
                Assert.That(task.TaskControlOperationId, Is.EqualTo(h.TaskControlOperationId));
                Assert.That(task.CurrentStateId, Is.EqualTo(h.TaskCurrentStateId));
                Assert.That(task.CurrentState, Is.EqualTo(RoboticsOperationState.Ready));
            });
        }

        [Test]
        public async Task TheOperationStateIsMatchedByStateNodeIdNotByLocalizedText()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            NodeId taskIdle = new(1910, 2);
            NodeId taskExecuting = new(1911, 2);
            NodeId systemIdle = new(1920, 2);
            NodeId systemReady = new(1921, 2);
            NodeId systemExecuting = new(1922, 2);
            // A German server: the state names are localized, the state nodes are not.
            h.AddOperationState(
                h.SystemOperationId,
                RoboticsBrowseNames.SystemOperationStateMachine,
                h.SystemStateMachineId,
                h.SystemCurrentStateId,
                "Ausf\u00fchrung");
            h.AddOperationState(
                h.TaskControlOperationId,
                RoboticsBrowseNames.TaskControlStateMachine,
                h.TaskControlStateMachineId,
                h.TaskCurrentStateId,
                "Bereit");
            h.AddStateNodes(
                h.SystemStateMachineId, h.SystemCurrentStateId, systemIdle, systemReady, systemExecuting,
                systemExecuting);
            h.AddStateNodes(
                h.TaskControlStateMachineId, h.TaskCurrentStateId, taskIdle, h.ReadyStateId, taskExecuting,
                h.ReadyStateId);

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);
            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.CurrentState, Is.EqualTo(RoboticsOperationState.Executing));
                Assert.That(task.CurrentState, Is.EqualTo(RoboticsOperationState.Ready));
            });
        }

        [Test]
        public async Task TheOperationStateIsMatchedByTheStateIdsTheModelDeclaresOnTheMachineType()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            // The Robotics model declares Idle, Ready and Executing on the state machine
            // types; a server that does not instantiate them publishes the type states'
            // NodeIds, mapped to its own index of the Robotics namespace.
            h.AddOperationState(
                h.SystemOperationId,
                RoboticsBrowseNames.SystemOperationStateMachine,
                h.SystemStateMachineId,
                h.SystemCurrentStateId,
                "Bereit");
            h.AddOperationState(
                h.TaskControlOperationId,
                RoboticsBrowseNames.TaskControlStateMachine,
                h.TaskControlStateMachineId,
                h.TaskCurrentStateId,
                "Ausf\u00fchrung");
            h.SetCurrentStateId(
                h.SystemCurrentStateId,
                NodeId.Create(
                    SystemOperationStateMachineTypeIds.StateIds.Ready,
                    global::Opc.Ua.Robotics.Namespaces.Robotics,
                    h.NamespaceUris));
            h.SetCurrentStateId(
                h.TaskCurrentStateId,
                NodeId.Create(
                    TaskControlStateMachineTypeIds.StateIds.Executing,
                    global::Opc.Ua.Robotics.Namespaces.Robotics,
                    h.NamespaceUris));

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);
            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.CurrentState, Is.EqualTo(RoboticsOperationState.Ready));
                Assert.That(task.CurrentState, Is.EqualTo(RoboticsOperationState.Executing));
            });
        }

        [Test]
        public async Task ABadCurrentStateReadsAsNoStateEvenWithAMatchingStateId()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            h.AddStateNodes(
                h.SystemStateMachineId, h.SystemCurrentStateId,
                new NodeId(1920, 2), new NodeId(1921, 2), new NodeId(1922, 2),
                new NodeId(1922, 2));
            h.SetBadQuality(h.SystemCurrentStateId);

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.CurrentStateId, Is.EqualTo(h.SystemCurrentStateId));
                Assert.That(controller.CurrentState.HasValue, Is.False, "a bad CurrentState is no state");
            });
        }

        [Test]
        public async Task APublishedStateIdThatNamesNoKnownStateIsNotOverriddenByTheStateName()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            // Both name a state in their text, but the Id the server publishes is
            // unknown for the controller and unreadable for the task control.
            h.SetCurrentStateId(h.SystemCurrentStateId, new NodeId(4711, 2));
            h.SetCurrentStateId(h.TaskCurrentStateId, new NodeId(1922, 2));
            h.SetBadQuality(h.Resolve(h.TaskCurrentStateId, RoboticsSessionHarness.Ua(Opc.Ua.BrowseNames.Id)));

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);
            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.CurrentStateId, Is.EqualTo(h.SystemCurrentStateId));
                Assert.That(controller.CurrentState.HasValue, Is.False, "an unknown state Id is no state");
                Assert.That(task.CurrentStateId, Is.EqualTo(h.TaskCurrentStateId));
                Assert.That(task.CurrentState.HasValue, Is.False, "an unreadable state Id is no state");
            });
        }

        [Test]
        public async Task ABadQualityEngineeringValueReadsAsNoStructure()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            h.SetBadQuality(h.Resolve(h.MotorTemperatureId, RoboticsSessionHarness.Ua(Opc.Ua.BrowseNames.EURange)));

            RoboticsTopologySnapshot snapshot = await h.Client.ReadSystemAsync(h.SystemId).ConfigureAwait(false);

            MotorSnapshot motor = snapshot.Motors[0];
            Assert.Multiple(() =>
            {
                Assert.That(motor.MotorTemperatureEngineering.Range, Is.Null, "a bad read is not a range");
                Assert.That(motor.MotorTemperatureEngineering.EngineeringUnits?.UnitId, Is.EqualTo(h.Celsius.UnitId));
            });
        }

        [Test]
        public async Task AnUnknownOrUnreadableOperationStateReadsAsNull()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            // A state the client does not know, and a CurrentState whose read fails.
            h.AddOperationState(
                h.TaskControlOperationId,
                RoboticsBrowseNames.TaskControlStateMachine,
                h.TaskControlStateMachineId,
                h.TaskCurrentStateId,
                "Halted");
            h.AddOperationState(
                h.SystemOperationId,
                RoboticsBrowseNames.SystemOperationStateMachine,
                h.SystemStateMachineId,
                h.SystemCurrentStateId,
                null);

            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);
            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(task.CurrentStateId, Is.EqualTo(h.TaskCurrentStateId));
                Assert.That(task.CurrentState.HasValue, Is.False, "Halted is not an OPC 40010-1 operation state");
                Assert.That(controller.CurrentStateId, Is.EqualTo(h.SystemCurrentStateId));
                Assert.That(controller.CurrentState.HasValue, Is.False, "a failed read is no state, not an exception");
                Assert.That(controller.Identification.SerialNumber, Is.EqualTo("ControllerSerial"));
            });
        }

        [Test]
        public async Task SnapshotsWithoutOperationsHaveNoStateIds()
        {
            RoboticsSessionHarness h = new();

            ControllerSnapshot controller = await h.Client.ReadControllerAsync(h.ControllerId).ConfigureAwait(false);
            TaskControlSnapshot task = await h.Client.ReadTaskControlAsync(h.TaskControlId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(controller.SystemOperationId.IsNull, Is.True);
                Assert.That(controller.CurrentStateId.IsNull, Is.True);
                Assert.That(controller.CurrentState.HasValue, Is.False);
                Assert.That(task.TaskControlOperationId.IsNull, Is.True);
                Assert.That(task.CurrentStateId.IsNull, Is.True);
                Assert.That(task.CurrentState.HasValue, Is.False);
                Assert.That(task.ExecutionModeId.IsNull, Is.True);
                Assert.That(task.ExecutionMode.IsNull, Is.True);
            });
        }

        [Test]
        public async Task IdentificationAndMotorValuesComeFromTheDiPropertiesAndTheParameterSet()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();

            RoboticsTopologySnapshot snapshot = await h.Client.ReadSystemAsync(h.SystemId).ConfigureAwait(false);

            RoboticsComponentIdentification robot = snapshot.MotionDevices[0].Identification;
            MotorSnapshot motor = snapshot.Motors[0];
            Assert.Multiple(() =>
            {
                Assert.That(robot.HardwareRevision, Is.EqualTo("MotionHardware"));
                Assert.That(robot.SoftwareRevision, Is.EqualTo("MotionSoftware"), "DI namespace, not the decoy");
                Assert.That(robot.ManufacturerUri, Is.EqualTo("urn:opc:Motion"));
                Assert.That(robot.ProductInstanceUri, Is.EqualTo("urn:opc:Motion:1"));
                Assert.That(motor.Identification.SoftwareRevision, Is.EqualTo("MotorSoftware"));

                Assert.That(motor.MotorTemperatureId, Is.EqualTo(h.MotorTemperatureId));
                Assert.That(motor.MotorTemperature.WrappedValue.TryGetValue(out double temperature), Is.True);
                Assert.That(temperature, Is.EqualTo(41.5d), "the ParameterSet variable, not the decoy");
                Assert.That(motor.MotorTemperatureEngineering.EngineeringUnits?.UnitId, Is.EqualTo(h.Celsius.UnitId));
                Assert.That(motor.MotorTemperatureEngineering.Range?.Low, Is.EqualTo(-20d));
                Assert.That(motor.MotorTemperatureEngineering.Range?.High, Is.EqualTo(120d));
                Assert.That(motor.BrakeReleased.WrappedValue.TryGetValue(out bool released), Is.True);
                Assert.That(released, Is.True);
                Assert.That(
                    motor.BrakeReleasedId,
                    Is.EqualTo(h.Resolve(
                        h.MotorId, h.Di(DiBrowseNames.ParameterSet), h.Rob(RoboticsBrowseNames.BrakeReleased))));
                // EffectiveLoadRate is optional and not published here.
                Assert.That(motor.EffectiveLoadRateId.IsNull, Is.True);
                Assert.That(motor.EffectiveLoadRate.IsNull, Is.True);
            });
        }

        [Test]
        public async Task ProgramsAsyncReturnsFileSystemClientAndFailsWhenAbsent()
        {
            RoboticsSessionHarness h = new();
            h.AddChild(h.ControllerId, h.Rob(RoboticsBrowseNames.Programs), h.ProgramsId);

            FileSystemClient fileSystem = await h.Client.ProgramsAsync(h.ControllerId).ConfigureAwait(false);

            Assert.That(fileSystem.Root.NodeId, Is.EqualTo(h.ProgramsId));
            RoboticsSessionHarness missing = new();
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await missing.Client.ProgramsAsync(missing.ControllerId).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [TestCase("GetReady")]
        [TestCase("Start")]
        [TestCase("Stop")]
        [TestCase("StandDown")]
        public async Task SystemOperationVerbsReturnStatusAndBadResultsThrow(string verb)
        {
            RoboticsSessionHarness good = new();
            good.ConfigureSystemOperation(StatusCodes.Good, 17);
            SystemOperationClient goodClient = new(good.Session.Object, good.SystemOperationId, good.Telemetry);

            int status = await InvokeSystemVerbAsync(goodClient, verb).ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(17));
            RoboticsSessionHarness bad = new();
            bad.ConfigureSystemOperation(StatusCodes.BadInvalidState, 0);
            SystemOperationClient badClient = new(bad.Session.Object, bad.SystemOperationId, bad.Telemetry);
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await InvokeSystemVerbAsync(badClient, verb).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [TestCase("LoadByName")]
        [TestCase("LoadByNodeId")]
        [TestCase("UnloadByName")]
        [TestCase("UnloadByNodeId")]
        [TestCase("UnloadProgram")]
        [TestCase("ResetToProgramStart")]
        [TestCase("Start")]
        [TestCase("Stop")]
        public async Task TaskControlMembersReturnStatusAndBadResultsThrow(string verb)
        {
            RoboticsSessionHarness good = new();
            good.ConfigureTaskControl(StatusCodes.Good, 23);
            TaskControlClient goodClient = new(good.Session.Object, good.TaskControlId, good.Telemetry);

            int status = await InvokeTaskVerbAsync(goodClient, verb).ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(23));
            RoboticsSessionHarness bad = new();
            bad.ConfigureTaskControl(StatusCodes.BadInvalidState, 0);
            TaskControlClient badClient = new(bad.Session.Object, bad.TaskControlId, bad.Telemetry);
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await InvokeTaskVerbAsync(badClient, verb).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task OperationClientsReadAndObserveState()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureSystemOperation(StatusCodes.Good, 1);
            h.AddStateReads(h.SystemStateMachineId, RoboticsBrowseNames.Executing);
            Mock<IStreamingSubscription> streaming = h.Streaming(h.CurrentStateIdNode);
            SystemOperationClient system = new(h.Session.Object, h.SystemOperationId, h.Telemetry);

            RoboticsOperationState read = await system.ReadStateAsync().ConfigureAwait(false);
            RoboticsOperationState observed = await FirstAsync(
                system.ObserveStateAsync(streaming.Object)).ConfigureAwait(false);

            Assert.That(read, Is.EqualTo(RoboticsOperationState.Executing));
            Assert.That(observed, Is.EqualTo(RoboticsOperationState.Executing));
        }

        [Test]
        public async Task ObserveAxisAndSafetyYieldSnapshotsAndCancellationStopsEnumeration()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureCompleteTopology();
            Mock<IStreamingSubscription> streaming = h.Streaming(h.AxisPositionId);

            AxisStateSnapshot axis = await FirstAsync(
                h.Client.ObserveAxisAsync(h.AxisId, streaming.Object)).ConfigureAwait(false);
            SafetyStateSnapshot safety = await FirstAsync(
                h.Client.ObserveSafetyAsync(h.SafetyId, streaming.Object)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(axis.ActualPosition.WrappedValue.TryGetValue(out double position), Is.True);
                Assert.That(position, Is.EqualTo(12.5d));
                Assert.That(axis.ActualPositionId, Is.EqualTo(h.AxisPositionId));
                Assert.That(safety.EmergencyStop.WrappedValue.TryGetValue(out bool emergency), Is.True);
                Assert.That(emergency, Is.False);
                Assert.That(safety.EmergencyStopId.IsNull, Is.False);
            });
        }

        [Test]
        public async Task ResetToProgramStartCallsTheReadySubstateMachine()
        {
            // OPC 40010-1 declares ResetToProgramStart on the ReadySubstateMachine,
            // a sibling of the Ready state below the TaskControlStateMachine.
            RoboticsSessionHarness h = new();
            h.ConfigureTaskControl(StatusCodes.Good, 5);
            TaskControlClient client = new(h.Session.Object, h.TaskControlId, h.Telemetry);

            int status = await client.ResetToProgramStartAsync().ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(5));
            Assert.That(h.Calls, Has.Count.EqualTo(1));
            Assert.That(h.Calls[0].ObjectId, Is.EqualTo(h.ReadySubstateMachineId));
        }

        [Test]
        public void ResetToProgramStartFailsWithoutAReadySubstateMachine()
        {
            RoboticsSessionHarness h = new();
            h.ConfigureTaskControl(StatusCodes.Good, 5, readySubstateMachine: false);
            TaskControlClient client = new(h.Session.Object, h.TaskControlId, h.Telemetry);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ResetToProgramStartAsync().ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(h.Calls, Is.Empty, "the Ready state has no ResetToProgramStart to call");
        }

        [Test]
        public void OperationCallsFailExplicitlyWhenRequiredNodesAreAbsent()
        {
            RoboticsSessionHarness h = new();
            SystemOperationClient system = new(h.Session.Object, h.SystemOperationId, h.Telemetry);
            TaskControlClient task = new(h.Session.Object, h.TaskControlId, h.Telemetry);

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await system.StartAsync().ConfigureAwait(false));
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await task.StartAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task SessionExtensionFactoryAndRegistrationWorkAndValidateNulls()
        {
            RoboticsSessionHarness h = new();

            RoboticsClient fromExtension = h.Session.Object.Robotics(h.Telemetry);
            var factory = new RoboticsClientFactory(
                _ => Task.FromResult((ManagedSession)h.Session.Object), h.Telemetry);

            Assert.That(fromExtension.Session, Is.SameAs(h.Session.Object));
            Assert.Throws<ArgumentNullException>(() => SessionRoboticsExtensions.Robotics(null!, h.Telemetry));
            Assert.Throws<ArgumentNullException>(() => h.Session.Object.Robotics(null!));
            Assert.Throws<ArgumentNullException>(() => new RoboticsClientFactory(null!, h.Telemetry));
            Assert.Throws<ArgumentNullException>(() => new RoboticsClientFactory(_ => null!, null!));

            var services = new ServiceCollection();
            var builder = new TestClientBuilder(services);
            builder.AddRoboticsClient();
            Assert.That(services.Any(s => s.ServiceType == typeof(RoboticsClientFactory)), Is.True);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task<int> InvokeSystemVerbAsync(SystemOperationClient client, string verb)
        {
            return verb switch
            {
                "GetReady" => await client.GetReadyAsync().ConfigureAwait(false),
                "Start" => await client.StartAsync().ConfigureAwait(false),
                "Stop" => await client.StopAsync(RoboticsStopMode.Controlled).ConfigureAwait(false),
                _ => await client.StandDownAsync().ConfigureAwait(false)
            };
        }

        private static async Task<int> InvokeTaskVerbAsync(TaskControlClient client, string verb)
        {
            return verb switch
            {
                "LoadByName" => (await client.LoadByNameAsync("p").ConfigureAwait(false)).Status,
                "LoadByNodeId" => (await client.LoadByNodeIdAsync(new NodeId(1)).ConfigureAwait(false)).Status,
                "UnloadByName" => (await client.UnloadByNameAsync("p").ConfigureAwait(false)).Status,
                "UnloadByNodeId" => (await client.UnloadByNodeIdAsync(new NodeId(1)).ConfigureAwait(false)).Status,
                "UnloadProgram" => (await client.UnloadProgramAsync().ConfigureAwait(false)).Status,
                "ResetToProgramStart" => await client.ResetToProgramStartAsync().ConfigureAwait(false),
                "Start" => await client.StartAsync().ConfigureAwait(false),
                _ => await client.StopAsync(RoboticsStopMode.Quick).ConfigureAwait(false)
            };
        }

        private static async Task<T> FirstAsync<T>(IAsyncEnumerable<T> source)
        {
            await foreach (T item in source.ConfigureAwait(false))
            {
                return item;
            }
            throw new InvalidOperationException("The sequence did not produce a value.");
        }

        private sealed class TestClientBuilder(IServiceCollection services) : IOpcUaClientBuilder
        {
            public IServiceCollection Services { get; } = services;
        }

        private sealed class RoboticsSessionHarness
        {
            private readonly Dictionary<(NodeId Parent, QualifiedName BrowseName), NodeId> m_children = [];
            private readonly Dictionary<NodeId, NodeId> m_parameterSets = [];
            private readonly Dictionary<NodeId, List<ReferenceDescription>> m_browse = [];
            private readonly Dictionary<NodeId, Variant> m_values = [];
            private readonly HashSet<NodeId> m_continuationNodes = [];
            private readonly HashSet<NodeId> m_badQualityNodes = [];
            private StatusCode m_callStatus = StatusCodes.Good;
            private int m_callOutput;

            public RoboticsSessionHarness()
            {
                Telemetry = new Mock<ITelemetryContext>().Object;
                NamespaceUris.GetIndexOrAppend(global::Opc.Ua.Robotics.Namespaces.Robotics);
                NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
                MessageContext = ServiceMessageContext.Create(Telemetry);
                MessageContext.NamespaceUris.GetIndexOrAppend(global::Opc.Ua.Robotics.Namespaces.Robotics);
                MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
                Session.SetupGet(s => s.NamespaceUris).Returns(NamespaceUris);
                Session.SetupGet(s => s.MessageContext).Returns(MessageContext);
                Session.SetupGet(s => s.Factory).Returns(MessageContext.Factory);
                Session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
                Session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities());
                Session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
                Session.SetupGet(s => s.NodeCache).Returns(NodeCache.Object);
                // Exact type match: every reference in the harness carries its
                // concrete TypeDefinition, and a permissive answer would hide
                // type filtering (a gear classified as a motor).
                NodeCache.Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns<NodeId, NodeId, CancellationToken>(
                        (subType, superType, _) => new ValueTask<bool>(subType == superType));
                SetupTranslate();
                SetupBrowse();
                SetupRead();
                SetupCall();
                Client = new RoboticsClient(Session.Object, Telemetry);
            }

            public Mock<ISession> Session { get; } = new(MockBehavior.Loose);

            public Mock<INodeCache> NodeCache { get; } = new(MockBehavior.Loose);

            public ITelemetryContext Telemetry { get; }

            public NamespaceTable NamespaceUris { get; } = new();

            public ServiceMessageContext MessageContext { get; }

            public RoboticsClient Client { get; }

            public NodeId DeviceSetId => NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet, Opc.Ua.Di.Namespaces.OpcUaDi, NamespaceUris);

            public NodeId SystemId { get; } = new(1001, 2);

            public NodeId ControllersFolderId { get; } = new(1002, 2);

            public NodeId MotionDevicesFolderId { get; } = new(1003, 2);

            public NodeId SafetyStatesFolderId { get; } = new(1004, 2);

            public NodeId ControllerId { get; } = new(1100, 2);

            public NodeId ControllerComponentId { get; } = new(1101, 2);

            public NodeId ControllerComponentsFolderId { get; } = new(1102, 2);

            public NodeId TaskControlsFolderId { get; } = new(1103, 2);

            public NodeId SystemOperationId { get; } = new(1104, 2);

            public NodeId SystemStateMachineId { get; } = new(1105, 2);

            public NodeId ProgramsId { get; } = new(1106, 2);

            public NodeId MotionDeviceId { get; } = new(1200, 2);

            public NodeId AxesFolderId { get; } = new(1201, 2);

            public NodeId PowerTrainsFolderId { get; } = new(1202, 2);

            public NodeId AdditionalComponentsFolderId { get; } = new(1203, 2);

            public NodeId AxisId { get; } = new(1300, 2);

            public NodeId AxisPositionId { get; } = new(1301, 2);

            public NodeId AxisSpeedId { get; } = new(1302, 2);

            public NodeId AxisAccelerationId { get; } = new(1303, 2);

            public NodeId AxisMotionProfileId { get; } = new(1304, 2);

            public NodeId AxisLoadId { get; } = new(1305, 2);

            public NodeId FlangeLoadId { get; } = new(1306, 2);

            public NodeId PowerTrainId { get; } = new(1400, 2);

            public NodeId MotorId { get; } = new(1401, 2);

            public NodeId GearId { get; } = new(1402, 2);

            public NodeId DriveId { get; } = new(1403, 2);

            public NodeId SafetyId { get; } = new(1500, 2);

            public NodeId EmergencyFunctionsFolderId { get; } = new(1501, 2);

            public NodeId ProtectiveFunctionsFolderId { get; } = new(1502, 2);

            public NodeId EmergencyFunctionId { get; } = new(1503, 2);

            public NodeId ProtectiveFunctionId { get; } = new(1504, 2);

            public NodeId TaskControlId { get; } = new(1600, 2);

            public NodeId TaskControlOperationId { get; } = new(1601, 2);

            public NodeId TaskControlStateMachineId { get; } = new(1602, 2);

            public NodeId TaskModulesFolderId { get; } = new(1603, 2);

            public NodeId TaskModuleId { get; } = new(1604, 2);

            public NodeId ReadyStateId { get; } = new(1605, 2);

            public NodeId ReadySubstateMachineId { get; } = new(1606, 2);

            public List<CallMethodRequest> Calls { get; } = [];

            public NodeId CurrentStateNode { get; } = new(1700, 2);

            public NodeId CurrentStateIdNode { get; } = new(1701, 2);

            public NodeId SystemCurrentStateId { get; } = new(1107, 2);

            public NodeId TaskCurrentStateId { get; } = new(1607, 2);

            public NodeId MotorTemperatureId { get; } = new(1404, 2);

            public EUInformation Celsius { get; } = new()
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4408652,
                DisplayName = new LocalizedText("en", "°C"),
                Description = new LocalizedText("en", "degree Celsius")
            };

            public void ConfigureCompleteTopology()
            {
                // The layout of OPC 40010-1: folders and non-parameter members with
                // Robotics browse names, identification with DI browse names, process
                // values below DI:ParameterSet. Decoys with the same name in namespace 0
                // or on the wrong level catch a client that ignores either.
                AddChild(SystemId, Rob(RoboticsBrowseNames.Controllers), ControllersFolderId);
                AddChild(SystemId, Rob(RoboticsBrowseNames.MotionDevices), MotionDevicesFolderId);
                AddChild(SystemId, Rob(RoboticsBrowseNames.SafetyStates), SafetyStatesFolderId);
                AddBrowse(ControllersFolderId, [Ref(ControllerId, "Controller", RoboticsModel.ControllerType)]);
                AddBrowse(MotionDevicesFolderId, [Ref(MotionDeviceId, "Motion", RoboticsModel.MotionDeviceType)]);
                AddBrowse(SafetyStatesFolderId, [Ref(SafetyId, "Safety", ObjectTypes.SafetyStateType)]);

                AddIdentification(SystemId, "System");
                AddIdentification(ControllerId, "Controller");
                AddChild(ControllerId, Rob(RoboticsBrowseNames.TaskControls), TaskControlsFolderId);
                AddChild(ControllerId, Rob(RoboticsBrowseNames.Components), ControllerComponentsFolderId);
                AddChild(ControllerId, Rob(RoboticsBrowseNames.Programs), ProgramsId);
                AddBrowse(TaskControlsFolderId, [Ref(TaskControlId, "Task", ObjectTypes.TaskControlType)]);
                AddBrowse(
                ControllerComponentsFolderId,
                [Ref(ControllerComponentId, "Component", ObjectTypes.AuxiliaryComponentType)]);

                AddIdentification(MotionDeviceId, "Motion");
                AddChild(MotionDeviceId, Rob(RoboticsBrowseNames.Axes), AxesFolderId);
                AddChild(MotionDeviceId, Rob(RoboticsBrowseNames.PowerTrains), PowerTrainsFolderId);
                AddChild(MotionDeviceId, Rob(RoboticsBrowseNames.AdditionalComponents), AdditionalComponentsFolderId);
                AddChild(MotionDeviceId, Rob(RoboticsBrowseNames.FlangeLoad), FlangeLoadId);
                AddValueChild(MotionDeviceId, Rob(RoboticsBrowseNames.MotionDeviceCategory),
                    (int)MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT);
                AddParameter(MotionDeviceId, RoboticsBrowseNames.SpeedOverride, 0.75d);
                AddValueChild(MotionDeviceId, Ua(RoboticsBrowseNames.SpeedOverride), -1d);
                AddBrowse(AxesFolderId, [Ref(AxisId, "Axis", RoboticsModel.AxisType)]);
                AddBrowse(PowerTrainsFolderId, [Ref(PowerTrainId, "PowerTrain", ObjectTypes.PowerTrainType)]);
                AddBrowse(AdditionalComponentsFolderId, []);

                AddIdentification(AxisId, "Axis");
                AddChild(AxisId, Rob(RoboticsBrowseNames.AdditionalLoad), AxisLoadId);
                AddParameter(AxisId, RoboticsBrowseNames.ActualPosition, 12.5d, AxisPositionId);
                AddParameter(AxisId, RoboticsBrowseNames.ActualSpeed, 3.0d, AxisSpeedId);
                AddParameter(AxisId, RoboticsBrowseNames.ActualAcceleration, 1.5d, AxisAccelerationId);
                AddValueChild(AxisId, Rob(RoboticsBrowseNames.ActualPosition), -1d);
                AddValueChild(AxisId, Rob(RoboticsBrowseNames.MotionProfile), (int)AxisMotionProfileEnumeration.ROTARY,
                    AxisMotionProfileId);
                AddLoad(AxisLoadId);
                AddLoad(FlangeLoadId);

                AddIdentification(PowerTrainId, "PowerTrain");
                // Motors and gears instantiate the <MotorIdentifier>/<GearIdentifier>
                // placeholders, so a real server exposes them under their own browse
                // names; nothing is reachable under the placeholder names.
                AddComponent(PowerTrainId, Ref(MotorId, "Motor1", ObjectTypes.MotorType));
                AddComponent(PowerTrainId, Ref(GearId, "Gear1", ObjectTypes.GearType));
                AddIdentification(MotorId, "Motor");
                // MotorTemperature with its unit and range, BrakeReleased; no
                // EffectiveLoadRate. The same name directly below the motor is a decoy.
                AddParameter(MotorId, RoboticsBrowseNames.MotorTemperature, 41.5d, MotorTemperatureId);
                AddValueChild(MotorTemperatureId, Ua(Opc.Ua.BrowseNames.EngineeringUnits), Celsius);
                AddValueChild(MotorTemperatureId, Ua(Opc.Ua.BrowseNames.EURange), new Range(120, -20));
                AddParameter(MotorId, RoboticsBrowseNames.BrakeReleased, true);
                AddValueChild(MotorId, Rob(RoboticsBrowseNames.MotorTemperature), -1d);
                AddIdentification(GearId, "Gear");
                AddValueChild(GearId, Rob(RoboticsBrowseNames.Pitch), 1.0d);
                AddIdentification(DriveId, "Drive");

                AddIdentification(SafetyId, "Safety");
                AddChild(SafetyId, Rob(RoboticsBrowseNames.EmergencyStopFunctions), EmergencyFunctionsFolderId);
                AddChild(SafetyId, Rob(RoboticsBrowseNames.ProtectiveStopFunctions), ProtectiveFunctionsFolderId);
                AddParameter(SafetyId, RoboticsBrowseNames.EmergencyStop, false);
                AddParameter(SafetyId, RoboticsBrowseNames.OperationalMode, 1);
                AddParameter(SafetyId, RoboticsBrowseNames.ProtectiveStop, true);
                AddBrowse(
                EmergencyFunctionsFolderId,
                [Ref(EmergencyFunctionId, "EStop", ObjectTypes.EmergencyStopFunctionType)]);
                AddBrowse(
                ProtectiveFunctionsFolderId,
                [Ref(ProtectiveFunctionId, "PStop", ObjectTypes.ProtectiveStopFunctionType)]);
                AddSafetyFunction(EmergencyFunctionId, "EStop");
                AddSafetyFunction(ProtectiveFunctionId, "PStop");

                AddIdentification(TaskControlId, "TaskControl");
                AddChild(TaskControlId, Rob(RoboticsBrowseNames.TaskControlOperation), TaskControlOperationId);
                AddChild(TaskControlId, Rob(RoboticsBrowseNames.TaskModules), TaskModulesFolderId);
                AddParameter(TaskControlId, RoboticsBrowseNames.ExecutionMode, 1);
                AddParameter(TaskControlId, RoboticsBrowseNames.TaskProgramLoaded, true);
                AddParameter(TaskControlId, RoboticsBrowseNames.TaskProgramName, "Program");
                AddBrowse(TaskModulesFolderId, [Ref(TaskModuleId, "Module", ObjectTypes.TaskModuleType)]);
                AddValueChild(TaskModuleId, Rob(RoboticsBrowseNames.Name), "Module");
                AddValueChild(TaskModuleId, Rob(RoboticsBrowseNames.Version), "1.0");
                AddValueChild(TaskModuleId, Rob(RoboticsBrowseNames.IsReferenced), true);

                AddChild(ControllerId, Rob(RoboticsBrowseNames.SystemOperation), SystemOperationId);
                AddOperationState(
                    SystemOperationId,
                    RoboticsBrowseNames.SystemOperationStateMachine,
                    SystemStateMachineId,
                    SystemCurrentStateId,
                    RoboticsBrowseNames.Executing);
                AddOperationState(
                    TaskControlOperationId,
                    RoboticsBrowseNames.TaskControlStateMachine,
                    TaskControlStateMachineId,
                    TaskCurrentStateId,
                    RoboticsBrowseNames.Ready);

                AddRelationship(ControllerId, ReferenceTypes.Controls, MotionDeviceId);
                AddRelationship(ControllerId, ReferenceTypes.HasSafetyStates, SafetyId);
                AddRelationship(PowerTrainId, ReferenceTypes.Moves, AxisId);
                AddRelationship(AxisId, ReferenceTypes.Requires, PowerTrainId);
                AddRelationship(PowerTrainId, ReferenceTypes.HasSlave, PowerTrainId);
                AddRelationship(MotorId, ReferenceTypes.IsDrivenBy, DriveId);
                AddRelationship(MotionDeviceId, ReferenceTypes.IsConnectedTo, ControllerId);
            }

            public void ConfigureSystemOperation(StatusCode statusCode, int output)
            {
                m_callStatus = statusCode;
                m_callOutput = output;
                AddChild(SystemOperationId, Rob(RoboticsBrowseNames.SystemOperationStateMachine), SystemStateMachineId);
                AddStateReads(SystemStateMachineId, RoboticsBrowseNames.Ready);
            }

            public void ConfigureTaskControl(StatusCode statusCode, int output, bool readySubstateMachine = true)
            {
                m_callStatus = statusCode;
                m_callOutput = output;
                AddChild(TaskControlId, Rob(RoboticsBrowseNames.TaskControlOperation), TaskControlOperationId);
                AddChild(
                TaskControlOperationId,
                Rob(RoboticsBrowseNames.TaskControlStateMachine),
                TaskControlStateMachineId);
                AddChild(TaskControlStateMachineId, Rob(RoboticsBrowseNames.Ready), ReadyStateId);
                if (readySubstateMachine)
                {
                    AddChild(
                    TaskControlStateMachineId,
                    Rob(RoboticsBrowseNames.ReadySubstateMachine),
                    ReadySubstateMachineId);
                }
                AddStateReads(TaskControlStateMachineId, RoboticsBrowseNames.Ready);
            }

            /// <summary>
            /// Registers an operation state machine below its operation object with
            /// a CurrentState variable that names <paramref name="stateName"/>; no
            /// value when <paramref name="stateName"/> is null, so its read fails.
            /// CurrentState has no Id property until <see cref="SetCurrentStateId"/>
            /// adds one, so the client falls back to the state name.
            /// </summary>
            public void AddOperationState(
                NodeId operation,
                string stateMachineBrowseName,
                NodeId stateMachine,
                NodeId currentState,
                string? stateName)
            {
                AddChild(operation, Rob(stateMachineBrowseName), stateMachine);
                AddChild(stateMachine, Ua(Opc.Ua.BrowseNames.CurrentState), currentState);
                if (stateName == null)
                {
                    m_values.Remove(currentState);
                }
                else
                {
                    m_values[currentState] = Variant.From(new LocalizedText(stateName));
                }
            }

            /// <summary>
            /// The node the harness registered at <paramref name="path"/> below
            /// <paramref name="start"/>, to compare a snapshot id against.
            /// </summary>
            public NodeId Resolve(NodeId start, params QualifiedName[] path)
            {
                NodeId current = start;
                foreach (QualifiedName name in path)
                {
                    current = m_children[(current, name)];
                }
                return current;
            }

            /// <summary>
            /// Makes every read of <paramref name="node"/> return its value with a bad status.
            /// </summary>
            public void SetBadQuality(NodeId node)
            {
                m_badQualityNodes.Add(node);
            }

            /// <summary>
            /// Registers the Idle, Ready and Executing state objects of an operation state
            /// machine and points its CurrentState/Id at <paramref name="activeState"/>, so
            /// the state is known by NodeId whatever text the server localizes it to.
            /// </summary>
            public void AddStateNodes(
                NodeId stateMachine,
                NodeId currentState,
                NodeId idle,
                NodeId ready,
                NodeId executing,
                NodeId activeState)
            {
                AddChild(stateMachine, Rob(RoboticsBrowseNames.Idle), idle);
                AddChild(stateMachine, Rob(RoboticsBrowseNames.Ready), ready);
                AddChild(stateMachine, Rob(RoboticsBrowseNames.Executing), executing);
                SetCurrentStateId(currentState, activeState);
            }

            /// <summary>
            /// Publishes the CurrentState/Id property of <paramref name="currentState"/>
            /// holding <paramref name="stateId"/>, the NodeId of the active state.
            /// </summary>
            public void SetCurrentStateId(NodeId currentState, NodeId stateId)
            {
                AddValueChild(currentState, Ua(Opc.Ua.BrowseNames.Id), stateId);
            }

            public void AddStateReads(NodeId stateMachine, string stateName)
            {
                AddChild(stateMachine, Ua(Opc.Ua.BrowseNames.CurrentState), CurrentStateNode);
                AddChild(CurrentStateNode, Ua(Opc.Ua.BrowseNames.Id), CurrentStateIdNode);
                m_values[CurrentStateNode] = Variant.From(new LocalizedText(stateName));
                m_values[CurrentStateIdNode] = Variant.From(new NodeId(999, 2));
            }

            public Mock<IStreamingSubscription> Streaming(NodeId nodeId)
            {
                var streaming = new Mock<IStreamingSubscription>();
                streaming.Setup(s => s.SubscribeDataChangesAsync(
                    It.IsAny<NodeId>(), It.IsAny<Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(SingleChange(nodeId));
                streaming.Setup(s => s.SubscribeDataChangesAsync(
                    It.IsAny<IReadOnlyList<NodeId>>(),
                    It.IsAny<Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(SingleChange(nodeId));
                return streaming;
            }

            public ReferenceDescription Ref(NodeId nodeId, string browseName, uint typeId)
            {
                return new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(nodeId),
                    BrowseName = new QualifiedName(browseName, 2),
                    DisplayName = new LocalizedText(browseName),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = new ExpandedNodeId(new NodeId(typeId, RoboticsNamespaceIndex)),
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                };
            }

            public void AddBrowse(NodeId folder, IReadOnlyList<ReferenceDescription> references)
            {
                m_browse[folder] = [.. references];
            }

            public void AddComponent(NodeId parent, ReferenceDescription reference)
            {
                if (!m_browse.TryGetValue(parent, out List<ReferenceDescription>? list))
                {
                    list = [];
                    m_browse[parent] = list;
                }
                reference.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                list.Add(reference);
            }

            public void EnableBrowseContinuationFor(NodeId folder)
            {
                m_continuationNodes.Add(folder);
            }

            public ushort RoboticsNamespaceIndex =>
                (ushort)NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics);

            public QualifiedName Rob(string name)
            {
                return new QualifiedName(name, RoboticsNamespaceIndex);
            }

            public QualifiedName Di(string name)
            {
                return new QualifiedName(name, (ushort)NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi));
            }

            public static QualifiedName Ua(string name)
            {
                return new QualifiedName(name);
            }

            /// <summary>
            /// Registers a child under its full browse name, namespace included:
            /// a path resolves only when every element names the child exactly.
            /// </summary>
            public void AddChild(NodeId parent, QualifiedName browseName, NodeId child)
            {
                m_children[(parent, browseName)] = child;
            }

            private void AddValueChild(NodeId parent, QualifiedName browseName, object value)
            {
                AddValueChild(
                parent, browseName, value,
                new NodeId((uint)Math.Abs(HashCode.Combine(parent, browseName)), 2));
            }

            private void AddValueChild(NodeId parent, QualifiedName browseName, object value, NodeId nodeId)
            {
                AddChild(parent, browseName, nodeId);
                m_values[nodeId] = ToVariant(value);
            }

            /// <summary>
            /// Registers a process value below the DI ParameterSet of its component.
            /// </summary>
            private void AddParameter(NodeId parent, string robName, object value, NodeId nodeId = default)
            {
                if (!m_parameterSets.TryGetValue(parent, out NodeId parameterSet))
                {
                    parameterSet = new NodeId((uint)Math.Abs(HashCode.Combine(parent, "ParameterSet")), 2);
                    m_parameterSets[parent] = parameterSet;
                    AddChild(parent, Di(DiBrowseNames.ParameterSet), parameterSet);
                }
                if (nodeId.IsNull)
                {
                    AddValueChild(parameterSet, Rob(robName), value);
                }
                else
                {
                    AddValueChild(parameterSet, Rob(robName), value, nodeId);
                }
            }

            private void AddIdentification(NodeId nodeId, string name)
            {
                AddValueChild(nodeId, Di(DiBrowseNames.ComponentName), new LocalizedText(name));
                AddValueChild(nodeId, Di(DiBrowseNames.AssetId), name + "Asset");
                AddValueChild(nodeId, Di(DiBrowseNames.Manufacturer), new LocalizedText("OPC"));
                AddValueChild(nodeId, Di(DiBrowseNames.Model), new LocalizedText(name + "Model"));
                AddValueChild(nodeId, Di(DiBrowseNames.ProductCode), name + "Code");
                AddValueChild(nodeId, Di(DiBrowseNames.SerialNumber), name + "Serial");
                AddValueChild(nodeId, Di(DiBrowseNames.DeviceManual), name + "Manual");
                AddValueChild(nodeId, Di(DiBrowseNames.HardwareRevision), name + "Hardware");
                AddValueChild(nodeId, Di(DiBrowseNames.SoftwareRevision), name + "Software");
                AddValueChild(nodeId, Di(DiBrowseNames.ManufacturerUri), "urn:opc:" + name);
                AddValueChild(nodeId, Di(DiBrowseNames.ProductInstanceUri), "urn:opc:" + name + ":1");
                AddValueChild(nodeId, Ua(DiBrowseNames.SerialNumber), "wrong namespace");
                AddValueChild(nodeId, Ua(DiBrowseNames.SoftwareRevision), "wrong namespace");
            }

            private void AddLoad(NodeId load)
            {
                AddValueChild(load, Rob(RoboticsBrowseNames.Mass), 10.0d);
                AddValueChild(load, Rob(RoboticsBrowseNames.CenterOfMass), "center");
                AddValueChild(load, Rob(RoboticsBrowseNames.Inertia), "inertia");
            }

            private void AddSafetyFunction(NodeId nodeId, string name)
            {
                AddValueChild(nodeId, Rob(RoboticsBrowseNames.Name), name);
                AddValueChild(nodeId, Rob(RoboticsBrowseNames.Active), true);
                AddValueChild(nodeId, Rob(RoboticsBrowseNames.Enabled), true);
            }

            private void AddRelationship(NodeId source, uint referenceType, NodeId target)
            {
                NodeId referenceTypeId = new(
                    referenceType,
                    (ushort)NamespaceUris.GetIndex(global::Opc.Ua.Robotics.Namespaces.Robotics));
                if (!m_browse.TryGetValue(source, out List<ReferenceDescription>? list))
                {
                    list = [];
                    m_browse[source] = list;
                }
                list.Add(new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(target),
                    BrowseName = new QualifiedName("rel", 2),
                    DisplayName = new LocalizedText("rel"),
                    ReferenceTypeId = referenceTypeId,
                    IsForward = true,
                    NodeClass = NodeClass.Object
                });
            }

            private void SetupTranslate()
            {
                Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>((_, paths, _) =>
                    {
                        var results = new List<BrowsePathResult>();
                        for (int ii = 0; ii < paths.Count; ii++)
                        {
                            BrowsePath path = paths[ii];
                            NodeId current = path.StartingNode;
                            bool found = true;
                            for (int jj = 0; jj < path.RelativePath.Elements.Count; jj++)
                            {
                                QualifiedName name = path.RelativePath.Elements[jj].TargetName;
                                if (!m_children.TryGetValue((current, name), out NodeId next))
                                {
                                    found = false;
                                    break;
                                }
                                current = next;
                            }
                            results.Add(found ? GoodPath(current) : BadPath());
                        }
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = results.ToArrayOf(),
                                DiagnosticInfos = default
                            });
                    });
            }

            private void SetupBrowse()
            {
                Session.Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                        (_, _, _, descriptions, _) =>
                        {
                            var results = new List<BrowseResult>(descriptions.Count);
                            for (int ii = 0; ii < descriptions.Count; ii++)
                            {
                                BrowseDescription description = descriptions[ii];
                                List<ReferenceDescription> refs = m_browse.TryGetValue(
                                    description.NodeId, out List<ReferenceDescription>? value)
                                    ? value.Where(r =>
                                        description.ReferenceTypeId.IsNull ||
                                        r.ReferenceTypeId == description.ReferenceTypeId ||
                                        description.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.References ||
                                        description.ReferenceTypeId ==
                                            Opc.Ua.ReferenceTypeIds.HierarchicalReferences).ToList()
                                    : [];
                                bool continuation = m_continuationNodes.Remove(description.NodeId) && refs.Count > 0;
                                results.Add(new BrowseResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    References = continuation ? [refs[0]] : refs.ToArrayOf(),
                                    ContinuationPoint = continuation ? new ByteString(new byte[] { 1 }) : default
                                });
                            }
                            return new ValueTask<BrowseResponse>(new BrowseResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = results.ToArrayOf(),
                                DiagnosticInfos = default
                            });
                        });
                Session.Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = [] }],
                        DiagnosticInfos = default
                    }));
            }

            private void SetupRead()
            {
                Session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                        (_, _, _, nodes, _) =>
                        {
                            var values = new List<DataValue>();
                            for (int ii = 0; ii < nodes.Count; ii++)
                            {
                                ReadValueId node = nodes[ii];
                                if (node.AttributeId == Attributes.BrowseName)
                                {
                                    values.Add(Value(new QualifiedName(
                        "Browse" + ii.ToString(System.Globalization.CultureInfo.InvariantCulture), 2)));
                                }
                                else if (node.AttributeId == Attributes.DisplayName)
                                {
                                    values.Add(Value(new LocalizedText(
                        "Display" + ii.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                                }
                                else
                                {
                                    values.Add(m_values.TryGetValue(node.NodeId, out Variant variant)
                                        ? m_badQualityNodes.Contains(node.NodeId)
                                            ? new DataValue(variant, StatusCodes.BadSensorFailure)
                                            : Value(variant)
                                        : new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown));
                                }
                            }
                            return new ValueTask<ReadResponse>(new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = values.ToArrayOf(),
                                DiagnosticInfos = default
                            });
                        });
            }

            private void SetupCall()
            {
                Session.Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                    {
                        Calls.AddRange(requests.ToList());
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new CallMethodResult
                                {
                                    StatusCode = m_callStatus,
                                    OutputArguments = [Variant.From(m_callOutput)]
                                }
                            ],
                            DiagnosticInfos = default
                        });
                    });
            }

            private static async IAsyncEnumerable<DataValueChange> SingleChange(NodeId nodeId)
            {
                yield return new DataValueChange(null, Value(nodeId), null);
                await Task.CompletedTask.ConfigureAwait(false);
            }

            private static BrowsePathResult GoodPath(NodeId nodeId)
            {
                return new BrowsePathResult
                {
                    StatusCode = StatusCodes.Good,
                    Targets = [new BrowsePathTarget { TargetId = new ExpandedNodeId(nodeId) }]
                };
            }

            private static BrowsePathResult BadPath()
            {
                return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] };
            }

            private static DataValue Value(object value)
            {
                return Value(ToVariant(value));
            }

            private static DataValue Value(Variant value)
            {
                return new DataValue(value, StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow);
            }

            private static Variant ToVariant(object value)
            {
                return value switch
                {
                    bool b => Variant.From(b),
                    int i => Variant.From(i),
                    ushort u => Variant.From(u),
                    double d => Variant.From(d),
                    string s => Variant.From(s),
                    NodeId n => Variant.From(n),
                    QualifiedName q => Variant.From(q),
                    LocalizedText l => Variant.From(l),
                    IEncodeable e => Variant.FromStructure(e),
                    _ => Variant.Null
                };
            }
        }
    }
}
