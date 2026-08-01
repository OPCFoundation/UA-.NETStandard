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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Category("Robotics")]
    [Category("TopologyBuilder")]
    public sealed class RoboticsTopologyBuilderTests
    {
        private RoboticsServerFixture m_fixture = null!;
        private int m_nameCounter;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new RoboticsServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task FullGraphRegistersTypedTopologyAndSemantics()
        {
            var graph = new FullGraph();
            DateTimeUtc telemetryTimestamp = DateTimeUtc.Now;
            var degrees = new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 17476,
                DisplayName = new LocalizedText("deg")
            };
            var range = new global::Opc.Ua.Range
            {
                Low = -180,
                High = 180
            };

            graph.System = await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("Cell"), system =>
                {
                    system.WithComponentName("Robot Cell");
                    graph.MotionDevice = system.AddMotionDevice("Robot", motion =>
                    {
                        motion.WithIdentification(data =>
                            {
                                data.Manufacturer = new LocalizedText("Acme Robotics");
                                data.Model = new LocalizedText("R-100");
                                data.ProductCode = "R100";
                                data.SerialNumber = "ROBOT-1";
                            })
                            .WithCategory(
                                MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT)
                            .WithComponentName("Robot")
                            .WithSpeedOverride(
                                75,
                                StatusCodes.GoodClamped,
                                telemetryTimestamp)
                            .WithFlangeLoad(load => load
                                .WithMass(12.5, engineeringUnits: degrees, range: range)
                                .WithCenterOfMass(new ThreeDFrame())
                                .WithInertia(new ThreeDVector()));
                        graph.Axis = motion.AddAxis("Axis1", axis => axis
                            .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                            .WithActualPosition(
                                12,
                                degrees,
                                range,
                                StatusCodes.Good,
                                telemetryTimestamp)
                            .WithActualSpeed(3, degrees)
                            .WithActualAcceleration(0.5, degrees)
                            .WithAdditionalLoad(load => load.WithMass(2)));
                        graph.PowerTrain = motion.AddPowerTrain(
                            "PowerTrain1",
                            powerTrain =>
                            {
                                powerTrain.WithComponentName("Main train");
                                graph.Motor = powerTrain.AddMotor("Motor1", motor =>
                                    motor.WithIdentification(data =>
                                        {
                                            data.Manufacturer =
                                                new LocalizedText("Motor Corp");
                                            data.ProductCode = "MOTOR";
                                            data.SerialNumber = "M-1";
                                        })
                                        .WithMotorTemperature(42, degrees)
                                        .WithBrakeReleased(true)
                                        .WithEffectiveLoadRate(25));
                                graph.Gear = powerTrain.AddGear("Gear1", gear => gear
                                    .WithIdentification(data =>
                                    {
                                        data.Manufacturer =
                                            new LocalizedText("Gear Corp");
                                        data.ProductCode = "GEAR";
                                        data.SerialNumber = "G-1";
                                    })
                                    .WithGearRatio(-100, 3)
                                    .WithPitch(4.5));
                            });
                        graph.SlavePowerTrain = motion.AddPowerTrain(
                            "SlavePowerTrain",
                            powerTrain =>
                            {
                                graph.SlaveMotor = powerTrain.AddMotor(
                                    "SlaveMotor",
                                    motor => motor.WithMotorTemperature(38));
                            });
                        motion.AddAuxiliaryComponent("RobotIo", component => component
                            .WithProductCode("IO")
                            .WithAssetId("asset-io")
                            .WithComponentName("Robot IO"));
                    });
                    graph.Safety = system.AddSafetyState("Safety", safety =>
                    {
                        safety.WithComponentName("Safety")
                            .WithEmergencyStop(false)
                            .WithOperationalMode(OperationalModeEnumeration.AUTOMATIC)
                            .WithProtectiveStop(true);
                        graph.EmergencyStop = safety.AddEmergencyStop(
                            "EmergencyStop1",
                            "Main E-Stop",
                            stop => stop.WithActive(false));
                        graph.ProtectiveStop = safety.AddProtectiveStop(
                            "ProtectiveStop1",
                            "Guard Door",
                            stop => stop.WithEnabled(true).WithActive(true));
                    });
                    graph.Controller = system.AddController("Controller", controller =>
                    {
                        controller.WithIdentification(data =>
                            {
                                data.Manufacturer = new LocalizedText("Acme Controls");
                                data.ProductCode = "CTRL";
                                data.SerialNumber = "C-1";
                            })
                            .WithComponentName("Controller");
                        graph.Software = controller.AddSoftware(
                            "ControlSoftware",
                            software => software
                                .WithIdentification(data =>
                                {
                                    data.Manufacturer =
                                        new LocalizedText("Acme Controls");
                                    data.Model = new LocalizedText("Robot Runtime");
                                    data.SoftwareRevision = "2.0";
                                })
                                .Configure((state, _) =>
                                    state.DisplayName =
                                        new LocalizedText("Control Software")));
                        graph.Drive = controller.AddDrive("Drive1", drive => drive
                            .WithProductCode("DRV")
                            .WithAssetId("asset-drive")
                            .WithComponentName("Axis drive"));
                        controller.AddAuxiliaryComponent("ControllerIo", component =>
                            component.WithProductCode("CIO"));
                        graph.TaskControl = controller.AddTaskControl(
                            "Task1",
                            taskControl =>
                            {
                                taskControl.WithComponentName("Task")
                                    .WithExecutionMode(
                                        ExecutionModeEnumeration.CONTINUOUS)
                                    .WithTaskProgramLoaded(true)
                                    .WithTaskProgramName("Weld");
                                graph.TaskModule = taskControl.AddTaskModule(
                                    "Module1",
                                    module => module
                                        .WithName("Main")
                                        .WithVersion("1.0")
                                        .WithIsReferenced(true));
                            });
                    });

                    graph.Controller.Controls(graph.MotionDevice)
                        .UsesSafetyState(graph.Safety)
                        .IsConnectedTo(graph.MotionDevice);
                    graph.TaskControl.Controls(graph.MotionDevice);
                    graph.MotionDevice.UsesTaskControl(graph.TaskControl);
                    graph.Axis.Requires(graph.PowerTrain).IsConnectedTo(graph.Gear);
                    graph.PowerTrain.Moves(graph.Axis)
                        .HasSlave(graph.SlavePowerTrain)
                        .IsConnectedTo(graph.Gear);
                    graph.Motor.IsDrivenBy(graph.Drive).IsConnectedTo(graph.Gear);
                    graph.SlaveMotor.IsDrivenBy(graph.Drive);
                    graph.Drive.IsConnectedTo(graph.Gear);
                })
                .ConfigureAwait(false);

            Assert.That(graph.System.State, Is.TypeOf<MotionDeviceSystemState>());
            Assert.That(graph.Controller.State, Is.TypeOf<ControllerState>());
            Assert.That(graph.MotionDevice.State, Is.TypeOf<MotionDeviceState>());
            Assert.That(graph.Axis.State, Is.TypeOf<AxisState>());
            Assert.That(graph.PowerTrain.State, Is.TypeOf<PowerTrainState>());
            Assert.That(graph.Motor.State, Is.TypeOf<MotorState>());
            Assert.That(graph.Gear.State, Is.TypeOf<GearState>());
            Assert.That(graph.Drive.State, Is.TypeOf<DriveState>());
            Assert.That(graph.Software.State, Is.TypeOf<SoftwareState>());
            Assert.That(graph.Safety.State, Is.TypeOf<SafetyStateState>());

            Assert.That(graph.System.State.Controllers, Is.Not.Null);
            Assert.That(graph.System.State.MotionDevices, Is.Not.Null);
            Assert.That(graph.System.State.SafetyStates, Is.Not.Null);
            Assert.That(graph.MotionDevice.State.Axes, Is.Not.Null);
            Assert.That(graph.MotionDevice.State.PowerTrains, Is.Not.Null);
            Assert.That(graph.Controller.State.TaskControls, Is.Not.Null);
            Assert.That(graph.Controller.State.Software, Is.Not.Null);
            Assert.That(graph.Controller.State.Components, Is.Not.Null);

            Assert.That(graph.System.State.Parent, Is.SameAs(
                graph.System.BuildContext.DeviceSet));
            Assert.That(graph.Controller.State.Parent, Is.SameAs(
                graph.System.State.Controllers));
            Assert.That(graph.MotionDevice.State.Parent, Is.SameAs(
                graph.System.State.MotionDevices));
            Assert.That(graph.Safety.State.Parent, Is.SameAs(
                graph.System.State.SafetyStates));
            Assert.That(graph.Axis.State.Parent, Is.SameAs(
                graph.MotionDevice.State.Axes));
            Assert.That(graph.PowerTrain.State.Parent, Is.SameAs(
                graph.MotionDevice.State.PowerTrains));
            Assert.That(graph.Motor.State.Parent, Is.SameAs(graph.PowerTrain.State));
            Assert.That(graph.Gear.State.Parent, Is.SameAs(graph.PowerTrain.State));
            Assert.That(graph.Drive.State.Parent, Is.SameAs(
                graph.Controller.State.Components));
            Assert.That(graph.Software.State.Parent, Is.SameAs(
                graph.Controller.State.Software));
            Assert.That(graph.TaskModule.State.Parent, Is.SameAs(
                graph.TaskControl.State.TaskModules));
            Assert.That(
                graph.TaskModule.State.ReferenceTypeId,
                Is.EqualTo(global::Opc.Ua.ReferenceTypeIds.Organizes));
            Assert.That(graph.Motor.State.Parent, Is.Not.SameAs(graph.Drive.State));

            AssertTreeRegisteredAndUnique(graph.System.State);
            ushort roboticsNamespaceIndex = (ushort)m_fixture.Manager.SystemContext
                .NamespaceUris.GetIndex(Robotics.Namespaces.Robotics);
            Assert.That(
                graph.System.State.NodeId.NamespaceIndex,
                Is.EqualTo(m_fixture.Manager.RoboticsInstanceNamespaceIndex));
            Assert.That(
                graph.System.State.BrowseName.NamespaceIndex,
                Is.EqualTo(m_fixture.Manager.RoboticsInstanceNamespaceIndex));
            Assert.That(
                graph.Controller.State.BrowseName.NamespaceIndex,
                Is.EqualTo(m_fixture.Manager.RoboticsInstanceNamespaceIndex));
            Assert.That(
                graph.System.State.Controllers!.BrowseName.NamespaceIndex,
                Is.EqualTo(roboticsNamespaceIndex));

            AssertReference(
                graph.Controller.State,
                Robotics.ReferenceTypes.Controls,
                graph.MotionDevice.State);
            AssertReference(
                graph.TaskControl.State,
                Robotics.ReferenceTypes.Controls,
                graph.MotionDevice.State);
            AssertReference(
                graph.Controller.State,
                Robotics.ReferenceTypes.HasSafetyStates,
                graph.Safety.State);
            AssertReference(
                graph.PowerTrain.State,
                Robotics.ReferenceTypes.Moves,
                graph.Axis.State);
            AssertReference(
                graph.Axis.State,
                Robotics.ReferenceTypes.Requires,
                graph.PowerTrain.State);
            AssertReference(
                graph.Motor.State,
                Robotics.ReferenceTypes.IsDrivenBy,
                graph.Drive.State);
            AssertReference(
                graph.PowerTrain.State,
                Robotics.ReferenceTypes.HasSlave,
                graph.SlavePowerTrain.State);
            AssertReference(
                graph.Axis.State,
                Robotics.ReferenceTypes.IsConnectedTo,
                graph.Gear.State);

            Assert.That(
                graph.MotionDevice.State.Manufacturer!.Value.Text,
                Is.EqualTo("Acme Robotics"));
            Assert.That(
                graph.MotionDevice.State.MotionDeviceCategory!.Value,
                Is.EqualTo(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT));
            Assert.That(
                FindChild<BaseDataVariableState<double>>(
                    graph.MotionDevice.State.ParameterSet!,
                    "SpeedOverride").Value,
                Is.EqualTo(75));
            Assert.That(
                graph.Axis.State.MotionProfile!.Value,
                Is.EqualTo(AxisMotionProfileEnumeration.ROTARY));
            Assert.That(
                FindChild<AnalogUnitState<double>>(
                    graph.Axis.State.ParameterSet!,
                    "ActualPosition").Value,
                Is.EqualTo(12));
            Assert.That(
                FindChild<AnalogUnitState<double>>(
                    graph.Axis.State.ParameterSet!,
                    "ActualSpeed").Value,
                Is.EqualTo(3));
            Assert.That(
                graph.Gear.State.GearRatio!.Value.Denominator,
                Is.EqualTo(3));
            Assert.That(
                graph.Gear.State.GearRatio.Value.Numerator,
                Is.EqualTo(-100));
            Assert.That(graph.Gear.State.Pitch!.Value, Is.EqualTo(4.5));
            var pitchChildren = new List<BaseInstanceState>();
            graph.Gear.State.Pitch.GetChildren(
                m_fixture.Manager.SystemContext,
                pitchChildren);
            Assert.That(pitchChildren, Is.Empty);
            Assert.That(graph.Drive.State.AssetId!.Value, Is.EqualTo("asset-drive"));
            Assert.That(
                graph.Software.State.SoftwareRevision!.Value,
                Is.EqualTo("2.0"));
            Assert.That(graph.EmergencyStop.State.Name!.Value, Is.EqualTo("Main E-Stop"));
            Assert.That(graph.ProtectiveStop.State.Enabled!.Value, Is.True);
            Assert.That(graph.TaskModule.State.Version!.Value, Is.EqualTo("1.0"));
            Assert.That(graph.MotionDevice.State.TaskControlReference, Is.Null);

            INodeBuilder<MotionDeviceSystemState> rootNode = graph.System.AsNode();
            Assert.That(rootNode.Node, Is.SameAs(graph.System.State));
            Assert.That(
                rootNode.Components().Controllers().Node,
                Is.SameAs(graph.System.State.Controllers));
            Assert.That(
                graph.MotionDevice.AsNode().Components().MotionDeviceCategory().Node,
                Is.SameAs(graph.MotionDevice.State.MotionDeviceCategory));
        }

        [Test]
        public async Task MultipleSystemsHaveDisjointNodeIds()
        {
            IRoboticsBuildContext context = m_fixture.CreateBuildContext();
            GraphParts first = await AddValidGraphAsync(context, NextName("Multi"))
                .ConfigureAwait(false);
            GraphParts second = await AddValidGraphAsync(context, NextName("Multi"))
                .ConfigureAwait(false);

            HashSet<NodeId> firstIds = GetNodeIds(first.System.State);
            HashSet<NodeId> secondIds = GetNodeIds(second.System.State);
            firstIds.IntersectWith(secondIds);

            Assert.That(firstIds, Is.Empty);
        }

        [Test]
        public async Task ConcurrentContextsReserveDisjointNodeIds()
        {
            var manager = new CustomRoboticsNodeManager(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);

                var options = new RoboticsServerOptions
                {
                    InstanceNamespaceUri =
                        CustomRoboticsNodeManager.InstanceNamespaceUri
                };
                IRoboticsBuildContext firstContext =
                    manager.CreateRoboticsBuildContext(options);
                IRoboticsBuildContext secondContext =
                    manager.CreateRoboticsBuildContext(options);
                manager.PauseNextRootRegistrations(2);

                Task<IMotionDeviceSystemBuilder> firstTask = firstContext
                    .AddMotionDeviceSystemAsync(
                        "ConcurrentCell1",
                        system => ConfigureValidGraph(system))
                    .AsTask();
                Task<IMotionDeviceSystemBuilder> secondTask = secondContext
                    .AddMotionDeviceSystemAsync(
                        "ConcurrentCell2",
                        system => ConfigureValidGraph(system))
                    .AsTask();

                try
                {
                    await manager.WaitForPausedRootRegistrationsAsync()
                        .WaitAsync(TimeSpan.FromSeconds(30))
                        .ConfigureAwait(false);
                }
                finally
                {
                    manager.ResumeRootRegistrations();
                }

                IMotionDeviceSystemBuilder[] systems =
                    await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);
                HashSet<NodeId> firstIds = AssertTreeRegisteredAndUnique(
                    manager,
                    systems[0].State,
                    manager.RoboticsInstanceNamespaceIndex);
                HashSet<NodeId> secondIds = AssertTreeRegisteredAndUnique(
                    manager,
                    systems[1].State,
                    manager.RoboticsInstanceNamespaceIndex);
                firstIds.IntersectWith(secondIds);

                Assert.That(firstIds, Is.Empty);
                List<BaseInstanceState> deviceSetChildren =
                    GetChildren(firstContext.Context, firstContext.DeviceSet);
                Assert.That(deviceSetChildren, Does.Contain(systems[0].State));
                Assert.That(deviceSetChildren, Does.Contain(systems[1].State));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public async Task ConcurrentContextsRejectDuplicateRootBrowseName()
        {
            string browseName = NextName("ConcurrentDuplicate");
            IRoboticsBuildContext firstContext = m_fixture.CreateBuildContext();
            IRoboticsBuildContext secondContext = m_fixture.CreateBuildContext();
            using var firstConfigurationStarted = new ManualResetEventSlim();
            using var releaseFirstConfiguration = new ManualResetEventSlim();

            Task<IMotionDeviceSystemBuilder> firstTask = Task.Run(async () =>
                await firstContext.AddMotionDeviceSystemAsync(
                    browseName,
                    system =>
                    {
                        firstConfigurationStarted.Set();
                        if (!releaseFirstConfiguration.Wait(TimeSpan.FromSeconds(30)))
                        {
                            throw new TimeoutException(
                                "The duplicate root BrowseName test was not released.");
                        }
                        ConfigureValidGraph(system);
                    })
                    .ConfigureAwait(false));

            if (!firstConfigurationStarted.Wait(TimeSpan.FromSeconds(30)))
            {
                releaseFirstConfiguration.Set();
                await firstTask.ConfigureAwait(false);
                Assert.Fail("The first duplicate root configuration did not start.");
            }

            IMotionDeviceSystemBuilder? secondSystem = null;
            ServiceResultException? duplicateException = null;
            try
            {
                try
                {
                    secondSystem = await secondContext
                        .AddMotionDeviceSystemAsync(
                            browseName,
                            system => ConfigureValidGraph(system))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    duplicateException = exception;
                }
            }
            finally
            {
                releaseFirstConfiguration.Set();
            }

            IMotionDeviceSystemBuilder firstSystem =
                await firstTask.ConfigureAwait(false);
            _ = AssertTreeRegisteredAndUnique(
                m_fixture.Manager,
                firstSystem.State,
                firstContext.InstanceNamespaceIndex);

            Assert.That(secondSystem, Is.Null);
            Assert.That(duplicateException, Is.Not.Null);
            Assert.That(
                duplicateException!.StatusCode,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));

            List<BaseInstanceState> children =
                GetChildren(firstContext.Context, firstContext.DeviceSet);
            int matchingChildCount = 0;
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii].BrowseName == firstSystem.State.BrowseName)
                {
                    matchingChildCount++;
                }
            }

            Assert.That(matchingChildCount, Is.EqualTo(1));
            Assert.That(children, Does.Contain(firstSystem.State));
        }

        [Test]
        public async Task ConfigureRoboticsForCustomManagerUsesInstanceNamespace()
        {
            var manager = new CustomRoboticsNodeManager(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);

                var systems = new List<IMotionDeviceSystemBuilder>();
                IServiceCollection services = new ServiceCollection();
                services.AddOpcUa()
                    .AddServer(options => options.ApplicationName = "robotics-custom-manager")
                    .ConfigureRoboticsFor<CustomRoboticsNodeManager>(
                        async context =>
                        {
                            systems.Add(await context.AddMotionDeviceSystemAsync(
                                "CustomCell1",
                                system => ConfigureValidGraph(system))
                                .ConfigureAwait(false));
                            systems.Add(await context.AddMotionDeviceSystemAsync(
                                "CustomCell2",
                                system => ConfigureValidGraph(system))
                                .ConfigureAwait(false));
                        });
                services.Configure<RoboticsServerOptions>(
                    options => options.InstanceNamespaceUri =
                        CustomRoboticsNodeManager.InstanceNamespaceUri);

                using ServiceProvider provider = services.BuildServiceProvider();
                IDiPostSetupRunner runner =
                    provider.GetRequiredService<IDiPostSetupRunner>();
                await runner.RunAsync(manager, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(systems, Has.Count.EqualTo(2));
                ushort instanceNamespaceIndex = manager.RoboticsInstanceNamespaceIndex;
                HashSet<NodeId> firstIds = AssertTreeRegisteredAndUnique(
                    manager,
                    systems[0].State,
                    instanceNamespaceIndex);
                HashSet<NodeId> secondIds = AssertTreeRegisteredAndUnique(
                    manager,
                    systems[1].State,
                    instanceNamespaceIndex);
                firstIds.IntersectWith(secondIds);

                Assert.That(firstIds, Is.Empty);
                Assert.That(
                    systems[0].AsNode().Node,
                    Is.SameAs(systems[0].State));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public async Task CustomManagerWithoutRoboticsNodeIdFactoryIsRejected()
        {
            var manager = new UnmarkedRoboticsNodeManager(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);
                var options = new RoboticsServerOptions
                {
                    InstanceNamespaceUri =
                        UnmarkedRoboticsNodeManager.InstanceNamespaceUri
                };

                ServiceResultException exception = Assert.Throws<ServiceResultException>(
                    () => manager.CreateRoboticsBuildContext(options))!;

                Assert.That(
                    exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(
                    exception.Message,
                    Does.Contain(nameof(IRoboticsNodeIdFactory)));
                Assert.That(
                    exception.Message,
                    Does.Contain("ConfigureRoboticsFor/CreateRoboticsBuildContext"));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public async Task NewSkipsExistingNumericNodeId()
        {
            await using var fixture = new RoboticsServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            ushort namespaceIndex = fixture.Manager.RoboticsInstanceNamespaceIndex;
            var existing = new BaseObjectState(null)
            {
                NodeId = new NodeId(1u, namespaceIndex),
                BrowseName = new QualifiedName("Existing", namespaceIndex),
                DisplayName = new LocalizedText("Existing"),
                TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await fixture.Manager.AddPredefinedNodeAsync(existing)
                .ConfigureAwait(false);

            NodeId allocated = fixture.Manager.New(
                fixture.Manager.SystemContext,
                new BaseObjectState(null));

            Assert.That(allocated, Is.EqualTo(new NodeId(2u, namespaceIndex)));
        }

        [Test]
        public async Task BuildContextAllocatorSkipsExistingNumericNodeId()
        {
            await using var fixture = new RoboticsServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            ushort namespaceIndex = fixture.Manager.RoboticsInstanceNamespaceIndex;
            var existing = new BaseObjectState(null)
            {
                NodeId = new NodeId(1u, namespaceIndex),
                BrowseName = new QualifiedName("Existing", namespaceIndex),
                DisplayName = new LocalizedText("Existing"),
                TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            await fixture.Manager.AddPredefinedNodeAsync(existing)
                .ConfigureAwait(false);

            IMotionDeviceSystemBuilder system =
                await fixture.CreateBuildContext()
                    .AddMotionDeviceSystemAsync(
                        "AllocatedAfterExisting",
                        item => ConfigureValidGraph(item))
                    .ConfigureAwait(false);

            Assert.That(system.State.NodeId, Is.EqualTo(new NodeId(2u, namespaceIndex)));
        }

        [Test]
        public async Task StockNewUnindexedReservationIsNotReusedByTopologyBuild()
        {
            await using var fixture = new RoboticsServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            ushort namespaceIndex = fixture.Manager.RoboticsInstanceNamespaceIndex;
            NodeId reserved = fixture.Manager.New(
                fixture.Manager.SystemContext,
                new BaseObjectState(null));

            IMotionDeviceSystemBuilder system =
                await fixture.CreateBuildContext()
                    .AddMotionDeviceSystemAsync(
                        "AllocatedAfterUnindexedReservation",
                        item => ConfigureValidGraph(item))
                    .ConfigureAwait(false);

            HashSet<NodeId> topologyNodeIds = AssertTreeRegisteredAndUnique(
                fixture.Manager,
                system.State,
                namespaceIndex);
            Assert.That(topologyNodeIds, Does.Not.Contain(reserved));
            Assert.That(fixture.Manager.FindPredefinedNode(reserved), Is.Null);
        }

        [Test]
        public async Task FailedBuildsReleaseStockReservations()
        {
            await using var fixture = new RoboticsServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            IRoboticsBuildContext context = fixture.CreateBuildContext();
            const string browseName = "RetryAfterFailedBuild";
            for (int ii = 0; ii < 3; ii++)
            {
                _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await context.AddMotionDeviceSystemAsync(
                        browseName,
                        system =>
                        {
                            ConfigureValidGraph(system);
                            throw new InvalidOperationException(
                                "Expected configuration failure.");
                        })
                        .ConfigureAwait(false));
                Assert.That(fixture.Manager.ReservedNodeIdCount, Is.Zero);

                ServiceResultException validationException =
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await context.AddMotionDeviceSystemAsync(
                            browseName,
                            system => ConfigureValidGraph(
                                system,
                                new GraphOptions { IncludeController = false }))
                            .ConfigureAwait(false))!;
                Assert.That(
                    validationException.StatusCode,
                    Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(fixture.Manager.ReservedNodeIdCount, Is.Zero);

                ServiceResultException nodeIdException =
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await context.AddMotionDeviceSystemAsync(
                            browseName,
                            system =>
                            {
                                ConfigureValidGraph(system);
                                system.Configure((state, _) =>
                                    state.NodeId = NodeId.Null);
                            })
                            .ConfigureAwait(false))!;
                Assert.That(
                    nodeIdException.StatusCode,
                    Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(nodeIdException.Message, Does.Contain("null NodeId"));
                Assert.That(fixture.Manager.ReservedNodeIdCount, Is.Zero);
            }

            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            _ = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await context.AddMotionDeviceSystemAsync(
                    browseName,
                    system => ConfigureValidGraph(system),
                    cancellationSource.Token)
                    .ConfigureAwait(false));
            Assert.That(fixture.Manager.ReservedNodeIdCount, Is.Zero);

            IMotionDeviceSystemBuilder system =
                await context.AddMotionDeviceSystemAsync(
                    browseName,
                    item => ConfigureValidGraph(item))
                    .ConfigureAwait(false);

            _ = AssertTreeRegisteredAndUnique(
                fixture.Manager,
                system.State,
                fixture.Manager.RoboticsInstanceNamespaceIndex);
            Assert.That(fixture.Manager.ReservedNodeIdCount, Is.Zero);
        }

        [Test]
        public async Task AsNodeBeforeRegistrationThrowsBadInvalidState()
        {
            ServiceResultException? observed = null;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("BeforeRegistration"), system =>
                {
                    observed = Assert.Throws<ServiceResultException>(
                        () => system.AsNode());
                    ConfigureValidGraph(system);
                })
                .ConfigureAwait(false);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task AsNodeAndGeneratedTraversalWorkAfterContextSeal()
        {
            IRoboticsBuildContext context = m_fixture.CreateBuildContext();
            var graph = new GraphParts();
            graph.System = await context.AddMotionDeviceSystemAsync(
                NextName("Sealed"),
                system => Copy(ConfigureValidGraph(system), graph))
                .ConfigureAwait(false);

            context.Seal();

            Assert.That(graph.System.AsNode().Node, Is.SameAs(graph.System.State));
            Assert.That(
                graph.System.AsNode().Components().Controllers().Node,
                Is.SameAs(graph.System.State.Controllers));
            Assert.That(
                graph.MotionDevice.AsNode().Components().MotionDeviceCategory().Node,
                Is.SameAs(graph.MotionDevice.State.MotionDeviceCategory));
            Assert.That(graph.Software.AsNode().Node, Is.SameAs(graph.Software.State));
        }

        [Test]
        public async Task SealDuringRegistrationLeavesContextUsable()
        {
            var manager = new CustomRoboticsNodeManager(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);

                var options = new RoboticsServerOptions
                {
                    InstanceNamespaceUri =
                        CustomRoboticsNodeManager.InstanceNamespaceUri
                };
                IRoboticsBuildContext context =
                    manager.CreateRoboticsBuildContext(options);
                int initialDeviceSetChildCount =
                    GetChildren(context.Context, context.DeviceSet).Count;
                manager.PauseNextRootRegistrations(1);

                Task<IMotionDeviceSystemBuilder> registration = context
                    .AddMotionDeviceSystemAsync(
                        "SealActiveCell",
                        system => ConfigureValidGraph(system))
                    .AsTask();

                try
                {
                    await manager.WaitForPausedRootRegistrationsAsync()
                        .WaitAsync(TimeSpan.FromSeconds(30))
                        .ConfigureAwait(false);
                    ServiceResultException exception =
                        Assert.Throws<ServiceResultException>(() => context.Seal())!;
                    Assert.That(
                        exception.StatusCode,
                        Is.EqualTo(StatusCodes.BadInvalidState));
                }
                finally
                {
                    manager.ResumeRootRegistrations();
                }

                IMotionDeviceSystemBuilder first =
                    await registration.ConfigureAwait(false);
                IMotionDeviceSystemBuilder second =
                    await context.AddMotionDeviceSystemAsync(
                        "AfterRejectedSealCell",
                        system => ConfigureValidGraph(system))
                    .ConfigureAwait(false);
                context.Seal();

                _ = AssertTreeRegisteredAndUnique(
                    manager,
                    first.State,
                    manager.RoboticsInstanceNamespaceIndex);
                _ = AssertTreeRegisteredAndUnique(
                    manager,
                    second.State,
                    manager.RoboticsInstanceNamespaceIndex);
                List<BaseInstanceState> deviceSetChildren =
                    GetChildren(context.Context, context.DeviceSet);
                Assert.That(
                    deviceSetChildren,
                    Has.Count.EqualTo(initialDeviceSetChildCount + 2));
                Assert.That(deviceSetChildren, Does.Contain(first.State));
                Assert.That(deviceSetChildren, Does.Contain(second.State));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public async Task SealedContextRejectsTopologyWithoutChangingAddressSpace()
        {
            var manager = new CustomRoboticsNodeManager(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);

                var options = new RoboticsServerOptions
                {
                    InstanceNamespaceUri =
                        CustomRoboticsNodeManager.InstanceNamespaceUri
                };
                IRoboticsBuildContext context =
                    manager.CreateRoboticsBuildContext(options);
                int predefinedNodeCount = manager.PredefinedNodeCount;
                List<BaseInstanceState> deviceSetChildren =
                    GetChildren(context.Context, context.DeviceSet);
                int configureCalls = 0;

                context.Seal();

                ServiceResultException exception =
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await context.AddMotionDeviceSystemAsync(
                            "RejectedAfterSeal",
                            system =>
                            {
                                configureCalls++;
                                ConfigureValidGraph(system);
                            })
                            .ConfigureAwait(false))!;

                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(configureCalls, Is.Zero);
                Assert.That(manager.PredefinedNodeCount, Is.EqualTo(predefinedNodeCount));
                Assert.That(
                    GetChildren(context.Context, context.DeviceSet),
                    Is.EqualTo(deviceSetChildren));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public async Task DirectCustomManagerContextRequiresRoboticsModel()
        {
            var manager = new MissingRoboticsModelNodeManager(
                m_fixture.Server.CurrentInstance,
                m_fixture.Configuration);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences)
                    .ConfigureAwait(false);
                var options = new RoboticsServerOptions
                {
                    InstanceNamespaceUri =
                        MissingRoboticsModelNodeManager.InstanceNamespaceUri
                };

                ServiceResultException exception = Assert.Throws<ServiceResultException>(
                    () => manager.CreateRoboticsBuildContext(options))!;

                Assert.That(
                    exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(exception.Message, Does.Contain("MotionDeviceSystemType"));
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public async Task MissingControllerFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one controller",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeController = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MissingMotionDeviceFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one motion device",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeMotionDevice = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MissingSafetyStateFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one safety state",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeSafetyState = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ControllerWithoutControlsSucceeds()
        {
            GraphParts graph = await AddValidGraphAsync(
                NextName("NoControllerControls"),
                new GraphOptions { AddControllerControls = false })
                .ConfigureAwait(false);

            Assert.That(graph.System.State.NodeId.IsNull, Is.False);
        }

        [Test]
        public async Task ControllerWithoutSoftwareFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one software",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeSoftware = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ControllerWithoutTaskControlFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one task control",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeTaskControl = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MotionDeviceWithoutAxisFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one axis",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeAxis = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MotionDeviceWithoutPowerTrainFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one power train",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions
                    {
                        IncludePowerTrain = false,
                        VirtualAxis = true
                    }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task NonVirtualAxisWithoutRequiresFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "must Requires",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { AddRequires = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task PowerTrainWithoutMotorFailsValidation()
        {
            await AssertConfigurationFailsAsync(
                "at least one motor",
                system => ConfigureValidGraph(
                    system,
                    new GraphOptions { IncludeMotor = false }))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MotorWithoutDriveSucceeds()
        {
            GraphParts graph = await AddValidGraphAsync(
                NextName("NoMotorDrive"),
                new GraphOptions { DriveMotor = false })
                .ConfigureAwait(false);

            Assert.That(graph.System.State.NodeId.IsNull, Is.False);
        }

        [Test]
        public async Task VirtualAxisWithoutRequiresSucceeds()
        {
            GraphParts graph = await AddValidGraphAsync(
                NextName("Virtual"),
                new GraphOptions
                {
                    VirtualAxis = true,
                    AddRequires = false
                })
                .ConfigureAwait(false);

            Assert.That(graph.System.State.NodeId.IsNull, Is.False);
        }

        [Test]
        public async Task CrossScopeRelationshipIsRejected()
        {
            GraphParts first = await AddValidGraphAsync(NextName("ScopeA"))
                .ConfigureAwait(false);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_fixture.CreateBuildContext()
                        .AddMotionDeviceSystemAsync(NextName("ScopeB"), system =>
                        {
                            GraphParts second = ConfigureValidGraph(system);
                            second.Controller.Controls(first.MotionDevice);
                        })
                        .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(exception.Message, Does.Contain("same MotionDeviceSystem"));
        }

        [Test]
        public async Task AsyncBindingsAreAttachedMaterializedAndAwaited()
        {
            var readStarted = new TaskCompletionSource<bool>();
            var releaseRead = new TaskCompletionSource<bool>();
            var writeStarted = new TaskCompletionSource<bool>();
            var releaseWrite = new TaskCompletionSource<bool>();
            DateTimeUtc readTimestamp = DateTimeUtc.Now;
            IMotionDeviceBuilder motionDevice = null!;
            IAxisBuilder axis = null!;
            ISafetyStateBuilder safetyState = null!;
            IEmergencyStopBuilder emergencyStop = null!;
            IProtectiveStopBuilder protectiveStop = null!;
            ITaskControlBuilder taskControl = null!;
            IMotorBuilder motor = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("Callbacks"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    motionDevice = graph.MotionDevice;
                    axis = graph.Axis;
                    safetyState = graph.Safety;
                    motor = graph.Motor;
                    motionDevice
                        .BindSpeedOverrideRead(async cancellationToken =>
                        {
                            readStarted.SetResult(true);
                            await releaseRead.Task.ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            return new DataValue(
                                new Variant(88.0),
                                StatusCodes.Uncertain,
                                readTimestamp);
                        })
                        .BindSpeedOverrideWrite(async (value, cancellationToken) =>
                        {
                            Assert.That(value.TryGetValue(out double typed), Is.True);
                            Assert.That(typed, Is.EqualTo(64));
                            writeStarted.SetResult(true);
                            await releaseWrite.Task.ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            return ServiceResult.Good;
                        });
                    axis.BindActualPosition(
                            _ => new ValueTask<DataValue>(new DataValue(9.0)))
                        .BindActualSpeed(
                            _ => new ValueTask<DataValue>(new DataValue(2.0)))
                        .BindActualAcceleration(
                            _ => new ValueTask<DataValue>(new DataValue(0.2)));
                    motor.BindMotorTemperature(
                            _ => new ValueTask<DataValue>(new DataValue(35.0)))
                        .BindBrakeReleased(
                            _ => new ValueTask<DataValue>(new DataValue(true)))
                        .BindEffectiveLoadRate(
                            _ => new ValueTask<DataValue>(new DataValue((ushort)10)));
                    safetyState
                        .BindEmergencyStop(
                            _ => new ValueTask<DataValue>(new DataValue(false)))
                        .BindOperationalMode(
                            _ => new ValueTask<DataValue>(
                                new DataValue(
                                    Variant.From(
                                        OperationalModeEnumeration.AUTOMATIC))))
                        .BindProtectiveStop(
                            _ => new ValueTask<DataValue>(new DataValue(false)));
                    emergencyStop = safetyState.AddEmergencyStop(
                        "BoundEmergencyStop",
                        "Bound E-Stop",
                        stop => stop.BindActive(
                            _ => new ValueTask<DataValue>(new DataValue(false))));
                    protectiveStop = safetyState.AddProtectiveStop(
                        "BoundProtectiveStop",
                        "Bound Guard",
                        stop => stop
                            .BindActive(
                                _ => new ValueTask<DataValue>(new DataValue(false)))
                            .BindEnabled(
                                _ => new ValueTask<DataValue>(new DataValue(true))));
                    taskControl = graph.Controller.AddTaskControl(
                        "BoundTask",
                        task => task
                            .BindExecutionMode(
                                _ => new ValueTask<DataValue>(
                                    new DataValue(
                                        Variant.From(ExecutionModeEnumeration.STEP))))
                            .BindTaskProgramLoaded(
                                _ => new ValueTask<DataValue>(new DataValue(true)))
                            .BindTaskProgramName(
                                _ => new ValueTask<DataValue>(
                                    new DataValue(Variant.From("Program")))));
                    taskControl.Controls(motionDevice);
                })
                .ConfigureAwait(false);

            BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                motionDevice.State.ParameterSet!,
                "SpeedOverride");
            AnalogUnitState<double> actualSpeed = FindChild<AnalogUnitState<double>>(
                axis.State.ParameterSet!,
                "ActualSpeed");
            AnalogUnitState<double> actualAcceleration = FindChild<AnalogUnitState<double>>(
                axis.State.ParameterSet!,
                "ActualAcceleration");
            BaseDataVariableState<ExecutionModeEnumeration> executionMode =
                FindChild<BaseDataVariableState<ExecutionModeEnumeration>>(
                    taskControl.State.ParameterSet!,
                    "ExecutionMode");

            StatusCode cachedStatusCode = speedOverride.StatusCode;
            DateTimeUtc cachedTimestamp = speedOverride.Timestamp;
            Assert.That(speedOverride.OnReadValueAsync, Is.Not.Null);
            Assert.That(speedOverride.OnSimpleReadValueAsync, Is.Null);
            Assert.That(speedOverride.OnSimpleWriteValueAsync, Is.Not.Null);
            Assert.That(actualSpeed.OnReadValueAsync, Is.Not.Null);
            Assert.That(actualAcceleration.OnReadValueAsync, Is.Not.Null);
            Assert.That(executionMode.OnReadValueAsync, Is.Not.Null);
            Assert.That(emergencyStop.State.Active!.OnReadValueAsync, Is.Not.Null);
            Assert.That(protectiveStop.State.Enabled!.OnReadValueAsync, Is.Not.Null);

            ValueTask<(ServiceResult, DataValue)> readTask = speedOverride.ReadAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue());
            await readStarted.Task.ConfigureAwait(false);
            Assert.That(readTask.IsCompleted, Is.False);
            releaseRead.SetResult(true);
            (ServiceResult readResult, DataValue value) =
                await readTask.ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(readResult), Is.False);
            Assert.That(value.WrappedValue.GetDouble(), Is.EqualTo(88));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(value.SourceTimestamp, Is.EqualTo(readTimestamp));
            Assert.That(speedOverride.StatusCode, Is.EqualTo(cachedStatusCode));
            Assert.That(speedOverride.Timestamp, Is.EqualTo(cachedTimestamp));

            ValueTask<ServiceResult> writeTask = speedOverride.WriteAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(64.0));
            await writeStarted.Task.ConfigureAwait(false);
            Assert.That(writeTask.IsCompleted, Is.False);
            releaseWrite.SetResult(true);
            ServiceResult writeResult = await writeTask.ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(writeResult), Is.True);
            Assert.That(speedOverride.Value, Is.EqualTo(64));
        }

        [Test]
        public async Task ConcurrentReadsKeepValueStatusAndTimestampTogether()
        {
            var firstReadStarted = new TaskCompletionSource<bool>();
            var releaseFirstRead = new TaskCompletionSource<bool>();
            DateTimeUtc firstTimestamp = DateTimeUtc.Now;
            DateTimeUtc secondTimestamp =
                (DateTimeUtc)firstTimestamp.ToDateTime().AddSeconds(1);
            int readCount = 0;
            IMotionDeviceBuilder motionDevice = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("ConcurrentReads"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    motionDevice = graph.MotionDevice;
                    motionDevice.BindSpeedOverrideRead(async cancellationToken =>
                    {
                        int readNumber = Interlocked.Increment(ref readCount);
                        if (readNumber == 1)
                        {
                            firstReadStarted.SetResult(true);
                            await releaseFirstRead.Task.ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            return new DataValue(
                                new Variant(25.0),
                                StatusCodes.Uncertain,
                                firstTimestamp);
                        }

                        return new DataValue(
                            new Variant(75.0),
                            StatusCodes.GoodClamped,
                            secondTimestamp);
                    });
                })
                .ConfigureAwait(false);

            BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                motionDevice.State.ParameterSet!,
                "SpeedOverride");
            ValueTask<(ServiceResult, DataValue)> firstRead = speedOverride.ReadAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue());
            await firstReadStarted.Task.ConfigureAwait(false);
            ValueTask<(ServiceResult, DataValue)> secondRead = speedOverride.ReadAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue());

            (ServiceResult secondResult, DataValue secondValue) =
                await secondRead.ConfigureAwait(false);
            releaseFirstRead.SetResult(true);
            (ServiceResult firstResult, DataValue firstValue) =
                await firstRead.ConfigureAwait(false);

            Assert.That(firstResult.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(firstValue.WrappedValue.GetDouble(), Is.EqualTo(25));
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(firstValue.SourceTimestamp, Is.EqualTo(firstTimestamp));
            Assert.That(secondResult.StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(secondValue.WrappedValue.GetDouble(), Is.EqualTo(75));
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(secondValue.SourceTimestamp, Is.EqualTo(secondTimestamp));
        }

        [Test]
        public async Task ScalarReadBindingReturnsFrameworkIndexRangeError()
        {
            IMotionDeviceBuilder motionDevice = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("ScalarRange"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    motionDevice = graph.MotionDevice;
                    motionDevice.BindSpeedOverrideRead(
                        _ => new ValueTask<DataValue>(new DataValue(50.0)));
                })
                .ConfigureAwait(false);

            BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                motionDevice.State.ParameterSet!,
                "SpeedOverride");
            (ServiceResult result, DataValue value) =
                await speedOverride.ReadAttributeAsync(
                    motionDevice.BuildContext.Context,
                    Attributes.Value,
                    new NumericRange(0),
                    QualifiedName.Null,
                    new DataValue())
                    .ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public async Task SpeedOverrideRejectsInvalidStaticValues()
        {
            double[] invalidValues =
            [
                -1,
                101,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity
            ];

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("InvalidSpeedStatic"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    for (int ii = 0; ii < invalidValues.Length; ii++)
                    {
                        double value = invalidValues[ii];
                        Assert.That(
                            () => graph.MotionDevice.WithSpeedOverride(value),
                            Throws.TypeOf<ArgumentOutOfRangeException>());
                    }
                })
                .ConfigureAwait(false);
        }

        [Test]
        public async Task SpeedOverrideReadBindingValidatesGoodValues()
        {
            DataValue callbackValue = new DataValue(50.0);
            IMotionDeviceBuilder motionDevice = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("InvalidSpeedRead"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    motionDevice = graph.MotionDevice;
                    motionDevice.BindSpeedOverrideRead(
                        _ => new ValueTask<DataValue>(callbackValue));
                })
                .ConfigureAwait(false);

            BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                motionDevice.State.ParameterSet!,
                "SpeedOverride");
            (DataValue Value, StatusCode Expected)[] invalidValues =
            [
                (new DataValue(Variant.From("invalid")), StatusCodes.BadTypeMismatch),
                (new DataValue(-1.0), StatusCodes.BadOutOfRange),
                (new DataValue(101.0), StatusCodes.BadOutOfRange),
                (new DataValue(double.NaN), StatusCodes.BadOutOfRange),
                (new DataValue(double.PositiveInfinity), StatusCodes.BadOutOfRange),
                (new DataValue(double.NegativeInfinity), StatusCodes.BadOutOfRange)
            ];

            for (int ii = 0; ii < invalidValues.Length; ii++)
            {
                callbackValue = invalidValues[ii].Value;
                (ServiceResult result, DataValue value) =
                    await speedOverride.ReadAttributeAsync(
                        motionDevice.BuildContext.Context,
                        Attributes.Value,
                        NumericRange.Null,
                        QualifiedName.Null,
                        new DataValue())
                        .ConfigureAwait(false);

                Assert.That(
                    result.StatusCode,
                    Is.EqualTo(invalidValues[ii].Expected));
                Assert.That(value.WrappedValue.IsNull, Is.True);
            }

            callbackValue = new DataValue(
                Variant.From("offline"),
                StatusCodes.BadNotConnected);
            (ServiceResult badResult, _) = await speedOverride.ReadAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue())
                .ConfigureAwait(false);
            Assert.That(badResult.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));

            callbackValue = DataValue.Null;
            (ServiceResult noDataResult, _) = await speedOverride.ReadAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue())
                .ConfigureAwait(false);
            Assert.That(
                noDataResult.StatusCode,
                Is.EqualTo(StatusCodes.BadNoDataAvailable));
        }

        [Test]
        public async Task SpeedOverrideReadBindingValidatesUncertainValues()
        {
            DataValue callbackValue = new DataValue(50.0);
            IMotionDeviceBuilder motionDevice = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("UncertainSpeedRead"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    motionDevice = graph.MotionDevice;
                    motionDevice.BindSpeedOverrideRead(
                        _ => new ValueTask<DataValue>(callbackValue));
                })
                .ConfigureAwait(false);

            BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                motionDevice.State.ParameterSet!,
                "SpeedOverride");
            (DataValue Value, StatusCode Expected)[] invalidValues =
            [
                (
                    new DataValue(
                        Variant.From("invalid"),
                        StatusCodes.Uncertain),
                    StatusCodes.BadTypeMismatch),
                (
                    new DataValue(
                        Variant.From(101.0),
                        StatusCodes.Uncertain),
                    StatusCodes.BadOutOfRange),
                (
                    new DataValue(
                        Variant.From(double.NaN),
                        StatusCodes.Uncertain),
                    StatusCodes.BadOutOfRange)
            ];

            for (int ii = 0; ii < invalidValues.Length; ii++)
            {
                callbackValue = invalidValues[ii].Value;
                (ServiceResult result, DataValue value) =
                    await speedOverride.ReadAttributeAsync(
                        motionDevice.BuildContext.Context,
                        Attributes.Value,
                        NumericRange.Null,
                        QualifiedName.Null,
                        new DataValue())
                        .ConfigureAwait(false);

                Assert.That(
                    result.StatusCode,
                    Is.EqualTo(invalidValues[ii].Expected));
                Assert.That(value.WrappedValue.IsNull, Is.True);
            }
        }

        [Test]
        public async Task SpeedOverrideWriteBindingValidatesBeforeHandler()
        {
            int handlerCalls = 0;
            IMotionDeviceBuilder motionDevice = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("InvalidSpeedWrite"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    motionDevice = graph.MotionDevice;
                    motionDevice.BindSpeedOverrideWrite((_, _) =>
                    {
                        handlerCalls++;
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });
                })
                .ConfigureAwait(false);

            BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                motionDevice.State.ParameterSet!,
                "SpeedOverride");
            (Variant Value, StatusCode Expected)[] invalidValues =
            [
                (Variant.From("invalid"), StatusCodes.BadTypeMismatch),
                (Variant.From(-1.0), StatusCodes.BadOutOfRange),
                (Variant.From(101.0), StatusCodes.BadOutOfRange),
                (Variant.From(double.NaN), StatusCodes.BadOutOfRange),
                (Variant.From(double.PositiveInfinity), StatusCodes.BadOutOfRange),
                (Variant.From(double.NegativeInfinity), StatusCodes.BadOutOfRange)
            ];

            for (int ii = 0; ii < invalidValues.Length; ii++)
            {
                ServiceResult result = await speedOverride.WriteAttributeAsync(
                    motionDevice.BuildContext.Context,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(invalidValues[ii].Value))
                    .ConfigureAwait(false);

                Assert.That(
                    result.StatusCode,
                    Is.EqualTo(invalidValues[ii].Expected));
                Assert.That(handlerCalls, Is.Zero);
            }

            ServiceResult goodResult = await speedOverride.WriteAttributeAsync(
                motionDevice.BuildContext.Context,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(50.0))
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(goodResult), Is.True);
            Assert.That(handlerCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadBindingRejectsEitherOccupiedAsyncReadSlot()
        {
            ServiceResultException? fullSlotException = null;
            ServiceResultException? simpleSlotException = null;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("OccupiedReadSlots"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                        graph.MotionDevice.State.ParameterSet!,
                        "SpeedOverride");
                    speedOverride.OnReadValueAsync = (_, _, _, _, _) =>
                        new ValueTask<AttributeReadResult>(
                            new AttributeReadResult(
                                ServiceResult.Good,
                                Variant.From(1.0),
                                StatusCodes.Good,
                                DateTimeUtc.Now));

                    fullSlotException = Assert.Throws<ServiceResultException>(
                        () => graph.MotionDevice.BindSpeedOverrideRead(
                            _ => new ValueTask<DataValue>(new DataValue(1.0))));

                    speedOverride.OnReadValueAsync = null;
                    speedOverride.OnSimpleReadValueAsync = (_, _, _) =>
                        new ValueTask<AttributeSimpleReadResult>(
                            new AttributeSimpleReadResult(
                                ServiceResult.Good,
                                Variant.From(1.0)));
                    simpleSlotException = Assert.Throws<ServiceResultException>(
                        () => graph.MotionDevice.BindSpeedOverrideRead(
                            _ => new ValueTask<DataValue>(new DataValue(1.0))));
                })
                .ConfigureAwait(false);

            Assert.That(fullSlotException!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(simpleSlotException!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task WriteBindingRejectsEitherOccupiedAsyncWriteSlot()
        {
            ServiceResultException? fullSlotException = null;
            ServiceResultException? simpleSlotException = null;
            int boundHandlerCalls = 0;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("OccupiedWriteSlots"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    BaseDataVariableState<double> speedOverride = FindChild<BaseDataVariableState<double>>(
                        graph.MotionDevice.State.ParameterSet!,
                        "SpeedOverride");
                    speedOverride.OnWriteValueAsync = (_, _, _, _, _) =>
                        new ValueTask<AttributeWriteResult>(
                            new AttributeWriteResult(ServiceResult.Good));

                    fullSlotException = Assert.Throws<ServiceResultException>(
                        () => graph.MotionDevice.BindSpeedOverrideWrite((_, _) =>
                        {
                            boundHandlerCalls++;
                            return new ValueTask<ServiceResult>(ServiceResult.Good);
                        }));

                    speedOverride.OnWriteValueAsync = null;
                    speedOverride.OnSimpleWriteValueAsync = (_, _, _, _) =>
                        new ValueTask<AttributeWriteResult>(
                            new AttributeWriteResult(ServiceResult.Good));
                    simpleSlotException = Assert.Throws<ServiceResultException>(
                        () => graph.MotionDevice.BindSpeedOverrideWrite((_, _) =>
                        {
                            boundHandlerCalls++;
                            return new ValueTask<ServiceResult>(ServiceResult.Good);
                        }));
                })
                .ConfigureAwait(false);

            Assert.That(fullSlotException!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(simpleSlotException!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(boundHandlerCalls, Is.Zero);
        }

        [Test]
        public async Task GearRatioPreservesSignedNumeratorAndPitchMustBeFinite()
        {
            IGearBuilder gear = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("GearValues"), system =>
                {
                    GraphParts graph = ConfigureValidGraph(system);
                    gear = graph.PowerTrain.AddGear(
                        "Gear",
                        item => item.WithGearRatio(-7, 3));

                    Assert.That(
                        () => gear.WithPitch(double.NaN),
                        Throws.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(
                        () => gear.WithPitch(double.PositiveInfinity),
                        Throws.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(
                        () => gear.WithPitch(double.NegativeInfinity),
                        Throws.TypeOf<ArgumentOutOfRangeException>());
                    gear.WithPitch(12.5);
                })
                .ConfigureAwait(false);

            Assert.That(gear.State.GearRatio!.Value.Numerator, Is.EqualTo(-7));
            Assert.That(gear.State.GearRatio.Value.Denominator, Is.EqualTo(3));
            Assert.That(gear.State.Pitch!.Value, Is.EqualTo(12.5));
        }

        private ValueTask<GraphParts> AddValidGraphAsync(
            string browseName,
            GraphOptions? options = null)
        {
            return AddValidGraphAsync(
                m_fixture.CreateBuildContext(),
                browseName,
                options);
        }

        private static async ValueTask<GraphParts> AddValidGraphAsync(
            IRoboticsBuildContext context,
            string browseName,
            GraphOptions? options = null)
        {
            var result = new GraphParts();
            result.System = await context
                .AddMotionDeviceSystemAsync(
                    browseName,
                    system => Copy(ConfigureValidGraph(system, options), result))
                .ConfigureAwait(false);
            return result;
        }

        private static GraphParts ConfigureValidGraph(
            IMotionDeviceSystemBuilder system,
            GraphOptions? options = null)
        {
            options ??= new GraphOptions();
            var graph = new GraphParts();

            if (options.IncludeMotionDevice)
            {
                graph.MotionDevice = system.AddMotionDevice("MotionDevice", motion =>
                {
                    motion.WithCategory(MotionDeviceCategoryEnumeration.OTHER)
                        .WithSpeedOverride(100);
                });
                graph.Drive = graph.MotionDevice.AddDrive(
                    "Drive",
                    drive => drive.WithProductCode("DRIVE"));

                if (options.IncludeAxis)
                {
                    graph.Axis = graph.MotionDevice.AddAxis("Axis", axis =>
                    {
                        axis.WithMotionProfile(AxisMotionProfileEnumeration.OTHER)
                            .WithActualPosition(0);
                        if (options.VirtualAxis)
                        {
                            axis.AsVirtual();
                        }
                    });
                }

                if (options.IncludePowerTrain)
                {
                    graph.PowerTrain = graph.MotionDevice.AddPowerTrain("PowerTrain");
                    if (options.IncludeMotor)
                    {
                        graph.Motor = graph.PowerTrain.AddMotor(
                            "Motor",
                            motor => motor.WithMotorTemperature(20));
                        if (options.DriveMotor)
                        {
                            graph.Motor.IsDrivenBy(graph.Drive);
                        }
                    }
                    if (options.IncludeAxis && options.AddRequires)
                    {
                        graph.Axis.Requires(graph.PowerTrain);
                        graph.PowerTrain.Moves(graph.Axis);
                    }
                }
            }

            if (options.IncludeSafetyState)
            {
                graph.Safety = system.AddSafetyState("Safety", safety =>
                    safety.WithEmergencyStop(false)
                        .WithOperationalMode(OperationalModeEnumeration.OTHER)
                        .WithProtectiveStop(false));
            }

            if (options.IncludeController)
            {
                graph.Controller = system.AddController("Controller");
                if (options.IncludeSoftware)
                {
                    graph.Software = graph.Controller.AddSoftware(
                        "Software",
                        software => software.WithIdentification(data =>
                        {
                            data.Manufacturer = new LocalizedText("Vendor");
                            data.Model = new LocalizedText("Runtime");
                            data.SoftwareRevision = "1.0";
                        }));
                }
                if (options.IncludeTaskControl)
                {
                    graph.TaskControl = graph.Controller.AddTaskControl(
                        "TaskControl",
                        taskControl => taskControl.WithComponentName("Task Control"));
                }
                if (options.IncludeMotionDevice && options.AddControllerControls)
                {
                    graph.Controller.Controls(graph.MotionDevice);
                }
            }

            return graph;
        }

        private async Task AssertConfigurationFailsAsync(
            string expectedMessage,
            Action<IMotionDeviceSystemBuilder> configure)
        {
            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_fixture.CreateBuildContext()
                        .AddMotionDeviceSystemAsync(
                            NextName("Invalid"),
                            configure)
                        .ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        private void AssertTreeRegisteredAndUnique(NodeState root)
        {
            _ = AssertTreeRegisteredAndUnique(
                m_fixture.Manager,
                root,
                m_fixture.Manager.RoboticsInstanceNamespaceIndex);
        }

        private static HashSet<NodeId> AssertTreeRegisteredAndUnique(
            DiNodeManager manager,
            NodeState root,
            ushort instanceNamespaceIndex)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            var nodeIds = new HashSet<NodeId>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                NodeState node = nodes[ii];
                Assert.That(node.NodeId.IsNull, Is.False, node.BrowseName.ToString());
                Assert.That(nodeIds.Add(node.NodeId), Is.True, node.NodeId.ToString());
                Assert.That(
                    node.NodeId.NamespaceIndex,
                    Is.EqualTo(instanceNamespaceIndex));
                Assert.That(
                    manager.FindPredefinedNode(node.NodeId),
                    Is.SameAs(node));

                children.Clear();
                node.GetChildren(manager.SystemContext, children);
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    nodes.Add(children[childIndex]);
                }
            }
            return nodeIds;
        }

        private HashSet<NodeId> GetNodeIds(NodeState root)
        {
            var result = new HashSet<NodeId>();
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                result.Add(nodes[ii].NodeId);
                children.Clear();
                nodes[ii].GetChildren(m_fixture.Manager.SystemContext, children);
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    nodes.Add(children[childIndex]);
                }
            }
            return result;
        }

        private void AssertReference(
            NodeState source,
            uint referenceTypeIdentifier,
            NodeState target)
        {
            NodeId referenceTypeId = NodeId.Create(
                referenceTypeIdentifier,
                Robotics.Namespaces.Robotics,
                m_fixture.Manager.SystemContext.NamespaceUris);
            Assert.That(
                source.ReferenceExists(referenceTypeId, false, target.NodeId),
                Is.True);
            Assert.That(
                target.ReferenceExists(referenceTypeId, true, source.NodeId),
                Is.True);
        }

        private TChild FindChild<TChild>(NodeState parent, string browseName)
            where TChild : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_fixture.Manager.SystemContext, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is TChild typed &&
                    children[ii].BrowseName.Name == browseName)
                {
                    return typed;
                }
            }
            Assert.Fail(
                $"Child '{browseName}' of type '{typeof(TChild).Name}' " +
                $"was not found below '{parent.BrowseName}'.");
            return null!;
        }

        private static List<BaseInstanceState> GetChildren(
            ISystemContext context,
            NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            return children;
        }

        private string NextName(string prefix)
        {
            return $"{prefix}{Interlocked.Increment(ref m_nameCounter)}";
        }

        private static void Copy(GraphParts source, GraphParts target)
        {
            target.Controller = source.Controller;
            target.MotionDevice = source.MotionDevice;
            target.Safety = source.Safety;
            target.Axis = source.Axis;
            target.PowerTrain = source.PowerTrain;
            target.Motor = source.Motor;
            target.Drive = source.Drive;
            target.Software = source.Software;
            target.TaskControl = source.TaskControl;
        }

        private sealed class GraphOptions
        {
            public bool IncludeController { get; set; } = true;

            public bool IncludeMotionDevice { get; set; } = true;

            public bool IncludeSafetyState { get; set; } = true;

            public bool IncludeAxis { get; set; } = true;

            public bool IncludePowerTrain { get; set; } = true;

            public bool IncludeMotor { get; set; } = true;

            public bool IncludeSoftware { get; set; } = true;

            public bool IncludeTaskControl { get; set; } = true;

            public bool AddControllerControls { get; set; } = true;

            public bool AddRequires { get; set; } = true;

            public bool DriveMotor { get; set; } = true;

            public bool VirtualAxis { get; set; }
        }

        private class GraphParts
        {
            public IMotionDeviceSystemBuilder System { get; set; } = null!;

            public IControllerBuilder Controller { get; set; } = null!;

            public IMotionDeviceBuilder MotionDevice { get; set; } = null!;

            public ISafetyStateBuilder Safety { get; set; } = null!;

            public IAxisBuilder Axis { get; set; } = null!;

            public IPowerTrainBuilder PowerTrain { get; set; } = null!;

            public IMotorBuilder Motor { get; set; } = null!;

            public IDriveBuilder Drive { get; set; } = null!;

            public IRoboticsSoftwareBuilder Software { get; set; } = null!;

            public ITaskControlBuilder TaskControl { get; set; } = null!;
        }

        private sealed class FullGraph : GraphParts
        {
            public IGearBuilder Gear { get; set; } = null!;

            public IEmergencyStopBuilder EmergencyStop { get; set; } = null!;

            public IProtectiveStopBuilder ProtectiveStop { get; set; } = null!;

            public ITaskModuleBuilder TaskModule { get; set; } = null!;

            public IPowerTrainBuilder SlavePowerTrain { get; set; } = null!;

            public IMotorBuilder SlaveMotor { get; set; } = null!;
        }

        private sealed class CustomRoboticsNodeManager :
            DiNodeManager,
            IRoboticsNodeIdFactory
        {
            public const string InstanceNamespaceUri =
                "urn:tests:robotics:custom-manager:instances";

            public CustomRoboticsNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(
                    server,
                    configuration,
                    Opc.Ua.IA.Namespaces.IA,
                    Robotics.Namespaces.Robotics,
                    InstanceNamespaceUri)
            {
            }

            public ushort RoboticsInstanceNamespaceIndex =>
                (ushort)SystemContext.NamespaceUris.GetIndex(InstanceNamespaceUri);

            public int PredefinedNodeCount => PredefinedNodes.Count;

            public override NodeId New(ISystemContext context, NodeState node)
            {
                return m_nodeIdAllocator.New(
                    this,
                    RoboticsInstanceNamespaceIndex,
                    node);
            }

            public void PauseNextRootRegistrations(int count)
            {
                if (count <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                lock (m_registrationPauseLock)
                {
                    if (m_resumeRootRegistrations != null)
                    {
                        throw new InvalidOperationException(
                            "A root-registration pause is already active.");
                    }

                    m_rootRegistrationsToPause = count;
                    m_rootRegistrationsPaused = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_resumeRootRegistrations = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            public Task<bool> WaitForPausedRootRegistrationsAsync()
            {
                lock (m_registrationPauseLock)
                {
                    return m_rootRegistrationsPaused?.Task ??
                        throw new InvalidOperationException(
                            "No root-registration pause is active.");
                }
            }

            public void ResumeRootRegistrations()
            {
                TaskCompletionSource<bool> resume;
                lock (m_registrationPauseLock)
                {
                    resume = m_resumeRootRegistrations ??
                        throw new InvalidOperationException(
                            "No root-registration pause is active.");
                    m_rootRegistrationsPaused = null;
                    m_resumeRootRegistrations = null;
                    m_rootRegistrationsToPause = 0;
                }
                resume.TrySetResult(true);
            }

            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                var nodes = new NodeStateCollection();
                nodes.AddRoboticsTypeSystem(context);
                return new ValueTask<NodeStateCollection>(nodes);
            }

            protected override async ValueTask AddPredefinedNodeAsync(
                ISystemContext context,
                NodeState node,
                CancellationToken cancellationToken = default)
            {
                Task? resumeTask = null;
                TaskCompletionSource<bool>? paused = null;
                lock (m_registrationPauseLock)
                {
                    if (node is MotionDeviceSystemState &&
                        m_rootRegistrationsToPause > 0)
                    {
                        m_rootRegistrationsToPause--;
                        resumeTask = m_resumeRootRegistrations!.Task;
                        if (m_rootRegistrationsToPause == 0)
                        {
                            paused = m_rootRegistrationsPaused;
                        }
                    }
                }

                paused?.TrySetResult(true);
                if (resumeTask != null)
                {
                    await resumeTask.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                await base.AddPredefinedNodeAsync(context, node, cancellationToken)
                    .ConfigureAwait(false);
            }

            private readonly Lock m_registrationPauseLock = new();
            private readonly TestRoboticsNodeIdAllocator m_nodeIdAllocator = new();
            private TaskCompletionSource<bool>? m_resumeRootRegistrations;
            private TaskCompletionSource<bool>? m_rootRegistrationsPaused;
            private int m_rootRegistrationsToPause;
        }

        private sealed class MissingRoboticsModelNodeManager :
            DiNodeManager,
            IRoboticsNodeIdFactory
        {
            public const string InstanceNamespaceUri =
                "urn:tests:robotics:missing-model:instances";

            public MissingRoboticsModelNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(
                    server,
                    configuration,
                    Opc.Ua.IA.Namespaces.IA,
                    Robotics.Namespaces.Robotics,
                    InstanceNamespaceUri)
            {
            }

            public ushort RoboticsInstanceNamespaceIndex =>
                (ushort)SystemContext.NamespaceUris.GetIndex(InstanceNamespaceUri);

            public override NodeId New(ISystemContext context, NodeState node)
            {
                return m_nodeIdAllocator.New(
                    this,
                    RoboticsInstanceNamespaceIndex,
                    node);
            }

            private readonly TestRoboticsNodeIdAllocator m_nodeIdAllocator = new();
        }

        private sealed class UnmarkedRoboticsNodeManager : DiNodeManager
        {
            public const string InstanceNamespaceUri =
                "urn:tests:robotics:unmarked-manager:instances";

            public UnmarkedRoboticsNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(
                    server,
                    configuration,
                    Opc.Ua.IA.Namespaces.IA,
                    Robotics.Namespaces.Robotics,
                    InstanceNamespaceUri)
            {
            }

            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                var nodes = new NodeStateCollection();
                nodes.AddRoboticsTypeSystem(context);
                return new ValueTask<NodeStateCollection>(nodes);
            }
        }

        private sealed class TestRoboticsNodeIdAllocator
        {
            public NodeId New(
                DiNodeManager manager,
                ushort namespaceIndex,
                NodeState node)
            {
                if (!node.NodeId.IsNull)
                {
                    return node.NodeId;
                }

                lock (m_lock)
                {
                    while (true)
                    {
                        uint identifier =
                            Utils.IncrementIdentifier(ref m_lastUsedNodeId);
                        var candidate = new NodeId(identifier, namespaceIndex);
                        if (m_reservedNodeIds.Contains(candidate) ||
                            manager.FindPredefinedNode(candidate) != null)
                        {
                            continue;
                        }

                        m_reservedNodeIds.Add(candidate);
                        return candidate;
                    }
                }
            }

            private readonly HashSet<NodeId> m_reservedNodeIds = [];
            private readonly Lock m_lock = new();
            private uint m_lastUsedNodeId;
        }
    }
}
