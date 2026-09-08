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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies Robot Intent builder compliance with clauses 9, 10, 12 and Annex B.
    /// </summary>
    [TestFixture]
    public class IntentBuilderComplianceTests
    {
        [Test]
        public async Task AcceptsDefaultsPauseUnsupportedAndDoesNotCreatePauseMethods()
        {
            await using ComplianceServerFixture fixture = new();
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext(new RecordingExecutor());

            IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                "DefaultPause",
                controller => controller.Accepts<WaitIntentDataType>(),
                CancellationToken.None).ConfigureAwait(false);

            IntentCapabilityDataType capability = builder.State.Capabilities!.SupportedIntents!.Value[0];

            Assert.Multiple(() =>
            {
                Assert.That(capability.PauseSupported, Is.False);
                Assert.That(builder.State.Pause, Is.Null);
                Assert.That(builder.State.Resume, Is.Null);
                Assert.That(RobotIntentFacetCalculator.Compute(builder.State).ToArray(), Does.Not.Contain("RI-Pause"));
            });
        }

        [Test]
        public async Task SafetySourceReadUpdatesAdmissionGate()
        {
            await using ComplianceServerFixture fixture = new();
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext(new RecordingExecutor());
            var safetySource = new MutableSafetySource
            {
                Snapshot = new RobotIntentSafetySnapshot(
                    SafeMotionFunctionEnum.None,
                    false,
                    false,
                    false,
                    0.0,
                    true,
                    LocalizedText.Null)
            };
            IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                "SafetyGated",
                controller => controller
                    .WithSafetyState(safetySource)
                    .Accepts<WaitIntentDataType>(),
                CancellationToken.None).ConfigureAwait(false);
            fixture.Manager.StartIntentControllerHosts();
            safetySource.Snapshot = new RobotIntentSafetySnapshot(
                SafeMotionFunctionEnum.None,
                true,
                false,
                false,
                0.0,
                true,
                LocalizedText.From("emergency stop"));

            var sessionId = new NodeId("operator-session", 2);
            Assert.That(builder.Host.RequestControl(context.Context, sessionId, out _), Is.True);

            IntentAdmission admission = await builder.Host.SubmitIntentAsync(
                context.Context,
                sessionId,
                new WaitIntentDataType { IntentId = "blocked", Duration = 1.0 });
            DataValue ready = await ReadValueAsync(context.Context, builder.State.Ready!).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(ready.WrappedValue.GetBoolean(true), Is.False);
                Assert.That(RobotIntentFacetCalculator.Compute(builder.State).ToArray(), Does.Contain("RI-Safety"));
            });
        }

        [Test]
        public void HasIntentControllerProjectsRoboticsOperationalMode()
        {
            SystemContext context = CreateSystemContext();
            MotionDeviceSystemState motionDeviceSystem = CreateMotionDeviceSystemWithSafety(
                context,
                OperationalModeEnumeration.AUTOMATIC);
            IntentControllerState controller = CreateIntentController(context, OperationalModeEnum.AutomaticExternal);
            IMotionDeviceSystemBuilder motionBuilder = MockMotionDeviceSystemBuilder(context, motionDeviceSystem);
            IIntentControllerBuilder intentBuilder = MockIntentControllerBuilder(controller);

            motionBuilder.HasIntentController(intentBuilder);

            Assert.Multiple(() =>
            {
                Assert.That(controller.OperationalMode!.Value, Is.EqualTo(OperationalModeEnum.Automatic));
                Assert.That(RobotIntentFacetCalculator.Compute(controller).ToArray(), Does.Contain("RI-Interop-40010"));
            });
        }

        [Test]
        public void Interop40010FacetRequiresLoadedTaskProgramToBePublished()
        {
            SystemContext context = CreateSystemContext();
            MotionDeviceSystemState motionDeviceSystem = CreateMotionDeviceSystemWithSafety(
                context,
                OperationalModeEnumeration.AUTOMATIC);
            AddLoadedTaskProgram(motionDeviceSystem, "missing-program");
            IntentControllerState controller = CreateIntentController(context, OperationalModeEnum.Automatic);
            IMotionDeviceSystemBuilder motionBuilder = MockMotionDeviceSystemBuilder(context, motionDeviceSystem);
            IIntentControllerBuilder intentBuilder = MockIntentControllerBuilder(controller);

            motionBuilder.HasIntentController(intentBuilder);

            Assert.That(RobotIntentFacetCalculator.Compute(controller).ToArray(), Does.Not.Contain("RI-Interop-40010"));
        }

        [Test]
        public async Task AxisCountIsSourcedFromPublishedAxesForAdmission()
        {
            await using ComplianceServerFixture fixture = new();
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext(new RecordingExecutor());
            IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                "SevenAxis",
                controller =>
                {
                    for (uint ii = 0; ii < 7; ii++)
                    {
                        controller.AddAxis($"J{ii + 1}", ii, AxisKindEnum.Revolute);
                    }
                    controller.Accepts<JointMoveIntentDataType>();
                },
                CancellationToken.None).ConfigureAwait(false);
            fixture.Manager.StartIntentControllerHosts();
            var sessionId = new NodeId("operator-session", 2);
            Assert.That(builder.Host.RequestControl(context.Context, sessionId, out _), Is.True);

            IntentAdmission admission = builder.Host.SubmitIntent(
                context.Context,
                sessionId,
                new JointMoveIntentDataType
                {
                    IntentId = "seven-axis",
                    BufferMode = BufferModeEnum.Aborting,
                    HasJointTargets = true,
                    JointTargets = s_doubleValues1.ToArrayOf()
                });

            Assert.Multiple(() =>
            {
                Assert.That(builder.State.Capabilities!.AxisCount!.Value, Is.EqualTo(7));
                Assert.That(admission.Accepted, Is.True, admission.Message);
            });
        }

        [Test]
        public async Task ActiveMissionIsInstantiatedWithController()
        {
            await using ComplianceServerFixture fixture = new();
            await fixture.StartAsync().ConfigureAwait(false);
            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext(new RecordingExecutor());

            IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                "ActiveMission",
                controller => controller.Accepts<WaitIntentDataType>(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(builder.State.ActiveMission, Is.Not.Null);
                Assert.That(builder.State.ActiveMission!.Value, Is.EqualTo(NodeId.Null));
            });
        }

        private static async Task<DataValue> ReadValueAsync(ISystemContext context, BaseVariableState variable)
        {
            (ServiceResult result, DataValue value) = await variable.ReadAttributeAsync(
                context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsBad(result), Is.False);
                Assert.That(StatusCode.IsBad(value.StatusCode), Is.False);
            });
            return value;
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        private static MotionDeviceSystemState CreateMotionDeviceSystemWithSafety(
            ISystemContext context,
            OperationalModeEnumeration mode)
        {
            var motionDeviceSystem = new MotionDeviceSystemState(null)
            {
                NodeId = new NodeId("motion", 2),
                BrowseName = new QualifiedName("Motion", 2)
            };
            var safetyState = new SafetyStateState(motionDeviceSystem)
            {
                BrowseName = new QualifiedName("Safety", 2)
            };
            var parameterSet = new BaseObjectState(safetyState)
            {
                BrowseName = new QualifiedName("ParameterSet", 0)
            };
            var operationalMode = new BaseDataVariableState(parameterSet)
            {
                BrowseName = new QualifiedName(BrowseNames.OperationalMode, 0),
                Value = new Variant((int)mode)
            };
            parameterSet.AddChild(operationalMode);
            safetyState.AddChild(parameterSet);
            motionDeviceSystem.AddChild(safetyState);
            return motionDeviceSystem;
        }

        private static IntentControllerState CreateIntentController(
            SystemContext context,
            OperationalModeEnum mode)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                context,
                new NodeId("intent", 2),
                new QualifiedName("Intent", 2),
                new LocalizedText("Intent"),
                true);
            controller.OperationalMode!.Value = mode;
            controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                new IntentCapabilityDataType
                {
                    IntentType = ExpandedNodeId.ToNodeId(
                        global::Opc.Ua.RobotIntent.DataTypeIds.WaitIntentDataType,
                        context.NamespaceUris),
                    SupportedBufferModes = new[] { BufferModeEnum.Aborting }.ToArrayOf()
                }
            }.ToArrayOf();
            return controller;
        }

        private static void AddLoadedTaskProgram(MotionDeviceSystemState motionDeviceSystem, string programName)
        {
            var taskControl = new TaskControlState(motionDeviceSystem)
            {
                BrowseName = new QualifiedName("Task", 2)
            };
            var parameterSet = new BaseObjectState(taskControl)
            {
                BrowseName = new QualifiedName("ParameterSet", 0)
            };
            parameterSet.AddChild(new BaseDataVariableState(parameterSet)
            {
                BrowseName = new QualifiedName(BrowseNames.TaskProgramLoaded, 0),
                Value = new Variant(true)
            });
            parameterSet.AddChild(new BaseDataVariableState(parameterSet)
            {
                BrowseName = new QualifiedName(BrowseNames.TaskProgramName, 0),
                Value = new Variant(programName)
            });
            taskControl.AddChild(parameterSet);
            motionDeviceSystem.AddChild(taskControl);
        }

        private static IMotionDeviceSystemBuilder MockMotionDeviceSystemBuilder(
            SystemContext context,
            MotionDeviceSystemState motionDeviceSystem)
        {
            var buildContext = new Mock<IRoboticsBuildContext>(MockBehavior.Strict);
            buildContext.SetupGet(static build => build.Context).Returns(context);
            var motionBuilder = new Mock<IMotionDeviceSystemBuilder>(MockBehavior.Strict);
            motionBuilder.SetupGet(static builder => builder.BuildContext).Returns(buildContext.Object);
            motionBuilder.SetupGet(static builder => builder.State).Returns(motionDeviceSystem);
            return motionBuilder.Object;
        }

        private static IIntentControllerBuilder MockIntentControllerBuilder(IntentControllerState controller)
        {
            var intentBuilder = new Mock<IIntentControllerBuilder>(MockBehavior.Strict);
            intentBuilder.SetupGet(static builder => builder.State).Returns(controller);
            return intentBuilder.Object;
        }

        private sealed class MutableSafetySource : IRobotIntentSafetySource
        {
            public RobotIntentSafetySnapshot Snapshot { get; set; }

            public ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                return new ValueTask<RobotIntentSafetySnapshot>(Snapshot);
            }
        }

        private sealed class RecordingExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> StartedIds { get; } = new();

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                StartedIds.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }

        private sealed class ComplianceServerFixture : IAsyncDisposable
        {
            public RobotIntentNodeManager Manager { get; private set; } = null!;

            public async Task StartAsync()
            {
                m_fixture = new ServerFixture<StandardServer>(
                    telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
                Manager = new RobotIntentNodeManager(
                    server.CurrentInstance,
                    m_fixture.Config,
                    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                    new RobotIntentServerOptions());
                await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync().ConfigureAwait(false);
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }

            private ServerFixture<StandardServer>? m_fixture;
        }

        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0];
    }
}
