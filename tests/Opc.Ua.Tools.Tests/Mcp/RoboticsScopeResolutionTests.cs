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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Verifies that scoped selectors (frames, tools, locations, outputs,
    /// programs, axes) are resolved through the controller's published lookup
    /// tables before conversion, for direct intents and for mission steps, and
    /// that resolution has no authority or submission side effects.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsScopeResolutionTests
    {
        [Test]
        public async Task ResolutionContextReadsControllerInfoExactlyOnce()
        {
            var transport = new RecordingRobotIntentTransport { ControllerInfo = MakeInfo() };
            var controller = new RobotIntentControllerClient(transport);

            RoboticsResolutionContext context = await RoboticsResolutionContext
                .CreateAsync(controller, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.ReadControllerCallCount, Is.EqualTo(1));
                Assert.That(context.Scope.Lookups.Tools.Count, Is.EqualTo(2));
                Assert.That(ReferenceEquals(context.Client, controller), Is.True);
            });
        }

        [Test]
        public async Task ResolutionHasNoAuthorityOrSubmissionSideEffects()
        {
            var transport = new RecordingRobotIntentTransport { ControllerInfo = MakeInfo() };
            var controller = new RobotIntentControllerClient(transport);

            RoboticsResolutionContext context = await RoboticsResolutionContext
                .CreateAsync(controller, CancellationToken.None).ConfigureAwait(false);
            IntentDataType intent = RoboticsIntentDtoConverter.ConvertPick(
                new PickIntentInput { Source = "Bin1", Tool = "Gripper" },
                context.Scope);

            Assert.Multiple(() =>
            {
                Assert.That(intent, Is.Not.Null);
                Assert.That(transport.RequestControlCallCount, Is.Zero);
                Assert.That(transport.ReleaseControlCallCount, Is.Zero);
                Assert.That(transport.SubmitIntentCallCount, Is.Zero);
                Assert.That(transport.SubmitMissionCallCount, Is.Zero);
            });
        }

        [Test]
        public void ToolFrameResolvesThroughFrames()
        {
            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(
                new LinearMoveIntentInput { Target = MakePose(), ToolFrame = "Tcp" },
                MakeScope());

            Assert.That(intent.ToolFrame.ToString(), Is.EqualTo("ns=2;s=Frames/Tcp"));
        }

        [Test]
        public void ToolFrameAcceptsFullNodeId()
        {
            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(
                new LinearMoveIntentInput { Target = MakePose(), ToolFrame = "ns=7;s=Other/Frame" },
                MakeScope());

            Assert.That(intent.ToolFrame.ToString(), Is.EqualTo("ns=7;s=Other/Frame"));
        }

        [Test]
        public void UnknownToolFrameIsRejectedWithNamesAndNodeIds()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => RoboticsIntentDtoConverter.ConvertLinearMove(
                    new LinearMoveIntentInput { Target = MakePose(), ToolFrame = "NoSuchFrame" },
                    MakeScope()));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("No frame named 'NoSuchFrame'"));
                Assert.That(exception.Message, Does.Contain("Tcp"));
                Assert.That(exception.Message, Does.Contain("ns=2;s=Frames/Tcp"));
            });
        }

        [Test]
        public void PoseFrameSelectorStoresPublishedFrameId()
        {
            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(
                new LinearMoveIntentInput { Target = MakePose("Tcp") },
                MakeScope());

            Assert.That(intent.Target.FrameId, Is.EqualTo("frame-tcp"));
        }

        [Test]
        public void PoseFrameSelectorAcceptsPublishedFrameIdDirectly()
        {
            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(
                new LinearMoveIntentInput { Target = MakePose("frame-base") },
                MakeScope());

            Assert.That(intent.Target.FrameId, Is.EqualTo("frame-base"));
        }

        [Test]
        public void PoseFrameSelectorAcceptsFrameNodeId()
        {
            var intent = (LinearMoveIntentDataType)RoboticsIntentDtoConverter.ConvertLinearMove(
                new LinearMoveIntentInput { Target = MakePose("ns=2;s=Frames/Base") },
                MakeScope());

            Assert.That(intent.Target.FrameId, Is.EqualTo("frame-base"));
        }

        [Test]
        public void ForceFrameSelectorStoresPublishedFrameId()
        {
            var intent = (ForceIntentDataType)RoboticsIntentDtoConverter.ConvertForce(
                new ForceIntentInput
                {
                    Direction = [0, 0, -1],
                    ContactForce = 12,
                    FrameId = "Tcp"
                },
                MakeScope());

            Assert.That(intent.FrameId, Is.EqualTo("frame-tcp"));
        }

        [Test]
        public void CircularMoveResolvesBothPoseFrames()
        {
            var intent = (CircularMoveIntentDataType)RoboticsIntentDtoConverter.ConvertCircularMove(
                new CircularMoveIntentInput
                {
                    ViaPoint = MakePose("Tcp"),
                    Target = MakePose("Base")
                },
                MakeScope());

            Assert.Multiple(() =>
            {
                Assert.That(intent.ViaPoint.FrameId, Is.EqualTo("frame-tcp"));
                Assert.That(intent.Target.FrameId, Is.EqualTo("frame-base"));
            });
        }

        [Test]
        public void CartesianPathResolvesWaypointFrames()
        {
            var intent = (CartesianPathIntentDataType)RoboticsIntentDtoConverter.ConvertCartesianPath(
                new CartesianPathIntentInput
                {
                    Waypoints = [new CartesianWaypointDto { Pose = MakePose("Tcp") }]
                },
                MakeScope());

            Assert.That(intent.Waypoints[0].Pose.FrameId, Is.EqualTo("frame-tcp"));
        }

        [Test]
        public void JointMoveResolvesTargetPoseFrame()
        {
            var intent = (JointMoveIntentDataType)RoboticsIntentDtoConverter.ConvertJointMove(
                new JointMoveIntentInput { TargetPose = MakePose("Tcp") },
                0,
                MakeScope());

            Assert.That(intent.TargetPose.FrameId, Is.EqualTo("frame-tcp"));
        }

        [Test]
        public void GraspResolvesToolThroughTools()
        {
            var intent = (GraspIntentDataType)RoboticsIntentDtoConverter.ConvertGrasp(
                new GraspIntentInput { Tool = "Gripper", Force = 10 },
                MakeScope());

            Assert.That(intent.Tool.ToString(), Is.EqualTo("ns=2;s=Tools/Gripper"));
        }

        [Test]
        public void ReleaseResolvesToolThroughTools()
        {
            var intent = (ReleaseIntentDataType)RoboticsIntentDtoConverter.ConvertRelease(
                new ReleaseIntentInput { Tool = "Gripper" },
                MakeScope());

            Assert.That(intent.Tool.ToString(), Is.EqualTo("ns=2;s=Tools/Gripper"));
        }

        [Test]
        public void UnknownToolIsRejectedWithNamesAndNodeIds()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => RoboticsIntentDtoConverter.ConvertGrasp(
                    new GraspIntentInput { Tool = "Welder", Force = 10 },
                    MakeScope()));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("No tool named 'Welder'"));
                Assert.That(exception.Message, Does.Contain("Gripper"));
                Assert.That(exception.Message, Does.Contain("ns=2;s=Tools/Gripper"));
            });
        }

        [Test]
        public void PickResolvesSourceAndToolThroughLookups()
        {
            var intent = (PickIntentDataType)RoboticsIntentDtoConverter.ConvertPick(
                new PickIntentInput { Source = "Bin1", Tool = "Gripper" },
                MakeScope());

            Assert.Multiple(() =>
            {
                Assert.That(intent.Source.ToString(), Is.EqualTo("ns=2;s=Locations/Bin1"));
                Assert.That(intent.Tool.ToString(), Is.EqualTo("ns=2;s=Tools/Gripper"));
            });
        }

        [Test]
        public void PlaceResolvesDestinationThroughLocations()
        {
            var intent = (PlaceIntentDataType)RoboticsIntentDtoConverter.ConvertPlace(
                new PlaceIntentInput { Destination = "Slot1", Tool = "Gripper" },
                MakeScope());

            Assert.That(intent.Destination.ToString(), Is.EqualTo("ns=2;s=Locations/Slot1"));
        }

        [Test]
        public void ToolChangeResolvesToolAndDockStation()
        {
            var intent = (ToolChangeIntentDataType)RoboticsIntentDtoConverter.ConvertToolChange(
                new ToolChangeIntentInput { Tool = "Welder2", DockStation = "Dock1" },
                MakeScope());

            Assert.Multiple(() =>
            {
                Assert.That(intent.Tool.ToString(), Is.EqualTo("ns=2;s=Tools/Welder2"));
                Assert.That(intent.DockStation.ToString(), Is.EqualTo("ns=2;s=Locations/Dock1"));
            });
        }

        [Test]
        public void PalletiseResolvesPatternThroughLocations()
        {
            var intent = (PalletiseIntentDataType)RoboticsIntentDtoConverter.ConvertPalletise(
                new PalletiseIntentInput { Pattern = "PalletPattern" },
                MakeScope());

            Assert.That(intent.Pattern.ToString(), Is.EqualTo("ns=2;s=Locations/PalletPattern"));
        }

        [Test]
        public void SetOutputResolvesOutputThroughOutputs()
        {
            var intent = (SetOutputIntentDataType)RoboticsIntentDtoConverter.ConvertSetOutput(
                new SetOutputIntentInput
                {
                    Output = "Do1",
                    Value = new TypedValueDto
                    {
                        DataType = "Boolean",
                        Value = JsonDocument.Parse("true").RootElement
                    }
                },
                MakeScope());

            Assert.That(intent.Output.ToString(), Is.EqualTo("ns=2;s=Outputs/Do1"));
        }

        [Test]
        public void WaitResolvesSignalThroughOutputs()
        {
            var intent = (WaitIntentDataType)RoboticsIntentDtoConverter.ConvertWait(
                new WaitIntentInput { Duration = 1, Signal = "Do1" },
                MakeScope());

            Assert.That(intent.Signal.ToString(), Is.EqualTo("ns=2;s=Outputs/Do1"));
        }

        [Test]
        public void WaitAcceptsSignalNodeIdOutsideOutputs()
        {
            var intent = (WaitIntentDataType)RoboticsIntentDtoConverter.ConvertWait(
                new WaitIntentInput { Duration = 1, Signal = "ns=5;s=Plc/Di7" },
                MakeScope());

            Assert.That(intent.Signal.ToString(), Is.EqualTo("ns=5;s=Plc/Di7"));
        }

        [Test]
        public void ProcessProgramResolvesThroughPrograms()
        {
            var intent = (ArcWeldIntentDataType)RoboticsIntentDtoConverter.ConvertArcWeld(
                new ArcWeldIntentInput { ProcessProgram = "WeldProgram" },
                MakeScope());

            Assert.That(intent.ProcessProgram.ToString(), Is.EqualTo("ns=2;s=Programs/WeldProgram"));
        }

        [Test]
        public void CallProgramResolvesThroughPrograms()
        {
            var intent = (CallProgramIntentDataType)RoboticsIntentDtoConverter.ConvertCallProgram(
                new CallProgramIntentInput { Program = "WeldProgram" },
                MakeScope());

            Assert.That(intent.Program.ToString(), Is.EqualTo("ns=2;s=Programs/WeldProgram"));
        }

        [Test]
        public void FastenJointDoesNotResolveThroughRobotAxes()
        {
            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertFasten(
                    new FastenIntentInput { Joint = "Axis1" },
                    MakeScope()),
                Throws.ArgumentException.With.Message.Contains("NodeId"));
        }

        [Test]
        public void FastenJointIsAlwaysNodeIdOnly()
        {
            var intent = (FastenIntentDataType)RoboticsIntentDtoConverter.ConvertFasten(
                new FastenIntentInput { Joint = "ns=3;s=Fastener/J1" },
                MakeScope());

            Assert.That(intent.Joint.ToString(), Is.EqualTo("ns=3;s=Fastener/J1"));
        }

        [Test]
        public void FastenJointNameWithoutJoiningLookupIsRejected()
        {
            var scope = new RoboticsScopeResolver(new RobotIntentLookups());

            Assert.That(
                () => RoboticsIntentDtoConverter.ConvertFasten(
                    new FastenIntentInput { Joint = "Axis1" }, scope),
                Throws.ArgumentException.With.Message.Contains("NodeId"));
        }

        [Test]
        public void BrowseNameIsAcceptedAsSelectorCandidate()
        {
            ArrayOf<RobotIntentNodeLookupEntry> tools =
            [
                new RobotIntentNodeLookupEntry(
                    new NodeId("Tools/Gripper", 2), new QualifiedName("GripperBrowse", 2), "Gripper")
            ];
            var scope = new RoboticsScopeResolver(new RobotIntentLookups { Tools = tools });

            var intent = (GraspIntentDataType)RoboticsIntentDtoConverter.ConvertGrasp(
                new GraspIntentInput { Tool = "GripperBrowse", Force = 5 },
                scope);

            Assert.That(intent.Tool.ToString(), Is.EqualTo("ns=2;s=Tools/Gripper"));
        }

        [Test]
        public void SelectorsAreTrimmedAndCaseSensitive()
        {
            RoboticsScopeResolver scope = MakeScope();

            Assert.Multiple(() =>
            {
                Assert.That(scope.ResolveTool("  Gripper  ").ToString(), Is.EqualTo("ns=2;s=Tools/Gripper"));
                Assert.That(
                    () => scope.ResolveTool("gripper"),
                    Throws.ArgumentException.With.Message.Contains("No tool named"));
            });
        }

        [Test]
        public void MissionStepsResolveSelectorsRecursively()
        {
            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    StepId = "s1",
                    Released = true,
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Pick,
                        Source = "Bin1",
                        Tool = "Gripper"
                    }
                },
                new MissionStepInput
                {
                    StepId = "s2",
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.LinearMove,
                        Target = MakePose("Tcp"),
                        ToolFrame = "Tcp"
                    }
                },
                new MissionStepInput
                {
                    StepId = "s3",
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.CallProgram,
                        Program = "WeldProgram"
                    }
                }
            ];

            ArrayOf<MissionStepDataType> converted =
                RoboticsIntentDtoConverter.ConvertMissionSteps(steps, MakeScope());

            var pick = (PickIntentDataType)converted[0].Intent;
            var move = (LinearMoveIntentDataType)converted[1].Intent;
            var call = (CallProgramIntentDataType)converted[2].Intent;

            Assert.Multiple(() =>
            {
                Assert.That(pick.Source.ToString(), Is.EqualTo("ns=2;s=Locations/Bin1"));
                Assert.That(pick.Tool.ToString(), Is.EqualTo("ns=2;s=Tools/Gripper"));
                Assert.That(move.ToolFrame.ToString(), Is.EqualTo("ns=2;s=Frames/Tcp"));
                Assert.That(move.Target.FrameId, Is.EqualTo("frame-tcp"));
                Assert.That(call.Program.ToString(), Is.EqualTo("ns=2;s=Programs/WeldProgram"));
            });
        }

        [Test]
        public void MissionBuildResolvesSelectorsThroughScope()
        {
            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    StepId = "s1",
                    Released = true,
                    Intent = new MissionIntentInput
                    {
                        Kind = IntentKind.Place,
                        Destination = "Slot1",
                        Tool = "Gripper"
                    }
                }
            ];

            MissionDataType mission = RoboticsMissionTools.BuildMission(
                "m1", 1, steps, default, "Palletise", MakeScope());
            var place = (PlaceIntentDataType)mission.Steps[0].Intent;

            Assert.That(place.Destination.ToString(), Is.EqualTo("ns=2;s=Locations/Slot1"));
        }

        [Test]
        public void AmbiguousSelectorRequiresNodeId()
        {
            ArrayOf<RobotIntentNodeLookupEntry> locations =
            [
                new RobotIntentNodeLookupEntry(
                    new NodeId("Locations/A1", 2), new QualifiedName("Slot", 2), "Slot"),
                new RobotIntentNodeLookupEntry(
                    new NodeId("Locations/B1", 2), new QualifiedName("Slot", 2), "Slot")
            ];
            var scope = new RoboticsScopeResolver(new RobotIntentLookups { Locations = locations });

            var exception = Assert.Throws<ArgumentException>(() => scope.ResolveLocation("Slot"));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("Ambiguous location name"));
                Assert.That(exception.Message, Does.Contain("ns=2;s=Locations/A1"));
                Assert.That(exception.Message, Does.Contain("ns=2;s=Locations/B1"));
            });
        }

        [Test]
        public void ConverterExposesNoScopelessOverloads()
        {
            MethodInfo[] converters = typeof(RoboticsIntentDtoConverter)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name.StartsWith("Convert", StringComparison.Ordinal) &&
                    m.Name != "ConvertMissionTransitions")
                .ToArray();

            Assert.That(converters, Is.Not.Empty);
            Assert.That(
                converters.All(m => m.GetParameters()
                    .Any(p => p.ParameterType == typeof(RoboticsScopeResolver))),
                Is.True,
                "Every intent converter must take the per-call scope resolver explicitly.");
        }

        private static RoboticsScopeResolver MakeScope()
        {
            return new RoboticsScopeResolver(MakeLookups());
        }

        private static RobotIntentControllerInfo MakeInfo()
        {
            return new RobotIntentControllerInfo
            {
                NodeId = new NodeId("Controllers/Controller1", 2),
                BrowseName = new QualifiedName("Controller1", 2),
                AxisCount = 6,
                Lookups = MakeLookups()
            };
        }

        private static RobotIntentLookups MakeLookups()
        {
            return new RobotIntentLookups
            {
                Frames =
                [
                    Entry("Frames/Tcp", "Tcp"),
                    Entry("Frames/Base", "Base")
                ],
                FramesByFrameId =
                [
                    new RobotIntentNodeLookupEntry(
                        new NodeId("Frames/Tcp", 2), new QualifiedName("Tcp", 2), "frame-tcp"),
                    new RobotIntentNodeLookupEntry(
                        new NodeId("Frames/Base", 2), new QualifiedName("Base", 2), "frame-base")
                ],
                Tools =
                [
                    Entry("Tools/Gripper", "Gripper"),
                    Entry("Tools/Welder2", "Welder2")
                ],
                Locations =
                [
                    Entry("Locations/Bin1", "Bin1"),
                    Entry("Locations/Slot1", "Slot1"),
                    Entry("Locations/Dock1", "Dock1"),
                    Entry("Locations/PalletPattern", "PalletPattern")
                ],
                Axes =
                [
                    Entry("Axes/Axis1", "Axis1"),
                    Entry("Axes/Axis2", "Axis2")
                ],
                Outputs =
                [
                    Entry("Outputs/Do1", "Do1")
                ],
                Programs =
                [
                    Entry("Programs/WeldProgram", "WeldProgram")
                ]
            };
        }

        private static RobotIntentNodeLookupEntry Entry(string identifier, string name)
        {
            return new RobotIntentNodeLookupEntry(
                new NodeId(identifier, 2), new QualifiedName(name, 2), name);
        }

        private static PoseDto MakePose(string? frameId = null)
        {
            return new PoseDto
            {
                Position = new PosePositionDto { X = 0.4, Y = 0.1, Z = 0.25 },
                Orientation = new QuaternionDto { X = 0, Y = 0, Z = 0, W = 1 },
                FrameId = frameId
            };
        }
    }
}
#endif
