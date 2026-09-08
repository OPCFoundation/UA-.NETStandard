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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Tests;
using RiDataTypeIds = Opc.Ua.RobotIntent.DataTypeIds;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Exercises the capability brought into scope beyond single moves: safety
    /// awareness, trajectories and force, brokered real-time channels, and the mission
    /// step graph with its error policies.
    /// </summary>
    [TestFixture]
    public class IntentScopeExtensionTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            m_executor = new ScriptedExecutor();
        }

        [Test]
        public void AProtectiveStopRefusesSubmission()
        {
            using IntentControllerHost host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus { ProtectiveStopActive = true });

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
            });
        }

        [Test]
        public void AFaultedSafetyControllerRefusesSubmission()
        {
            using IntentControllerHost host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus { SafetyControllerOk = false });

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
        }

        [Test]
        public void ASpeedAboveTheEnforcedSafeLimitIsRefused()
        {
            using IntentControllerHost host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus
            {
                ActiveFunction = SafeMotionFunctionEnum.Sls,
                SafeSpeedLimitActive = true,
                SafeSpeedLimit = 0.25
            });

            LinearMoveIntentDataType intent = Move();
            intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = 1.0 };

            IntentAdmission admission = host.SubmitIntent(m_context, null, intent);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
            });
        }

        [Test]
        public void ASpeedWithinTheEnforcedSafeLimitIsAdmitted()
        {
            using IntentControllerHost host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus
            {
                ActiveFunction = SafeMotionFunctionEnum.Sls,
                SafeSpeedLimitActive = true,
                SafeSpeedLimit = 0.25
            });

            LinearMoveIntentDataType intent = Move();
            intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = 0.1 };

            Assert.That(host.SubmitIntent(m_context, null, intent).Accepted, Is.True);
        }

        [Test]
        public void AnInactiveSpeedLimitDoesNotRefuse()
        {
            using IntentControllerHost host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus
            {
                SafeSpeedLimitActive = false,
                SafeSpeedLimit = 0.25
            });

            LinearMoveIntentDataType intent = Move();
            intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = 1.0 };

            Assert.That(host.SubmitIntent(m_context, null, intent).Accepted, Is.True,
                "a limit that is not being enforced constrains nothing");
        }

        [Test]
        public void EmergencyStopRefusesSubmission()
        {
            using IntentControllerHost host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus { EmergencyStopActive = true });

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
        }

        [Test]
        public async Task ConstraintsAreClampedBeforeExecution()
        {
            IntentControllerHostOptions options = Options();
            options.MaxCartesianSpeed = 0.5;
            using IntentControllerHost host = NewHost(options);
            LinearMoveIntentDataType intent = Move();
            intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = 2.0 };

            IntentAdmission admission = host.SubmitIntent(m_context, null, intent);

            Assert.That(admission.Accepted, Is.True);
            await WaitAsync(() => m_executor.LastIntent != null).ConfigureAwait(false);
            Assert.That(((MotionIntentDataType)m_executor.LastIntent!).Constraints!.CartesianSpeed, Is.EqualTo(0.5));
        }

        [Test]
        public void ATrajectoryOutOfTimeOrderIsRefused()
        {
            using IntentControllerHost host = NewHost();
            var intent = new TrajectoryIntentDataType
            {
                Points = new[] { Point(100), Point(50) }
            };

            IntentAdmission admission = host.SubmitIntent(m_context, null, intent);

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void ATrajectoryPointWithTheWrongAxisCountIsRefused()
        {
            using IntentControllerHost host = NewHost();
            var bad = new TrajectoryPointDataType
            {
                TimeFromStart = 100,
                Positions = new[] { 0.1, 0.2 }
            };

            IntentAdmission admission = host.SubmitIntent(
                m_context, null, new TrajectoryIntentDataType { Points = new[] { bad } });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public void ATrajectoryLongerThanTheDeclaredLimitIsRefused()
        {
            IntentControllerHostOptions options = Options();
            options.MaxTrajectoryPoints = 2;
            using IntentControllerHost host = NewHost(options);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType
                {
                    Points = new[] { Point(10), Point(20), Point(30) }
                });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public async Task AWellFormedTrajectoryExecutes()
        {
            using IntentControllerHost host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType
                {
                    Points = new[] { Point(10), Point(20), Point(30) }
                });

            Assert.That(admission.Accepted, Is.True);
            await WaitAsync(() => m_executor.Started.Length == 1).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo([admission.IntentId]));
        }

        [Test]
        public async Task TrajectoryPathToleranceFailureIsAppliedByTheHost()
        {
            using IntentControllerHost host = NewHost();
            m_executor.OnExecute = e => e.Progress.ReportTrajectoryDeviation(0.02, 0, 10, false);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType
                {
                    Points = new[] { Point(100) },
                    PathTolerance = new MotionToleranceDataType { Position = 0.001 }
                });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(node.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.Kinematics));
            });
        }

        [Test]
        public async Task TrajectoryGoalToleranceFailureIsAppliedByTheHost()
        {
            using IntentControllerHost host = NewHost();
            m_executor.OnExecute = e => e.Progress.ReportTrajectoryDeviation(0, 0.02, 100, true);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType
                {
                    Points = new[] { Point(100) },
                    GoalTolerance = new MotionToleranceDataType { Position = 0.001 }
                });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            Assert.That(node.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.Kinematics));
        }

        [Test]
        public async Task TrajectoryGoalTimeToleranceFailureIsAppliedByTheHost()
        {
            using IntentControllerHost host = NewHost();
            m_executor.OnExecute = e => e.Progress.ReportTrajectoryDeviation(0, 0, 250, true);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType
                {
                    Points = new[] { Point(100) },
                    GoalTimeTolerance = 10
                });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            Assert.That(node.Result!.Value!.Failure, Is.EqualTo(IntentFailureEnum.Timeout));
        }

        [Test]
        public async Task ServerChosenTrajectoryTolerancesArePublishedInTheResult()
        {
            IntentControllerHostOptions options = Options();
            options.DefaultPathTolerance = 0.01;
            using IntentControllerHost host = NewHost(options);
            m_executor.OnExecute = e => e.Progress.ReportTrajectoryDeviation(0.02, 0, 10, false);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType { Points = new[] { Point(100) } });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            Assert.That(HasOutput(node.Result!.Value!.Outputs, "PathTolerance"), Is.True);
        }

        [Test]
        public async Task TrajectoryProgressTracksElapsedTimeFraction()
        {
            using IntentControllerHost host = NewHost();
            m_executor.OnExecute = e => e.Progress.ReportTrajectoryDeviation(0, 0, 25, false);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType { Points = new[] { Point(100) } });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            Assert.That(node.Progress!.Value, Is.EqualTo(0.25).Within(0.001));
        }

        [Test]
        public void AForceIntentWithAZeroDirectionIsRefused()
        {
            using IntentControllerHost host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new ForceIntentDataType
                {
                    Direction = s_doubleValues1,
                    ContactForce = 5,
                    MaxDistance = 0.1
                });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public void AForceIntentWithoutADistanceIsRefused()
        {
            using IntentControllerHost host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new ForceIntentDataType
                {
                    Direction = new[] { 0.0, 0.0, -1.0 },
                    ContactForce = 5,
                    MaxDistance = 0
                });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public void AForceIntentWithoutContactForceIsRefused()
        {
            using IntentControllerHost host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new ForceIntentDataType
                {
                    Direction = new[] { 0.0, 0.0, -1.0 },
                    ContactForce = 0,
                    MaxDistance = 0.1
                });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public async Task ForceIntentPassesContactSearchParametersToExecutor()
        {
            using IntentControllerHost host = NewHost();

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new ForceIntentDataType
                {
                    Direction = new[] { 0.0, 0.0, -1.0 },
                    ContactForce = 5,
                    MaxDistance = 0.1
                });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            var force = (ForceIntentDataType)m_executor.LastIntent!;
            Assert.Multiple(() =>
            {
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(force.Direction, Is.EqualTo(new[] { 0.0, 0.0, -1.0 }));
                Assert.That(force.ContactForce, Is.EqualTo(5));
                Assert.That(force.MaxDistance, Is.EqualTo(0.1));
            });
        }

        [Test]
        public async Task GraspRequestedForceIsAdvisoryWhenExecutorSucceeds()
        {
            IntentControllerHostOptions options = Options();
            options.Accept(RiDataTypeIds.GraspIntentDataType);
            using IntentControllerHost host = NewHost(options);

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new GraspIntentDataType { Force = 999, Width = 0.02 });

            IntentOperationState node = await WaitForOperationAsync(admission.Operation).ConfigureAwait(false);
            var grasp = (GraspIntentDataType)m_executor.LastIntent!;
            Assert.Multiple(() =>
            {
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(grasp.Force, Is.EqualTo(999));
            });
        }

        [Test]
        public async Task ToolChangeWaitReleaseAndProcessIntentsReachTheExecutor()
        {
            IntentControllerHostOptions options = Options();
            options.MaxQueueDepth = 16;
            options.Accept(RiDataTypeIds.ToolChangeIntentDataType);
            options.Accept(RiDataTypeIds.WaitIntentDataType);
            options.Accept(RiDataTypeIds.ReleaseIntentDataType);
            using IntentControllerHost host = NewHost(options);

            IntentDataType[] intents =
            [
                new ToolChangeIntentDataType { IntentId = "tool-change" },
                new WaitIntentDataType { IntentId = "wait", Duration = 1 },
                new ReleaseIntentDataType { IntentId = "release" },
                new ArcWeldIntentDataType { IntentId = "arc-weld" },
                new SpotWeldIntentDataType { IntentId = "spot-weld" },
                new DispenseIntentDataType { IntentId = "dispense" },
                new FastenIntentDataType { IntentId = "fasten" },
                new PalletiseIntentDataType { IntentId = "palletise" },
                new SurfaceFinishIntentDataType { IntentId = "surface-finish" }
            ];
            for (int ii = 1; ii < intents.Length; ii++)
            {
                intents[ii].BufferMode = BufferModeEnum.Buffered;
            }
            foreach (IntentDataType intent in intents)
            {
                IntentAdmission admission = host.SubmitIntent(m_context, null, intent);
                Assert.That(admission.Accepted, Is.True, intent.GetType().Name);
            }
            await WaitAsync(() => m_executor.Started.Length == intents.Length).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(intents.Select(static intent => intent.IntentId).ToArray()));
        }

        [Test]
        public void ProcessIntentNegativeParametersAreRefused()
        {
            using IntentControllerHost host = NewHost();

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new SurfaceFinishIntentDataType { ContactForce = -1 });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public async Task ProcessProgramMustResolveUnderTheControllerProgramsFolder()
        {
            NodeId program = new("program", 1);
            using IntentControllerHost host = NewHost(null, controller => AddProgram(controller, program));

            IntentAdmission accepted = host.SubmitIntent(m_context, null,
                new ArcWeldIntentDataType { ProcessProgram = program });
            IntentAdmission refused = host.SubmitIntent(m_context, null,
                new ArcWeldIntentDataType
                {
                    IntentId = "missing-program",
                    ProcessProgram = new NodeId("missing", 1)
                });

            IntentOperationState node = await WaitForOperationAsync(accepted.Operation).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(accepted.Accepted, Is.True);
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(refused.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void FastenJointReferenceIsRefusedUntilJointModelSupportExists()
        {
            using IntentControllerHost host = NewHost();

            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new FastenIntentDataType { Joint = new NodeId("joint", 1) });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
        }

        [Test]
        public void ScopedNodeIdsMustResolveUnderTheCommandedController()
        {
            NodeId location = new("loc", 1);
            NodeId otherLocation = new("otherLoc", 1);
            using IntentControllerHost host = NewHost(null, controller =>
            {
                AddLocation(controller, location);
                AddWrongTypedChild(controller.Locations!, otherLocation);
            });

            Assert.Multiple(() =>
            {
                Assert.That(host.SubmitIntent(m_context, null,
                    new PickIntentDataType { Source = location }).Accepted, Is.True);
                Assert.That(host.SubmitIntent(m_context, null,
                    new PickIntentDataType { Source = new NodeId("unknown", 1) }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new PickIntentDataType { Source = otherLocation }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void EmptyScopedIndexesRejectNonNullReferences()
        {
            using IntentControllerHost host = NewHost();
            NodeId arbitrary = new("attacker", 1);

            Assert.Multiple(() =>
            {
                Assert.That(host.SubmitIntent(m_context, null,
                    new PickIntentDataType { Source = arbitrary }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new CallProgramIntentDataType { Program = arbitrary }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new LinearMoveIntentDataType { Target = Pose(), ToolFrame = arbitrary }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null, Force("missing")).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void EveryScopedReferenceKindIsValidatedAgainstItsExpectedIndex()
        {
            NodeId location = new("loc", 1);
            NodeId tool = new("tool", 1);
            NodeId toolFrame = new("toolFrame", 1);
            NodeId workFrame = new("workFrame", 1);
            NodeId output = new("out", 1);
            NodeId booleanVariable = new("bool", 1);
            NodeId program = new("program", 1);
            using IntentControllerHost host = NewHost(null, controller =>
            {
                AddLocation(controller, location);
                AddTool(controller, tool);
                AddFrame(controller, toolFrame, FrameRoleEnum.Tool, "tool");
                AddFrame(controller, workFrame, FrameRoleEnum.Base, "work");
                AddOutput(controller, output, global::Opc.Ua.DataTypeIds.Boolean);
                AddBooleanVariable(controller, booleanVariable);
                AddProgram(controller, program);
            });

            IntentDataType[] accepted =
            [
                new PlaceIntentDataType { Destination = location },
                new PalletiseIntentDataType { Pattern = location },
                new ToolChangeIntentDataType { Tool = tool },
                new SetOutputIntentDataType { Output = output, Value = new Variant(true) },
                new CallProgramIntentDataType { Program = program },
                new WaitIntentDataType { Signal = output },
                new WaitIntentDataType { Signal = booleanVariable },
                new LinearMoveIntentDataType { Target = Pose(), ToolFrame = toolFrame }
            ];
            foreach (IntentDataType intent in accepted)
            {
                Assert.That(host.SubmitIntent(m_context, null, intent).Accepted, Is.True, intent.GetType().Name);
            }

            Assert.Multiple(() =>
            {
                Assert.That(host.SubmitIntent(m_context, null,
                    new SetOutputIntentDataType { Output = output, Value = new Variant("wrong") }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new SetOutputIntentDataType { Output = booleanVariable, Value = new Variant(true) }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new SetOutputIntentDataType { Output = output }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new LinearMoveIntentDataType { Target = Pose(), ToolFrame = workFrame }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(host.SubmitIntent(m_context, null,
                    new CallProgramIntentDataType { Program = location }).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void EveryGeneratedNodeIdIntentMemberIsValidatedAgainstControllerScope()
        {
            NodeId location = new("loc", 1);
            NodeId tool = new("tool", 1);
            NodeId toolFrame = new("toolFrame", 1);
            NodeId output = new("out", 1);
            NodeId booleanVariable = new("bool", 1);
            NodeId program = new("program", 1);
            NodeId arbitrary = new("attacker", 1);
            IntentControllerHostOptions options = Options();
            options.Accept(RiDataTypeIds.JointMoveIntentDataType);
            options.Accept(RiDataTypeIds.CircularMoveIntentDataType);
            options.Accept(RiDataTypeIds.GraspIntentDataType);
            options.Accept(RiDataTypeIds.ReleaseIntentDataType);
            using IntentControllerHost host = NewHost(options, controller =>
            {
                AddLocation(controller, location);
                AddTool(controller, tool);
                AddFrame(controller, toolFrame, FrameRoleEnum.Tool, "tool");
                AddOutput(controller, output, global::Opc.Ua.DataTypeIds.Boolean);
                AddBooleanVariable(controller, booleanVariable);
                AddProgram(controller, program);
            });

            var factories = new Dictionary<Type, Func<IntentDataType>>
            {
                [typeof(LinearMoveIntentDataType)] = () => new LinearMoveIntentDataType
                {
                    Target = Pose(),
                    ToolFrame = toolFrame
                },
                [typeof(JointMoveIntentDataType)] = () => new JointMoveIntentDataType
                {
                    HasJointTargets = true,
                    JointTargets = new[] { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5 },
                    ToolFrame = toolFrame
                },
                [typeof(CircularMoveIntentDataType)] = () => new CircularMoveIntentDataType
                {
                    ViaPoint = Pose(),
                    Target = Pose(),
                    ToolFrame = toolFrame
                },
                [typeof(TrajectoryIntentDataType)] = () => new TrajectoryIntentDataType
                {
                    Points = new[] { Point(10), Point(20) },
                    ToolFrame = toolFrame
                },
                [typeof(CartesianPathIntentDataType)] = () => new CartesianPathIntentDataType
                {
                    Waypoints = new[] { new PathWaypointDataType { Pose = Pose() } },
                    ToolFrame = toolFrame
                },
                [typeof(ForceIntentDataType)] = () => new ForceIntentDataType
                {
                    Direction = new[] { 0.0, 0.0, -1.0 },
                    ContactForce = 5,
                    MaxDistance = 0.1,
                    ToolFrame = toolFrame
                },
                [typeof(ArcWeldIntentDataType)] = () => new ArcWeldIntentDataType { ProcessProgram = program },
                [typeof(SpotWeldIntentDataType)] = () => new SpotWeldIntentDataType { ProcessProgram = program },
                [typeof(DispenseIntentDataType)] = () => new DispenseIntentDataType { ProcessProgram = program },
                [typeof(FastenIntentDataType)] = () => new FastenIntentDataType(),
                [typeof(PalletiseIntentDataType)] = () => new PalletiseIntentDataType { Pattern = location },
                [typeof(SurfaceFinishIntentDataType)] = () => new SurfaceFinishIntentDataType
                {
                    ProcessProgram = program
                },
                [typeof(PlaceIntentDataType)] = () => new PlaceIntentDataType
                {
                    Destination = location,
                    Tool = tool
                },
                [typeof(PickIntentDataType)] = () => new PickIntentDataType
                {
                    Source = location,
                    Tool = tool
                },
                [typeof(ToolChangeIntentDataType)] = () => new ToolChangeIntentDataType
                {
                    Tool = tool,
                    DockStation = location
                },
                [typeof(GraspIntentDataType)] = () => new GraspIntentDataType { Tool = tool },
                [typeof(ReleaseIntentDataType)] = () => new ReleaseIntentDataType { Tool = tool },
                [typeof(SetOutputIntentDataType)] = () => new SetOutputIntentDataType
                {
                    Output = output,
                    Value = new Variant(true)
                },
                [typeof(CallProgramIntentDataType)] = () => new CallProgramIntentDataType { Program = program },
                [typeof(WaitIntentDataType)] = () => new WaitIntentDataType { Signal = booleanVariable }
            };
            var cases = typeof(IntentDataType).Assembly.GetTypes()
                .Where(static type => !type.IsAbstract &&
                    type != typeof(MotionIntentDataType) &&
                    type != typeof(ProcessIntentDataType) &&
                    typeof(IntentDataType).IsAssignableFrom(type))
                .Select(type => new
                {
                    Type = type,
                    Properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Where(static property => property.PropertyType == typeof(NodeId) &&
                            property.SetMethod != null)
                        .ToArray()
                })
                .Where(static item => item.Properties.Length > 0)
                .ToArray();

            Assert.Multiple(() =>
            {
                foreach (var item in cases)
                {
                    Assert.That(factories.TryGetValue(item.Type, out Func<IntentDataType>? factory), Is.True,
                        $"add a scoped-reference test factory for {item.Type.Name}");
                    if (factory == null)
                    {
                        continue;
                    }
                    Assert.That(host.SubmitIntent(m_context, null, factory()).Accepted, Is.True,
                        $"{item.Type.Name} test factory must produce an admissible intent");
                    foreach (PropertyInfo property in item.Properties)
                    {
                        IntentDataType intent = factory();
                        property.SetValue(intent, arbitrary);
                        IntentAdmission admission = host.SubmitIntent(m_context, null, intent);
                        IntentFailureEnum expected = item.Type == typeof(FastenIntentDataType) &&
                            property.Name == nameof(FastenIntentDataType.Joint)
                                ? IntentFailureEnum.CapabilityNotSupported
                                : IntentFailureEnum.ParameterInvalid;
                        Assert.That(admission.Failure, Is.EqualTo(expected),
                            $"{item.Type.Name}.{property.Name} must be rejected when outside the controller scope");
                    }
                }
            });
        }

        [Test]
        public void ForceFrameIdsResolveByFrameIdString()
        {
            using IntentControllerHost host = NewHost(null, controller =>
                AddFrame(controller, new NodeId("frame", 1), FrameRoleEnum.Base, "known"));

            Assert.Multiple(() =>
            {
                Assert.That(host.SubmitIntent(m_context, null, Force("known")).Accepted, Is.True);
                Assert.That(host.SubmitIntent(m_context, null, Force(string.Empty)).Accepted, Is.True);
                Assert.That(host.SubmitIntent(m_context, null, Force("missing")).Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
            });
        }

        [Test]
        public void AChannelLeaseIsExclusiveAndReleasable()
        {
            using IntentControllerHost host = NewHost(ChannelOptions());
            var first = new NodeId("s1", 1);
            var second = new NodeId("s2", 1);

            RealTimeLease granted = host.OpenRealTimeChannel(m_context, first, "rtde", 5000);
            Assert.Multiple(() =>
            {
                Assert.That(granted.Granted, Is.True);
                Assert.That(granted.EndpointUrl, Is.EqualTo("rtde://robot:30004"));
                Assert.That(granted.Expiry, Is.Not.EqualTo(DateTime.MinValue));
            });

            Assert.That(host.OpenRealTimeChannel(m_context, second, "rtde", 5000).Granted,
                Is.False, "a second Session must not take a held channel");

            Assert.That(host.CloseRealTimeChannel(m_context, second, "rtde"), Is.False,
                "only the holder may release the lease");
            Assert.That(host.CloseRealTimeChannel(m_context, first, "rtde"), Is.True);
            Assert.That(host.OpenRealTimeChannel(m_context, second, "rtde", 5000).Granted,
                Is.True);
        }

        [Test]
        public void AnUnknownChannelIsRefused()
        {
            using IntentControllerHost host = NewHost(ChannelOptions());

            Assert.That(host.OpenRealTimeChannel(m_context, new NodeId("s", 1), "nope", 1000).Granted,
                Is.False);
        }

        [Test]
        public void AChannelLeaseRequiresASession()
        {
            using IntentControllerHost host = NewHost(ChannelOptions());

            RealTimeLease lease = host.OpenRealTimeChannel(m_context, null, "rtde", 1000);

            Assert.That(lease.Granted, Is.False);
        }

        [Test]
        public void AChannelIsRefusedOutsideItsRequiredMode()
        {
            IntentControllerHostOptions options = ChannelOptions();
            options.OperationalMode = OperationalModeEnum.Automatic;
            using IntentControllerHost host = NewHost(options);

            RealTimeLease lease = host.OpenRealTimeChannel(m_context, new NodeId("s", 1), "rtde", 1000);

            Assert.That(lease.Granted, Is.False,
                "the channel declares it needs AutomaticExternal");
        }

        [Test]
        public void MotionIsRefusedWhileAChannelLeaseIsHeldAndNothingArbitrates()
        {
            using IntentControllerHost host = NewHost(ChannelOptions());
            host.OpenRealTimeChannel(m_context, new NodeId("s", 1), "rtde", 5000);

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure,
                    Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        [Test]
        public void MotionIsAdmittedAlongsideAChannelWhenTheHostArbitrates()
        {
            IntentControllerHostOptions options = ChannelOptions();
            options.ArbitratesWithRealTimeChannel = true;
            using IntentControllerHost host = NewHost(options);
            host.OpenRealTimeChannel(m_context, new NodeId("s", 1), "rtde", 5000);

            Assert.That(host.SubmitIntent(m_context, null, Move()).Accepted, Is.True);
        }

        [Test]
        public void SessionCloseReleasesAChannelLease()
        {
            using IntentControllerHost host = NewHost(ChannelOptions());
            var session = new NodeId("s1", 1);
            Assert.That(host.OpenRealTimeChannel(m_context, session, "rtde", 5000).Granted, Is.True);

            host.OnSessionClosed(m_context, session);

            Assert.That(host.OpenRealTimeChannel(m_context, new NodeId("s2", 1), "rtde", 5000).Granted, Is.True);
        }

        [Test]
        public async Task ChannelLeaseExpiresAndCanBeRenewed()
        {
            IntentControllerHostOptions options = ChannelOptions();
            options.MaxChannelLeaseMs = 100;
            using IntentControllerHost host = NewHost(options);
            var first = new NodeId("s1", 1);
            var second = new NodeId("s2", 1);

            RealTimeLease initial = host.OpenRealTimeChannel(m_context, first, "rtde", 1000);
            RealTimeLease renewed = host.OpenRealTimeChannel(m_context, first, "rtde", 1000);
            Assert.Multiple(() =>
            {
                Assert.That(initial.Granted, Is.True);
                Assert.That(renewed.Granted, Is.True);
                Assert.That(renewed.Expiry, Is.GreaterThanOrEqualTo(initial.Expiry));
            });

            RealTimeLease expired = RealTimeLease.Refused("not yet expired");
            await WaitAsync(() =>
            {
                expired = host.OpenRealTimeChannel(m_context, second, "rtde", 1000);
                return expired.Granted;
            }).ConfigureAwait(false);
            Assert.That(expired.Granted, Is.True);
        }

        [Test]
        public void NonPositiveRequestedLeaseUsesTheServerDefault()
        {
            IntentControllerHostOptions options = ChannelOptions();
            options.MaxChannelLeaseMs = 500;
            using IntentControllerHost host = NewHost(options);

            var first = new NodeId("s1", 1);
            var second = new NodeId("s2", 1);

            RealTimeLease requested = host.OpenRealTimeChannel(m_context, first, "rtde", 1);
            host.CloseRealTimeChannel(m_context, first, "rtde");
            RealTimeLease lease = host.OpenRealTimeChannel(m_context, second, "rtde", 0);

            Assert.That(lease.Expiry, Is.GreaterThan(requested.Expiry));
        }

        [Test]
        public void ATransitionNamingAnUnknownStepIsRefused()
        {
            using IntentControllerHost host = NewHost();
            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2) },
                Transitions = new[] { Transition("s1", "nowhere") }
            });

            Assert.That(admission.Accepted, Is.False);
        }

        [Test]
        public void AStepMixingDivergenceKindsIsRefused()
        {
            using IntentControllerHost host = NewHost();
            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2), Step("s3", 3) },
                Transitions = new[]
                {
                    Transition("s1", "s2"),
                    Transition("s1", "s3", DivergenceKindEnum.Parallel)
                }
            });

            Assert.That(admission.Accepted, Is.False,
                "a step cannot both choose one branch and take them all");
        }

        [Test]
        public void AFallbackNamingAnUnknownStepIsRefused()
        {
            using IntentControllerHost host = NewHost();
            MissionStepDataType step = Step("s1", 1);
            step.ErrorPolicy = ErrorPolicyEnum.Fallback;
            step.FallbackStepId = "nowhere";

            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { step, Step("s2", 2) }
            });

            Assert.That(admission.Accepted, Is.False);
        }

        [Test]
        public void MissionStepIdsMustBeUniqueAndSequenceIdsAscending()
        {
            using IntentControllerHost host = NewHost();

            MissionAdmission duplicate = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "duplicate",
                Steps = new[] { Step("s1", 1), Step("s1", 2) }
            });
            MissionAdmission unordered = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "unordered",
                Steps = new[] { Step("s1", 2), Step("s2", 1) }
            });

            Assert.Multiple(() =>
            {
                Assert.That(duplicate.Accepted, Is.False);
                Assert.That(unordered.Accepted, Is.False);
            });
        }

        [Test]
        public async Task ASkipPolicyContinuesPastAFailedStep()
        {
            using IntentControllerHost host = NewHost();
            m_executor.FailFirst = true;

            MissionStepDataType first = Step("s1", 1);
            first.ErrorPolicy = ErrorPolicyEnum.Skip;

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { first, Step("s2", 2) }
            });

            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            Assert.That(m_executor.Started, Has.Length.EqualTo(2),
                "the mission must reach the second step even though the first failed");
        }

        [Test]
        public async Task AnAbortPolicyStopsAtTheFailedStep()
        {
            using IntentControllerHost host = NewHost();
            m_executor.FailFirst = true;

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2) }
            });

            await WaitAsync(() => MissionReached(ExecutionStateEnum.Failed)).ConfigureAwait(false);
            Assert.That(m_executor.Started, Has.Length.EqualTo(1),
                "Abort is the default and must not begin a later step");
        }

        [Test]
        public async Task ARetryPolicyReattemptsUpToTheConfiguredBound()
        {
            IntentControllerHostOptions options = Options();
            options.MaxStepRetries = 2;
            using IntentControllerHost host = NewHost(options);
            m_executor.AlwaysFail = true;

            MissionStepDataType step = Step("s1", 1);
            step.ErrorPolicy = ErrorPolicyEnum.Retry;

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { step }
            });

            await WaitAsync(() => MissionReached(ExecutionStateEnum.Failed)).ConfigureAwait(false);
            Assert.That(m_executor.Started, Has.Length.EqualTo(3),
                "one attempt plus two retries");
        }

        [Test]
        public async Task AnUnconditionalTransitionChoosesTheNextStep()
        {
            using IntentControllerHost host = NewHost();
            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2), Step("s3", 3) },
                // The graph skips s2 entirely; without it the mission would run all three.
                Transitions = new[] { Transition("s1", "s3") }
            });

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(["s1", "s3"]));
        }

        [Test]
        public async Task AlternativeTransitionsUseArrayOrder()
        {
            using IntentControllerHost host = NewHost();
            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2), Step("s3", 3) },
                Transitions = new[] { Transition("s1", "s2"), Transition("s1", "s3") }
            });

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(["s1", "s2"]));
        }

        [Test]
        public async Task FallbackPolicyRunsTheFallbackAndContinues()
        {
            using IntentControllerHost host = NewHost();
            m_executor.FailFirst = true;
            MissionStepDataType first = Step("s1", 1);
            first.ErrorPolicy = ErrorPolicyEnum.Fallback;
            first.FallbackStepId = "s3";

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { first, Step("s2", 2), Step("s3", 3) }
            });

            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            Assert.That(m_executor.Started.Take(2), Is.EqualTo(["s1", "s3"]));
        }

        [Test]
        public async Task CompensatePolicyRunsFallbackAndEndsMission()
        {
            using IntentControllerHost host = NewHost();
            m_executor.FailFirst = true;
            MissionStepDataType first = Step("s1", 1);
            first.ErrorPolicy = ErrorPolicyEnum.Compensate;
            first.FallbackStepId = "s3";

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { first, Step("s2", 2), Step("s3", 3), Step("s4", 4) }
            });

            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            await WaitAsync(() => MissionReached(ExecutionStateEnum.Failed)).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(["s1", "s3"]));
        }

        [Test]
        public async Task TransitionsAreIgnoredWhenBranchingIsNotSupported()
        {
            IntentControllerHostOptions options = Options();
            options.MissionBranchingSupported = false;
            using IntentControllerHost host = NewHost(options);

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2), Step("s3", 3) },
                Transitions = new[] { Transition("s1", "s3") }
            });

            await WaitAsync(() => m_executor.Started.Length >= 3).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(["s1", "s2", "s3"]),
                "a host that declares no branching runs the steps in order");
        }

        [Test]
        public async Task AMissionWithoutTransitionsIsStillTheFlatSequence()
        {
            using IntentControllerHost host = NewHost();
            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2) }
            });

            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(["s1", "s2"]));
        }

        private static IntentControllerHostOptions Options()
        {
            var options = new IntentControllerHostOptions
            {
                RequireControlAuthority = false,
                AxisCount = 6,
                MaxQueueDepth = 8,
                ForceControlSupported = true
            };
            options.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            options.Accept(RiDataTypeIds.TrajectoryIntentDataType);
            options.Accept(RiDataTypeIds.CartesianPathIntentDataType);
            options.Accept(RiDataTypeIds.ForceIntentDataType);
            options.Accept(RiDataTypeIds.ArcWeldIntentDataType);
            options.Accept(RiDataTypeIds.SpotWeldIntentDataType);
            options.Accept(RiDataTypeIds.DispenseIntentDataType);
            options.Accept(RiDataTypeIds.FastenIntentDataType);
            options.Accept(RiDataTypeIds.PalletiseIntentDataType);
            options.Accept(RiDataTypeIds.SurfaceFinishIntentDataType);
            options.Accept(RiDataTypeIds.PlaceIntentDataType);
            options.Accept(RiDataTypeIds.PickIntentDataType);
            options.Accept(RiDataTypeIds.ToolChangeIntentDataType);
            options.Accept(RiDataTypeIds.SetOutputIntentDataType);
            options.Accept(RiDataTypeIds.CallProgramIntentDataType);
            options.Accept(RiDataTypeIds.WaitIntentDataType);
            return options;
        }

        private static IntentControllerHostOptions ChannelOptions()
        {
            IntentControllerHostOptions options = Options();
            options.RealTimeChannelsSupported = true;
            options.Channels.Add(new DeclaredChannel
            {
                ChannelId = "rtde",
                Transport = RealTimeTransportEnum.Rtde,
                EndpointUrl = "rtde://robot:30004",
                Initiator = ChannelInitiatorEnum.Client,
                NominalRate = 500,
                PayloadDescriptor = "actual_q,actual_TCP_pose",
                RequiredMode = OperationalModeEnum.AutomaticExternal
            });
            return options;
        }

        private IntentControllerHost NewHost(
            IntentControllerHostOptions? options = null,
            Action<IntentControllerState>? configureController = null)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                m_context,
                new NodeId(Guid.NewGuid().ToString(), 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            configureController?.Invoke(controller);
            var host = new IntentControllerHost(
                controller,
                m_executor,
                (node, _) =>
                {
                    m_executor.Added.Enqueue(node);
                    return default;
                },
                options ?? Options());
            host.Start(m_context);
            return host;
        }

        private bool MissionReached(ExecutionStateEnum state)
        {
            return m_executor.Added
                .OfType<MissionObjectState>()
                .Any(mission => mission.ExecutionState?.Value == state);
        }

        private static Pose3DDataType Pose()
        {
            return new Pose3DDataType
            {
                FrameId = "base",
                Position = new[] { 1.0, 0.0, 0.0 },
                Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static LinearMoveIntentDataType Move(string id = "")
        {
            return new LinearMoveIntentDataType { IntentId = id, Target = Pose() };
        }

        private static ForceIntentDataType Force(string frameId)
        {
            return new ForceIntentDataType
            {
                Direction = new[] { 0.0, 0.0, -1.0 },
                FrameId = frameId,
                ContactForce = 5,
                MaxDistance = 0.1
            };
        }

        private static TrajectoryPointDataType Point(double timeMs)
        {
            return new TrajectoryPointDataType
            {
                TimeFromStart = timeMs,
                Positions = new[] { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5 }
            };
        }

        private static MissionStepDataType Step(string id, uint sequence)
        {
            return new MissionStepDataType
            {
                StepId = id,
                SequenceId = sequence,
                Released = true,
                Intent = Move(id)
            };
        }

        private static MissionTransitionDataType Transition(
            string from, string to,
            DivergenceKindEnum kind = DivergenceKindEnum.Alternative)
        {
            return new MissionTransitionDataType
            {
                FromStepId = from,
                ToStepId = to,
                DivergenceKind = kind
            };
        }

        private async Task<IntentOperationState> WaitForOperationAsync(NodeId operation)
        {
            IntentOperationState? node = null;
            await WaitAsync(() =>
            {
                node = m_executor.Added.OfType<IntentOperationState>()
                    .FirstOrDefault(n => n.NodeId == operation);
                return node?.ExecutionState?.Value is { } state && IntentOutcome.IsTerminal(state);
            }).ConfigureAwait(false);
            return node!;
        }

        private static bool HasOutput(ArrayOf<KeyValuePair> outputs, string name)
        {
            for (int ii = 0; ii < outputs.Count; ii++)
            {
                if (outputs[ii].Key.Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private void AddLocation(IntentControllerState controller, NodeId nodeId)
        {
            AddChild(controller.Locations!, new LocationState(controller.Locations!), nodeId, "Location");
        }

        private void AddTool(IntentControllerState controller, NodeId nodeId)
        {
            AddChild(controller.Tools!, new ToolState(controller.Tools!), nodeId, "Tool");
        }

        private void AddProgram(IntentControllerState controller, NodeId nodeId)
        {
            FolderState programs = EnsurePrograms(controller);
            AddChild(programs, new ProgramState(programs), nodeId, "Program");
        }

        private void AddFrame(
            IntentControllerState controller, NodeId nodeId, FrameRoleEnum role, string frameId)
        {
            var frame = new CoordinateFrameState(controller.Frames!);
            frame.Create(
                m_context,
                nodeId,
                new QualifiedName("Frame", 1),
                new LocalizedText("Frame"),
                false);
            frame.Role!.Value = role;
            frame.FrameId!.Value = frameId;
            controller.Frames!.AddChild(frame);
        }

        private void AddOutput(IntentControllerState controller, NodeId nodeId, NodeId dataType)
        {
            FolderState outputs = EnsureOutputs(controller);
            var output = new OutputSignalState(outputs)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Output", 1),
                DisplayName = new LocalizedText("Output")
            };
            output.Value = new BaseDataVariableState(output)
            {
                BrowseName = new QualifiedName("Value", 1),
                DisplayName = new LocalizedText("Value"),
                DataType = dataType
            };
            outputs.AddChild(output);
        }

        private void AddBooleanVariable(IntentControllerState controller, NodeId nodeId)
        {
            FolderState outputs = EnsureOutputs(controller);
            var variable = new BaseDataVariableState(outputs)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("BooleanVariable", 1),
                DisplayName = new LocalizedText("BooleanVariable"),
                DataType = global::Opc.Ua.DataTypeIds.Boolean,
                ValueRank = ValueRanks.Scalar
            };
            outputs.AddChild(variable);
        }

        private static FolderState EnsureOutputs(IntentControllerState controller)
        {
            if (controller.Outputs != null)
            {
                return controller.Outputs;
            }
            controller.Outputs = new FolderState(controller)
            {
                NodeId = new NodeId("Outputs", 1),
                BrowseName = new QualifiedName("Outputs", 1),
                DisplayName = new LocalizedText("Outputs")
            };
            controller.AddChild(controller.Outputs);
            return controller.Outputs;
        }

        private static FolderState EnsurePrograms(IntentControllerState controller)
        {
            if (controller.Programs != null)
            {
                return controller.Programs;
            }
            controller.Programs = new FolderState(controller)
            {
                NodeId = new NodeId("Programs", 1),
                BrowseName = new QualifiedName("Programs", 1),
                DisplayName = new LocalizedText("Programs")
            };
            controller.AddChild(controller.Programs);
            return controller.Programs;
        }

        private void AddWrongTypedChild(NodeState folder, NodeId nodeId)
        {
            AddChild(folder, new FolderState(folder), nodeId, "Wrong");
        }

        private T AddChild<T>(NodeState folder, T node, NodeId nodeId, string browseName)
            where T : BaseInstanceState
        {
            node.NodeId = nodeId;
            node.BrowseName = new QualifiedName(browseName, 1);
            node.DisplayName = new LocalizedText(browseName);
            folder.AddChild(node);
            return node;
        }

        private static async Task WaitAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("timed out waiting for the expected condition");
        }

        private SystemContext m_context = null!;
        private ScriptedExecutor m_executor = null!;

        private sealed class ScriptedExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> StartedQueue { get; } = new();
            public ConcurrentQueue<NodeState> Added { get; } = new();
            public string[] Started => [.. StartedQueue];
            public bool FailFirst { get; set; }
            public bool AlwaysFail { get; set; }
            public IntentOutcome Outcome { get; set; } = IntentOutcome.Success;
            public IntentDataType? LastIntent { get; private set; }
            public Action<IntentExecution>? OnExecute { get; set; }

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution, CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref m_calls);
                StartedQueue.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                LastIntent = execution.Intent;
                OnExecute?.Invoke(execution);
                if (AlwaysFail || (FailFirst && call == 1))
                {
                    return new ValueTask<IntentOutcome>(
                        IntentOutcome.Fail(IntentFailureEnum.Other, "scripted failure"));
                }
                return new ValueTask<IntentOutcome>(Outcome);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }

            private int m_calls;
        }

        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 0.0];
    }
}
