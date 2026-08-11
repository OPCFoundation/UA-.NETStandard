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
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests the translation of agent-supplied JSON into Robot Intent structures.
    /// The MCP layer accepts this JSON from a language model, so malformed input
    /// has to be rejected rather than silently producing a partly filled intent.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsIntentJsonTests
    {
        [Test]
        public void JointMoveFromJointTargetsCarriesEveryTarget()
        {
            const string json = """{"jointTargets":[0.1,-0.2,0.3,0.4,-0.5,0.6]}""";

            var intent = (JointMoveIntentDataType)RoboticsIntentJson.BuildIntent("jointmove", json, 6);

            Assert.That(intent.JointTargets.Count, Is.EqualTo(6));
            Assert.That(intent.JointTargets[0], Is.EqualTo(0.1).Within(1e-12));
            Assert.That(intent.JointTargets[5], Is.EqualTo(0.6).Within(1e-12));
        }

        [Test]
        public void JointMoveFallsBackToTargetPoseWhenJointTargetsAreAbsent()
        {
            const string json = """
                {"targetPose":{"position":[1.0,2.0,3.0],"orientation":[0.0,0.0,0.0,1.0],"frameId":"Base"}}
                """;

            var intent = (JointMoveIntentDataType)RoboticsIntentJson.BuildIntent("joint_move", json);

            Assert.That(intent.TargetPose.Position[0], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(intent.TargetPose.Position[2], Is.EqualTo(3.0).Within(1e-12));
            Assert.That(intent.TargetPose.FrameId, Is.EqualTo("Base"));
        }

        [Test]
        public void LinearMoveCarriesTargetAndSpeedFraction()
        {
            const string json = """
                {"target":{"position":[0.4,0.0,0.25],"orientation":[0.0,1.0,0.0,0.0]},"speedFraction":0.5}
                """;

            var intent = (LinearMoveIntentDataType)RoboticsIntentJson.BuildIntent("linear_move", json);

            Assert.That(intent.Target.Position[0], Is.EqualTo(0.4).Within(1e-12));
            Assert.That(intent.Constraints.SpeedFraction, Is.EqualTo(0.5).Within(1e-12));
        }

        [Test]
        public void CircularMoveCarriesViaPointAndTarget()
        {
            const string json = """
                {"viaPoint":{"position":[0.1,0.2,0.3],"orientation":[0.0,0.0,0.0,1.0]},
                 "target":{"position":[0.4,0.5,0.6],"orientation":[0.0,0.0,0.0,1.0]}}
                """;

            var intent = (CircularMoveIntentDataType)RoboticsIntentJson.BuildIntent("circularmove", json);

            Assert.That(intent.ViaPoint.Position[1], Is.EqualTo(0.2).Within(1e-12));
            Assert.That(intent.Target.Position[2], Is.EqualTo(0.6).Within(1e-12));
        }

        [Test]
        public void TrajectoryKeepsOptionalVelocitiesAndAccelerationsSeparate()
        {
            const string json = """
                {"points":[
                  {"timeFromStart":0.0,"positions":[0.0,0.0]},
                  {"timeFromStart":1.5,"positions":[0.1,0.2],"velocities":[0.3,0.4],"accelerations":[0.5,0.6]}]}
                """;

            var intent = (TrajectoryIntentDataType)RoboticsIntentJson.BuildIntent("trajectory", json);

            Assert.That(intent.Points.Count, Is.EqualTo(2));
            Assert.That(intent.Points[0].Velocities.Count, Is.Zero);
            Assert.That(intent.Points[1].TimeFromStart, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(intent.Points[1].Velocities[1], Is.EqualTo(0.4).Within(1e-12));
            Assert.That(intent.Points[1].Accelerations[0], Is.EqualTo(0.5).Within(1e-12));
        }

        [Test]
        public void CartesianPathCarriesPerWaypointBlend()
        {
            const string json = """
                {"waypoints":[
                  {"pose":{"position":[0.1,0.0,0.0],"orientation":[0.0,0.0,0.0,1.0]},
                   "blend":{"termination":"Blend","radius":0.02}}]}
                """;

            var intent = (CartesianPathIntentDataType)RoboticsIntentJson.BuildIntent("cartesian_path", json);

            Assert.That(intent.Waypoints.Count, Is.EqualTo(1));
            Assert.That(intent.Waypoints[0].Blend.Termination, Is.EqualTo(TerminationModeEnum.Blend));
            Assert.That(intent.Waypoints[0].Blend.Radius, Is.EqualTo(0.02).Within(1e-12));
        }

        [Test]
        public void ForceCarriesDirectionAndItsOwnOptionalFields()
        {
            const string json = """
                {"direction":[0.0,0.0,-1.0],"contactForce":12.5,"frameId":"Tcp",
                 "maxDistance":0.05,"holdForce":true}
                """;

            var intent = (ForceIntentDataType)RoboticsIntentJson.BuildIntent("force", json);

            Assert.That(intent.Direction[2], Is.EqualTo(-1.0).Within(1e-12));
            Assert.That(intent.ContactForce, Is.EqualTo(12.5).Within(1e-12));
            Assert.That(intent.FrameId, Is.EqualTo("Tcp"));
            Assert.That(intent.MaxDistance, Is.EqualTo(0.05).Within(1e-12));
            Assert.That(intent.HoldForce, Is.True);
        }

        [Test]
        public void ArcWeldCarriesItsProcessSpecificFields()
        {
            const string json = """
                {"voltage":24.0,"wireFeedSpeed":7.5,"travelSpeed":0.01,
                 "seamTrackingEnabled":true,"weldProcedureRef":"WPS-17"}
                """;

            var intent = (ArcWeldIntentDataType)RoboticsIntentJson.BuildIntent("arc_weld", json);

            Assert.That(intent.Voltage, Is.EqualTo(24.0).Within(1e-12));
            Assert.That(intent.WireFeedSpeed, Is.EqualTo(7.5).Within(1e-12));
            Assert.That(intent.SeamTrackingEnabled, Is.True);
            Assert.That(intent.WeldProcedureRef, Is.EqualTo("WPS-17"));
        }

        [Test]
        public void SpotWeldCarriesItsProcessSpecificFields()
        {
            var intent = (SpotWeldIntentDataType)RoboticsIntentJson.BuildIntent(
                "spotweld", """{"weldSchedule":4,"gunForce":2200.0}""");

            Assert.That(intent.WeldSchedule, Is.EqualTo(4u));
            Assert.That(intent.GunForce, Is.EqualTo(2200.0).Within(1e-12));
        }

        [Test]
        public void DispenseCarriesItsProcessSpecificFields()
        {
            var intent = (DispenseIntentDataType)RoboticsIntentJson.BuildIntent(
                "dispense", """{"flowRate":3.5,"beadWidth":0.004,"purgeCycles":2}""");

            Assert.That(intent.FlowRate, Is.EqualTo(3.5).Within(1e-12));
            Assert.That(intent.BeadWidth, Is.EqualTo(0.004).Within(1e-12));
            Assert.That(intent.PurgeCycles, Is.EqualTo(2u));
        }

        [Test]
        public void FastenCarriesItsProcessSpecificFields()
        {
            var intent = (FastenIntentDataType)RoboticsIntentJson.BuildIntent(
                "fasten", """{"joint":"ns=2;s=Joint","programNumber":3,"targetTorque":18.0}""");

            Assert.That(intent.Joint.IsNull, Is.False);
            Assert.That(intent.ProgramNumber, Is.EqualTo(3u));
            Assert.That(intent.TargetTorque, Is.EqualTo(18.0).Within(1e-12));
        }

        [Test]
        public void PalletiseCarriesItsProcessSpecificFields()
        {
            var intent = (PalletiseIntentDataType)RoboticsIntentJson.BuildIntent(
                "palletize", """{"pattern":"ns=2;s=Pattern","layer":1,"row":2,"column":3}""");

            Assert.That(intent.Pattern.IsNull, Is.False);
            Assert.That(intent.Layer, Is.EqualTo(1u));
            Assert.That(intent.Row, Is.EqualTo(2u));
            Assert.That(intent.Column, Is.EqualTo(3u));
        }

        [Test]
        public void SurfaceFinishCarriesItsProcessSpecificFields()
        {
            var intent = (SurfaceFinishIntentDataType)RoboticsIntentJson.BuildIntent(
                "surface_finish",
                """{"contactForce":30.0,"feedRate":0.02,"toolSpeed":1200.0,"stepOver":0.003}""");

            Assert.That(intent.ContactForce, Is.EqualTo(30.0).Within(1e-12));
            Assert.That(intent.FeedRate, Is.EqualTo(0.02).Within(1e-12));
            Assert.That(intent.ToolSpeed, Is.EqualTo(1200.0).Within(1e-12));
            Assert.That(intent.StepOver, Is.EqualTo(0.003).Within(1e-12));
        }

        [Test]
        public void ProcessIntentCarriesProgramAndAttributes()
        {
            const string json = """
                {"processProgram":"ns=3;s=Weld","attributes":{"Pass":2,"Mode":"Root"}}
                """;

            var intent = (ArcWeldIntentDataType)RoboticsIntentJson.BuildIntent("arcweld", json);

            Assert.That(intent.ProcessProgram.IsNull, Is.False);
            Assert.That(intent.Attributes.Count, Is.EqualTo(2));
        }

        [Test]
        public void GraspReleasePickAndPlaceResolveTheirNodeMembers()
        {
            var grasp = (GraspIntentDataType)RoboticsIntentJson.BuildIntent(
                "grasp", """{"tool":"ns=2;s=Gripper","force":25.0}""");
            var release = (ReleaseIntentDataType)RoboticsIntentJson.BuildIntent(
                "release", """{"tool":"ns=2;s=Gripper"}""");
            var pick = (PickIntentDataType)RoboticsIntentJson.BuildIntent(
                "pick", """{"source":"ns=2;s=Bin","tool":"ns=2;s=Gripper"}""");
            var place = (PlaceIntentDataType)RoboticsIntentJson.BuildIntent(
                "place", """{"destination":"ns=2;s=Slot","tool":"ns=2;s=Gripper"}""");

            Assert.That(grasp.Tool.IsNull, Is.False);
            Assert.That(grasp.Force, Is.EqualTo(25.0).Within(1e-12));
            Assert.That(release.Tool.IsNull, Is.False);
            Assert.That(pick.Source.IsNull, Is.False);
            Assert.That(place.Destination.IsNull, Is.False);
        }

        [Test]
        public void ToolChangeCallProgramSetOutputAndWaitResolveTheirMembers()
        {
            var toolChange = (ToolChangeIntentDataType)RoboticsIntentJson.BuildIntent(
                "tool_change", """{"tool":"ns=2;s=Tool","dockStation":"ns=2;s=Dock"}""");
            var callProgram = (CallProgramIntentDataType)RoboticsIntentJson.BuildIntent(
                "call_program", """{"program":"ns=2;s=Prog","arguments":{"Count":3}}""");
            var setOutput = (SetOutputIntentDataType)RoboticsIntentJson.BuildIntent(
                "set_output", """{"output":"ns=2;s=Do1","value":true,"dataType":"Boolean"}""");
            var wait = (WaitIntentDataType)RoboticsIntentJson.BuildIntent(
                "wait", """{"duration":2.5,"signal":"ns=2;s=Di1"}""");

            Assert.That(toolChange.Tool.IsNull, Is.False);
            Assert.That(toolChange.DockStation.IsNull, Is.False);
            Assert.That(callProgram.Program.IsNull, Is.False);
            Assert.That(callProgram.Arguments.Count, Is.EqualTo(1));
            Assert.That(setOutput.Output.IsNull, Is.False);
            Assert.That(wait.Duration, Is.EqualTo(2.5).Within(1e-12));
            Assert.That(wait.Signal.IsNull, Is.False);
        }

        [Test]
        public void CommonFieldsApplyToEveryIntentKind()
        {
            const string json = """
                {"target":{"position":[0.1,0.2,0.3],"orientation":[0.0,0.0,0.0,1.0]},
                 "intentId":"i-42","label":"Approach","bufferMode":"Buffered","blockingMode":"Single",
                 "toolFrame":"ns=2;s=Tcp",
                 "constraints":{"speedFraction":0.25,"cartesianSpeed":0.3,"cartesianAcceleration":0.4,"jerk":0.5},
                 "blend":{"termination":"Blend","radius":0.01}}
                """;

            var intent = (LinearMoveIntentDataType)RoboticsIntentJson.BuildIntent("linearmove", json);

            Assert.That(intent.IntentId, Is.EqualTo("i-42"));
            Assert.That(intent.Label.Text, Is.EqualTo("Approach"));
            Assert.That(intent.BufferMode, Is.EqualTo(BufferModeEnum.Buffered));
            Assert.That(intent.BlockingMode, Is.EqualTo(BlockingModeEnum.Single));
            Assert.That(intent.ToolFrame.IsNull, Is.False);
            Assert.That(intent.Constraints.CartesianAcceleration, Is.EqualTo(0.4).Within(1e-12));
            Assert.That(intent.Constraints.Jerk, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(intent.Blend.Radius, Is.EqualTo(0.01).Within(1e-12));
        }

        [Test]
        public void AnAbsentIntentBodyStillProducesAnIntentWhereTheKindAllowsIt()
        {
            var intent = (WaitIntentDataType)RoboticsIntentJson.BuildIntent("wait", null);

            Assert.That(intent.Duration, Is.Zero);
            Assert.That(intent.Signal.IsNull, Is.True);
        }

        [Test]
        public void AnUnknownIntentKindIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("teleport", "{}"),
                Throws.ArgumentException);
        }

        [Test]
        public void AnEmptyIntentKindIsRejected()
        {
            Assert.That(() => RoboticsIntentJson.BuildIntent(" ", "{}"), Throws.ArgumentException);
        }

        [Test]
        public void IntentJsonThatIsNotAnObjectIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("wait", "[1,2,3]"),
                Throws.ArgumentException);
        }

        [Test]
        public void AMissingRequiredPropertyIsRejectedRatherThanDefaulted()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("linearmove", "{}"),
                Throws.ArgumentException);
        }

        [Test]
        public void MalformedIntentJsonIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("wait", "{ not json"),
                Throws.ArgumentException);
        }

        [Test]
        public void MalformedMissionStepsJsonIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionSteps("[{ not json"),
                Throws.ArgumentException);
        }

        [Test]
        public void MalformedMissionTransitionsJsonIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionTransitions("[{ not json"),
                Throws.ArgumentException);
        }

        [Test]
        public void ANodeIdThatCannotBeParsedIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("grasp", """{"tool":"not a node id","force":1.0}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void AttributesThatAreNotAnObjectAreRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("arcweld", """{"attributes":[1,2]}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void MissionStepsCarryIdentitySequencingAndErrorPolicy()
        {
            const string json = """
                [{"stepId":"s1","released":true,
                  "intent":{"kind":"wait","duration":1.0}},
                 {"stepId":"s2","sequenceId":7,"errorPolicy":"Skip","fallbackStepId":"s1",
                  "intent":{"kind":"wait","duration":2.0}}]
                """;

            ArrayOf<MissionStepDataType> steps = RoboticsIntentJson.BuildMissionSteps(json);

            Assert.That(steps.Count, Is.EqualTo(2));
            Assert.That(steps[0].StepId, Is.EqualTo("s1"));
            Assert.That(steps[0].SequenceId, Is.EqualTo(1u));
            Assert.That(steps[0].Released, Is.True);
            Assert.That(steps[1].SequenceId, Is.EqualTo(7u));
            Assert.That(steps[1].Released, Is.False);
            Assert.That(steps[1].ErrorPolicy, Is.EqualTo(ErrorPolicyEnum.Skip));
            Assert.That(steps[1].FallbackStepId, Is.EqualTo("s1"));
        }

        [Test]
        public void AbsentMissionStepsProduceAnEmptySet()
        {
            Assert.That(RoboticsIntentJson.BuildMissionSteps(null).Count, Is.Zero);
            Assert.That(RoboticsIntentJson.BuildMissionSteps("  ").Count, Is.Zero);
        }

        [Test]
        public void MissionStepsThatAreNotAnArrayAreRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionSteps("""{"stepId":"s1"}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void AMissionStepWithoutAnIntentIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionSteps("""[{"stepId":"s1"}]"""),
                Throws.ArgumentException);
        }

        [Test]
        public void AMissionStepWithoutAStepIdIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionSteps(
                    """[{"intent":{"kind":"wait","duration":1.0}}]"""),
                Throws.ArgumentException);
        }

        [Test]
        public void MissionTransitionsCarryEndpointsAndDivergenceKind()
        {
            const string json = """
                [{"fromStepId":"s1","toStepId":"s2"},
                 {"fromStepId":"s2","toStepId":"s3","divergenceKind":"Parallel"}]
                """;

            ArrayOf<MissionTransitionDataType> transitions = RoboticsIntentJson.BuildMissionTransitions(json);

            Assert.That(transitions.Count, Is.EqualTo(2));
            Assert.That(transitions[0].FromStepId, Is.EqualTo("s1"));
            Assert.That(transitions[0].DivergenceKind, Is.EqualTo(DivergenceKindEnum.Alternative));
            Assert.That(transitions[1].DivergenceKind, Is.EqualTo(DivergenceKindEnum.Parallel));
        }

        [Test]
        public void AbsentMissionTransitionsProduceAnEmptySet()
        {
            Assert.That(RoboticsIntentJson.BuildMissionTransitions(null).Count, Is.Zero);
            Assert.That(RoboticsIntentJson.BuildMissionTransitions("   ").Count, Is.Zero);
        }

        [Test]
        public void MissionTransitionsThatAreNotAnArrayAreRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionTransitions("""{"fromStepId":"s1"}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void AMissionTransitionMissingAnEndpointIsRejected()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildMissionTransitions("""[{"fromStepId":"s1"}]"""),
                Throws.ArgumentException);
        }

        [TestCase("""{"target":{"position":[],"orientation":[0.0,0.0,0.0,1.0]}}""")]
        [TestCase("""{"target":{"position":[0.1,0.2],"orientation":[0.0,0.0,0.0,1.0]}}""")]
        [TestCase("""{"target":{"position":[0.1,0.2,0.3],"orientation":[]}}""")]
        [TestCase("""{"target":{"position":[0.1,0.2,0.3],"orientation":[0.0,0.0,1.0]}}""")]
        public void APoseWithTooFewComponentsIsRejectedAsAnArgumentError(string json)
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("linearmove", json),
                Throws.ArgumentException);
        }

        [TestCase("""{"target":{"position":"0.1","orientation":[0.0,0.0,0.0,1.0]}}""")]
        [TestCase("""{"target":{"position":{"x":0.1},"orientation":[0.0,0.0,0.0,1.0]}}""")]
        [TestCase("""{"target":{"position":[0.1,0.2,0.3],"orientation":42}}""")]
        public void APoseComponentThatIsNotAnArrayIsRejectedAsAnArgumentError(string json)
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("linearmove", json),
                Throws.ArgumentException);
        }

        [Test]
        public void APoseComponentHoldingANonNumberIsRejectedAsAnArgumentError()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent(
                    "linearmove",
                    """{"target":{"position":["a","b","c"],"orientation":[0.0,0.0,0.0,1.0]}}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void ATrajectoryPositionsFieldThatIsNotAnArrayIsRejectedAsAnArgumentError()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent(
                    "trajectory",
                    """{"points":[{"timeFromStart":0.0,"positions":"nope"}]}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void ATrajectoryPointsFieldThatIsNotAnArrayIsRejectedAsAnArgumentError()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("trajectory", """{"points":{"timeFromStart":0.0}}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void AForceDirectionThatIsNotAnArrayIsRejectedAsAnArgumentError()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("force", """{"direction":1.0,"contactForce":5.0}"""),
                Throws.ArgumentException);
        }

        [Test]
        public void AJointMoveWithNonArrayJointTargetsIsRejectedAsAnArgumentError()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("jointmove", """{"jointTargets":"0.1"}""", 6),
                Throws.ArgumentException);
        }

        [Test]
        public void APoseThatIsNotAnObjectIsRejectedAsAnArgumentError()
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("linearmove", """{"target":5}"""),
                Throws.ArgumentException);
        }

        [TestCase("""{"weldSchedule":-1}""")]
        [TestCase("""{"weldSchedule":99999999999}""")]
        [TestCase("""{"weldSchedule":2.5}""")]
        public void AWholeNumberFieldOutsideUInt32IsRejectedAsAnArgumentError(string json)
        {
            Assert.That(
                () => RoboticsIntentJson.BuildIntent("spotweld", json),
                Throws.ArgumentException);
        }
    }
}
#endif
