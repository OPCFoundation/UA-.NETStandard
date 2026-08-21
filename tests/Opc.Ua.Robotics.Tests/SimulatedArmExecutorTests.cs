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
using NUnit.Framework;
using Opc.Ua.RobotIntent;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;
using Vision.BinPickingCell;

namespace Opc.Ua.Robotics.Tests
{
    [TestFixture]
    public sealed class SimulatedArmExecutorTests
    {
        [Test]
        public async Task CircularMoveIgnoresViaPointOrientation()
        {
            var clockA = new ManualSimulatedArmClock();
            var clockB = new ManualSimulatedArmClock();
            var executorA = new SimulatedArmExecutor(new SimulatedArmKinematics(), clockA);
            var executorB = new SimulatedArmExecutor(new SimulatedArmKinematics(), clockB);
            await MoveToNonSingularStartAsync(executorA, clockA, "circ-start-a").ConfigureAwait(false);
            await MoveToNonSingularStartAsync(executorB, clockB, "circ-start-b").ConfigureAwait(false);
            Pose3DDataType start = executorA.CurrentSnapshot.ToolPose;
            Pose3DDataType viaA = Offset(start, 0.02, 0.03, 0.02, [0.0, 0.0, 0.0, 1.0]);
            Pose3DDataType viaB = Offset(start, 0.02, 0.03, 0.02, [0.0, 0.0, 1.0, 0.0]);
            Pose3DDataType target = Offset(start, 0.04, 0.0, 0.0, start.Orientation.Span.ToArray());

            Task<IntentOutcome> a = ExecuteAsync(
                executorA,
                "circ-a",
                new CircularMoveIntentDataType { ViaPoint = viaA, Target = target },
                clockA);
            Task<IntentOutcome> b = ExecuteAsync(
                executorB,
                "circ-b",
                new CircularMoveIntentDataType { ViaPoint = viaB, Target = target },
                clockB);
            IntentOutcome outcomeA = await a.ConfigureAwait(false);
            IntentOutcome outcomeB = await b.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcomeA.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(outcomeB.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(
                    Distance(executorA.CurrentSnapshot.ToolPose, executorB.CurrentSnapshot.ToolPose),
                    Is.LessThan(1e-9));
            });
        }

        [Test]
        public async Task ForceIntentWithoutContactFailsObjectNotFound()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "force",
                new ForceIntentDataType
                {
                    Direction = ArrayOf.Create([1.0, 0.0, 0.0]),
                    ContactForce = 5.0,
                    MaxDistance = 0.01
                },
                clock).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(outcome.Failure, Is.EqualTo(IntentFailureEnum.ObjectNotFound));
            });
        }

        [Test]
        public async Task ForceIntentContactSucceeds()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            await MoveToNonSingularStartAsync(executor, clock, "force-contact-start").ConfigureAwait(false);
            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "force-contact",
                new ForceIntentDataType
                {
                    Direction = ArrayOf.Create([0.0, 0.0, 1.0]),
                    ContactForce = 5.0,
                    MaxDistance = 1.0
                },
                clock).ConfigureAwait(false);

            Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
        }

        [Test]
        public async Task ForceIntentFailsWhenInterpolatedPathCannotBeSolved()
        {
            var clock = new ManualSimulatedArmClock();
            var kinematics = new SimulatedArmKinematics();
            var executor = new SimulatedArmExecutor(kinematics, clock);
            await MoveToNonSingularStartAsync(executor, clock, "force-unreachable-start").ConfigureAwait(false);

            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "force-unreachable",
                new ForceIntentDataType
                {
                    Direction = ArrayOf.Create([1.0, 0.0, 0.0]),
                    ContactForce = 5.0,
                    MaxDistance = 2.0
                },
                clock).ConfigureAwait(false);
            Pose3DDataType forwardPose = kinematics.Forward(executor.CurrentSnapshot.JointAngles).ToolPose;

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(outcome.Failure, Is.EqualTo(IntentFailureEnum.JointLimit));
                Assert.That(Distance(executor.CurrentSnapshot.ToolPose, forwardPose), Is.LessThan(2e-5));
            });
        }

        [Test]
        public async Task GraspIgnoresForceAndSucceeds()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            const double ignoredGraspForce = 999.0;

            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "grasp",
                new GraspIntentDataType { Force = ignoredGraspForce, Width = 0.02 },
                clock).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(executor.CurrentSnapshot.GripperOpening, Is.EqualTo(0.02).Within(0.002));
            });
        }

        [Test]
        public async Task PickUsesSelectedPartPoseClosesBeforeHoldingAndRetracts()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new BinPickingPalletizerKinematics(), clock);
            var source = new NodeId("bin", 2);
            Pose3DDataType selectedPartPose = PalletizerPose(0.52, -0.08, -0.145);
            var snapshots = new List<SimulatedArmSnapshot>();
            executor.ResolvePickPose = (
                NodeId resolvedSource,
                string objectClass,
                out Pose3DDataType pose) =>
            {
                pose = selectedPartPose;
                return resolvedSource == source && objectClass == "RedCube";
            };
            executor.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "pick-selected-part",
                new PickIntentDataType
                {
                    Source = source,
                    ObjectClass = "RedCube"
                },
                clock).ConfigureAwait(false);

            SimulatedArmSnapshot firstHeld = snapshots.Find(snapshot => snapshot.HasObject)!;
            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(firstHeld, Is.Not.Null);
                Assert.That(firstHeld.HeldObjectClass, Is.EqualTo("RedCube"));
                Assert.That(firstHeld.GripperOpening, Is.EqualTo(0.02).Within(0.002));
                Assert.That(
                    Distance(firstHeld.ToolPose, selectedPartPose),
                    Is.LessThan(2e-5),
                    "The part must attach only after the jaws close at its detected pose.");
                Assert.That(
                    executor.CurrentSnapshot.ToolPose.Position[0],
                    Is.EqualTo(selectedPartPose.Position[0]).Within(2e-5));
                Assert.That(
                    executor.CurrentSnapshot.ToolPose.Position[2],
                    Is.GreaterThan(selectedPartPose.Position[2] + 0.035),
                    "A completed Pick must leave the tool retracted from the work.");
            });
        }

        [Test]
        public async Task PickAtCurrentWorkPositionSkipsCrossCellTraverse()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new BinPickingPalletizerKinematics(), clock);
            Pose3DDataType target = PalletizerPose(0.60, 0.0, -0.145);
            var diagnostics = new List<string>();
            executor.ResolvePickPose = (
                NodeId _,
                string _,
                out Pose3DDataType pose) =>
            {
                pose = target;
                return true;
            };
            executor.Diagnostic = diagnostics.Add;

            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "pick-current-work-position",
                new PickIntentDataType
                {
                    Source = new NodeId("fixture", 2),
                    ObjectClass = "RedCube"
                },
                clock).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(
                    diagnostics,
                    Does.Contain("target is at the current work position; skipping cross-cell traverse"));
                Assert.That(diagnostics, Does.Not.Contain("traversed the cell at the clear height"));
            });
        }

        [Test]
        public async Task FailedPickNotifiesHostThatTargetExclusionCanBeCleared()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new BinPickingPalletizerKinematics(), clock);
            string finishedClass = string.Empty;
            executor.ResolvePickPose = (
                NodeId _,
                string _,
                out Pose3DDataType pose) =>
            {
                pose = PalletizerPose(3.0, 0.0, 0.0);
                return true;
            };
            executor.PickAttemptFinished = objectClass => finishedClass = objectClass;

            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "pick-unreachable-target",
                new PickIntentDataType
                {
                    Source = new NodeId("bin", 2),
                    ObjectClass = "RedCube"
                },
                clock).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(finishedClass, Is.EqualTo("RedCube"));
            });
        }

        [Test]
        public async Task CanCancelIsFalseForGraspAndToolChangeAndTrueForMove()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            var graspExecution = new IntentExecution("grasp-cancel", new GraspIntentDataType(), new ProgressSink());
            Task<IntentOutcome> grasp = executor.ExecuteAsync(graspExecution, CancellationToken.None).AsTask();
            await clock.WaitForDelayAsync().ConfigureAwait(false);
            bool canCancelGrasp = executor.CanCancel(graspExecution);
            clock.CompleteAll();
            await DrainAsync(grasp, clock).ConfigureAwait(false);

            var toolExecution = new IntentExecution("tool-cancel", new ToolChangeIntentDataType(), new ProgressSink());
            Task<IntentOutcome> tool = executor.ExecuteAsync(toolExecution, CancellationToken.None).AsTask();
            await clock.WaitForDelayAsync().ConfigureAwait(false);
            bool canCancelTool = executor.CanCancel(toolExecution);
            clock.CompleteAll();
            await DrainAsync(tool, clock).ConfigureAwait(false);

            var moveExecution = new IntentExecution(
                "move-cancel",
                new JointMoveIntentDataType
                {
                    HasJointTargets = true,
                    JointTargets = ArrayOf.Create([0.1, -1.0, 1.2, -1.0, 0.8, 0.1])
                },
                new ProgressSink());

            Assert.Multiple(() =>
            {
                Assert.That(canCancelGrasp, Is.False);
                Assert.That(canCancelTool, Is.False);
                Assert.That(executor.CanCancel(moveExecution), Is.True);
            });
        }

        [Test]
        public async Task CancellationBringsJointMotionToAStopAndReturnsPromptly()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            var cts = new CancellationTokenSource();
            var execution = new IntentExecution(
                "cancel-motion",
                new JointMoveIntentDataType
                {
                    HasJointTargets = true,
                    JointTargets = ArrayOf.Create([0.8, -1.2, 1.1, -1.1, 0.8, 0.4]),
                    Constraints = new MotionConstraintsDataType { SpeedFraction = 0.1 }
                },
                new ProgressSink());
            Task<IntentOutcome> move = executor.ExecuteAsync(execution, cts.Token).AsTask();

            await clock.WaitForDelayAsync().ConfigureAwait(false);
            execution.AcceptCancellation(StopModeEnum.QuickStop);
            cts.Cancel();
            await DrainAsync(move, clock).ConfigureAwait(false);
            IntentOutcome outcome = await move.ConfigureAwait(false);

            Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
        }

        [Test]
        public async Task StopModesProduceObservableMotionDifferences()
        {
            CancelledMotion onPath = await ExecuteCancelledMoveAsync(StopModeEnum.OnPath).ConfigureAwait(false);
            CancelledMotion endOfCycle = await ExecuteCancelledMoveAsync(StopModeEnum.EndOfCycle).ConfigureAwait(false);
            CancelledMotion processStop = await ExecuteCancelledMoveAsync(StopModeEnum.ProcessStop).ConfigureAwait(false);
            CancelledMotion quickStop = await ExecuteCancelledMoveAsync(StopModeEnum.QuickStop).ConfigureAwait(false);
            CancelledMotion endOfInstruction = await ExecuteCancelledMoveAsync(StopModeEnum.EndOfInstruction)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(quickStop.Outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(onPath.Outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(processStop.Outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(endOfCycle.Outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(endOfInstruction.Outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(quickStop.DistanceAfterCancel, Is.LessThan(onPath.DistanceAfterCancel));
                Assert.That(quickStop.TicksAfterCancel, Is.LessThan(onPath.TicksAfterCancel));
                Assert.That(processStop.DistanceAfterCancel, Is.GreaterThan(quickStop.DistanceAfterCancel));
                Assert.That(processStop.DistanceAfterCancel, Is.LessThan(onPath.DistanceAfterCancel));
                Assert.That(endOfCycle.DistanceToTarget, Is.LessThan(1e-8));
                Assert.That(endOfInstruction.DistanceToTarget, Is.LessThan(1e-8));
            });
        }

        [Test]
        public async Task ProgressIsMonotonicAndReachesOne()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            var progress = new ProgressSink();
            var execution = new IntentExecution(
                "progress",
                new JointMoveIntentDataType
                {
                    HasJointTargets = true,
                    JointTargets = ArrayOf.Create([0.1, -1.0, 1.2, -1.0, 0.8, 0.1])
                },
                progress);
            Task<IntentOutcome> move = executor.ExecuteAsync(execution, CancellationToken.None).AsTask();
            await DrainAsync(move, clock).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(progress.Fractions, Is.Not.Empty);
                for (int i = 1; i < progress.Fractions.Count; i++)
                {
                    Assert.That(progress.Fractions[i], Is.GreaterThanOrEqualTo(progress.Fractions[i - 1] - 1e-12));
                }
                Assert.That(progress.Fractions[^1], Is.EqualTo(1.0).Within(1e-12));
            });
        }

        [Test]
        public async Task SnapshotJointAnglesAndToolPoseRemainConsistentDuringMove()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            var positionMismatches = new List<double>();
            var orientationMismatches = new List<double>();
            executor.SnapshotChanged += (_, snapshot) =>
            {
                Pose3DDataType expected = ManualForward(snapshot.JointAngles.Span);
                positionMismatches.Add(Distance(expected, snapshot.ToolPose));
                orientationMismatches.Add(QuaternionDistance(
                    expected.Orientation.Span,
                    snapshot.ToolPose.Orientation.Span));
            };

            Task<IntentOutcome> move = ExecuteAsync(
                executor,
                "consistency",
                new JointMoveIntentDataType
                {
                    HasJointTargets = true,
                    JointTargets = ArrayOf.Create([0.1, -1.0, 1.2, -1.0, 0.8, 0.1])
                },
                clock);
            await move.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(positionMismatches, Has.All.LessThan(1e-10));
                Assert.That(orientationMismatches, Has.All.LessThan(1e-10));
            });
        }

        private static async Task<IntentOutcome> ExecuteAsync(
            SimulatedArmExecutor executor,
            string id,
            IntentDataType intent,
            ManualSimulatedArmClock clock)
        {
            Task<IntentOutcome> task = executor.ExecuteAsync(
                new IntentExecution(id, intent, new ProgressSink()),
                CancellationToken.None).AsTask();
            await DrainAsync(task, clock).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        private static async Task MoveToNonSingularStartAsync(
            SimulatedArmExecutor executor,
            ManualSimulatedArmClock clock,
            string intentId)
        {
            IntentOutcome outcome = await ExecuteAsync(
                executor,
                intentId,
                new JointMoveIntentDataType
                {
                    HasJointTargets = true,
                    JointTargets = ArrayOf.Create([0.2, -1.25, 1.6, -1.1, 0.9, -0.35])
                },
                clock).ConfigureAwait(false);
            Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
        }

        private static async Task<CancelledMotion> ExecuteCancelledMoveAsync(StopModeEnum stopMode)
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            var cts = new CancellationTokenSource();
            var target = new JointMoveIntentDataType
            {
                HasJointTargets = true,
                JointTargets = ArrayOf.Create([0.8, -1.2, 1.1, -1.1, 0.8, 0.4])
            };
            var execution = new IntentExecution("cancel-" + stopMode, target, new ProgressSink());
            Task<IntentOutcome> move = executor.ExecuteAsync(execution, cts.Token).AsTask();
            for (int i = 0; i < 8; i++)
            {
                await clock.WaitForDelayAsync().ConfigureAwait(false);
                clock.CompleteNext();
            }
            Pose3DDataType poseAtCancel = executor.CurrentSnapshot.ToolPose;
            execution.AcceptCancellation(stopMode);
            cts.Cancel();
            int ticksAfterCancel = await DrainAsync(move, clock).ConfigureAwait(false);
            IntentOutcome outcome = await move.ConfigureAwait(false);
            double distanceAfterCancel = Distance(poseAtCancel, executor.CurrentSnapshot.ToolPose);

            Pose3DDataType finalTargetPose = new SimulatedArmKinematics().Forward(target.JointTargets).ToolPose;
            return new CancelledMotion(
                outcome,
                ticksAfterCancel,
                distanceAfterCancel,
                Distance(executor.CurrentSnapshot.ToolPose, finalTargetPose));
        }

        private static async Task<int> DrainAsync(Task task, ManualSimulatedArmClock clock)
        {
            // The executor runs on a manual clock precisely so these tests do not depend on
            // machine speed, so the drain is bounded by simulated steps rather than by elapsed
            // time. A wall-clock guard here reintroduced exactly the dependence the manual clock
            // removes: the same force probe takes ~0.6 s locally and over 5 s on a CI agent
            // running with coverage instrumentation, which made the bound a lottery. The elapsed
            // deadline is kept only as a generous backstop so a genuine hang still fails the test
            // rather than hanging the run.
            const int kMaxCompletions = 200_000;
            DateTime deadline = DateTime.UtcNow.AddMinutes(2);
            int completions = 0;
            while (!task.IsCompleted)
            {
                if (completions >= kMaxCompletions)
                {
                    Assert.Fail(
                        $"The simulated arm executor did not complete within {kMaxCompletions} simulated steps.");
                }
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("Timed out waiting for the simulated arm executor to complete.");
                }
                if (await clock.WaitForDelayAsync(task).ConfigureAwait(false))
                {
                    clock.CompleteNext();
                    completions++;
                }
            }
            return completions;
        }

        private static Pose3DDataType Offset(
            Pose3DDataType pose,
            double x,
            double y,
            double z,
            double[] orientation)
        {
            return new Pose3DDataType
            {
                FrameId = pose.FrameId,
                Position = ArrayOf.Create([
                    pose.Position[0] + x,
                    pose.Position[1] + y,
                    pose.Position[2] + z
                ]),
                Orientation = ArrayOf.Create(orientation.AsSpan())
            };
        }

        private static Pose3DDataType PalletizerPose(double x, double y, double z)
        {
            return new Pose3DDataType
            {
                FrameId = BinPickingPalletizerGeometry.RobotBaseFrameId,
                Position = new[] { x, y, z }.ToArrayOf(),
                Orientation = BinPickingPalletizerKinematics.ToolDownOrientation(
                    Math.Atan2(y, x),
                    Math.PI / 2.0)
            };
        }


        private static Pose3DDataType ManualForward(ReadOnlySpan<double> jointAngles)
        {
            double[,] transform = Identity();
            transform = Multiply(Multiply(transform, RotateZ(jointAngles[0])), Translate(0.0, 0.0, D1));
            transform = Multiply(transform, RotateY(jointAngles[1]));
            transform = Multiply(Multiply(transform, Translate(A2, 0.0, 0.0)), RotateY(jointAngles[2]));
            transform = Multiply(Multiply(transform, Translate(A3, 0.0, D4)), RotateY(jointAngles[3]));
            transform = Multiply(Multiply(transform, Translate(0.0, 0.0, D5)), RotateZ(jointAngles[4]));
            transform = Multiply(Multiply(transform, Translate(0.0, 0.0, D6)), RotateY(jointAngles[5]));
            transform = Multiply(transform, Translate(FlangeToTcp, 0.0, 0.0));

            return new Pose3DDataType
            {
                FrameId = "base",
                Position = ArrayOf.Create([transform[0, 3], transform[1, 3], transform[2, 3]]),
                Orientation = MatrixToQuaternion(transform)
            };
        }

        private static double[,] Identity()
        {
            return new double[,]
            {
                { 1.0, 0.0, 0.0, 0.0 },
                { 0.0, 1.0, 0.0, 0.0 },
                { 0.0, 0.0, 1.0, 0.0 },
                { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static double[,] Translate(double x, double y, double z)
        {
            return new double[,]
            {
                { 1.0, 0.0, 0.0, x },
                { 0.0, 1.0, 0.0, y },
                { 0.0, 0.0, 1.0, z },
                { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static double[,] RotateY(double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new double[,]
            {
                { c, 0.0, s, 0.0 },
                { 0.0, 1.0, 0.0, 0.0 },
                { -s, 0.0, c, 0.0 },
                { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static double[,] RotateZ(double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new double[,]
            {
                { c, -s, 0.0, 0.0 },
                { s, c, 0.0, 0.0 },
                { 0.0, 0.0, 1.0, 0.0 },
                { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static double[,] Multiply(double[,] left, double[,] right)
        {
            var result = new double[4, 4];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    for (int index = 0; index < 4; index++)
                    {
                        result[row, column] += left[row, index] * right[index, column];
                    }
                }
            }
            return result;
        }

        private static ArrayOf<double> MatrixToQuaternion(double[,] matrix)
        {
            double trace = matrix[0, 0] + matrix[1, 1] + matrix[2, 2];
            if (trace > 0.0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2.0;
                return PoseMath.Normalize([
                    (matrix[2, 1] - matrix[1, 2]) / s,
                    (matrix[0, 2] - matrix[2, 0]) / s,
                    (matrix[1, 0] - matrix[0, 1]) / s,
                    0.25 * s
                ]);
            }
            if (matrix[0, 0] > matrix[1, 1] && matrix[0, 0] > matrix[2, 2])
            {
                double s = Math.Sqrt(1.0 + matrix[0, 0] - matrix[1, 1] - matrix[2, 2]) * 2.0;
                return PoseMath.Normalize([
                    0.25 * s,
                    (matrix[0, 1] + matrix[1, 0]) / s,
                    (matrix[0, 2] + matrix[2, 0]) / s,
                    (matrix[2, 1] - matrix[1, 2]) / s
                ]);
            }
            if (matrix[1, 1] > matrix[2, 2])
            {
                double s = Math.Sqrt(1.0 + matrix[1, 1] - matrix[0, 0] - matrix[2, 2]) * 2.0;
                return PoseMath.Normalize([
                    (matrix[0, 1] + matrix[1, 0]) / s,
                    0.25 * s,
                    (matrix[1, 2] + matrix[2, 1]) / s,
                    (matrix[0, 2] - matrix[2, 0]) / s
                ]);
            }
            double sz = Math.Sqrt(1.0 + matrix[2, 2] - matrix[0, 0] - matrix[1, 1]) * 2.0;
            return PoseMath.Normalize([
                (matrix[0, 2] + matrix[2, 0]) / sz,
                (matrix[1, 2] + matrix[2, 1]) / sz,
                0.25 * sz,
                (matrix[1, 0] - matrix[0, 1]) / sz
            ]);
        }

        private static double Distance(Pose3DDataType a, Pose3DDataType b)
        {
            double dx = a.Position[0] - b.Position[0];
            double dy = a.Position[1] - b.Position[1];
            double dz = a.Position[2] - b.Position[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }


        private static double QuaternionDistance(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            double dot = Math.Abs(
                (left[0] * right[0]) + (left[1] * right[1]) + (left[2] * right[2]) + (left[3] * right[3]));
            return 1.0 - Math.Min(1.0, dot);
        }

        private readonly record struct CancelledMotion(
            IntentOutcome Outcome,
            int TicksAfterCancel,
            double DistanceAfterCancel,
            double DistanceToTarget);

        private sealed class ProgressSink : IIntentProgress
        {
            public List<double> Fractions { get; } = [];

            public void ReportProgress(double fraction)
            {
                Fractions.Add(fraction);
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


        private const double D1 = 0.1625;
        private const double A2 = -0.425;
        private const double A3 = -0.3922;
        private const double D4 = 0.1333;
        private const double D5 = 0.0997;
        private const double D6 = 0.0996;
        private const double FlangeToTcp = 0.165;

        private sealed class ManualSimulatedArmClock : ISimulatedArmClock, IDisposable
        {
            public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource();
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
                }
                lock (m_lock)
                {
                    m_pending.Enqueue(completion);
                }
                m_available.Release();
                return new ValueTask(completion.Task);
            }

            public async ValueTask WaitForDelayAsync()
            {
                Task wait = m_available.WaitAsync();
                Task completed = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (!ReferenceEquals(completed, wait))
                {
                    Assert.Fail("Timed out waiting for the simulated arm clock to receive a delay.");
                }
                await wait.ConfigureAwait(false);
            }

            public async ValueTask<bool> WaitForDelayAsync(Task task)
            {
                if (task.IsCompleted)
                {
                    return false;
                }
                Task wait = m_available.WaitAsync();
                Task completed = await Task.WhenAny(
                    wait,
                    task,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (ReferenceEquals(completed, task))
                {
                    return false;
                }
                if (!ReferenceEquals(completed, wait))
                {
                    Assert.Fail("Timed out waiting for the simulated arm clock to receive a delay.");
                }
                await wait.ConfigureAwait(false);
                return true;
            }

            public void CompleteNext()
            {
                while (true)
                {
                    TaskCompletionSource completion;
                    lock (m_lock)
                    {
                        if (m_pending.Count == 0)
                        {
                            Assert.Fail("No pending simulated arm delay was available to complete.");
                            return;
                        }
                        completion = m_pending.Dequeue();
                    }
                    if (completion.Task.IsCompleted)
                    {
                        continue;
                    }
                    completion.TrySetResult();
                    return;
                }
            }

            public void CompleteAll()
            {
                while (true)
                {
                    TaskCompletionSource? completion;
                    lock (m_lock)
                    {
                        completion = m_pending.Count == 0 ? null : m_pending.Dequeue();
                    }
                    if (completion is null)
                    {
                        return;
                    }
                    completion.TrySetResult();
                }
            }

            public void Dispose()
            {
                m_available.Dispose();
            }

            private readonly Queue<TaskCompletionSource> m_pending = new();
            private readonly SemaphoreSlim m_available = new(0);
            private readonly System.Threading.Lock m_lock = new();
        }
    }
}
