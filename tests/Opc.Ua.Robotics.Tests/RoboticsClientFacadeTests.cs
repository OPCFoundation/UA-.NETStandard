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
using RoboticsBrowseNames = Opc.Ua.Robotics.BrowseNames;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;

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
                Assert.That(snapshot.Controllers[0].TaskControlIds.ToList(), Does.Contain(h.TaskControlId));
                Assert.That(snapshot.MotionDevices[0].AxisIds.ToList(), Does.Contain(h.AxisId));
                Assert.That(snapshot.MotionDevices[0].FlangeLoadId, Is.EqualTo(h.FlangeLoadId));
                Assert.That(
                    snapshot.Axes[0].State.ActualPosition.WrappedValue.TryGetValue(out double position),
                    Is.True);
                Assert.That(position, Is.EqualTo(12.5d));
                Assert.That(snapshot.Loads.ToList().Any(l => l.NodeId == h.FlangeLoadId), Is.True);
                Assert.That(snapshot.PowerTrains[0].MotorIds.ToList(), Does.Contain(h.MotorId));
                Assert.That(snapshot.Motors[0].Identification.NodeId, Is.EqualTo(h.MotorId));
                Assert.That(snapshot.Gears[0].Identification.NodeId, Is.EqualTo(h.GearId));
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
        public async Task ProgramsAsyncReturnsFileSystemClientAndFailsWhenAbsent()
        {
            RoboticsSessionHarness h = new();
            h.AddChild(h.ControllerId, RoboticsBrowseNames.Programs, h.ProgramsId);

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
                Assert.That(safety.EmergencyStop.WrappedValue.TryGetValue(out bool emergency), Is.True);
                Assert.That(emergency, Is.False);
            });
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
            private readonly Dictionary<(NodeId Parent, string BrowseName), NodeId> m_children = [];
            private readonly Dictionary<NodeId, List<ReferenceDescription>> m_browse = [];
            private readonly Dictionary<NodeId, Variant> m_values = [];
            private readonly HashSet<NodeId> m_continuationNodes = [];
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
                NodeCache.Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(true));
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

            public NodeId CurrentStateNode { get; } = new(1700, 2);

            public NodeId CurrentStateIdNode { get; } = new(1701, 2);

            public void ConfigureCompleteTopology()
            {
                AddChild(SystemId, RoboticsBrowseNames.Controllers, ControllersFolderId);
                AddChild(SystemId, RoboticsBrowseNames.MotionDevices, MotionDevicesFolderId);
                AddChild(SystemId, RoboticsBrowseNames.SafetyStates, SafetyStatesFolderId);
                AddBrowse(ControllersFolderId, [Ref(ControllerId, "Controller", RoboticsModel.ControllerType)]);
                AddBrowse(MotionDevicesFolderId, [Ref(MotionDeviceId, "Motion", RoboticsModel.MotionDeviceType)]);
                AddBrowse(SafetyStatesFolderId, [Ref(SafetyId, "Safety", ObjectTypes.SafetyStateType)]);

                AddIdentification(SystemId, "System");
                AddIdentification(ControllerId, "Controller");
                AddChild(ControllerId, RoboticsBrowseNames.TaskControls, TaskControlsFolderId);
                AddChild(ControllerId, RoboticsBrowseNames.Components, ControllerComponentsFolderId);
                AddChild(ControllerId, RoboticsBrowseNames.Programs, ProgramsId);
                AddBrowse(TaskControlsFolderId, [Ref(TaskControlId, "Task", ObjectTypes.TaskControlType)]);
                AddBrowse(
                ControllerComponentsFolderId,
                [Ref(ControllerComponentId, "Component", ObjectTypes.AuxiliaryComponentType)]);

                AddIdentification(MotionDeviceId, "Motion");
                AddChild(MotionDeviceId, RoboticsBrowseNames.Axes, AxesFolderId);
                AddChild(MotionDeviceId, RoboticsBrowseNames.PowerTrains, PowerTrainsFolderId);
                AddChild(MotionDeviceId, RoboticsBrowseNames.AdditionalComponents, AdditionalComponentsFolderId);
                AddChild(MotionDeviceId, RoboticsBrowseNames.FlangeLoad, FlangeLoadId);
                AddValueChild(MotionDeviceId, RoboticsBrowseNames.MotionDeviceCategory,
                    (int)MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT);
                AddValueChild(MotionDeviceId, RoboticsBrowseNames.SpeedOverride, 0.75d);
                AddBrowse(AxesFolderId, [Ref(AxisId, "Axis", RoboticsModel.AxisType)]);
                AddBrowse(PowerTrainsFolderId, [Ref(PowerTrainId, "PowerTrain", ObjectTypes.PowerTrainType)]);
                AddBrowse(AdditionalComponentsFolderId, []);

                AddIdentification(AxisId, "Axis");
                AddChild(AxisId, RoboticsBrowseNames.AdditionalLoad, AxisLoadId);
                AddValueChild(AxisId, RoboticsBrowseNames.ActualPosition, 12.5d, AxisPositionId);
                AddValueChild(AxisId, RoboticsBrowseNames.ActualSpeed, 3.0d, AxisSpeedId);
                AddValueChild(AxisId, RoboticsBrowseNames.ActualAcceleration, 1.5d, AxisAccelerationId);
                AddValueChild(AxisId, RoboticsBrowseNames.MotionProfile, (int)AxisMotionProfileEnumeration.ROTARY,
                    AxisMotionProfileId);
                AddLoad(AxisLoadId);
                AddLoad(FlangeLoadId);

                AddIdentification(PowerTrainId, "PowerTrain");
                AddChild(PowerTrainId, RoboticsBrowseNames.MotorIdentifier_Placeholder, MotorId);
                AddChild(PowerTrainId, "MotorIdentifier_Placeholder", MotorId);
                AddChild(PowerTrainId, RoboticsBrowseNames.GearIdentifier_Placeholder, GearId);
                AddChild(PowerTrainId, "GearIdentifier_Placeholder", GearId);
                AddIdentification(MotorId, "Motor");
                AddChild(MotorId, RoboticsBrowseNames.DriveIdentifier_Placeholder, DriveId);
                AddChild(MotorId, "DriveIdentifier_Placeholder", DriveId);
                AddIdentification(GearId, "Gear");
                AddValueChild(GearId, RoboticsBrowseNames.Pitch, 1.0d);
                AddIdentification(DriveId, "Drive");

                AddIdentification(SafetyId, "Safety");
                AddChild(SafetyId, RoboticsBrowseNames.EmergencyStopFunctions, EmergencyFunctionsFolderId);
                AddChild(SafetyId, RoboticsBrowseNames.ProtectiveStopFunctions, ProtectiveFunctionsFolderId);
                AddValueChild(SafetyId, RoboticsBrowseNames.EmergencyStop, false);
                AddValueChild(SafetyId, RoboticsBrowseNames.OperationalMode, 1);
                AddValueChild(SafetyId, RoboticsBrowseNames.ProtectiveStop, true);
                AddBrowse(
                EmergencyFunctionsFolderId,
                [Ref(EmergencyFunctionId, "EStop", ObjectTypes.EmergencyStopFunctionType)]);
                AddBrowse(
                ProtectiveFunctionsFolderId,
                [Ref(ProtectiveFunctionId, "PStop", ObjectTypes.ProtectiveStopFunctionType)]);
                AddSafetyFunction(EmergencyFunctionId, "EStop");
                AddSafetyFunction(ProtectiveFunctionId, "PStop");

                AddIdentification(TaskControlId, "TaskControl");
                AddChild(TaskControlId, RoboticsBrowseNames.TaskControlOperation, TaskControlOperationId);
                AddChild(TaskControlId, RoboticsBrowseNames.TaskModules, TaskModulesFolderId);
                AddValueChild(TaskControlId, RoboticsBrowseNames.ExecutionMode, 1);
                AddValueChild(TaskControlId, RoboticsBrowseNames.TaskProgramLoaded, true);
                AddValueChild(TaskControlId, RoboticsBrowseNames.TaskProgramName, "Program");
                AddBrowse(TaskModulesFolderId, [Ref(TaskModuleId, "Module", ObjectTypes.TaskModuleType)]);
                AddValueChild(TaskModuleId, RoboticsBrowseNames.Name, "Module");
                AddValueChild(TaskModuleId, "Version", "1.0");
                AddValueChild(TaskModuleId, RoboticsBrowseNames.IsReferenced, true);

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
                AddChild(SystemOperationId, RoboticsBrowseNames.SystemOperationStateMachine, SystemStateMachineId);
                AddStateReads(SystemStateMachineId, RoboticsBrowseNames.Ready);
            }

            public void ConfigureTaskControl(StatusCode statusCode, int output)
            {
                m_callStatus = statusCode;
                m_callOutput = output;
                AddChild(TaskControlId, RoboticsBrowseNames.TaskControlOperation, TaskControlOperationId);
                AddChild(
                TaskControlOperationId,
                RoboticsBrowseNames.TaskControlStateMachine,
                TaskControlStateMachineId);
                AddChild(TaskControlStateMachineId, RoboticsBrowseNames.Ready, new NodeId(1605, 2));
                AddStateReads(TaskControlStateMachineId, RoboticsBrowseNames.Ready);
            }

            public void AddStateReads(NodeId stateMachine, string stateName)
            {
                AddChild(stateMachine, Opc.Ua.BrowseNames.CurrentState, CurrentStateNode);
                AddChild(CurrentStateNode, Opc.Ua.BrowseNames.Id, CurrentStateIdNode);
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
                    TypeDefinition = new ExpandedNodeId(new NodeId(typeId, 2)),
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    IsForward = true
                };
            }

            public void AddBrowse(NodeId folder, IReadOnlyList<ReferenceDescription> references)
            {
                m_browse[folder] = [.. references];
            }

            public void EnableBrowseContinuationFor(NodeId folder)
            {
                m_continuationNodes.Add(folder);
            }

            public void AddChild(NodeId parent, string browseName, NodeId child)
            {
                m_children[(parent, browseName)] = child;
            }

            private void AddValueChild(NodeId parent, string browseName, object value)
            {
                AddValueChild(
                parent, browseName, value,
                new NodeId((uint)Math.Abs(HashCode.Combine(parent, browseName)), 2));
            }

            private void AddValueChild(NodeId parent, string browseName, object value, NodeId nodeId)
            {
                AddChild(parent, browseName, nodeId);
                m_values[nodeId] = ToVariant(value);
            }

            private void AddIdentification(NodeId nodeId, string name)
            {
                AddValueChild(nodeId, DiBrowseNames.ComponentName, new LocalizedText(name));
                AddValueChild(nodeId, DiBrowseNames.AssetId, name + "Asset");
                AddValueChild(nodeId, DiBrowseNames.Manufacturer, new LocalizedText("OPC"));
                AddValueChild(nodeId, DiBrowseNames.Model, new LocalizedText(name + "Model"));
                AddValueChild(nodeId, DiBrowseNames.ProductCode, name + "Code");
                AddValueChild(nodeId, DiBrowseNames.SerialNumber, name + "Serial");
                AddValueChild(nodeId, DiBrowseNames.DeviceManual, name + "Manual");
            }

            private void AddLoad(NodeId load)
            {
                AddValueChild(load, RoboticsBrowseNames.Mass, 10.0d);
                AddValueChild(load, RoboticsBrowseNames.CenterOfMass, "center");
                AddValueChild(load, RoboticsBrowseNames.Inertia, "inertia");
            }

            private void AddSafetyFunction(NodeId nodeId, string name)
            {
                AddValueChild(nodeId, RoboticsBrowseNames.Name, name);
                AddValueChild(nodeId, RoboticsBrowseNames.Active, true);
                AddValueChild(nodeId, RoboticsBrowseNames.Enabled, true);
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
                                string name = path.RelativePath.Elements[jj].TargetName.Name ?? string.Empty;
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
                            BrowseDescription description = descriptions[0];
                            List<ReferenceDescription> refs = m_browse.TryGetValue(
                                description.NodeId, out List<ReferenceDescription>? value)
                                ? value.Where(r =>
                                    description.ReferenceTypeId.IsNull ||
                                    r.ReferenceTypeId == description.ReferenceTypeId ||
                                    description.ReferenceTypeId ==
                                        Opc.Ua.ReferenceTypeIds.HierarchicalReferences).ToList()
                                : [];
                            bool continuation = m_continuationNodes.Remove(description.NodeId) && refs.Count > 0;
                            ArrayOf<ReferenceDescription> returned = continuation ? [refs[0]] : refs.ToArrayOf();
                            return new ValueTask<BrowseResponse>(new BrowseResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results =
                                [
                                    new BrowseResult
                                    {
                                        StatusCode = StatusCodes.Good,
                                        References = returned,
                                        ContinuationPoint = continuation ? new ByteString(new byte[] { 1 }) : default
                                    }
                                ],
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
                                        ? Value(variant)
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
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, _, _) =>
                        new ValueTask<CallResponse>(new CallResponse
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
                        }));
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
                    double d => Variant.From(d),
                    string s => Variant.From(s),
                    NodeId n => Variant.From(n),
                    QualifiedName q => Variant.From(q),
                    LocalizedText l => Variant.From(l),
                    _ => Variant.Null
                };
            }
        }
    }
}
