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
using Robotics.MinimalIntentRobotServer.Kinematics;
using Robotics.MinimalIntentRobotServer.Simulation;

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
        public async Task GraspIgnoresForceAndSucceeds()
        {
            var clock = new ManualSimulatedArmClock();
            var executor = new SimulatedArmExecutor(new SimulatedArmKinematics(), clock);
            IntentOutcome outcome = await ExecuteAsync(
                executor,
                "grasp",
                new GraspIntentDataType { Force = 999.0, Width = 0.02 },
                clock).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(executor.CurrentSnapshot.GripperOpening, Is.EqualTo(0.02).Within(0.002));
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
            var kinematics = new SimulatedArmKinematics();
            var executor = new SimulatedArmExecutor(kinematics, clock);
            var mismatches = new List<double>();
            executor.SnapshotChanged += (_, snapshot) =>
            {
                Pose3DDataType expected = kinematics.Forward(snapshot.JointAngles).ToolPose;
                mismatches.Add(Distance(expected, snapshot.ToolPose));
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

            Assert.That(mismatches, Has.All.LessThan(1e-10));
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
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            int completions = 0;
            while (!task.IsCompleted)
            {
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

        private static double Distance(Pose3DDataType a, Pose3DDataType b)
        {
            double dx = a.Position[0] - b.Position[0];
            double dy = a.Position[1] - b.Position[1];
            double dz = a.Position[2] - b.Position[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
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
