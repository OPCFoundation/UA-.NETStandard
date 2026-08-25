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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Tests;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Exercises mission browseability, retention, step IntentId correlation,
    /// duplicate refusal, and authoritative failure/message.
    /// </summary>
    [TestFixture]
    public class IntentMissionHostTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            m_messageContext = ServiceMessageContext.Create(telemetry);
            m_messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                EncodeableFactory = m_messageContext.Factory
            };

            m_controller = new IntentControllerState(null);
            m_controller.Create(
                m_context,
                new NodeId("Controller", 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);

            m_executor = new ScriptedExecutor();
            m_added.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            m_host?.Dispose();
        }

        [Test]
        public async Task MissionIsVisibleWhileExecuting()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, SimpleMission("m-visible"));

            Assert.That(admission.Accepted, Is.True, "submission should be accepted");
            Assert.That(admission.MissionId, Is.EqualTo("m-visible"));
            Assert.That(admission.Operation.IsNull, Is.False, "must return a node");

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null, "mission node should be browsable");

            m_executor.Gate.Release();
            await WaitForCompletion(admission.Operation);

            MissionObjectState? terminal = FindMissionNode(admission.Operation);
            Assert.That(terminal, Is.Not.Null, "terminal mission should be retained");
        }

        [Test]
        public async Task TerminalMissionsArePrunedAtRetentionBound()
        {
            IntentControllerHostOptions options = Options();
            options.RetainedTerminalMissions = 2;
            m_host = NewHost(options);

            var admissions = new List<MissionAdmission>();
            for (int i = 0; i < 4; i++)
            {
                MissionAdmission a = m_host.SubmitMission(m_context, null, SimpleMission($"m-{i}"));
                Assert.That(a.Accepted, Is.True);
                admissions.Add(a);
                await WaitForCompletion(a.Operation);
            }

            MissionObjectState? oldest = FindMissionNode(admissions[0].Operation);
            MissionObjectState? secondOldest = FindMissionNode(admissions[1].Operation);
            MissionObjectState? newest = FindMissionNode(admissions[3].Operation);

            Assert.Multiple(() =>
            {
                Assert.That(oldest, Is.Null, "oldest terminal mission should be pruned");
                Assert.That(secondOldest, Is.Null, "second-oldest terminal mission should be pruned");
                Assert.That(newest, Is.Not.Null, "newest terminal mission should be retained");
            });
        }

        [Test]
        public void DuplicateActiveMissionIdIsRefused()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host = NewHost(Options());

            MissionAdmission first = m_host.SubmitMission(m_context, null, SimpleMission("dup"));
            Assert.That(first.Accepted, Is.True);

            MissionAdmission second = m_host.SubmitMission(m_context, null, SimpleMission("dup"));

            Assert.Multiple(() =>
            {
                Assert.That(second.Accepted, Is.False, "duplicate active mission should be refused");
                Assert.That(second.Message, Does.Contain("dup"));
            });

            m_executor.Gate!.Release();
        }

        [Test]
        public async Task MissionStepIntentIdCorrelatesMissionAndStep()
        {
            m_host = NewHost(Options());
            var admitted = new List<string>();
            m_executor.OnExecute = exec => admitted.Add(exec.IntentId);

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "corr-mission",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "step-1",
                        SequenceId = 1,
                        Released = true,
                        Intent = Move()
                    },
                    new MissionStepDataType
                    {
                        StepId = "step-2",
                        SequenceId = 2,
                        Released = true,
                        Intent = Move()
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            Assert.Multiple(() =>
            {
                Assert.That(admitted, Has.Count.EqualTo(2));
                Assert.That(admitted[0], Is.EqualTo("corr-mission/step-1"));
                Assert.That(admitted[1], Is.EqualTo("corr-mission/step-2"));
            });
        }

        [Test]
        public async Task MissionFailurePublishesAuthoritativeFailureAndMessage()
        {
            m_executor.Outcome = IntentOutcome.Fail(
                IntentFailureEnum.Other, "arm collision detected");
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(
                m_context, null, SimpleMission("fail-mission"));

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);

            IntentFailureEnum failure = ReadFailureEnum(node!);
            string message = ReadFailureMessage(node!);

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.EqualTo(IntentFailureEnum.Other));
                Assert.That(message, Is.EqualTo("arm collision detected"));
            });
        }

        [Test]
        public async Task ExecutorExceptionPublishesExactFailureAndMessage()
        {
            m_executor.Exception = new InvalidOperationException("executor fault");
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(
                m_context,
                null,
                SimpleMission("executor-exception"));

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);

            Assert.Multiple(() =>
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(ReadFailureEnum(node!), Is.EqualTo(IntentFailureEnum.Other));
                Assert.That(ReadFailureMessage(node!), Is.EqualTo("executor fault"));
            });
        }

        [Test]
        public async Task StepAdmissionRefusalPublishesExactFailureAndMessage()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "step-refusal",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "first",
                        SequenceId = 1,
                        Released = true,
                        Intent = MoveWithId("first-step")
                    },
                    new MissionStepDataType
                    {
                        StepId = "second",
                        SequenceId = 2,
                        Released = true,
                        Intent = MoveWithId("late-collision")
                    }
                ]
            });
            Assert.That(admission.Accepted, Is.True);

            var unrelated = MoveWithId("late-collision");
            unrelated.BufferMode = BufferModeEnum.Buffered;
            IntentAdmission collision = m_host.SubmitIntent(m_context, null, unrelated);
            Assert.That(collision.Accepted, Is.True);

            m_executor.Gate.Release();
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);

            Assert.Multiple(() =>
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(ReadFailureEnum(node!), Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(
                    ReadFailureMessage(node!),
                    Is.EqualTo("IntentId 'late-collision' is already retained by another operation."));
            });
        }

        [Test]
        public async Task TerminalMissionRetentionParallelsOperationRetention()
        {
            IntentControllerHostOptions options = Options();
            options.RetainedTerminalOperations = 4;
            options.RetainedTerminalMissions = 4;
            m_host = NewHost(options);

            for (int i = 0; i < 6; i++)
            {
                MissionAdmission a = m_host.SubmitMission(m_context, null, SimpleMission($"par-{i}"));
                Assert.That(a.Accepted, Is.True);
                await WaitForCompletion(a.Operation);
            }

            int missionCount = m_added.OfType<MissionObjectState>().Count();
            Assert.That(missionCount, Is.GreaterThanOrEqualTo(4),
                "at least RetainedTerminalMissions missions should survive");
        }

        [Test]
        public async Task ExplicitIntentIdIsPreservedThroughExecution()
        {
            m_host = NewHost(Options());
            var admitted = new List<string>();
            m_executor.OnExecute = exec => admitted.Add(exec.IntentId);

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "preserve-id",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "step-a",
                        SequenceId = 1,
                        Released = true,
                        Intent = MoveWithId("my-explicit-id")
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            Assert.That(admitted, Has.Count.EqualTo(1));
            Assert.That(admitted[0], Is.EqualTo("my-explicit-id"));
        }

        [Test]
        public async Task GeneratedIntentIdFollowsMissionSlashStepPattern()
        {
            m_host = NewHost(Options());
            var admitted = new List<string>();
            m_executor.OnExecute = exec => admitted.Add(exec.IntentId);

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "gen-mission",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "step-x",
                        SequenceId = 1,
                        Released = true,
                        Intent = Move()
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            Assert.That(admitted, Has.Count.EqualTo(1));
            Assert.That(admitted[0], Is.EqualTo("gen-mission/step-x"));
        }

        [Test]
        public void DuplicateExplicitIntentIdWithinMissionIsRefused()
        {
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "dup-id-mission",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "s1",
                        SequenceId = 1,
                        Released = true,
                        Intent = MoveWithId("same-id")
                    },
                    new MissionStepDataType
                    {
                        StepId = "s2",
                        SequenceId = 2,
                        Released = true,
                        Intent = MoveWithId("same-id")
                    }
                ]
            });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Message, Does.Contain("same-id"));
            });
        }

        [Test]
        public void ExplicitAndGeneratedIntentIdsArePreflightedBeforeAnyStepExecutes()
        {
            m_host = NewHost(Options());
            int executions = 0;
            m_executor.OnExecute = _ => Interlocked.Increment(ref executions);

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "two-pass",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "first",
                        SequenceId = 1,
                        Released = true,
                        Intent = MoveWithId("two-pass/second")
                    },
                    new MissionStepDataType
                    {
                        StepId = "second",
                        SequenceId = 2,
                        Released = true,
                        Intent = Move()
                    }
                ]
            });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Message, Does.Contain("two-pass/second"));
                Assert.That(executions, Is.Zero);
            });
        }

        [Test]
        public async Task ExplicitIntentIdCollisionWithTerminalRetainedOperationIsRefused()
        {
            m_host = NewHost(Options());

            IntentAdmission standalone = m_host.SubmitIntent(
                m_context,
                null,
                MoveWithId("retained-terminal"));
            Assert.That(standalone.Accepted, Is.True);
            await WaitForIntentCompletion(standalone.Operation);

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "collide-terminal",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "s1",
                        SequenceId = 1,
                        Released = true,
                        Intent = MoveWithId("retained-terminal")
                    }
                ]
            });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(admission.Message, Does.Contain("retained-terminal"));
            });
        }

        [Test]
        public async Task RetryUsesStableStepIdentityAndMapsItsLatestOperation()
        {
            m_host = NewHost(Options());
            var executed = new List<string>();
            int call = 0;
            m_executor.OnExecute = execution => executed.Add(execution.IntentId);
            m_executor.OutcomeFunc = _ => Interlocked.Increment(ref call) == 1
                ? IntentOutcome.Fail(IntentFailureEnum.Other, "retry once")
                : IntentOutcome.Success;

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "retry-stable",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "step",
                        SequenceId = 1,
                        Released = true,
                        ErrorPolicy = ErrorPolicyEnum.Retry,
                        Intent = MoveWithId("stable-intent")
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            MissionDataType mission = node!.Mission?.Value is MissionDataType value
                ? value
                : new MissionDataType();
            MissionStepDataType step = mission.Steps[0];

            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.EqualTo(["stable-intent", "stable-intent#attempt-2"]));
                Assert.That(step.Intent!.IntentId, Is.EqualTo("stable-intent#attempt-2"));
                Assert.That(step.Operation.IsNull, Is.False);
                Assert.That(step.Status, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [TestCaseSource(nameof(RevisitedErrorPolicies))]
        public async Task RevisitedFallbackOrCompensationStepUsesNextStableAttemptIdentity(
            ErrorPolicyEnum policy,
            ExecutionStateEnum expectedState)
        {
            m_host = NewHost(Options());
            var executed = new List<string>();
            m_executor.OnExecute = execution => executed.Add(execution.IntentId);
            m_executor.OutcomeFunc = execution => execution.IntentId switch
            {
                "root" => IntentOutcome.Fail(IntentFailureEnum.Other, "root fault"),
                "recovery" => IntentOutcome.Fail(IntentFailureEnum.Other, "revisit recovery"),
                _ => IntentOutcome.Success
            };

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "revisit",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "root",
                        SequenceId = 1,
                        Released = true,
                        ErrorPolicy = policy,
                        FallbackStepId = "recovery",
                        Intent = MoveWithId("root")
                    },
                    new MissionStepDataType
                    {
                        StepId = "recovery",
                        SequenceId = 2,
                        Released = true,
                        ErrorPolicy = policy,
                        FallbackStepId = "recovery",
                        Intent = MoveWithId("recovery")
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            MissionDataType mission = node!.Mission?.Value is MissionDataType missionData
                ? missionData
                : new MissionDataType();
            MissionStepDataType recovery = mission.Steps[1];
            ExecutionStateEnum missionState = node.ExecutionState?.Value is ExecutionStateEnum stateValue
                ? stateValue
                : ExecutionStateEnum.Accepted;

            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.EqualTo(["root", "recovery", "recovery#attempt-2"]));
                Assert.That(recovery.Intent!.IntentId, Is.EqualTo("recovery#attempt-2"));
                Assert.That(recovery.Operation.IsNull, Is.False);
                Assert.That(recovery.Status, Is.EqualTo(expectedState));
                Assert.That(
                    missionState,
                    Is.EqualTo(policy == ErrorPolicyEnum.Compensate
                        ? ExecutionStateEnum.Failed
                        : ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task FailedMissionPublishesExactStepFailureEnum()
        {
            m_executor.Outcome = IntentOutcome.Fail(
                IntentFailureEnum.SafetyLimitExceeded,
                "joint limit breached");
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, SimpleMission("exact-fail"));
            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);

            IntentFailureEnum failure = ReadFailureEnum(node!);
            string message = ReadFailureMessage(node);

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
                Assert.That(message, Does.Contain("joint limit breached"));
            });
        }

        [Test]
        public async Task FailedMissionNeverHasFailureNone()
        {
            m_executor.Outcome = IntentOutcome.Fail(IntentFailureEnum.None, string.Empty);
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, SimpleMission("no-none"));
            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);
            ExecutionStateEnum state = node!.ExecutionState?.Value is ExecutionStateEnum s
                ? s : ExecutionStateEnum.Accepted;

            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(ExecutionStateEnum.Failed));
                IntentFailureEnum failure = ReadFailureEnum(node);
                Assert.That(failure, Is.Not.EqualTo(IntentFailureEnum.None),
                    "A failed mission must never leave Failure=None");
            });
        }

        [Test]
        public async Task SucceededMissionHasFailureNone()
        {
            m_executor.Outcome = IntentOutcome.Success;
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, SimpleMission("success-m"));
            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);
            ExecutionStateEnum state = node!.ExecutionState?.Value is ExecutionStateEnum s
                ? s : ExecutionStateEnum.Accepted;

            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task CancelledMissionReportsCorrectState()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, SimpleMission("cancel-m"));
            Assert.That(admission.Accepted, Is.True);

            bool cancelled = m_host.CancelMission(m_context, null, "cancel-m", StopModeEnum.QuickStop);
            Assert.That(cancelled, Is.True);

            m_executor.Gate.Release();
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);
            ExecutionStateEnum state = node!.ExecutionState?.Value is ExecutionStateEnum s
                ? s : ExecutionStateEnum.Accepted;
            MissionDataType mission = node.Mission?.Value is MissionDataType value
                ? value
                : new MissionDataType();

            Assert.Multiple(() =>
            {
                Assert.That(state, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(mission.Steps[0].Status, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(node.CurrentStepId!.Value, Is.Empty);
            });
        }

        [Test]
        public async Task ErrorPolicyAbortFailsMission()
        {
            IntentControllerHostOptions options = Options();
            options.MaxStepRetries = 3;
            m_host = NewHost(options);
            m_executor.Outcome = IntentOutcome.Fail(IntentFailureEnum.Other, "step fault");

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "abort-policy",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "s1",
                        SequenceId = 1,
                        Released = true,
                        ErrorPolicy = ErrorPolicyEnum.Abort,
                        Intent = Move()
                    },
                    new MissionStepDataType
                    {
                        StepId = "s2",
                        SequenceId = 2,
                        Released = true,
                        Intent = Move()
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);
            ExecutionStateEnum state = node!.ExecutionState?.Value is ExecutionStateEnum s
                ? s : ExecutionStateEnum.Accepted;
            Assert.That(state, Is.EqualTo(ExecutionStateEnum.Failed),
                "Abort policy should stop the mission after first step fails");
        }

        [Test]
        public async Task ErrorPolicySkipContinuesToNextStep()
        {
            m_host = NewHost(Options());
            int callCount = 0;
            m_executor.OnExecute = _ => callCount++;
            int failOnFirst = 0;
            m_executor.OutcomeFunc = exec =>
            {
                int idx = Interlocked.Increment(ref failOnFirst);
                return idx == 1
                    ? IntentOutcome.Fail(IntentFailureEnum.Other, "skippable")
                    : IntentOutcome.Success;
            };

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "skip-policy",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "s1",
                        SequenceId = 1,
                        Released = true,
                        ErrorPolicy = ErrorPolicyEnum.Skip,
                        Intent = Move()
                    },
                    new MissionStepDataType
                    {
                        StepId = "s2",
                        SequenceId = 2,
                        Released = true,
                        Intent = Move()
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            Assert.That(callCount, Is.EqualTo(2), "Skip should have continued to s2");
            MissionObjectState? node = FindMissionNode(admission.Operation);
            ExecutionStateEnum state = node!.ExecutionState?.Value is ExecutionStateEnum s
                ? s : ExecutionStateEnum.Accepted;
            Assert.That(state, Is.EqualTo(ExecutionStateEnum.Succeeded));
        }

        [Test]
        public async Task StepIntentIdWrittenAtAdmissionBeforeExecution()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_host = NewHost(Options());

            var mission = new MissionDataType
            {
                MissionId = "imm-corr",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "step-one",
                        SequenceId = 1,
                        Released = true,
                        Intent = Move()
                    }
                ]
            };

            MissionAdmission admission = m_host.SubmitMission(m_context, null, mission);
            Assert.That(admission.Accepted, Is.True);

            Assert.That(mission.Steps[0].Intent!.IntentId, Is.EqualTo("imm-corr/step-one"),
                "IntentId should be written into the step at admission, before execution");

            m_executor.Gate.Release();
            await WaitForCompletion(admission.Operation);
        }

        [Test]
        public async Task MultiStepMissionTracksOperationNodesPerStep()
        {
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(m_context, null, new MissionDataType
            {
                MissionId = "multi-ops",
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "s1",
                        SequenceId = 1,
                        Released = true,
                        Intent = Move()
                    },
                    new MissionStepDataType
                    {
                        StepId = "s2",
                        SequenceId = 2,
                        Released = true,
                        Intent = Move()
                    }
                ]
            });

            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);

            ArrayOf<MissionStepDataType> steps = node!.Mission?.Value is MissionDataType md
                ? md.Steps
                : [];
            Assert.That(steps.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(steps[0].Operation.IsNull, Is.False,
                "First step should have an operation NodeId");
            Assert.That(steps[1].Operation.IsNull, Is.False,
                "Second step should have an operation NodeId");
        }

        [Test]
        public async Task GatedStepHasOperationAndStateBeforeRelease()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            var executionStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_executor.OnExecute = _ => executionStarted.TrySetResult(true);
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(
                m_context, null, SimpleMission("gated-map"));
            Assert.That(admission.Accepted, Is.True);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            Assert.That(node, Is.Not.Null);
            await executionStarted.Task.ConfigureAwait(false);

            MissionDataType md = node!.Mission?.Value is MissionDataType m
                ? m : new MissionDataType();
            Assert.That(md.Steps.Count, Is.GreaterThanOrEqualTo(1));

            MissionStepDataType firstStep = md.Steps[0];
            Assert.Multiple(() =>
            {
                Assert.That(firstStep.Operation.IsNull, Is.False,
                    "Operation should be set before execution completes");
                Assert.That(firstStep.Intent!.IntentId, Is.EqualTo("gated-map/s1"));
                Assert.That(firstStep.Status, Is.EqualTo(ExecutionStateEnum.Executing));
            });

            m_executor.Gate.Release();
            await WaitForCompletion(admission.Operation);
        }

        [Test]
        public async Task DynamicMissionsFolderUsesRobotIntentNamespace()
        {
            m_host = NewHost(Options());

            MissionAdmission admission = m_host.SubmitMission(
                m_context, null, SimpleMission("ns-check"));
            Assert.That(admission.Accepted, Is.True);
            await WaitForCompletion(admission.Operation);

            FolderState? missionsFolder = null;
            lock (m_addedLock)
            {
                missionsFolder = m_added.OfType<FolderState>()
                    .FirstOrDefault(f => f.BrowseName.Name == "Missions");
            }

            if (missionsFolder != null)
            {
                ushort riNs = (ushort)m_messageContext.NamespaceUris.GetIndex(
                    RiNamespaces.RobotIntent);
                MissionObjectState? mission = FindMissionNode(admission.Operation);
                Assert.That(
                    missionsFolder.BrowseName.NamespaceIndex,
                    Is.EqualTo(riNs),
                    "Dynamically created Missions folder must use RobotIntent namespace");
                Assert.That(mission, Is.Not.Null);
                Assert.That(
                    mission!.BrowseName.NamespaceIndex,
                    Is.EqualTo(m_controller.BrowseName.NamespaceIndex),
                    "Dynamic mission instances must use the controller instance namespace.");
            }
        }

        [Test]
        public async Task ReusedMissionIdRetainsPriorInvocationUntilPruning()
        {
            IntentControllerHostOptions options = Options();
            options.RetainedTerminalMissions = 32;
            m_host = NewHost(options);

            MissionAdmission first = m_host.SubmitMission(
                m_context, null, SimpleMission("reuse-id", "reuse-id-first"));
            Assert.That(first.Accepted, Is.True);
            await WaitForCompletion(first.Operation);
            NodeId firstNode = first.Operation;

            MissionAdmission second = m_host.SubmitMission(
                m_context, null, SimpleMission("reuse-id", "reuse-id-second"));
            Assert.That(second.Accepted, Is.True);
            await WaitForCompletion(second.Operation);
            NodeId secondNode = second.Operation;

            Assert.That(secondNode, Is.Not.EqualTo(firstNode),
                "Reused MissionId should create a new node");

            MissionObjectState? oldNode = FindMissionNode(firstNode);
            Assert.That(oldNode, Is.Not.Null,
                "A reused MissionId must not overwrite the prior retained invocation.");

            MissionObjectState? newNode = FindMissionNode(secondNode);
            Assert.That(newNode, Is.Not.Null,
                "New mission node should still be browseable");
        }

        [Test]
        public async Task ReusedMissionIdWithOmittedIntentIdGeneratesANewRunIdentity()
        {
            m_host = NewHost(Options());
            MissionDataType firstMission = SimpleMission("reuse-generated");
            MissionDataType secondMission = SimpleMission("reuse-generated");

            MissionAdmission first = m_host.SubmitMission(m_context, null, firstMission);
            Assert.That(first.Accepted, Is.True);
            await WaitForCompletion(first.Operation);

            MissionAdmission second = m_host.SubmitMission(m_context, null, secondMission);
            Assert.That(second.Accepted, Is.True);
            await WaitForCompletion(second.Operation);

            Assert.Multiple(() =>
            {
                Assert.That(
                    firstMission.Steps[0].Intent!.IntentId,
                    Is.EqualTo("reuse-generated/s1"));
                Assert.That(
                    secondMission.Steps[0].Intent!.IntentId,
                    Is.EqualTo("reuse-generated/s1#run-2"));
                Assert.That(second.Operation, Is.Not.EqualTo(first.Operation));
            });
        }

        [Test]
        public async Task ReusedMissionIdsPruneOldestTerminalInvocationsAtRetentionBound()
        {
            IntentControllerHostOptions options = Options();
            options.RetainedTerminalMissions = 2;
            m_host = NewHost(options);

            var admissions = new List<MissionAdmission>();
            for (int ii = 0; ii < 4; ii++)
            {
                MissionAdmission admission = m_host.SubmitMission(
                    m_context,
                    null,
                    SimpleMission("reused-retention", $"reused-retention-{ii}"));
                Assert.That(admission.Accepted, Is.True);
                admissions.Add(admission);
                await WaitForCompletion(admission.Operation);
            }

            Assert.Multiple(() =>
            {
                Assert.That(FindMissionNode(admissions[0].Operation), Is.Null);
                Assert.That(FindMissionNode(admissions[1].Operation), Is.Null);
                Assert.That(FindMissionNode(admissions[2].Operation), Is.Not.Null);
                Assert.That(FindMissionNode(admissions[3].Operation), Is.Not.Null);
            });
        }

        [Test]
        public async Task ExplicitIntentIdCollisionWithRetainedIsRefused()
        {
            m_host = NewHost(Options());

            IntentAdmission standalone = m_host.SubmitIntent(
                m_context, null, MoveWithId("retained-op"));
            Assert.That(standalone.Accepted, Is.True);

            MissionAdmission admission = m_host.SubmitMission(
                m_context, null, new MissionDataType
                {
                    MissionId = "collide-retained",
                    Steps =
                    [
                        new MissionStepDataType
                        {
                            StepId = "s1",
                            SequenceId = 1,
                            Released = true,
                            Intent = MoveWithId("retained-op")
                        }
                    ]
                });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False);
                Assert.That(admission.Message, Does.Contain("retained-op"));
            });
        }

        [Test]
        public void RefusedStepAdmissionPropagatesFailure()
        {
            IntentControllerHostOptions options = Options();
            m_host = NewHost(options);

            MissionAdmission admission = m_host.SubmitMission(
                m_context, null, new MissionDataType
                {
                    MissionId = "refused-step",
                    Steps =
                    [
                        new MissionStepDataType
                        {
                            StepId = "s1",
                            SequenceId = 1,
                            Released = true,
                            Intent = MoveWithId("collide-explicit")
                        },
                        new MissionStepDataType
                        {
                            StepId = "s2",
                            SequenceId = 2,
                            Released = true,
                            Intent = MoveWithId("collide-explicit")
                        }
                    ]
                });

            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.False,
                    "Mission with duplicate explicit IDs should be refused");
                Assert.That(
                    admission.Failure,
                    Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(
                    admission.Message,
                    Does.Contain("collide-explicit"));
            });
        }

        [Test]
        public async Task HorizonUpdateWithOmittedIntentIdsPreservesBaseAndCompletes()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            IntentControllerHostOptions options = Options();
            options.MissionHorizonSupported = true;
            m_host = NewHost(options);

            MissionAdmission admission = m_host.SubmitMission(
                m_context,
                null,
                new MissionDataType
                {
                    MissionId = "updated-horizon",
                    MissionUpdateId = 1,
                    Steps =
                    [
                        new MissionStepDataType
                        {
                            StepId = "base",
                            SequenceId = 1,
                            Released = true,
                            Intent = Move()
                        },
                        new MissionStepDataType
                        {
                            StepId = "horizon",
                            SequenceId = 2,
                            Released = false,
                            Intent = Move()
                        }
                    ]
                });
            Assert.That(admission.Accepted, Is.True);

            MissionUpdateOutcome update = m_host.UpdateMission(
                m_context,
                null,
                "updated-horizon",
                2,
                [
                    new MissionStepDataType
                    {
                        StepId = "base",
                        SequenceId = 1,
                        Released = true,
                        Intent = Move()
                    },
                    new MissionStepDataType
                    {
                        StepId = "horizon",
                        SequenceId = 2,
                        Released = true,
                        Intent = Move()
                    }
                ]);
            Assert.That(update.Result, Is.EqualTo(MissionUpdateResultEnum.Accepted));

            m_executor.Gate.Release(2);
            await WaitForCompletion(admission.Operation);

            MissionObjectState? node = FindMissionNode(admission.Operation);
            MissionDataType published = node?.Mission?.Value is MissionDataType value
                ? value
                : new MissionDataType();
            Assert.Multiple(() =>
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(node!.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(published.Steps, Has.Count.EqualTo(2));
                Assert.That(published.Steps[0].Intent!.IntentId, Is.EqualTo("updated-horizon/base"));
                Assert.That(published.Steps[1].Intent!.IntentId, Is.EqualTo("updated-horizon/horizon"));
                Assert.That(published.Steps[0].Operation.IsNull, Is.False);
                Assert.That(published.Steps[1].Operation.IsNull, Is.False);
                Assert.That(published.Steps[1].Status, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        private IntentControllerHostOptions Options()
        {
            var options = new IntentControllerHostOptions
            {
                RequireControlAuthority = false,
                MissionsSupported = true,
                RetainedTerminalMissions = 32
            };
            options.Accept(global::Opc.Ua.RobotIntent.DataTypeIds.LinearMoveIntentDataType);
            return options;
        }

        private IntentControllerHost NewHost(IntentControllerHostOptions options)
        {
            var host = new IntentControllerHost(
                m_controller,
                m_executor,
                (node, ct) =>
                {
                    lock (m_addedLock)
                    {
                        m_added.Add(node);
                    }
                    return default;
                },
                options,
                (node, ct) =>
                {
                    lock (m_addedLock)
                    {
                        m_added.Remove(node);
                    }
                    return default;
                });
            host.Start(m_context);
            return host;
        }

        private static MissionDataType SimpleMission(string missionId, string? intentId = null)
        {
            LinearMoveIntentDataType intent = Move();
            intent.IntentId = intentId ?? string.Empty;
            return new MissionDataType
            {
                MissionId = missionId,
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "s1",
                        SequenceId = 1,
                        Released = true,
                        Intent = intent
                    }
                ]
            };
        }

        private static LinearMoveIntentDataType Move()
        {
            return new LinearMoveIntentDataType
            {
                BufferMode = BufferModeEnum.Aborting,
                Target = new Pose3DDataType
                {
                    FrameId = "base",
                    Position = [1.0, 0.0, 0.0],
                    Orientation = [0.0, 0.0, 0.0, 1.0]
                }
            };
        }

        private static LinearMoveIntentDataType MoveWithId(string intentId)
        {
            var intent = Move();
            intent.IntentId = intentId;
            return intent;
        }

        private MissionObjectState? FindMissionNode(NodeId nodeId)
        {
            lock (m_addedLock)
            {
                return m_added.OfType<MissionObjectState>()
                    .FirstOrDefault(n => n.NodeId == nodeId);
            }
        }

        private async Task WaitForIntentCompletion(NodeId operationNodeId)
        {
            for (int i = 0; i < 200; i++)
            {
                IntentOperationState? node;
                lock (m_addedLock)
                {
                    node = m_added.OfType<IntentOperationState>()
                        .FirstOrDefault(candidate => candidate.NodeId == operationNodeId);
                }
                if (node?.ExecutionState?.Value is ExecutionStateEnum state && IntentOutcome.IsTerminal(state))
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("Intent did not reach terminal state within 2 seconds");
        }

        private IntentFailureEnum ReadFailureEnum(MissionObjectState node)
        {
            BaseObjectState? finalResult = node.FinalResultData;
            if (finalResult == null)
            {
                return IntentFailureEnum.None;
            }
            if (finalResult.FindChild(
                m_context,
                new QualifiedName("Failure", node.BrowseName.NamespaceIndex))
                is BaseDataVariableState failureVar)
            {
                Variant v = failureVar.WrappedValue;
                if (v.TryGetValue(out int intVal))
                {
                    return EnumHelper.Int32ToEnum<IntentFailureEnum>(intVal);
                }
            }
            return IntentFailureEnum.None;
        }

        private string ReadFailureMessage(MissionObjectState node)
        {
            BaseObjectState? finalResult = node.FinalResultData;
            if (finalResult == null)
            {
                return string.Empty;
            }
            if (finalResult.FindChild(
                m_context,
                new QualifiedName("Message", node.BrowseName.NamespaceIndex))
                is BaseDataVariableState messageVar)
            {
                Variant v = messageVar.WrappedValue;
                if (v.TryGetValue(out LocalizedText text))
                {
                    return text.Text ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private async Task WaitForCompletion(NodeId operationNodeId)
        {
            for (int i = 0; i < 200; i++)
            {
                MissionObjectState? node = FindMissionNode(operationNodeId);
                if (node != null)
                {
                    ExecutionStateEnum state = node.ExecutionState?.Value is ExecutionStateEnum s
                        ? s
                        : ExecutionStateEnum.Accepted;
                    if (IntentOutcome.IsTerminal(state))
                    {
                        return;
                    }
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("Mission did not reach terminal state within 2 seconds");
        }

        private static IEnumerable<TestCaseData> RevisitedErrorPolicies()
        {
            yield return new TestCaseData(
                ErrorPolicyEnum.Fallback,
                ExecutionStateEnum.Succeeded);
            yield return new TestCaseData(
                ErrorPolicyEnum.Compensate,
                ExecutionStateEnum.Succeeded);
        }

        private ServiceMessageContext m_messageContext = null!;
        private SystemContext m_context = null!;
        private IntentControllerState m_controller = null!;
        private ScriptedExecutor m_executor = null!;
        private IntentControllerHost? m_host;
        private readonly Lock m_addedLock = new();
        private readonly List<NodeState> m_added = [];

        private sealed class ScriptedExecutor : IIntentExecutor
        {
            public SemaphoreSlim? Gate { get; set; }
            public IntentOutcome Outcome { get; set; } = IntentOutcome.Success;
            public Func<IntentExecution, IntentOutcome>? OutcomeFunc { get; set; }
            public Action<IntentExecution>? OnExecute { get; set; }
            public Exception? Exception { get; set; }

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution, CancellationToken cancellationToken)
            {
                OnExecute?.Invoke(execution);
                if (Exception != null)
                {
                    throw Exception;
                }
                if (Gate != null)
                {
                    await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return OutcomeFunc?.Invoke(execution) ?? Outcome;
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }
    }
}
