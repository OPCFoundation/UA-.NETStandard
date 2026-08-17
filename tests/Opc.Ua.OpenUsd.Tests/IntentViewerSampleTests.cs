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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntentViewerClient;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsdScene.Conversion;
using Opc.Ua.OpenUsdScene.Scene;
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class IntentViewerSampleTests
    {
        [Test]
        public void IntentViewerRejectsUndefinedNumericPickMode()
        {
            Assert.That(IntentViewerOptions.TryParsePickMode("7", out UsdViewPickMode pickMode), Is.False);
            Assert.That(pickMode, Is.EqualTo((UsdViewPickMode)7));
        }

        [Test]
        public void IntentViewerParsesMcpFlag()
        {
            Assert.Multiple(() =>
            {
                Assert.That(IntentViewerOptions.Parse([]).Mcp, Is.False);
                Assert.That(IntentViewerOptions.Parse(["--mcp"]).Mcp, Is.True);
            });
        }

        [Test]
        public void IntentViewerDefaultsToStdioMcpTransport()
        {
            IntentViewerMcpTransportSelection selection = IntentViewerOptions.Parse([]).SelectMcpTransport();

            Assert.Multiple(() =>
            {
                Assert.That(selection.Transport, Is.EqualTo(IntentViewerMcpTransport.Stdio));
                Assert.That(selection.Explicit, Is.False);
                Assert.That(selection.Message, Does.Contain("default MCP transport 'stdio'"));
            });
        }

        [Test]
        public void IntentViewerSelectsHttpMcpTransportWhenViewIsEnabled()
        {
            IntentViewerMcpTransportSelection selection = IntentViewerOptions.Parse(["--view"]).SelectMcpTransport();

            Assert.Multiple(() =>
            {
                Assert.That(selection.Transport, Is.EqualTo(IntentViewerMcpTransport.Http));
                Assert.That(selection.Explicit, Is.False);
                Assert.That(selection.Message, Does.Contain("because --view is enabled"));
                Assert.That(selection.Message, Does.Contain("stdout"));
            });
        }

        [Test]
        public void IntentViewerHonorsExplicitHttpMcpTransportWithView()
        {
            IntentViewerOptions options = IntentViewerOptions.Parse(["--view", "--transport", "http", "--port", "5201"]);
            IntentViewerMcpTransportSelection selection = options.SelectMcpTransport();

            Assert.Multiple(() =>
            {
                Assert.That(options.Port, Is.EqualTo(5201));
                Assert.That(selection.Transport, Is.EqualTo(IntentViewerMcpTransport.Http));
                Assert.That(selection.Explicit, Is.True);
                Assert.That(selection.Message, Does.Contain("explicitly requested"));
            });
        }

        [Test]
        public void IntentViewerHonorsExplicitStdioMcpTransportWithViewAndWarns()
        {
            IntentViewerMcpTransportSelection selection =
                IntentViewerOptions.Parse(["--view", "--transport", "stdio"]).SelectMcpTransport();

            Assert.Multiple(() =>
            {
                Assert.That(selection.Transport, Is.EqualTo(IntentViewerMcpTransport.Stdio));
                Assert.That(selection.Explicit, Is.True);
                Assert.That(selection.Message, Does.Contain("WARNING"));
                Assert.That(selection.Message, Does.Contain("protocol corruption"));
            });
        }

        [Test]
        public void IntentViewerAcceptsSseAsHttpMcpTransportAlias()
        {
            IntentViewerMcpTransportSelection selection =
                IntentViewerOptions.Parse(["--transport", "sse"]).SelectMcpTransport();

            Assert.Multiple(() =>
            {
                Assert.That(selection.Transport, Is.EqualTo(IntentViewerMcpTransport.Http));
                Assert.That(selection.Explicit, Is.True);
            });
        }

        [Test]
        public async Task StreamConnectorUntilCancelledStopsAfterCancellation()
        {
            int starts = 0;
            int stops = 0;
            using var cts = new CancellationTokenSource();

            Task stream = IntentViewerClient.Program.StreamConnectorUntilCancelledAsync(
                _ =>
                {
                    starts++;
                    cts.Cancel();
                    return Task.CompletedTask;
                },
                cancellationToken =>
                {
                    Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                    stops++;
                    return Task.CompletedTask;
                },
                cts.Token);

            await stream.ConfigureAwait(false);

            Assert.That(starts, Is.EqualTo(1));
            Assert.That(stops, Is.EqualTo(1));
        }

        [Test]
        public void SampleSessionUsesStablePkiRoot()
        {
            MethodInfo method = typeof(SampleSession).GetMethod(
                "GetPrivateStateRoot",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            string first = (string)method.Invoke(null, [])!;
            string second = (string)method.Invoke(null, [])!;

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void IntentEnabledRobotBenchAssetParsesWithPayloadPrims()
        {
            using Stream stream = typeof(IntentRobotCell).Assembly.GetManifestResourceStream("Bench.usda")!;
            using var reader = new StreamReader(stream);
            UsdStage stage = UsdaReader.Parse(reader.ReadToEnd(), "Bench");

            string[] requiredPrims =
            [
                "/World/Payloads/BinParts/Part01",
                "/World/Payloads/BinParts/Part08",
                "/World/Payloads/FixtureStack/Slot01",
                "/World/Payloads/FixtureStack/Slot08",
                "/World/Payloads/HeldPart"
            ];

            Assert.Multiple(() =>
            {
                foreach (string primPath in requiredPrims)
                {
                    Assert.That(stage.Find(primPath), Is.Not.Null, primPath);
                }

                UsdPrim heldPart = stage.Find("/World/Payloads/HeldPart")!;
                Assert.That(
                    heldPart.Attributes.Any(a => a.Name == "xformOp:transform"),
                    Is.True,
                    "The held payload must declare the transform op driven by live bindings.");
            });
        }

        [Test]
        public async Task CircularMoveKeepsPublishedToolPoseConsistentWithJoints()
        {
            var clock = new ImmediateSimulatedArmClock();
            var kinematics = new SimulatedArmKinematics();
            var executor = new SimulatedArmExecutor(kinematics, clock);

            // Start from a configuration well inside the joint limits. From the home pose the
            // interpolated arc grazes a limit, which resolves differently on Windows and Linux
            // and leaves the executor at the edge of IK convergence - so the test would be
            // pinning a platform-dependent accident rather than the invariant it is named for.
            // That an unsolvable path fails instead of publishing an inconsistent pose is
            // covered deterministically by
            // SimulatedArmExecutorTests.ForceIntentFailsWhenInterpolatedPathCannotBeSolved.
            IntentOutcome preposition = await executor.ExecuteAsync(
                new IntentExecution(
                    "circular-consistency-start",
                    new JointMoveIntentDataType
                    {
                        HasJointTargets = true,
                        JointTargets = ArrayOf.Create([0.2, -1.25, 1.6, -1.1, 0.9, -0.35])
                    },
                    new NullProgress()),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(preposition.State, Is.EqualTo(ExecutionStateEnum.Succeeded));

            Pose3DDataType start = executor.CurrentSnapshot.ToolPose;
            Pose3DDataType via = Offset(start, 0.02, 0.03, 0.02);
            Pose3DDataType target = Offset(start, 0.04, 0.0, 0.0);

            IntentOutcome outcome = await executor.ExecuteAsync(
                new IntentExecution(
                    "circular-consistency",
                    new CircularMoveIntentDataType { ViaPoint = via, Target = target },
                    new NullProgress()),
                CancellationToken.None).ConfigureAwait(false);

            Pose3DDataType forward = kinematics.Forward(executor.CurrentSnapshot.JointAngles).ToolPose;

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(Distance(forward, executor.CurrentSnapshot.ToolPose), Is.LessThan(1e-6));
            });
        }

        [Test]
        public async Task ForceMoveKeepsPublishedToolPoseConsistentWithJoints()
        {
            var clock = new ImmediateSimulatedArmClock();
            var kinematics = new SimulatedArmKinematics();
            var executor = new SimulatedArmExecutor(kinematics, clock);

            IntentOutcome outcome = await executor.ExecuteAsync(
                new IntentExecution(
                    "force-consistency",
                    new ForceIntentDataType
                    {
                        Direction = ArrayOf.Create([1.0, 0.0, 0.0]),
                        ContactForce = 2.0,
                        MaxDistance = 0.01
                    },
                    new NullProgress()),
                CancellationToken.None).ConfigureAwait(false);

            Pose3DDataType forward = kinematics.Forward(executor.CurrentSnapshot.JointAngles).ToolPose;

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(outcome.Failure, Is.EqualTo(IntentFailureEnum.ObjectNotFound));
                Assert.That(Distance(forward, executor.CurrentSnapshot.ToolPose), Is.LessThan(1e-6));
            });
        }

        [Test]
        public async Task UnadvertisedOutputAndProgramIntentsRemainUnsupportedByExecutor()
        {
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), new ImmediateSimulatedArmClock());

            IntentOutcome output = await executor.ExecuteAsync(
                new IntentExecution(
                    "set-output",
                    new SetOutputIntentDataType
                    {
                        Output = NodeId.Parse("s=GripperOpen"),
                        Value = new Variant(true)
                    },
                    new NullProgress()),
                CancellationToken.None).ConfigureAwait(false);
            IntentOutcome program = await executor.ExecuteAsync(
                new IntentExecution(
                    "call-program",
                    new CallProgramIntentDataType { Program = NodeId.Parse("s=Home") },
                    new NullProgress()),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(output.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(program.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(program.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        private static Pose3DDataType Offset(Pose3DDataType pose, double x, double y, double z)
        {
            return new Pose3DDataType
            {
                FrameId = pose.FrameId,
                Position = ArrayOf.Create([
                    pose.Position[0] + x,
                    pose.Position[1] + y,
                    pose.Position[2] + z
                ]),
                Orientation = ArrayOf.Create(pose.Orientation.Span)
            };
        }

        private static double Distance(Pose3DDataType left, Pose3DDataType right)
        {
            double dx = left.Position[0] - right.Position[0];
            double dy = left.Position[1] - right.Position[1];
            double dz = left.Position[2] - right.Position[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private sealed class ImmediateSimulatedArmClock : ISimulatedArmClock
        {
            public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class NullProgress : IIntentProgress
        {
            public void ReportProgress(double fraction)
            {
            }

            public void ReportPose(Pose3DDataType pose)
            {
            }

            public void ReportTrajectoryDeviation(
                double pathPositionDeviation,
                double goalPositionDeviation,
                double elapsedMilliseconds,
                bool final)
            {
            }
        }
    }
}
#endif
