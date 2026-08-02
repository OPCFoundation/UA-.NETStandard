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
using System.Linq;
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
        private SystemContext m_context = null!;
        private ScriptedExecutor m_executor = null!;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            m_executor = new ScriptedExecutor();
        }

        // ------------------------------------------------------------ clause 10.4

        [Test]
        public void AProtectiveStopRefusesSubmission()
        {
            using var host = NewHost();
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
            using var host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus { SafetyControllerOk = false });

            IntentAdmission admission = host.SubmitIntent(m_context, null, Move());

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
        }

        [Test]
        public void ASpeedAboveTheEnforcedSafeLimitIsRefused()
        {
            using var host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus
            {
                ActiveFunction = SafeMotionFunctionEnum.Sls,
                SafeSpeedLimitActive = true,
                SafeSpeedLimit = 0.25
            });

            var intent = Move();
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
            using var host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus
            {
                ActiveFunction = SafeMotionFunctionEnum.Sls,
                SafeSpeedLimitActive = true,
                SafeSpeedLimit = 0.25
            });

            var intent = Move();
            intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = 0.1 };

            Assert.That(host.SubmitIntent(m_context, null, intent).Accepted, Is.True);
        }

        [Test]
        public void AnInactiveSpeedLimitDoesNotRefuse()
        {
            using var host = NewHost();
            host.UpdateSafetyState(m_context, new SafetyStatus
            {
                SafeSpeedLimitActive = false,
                SafeSpeedLimit = 0.25
            });

            var intent = Move();
            intent.Constraints = new MotionConstraintsDataType { CartesianSpeed = 1.0 };

            Assert.That(host.SubmitIntent(m_context, null, intent).Accepted, Is.True,
                "a limit that is not being enforced constrains nothing");
        }

        // -------------------------------------------------------------- clause 6.8

        [Test]
        public void ATrajectoryOutOfTimeOrderIsRefused()
        {
            using var host = NewHost();
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
            using var host = NewHost();
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
            using var host = NewHost(options);

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
            using var host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new TrajectoryIntentDataType
                {
                    Points = new[] { Point(10), Point(20), Point(30) }
                });

            Assert.That(admission.Accepted, Is.True);
            await WaitAsync(() => m_executor.Started.Length == 1).ConfigureAwait(false);
        }

        [Test]
        public void AForceIntentWithAZeroDirectionIsRefused()
        {
            using var host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new ForceIntentDataType
                {
                    Direction = new[] { 0.0, 0.0, 0.0 },
                    ContactForce = 5,
                    MaxDistance = 0.1
                });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        [Test]
        public void AForceIntentWithoutADistanceIsRefused()
        {
            using var host = NewHost();
            IntentAdmission admission = host.SubmitIntent(m_context, null,
                new ForceIntentDataType
                {
                    Direction = new[] { 0.0, 0.0, -1.0 },
                    ContactForce = 5,
                    MaxDistance = 0
                });

            Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
        }

        // -------------------------------------------------------------- clause 6.9

        [Test]
        public void AChannelLeaseIsExclusiveAndReleasable()
        {
            using var host = NewHost(ChannelOptions());
            var first = new NodeId("s1", 1);
            var second = new NodeId("s2", 1);

            RealTimeLease granted = host.OpenRealTimeChannel(m_context, first, "rtde", 5000);
            Assert.Multiple(() =>
            {
                Assert.That(granted.Granted, Is.True);
                Assert.That(granted.EndpointUrl, Is.EqualTo("rtde://robot:30004"));
                Assert.That(granted.Expiry, Is.GreaterThan(DateTime.UtcNow));
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
            using var host = NewHost(ChannelOptions());

            Assert.That(host.OpenRealTimeChannel(m_context, null, "nope", 1000).Granted,
                Is.False);
        }

        [Test]
        public void AChannelIsRefusedOutsideItsRequiredMode()
        {
            IntentControllerHostOptions options = ChannelOptions();
            options.OperationalMode = OperationalModeEnum.Automatic;
            using var host = NewHost(options);

            RealTimeLease lease = host.OpenRealTimeChannel(m_context, null, "rtde", 1000);

            Assert.That(lease.Granted, Is.False,
                "the channel declares it needs AutomaticExternal");
        }

        [Test]
        public void MotionIsRefusedWhileAChannelLeaseIsHeldAndNothingArbitrates()
        {
            using var host = NewHost(ChannelOptions());
            host.OpenRealTimeChannel(m_context, null, "rtde", 5000);

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
            using var host = NewHost(options);
            host.OpenRealTimeChannel(m_context, null, "rtde", 5000);

            Assert.That(host.SubmitIntent(m_context, null, Move()).Accepted, Is.True);
        }

        // ---------------------------------------------------------------- clause 7.4

        [Test]
        public void ATransitionNamingAnUnknownStepIsRefused()
        {
            using var host = NewHost();
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
            using var host = NewHost();
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
            using var host = NewHost();
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
        public async Task ASkipPolicyContinuesPastAFailedStep()
        {
            using var host = NewHost();
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
            using var host = NewHost();
            m_executor.FailFirst = true;

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2) }
            });

            await Task.Delay(300).ConfigureAwait(false);
            Assert.That(m_executor.Started, Has.Length.EqualTo(1),
                "Abort is the default and must not begin a later step");
        }

        [Test]
        public async Task ARetryPolicyReattemptsUpToTheConfiguredBound()
        {
            IntentControllerHostOptions options = Options();
            options.MaxStepRetries = 2;
            using var host = NewHost(options);
            m_executor.AlwaysFail = true;

            MissionStepDataType step = Step("s1", 1);
            step.ErrorPolicy = ErrorPolicyEnum.Retry;

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { step }
            });

            await Task.Delay(400).ConfigureAwait(false);
            Assert.That(m_executor.Started, Has.Length.EqualTo(3),
                "one attempt plus two retries");
        }

        [Test]
        public async Task AnUnconditionalTransitionChoosesTheNextStep()
        {
            using var host = NewHost();
            MissionAdmission admission = host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2), Step("s3", 3) },
                // The graph skips s2 entirely; without it the mission would run all three.
                Transitions = new[] { Transition("s1", "s3") }
            });

            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(new[] { "s1", "s3" }));
        }

        [Test]
        public async Task TransitionsAreIgnoredWhenBranchingIsNotSupported()
        {
            IntentControllerHostOptions options = Options();
            options.MissionBranchingSupported = false;
            using var host = NewHost(options);

            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2), Step("s3", 3) },
                Transitions = new[] { Transition("s1", "s3") }
            });

            await WaitAsync(() => m_executor.Started.Length >= 3).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(new[] { "s1", "s2", "s3" }),
                "a host that declares no branching runs the steps in order");
        }

        [Test]
        public async Task AMissionWithoutTransitionsIsStillTheFlatSequence()
        {
            using var host = NewHost();
            host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "m1",
                Steps = new[] { Step("s1", 1), Step("s2", 2) }
            });

            await WaitAsync(() => m_executor.Started.Length >= 2).ConfigureAwait(false);
            Assert.That(m_executor.Started, Is.EqualTo(new[] { "s1", "s2" }));
        }

        // ------------------------------------------------------------------- helpers

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

        private IntentControllerHost NewHost(IntentControllerHostOptions? options = null)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                m_context,
                new NodeId(Guid.NewGuid().ToString(), 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            var host = new IntentControllerHost(
                controller, m_executor, (_, _) => default, options ?? Options());
            host.Start(m_context);
            return host;
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

        private sealed class ScriptedExecutor : IIntentExecutor
        {
            private int m_calls;

            public ConcurrentQueue<string> StartedQueue { get; } = new();
            public string[] Started => StartedQueue.ToArray();
            public bool FailFirst { get; set; }
            public bool AlwaysFail { get; set; }

            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution, CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref m_calls);
                StartedQueue.Enqueue(execution.Intent.IntentId ?? execution.IntentId);
                if (AlwaysFail || (FailFirst && call == 1))
                {
                    return new ValueTask<IntentOutcome>(
                        IntentOutcome.Fail(IntentFailureEnum.Other, "scripted failure"));
                }
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }
    }
}
