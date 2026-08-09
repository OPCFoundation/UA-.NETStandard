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
using NUnit.Framework;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client.Tests
{
    [TestFixture]
    [Category("Robotics")]
    public sealed class IntentClientBuildersTests
    {
        [Test]
        public void LinearMoveBuilderSetsTargetAndSpeed()
        {
            Pose3DDataType pose = RobotIntentBuilder.Pose(1, 2, 3, 0, 0, 0, 1, "world");

            LinearMoveIntentDataType intent = RobotIntentBuilder.LinearMove(pose, 0.5).Build();

            Assert.That(intent.Target.FrameId, Is.EqualTo("world"));
            Assert.That(intent.Constraints.SpeedFraction, Is.EqualTo(0.5));
        }

        [Test]
        public void IntentBuilderSetsSharedIntentMembers()
        {
            WaitIntentDataType intent = RobotIntentBuilder.Wait(25)
                .WithIntentId("intent-1")
                .WithLabel(new LocalizedText("move"))
                .WithBufferMode(BufferModeEnum.Aborting)
                .WithBlockingMode(BlockingModeEnum.Hard)
                .Build();

            Assert.That(intent.IntentId, Is.EqualTo("intent-1"));
            Assert.That(intent.Label.Text, Is.EqualTo("move"));
            Assert.That(intent.BufferMode, Is.EqualTo(BufferModeEnum.Aborting));
            Assert.That(intent.BlockingMode, Is.EqualTo(BlockingModeEnum.Hard));
            Assert.That(intent.Duration, Is.EqualTo(25));
        }

        [Test]
        public void MotionBuilderSetsMotionMembers()
        {
            Pose3DDataType pose = RobotIntentBuilder.Pose(1, 2, 3, 0, 0, 0, 1, "world");
            var toolFrame = new NodeId(12);
            var constraints = new MotionConstraintsDataType { SpeedFraction = 0.2, CartesianSpeed = 0.4 };
            var blend = new BlendDataType { Termination = TerminationModeEnum.Blend, Radius = 0.3 };

            LinearMoveIntentDataType intent = new LinearMoveIntentBuilder()
                .To(pose)
                .WithToolFrame(toolFrame)
                .WithConstraints(constraints)
                .CartesianSpeed(0.6)
                .WithBlend(blend)
                .Exact()
                .Blend(0.7)
                .Build();

            Assert.That(intent.Target, Is.SameAs(pose));
            Assert.That(intent.ToolFrame, Is.EqualTo(toolFrame));
            Assert.That(intent.Constraints.SpeedFraction, Is.EqualTo(0.2));
            Assert.That(intent.Constraints.CartesianSpeed, Is.EqualTo(0.6));
            Assert.That(intent.Blend.Termination, Is.EqualTo(TerminationModeEnum.Blend));
            Assert.That(intent.Blend.Radius, Is.EqualTo(0.7));
        }

        [Test]
        public void BuildersCreateEveryIntentType()
        {
            Pose3DDataType pose = RobotIntentBuilder.Pose(0, 0, 0, 0, 0, 0, 1);
            ArrayOf<TrajectoryPointDataType> points =
            [
                new TrajectoryPointDataType { TimeFromStart = 1, Positions = [1, 2] }
            ];
            ArrayOf<PathWaypointDataType> waypoints =
            [
                new PathWaypointDataType { Pose = pose }
            ];

            Assert.That(RobotIntentBuilder.JointMove(2).ToJoints([1, 2]).Build(), Is.TypeOf<JointMoveIntentDataType>());
            Assert.That(RobotIntentBuilder.LinearMove(pose, 1).Build(), Is.TypeOf<LinearMoveIntentDataType>());
            Assert.That(RobotIntentBuilder.CircularMove(pose, pose).Build(), Is.TypeOf<CircularMoveIntentDataType>());
            Assert.That(
                RobotIntentBuilder.Trajectory().WithPoints(points).Build(),
                Is.TypeOf<TrajectoryIntentDataType>());
            Assert.That(
                RobotIntentBuilder.CartesianPath().WithWaypoints(waypoints).Build(),
                Is.TypeOf<CartesianPathIntentDataType>());
            Assert.That(RobotIntentBuilder.Force([0, 0, 1], 10).Build(), Is.TypeOf<ForceIntentDataType>());
            Assert.That(RobotIntentBuilder.ArcWeld().Build(), Is.TypeOf<ArcWeldIntentDataType>());
            Assert.That(RobotIntentBuilder.SpotWeld().Build(), Is.TypeOf<SpotWeldIntentDataType>());
            Assert.That(RobotIntentBuilder.Dispense().Build(), Is.TypeOf<DispenseIntentDataType>());
            Assert.That(RobotIntentBuilder.Fasten().Build(), Is.TypeOf<FastenIntentDataType>());
            Assert.That(RobotIntentBuilder.Palletise().Build(), Is.TypeOf<PalletiseIntentDataType>());
            Assert.That(RobotIntentBuilder.SurfaceFinish().Build(), Is.TypeOf<SurfaceFinishIntentDataType>());
            Assert.That(RobotIntentBuilder.Grasp(new NodeId(1), 1).Build(), Is.TypeOf<GraspIntentDataType>());
            Assert.That(RobotIntentBuilder.Release(new NodeId(1)).Build(), Is.TypeOf<ReleaseIntentDataType>());
            Assert.That(RobotIntentBuilder.Pick(new NodeId(1), new NodeId(2)).Build(), Is.TypeOf<PickIntentDataType>());
            Assert.That(
                RobotIntentBuilder.Place(new NodeId(1), new NodeId(2)).Build(),
                Is.TypeOf<PlaceIntentDataType>());
            Assert.That(
                RobotIntentBuilder.ToolChange(new NodeId(1), new NodeId(2)).Build(),
                Is.TypeOf<ToolChangeIntentDataType>());
            Assert.That(
                RobotIntentBuilder.SetOutput(new NodeId(1), Variant.From(true)).Build(),
                Is.TypeOf<SetOutputIntentDataType>());
            Assert.That(RobotIntentBuilder.CallProgram(new NodeId(1)).Build(), Is.TypeOf<CallProgramIntentDataType>());
            Assert.That(RobotIntentBuilder.Wait(100).Build(), Is.TypeOf<WaitIntentDataType>());
        }

        [Test]
        public void SimpleIntentBuildersSetSpecificMembers()
        {
            var tool = new NodeId(1);
            var other = new NodeId(2);
            Variant outputValue = Variant.From(true);

            Assert.That(RobotIntentBuilder.Grasp(tool, 2.5).Build().Force, Is.EqualTo(2.5));
            Assert.That(RobotIntentBuilder.Grasp(tool, 2.5).Build().Tool, Is.EqualTo(tool));
            Assert.That(RobotIntentBuilder.Release(tool).Build().Tool, Is.EqualTo(tool));
            Assert.That(RobotIntentBuilder.Pick(other, tool).Build().Source, Is.EqualTo(other));
            Assert.That(RobotIntentBuilder.Pick(other, tool).Build().Tool, Is.EqualTo(tool));
            Assert.That(RobotIntentBuilder.Place(other, tool).Build().Destination, Is.EqualTo(other));
            Assert.That(RobotIntentBuilder.Place(other, tool).Build().Tool, Is.EqualTo(tool));
            Assert.That(RobotIntentBuilder.ToolChange(tool, other).Build().DockStation, Is.EqualTo(other));
            Assert.That(RobotIntentBuilder.SetOutput(other, outputValue).Build().Value, Is.EqualTo(outputValue));
            Assert.That(RobotIntentBuilder.CallProgram(other).Build().Program, Is.EqualTo(other));
        }

        [Test]
        public void ProcessIntentBuilderSetsProgramAndAttributes()
        {
            var program = new NodeId(100);
            ArrayOf<KeyValuePair> attributes =
            [
                new KeyValuePair { Key = new QualifiedName("voltage"), Value = Variant.From(12.5) }
            ];

            ArcWeldIntentDataType intent = RobotIntentBuilder.ArcWeld()
                .WithProcessProgram(program)
                .WithAttributes(attributes)
                .Build();

            Assert.That(intent.ProcessProgram, Is.EqualTo(program));
            Assert.That(intent.Attributes.Count, Is.EqualTo(1));
            Assert.That(intent.Attributes[0].Key.Name, Is.EqualTo("voltage"));
        }

        [Test]
        public void JointMoveBuilderRejectsAxisCountMismatch()
        {
            ArgumentException? ex = Assert.Throws<ArgumentException>(
                () => RobotIntentBuilder.JointMove(6).ToJoints([1, 2]));

            Assert.That(ex?.ParamName, Is.EqualTo("jointTargets"));
        }

        [Test]
        public void JointMoveBuilderControlsHasJointTargets()
        {
            Pose3DDataType pose = RobotIntentBuilder.Pose(1, 2, 3, 0, 0, 0, 1, "world");

            JointMoveIntentDataType jointTarget = RobotIntentBuilder.JointMove(2)
                .ToJoints([1, 2])
                .Build();
            JointMoveIntentDataType poseTarget = RobotIntentBuilder.JointMove(2)
                .ToPose(pose)
                .Build();

            Assert.That(jointTarget.HasJointTargets, Is.True);
            Assert.That(jointTarget.JointTargets, Is.EqualTo(new double[] { 1, 2 }));
            Assert.That(jointTarget.TargetPose.Position, Is.Empty);
            Assert.That(poseTarget.HasJointTargets, Is.False);
            Assert.That(poseTarget.JointTargets, Is.Empty);
            Assert.That(poseTarget.TargetPose, Is.SameAs(pose));
        }

        [Test]
        public void CapabilityFacetsDeriveAbortingRule()
        {
            RobotIntentControllerInfo info = new()
            {
                AxisCount = 6,
                MaxQueueDepth = 1,
                TrajectorySupported = true,
                ForceControlSupported = true,
                MissionsSupported = true,
                MissionHorizonSupported = true,
                MissionBranchingSupported = true,
                BlendingSupported = true,
                RealTimeChannelsSupported = true,
                SupportedIntents =
                [
                    new IntentCapabilityDataType
                    {
                        SupportedBufferModes = [BufferModeEnum.Buffered]
                    }
                ]
            };

            RobotIntentFacets facets = RobotIntentRules.DeriveFacets(info);

            Assert.That(facets.Trajectories, Is.True);
            Assert.That(facets.MissionBranching, Is.True);
            Assert.That(facets.ForceControl, Is.True);
            Assert.That(facets.EveryCapabilitySupportsAborting, Is.False);
        }

        [Test]
        public void MissionBuilderValidatesUnknownTransitionStep()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .HorizonStep("a", RobotIntentBuilder.Wait(1).Build())
                .Transition("a", "missing", DivergenceKindEnum.Alternative, new ContentFilter());

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionBuilderValidatesMixedDivergenceKinds()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .HorizonStep("a", RobotIntentBuilder.Wait(1).Build())
                .HorizonStep("b", RobotIntentBuilder.Wait(1).Build())
                .HorizonStep("c", RobotIntentBuilder.Wait(1).Build())
                .Transition("a", "b", DivergenceKindEnum.Alternative, new ContentFilter())
                .Transition("a", "c", DivergenceKindEnum.Parallel, new ContentFilter());

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionBuilderValidatesUnknownFallbackStep()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .HorizonStep("a", RobotIntentBuilder.Wait(1).Build())
                .ErrorPolicy("a", ErrorPolicyEnum.Fallback, "missing");

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionBuilderValidatesUniqueStepIds()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .WithSteps(
                [
                    new MissionStepDataType
                    {
                        StepId = "a",
                        SequenceId = 1,
                        Intent = RobotIntentBuilder.Wait(1).Build()
                    },
                    new MissionStepDataType
                    {
                        StepId = "a",
                        SequenceId = 2,
                        Intent = RobotIntentBuilder.Wait(1).Build()
                    }
                ]);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionBuilderValidatesSequenceIdAscending()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .WithSteps(
                [
                    new MissionStepDataType
                    {
                        StepId = "a",
                        SequenceId = 2,
                        Intent = RobotIntentBuilder.Wait(1).Build()
                    },
                    new MissionStepDataType
                    {
                        StepId = "b",
                        SequenceId = 1,
                        Intent = RobotIntentBuilder.Wait(1).Build()
                    }
                ]);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionBuilderValidatesReleasedStepsFormPrefix()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .WithSteps(
                [
                    new MissionStepDataType
                    {
                        StepId = "a",
                        SequenceId = 1,
                        Released = false,
                        Intent = RobotIntentBuilder.Wait(1).Build()
                    },
                    new MissionStepDataType
                    {
                        StepId = "b",
                        SequenceId = 2,
                        Released = true,
                        Intent = RobotIntentBuilder.Wait(1).Build()
                    }
                ]);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionBuilderAllowsEmptyTransitionsForFlatSequence()
        {
            MissionDataType mission = RobotIntentBuilder.Mission()
                .ReleasedStep("a", RobotIntentBuilder.Wait(1).Build())
                .HorizonStep("b", RobotIntentBuilder.Wait(1).Build())
                .WithTransitions([])
                .Build();

            Assert.That(mission.Transitions, Is.Empty);
        }

        [Test]
        public void MissionBuilderSetsMissionMetadataAndTransitions()
        {
            ContentFilter condition = MissionCondition.Always();

            MissionDataType mission = RobotIntentBuilder.Mission("mission-1")
                .WithMissionUpdateId(2)
                .WithLabel(new LocalizedText("mission"))
                .ReleasedStep("a", RobotIntentBuilder.Wait(1).Build())
                .HorizonStep("b", RobotIntentBuilder.Wait(2).Build())
                .Transition("a", "b", DivergenceKindEnum.Alternative, condition)
                .ErrorPolicy("b", ErrorPolicyEnum.Fallback, "a")
                .Build();

            Assert.That(mission.MissionId, Is.EqualTo("mission-1"));
            Assert.That(mission.MissionUpdateId, Is.EqualTo(2));
            Assert.That(mission.Label.Text, Is.EqualTo("mission"));
            Assert.That(mission.Steps.Count, Is.EqualTo(2));
            Assert.That(mission.Steps[1].ErrorPolicy, Is.EqualTo(ErrorPolicyEnum.Fallback));
            Assert.That(mission.Steps[1].FallbackStepId, Is.EqualTo("a"));
            Assert.That(mission.Transitions.Count, Is.EqualTo(1));
            Assert.That(mission.Transitions[0].Condition, Is.SameAs(condition));
        }

        [Test]
        public void MissionBuilderRejectsInvalidStepConstruction()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .ReleasedStep("a", RobotIntentBuilder.Wait(1).Build());

            Assert.Throws<ArgumentException>(() => RobotIntentBuilder.Mission()
                .ReleasedStep(string.Empty, RobotIntentBuilder.Wait(1).Build()));
            Assert.Throws<ArgumentException>(() => builder.HorizonStep("a", RobotIntentBuilder.Wait(2).Build()));
            Assert.Throws<ArgumentNullException>(() => RobotIntentBuilder.Mission().HorizonStep("b", null!));
            Assert.Throws<ArgumentException>(() => builder.ErrorPolicy("missing", ErrorPolicyEnum.Abort));
        }

        [Test]
        public void MissionBuilderValidatesUnknownCompensateStep()
        {
            MissionBuilder builder = RobotIntentBuilder.Mission()
                .HorizonStep("a", RobotIntentBuilder.Wait(1).Build())
                .ErrorPolicy("a", ErrorPolicyEnum.Compensate, "missing");

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void MissionConditionAlwaysCreatesEmptyContentFilter()
        {
            ContentFilter condition = MissionCondition.Always();

            Assert.That(condition.Elements, Is.Empty);
        }

        [Test]
        public void MissionConditionEqualsCreatesContentFilterElement()
        {
            var operand = new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                BrowsePath = [new QualifiedName("State")]
            };

            ContentFilter condition = MissionCondition.Equals(operand, Variant.From(1));

            Assert.That(condition.Elements.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => MissionCondition.Equals(null!, Variant.From(1)));
        }
    }
}
