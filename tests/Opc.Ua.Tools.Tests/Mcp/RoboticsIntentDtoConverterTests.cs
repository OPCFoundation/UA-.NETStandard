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

#if NET10_0
using System;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests the DTO-based intent converter, the controller resolver, and the
    /// operation/mission list paging helper.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsIntentDtoConverterTests
    {
        [Test]
        public void JointMoveFromJointTargetsProducesCorrectIntent()
        {
            var dto = new JointMoveIntentInput
            {
                JointTargets = [0.1, -0.2, 0.3, 0.4, -0.5, 0.6]
            };

            var intent = (JointMoveIntentDataType)RoboticsIntentDtoConverter.ConvertJointMove(dto, 6, null);

            Assert.That(intent.JointTargets.Count, Is.EqualTo(6));
            Assert.That(intent.JointTargets[0], Is.EqualTo(0.1).Within(1e-12));
            Assert.That(intent.JointTargets[5], Is.EqualTo(0.6).Within(1e-12));
        }

        [Test]
        public void JointMoveFromTargetPoseProducesCorrectIntent()
        {
            var dto = new JointMoveIntentInput
            {
                TargetPose = MakePose(1.0, 2.0, 3.0, 0.0, 0.0, 0.0, 1.0, "Base")
            };

            var intent = (JointMoveIntentDataType)RoboticsIntentDtoConverter.ConvertJointMove(dto, 0, null);

            Assert.That(intent.TargetPose.Position[0], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(intent.TargetPose.FrameId, Is.EqualTo("Base"));
        }

        [Test]
        public void JointMoveWithNeitherTargetsNorPoseIsRejected()
        {
            var dto = new JointMoveIntentInput();

            Assert.That(() => RoboticsIntentDtoConverter.ConvertJointMove(dto, 0, null), Throws.ArgumentException);
        }

        [Test]
        public void LinearMoveCarriesTargetAndSpeedFraction()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(0.4, 0.0, 0.25, 0.0, 1.0, 0.0, 0.0),
                SpeedFraction = 0.5
            };

            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(dto, null);

            Assert.That(intent.Target.Position[0], Is.EqualTo(0.4).Within(1e-12));
            Assert.That(intent.Constraints.SpeedFraction, Is.EqualTo(0.5).Within(1e-12));
        }

        [Test]
        public void CircularMoveCarriesViaPointAndTarget()
        {
            var dto = new CircularMoveIntentInput
            {
                ViaPoint = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 1.0),
                Target = MakePose(0.4, 0.5, 0.6, 0.0, 0.0, 0.0, 1.0)
            };

            var intent = (CircularMoveIntentDataType)RoboticsIntentDtoConverter.ConvertCircularMove(dto, null);

            Assert.That(intent.ViaPoint.Position[1], Is.EqualTo(0.2).Within(1e-12));
            Assert.That(intent.Target.Position[2], Is.EqualTo(0.6).Within(1e-12));
        }

        [Test]
        public void TrajectoryConvertsPointsWithOptionalVelocities()
        {
            var dto = new TrajectoryIntentInput
            {
                Points =
                [
                    new TrajectoryPointDto { TimeFromStart = 0.0, Positions = [0.0, 0.0] },
                    new TrajectoryPointDto
                    {
                        TimeFromStart = 1.5, Positions = [0.1, 0.2],
                        Velocities = [0.3, 0.4], Accelerations = [0.5, 0.6]
                    }
                ]
            };

            var intent = (TrajectoryIntentDataType)RoboticsIntentDtoConverter.ConvertTrajectory(dto, null);

            Assert.That(intent.Points.Count, Is.EqualTo(2));
            Assert.That(intent.Points[0].Velocities.Count, Is.Zero);
            Assert.That(intent.Points[1].TimeFromStart, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(intent.Points[1].Velocities[1], Is.EqualTo(0.4).Within(1e-12));
        }

        [Test]
        public void TrajectoryWithEmptyPointsIsRejected()
        {
            var dto = new TrajectoryIntentInput { Points = [] };

            Assert.That(() => RoboticsIntentDtoConverter.ConvertTrajectory(dto, null), Throws.ArgumentException);
        }

        [Test]
        public void CartesianPathConvertsWaypointsWithBlend()
        {
            var dto = new CartesianPathIntentInput
            {
                Waypoints =
                [
                    new CartesianWaypointDto
                    {
                        Pose = MakePose(0.1, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0),
                        Blend = new BlendDto { Termination = TerminationModeEnum.Blend, Radius = 0.02 }
                    }
                ]
            };

            var intent = (CartesianPathIntentDataType)RoboticsIntentDtoConverter.ConvertCartesianPath(dto, null);

            Assert.That(intent.Waypoints.Count, Is.EqualTo(1));
            Assert.That(intent.Waypoints[0].Blend.Termination, Is.EqualTo(TerminationModeEnum.Blend));
            Assert.That(intent.Waypoints[0].Blend.Radius, Is.EqualTo(0.02).Within(1e-12));
        }

        [Test]
        public void ForceConvertsDirectionAndOptionalFields()
        {
            var dto = new ForceIntentInput
            {
                Direction = [0.0, 0.0, -1.0],
                ContactForce = 12.5,
                FrameId = "Tcp",
                MaxDistance = 0.05,
                HoldForce = true
            };

            var intent = (ForceIntentDataType)RoboticsIntentDtoConverter.ConvertForce(dto, null);

            Assert.That(intent.Direction[2], Is.EqualTo(-1.0).Within(1e-12));
            Assert.That(intent.ContactForce, Is.EqualTo(12.5).Within(1e-12));
            Assert.That(intent.FrameId, Is.EqualTo("Tcp"));
            Assert.That(intent.MaxDistance, Is.EqualTo(0.05).Within(1e-12));
            Assert.That(intent.HoldForce, Is.True);
        }

        [Test]
        public void ForceWithWrongDirectionLengthIsRejected()
        {
            var dto = new ForceIntentInput { Direction = [0.0, -1.0], ContactForce = 10 };

            Assert.That(() => RoboticsIntentDtoConverter.ConvertForce(dto, null), Throws.ArgumentException);
        }

        [Test]
        public void ArcWeldConvertsProcessFields()
        {
            var dto = new ArcWeldIntentInput
            {
                Voltage = 24.0,
                WireFeedSpeed = 7.5,
                TravelSpeed = 0.01,
                SeamTrackingEnabled = true,
                WeldProcedureRef = "WPS-17"
            };

            var intent = (ArcWeldIntentDataType)RoboticsIntentDtoConverter.ConvertArcWeld(dto, null);

            Assert.That(intent.Voltage, Is.EqualTo(24.0).Within(1e-12));
            Assert.That(intent.SeamTrackingEnabled, Is.True);
            Assert.That(intent.WeldProcedureRef, Is.EqualTo("WPS-17"));
        }

        [Test]
        public void SpotWeldConvertsProcessFields()
        {
            var dto = new SpotWeldIntentInput { WeldSchedule = 4, GunForce = 2200.0 };

            var intent = (SpotWeldIntentDataType)RoboticsIntentDtoConverter.ConvertSpotWeld(dto, null);

            Assert.That(intent.WeldSchedule, Is.EqualTo(4u));
            Assert.That(intent.GunForce, Is.EqualTo(2200.0).Within(1e-12));
        }

        [Test]
        public void DispenseConvertsProcessFields()
        {
            var dto = new DispenseIntentInput { FlowRate = 3.5, BeadWidth = 0.004, PurgeCycles = 2 };

            var intent = (DispenseIntentDataType)RoboticsIntentDtoConverter.ConvertDispense(dto, null);

            Assert.That(intent.FlowRate, Is.EqualTo(3.5).Within(1e-12));
            Assert.That(intent.PurgeCycles, Is.EqualTo(2u));
        }

        [Test]
        public void FastenConvertsProcessFields()
        {
            var dto = new FastenIntentInput
            {
                Joint = "ns=2;s=Joint",
                ProgramNumber = 3,
                TargetTorque = 18.0
            };

            var intent = (FastenIntentDataType)RoboticsIntentDtoConverter.ConvertFasten(dto, null);

            Assert.That(intent.Joint.IsNull, Is.False);
            Assert.That(intent.ProgramNumber, Is.EqualTo(3u));
            Assert.That(intent.TargetTorque, Is.EqualTo(18.0).Within(1e-12));
        }

        [Test]
        public void PalletiseConvertsProcessFields()
        {
            var dto = new PalletiseIntentInput
            {
                Pattern = "ns=2;s=Pat",
                Layer = 1,
                Row = 2,
                Column = 3
            };

            var intent = (PalletiseIntentDataType)RoboticsIntentDtoConverter.ConvertPalletise(dto, null);

            Assert.That(intent.Pattern.IsNull, Is.False);
            Assert.That(intent.Layer, Is.EqualTo(1u));
        }

        [Test]
        public void SurfaceFinishConvertsProcessFields()
        {
            var dto = new SurfaceFinishIntentInput
            {
                ContactForce = 30.0,
                FeedRate = 0.02,
                ToolSpeed = 1200.0,
                StepOver = 0.003
            };

            var intent = (SurfaceFinishIntentDataType)RoboticsIntentDtoConverter.ConvertSurfaceFinish(dto, null);

            Assert.That(intent.ContactForce, Is.EqualTo(30.0).Within(1e-12));
            Assert.That(intent.StepOver, Is.EqualTo(0.003).Within(1e-12));
        }

        [Test]
        public void GraspConvertsToolAndForce()
        {
            var dto = new GraspIntentInput { Tool = "ns=2;s=Gripper", Force = 25.0 };

            var intent = (GraspIntentDataType)RoboticsIntentDtoConverter.ConvertGrasp(dto, null);

            Assert.That(intent.Tool.IsNull, Is.False);
            Assert.That(intent.Force, Is.EqualTo(25.0).Within(1e-12));
        }

        [Test]
        public void GraspWithEmptyToolIsRejected()
        {
            var dto = new GraspIntentInput { Tool = string.Empty, Force = 10 };

            Assert.That(() => RoboticsIntentDtoConverter.ConvertGrasp(dto, null), Throws.ArgumentException);
        }

        [Test]
        public void ReleaseConvertsTool()
        {
            var dto = new ReleaseIntentInput { Tool = "ns=2;s=Gripper" };

            var intent = (ReleaseIntentDataType)RoboticsIntentDtoConverter.ConvertRelease(dto, null);

            Assert.That(intent.Tool.IsNull, Is.False);
        }

        [Test]
        public void PickConvertsSourceToolAndObjectClass()
        {
            var dto = new PickIntentInput
            {
                Source = "ns=2;s=Bin",
                Tool = "ns=2;s=Gripper",
                ObjectClass = "BlueSphere"
            };

            var intent = (PickIntentDataType)RoboticsIntentDtoConverter.ConvertPick(dto, null);

            Assert.That(intent.Source.IsNull, Is.False);
            Assert.That(intent.Tool.IsNull, Is.False);
            Assert.That(intent.ObjectClass, Is.EqualTo("BlueSphere"));
        }

        [Test]
        public void PlaceConvertsDestinationAndTool()
        {
            var dto = new PlaceIntentInput
            {
                Destination = "ns=2;s=Slot",
                Tool = "ns=2;s=Gripper"
            };

            var intent = (PlaceIntentDataType)RoboticsIntentDtoConverter.ConvertPlace(dto, null);

            Assert.That(intent.Destination.IsNull, Is.False);
        }

        [Test]
        public void ToolChangeConvertsToolAndDock()
        {
            var dto = new ToolChangeIntentInput
            {
                Tool = "ns=2;s=Tool",
                DockStation = "ns=2;s=Dock"
            };

            var intent = (ToolChangeIntentDataType)RoboticsIntentDtoConverter.ConvertToolChange(dto, null);

            Assert.That(intent.Tool.IsNull, Is.False);
            Assert.That(intent.DockStation.IsNull, Is.False);
        }

        [Test]
        public void SetOutputConvertsTypedValue()
        {
            var dto = new SetOutputIntentInput
            {
                Output = "ns=2;s=Do1",
                Value = new TypedValueDto
                {
                    DataType = "Boolean",
                    Value = JsonDocument.Parse("true").RootElement
                }
            };

            var intent = (SetOutputIntentDataType)RoboticsIntentDtoConverter.ConvertSetOutput(dto, null);

            Assert.That(intent.Output.IsNull, Is.False);
        }

        [Test]
        public void CallProgramConvertsArguments()
        {
            var dto = new CallProgramIntentInput
            {
                Program = "ns=2;s=Prog",
                Arguments =
                [
                    new NamedTypedValueDto
                    {
                        Name = "Count",
                        DataType = "Int32",
                        Value = JsonDocument.Parse("3").RootElement
                    }
                ]
            };

            var intent = (CallProgramIntentDataType)RoboticsIntentDtoConverter.ConvertCallProgram(dto, null);

            Assert.That(intent.Program.IsNull, Is.False);
            Assert.That(intent.Arguments.Count, Is.EqualTo(1));
        }

        [Test]
        public void WaitConvertsDurationAndSignal()
        {
            var dto = new WaitIntentInput { Duration = 2.5, Signal = "ns=2;s=Di1" };

            var intent = (WaitIntentDataType)RoboticsIntentDtoConverter.ConvertWait(dto, null);

            Assert.That(intent.Duration, Is.EqualTo(2.5).Within(1e-12));
            Assert.That(intent.Signal.IsNull, Is.False);
        }

        [Test]
        public void CommonFieldsAreApplied()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 1.0),
                IntentId = "i-42",
                Label = "Approach",
                BufferMode = BufferModeEnum.Buffered,
                BlockingMode = BlockingModeEnum.Single,
                ToolFrame = "ns=2;s=Tcp",
                Constraints = new MotionConstraintsDto
                {
                    SpeedFraction = 0.25,
                    CartesianSpeed = 0.3,
                    CartesianAcceleration = 0.4,
                    Jerk = 0.5
                },
                Blend = new BlendDto { Termination = TerminationModeEnum.Blend, Radius = 0.01 }
            };

            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(dto, null);

            Assert.That(intent.IntentId, Is.EqualTo("i-42"));
            Assert.That(intent.Label.Text, Is.EqualTo("Approach"));
            Assert.That(intent.BufferMode, Is.EqualTo(BufferModeEnum.Buffered));
            Assert.That(intent.BlockingMode, Is.EqualTo(BlockingModeEnum.Single));
            Assert.That(intent.ToolFrame.IsNull, Is.False);
            Assert.That(intent.Constraints.CartesianAcceleration, Is.EqualTo(0.4).Within(1e-12));
            Assert.That(intent.Blend.Radius, Is.EqualTo(0.01).Within(1e-12));
        }

        [Test]
        public void MissionIntentDiscriminatorRoutes()
        {
            var input = new MissionIntentInput
            {
                Kind = IntentKind.Wait,
                Duration = 1.5
            };

            var intent = (WaitIntentDataType)RoboticsIntentDtoConverter.ConvertIntent(input, null);

            Assert.That(intent.Duration, Is.EqualTo(1.5).Within(1e-12));
        }

        [Test]
        public void MissingRequiredFieldsForDiscriminatorAreRejected()
        {
            var input = new MissionIntentInput { Kind = IntentKind.Wait };

            Assert.That(() => RoboticsIntentDtoConverter.ConvertIntent(input, null), Throws.ArgumentException);
        }

        [Test]
        public void KindPayloadMismatchIsRejected()
        {
            var input = new MissionIntentInput
            {
                Kind = IntentKind.Wait,
                Force = 5
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertIntent(input, null),
                Throws.ArgumentException.With.Message.Contains("does not accept"));
        }

        [Test]
        public void FieldsForAnotherIntentKindAreRejected()
        {
            var input = new MissionIntentInput
            {
                Kind = IntentKind.Wait,
                Duration = 1.0,
                Tool = "ns=2;s=Gripper"
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertIntent(input, null),
                Throws.ArgumentException.With.Message.Contains("does not accept"));
        }

        [Test]
        public void UndefinedDiscriminatorValueIsRejected()
        {
            var input = new MissionIntentInput { Kind = (IntentKind)999 };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertIntent(input, null),
                Throws.ArgumentException.With.Message.Contains("Unknown intent kind"));
        }

        [Test]
        public void IntentKindSerializesAsExactMemberName()
        {
            string json = JsonSerializer.Serialize(
                new MissionIntentInput
                {
                    Kind = IntentKind.CartesianPath,
                    Waypoints = []
                },
                kMcpJson);

            Assert.That(json, Does.Contain("\"CartesianPath\""));
        }

        [Test]
        public void IntentKindRoundTripsExactMemberName()
        {
            MissionIntentInput? input = JsonSerializer.Deserialize<MissionIntentInput>(
                "{\"kind\":\"Wait\"}", kMcpJson);

            Assert.That(input, Is.Not.Null);
            Assert.That(input!.Kind, Is.EqualTo(IntentKind.Wait));
        }

        [Test]
        public void IntentKindRejectsUnknownValue()
        {
            Assert.That(
                () => JsonSerializer.Deserialize<MissionIntentInput>(
                    "{\"kind\":\"teleport\"}", kMcpJson),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void IntentKindRejectsFreeFormStringPayload()
        {
            Assert.That(
                () => JsonSerializer.Deserialize<MissionIntentInput>("{\"kind\":\"\"}", kMcpJson),
                Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void IntentKindCoversEveryMissionIntentVariant()
        {
            IntentKind[] kinds = Enum.GetValues<IntentKind>();

            Assert.That(kinds, Has.Length.EqualTo(20));
        }

        [Test]
        public void MissionStepsConvert()
        {
            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    StepId = "s1",
                    Released = true,
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Wait,
                        Duration = 1.0
                    }
                },
                new MissionStepInput
                {
                    StepId = "s2",
                    SequenceId = 7,
                    ErrorPolicy = ErrorPolicyEnum.Skip,
                    FallbackStepId = "s1",
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Wait,
                        Duration = 2.0
                    }
                }
            ];

            ArrayOf<MissionStepDataType> result = RoboticsIntentDtoConverter.ConvertMissionSteps(steps, null);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].StepId, Is.EqualTo("s1"));
            Assert.That(result[0].Released, Is.True);
            Assert.That(result[0].SequenceId, Is.EqualTo(1u));
            Assert.That(result[1].SequenceId, Is.EqualTo(7u));
            Assert.That(result[1].ErrorPolicy, Is.EqualTo(ErrorPolicyEnum.Skip));
            Assert.That(result[1].FallbackStepId, Is.EqualTo("s1"));
        }

        [Test]
        public void EmptyMissionStepsProduceEmptySet()
        {
            Assert.That(RoboticsIntentDtoConverter.ConvertMissionSteps([], null).Count, Is.Zero);
        }

        [Test]
        public void MissionStepWithoutStepIdIsRejected()
        {
            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Wait,
                        Duration = 1.0
                    }
                }
            ];

            Assert.That(() => RoboticsIntentDtoConverter.ConvertMissionSteps(steps, null), Throws.ArgumentException);
        }

        [Test]
        public void MissionTransitionsConvert()
        {
            MissionTransitionInput[] transitions =
            [
                new MissionTransitionInput { FromStepId = "s1", ToStepId = "s2" },
                new MissionTransitionInput
                {
                    FromStepId = "s2",
                    ToStepId = "s3",
                    DivergenceKind = DivergenceKindEnum.Parallel
                }
            ];

            ArrayOf<MissionTransitionDataType> result =
                RoboticsIntentDtoConverter.ConvertMissionTransitions(transitions);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].DivergenceKind, Is.EqualTo(DivergenceKindEnum.Alternative));
            Assert.That(result[1].DivergenceKind, Is.EqualTo(DivergenceKindEnum.Parallel));
        }

        [Test]
        public void NullMissionTransitionsProduceEmptySet()
        {
            Assert.That(RoboticsIntentDtoConverter.ConvertMissionTransitions(default).Count, Is.Zero);
        }

        [Test]
        public void InvalidNodeIdIsRejected()
        {
            Assert.That(
                () => RoboticsIntentDtoConverter.ResolveNodeId("not a node id"),
                Throws.ArgumentException);
        }

        [Test]
        public void ValidNodeIdIsResolved()
        {
            NodeId result = RoboticsIntentDtoConverter.ResolveNodeId("ns=2;s=Foo");

            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void EmptyStringResolvesToNull()
        {
            NodeId result = RoboticsIntentDtoConverter.ResolveNodeId(string.Empty);

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void PoseWithMissingPositionIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = new PoseDto { Orientation = new QuaternionDto { W = 1 } }
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("position"));
        }

        [Test]
        public void PoseWithMissingOrientationIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = new PoseDto { Position = new PosePositionDto { X = 1 } }
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("orientation"));
        }

        [Test]
        public void MissingTargetPoseIsRejectedRatherThanDefaultedToZero()
        {
            var dto = new LinearMoveIntentInput();

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("'target' is required"));
        }

        [Test]
        public void ZeroQuaternionIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = new PoseDto
                {
                    Position = new PosePositionDto(),
                    Orientation = new QuaternionDto()
                }
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("unit quaternion"));
        }

        [Test]
        public void NonUnitQuaternionIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 0.5)
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("unit quaternion"));
        }

        [Test]
        public void NonFinitePositionIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(double.NaN, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0)
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("finite"));
        }

        [Test]
        public void SpeedFractionAboveOneIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 1.0),
                SpeedFraction = 1.5
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("[0, 1]"));
        }

        [Test]
        public void NegativeCartesianSpeedIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 1.0),
                Constraints = new MotionConstraintsDto { SpeedFraction = 0.5, CartesianSpeed = -1 }
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("cartesianSpeed"));
        }

        [Test]
        public void BlendTerminationWithoutRadiusIsRejected()
        {
            var dto = new LinearMoveIntentInput
            {
                Target = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 1.0),
                Blend = new BlendDto { Termination = TerminationModeEnum.Blend, Radius = 0 }
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(dto, null),
                Throws.ArgumentException.With.Message.Contains("radius"));
        }

        [Test]
        public void JointMoveWithBothTargetsAndPoseIsRejected()
        {
            var dto = new JointMoveIntentInput
            {
                JointTargets = [0.1, 0.2],
                TargetPose = MakePose(0.1, 0.2, 0.3, 0.0, 0.0, 0.0, 1.0)
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertJointMove(dto, 0, null),
                Throws.ArgumentException.With.Message.Contains("not both"));
        }

        [Test]
        public void JointMoveWithNonFiniteTargetIsRejected()
        {
            var dto = new JointMoveIntentInput { JointTargets = [0.1, double.PositiveInfinity] };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertJointMove(dto, 0, null),
                Throws.ArgumentException.With.Message.Contains("finite"));
        }

        [Test]
        public void JointMoveWithWrongAxisCountIsRejected()
        {
            var dto = new JointMoveIntentInput { JointTargets = [0.1, 0.2] };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertJointMove(dto, 6, null),
                Throws.ArgumentException);
        }

        [Test]
        public void TrajectoryWithInconsistentJointCountIsRejected()
        {
            var dto = new TrajectoryIntentInput
            {
                Points =
                [
                    new TrajectoryPointDto { TimeFromStart = 0.0, Positions = [0.0, 0.0] },
                    new TrajectoryPointDto { TimeFromStart = 1.0, Positions = [0.0, 0.0, 0.0] }
                ]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertTrajectory(dto, null),
                Throws.ArgumentException.With.Message.Contains("trajectory uses 2"));
        }

        [Test]
        public void TrajectoryWithMismatchedVelocitiesIsRejected()
        {
            var dto = new TrajectoryIntentInput
            {
                Points =
                [
                    new TrajectoryPointDto
                    {
                        TimeFromStart = 0.0,
                        Positions = [0.0, 0.0],
                        Velocities = [0.1]
                    }
                ]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertTrajectory(dto, null),
                Throws.ArgumentException.With.Message.Contains("velocities"));
        }

        [Test]
        public void TrajectoryWithNegativeTimeIsRejected()
        {
            var dto = new TrajectoryIntentInput
            {
                Points = [new TrajectoryPointDto { TimeFromStart = -1.0, Positions = [0.0] }]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertTrajectory(dto, null),
                Throws.ArgumentException.With.Message.Contains("must not be negative"));
        }

        [Test]
        public void TrajectoryWithNonIncreasingTimeIsRejected()
        {
            var dto = new TrajectoryIntentInput
            {
                Points =
                [
                    new TrajectoryPointDto { TimeFromStart = 1.0, Positions = [0.0] },
                    new TrajectoryPointDto { TimeFromStart = 1.0, Positions = [0.1] }
                ]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertTrajectory(dto, null),
                Throws.ArgumentException.With.Message.Contains("strictly greater"));
        }

        [Test]
        public void TrajectoryPointWithoutPositionsIsRejected()
        {
            var dto = new TrajectoryIntentInput
            {
                Points = [new TrajectoryPointDto { TimeFromStart = 0.0 }]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertTrajectory(dto, null),
                Throws.ArgumentException.With.Message.Contains("positions"));
        }

        [Test]
        public void CartesianPathWithoutWaypointPoseIsRejected()
        {
            var dto = new CartesianPathIntentInput
            {
                Waypoints = [new CartesianWaypointDto()]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertCartesianPath(dto, null),
                Throws.ArgumentException.With.Message.Contains("waypoints[0].pose"));
        }

        [Test]
        public void ForceWithZeroDirectionIsRejected()
        {
            var dto = new ForceIntentInput { Direction = [0.0, 0.0, 0.0], ContactForce = 5 };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertForce(dto, null),
                Throws.ArgumentException.With.Message.Contains("zero vector"));
        }

        [Test]
        public void ForceWithNonPositiveContactForceIsRejected()
        {
            var dto = new ForceIntentInput { Direction = [0.0, 0.0, -1.0], ContactForce = 0 };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertForce(dto, null),
                Throws.ArgumentException.With.Message.Contains("contactForce"));
        }

        [Test]
        public void ForceWithNegativeMaxDistanceIsRejected()
        {
            var dto = new ForceIntentInput
            {
                Direction = [0.0, 0.0, -1.0],
                ContactForce = 5,
                MaxDistance = -0.1
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertForce(dto, null),
                Throws.ArgumentException.With.Message.Contains("maxDistance"));
        }

        [Test]
        public void NegativeProcessParametersAreRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => RoboticsIntentDtoConverter.ConvertArcWeld(
                        new ArcWeldIntentInput { Voltage = -1 }, null),
                    Throws.ArgumentException.With.Message.Contains("voltage"));
                Assert.That(
                    () => RoboticsIntentDtoConverter.ConvertSpotWeld(
                        new SpotWeldIntentInput { GunForce = -1 }, null),
                    Throws.ArgumentException.With.Message.Contains("gunForce"));
                Assert.That(
                    () => RoboticsIntentDtoConverter.ConvertDispense(
                        new DispenseIntentInput { FlowRate = -1 }, null),
                    Throws.ArgumentException.With.Message.Contains("flowRate"));
                Assert.That(
                    () => RoboticsIntentDtoConverter.ConvertFasten(
                        new FastenIntentInput { TargetTorque = -1 }, null),
                    Throws.ArgumentException.With.Message.Contains("targetTorque"));
                Assert.That(
                    () => RoboticsIntentDtoConverter.ConvertSurfaceFinish(
                        new SurfaceFinishIntentInput { StepOver = -1 }, null),
                    Throws.ArgumentException.With.Message.Contains("stepOver"));
            });
        }

        [Test]
        public void GraspWithNegativeForceIsRejected()
        {
            var dto = new GraspIntentInput { Tool = "ns=2;s=Gripper", Force = -1 };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertGrasp(dto, null),
                Throws.ArgumentException.With.Message.Contains("force"));
        }

        [Test]
        public void WaitWithoutDurationOrSignalIsRejected()
        {
            var dto = new WaitIntentInput();

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertWait(dto, null),
                Throws.ArgumentException.With.Message.Contains("positive duration"));
        }

        [Test]
        public void WaitWithNegativeDurationIsRejected()
        {
            var dto = new WaitIntentInput { Duration = -1 };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertWait(dto, null),
                Throws.ArgumentException.With.Message.Contains("duration"));
        }

        [Test]
        public void SetOutputWithoutValueIsRejected()
        {
            var dto = new SetOutputIntentInput { Output = "ns=2;s=Do1" };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertSetOutput(dto, null),
                Throws.ArgumentException.With.Message.Contains("'value' is required"));
        }

        [Test]
        public void SetOutputWithoutDataTypeIsRejected()
        {
            var dto = new SetOutputIntentInput
            {
                Output = "ns=2;s=Do1",
                Value = new TypedValueDto { Value = JsonDocument.Parse("true").RootElement }
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertSetOutput(dto, null),
                Throws.ArgumentException.With.Message.Contains("dataType"));
        }

        [Test]
        public void NamedTypedValueWithoutDataTypeIsRejected()
        {
            var dto = new CallProgramIntentInput
            {
                Program = "ns=2;s=Prog",
                Arguments =
                [
                    new NamedTypedValueDto
                    {
                        Name = "Count",
                        Value = JsonDocument.Parse("3").RootElement
                    }
                ]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertCallProgram(dto, null),
                Throws.ArgumentException.With.Message.Contains("dataType"));
        }

        [Test]
        public void DuplicateNamedTypedValueNamesAreRejected()
        {
            var dto = new CallProgramIntentInput
            {
                Program = "ns=2;s=Prog",
                Arguments =
                [
                    new NamedTypedValueDto
                    {
                        Name = "Count",
                        DataType = "Int32",
                        Value = JsonDocument.Parse("3").RootElement
                    },
                    new NamedTypedValueDto
                    {
                        Name = "Count",
                        DataType = "Int32",
                        Value = JsonDocument.Parse("4").RootElement
                    }
                ]
            };

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertCallProgram(dto, null),
                Throws.ArgumentException.With.Message.Contains("repeats"));
        }

        [Test]
        public void ProcessAttributesAreCarriedAsTypedNamedValues()
        {
            var dto = new DispenseIntentInput
            {
                FlowRate = 1.0,
                Attributes =
                [
                    new NamedTypedValueDto
                    {
                        Name = "Nozzle",
                        DataType = "Int32",
                        Value = JsonDocument.Parse("7").RootElement
                    }
                ]
            };

            var intent = (DispenseIntentDataType)RoboticsIntentDtoConverter.ConvertDispense(dto, null);

            Assert.That(intent.Attributes.Count, Is.EqualTo(1));
            Assert.That(intent.Attributes[0].Key.Name, Is.EqualTo("Nozzle"));
            Assert.That(intent.Attributes[0].Value.TryGetValue(out int nozzle), Is.True);
            Assert.That(nozzle, Is.EqualTo(7));
        }

        [Test]
        public void CallProgramArgumentsAreCarriedAsTypedNamedValues()
        {
            var dto = new CallProgramIntentInput
            {
                Program = "ns=2;s=Prog",
                Arguments =
                [
                    new NamedTypedValueDto
                    {
                        Name = "Count",
                        DataType = "Int32",
                        Value = JsonDocument.Parse("3").RootElement
                    }
                ]
            };

            var intent = (CallProgramIntentDataType)RoboticsIntentDtoConverter.ConvertCallProgram(dto, null);

            Assert.That(intent.Arguments[0].Key.Name, Is.EqualTo("Count"));
            Assert.That(intent.Arguments[0].Value.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public void MissionStepWithoutIntentIsRejected()
        {
            MissionStepInput[] steps = [new MissionStepInput { StepId = "s1" }];

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertMissionSteps(steps, null),
                Throws.ArgumentException.With.Message.Contains("missing its intent"));
        }

        [Test]
        public void DuplicateMissionStepIdsAreRejected()
        {
            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    StepId = "s1",
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Wait,
                        Duration = 1.0
                    }
                },
                new MissionStepInput
                {
                    StepId = "s1",
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Wait,
                        Duration = 2.0
                    }
                }
            ];

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertMissionSteps(steps, null),
                Throws.ArgumentException.With.Message.Contains("repeats stepId"));
        }

        private static readonly JsonSerializerOptions kMcpJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static PoseDto MakePose(
            double px, double py, double pz,
            double ox, double oy, double oz, double ow,
            string? frameId = null)
        {
            return new PoseDto
            {
                Position = new PosePositionDto { X = px, Y = py, Z = pz },
                Orientation = new QuaternionDto { X = ox, Y = oy, Z = oz, W = ow },
                FrameId = frameId
            };
        }
    }
}
#endif
