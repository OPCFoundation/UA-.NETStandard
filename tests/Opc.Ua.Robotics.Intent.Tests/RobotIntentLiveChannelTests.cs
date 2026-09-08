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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Configuration;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Robotics.Intent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Server.Hosting;
using RiRobotIntent = Opc.Ua.RobotIntent;
using RiServer = Opc.Ua.RobotIntent.Server;

namespace Opc.Ua.Robotics.Intent.Tests
{
    /// <summary>
    /// Exercises Robot Intent through real OPC UA SecureChannels and Sessions.
    /// </summary>
    [TestFixture]
    [Category("RobotIntent")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class RobotIntentLiveChannelTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new TestServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DiscoveryAndCapabilityHonestyAreObservableOverSession()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("discovery").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(NoPauseControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            RobotIntentControllerInfo info = await controller.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(info.SupportedIntents, Has.Count.EqualTo(5));
                Assert.That(info.Lookups.Frames, Has.Count.GreaterThanOrEqualTo(2));
                Assert.That(info.Lookups.Tools, Has.Count.EqualTo(1));
                Assert.That(info.Lookups.Locations, Has.Count.EqualTo(1));
                Assert.That(info.Lookups.Axes, Has.Count.EqualTo(6));
                Assert.That(info.Lookups.Outputs, Has.Count.EqualTo(1));
                Assert.That(info.Lookups.Programs, Has.Count.EqualTo(1));
                Assert.That(info.Facets.Base, Is.True);
                Assert.That(info.Facets.QueuedIntents, Is.True);
                Assert.That(info.Facets.RealTimeChannels, Is.True);
            });

            foreach (IntentCapabilityDataType? capability in info.SupportedIntents.ToArray()!)
            {
                Assert.That(capability, Is.Not.Null, "Declared intent capabilities must not contain null entries.");
                RiRobotIntent.IntentDataType intent = CreateIntentFor(capability!.IntentType, info);
                IntentSubmissionResult result = await controller.TrySubmitIntentAsync(intent).ConfigureAwait(false);
                Assert.That(
                    result.Failure,
                    Is.Not.EqualTo(IntentFailureEnum.CapabilityNotSupported),
                    $"{intent.GetType().Name} is declared and must not be refused as unsupported.");
                if (result.Accepted)
                {
                    await WaitForTerminalAsync(controller, result.IntentId, result.Operation).ConfigureAwait(false);
                }
            }

            IntentSubmissionResult unsupported = await controller
                .TrySubmitIntentAsync(new RiRobotIntent.ForceIntentDataType
                {
                    Direction = [0.0, 0.0, -1.0],
                    ContactForce = 10.0,
                    MaxDistance = 0.01
                })
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(unsupported.Accepted, Is.False);
                Assert.That(unsupported.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(unsupported.Message.Text, Is.Not.Empty);
            });
        }

        [Test]
        public async Task PublishedSupportedFacetsAreObservableAndNotRecomputedFromProjection()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("supported-facets").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(NoPauseControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            RobotIntentControllerInfo info = await controller.ReadAsync().ConfigureAwait(false);
            string[] supportedFacets = info.SupportedFacets.ToArray()!;

            // The local projection can only see individual capability flags. The published SupportedFacets claim is
            // authoritative for facets with additional structural or behavioural requirements, so this test only
            // compares a facet whose answer is visible to both sides.
            Assert.Multiple(() =>
            {
                Assert.That(supportedFacets, Is.Not.Empty);
                Assert.That(supportedFacets, Does.Contain("RI-Base"));
                Assert.That(supportedFacets, Does.Contain("RI-Motion-Linear"));
                Assert.That(supportedFacets, Does.Contain("RI-Output"));
                Assert.That(supportedFacets, Does.Contain("RI-Program"));
                Assert.That(supportedFacets, Does.Contain("RI-Wait"));
                Assert.That(supportedFacets, Does.Contain("RI-RealTimeChannel"));
                Assert.That(supportedFacets, Does.Contain("RI-Queue"));
                Assert.That(supportedFacets, Does.Contain("RI-Mission"));
                Assert.That(supportedFacets, Does.Contain("RI-Mission-Horizon"));
                Assert.That(supportedFacets, Does.Contain("RI-Mission-Branching"));
                Assert.That(supportedFacets, Does.Not.Contain("RI-Pause"));
                Assert.That(
                    supportedFacets.Contains("RI-RealTimeChannel"),
                    Is.EqualTo(info.Facets.RealTimeChannels));
            });
        }

        [Test]
        public async Task SubmitTrackCompleteAndPartTenResultSurviveTheChannel()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("track").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("tracked");
            await using IntentOperationHandle handle = await controller.SubmitIntentAsync(LinearIntent("tracked"))
                .ConfigureAwait(false);
            IntentOperationSnapshot executing;
            IntentOperationSnapshot final;
            try
            {
                executing = await WaitForSnapshotAsync(
                    controller,
                    handle.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                    "tracked intent executing").ConfigureAwait(false);
                m_fixture.Executor.ReleaseCompletion("tracked");
                final = await WaitForSnapshotAsync(
                    controller,
                    handle.Operation,
                    snapshot => snapshot.Result.State == ExecutionStateEnum.Succeeded,
                    "linear intent result update").ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCompletion("tracked");
            }
            IntentResultDataType partTen = await ReadFinalResultAsync(
                context.Session,
                controller.Transport,
                handle.Operation)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(executing.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(executing.Progress, Is.GreaterThan(0.0));
                Assert.That(executing.CurrentPose.Position.IsNull, Is.False);
                Assert.That(final.ExecutionState, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(final.Result.IntentId, Is.EqualTo(handle.IntentId));
                Assert.That(partTen.IntentId, Is.EqualTo(final.Result.IntentId));
                Assert.That(partTen.State, Is.EqualTo(final.Result.State));
                Assert.That(partTen.Failure, Is.EqualTo(final.Result.Failure));
            });
        }

        [Test]
        public async Task RefusalsAreGoodOutputsAndAuthorityIsPerSession()
        {
            await using ClientContext first = await m_fixture.ConnectAsync("authority-a").ConfigureAwait(false);
            await using ClientContext second = await m_fixture.ConnectAsync("authority-b").ConfigureAwait(false);
            RobotIntentControllerClient firstController = await first.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            RobotIntentControllerClient secondController = await second.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            CommandAuthorityLease firstAuthority = await WaitForAuthorityAsync(firstController)
                .ConfigureAwait(false);

            IntentSubmissionResult secondResult = await secondController.TrySubmitIntentAsync(LinearIntent("second"))
                .ConfigureAwait(false);
            RobotIntentControllerInfo readWhileRefused = await secondController.ReadAsync()
                .ConfigureAwait(false);
            await first.Session.CloseAsync(1000, true).ConfigureAwait(false);
            await using CommandAuthorityLease secondAuthority = await WaitForAuthorityAsync(secondController)
                .ConfigureAwait(false);
            IntentSubmissionResult accepted = await secondController.TrySubmitIntentAsync(LinearIntent("after-close"))
                .ConfigureAwait(false);
            await WaitForTerminalAsync(secondController, accepted.IntentId, accepted.Operation).ConfigureAwait(false);

            RobotIntentControllerClient manual = await second.GetControllerAsync(ManualControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease manualAuthority = await WaitForAuthorityAsync(manual)
                .ConfigureAwait(false);
            IntentSubmissionResult modeRefusal = await manual.TrySubmitIntentAsync(LinearIntent("manual"))
                .ConfigureAwait(false);
            IntentSubmissionResult invalid = await secondController
                .TrySubmitIntentAsync(new RiRobotIntent.LinearMoveIntentDataType())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(secondResult.Accepted, Is.False);
                Assert.That(secondResult.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(readWhileRefused.Lookups.Axes, Has.Count.EqualTo(6));
                Assert.That(accepted.Accepted, Is.True);
                Assert.That(modeRefusal.Accepted, Is.False);
                Assert.That(modeRefusal.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(invalid.Accepted, Is.False);
                Assert.That(invalid.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(invalid.Message.Text, Is.Not.Empty);
            });
        }

        [Test]
        public async Task CancellingIsObservableAndNotTerminalUntilExecutorCompletes()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("cancel-live").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("cancel");
            m_fixture.Executor.HoldCancellationCompletion("cancel");
            await using IntentOperationHandle running = await controller.SubmitIntentAsync(WaitIntent("cancel", 800))
                .ConfigureAwait(false);
            IntentCommandOutcome cancel;
            bool cancellingWasTerminal;
            IntentResultDataType cancelled;
            try
            {
                await WaitForStateAsync(running, ExecutionStateEnum.Executing).ConfigureAwait(false);
                cancel = await running.CancelAsync(StopModeEnum.QuickStop).ConfigureAwait(false);
                await WaitForStateAsync(running, ExecutionStateEnum.Cancelling).ConfigureAwait(false);
                cancellingWasTerminal = running.Completion.IsCompleted;
                m_fixture.Executor.ReleaseCancellationCompletion("cancel");
                cancelled = await WaitForTerminalAsync(controller, running.IntentId, running.Operation)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCancellationCompletion("cancel");
                m_fixture.Executor.ReleaseCompletion("cancel");
            }

            Assert.Multiple(() =>
            {
                Assert.That(cancel.Accepted, Is.True);
                Assert.That(cancellingWasTerminal, Is.False);
                Assert.That(cancelled.State, Is.EqualTo(ExecutionStateEnum.Cancelled));
            });
        }

        [Test]
        public async Task NonCancelableIntentRefusesCancel()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("cancel-refused").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            RobotIntentControllerInfo info = await controller.ReadAsync().ConfigureAwait(false);
            m_fixture.Executor.HoldCompletion("no-cancel");
            IntentSubmissionResult nonCancelable = await controller
                .TrySubmitIntentAsync(CallProgramIntent("no-cancel", info.Lookups.Programs[0].NodeId))
                .ConfigureAwait(false);
            IntentCommandOutcome refusedCancel;
            try
            {
                await WaitForSnapshotAsync(
                    controller,
                    nonCancelable.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                    "non-cancelable intent executing").ConfigureAwait(false);
                refusedCancel = await controller.Transport
                    .CancelIntentAsync(nonCancelable.IntentId, StopModeEnum.QuickStop)
                    .ConfigureAwait(false);
                m_fixture.Executor.ReleaseCompletion("no-cancel");
                await WaitForTerminalAsync(controller, nonCancelable.IntentId, nonCancelable.Operation)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCompletion("no-cancel");
            }

            Assert.Multiple(() =>
            {
                Assert.That(nonCancelable.Accepted, Is.True);
                Assert.That(refusedCancel.Accepted, Is.False);
            });
        }

        [Test]
        public async Task QueueingAndQueueFullAreObservable()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("queue-live").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("head");
            await using IntentOperationHandle queueHead = await controller.SubmitIntentAsync(WaitIntent("head", 900))
                .ConfigureAwait(false);
            IntentSubmissionResult queued;
            IntentOperationSnapshot queuedSnapshot;
            IntentSubmissionResult queuedAgain;
            IntentSubmissionResult queueFull;
            try
            {
                await WaitForStateAsync(queueHead, ExecutionStateEnum.Executing).ConfigureAwait(false);
                queued = await controller.TrySubmitIntentAsync(WaitIntent("queued", 100, BufferModeEnum.Buffered))
                    .ConfigureAwait(false);
                queuedSnapshot = await WaitForSnapshotAsync(
                    controller,
                    queued.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Queued,
                    "queued intent state").ConfigureAwait(false);
                queuedAgain = await controller.TrySubmitIntentAsync(
                    WaitIntent("queued-again", 100, BufferModeEnum.Buffered)).ConfigureAwait(false);
                queueFull = await controller.TrySubmitIntentAsync(
                    WaitIntent("queue-full", 100, BufferModeEnum.Buffered)).ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCompletion("head");
            }

            Assert.Multiple(() =>
            {
                Assert.That(queued.Accepted, Is.True);
                Assert.That(queuedAgain.Accepted, Is.True);
                Assert.That(queuedSnapshot.ExecutionState, Is.EqualTo(ExecutionStateEnum.Queued));
                Assert.That(queueFull.Accepted, Is.False);
                Assert.That(queueFull.Failure, Is.EqualTo(IntentFailureEnum.QueueFull));
            });
        }

        [Test]
        public async Task RealTimeChannelLeaseExcludesOtherSessionUntilClosed()
        {
            await using ClientContext first = await m_fixture.ConnectAsync("lease-a").ConfigureAwait(false);
            await using ClientContext second = await m_fixture.ConnectAsync("lease-b").ConfigureAwait(false);
            RobotIntentControllerClient controller = await first.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            RobotIntentControllerClient secondController = await second.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            RealTimeChannelOpenResult lease = await controller.Transport
                .OpenRealTimeChannelAsync(ChannelId, 30000.0)
                .ConfigureAwait(false);
            RealTimeChannelOpenResult secondLease = await secondController.Transport
                .OpenRealTimeChannelAsync(ChannelId, 30000.0)
                .ConfigureAwait(false);
            bool closed = await controller.Transport.CloseRealTimeChannelAsync(ChannelId).ConfigureAwait(false);
            await authority.DisposeAsync().ConfigureAwait(false);
            await using CommandAuthorityLease secondAuthority = await WaitForAuthorityAsync(secondController)
                .ConfigureAwait(false);
            RealTimeChannelOpenResult afterClose = await secondController.Transport
                .OpenRealTimeChannelAsync(ChannelId, 30000.0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(lease.Granted, Is.True);
                Assert.That(secondLease.Granted, Is.False);
                Assert.That(closed, Is.True);
                Assert.That(afterClose.Granted, Is.True);
            });
        }

        [Test]
        public async Task LiveOperationStateMatchesPartTenCurrentStateAcrossLifecycle()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("part-ten-lifecycle").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("part-ten");
            m_fixture.Executor.HoldCancellationCompletion("part-ten");
            await using IntentOperationHandle handle = await controller.SubmitIntentAsync(WaitIntent("part-ten", 800))
                .ConfigureAwait(false);
            IntentOperationSnapshot executing;
            NodeId executingPartTen;
            IntentCommandOutcome cancel;
            IntentOperationSnapshot cancelling;
            NodeId cancellingPartTen;
            IntentResultDataType terminal;
            NodeId terminalPartTen;
            try
            {
                executing = await WaitForSnapshotAsync(
                    controller,
                    handle.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                    "part-ten intent executing").ConfigureAwait(false);
                executingPartTen = await ReadProgramCurrentStateIdAsync(context.Session, handle.Operation)
                    .ConfigureAwait(false);
                cancel = await handle.CancelAsync(StopModeEnum.QuickStop).ConfigureAwait(false);
                cancelling = await WaitForSnapshotAsync(
                    controller,
                    handle.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Cancelling,
                    "part-ten intent cancelling").ConfigureAwait(false);
                cancellingPartTen = await ReadProgramCurrentStateIdAsync(context.Session, handle.Operation)
                    .ConfigureAwait(false);
                m_fixture.Executor.ReleaseCancellationCompletion("part-ten");
                terminal = await WaitForTerminalAsync(controller, handle.IntentId, handle.Operation)
                    .ConfigureAwait(false);
                terminalPartTen = await ReadProgramCurrentStateIdAsync(context.Session, handle.Operation)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCancellationCompletion("part-ten");
                m_fixture.Executor.ReleaseCompletion("part-ten");
            }

            Assert.Multiple(() =>
            {
                Assert.That(executing.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(
                    executingPartTen,
                    Is.EqualTo(StandardNode(global::Opc.Ua.Objects.ProgramStateMachineType_Running)));
                Assert.That(cancel.Accepted, Is.True);
                Assert.That(cancelling.ExecutionState, Is.EqualTo(ExecutionStateEnum.Cancelling));
                Assert.That(
                    cancellingPartTen,
                    Is.EqualTo(StandardNode(global::Opc.Ua.Objects.ProgramStateMachineType_Running)));
                Assert.That(terminal.State, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(
                    terminalPartTen,
                    Is.EqualTo(StandardNode(global::Opc.Ua.Objects.ProgramStateMachineType_Halted)));
            });
        }

        [Test]
        public async Task PauseResumeAndRetriableAreObservableOverSession()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("pause-retry").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("pause-live");
            m_fixture.Executor.HoldCompletion("pause-queued");
            await using IntentOperationHandle pausing = await controller.SubmitIntentAsync(WaitIntent("pause-live", 600))
                .ConfigureAwait(false);
            IntentSubmissionResult queued;
            IntentCommandOutcome pause;
            IntentOperationSnapshot executingWhilePaused;
            IntentOperationSnapshot queuedBeforePause;
            IntentOperationSnapshot queuedWhilePaused;
            IntentCommandOutcome resume;
            IntentOperationSnapshot resumed;
            IntentResultDataType pauseResult;
            IntentResultDataType queuedResult;
            bool readyWhilePaused;
            try
            {
                await WaitForStateAsync(pausing, ExecutionStateEnum.Executing).ConfigureAwait(false);
                queued = await controller.TrySubmitIntentAsync(WaitIntent("pause-queued", 100, BufferModeEnum.Buffered))
                    .ConfigureAwait(false);
                queuedBeforePause = await WaitForSnapshotAsync(
                    controller,
                    queued.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Queued,
                    "pause-queued queued before pause").ConfigureAwait(false);
                pause = await controller.Transport.PauseAsync().ConfigureAwait(false);
                executingWhilePaused = await WaitForSnapshotAsync(
                    controller,
                    pausing.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                    "pause-live remains executing while paused").ConfigureAwait(false);
                await WaitForAsync(
                    async () => !await ReadBooleanPathAsync(
                        context.Session,
                        controller.Transport.ControllerId,
                        "Ready").ConfigureAwait(false),
                    "controller Ready to become false after Pause").ConfigureAwait(false);
                readyWhilePaused = await ReadBooleanPathAsync(
                    context.Session,
                    controller.Transport.ControllerId,
                    "Ready").ConfigureAwait(false);
                m_fixture.Executor.ReleaseCompletion("pause-live");
                pauseResult = await WaitForTerminalAsync(controller, pausing.IntentId, pausing.Operation)
                    .ConfigureAwait(false);
                queuedWhilePaused = await WaitForSnapshotAsync(
                    controller,
                    queued.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Queued,
                    "pause-queued remains queued while paused").ConfigureAwait(false);
                resume = await controller.Transport.ResumeAsync().ConfigureAwait(false);
                resumed = await WaitForSnapshotAsync(
                    controller,
                    queued.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                    "pause-queued starts after resume").ConfigureAwait(false);
                m_fixture.Executor.ReleaseCompletion("pause-queued");
                queuedResult = await WaitForTerminalAsync(controller, queued.IntentId, queued.Operation)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCompletion("pause-live");
                m_fixture.Executor.ReleaseCompletion("pause-queued");
            }

            IntentSubmissionResult retriable = await controller
                .TrySubmitIntentAsync(WaitIntent("retriable-live", 50))
                .ConfigureAwait(false);
            IntentResultDataType retriableResult = await WaitForTerminalAsync(
                controller,
                retriable.IntentId,
                retriable.Operation).ConfigureAwait(false);
            IntentSubmissionResult retry = await controller.Transport.RetryAsync(retriable.IntentId)
                .ConfigureAwait(false);
            IntentResultDataType retryResult = await WaitForTerminalAsync(
                controller,
                retry.IntentId,
                retry.Operation).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pause.Accepted, Is.True);
                Assert.That(queued.Accepted, Is.True);
                Assert.That(executingWhilePaused.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(queuedBeforePause.ExecutionState, Is.EqualTo(ExecutionStateEnum.Queued));
                Assert.That(readyWhilePaused, Is.False);
                Assert.That(resume.Accepted, Is.True);
                Assert.That(queuedWhilePaused.ExecutionState, Is.EqualTo(ExecutionStateEnum.Queued));
                Assert.That(resumed.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(pauseResult.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(queuedResult.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(retriable.Accepted, Is.True);
                Assert.That(retriableResult.State, Is.EqualTo(ExecutionStateEnum.Retriable));
                Assert.That(retry.Accepted, Is.True);
                Assert.That(retry.Operation.IsNull, Is.False);
                Assert.That(retry.Operation, Is.Not.EqualTo(retriable.Operation));
                Assert.That(retryResult.State, Is.EqualTo(ExecutionStateEnum.Retriable));
            });
        }

        [Test]
        public async Task MissionUpdatesAndDisconnectedResultRetentionAreObservable()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("mission").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);
            MissionDataType mission = CreateMission("mission-retention");

            MissionSubmissionResult submission = await controller.SubmitMissionAsync(mission).ConfigureAwait(false);
            MissionDataType beforeRejected = await ReadMissionAsync(context.Session, submission.Operation)
                .ConfigureAwait(false);
            MissionUpdateOutcome outdated = await controller.Transport
                .UpdateMissionAsync(submission.MissionId, 0, mission.Steps)
                .ConfigureAwait(false);
            MissionUpdateOutcome baseConflict = await controller.Transport
                .UpdateMissionAsync(submission.MissionId, 1, CreateBaseConflictSteps())
                .ConfigureAwait(false);
            MissionDataType afterBaseConflict = await ReadMissionAsync(context.Session, submission.Operation)
                .ConfigureAwait(false);
            MissionUpdateOutcome accepted = await controller.Transport
                .UpdateMissionAsync(submission.MissionId, 1, CreateAcceptedUpdateSteps())
                .ConfigureAwait(false);
            uint advancedUpdateId = await ReadUInt32ChildAsync(
                context.Session,
                controller.Transport,
                submission.Operation,
                "MissionUpdateId").ConfigureAwait(false);
            MissionUpdateOutcome replayAcceptedId = await controller.Transport
                .UpdateMissionAsync(submission.MissionId, 1, CreateAcceptedUpdateSteps())
                .ConfigureAwait(false);
            MissionDataType beforeInvalid = await ReadMissionAsync(context.Session, submission.Operation)
                .ConfigureAwait(false);
            MissionUpdateOutcome invalid = await controller.Transport
                .UpdateMissionAsync(submission.MissionId, 2, CreateRejectedUpdateSteps())
                .ConfigureAwait(false);
            MissionDataType afterInvalid = await ReadMissionAsync(context.Session, submission.Operation)
                .ConfigureAwait(false);

            IntentSubmissionResult disconnecting = await controller.TrySubmitIntentAsync(WaitIntent("disconnect", 300))
                .ConfigureAwait(false);
            NodeId operation = disconnecting.Operation;
            await authority.DisposeAsync().ConfigureAwait(false);
            await context.Session.CloseAsync(1000, true).ConfigureAwait(false);
            await using ClientContext reconnected = await m_fixture.ConnectAsync("mission-reconnected")
                .ConfigureAwait(false);
            RobotIntentControllerClient reconnectedController = await reconnected.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            IntentOperationSnapshot retained = await WaitForSnapshotAsync(
                reconnectedController,
                operation,
                snapshot => snapshot.Result.State == ExecutionStateEnum.Succeeded,
                "disconnected operation result retention").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(submission.Accepted, Is.True);
                Assert.That(outdated.Result, Is.EqualTo(MissionUpdateResultEnum.Outdated));
                Assert.That(baseConflict.Result, Is.EqualTo(MissionUpdateResultEnum.BaseConflict));
                Assert.That(MissionSignature(afterBaseConflict), Is.EqualTo(MissionSignature(beforeRejected)));
                Assert.That(accepted.Result, Is.EqualTo(MissionUpdateResultEnum.Accepted));
                Assert.That(advancedUpdateId, Is.EqualTo(1));
                Assert.That(replayAcceptedId.Result, Is.EqualTo(MissionUpdateResultEnum.Outdated));
                Assert.That(invalid.Result, Is.EqualTo(MissionUpdateResultEnum.Rejected));
                Assert.That(MissionSignature(afterInvalid), Is.EqualTo(MissionSignature(beforeInvalid)));
                Assert.That(disconnecting.Accepted, Is.True);
                Assert.That(retained.Result.IntentId, Is.EqualTo(disconnecting.IntentId));
                Assert.That(retained.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task TerminalResultsAreImmutableAcrossLateCommands()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("terminal-immutability").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(WaitIntent("immutable", 50))
                .ConfigureAwait(false);
            IntentResultDataType completion = await WaitForTerminalAsync(
                controller,
                submission.IntentId,
                submission.Operation).ConfigureAwait(false);
            IntentResultDataType firstRead = (await controller.Transport
                .ReadOperationSnapshotAsync(submission.Operation)
                .ConfigureAwait(false)).Result;
            IntentCommandOutcome lateCancel = await controller.Transport
                .CancelIntentAsync(submission.IntentId, StopModeEnum.QuickStop)
                .ConfigureAwait(false);
            IntentResultDataType afterCancel = (await controller.Transport
                .ReadOperationSnapshotAsync(submission.Operation)
                .ConfigureAwait(false)).Result;
            IntentSubmissionResult lateRetry = await controller.Transport.RetryAsync(submission.IntentId)
                .ConfigureAwait(false);
            IntentResultDataType afterRetry = (await controller.Transport
                .ReadOperationSnapshotAsync(submission.Operation)
                .ConfigureAwait(false)).Result;

            Assert.Multiple(() =>
            {
                Assert.That(submission.Accepted, Is.True);
                Assert.That(completion.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(ResultSignature(firstRead), Is.EqualTo(ResultSignature(completion)));
                Assert.That(lateCancel.Accepted, Is.False);
                Assert.That(ResultSignature(afterCancel), Is.EqualTo(ResultSignature(firstRead)));
                Assert.That(lateRetry.Accepted, Is.False);
                Assert.That(ResultSignature(afterRetry), Is.EqualTo(ResultSignature(firstRead)));
            });
        }

        [Test]
        public async Task CapabilityClaimsHaveCallableMethodSurface()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("method-surface").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);
            RobotIntentControllerInfo info = await controller.ReadAsync().ConfigureAwait(false);

            await AssertMethodExistsAsync(controller.Transport, "RequestControl").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "ReleaseControl").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "SubmitIntent").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "CancelIntent").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "CancelAll").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "Pause").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "Resume").ConfigureAwait(false);
            await AssertMethodExistsAsync(controller.Transport, "Retry").ConfigureAwait(false);
            IntentCommandOutcome pause = await controller.Transport.PauseAsync().ConfigureAwait(false);
            IntentCommandOutcome resume = await controller.Transport.ResumeAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(pause.Accepted, Is.True);
                Assert.That(resume.Accepted, Is.True);
            });
            if (info.MissionsSupported)
            {
                await AssertMethodExistsAsync(controller.Transport, "SubmitMission").ConfigureAwait(false);
                await AssertMethodExistsAsync(controller.Transport, "CancelMission").ConfigureAwait(false);
                MissionDataType mission = CreateMission("method-mission");
                MissionSubmissionResult submission = await controller.SubmitMissionAsync(mission)
                    .ConfigureAwait(false);
                Assert.That(submission.Accepted, Is.True);
            }
            if (info.MissionHorizonSupported)
            {
                await AssertMethodExistsAsync(controller.Transport, "UpdateMission").ConfigureAwait(false);
            }
            if (info.RealTimeChannelsSupported)
            {
                await AssertMethodExistsAsync(controller.Transport, "OpenRealTimeChannel").ConfigureAwait(false);
                await AssertMethodExistsAsync(controller.Transport, "CloseRealTimeChannel").ConfigureAwait(false);
                RealTimeChannelOpenResult lease = await controller.Transport
                    .OpenRealTimeChannelAsync(ChannelId, 100.0)
                    .ConfigureAwait(false);
                bool released = await controller.Transport.CloseRealTimeChannelAsync(ChannelId).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(lease.Granted, Is.True);
                    Assert.That(released, Is.True);
                });
            }
        }

        [Test]
        public async Task SafetyRefusalsAndOperationalModeAreObservedOverSession()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("safety").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.ApplySafety(new RiServer.SafetyStatus { ProtectiveStopActive = true });
            IntentSubmissionResult protectiveStop = await controller.TrySubmitIntentAsync(LinearIntent("protective"))
                .ConfigureAwait(false);
            m_fixture.ApplySafety(new RiServer.SafetyStatus { SafetyControllerOk = false });
            IntentSubmissionResult controllerFault = await controller.TrySubmitIntentAsync(LinearIntent("fault"))
                .ConfigureAwait(false);
            m_fixture.ApplySafety(new RiServer.SafetyStatus
            {
                SafeSpeedLimitActive = true,
                SafeSpeedLimit = 0.01
            });
            IntentSubmissionResult speedLimited = await controller.TrySubmitIntentAsync(LinearIntent("speed-limit"))
                .ConfigureAwait(false);
            m_fixture.ResetSafety();

            NodeId mode = await controller.Transport.ResolveChildAsync(
                controller.Transport.ControllerId,
                "OperationalMode")
                .ConfigureAwait(false);
            StatusCode writeStatus = await WriteValueAsync(
                context.Session,
                mode,
                (int)OperationalModeEnum.ManualReducedSpeed).ConfigureAwait(false);
            NodeId capabilities = await controller.Transport.ResolveChildAsync(
                controller.Transport.ControllerId,
                "Capabilities")
                .ConfigureAwait(false);
            Assert.That(capabilities.IsNull, Is.False, "Capabilities must be browsable.");
            NodeId supportedFacets = await controller.Transport.ResolveChildAsync(
                capabilities,
                "SupportedFacets")
                .ConfigureAwait(false);
            StatusCode supportedFacetsWriteStatus = await WriteValueAsync(
                context.Session,
                supportedFacets,
                Variant.From(s_stringValues1.ToArrayOf())).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(protectiveStop.Accepted, Is.False);
                Assert.That(protectiveStop.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(controllerFault.Accepted, Is.False);
                Assert.That(controllerFault.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(speedLimited.Accepted, Is.False);
                Assert.That(speedLimited.Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
                Assert.That(StatusCode.IsBad(writeStatus), Is.True);
                Assert.That(supportedFacets.IsNull, Is.False, "SupportedFacets must be browsable.");
                Assert.That(supportedFacetsWriteStatus.Code, Is.EqualTo(StatusCodes.BadNotWritable));
            });
        }

        [Test]
        public async Task ProtectiveStopDuringExecutionIsPublishedAndPreventsNewWork()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("safety-executing").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("safety-running");
            IntentSubmissionResult running = await controller.TrySubmitIntentAsync(WaitIntent("safety-running", 700))
                .ConfigureAwait(false);
            IntentOperationSnapshot executing;
            bool protectiveStop;
            bool ready;
            IntentSubmissionResult refused;
            IntentOperationSnapshot whileStopped;
            IntentResultDataType final;
            try
            {
                executing = await WaitForSnapshotAsync(
                    controller,
                    running.Operation,
                    snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                    "safety-running executing").ConfigureAwait(false);
                m_fixture.ApplySafety(new RiServer.SafetyStatus { ProtectiveStopActive = true });
                protectiveStop = await ReadBooleanPathAsync(
                    context.Session,
                    controller.Transport.ControllerId,
                    "SafetyState",
                    "ProtectiveStopActive").ConfigureAwait(false);
                ready = await ReadBooleanPathAsync(context.Session, controller.Transport.ControllerId, "Ready")
                    .ConfigureAwait(false);
                refused = await controller.TrySubmitIntentAsync(LinearIntent("blocked-by-stop"))
                    .ConfigureAwait(false);
                whileStopped = await controller.Transport
                    .ReadOperationSnapshotAsync(running.Operation)
                    .ConfigureAwait(false);
                m_fixture.ResetSafety();
                m_fixture.Executor.ReleaseCompletion("safety-running");
                final = await WaitForTerminalAsync(controller, running.IntentId, running.Operation)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_fixture.ResetSafety();
                m_fixture.Executor.ReleaseCompletion("safety-running");
            }

            Assert.Multiple(() =>
            {
                Assert.That(running.Accepted, Is.True);
                Assert.That(executing.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(protectiveStop, Is.True);
                Assert.That(ready, Is.False);
                Assert.That(refused.Accepted, Is.False);
                Assert.That(refused.Failure, Is.EqualTo(IntentFailureEnum.NotPermittedInMode));
                Assert.That(whileStopped.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(final.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task OpcUaCancelServiceDoesNotStopRunningIntent()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("service-cancel").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);
            m_fixture.Executor.HoldCompletion("service");
            await using IntentOperationHandle running = await controller.SubmitIntentAsync(WaitIntent("service", 700))
                .ConfigureAwait(false);
            CancelResponse response;
            IntentOperationSnapshot stillExecuting;
            IntentOperationSnapshot final;
            try
            {
                await WaitForStateAsync(running, ExecutionStateEnum.Executing).ConfigureAwait(false);

                response = await context.Session.CancelAsync(
                    requestHeader: null,
                    requestHandle: 0,
                    ct: CancellationToken.None).ConfigureAwait(false);
                stillExecuting = await controller.Transport
                    .ReadOperationSnapshotAsync(running.Operation)
                    .ConfigureAwait(false);
                m_fixture.Executor.ReleaseCompletion("service");
                final = await WaitForSnapshotAsync(
                    controller,
                    running.Operation,
                    snapshot => snapshot.Result.State == ExecutionStateEnum.Succeeded,
                    "service-cancel final result").ConfigureAwait(false);
            }
            finally
            {
                m_fixture.Executor.ReleaseCompletion("service");
            }

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(response.ResponseHeader.ServiceResult), Is.True);
                Assert.That(stillExecuting.ExecutionState, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(final.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task AbortingSupersedesRunningIntentAndHardIntentWaits()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("supersede-hard").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("superseded");
            IntentSubmissionResult first = await controller.TrySubmitIntentAsync(WaitIntent("superseded", 900))
                .ConfigureAwait(false);
            await WaitForSnapshotAsync(
                controller,
                first.Operation,
                snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                "first intent executing").ConfigureAwait(false);
            IntentSubmissionResult replacement = await controller.TrySubmitIntentAsync(WaitIntent("replacement", 100))
                .ConfigureAwait(false);
            IntentResultDataType superseded = await WaitForTerminalAsync(controller, first.IntentId, first.Operation)
                .ConfigureAwait(false);
            IntentResultDataType replacementResult = await WaitForTerminalAsync(
                controller,
                replacement.IntentId,
                replacement.Operation).ConfigureAwait(false);

            m_fixture.Executor.HoldCompletion("hard-head");
            IntentSubmissionResult hardHead = await controller.TrySubmitIntentAsync(WaitIntent("hard-head", 400))
                .ConfigureAwait(false);
            await WaitForSnapshotAsync(
                controller,
                hardHead.Operation,
                snapshot => snapshot.ExecutionState == ExecutionStateEnum.Executing,
                "hard head executing").ConfigureAwait(false);
            RiRobotIntent.WaitIntentDataType hardIntent = WaitIntent("hard", 50, BufferModeEnum.Buffered);
            hardIntent.BlockingMode = BlockingModeEnum.Hard;
            IntentSubmissionResult hard = await controller.TrySubmitIntentAsync(hardIntent).ConfigureAwait(false);
            IntentOperationSnapshot hardSnapshot = await WaitForSnapshotAsync(
                controller,
                hard.Operation,
                snapshot => snapshot.ExecutionState == ExecutionStateEnum.Queued,
                "hard intent queued behind running intent").ConfigureAwait(false);
            uint queuePosition = await ReadUInt32ChildAsync(
                context.Session,
                controller.Transport,
                hard.Operation,
                "QueuePosition")
                .ConfigureAwait(false);
            IntentOperationSnapshot hardBlockedSnapshot = await controller.Transport
                .ReadOperationSnapshotAsync(hard.Operation)
                .ConfigureAwait(false);
            m_fixture.Executor.ReleaseCompletion("hard-head");
            await WaitForTerminalAsync(controller, hardHead.IntentId, hardHead.Operation).ConfigureAwait(false);
            IntentResultDataType hardResult = await WaitForTerminalAsync(controller, hard.IntentId, hard.Operation)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Accepted, Is.True);
                Assert.That(replacement.Accepted, Is.True);
                Assert.That(superseded.State, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(superseded.Failure, Is.EqualTo(IntentFailureEnum.Superseded));
                Assert.That(replacementResult.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(hard.Accepted, Is.True);
                Assert.That(hardSnapshot.ExecutionState, Is.EqualTo(ExecutionStateEnum.Queued));
                Assert.That(queuePosition, Is.GreaterThanOrEqualTo(1));
                Assert.That(hardBlockedSnapshot.ExecutionState, Is.EqualTo(ExecutionStateEnum.Queued));
                Assert.That(hardResult.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task MissionsBranchAndApplyErrorPoliciesInObservableOrder()
        {
            await using ClientContext context = await m_fixture.ConnectAsync("mission-branches").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(controller)
                .ConfigureAwait(false);

            m_fixture.Executor.ClearCompleted();
            MissionSubmissionResult branch = await controller.SubmitMissionAsync(CreateBranchingMission("branch"))
                .ConfigureAwait(false);
            string[] branchOrder = await WaitForCompletedIntentIdsToSettleAsync(
                m_fixture.Executor,
                2,
                "branching mission to execute two steps").ConfigureAwait(false);
            ExecutionStateEnum branchState = await WaitForMissionStateAsync(
                context.Session,
                branch.Operation,
                ExecutionStateEnum.Succeeded).ConfigureAwait(false);

            m_fixture.Executor.ClearCompleted();
            MissionSubmissionResult fallback = await controller.SubmitMissionAsync(
                CreateErrorPolicyMission("fallback", ErrorPolicyEnum.Fallback)).ConfigureAwait(false);
            string[] fallbackOrder = await WaitForCompletedIntentIdsToSettleAsync(
                m_fixture.Executor,
                3,
                "fallback mission to execute three steps").ConfigureAwait(false);

            m_fixture.Executor.ClearCompleted();
            MissionSubmissionResult compensate = await controller.SubmitMissionAsync(
                CreateErrorPolicyMission("compensate", ErrorPolicyEnum.Compensate)).ConfigureAwait(false);
            string[] compensateOrder = await WaitForCompletedIntentIdsToSettleAsync(
                m_fixture.Executor,
                2,
                "compensate mission to execute two steps").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(branch.Accepted, Is.True);
                Assert.That(branchOrder, Is.EqualTo(["branch-start", "branch-first"]));
                Assert.That(branchState, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(fallback.Accepted, Is.True);
                Assert.That(
                    fallbackOrder,
                    Is.EqualTo(["fallback-fail", "fallback-recovery", "fallback-after"]));
                Assert.That(compensate.Accepted, Is.True);
                Assert.That(compensateOrder, Is.EqualTo(["compensate-fail", "compensate-recovery"]));
            });
        }

        [Test]
        public async Task AnnexBIntentControllerReferencesAreBrowsableWhenAttachedToMotionDeviceSystem()
        {
            await using var annexFixture = new TestServerFixture(includeRoboticsInterop: true);
            await annexFixture.StartAsync().ConfigureAwait(false);
            await using ClientContext context = await annexFixture.ConnectAsync("annex-b").ConfigureAwait(false);
            RobotIntentControllerClient controller = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            NodeId forward = await BrowseTargetAsync(
                context.Session,
                annexFixture.MotionDeviceSystemId,
                "HasIntentController",
                inverse: false).ConfigureAwait(false);
            NodeId inverse = await BrowseTargetAsync(
                context.Session,
                controller.Transport.ControllerId,
                "IntentControllerOf",
                inverse: true).ConfigureAwait(false);
            NodeId mode = await controller.Transport.ResolveChildAsync(
                controller.Transport.ControllerId,
                "OperationalMode")
                .ConfigureAwait(false);
            NodeId safetyStates = await BrowseChildByNameAsync(
                context.Session,
                annexFixture.MotionDeviceSystemId,
                "SafetyStates").ConfigureAwait(false);
            NodeId safety = await BrowseChildByNameAsync(
                context.Session,
                safetyStates,
                "Safety").ConfigureAwait(false);
            NodeId parameterSet = await BrowseChildByNameAsync(context.Session, safety, "ParameterSet")
                .ConfigureAwait(false);
            NodeId roboticsMode = await BrowseChildByNameAsync(context.Session, parameterSet, "OperationalMode")
                .ConfigureAwait(false);
            DataValue intentValue = await context.Session.ReadValueAsync(mode).ConfigureAwait(false);
            DataValue roboticsValue = await context.Session.ReadValueAsync(roboticsMode).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(forward.IsNull, Is.False);
                Assert.That(inverse.IsNull, Is.False);
                Assert.That(safetyStates.IsNull, Is.False);
                Assert.That(safety.IsNull, Is.False);
                Assert.That(parameterSet.IsNull, Is.False);
                Assert.That(roboticsMode.IsNull, Is.False);
                Assert.That(intentValue.WrappedValue.TryGetValue(out int actual), Is.True);
                Assert.That(roboticsValue.WrappedValue.TryGetValue(out int expected), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
            });
        }

        [Test]
        public async Task ScopedNodeIdsFromAnotherControllerAreRejected()
        {
            await using var twoControllerFixture = new TestServerFixture(includePeerController: true);
            await twoControllerFixture.StartAsync().ConfigureAwait(false);
            await using ClientContext context = await twoControllerFixture.ConnectAsync("scoped-nodeids").ConfigureAwait(false);
            RobotIntentControllerClient source = await context.GetControllerAsync(MainControllerName)
                .ConfigureAwait(false);
            RobotIntentControllerClient target = await context.GetControllerAsync(PeerControllerName)
                .ConfigureAwait(false);
            await using CommandAuthorityLease authority = await WaitForAuthorityAsync(target)
                .ConfigureAwait(false);
            RobotIntentControllerInfo sourceInfo = await source.ReadAsync().ConfigureAwait(false);

            IntentSubmissionResult result = await target.TrySubmitIntentAsync(new RiRobotIntent.SetOutputIntentDataType
            {
                IntentId = "foreign-output",
                Output = sourceInfo.Lookups.Outputs[0].NodeId,
                Value = new Variant(true)
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(result.Message.Text, Does.Contain("under this controller"));
            });
        }

        private static RiRobotIntent.LinearMoveIntentDataType LinearIntent(string id)
        {
            return new RiRobotIntent.LinearMoveIntentDataType
            {
                IntentId = id,
                BufferMode = BufferModeEnum.Aborting,
                BlockingMode = BlockingModeEnum.None,
                Target = Pose(0.1, 0.2, 0.3),
                Constraints = new RiRobotIntent.MotionConstraintsDataType { CartesianSpeed = 0.05 }
            };
        }

        private static RiRobotIntent.WaitIntentDataType WaitIntent(
            string id,
            double duration,
            BufferModeEnum bufferMode = BufferModeEnum.Aborting)
        {
            return new RiRobotIntent.WaitIntentDataType
            {
                IntentId = id,
                BufferMode = bufferMode,
                BlockingMode = BlockingModeEnum.None,
                Duration = duration
            };
        }

        private static RiRobotIntent.CallProgramIntentDataType CallProgramIntent(string id, NodeId program)
        {
            return new RiRobotIntent.CallProgramIntentDataType
            {
                IntentId = id,
                Program = program,
                BufferMode = BufferModeEnum.Aborting,
                BlockingMode = BlockingModeEnum.None,
                Arguments = []
            };
        }

        private static Pose3DDataType Pose(double x, double y, double z)
        {
            return new Pose3DDataType
            {
                FrameId = "world",
                Position = [x, y, z],
                Orientation = [0.0, 0.0, 0.0, 1.0]
            };
        }

        private static RiRobotIntent.IntentDataType CreateIntentFor(
            NodeId intentType,
            RobotIntentControllerInfo info)
        {
            if (IsDataType(intentType, RiRobotIntent.DataTypes.LinearMoveIntentDataType))
            {
                return LinearIntent("declared-linear");
            }
            if (IsDataType(intentType, RiRobotIntent.DataTypes.WaitIntentDataType))
            {
                return WaitIntent("declared-wait", 10);
            }
            if (IsDataType(intentType, RiRobotIntent.DataTypes.SetOutputIntentDataType))
            {
                return new RiRobotIntent.SetOutputIntentDataType
                {
                    IntentId = "declared-output",
                    Output = info.Lookups.Outputs[0].NodeId,
                    Value = new Variant(true)
                };
            }
            if (IsDataType(intentType, RiRobotIntent.DataTypes.CallProgramIntentDataType))
            {
                return new RiRobotIntent.CallProgramIntentDataType
                {
                    IntentId = "declared-program",
                    Program = info.Lookups.Programs[0].NodeId,
                    Arguments = []
                };
            }
            return new RiRobotIntent.GraspIntentDataType
            {
                IntentId = "declared-grasp",
                Tool = info.Lookups.Tools[0].NodeId,
                Force = 1.0
            };
        }

        private static bool IsDataType(NodeId nodeId, uint identifier)
        {
            return nodeId.TryGetValue(out uint actual) && actual == identifier;
        }

        private static NodeId StandardNode(uint identifier)
        {
            return new NodeId(identifier, 0);
        }

        private static MissionDataType CreateMission(string id)
        {
            return new MissionDataType
            {
                MissionId = id,
                MissionUpdateId = 0,
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "base",
                        SequenceId = 1,
                        Released = true,
                        Intent = WaitIntent("base", 500),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    },
                    new MissionStepDataType
                    {
                        StepId = "horizon",
                        SequenceId = 2,
                        Released = false,
                        Intent = WaitIntent("horizon", 50),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    }
                ],
                Transitions = []
            };
        }

        private static ArrayOf<MissionStepDataType> CreateBaseConflictSteps()
        {
            return
            [
                new MissionStepDataType
                {
                    StepId = "changed-base",
                    SequenceId = 1,
                    Released = true,
                    Intent = WaitIntent("changed-base", 10),
                    ErrorPolicy = ErrorPolicyEnum.Abort
                }
            ];
        }

        private static ArrayOf<MissionStepDataType> CreateAcceptedUpdateSteps()
        {
            return
            [
                new MissionStepDataType
                {
                    StepId = "base",
                    SequenceId = 1,
                    Released = true,
                    Intent = WaitIntent("base", 500),
                    ErrorPolicy = ErrorPolicyEnum.Abort
                },
                new MissionStepDataType
                {
                    StepId = "replacement",
                    SequenceId = 2,
                    Released = false,
                    Intent = WaitIntent("replacement", 50),
                    ErrorPolicy = ErrorPolicyEnum.Abort
                }
            ];
        }

        private static ArrayOf<MissionStepDataType> CreateRejectedUpdateSteps()
        {
            return
            [
                new MissionStepDataType
                {
                    StepId = "base",
                    SequenceId = 1,
                    Released = true,
                    Intent = WaitIntent("base", 500),
                    ErrorPolicy = ErrorPolicyEnum.Abort
                },
                new MissionStepDataType
                {
                    StepId = "base",
                    SequenceId = 2,
                    Released = false,
                    Intent = WaitIntent("duplicate-base", 50),
                    ErrorPolicy = ErrorPolicyEnum.Abort
                }
            ];
        }

        private static MissionDataType CreateBranchingMission(string id)
        {
            return new MissionDataType
            {
                MissionId = id,
                MissionUpdateId = 0,
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "start",
                        SequenceId = 1,
                        Released = true,
                        Intent = WaitIntent($"{id}-start", 10),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    },
                    new MissionStepDataType
                    {
                        StepId = "first",
                        SequenceId = 2,
                        Released = true,
                        Intent = WaitIntent($"{id}-first", 10),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    },
                    new MissionStepDataType
                    {
                        StepId = "second",
                        SequenceId = 3,
                        Released = true,
                        Intent = WaitIntent($"{id}-second", 10),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    }
                ],
                Transitions =
                [
                    new MissionTransitionDataType
                    {
                        FromStepId = "start",
                        ToStepId = "first",
                        DivergenceKind = DivergenceKindEnum.Alternative,
                        Condition = MissionCondition.Always()
                    },
                    new MissionTransitionDataType
                    {
                        FromStepId = "start",
                        ToStepId = "second",
                        DivergenceKind = DivergenceKindEnum.Alternative,
                        Condition = MissionCondition.Always()
                    }
                ]
            };
        }

        private static MissionDataType CreateErrorPolicyMission(string id, ErrorPolicyEnum policy)
        {
            return new MissionDataType
            {
                MissionId = id,
                MissionUpdateId = 0,
                Steps =
                [
                    new MissionStepDataType
                    {
                        StepId = "fail",
                        SequenceId = 1,
                        Released = true,
                        Intent = WaitIntent($"{id}-fail", 10),
                        ErrorPolicy = policy,
                        FallbackStepId = "recovery"
                    },
                    new MissionStepDataType
                    {
                        StepId = "recovery",
                        SequenceId = 2,
                        Released = true,
                        Intent = WaitIntent($"{id}-recovery", 10),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    },
                    new MissionStepDataType
                    {
                        StepId = "after",
                        SequenceId = 3,
                        Released = true,
                        Intent = WaitIntent($"{id}-after", 10),
                        ErrorPolicy = ErrorPolicyEnum.Abort
                    }
                ],
                Transitions = []
            };
        }

        private static async ValueTask AssertMethodExistsAsync(
            IRobotIntentTransport transport,
            string browseName)
        {
            NodeId method = await transport.ResolveChildAsync(transport.ControllerId, browseName)
                .ConfigureAwait(false);
            Assert.That(method.IsNull, Is.False, $"{browseName} must exist when the capability surface requires it.");
        }

        private static async ValueTask<NodeId> ReadProgramCurrentStateIdAsync(ISession session, NodeId operation)
        {
            NodeId currentState = await BrowseChildByNameAsync(session, operation, "CurrentState")
                .ConfigureAwait(false);
            Assert.That(currentState.IsNull, Is.False, "Operation CurrentState must be browsable.");
            NodeId id = await BrowseChildByNameAsync(session, currentState, "Id").ConfigureAwait(false);
            Assert.That(id.IsNull, Is.False, "Operation CurrentState/Id must be browsable.");
            DataValue value = await session.ReadValueAsync(id).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, "CurrentState/Id must be readable.");
            Assert.That(value.WrappedValue.TryGetValue(out NodeId stateId), Is.True);
            return stateId;
        }

        private static async ValueTask<MissionDataType> ReadMissionAsync(ISession session, NodeId missionOperation)
        {
            NodeId missionNode = await BrowseChildByNameAsync(session, missionOperation, "Mission")
                .ConfigureAwait(false);
            Assert.That(missionNode.IsNull, Is.False, "Mission must be browsable from the mission operation.");
            DataValue value = await session.ReadValueAsync(missionNode).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, "Mission must be readable.");
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable? encodeable), Is.True);
            return (MissionDataType)encodeable!;
        }

        private static async ValueTask<ExecutionStateEnum> ReadMissionStateAsync(
            ISession session,
            NodeId missionOperation)
        {
            NodeId stateNode = await BrowseChildByNameAsync(session, missionOperation, "ExecutionState")
                .ConfigureAwait(false);
            Assert.That(stateNode.IsNull, Is.False, "Mission ExecutionState must be browsable.");
            DataValue value = await session.ReadValueAsync(stateNode).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, "Mission ExecutionState must be readable.");
            Assert.That(value.WrappedValue.TryGetValue(out ExecutionStateEnum result), Is.True);
            return result;
        }

        private static async ValueTask<ExecutionStateEnum> WaitForMissionStateAsync(
            ISession session,
            NodeId missionOperation,
            ExecutionStateEnum state)
        {
            await WaitForAsync(
                async () => await ReadMissionStateAsync(session, missionOperation).ConfigureAwait(false) == state,
                $"mission to reach {state}").ConfigureAwait(false);
            return await ReadMissionStateAsync(session, missionOperation).ConfigureAwait(false);
        }

        private static async ValueTask<bool> ReadBooleanPathAsync(
            ISession session,
            NodeId root,
            params string[] browseNames)
        {
            NodeId nodeId = root;
            foreach (string browseName in browseNames)
            {
                nodeId = await BrowseChildByNameAsync(session, nodeId, browseName).ConfigureAwait(false);
                Assert.That(nodeId.IsNull, Is.False, $"{browseName} must be browsable.");
            }
            DataValue value = await session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"{browseNames[^1]} must be readable.");
            Assert.That(value.WrappedValue.TryGetValue(out bool result), Is.True);
            return result;
        }

        private static string MissionSignature(MissionDataType mission)
        {
            var stepSignatures = new List<string>();
            for (int ii = 0; ii < mission.Steps.Count; ii++)
            {
                MissionStepDataType step = mission.Steps[ii];
                stepSignatures.Add(string.Join(
                    ":",
                    step.StepId,
                    step.SequenceId,
                    step.Released,
                    step.Intent?.IntentId,
                    step.ErrorPolicy,
                    step.FallbackStepId));
            }
            return string.Join("|", stepSignatures) + FormattableString.Invariant($":{mission.MissionUpdateId}");
        }

        private static string ResultSignature(IntentResultDataType result)
        {
            return string.Join(
                "|",
                result.IntentId,
                result.State,
                result.Failure,
                result.Message.Text,
                result.HasAchievedPose,
                result.StartTime,
                result.EndTime,
                result.Outputs.Count);
        }

        private static ValueTask<StatusCode> WriteValueAsync(ISession session, NodeId nodeId, int value)
        {
            return WriteValueAsync(session, nodeId, Variant.From(value));
        }

        private static async ValueTask<StatusCode> WriteValueAsync(ISession session, NodeId nodeId, Variant value)
        {
            WriteResponse response = await session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new()
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value)
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            return response.Results.Count == 0 ? StatusCodes.BadUnexpectedError : response.Results[0];
        }

        private static async ValueTask<uint> ReadUInt32ChildAsync(
            ISession session,
            IRobotIntentTransport transport,
            NodeId parent,
            string browseName)
        {
            NodeId nodeId = await transport.ResolveChildAsync(parent, browseName).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"{browseName} must be readable.");
            Assert.That(value.WrappedValue.TryGetValue(out uint result), Is.True, $"{browseName} must be a UInt32.");
            return result;
        }

        private static async ValueTask<NodeId> BrowseTargetAsync(
            ISession session,
            NodeId nodeId,
            string referenceName,
            bool inverse)
        {
            int ns = session.NamespaceUris.GetIndex(RiRobotIntent.Namespaces.RobotIntent);
            NodeId referenceType = ns < 0
                ? NodeId.Null
                : new NodeId(RiRobotIntent.ReferenceTypes.HasIntentController, (ushort)ns);
            if (referenceType.IsNull)
            {
                return NodeId.Null;
            }
            ReferenceTypeNode referenceTypeNode = (ReferenceTypeNode)await session
                .ReadNodeAsync(referenceType, NodeClass.ReferenceType)
                .ConfigureAwait(false);
            if (inverse)
            {
                Assert.That(referenceTypeNode.InverseName.Text, Is.EqualTo(referenceName));
            }
            else
            {
                Assert.That(referenceTypeNode.BrowseName.Name, Is.EqualTo(referenceName));
            }
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new()
                    {
                        NodeId = nodeId,
                        BrowseDirection = inverse ? BrowseDirection.Inverse : BrowseDirection.Forward,
                        ReferenceTypeId = referenceType,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            BrowseResult result = response.Results.Count == 0 ? new BrowseResult() : response.Results[0];
            ReferenceDescription? target = null;
            for (int ii = 0; ii < result.References.Count; ii++)
            {
                ReferenceDescription reference = result.References[ii];
                if (!reference.NodeId.IsNull)
                {
                    target = reference;
                    break;
                }
            }
            return target == null
                ? NodeId.Null
                : ExpandedNodeId.ToNodeId(target.NodeId, session.NamespaceUris);
        }

        private static async ValueTask<IntentResultDataType> ReadFinalResultAsync(
            ISession session,
            IRobotIntentTransport transport,
            NodeId operation)
        {
            NodeId finalResultData = await BrowseChildByNameAsync(session, operation, "FinalResultData")
                .ConfigureAwait(false);
            Assert.That(finalResultData.IsNull, Is.False, "FinalResultData must be browsable from the operation.");
            NodeId resultNode = await BrowseChildByNameAsync(session, finalResultData, "Result").ConfigureAwait(false);
            Assert.That(resultNode.IsNull, Is.False, "FinalResultData/Result must be browsable.");
            DataValue value = await session.ReadValueAsync(resultNode).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable? encodeable), Is.True);
            return (IntentResultDataType)encodeable!;
        }

        private static async ValueTask<NodeId> BrowseChildByNameAsync(ISession session, NodeId root, string browseName)
        {
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new()
                    {
                        NodeId = root,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                return NodeId.Null;
            }
            for (int ii = 0; ii < response.Results[0].References.Count; ii++)
            {
                ReferenceDescription reference = response.Results[0].References[ii];
                if (reference.BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private static async ValueTask<IntentResultDataType> WaitForTerminalAsync(
            RobotIntentControllerClient controller,
            string intentId,
            NodeId operation)
        {
            IntentOperationSnapshot snapshot = await WaitForSnapshotAsync(
                controller,
                operation,
                candidate => RobotIntentRules.IsTerminal(candidate.Result.State),
                $"operation {intentId} terminal result").ConfigureAwait(false);
            return snapshot.Result;
        }

        private static async ValueTask WaitForStateAsync(
            IntentOperationHandle handle,
            ExecutionStateEnum state)
        {
            await WaitForAsync(
                () => handle.Current.ExecutionState == state,
                $"operation {handle.IntentId} to reach {state}").ConfigureAwait(false);
        }

        private static async ValueTask<IntentOperationSnapshot> WaitForSnapshotAsync(
            RobotIntentControllerClient controller,
            NodeId operation,
            Func<IntentOperationSnapshot, bool> predicate,
            string description)
        {
            IntentOperationSnapshot snapshot = new();
            await WaitForAsync(async () =>
            {
                snapshot = await controller.Transport.ReadOperationSnapshotAsync(operation).ConfigureAwait(false);
                return predicate(snapshot);
            }, description).ConfigureAwait(false);
            return snapshot;
        }

        private static async ValueTask<CommandAuthorityLease> WaitForAuthorityAsync(
            RobotIntentControllerClient controller)
        {
            CommandAuthorityLease? lease = null;
            await WaitForAsync(async () =>
            {
                lease = await controller.RequestAuthorityAsync().ConfigureAwait(false);
                if (!lease.Granted)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                return lease.Granted;
            }, "command authority after Session close").ConfigureAwait(false);
            return lease!;
        }

        private static async ValueTask<string[]> WaitForCompletedIntentIdsToSettleAsync(
            DeterministicExecutor executor,
            int expectedCount,
            string description)
        {
            await WaitForAsync(
                () => executor.CompletedIntentIds.Count >= expectedCount,
                description).ConfigureAwait(false);

            while (true)
            {
                int observedVersion = executor.CompletionVersion;
                string[] snapshot = executor.CompletedIntentIds.ToArray()!;
                using var noChange = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                try
                {
                    await executor.WaitForCompletionChangeAsync(observedVersion, noChange.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (noChange.IsCancellationRequested)
                {
                    return snapshot;
                }
            }
        }

        /// <summary>
        /// Waits for a gate, cancelling with the token. Task.WaitAsync does not exist on
        /// .NET Framework, which this suite also targets.
        /// </summary>
        private static async Task AwaitGateAsync(Task gate, CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
            {
                Task completed = await Task.WhenAny(gate, cancelled.Task).ConfigureAwait(false);
                if (completed != gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            await gate.ConfigureAwait(false);
        }

        /// <summary>
        /// Bounds a teardown step. Task.WaitAsync is unavailable on .NET Framework.
        /// </summary>
        private static async Task WithTimeoutAsync(Task task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task)
            {
                throw new TimeoutException();
            }
            await task.ConfigureAwait(false);
        }

        private static async ValueTask WaitForAsync(Func<bool> predicate, string description)
        {
            await WaitForAsync(() => new ValueTask<bool>(predicate()), description).ConfigureAwait(false);
        }

        private static async ValueTask WaitForAsync(Func<ValueTask<bool>> predicate, string description)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (await predicate().ConfigureAwait(false))
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail($"Timed out waiting for {description}.");
        }

        private const string MainControllerName = "CellController";
        private const string ManualControllerName = "ManualController";
        private const string PeerControllerName = "PeerController";
        private const string NoPauseControllerName = "NoPauseController";
        private const string ChannelId = "deterministic-channel";

        private TestServerFixture m_fixture = null!;

        private sealed class TestServerFixture : IAsyncDisposable
        {
            public TestServerFixture(bool includeRoboticsInterop = false, bool includePeerController = false)
            {
                m_includeRoboticsInterop = includeRoboticsInterop;
                m_includePeerController = includePeerController;
            }

            public string ServerUrl { get; private set; } = string.Empty;

            public DeterministicExecutor Executor => m_executor;

            public NodeId MotionDeviceSystemId { get; private set; } = NodeId.Null;

            public async ValueTask StartAsync()
            {
                int port = GetFreeTcpPort();
                ServerUrl = $"opc.tcp://localhost:{port}/RobotIntentIntegration";
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.AddConsole();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                builder.Services.AddSingleton<IIntentExecutor>(m_executor);
                IOpcUaServerBuilder serverBuilder = builder.Services
                    .AddOpcUa()
                    .AddServer(options =>
                    {
                        options.ApplicationName = "RobotIntentIntegrationServer";
                        options.ApplicationUri = "urn:localhost:OPCFoundation:RobotIntentIntegrationServer";
                        options.ProductUri = "uri:opcfoundation.org:RobotIntentIntegrationServer";
                        options.AutoAcceptUntrustedCertificates = true;
                        options.EndpointUrls.Add(ServerUrl);
                        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
                        {
                            TokenType = UserTokenType.Anonymous
                        });
                    })
                    .ConfigureRoles(options => options.Roles.Add(new Opc.Ua.Server.RoleDefinitionOptions
                    {
                        Name = "Operator",
                        Identities =
                        {
                            new Opc.Ua.Server.RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.Anonymous
                            }
                        }
                    }))
                    .AddRobotics()
                    .AddRobotIntent(options =>
                        options.InstanceNamespaceUri = "http://opcfoundation.org/UA/RobotIntent/")
                    .ConfigureRobotIntent(ConfigureAsync);
                if (m_includeRoboticsInterop)
                {
                    serverBuilder.ConfigureRobotics(ConfigureRoboticsAsync);
                }
                m_host = builder.Build();
                await m_host.StartAsync().ConfigureAwait(false);
                if (m_configurationException != null)
                {
                    throw new InvalidOperationException(
                        "Robot Intent test server configuration failed.",
                        m_configurationException);
                }
                m_clientConfig = await CreateClientConfigurationAsync().ConfigureAwait(false);
                await WaitForEndpointAsync().ConfigureAwait(false);
            }

            public async ValueTask<ClientContext> ConnectAsync(string name)
            {
                EndpointDescription? endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                    m_clientConfig,
                    ServerUrl,
                    useSecurity: false,
                    m_telemetry,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(endpointDescription, Is.Not.Null, "The test server endpoint must be discoverable.");
                var endpoint = new ConfiguredEndpoint(
                    null,
                    endpointDescription!,
                    EndpointConfiguration.Create(m_clientConfig));
                var sessionFactory = new DefaultSessionFactory(m_telemetry)
                {
                    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance
                };
                ISession session = await sessionFactory.CreateAsync(
                    m_clientConfig,
                    endpoint,
                    updateBeforeConnect: false,
                    sessionName: name,
                    sessionTimeout: 60000,
                    identity: new UserIdentity(new AnonymousIdentityToken()),
                    preferredLocales: default,
                    ct: CancellationToken.None).ConfigureAwait(false);
                if (!session.TryGetSubscriptionManager(out Opc.Ua.Client.Subscriptions.ISubscriptionManager? manager))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The integration session did not expose the V2 subscription manager.");
                }
                var streaming = new StreamingSubscription(manager);
                return new ClientContext(session, m_telemetry, streaming);
            }

            public void ResetSafety()
            {
                ApplySafety(RiServer.SafetyStatus.Nominal);
            }

            public void ApplySafety(RiServer.SafetyStatus status)
            {
                foreach ((RiServer.IntentControllerHost host, ISystemContext context) in m_hosts)
                {
                    host.UpdateSafetyState(context, status);
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (m_host != null)
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try
                    {
                        await m_host.StopAsync(stopCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }

            private async ValueTask ConfigureAsync(
                IRobotIntentBuildContext context,
                CancellationToken cancellationToken)
            {
                try
                {
                    await AddControllerAsync(
                        context,
                        MainControllerName,
                        OperationalModeEnum.AutomaticExternal,
                        cancellationToken).ConfigureAwait(false);
                    await AddControllerAsync(
                        context,
                        ManualControllerName,
                        OperationalModeEnum.ManualReducedSpeed,
                        cancellationToken).ConfigureAwait(false);
                    await AddControllerAsync(
                        context,
                        NoPauseControllerName,
                        OperationalModeEnum.AutomaticExternal,
                        cancellationToken,
                        pauseSupported: false).ConfigureAwait(false);
                    if (m_includePeerController)
                    {
                        await AddControllerAsync(
                            context,
                            PeerControllerName,
                            OperationalModeEnum.AutomaticExternal,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    m_configurationException = ex;
                    throw;
                }
            }

            private async ValueTask AddControllerAsync(
                IRobotIntentBuildContext context,
                string browseName,
                OperationalModeEnum mode,
                CancellationToken cancellationToken,
                bool pauseSupported = true)
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    browseName,
                    controller => ConfigureController(controller, mode, pauseSupported),
                    cancellationToken).ConfigureAwait(false);
                m_hosts.Add((builder.Host, context.Context));
                if (browseName == MainControllerName)
                {
                    m_mainController = builder;
                    LinkMotionDeviceSystem();
                }
            }

            private async ValueTask ConfigureRoboticsAsync(
                IRoboticsBuildContext context,
                CancellationToken cancellationToken)
            {
                m_motionDeviceSystem = await context.AddMotionDeviceSystemAsync(
                    "RobotIntentCell",
                    system =>
                    {
                        system.WithComponentName("Robot Intent Cell");
                        ISafetyStateBuilder safety = system.AddSafetyState("Safety", state => state
                            .WithComponentName("Safety")
                            .WithEmergencyStop(false)
                            .WithOperationalMode(OperationalModeEnumeration.AUTOMATIC)
                            .WithProtectiveStop(false));
                        IMotionDeviceBuilder device = system.AddMotionDevice("Robot", motion =>
                        {
                            motion
                                .WithCategory(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT)
                                .WithComponentName("Robot");
                            IPowerTrainBuilder powerTrain = motion.AddPowerTrain("PowerTrain", power => power
                                .WithComponentName("PowerTrain")
                                .AddMotor("Motor", motor => motor.WithMotorTemperature(20.0)));
                            motion.AddAxis("Axis1", axis => axis
                                .AsVirtual()
                                .WithActualPosition(0.0)
                                .Requires(powerTrain));
                        });
                        system.AddController("Controller", controller =>
                        {
                            controller.WithCurrentUser(user => user.WithLevel("Operator").WithName("operator"));
                            controller.WithComponentName("Controller");
                            controller.AddSoftware("Software");
                            controller.AddTaskControl("Task", task => task
                                .WithComponentName("Task")
                                .WithTaskProgramLoaded(false)
                                .WithTaskProgramName(string.Empty)
                                .Controls(device));
                            controller.Controls(device);
                            controller.UsesSafetyState(safety);
                        });
                    },
                    cancellationToken).ConfigureAwait(false);
                MotionDeviceSystemId = m_motionDeviceSystem.State.NodeId;
                LinkMotionDeviceSystem();
            }

            private void LinkMotionDeviceSystem()
            {
                if (m_motionDeviceSystem != null && m_mainController != null)
                {
                    m_motionDeviceSystem.HasIntentController(m_mainController);
                }
            }

            private static void ConfigureController(
                IIntentControllerBuilder controller,
                OperationalModeEnum mode,
                bool pauseSupported)
            {
                controller
                    .WithOperationalMode(mode)
                    .WithReady(true)
                    .WithMaxQueueDepth(2)
                    .WithSafetyState()
                    .Accepts<RiRobotIntent.LinearMoveIntentDataType>(pauseSupported: pauseSupported)
                    .Accepts<RiRobotIntent.WaitIntentDataType>(
                        pauseSupported: pauseSupported,
                        retrySupported: true)
                    .Accepts<RiRobotIntent.SetOutputIntentDataType>(pauseSupported: pauseSupported)
                    .Accepts<RiRobotIntent.CallProgramIntentDataType>(
                        cancelSupported: false,
                        pauseSupported: pauseSupported)
                    .Accepts<RiRobotIntent.GraspIntentDataType>(pauseSupported: pauseSupported);
                IIntentFrameBuilder world = controller.AddFrame("World", "world", FrameRoleEnum.World, Pose(0, 0, 0));
                IIntentFrameBuilder toolFrame = controller.AddFrame(
                    "ToolFrame",
                    "tool",
                    FrameRoleEnum.Tool,
                    Pose(0, 0, 0),
                    frame => frame.WithParent(world));
                controller.AddTool("Tool", toolFrame, fitted: true);
                controller.AddLocation("Bin", Pose(0.1, 0.0, 0.0), location => location.WithOccupancy(false));
                for (uint ii = 0; ii < 6; ii++)
                {
                    controller.AddAxis(FormattableString.Invariant($"J{ii + 1}"), ii, AxisKindEnum.Revolute);
                }
                controller.AddOutput("Output", global::Opc.Ua.DataTypeIds.Boolean, new Variant(false));
                controller.AddProgram("Program", "program");
                IIntentRealTimeChannelBuilder channel = controller.AddRealTimeChannel(
                    "DeterministicChannel",
                    ChannelId,
                    RealTimeTransportEnum.OpcUaFx,
                    "udp://239.0.0.1:4840");
                channel.State.NominalRate!.Value = 1000.0;
                channel.State.PayloadDescriptor!.Value = "joint-positions";
                channel.State.RequiredMode!.Value = OperationalModeEnum.AutomaticExternal;
                if (mode != OperationalModeEnum.AutomaticExternal)
                {
                    channel.State.Available!.Value = false;
                }
            }

            private async ValueTask<ApplicationConfiguration> CreateClientConfigurationAsync()
            {
                string pkiRoot = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "pki",
                    Guid.NewGuid().ToString("N"));
                var config = new ApplicationConfiguration(m_telemetry)
                {
                    ApplicationName = "RobotIntentIntegrationClient",
                    ApplicationUri = "urn:localhost:OPCFoundation:RobotIntentIntegrationClient",
                    ApplicationType = ApplicationType.Client,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "own"),
                            SubjectName = "CN=RobotIntentIntegrationClient, O=OPC Foundation"
                        },
                        TrustedIssuerCertificates = Store(Path.Combine(pkiRoot, "issuer")),
                        TrustedPeerCertificates = Store(Path.Combine(pkiRoot, "trusted")),
                        RejectedCertificateStore = Store(Path.Combine(pkiRoot, "rejected")),
                        AutoAcceptUntrustedCertificates = true
                    },
                    TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                    ClientConfiguration = new ClientConfiguration(),
                    ServerConfiguration = new ServerConfiguration()
                };
                await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
                var appInstance = new ApplicationInstance(config, m_telemetry);
                await appInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
                config.CertificateManager ??= CertificateManagerFactory.Create(
                    config.SecurityConfiguration,
                    m_telemetry);
                config.CertificateManager.AcceptError = static (_, _) => true;
                return config;
            }

            private static CertificateTrustList Store(string path)
            {
                return new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = path
                };
            }

            private async ValueTask WaitForEndpointAsync()
            {
                Exception? lastException = null;
                await WaitForAsync(async () =>
                {
                    try
                    {
                        EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                            m_clientConfig,
                            ServerUrl,
                            useSecurity: false,
                            m_telemetry,
                            CancellationToken.None).ConfigureAwait(false);
                        return endpoint != null;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        return false;
                    }
                }, $"server endpoint availability. Last error: {lastException?.Message}").ConfigureAwait(false);
            }

            private static int GetFreeTcpPort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private readonly bool m_includeRoboticsInterop;
            private readonly bool m_includePeerController;
            private readonly List<(RiServer.IntentControllerHost Host, ISystemContext Context)> m_hosts = [];
            private readonly DeterministicExecutor m_executor = new();

            private readonly ITelemetryContext m_telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));

            private Exception? m_configurationException;
            private IHost? m_host;
            private ApplicationConfiguration m_clientConfig = null!;
            private IIntentControllerBuilder? m_mainController;
            private IMotionDeviceSystemBuilder? m_motionDeviceSystem;
        }

        private sealed class ClientContext : IAsyncDisposable
        {
            public ClientContext(
                ISession session,
                ITelemetryContext telemetry,
                IStreamingSubscription streaming)
            {
                Session = session;
                m_telemetry = telemetry;
                m_streaming = streaming;
            }

            public ISession Session { get; }

            public async ValueTask<RobotIntentControllerClient> GetControllerAsync(string name)
            {
                var discovery = new RobotIntentClient(Session, m_telemetry, m_streaming);
                ArrayOf<RobotIntentNodeLookupEntry> controllers = await discovery.DiscoverControllersAsync()
                    .ConfigureAwait(false);
                RobotIntentNodeLookupEntry[] controllerEntries = controllers.ToArray()!;
                RobotIntentNodeLookupEntry? entry = controllerEntries.SingleOrDefault(
                    controller => controller.Name == name);
                Assert.That(
                    entry,
                    Is.Not.Null,
                    $"Controller '{name}' was not discovered. Controllers: " +
                    $"{string.Join(", ", controllerEntries.Select(c => c.Name))}.");
                return discovery.Controller(entry!.NodeId);
            }

            public async ValueTask DisposeAsync()
            {
                if (Session.Connected)
                {
                    await WithTimeoutAsync(Session.CloseAsync(1000, true), TimeSpan.FromSeconds(30))
                        .ConfigureAwait(false);
                }
                try
                {
                    await WithTimeoutAsync(m_streaming.DisposeAsync().AsTask(), TimeSpan.FromSeconds(30))
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
                Session.Dispose();
            }

            private readonly ITelemetryContext m_telemetry;
            private readonly IStreamingSubscription m_streaming;
        }

        private sealed class DeterministicExecutor : IIntentExecutor
        {
            public ArrayOf<string> CompletedIntentIds
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_completedIntentIds.ToArray().ToArrayOf();
                    }
                }
            }

            public int CompletionVersion
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_completionVersion;
                    }
                }
            }

            public void ClearCompleted()
            {
                lock (m_lock)
                {
                    m_completedIntentIds.Clear();
                    SignalCompletionChanged();
                }
            }

            public void HoldCompletion(string intentId)
            {
                lock (m_lock)
                {
                    m_completionGates[intentId] = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            public void HoldCancellationCompletion(string intentId)
            {
                lock (m_lock)
                {
                    m_cancellationGates[intentId] = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            public void ReleaseCompletion(string intentId)
            {
                TaskCompletionSource<bool>? gate = null;
                lock (m_lock)
                {
                    if (m_completionGates.TryGetValue(intentId, out gate))
                    {
                        m_completionGates.Remove(intentId);
                    }
                }
                gate?.TrySetResult(true);
            }

            public void ReleaseCancellationCompletion(string intentId)
            {
                TaskCompletionSource<bool>? gate = null;
                lock (m_lock)
                {
                    if (m_cancellationGates.TryGetValue(intentId, out gate))
                    {
                        m_cancellationGates.Remove(intentId);
                    }
                }
                gate?.TrySetResult(true);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return execution.Intent is not RiRobotIntent.CallProgramIntentDataType;
            }

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                const int steps = 5;
                for (int ii = 1; ii <= steps; ii++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await WaitForCancellationGateAsync(
                            execution.Intent.IntentId ?? string.Empty,
                            CancellationToken.None).ConfigureAwait(false);
                        AddCompleted(execution.Intent.IntentId ?? string.Empty);
                        return IntentOutcome.Success;
                    }
                    execution.Progress.ReportProgress((double)ii / steps);
                    execution.Progress.ReportPose(Pose(ii * 0.01, ii * 0.02, ii * 0.03));
                }
                string intentId = execution.Intent.IntentId ?? string.Empty;
                if (await WaitForCompletionOrCancellationAsync(intentId, cancellationToken).ConfigureAwait(false))
                {
                    await WaitForCancellationGateAsync(intentId, CancellationToken.None).ConfigureAwait(false);
                    AddCompleted(intentId);
                    return IntentOutcome.Success;
                }
                AddCompleted(intentId);
                if (intentId.StartsWith("retriable-", StringComparison.Ordinal))
                {
                    return IntentOutcome.Retriable(IntentFailureEnum.Other, "Deterministic retriable outcome.");
                }
                if (intentId.EndsWith("-fail", StringComparison.Ordinal))
                {
                    return IntentOutcome.Fail(IntentFailureEnum.Other, "Deterministic failure.");
                }
                return IntentOutcome.SucceededAt(Pose(0.05, 0.1, 0.15));
            }

            public Task WaitForCompletionChangeAsync(int observedVersion, CancellationToken cancellationToken)
            {
                Task gate;
                lock (m_lock)
                {
                    if (m_completionVersion != observedVersion)
                    {
                        return Task.CompletedTask;
                    }
                    gate = m_completionChanged.Task;
                }
                return AwaitGateAsync(gate, cancellationToken);
            }

            private void AddCompleted(string intentId)
            {
                lock (m_lock)
                {
                    m_completedIntentIds.Add(intentId);
                    SignalCompletionChanged();
                }
            }

            private void SignalCompletionChanged()
            {
                m_completionVersion++;
                m_completionChanged.TrySetResult(true);
                m_completionChanged = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private async ValueTask WaitForCancellationGateAsync(
                string intentId,
                CancellationToken cancellationToken)
            {
                TaskCompletionSource<bool>? gate;
                lock (m_lock)
                {
                    m_cancellationGates.TryGetValue(intentId, out gate);
                }
                if (gate != null)
                {
                    await AwaitGateAsync(gate.Task, cancellationToken).ConfigureAwait(false);
                }
            }

            private async ValueTask<bool> WaitForCompletionOrCancellationAsync(
                string intentId,
                CancellationToken cancellationToken)
            {
                TaskCompletionSource<bool>? gate;
                lock (m_lock)
                {
                    m_completionGates.TryGetValue(intentId, out gate);
                }
                if (gate == null)
                {
                    return false;
                }

                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(
                    static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                    cancelled))
                {
                    Task completed = await Task.WhenAny(gate.Task, cancelled.Task).ConfigureAwait(false);
                    return completed != gate.Task;
                }
            }

            private readonly System.Threading.Lock m_lock = new();
            private readonly List<string> m_completedIntentIds = [];
            private readonly Dictionary<string, TaskCompletionSource<bool>> m_completionGates = [];
            private readonly Dictionary<string, TaskCompletionSource<bool>> m_cancellationGates = [];
            private TaskCompletionSource<bool> m_completionChanged = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_completionVersion;
        }

        private static readonly string[] s_stringValues1 = ["RI-Fake"];
    }
}
