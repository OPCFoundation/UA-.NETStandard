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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;

namespace Opc.Ua.Robotics.Client.Tests
{
    [TestFixture]
    [Category("Robotics")]
    public sealed class IntentClientRuntimeTests
    {
        [TestCaseSource(nameof(TerminalExecutionStates))]
        public async Task OperationHandleCompletesOnTerminalStates(ExecutionStateEnum state)
        {
            FakeRobotIntentTransport transport = new()
            {
                Snapshot = Snapshot(state)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));

            IntentResultDataType result = await AwaitWithTimeoutAsync(handle.Completion, TimeSpan.FromSeconds(1));

            Assert.That(result.State, Is.EqualTo(state));
        }

        [Test]
        public async Task OperationHandleReadsInitialStateAfterSubscribing()
        {
            FakeRobotIntentTransport transport = new()
            {
                Snapshot = Snapshot(ExecutionStateEnum.Succeeded)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));

            Assert.That(await AwaitWithTimeoutAsync(handle.Completion, TimeSpan.FromSeconds(1)), Is.Not.Null);
            Assert.That(transport.SubscribeCount, Is.EqualTo(1));
            Assert.That(transport.ReadSnapshotCount, Is.EqualTo(1));
        }

        [Test]
        public async Task OperationHandleWaitsForResultAfterTerminalStateNotification()
        {
            FakeRobotIntentTransport transport = new()
            {
                Snapshot = new IntentOperationSnapshot
                {
                    Operation = new NodeId(10),
                    ExecutionState = ExecutionStateEnum.Executing
                }
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));
            transport.PublishChange("ExecutionState", Variant.From((int)ExecutionStateEnum.Succeeded));
            Task early = await Task.WhenAny(handle.Completion, Task.Delay(100)).ConfigureAwait(false);

            IntentResultDataType expected = new()
            {
                IntentId = "i1",
                State = ExecutionStateEnum.Succeeded
            };
            transport.PublishChange("Result", Variant.FromStructure(expected));
            IntentResultDataType result = await AwaitWithTimeoutAsync(handle.Completion, TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(early, Is.Not.SameAs(handle.Completion));
                Assert.That(result.IntentId, Is.EqualTo("i1"));
                Assert.That(result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
            });
        }

        [Test]
        public async Task OperationHandleRereadsAfterReconnect()
        {
            FakeRobotIntentTransport transport = new()
            {
                Snapshot = Snapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));
            transport.Snapshot = Snapshot(ExecutionStateEnum.Succeeded);
            transport.PublishReconnect();
            IntentResultDataType result = await AwaitWithTimeoutAsync(handle.Completion, TimeSpan.FromSeconds(1));

            Assert.That(result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
            Assert.That(transport.ReadSnapshotCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task CancelRefusalIsReturnedNotThrown()
        {
            FakeRobotIntentTransport transport = new()
            {
                CancelOutcome = new IntentCommandOutcome(false),
                Snapshot = Snapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));
            IntentCommandOutcome outcome = await handle.CancelAsync(StopModeEnum.QuickStop);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.False);
                Assert.That(transport.CancelIntentCount, Is.EqualTo(1));
                Assert.That(transport.LastCancelIntentId, Is.EqualTo("i1"));
                Assert.That(transport.LastCancelStopMode, Is.EqualTo(StopModeEnum.QuickStop));
            });
        }

        [Test]
        public async Task OperationHandleCommandMethodsDelegateToTransport()
        {
            FakeRobotIntentTransport transport = new()
            {
                PauseOutcome = new IntentCommandOutcome(false),
                ResumeOutcome = new IntentCommandOutcome(true),
                RetryResult = new IntentSubmissionResult
                {
                    Accepted = false,
                    IntentId = "i1",
                    Failure = IntentFailureEnum.Other,
                    Message = new LocalizedText("retry refused")
                },
                Snapshot = Snapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));

            IntentCommandOutcome pause = await handle.PauseAsync();
            IntentCommandOutcome resume = await handle.ResumeAsync();
            IntentSubmissionResult retry = await handle.RetryAsync();

            Assert.Multiple(() =>
            {
                Assert.That(pause.Accepted, Is.False);
                Assert.That(resume.Accepted, Is.True);
                Assert.That(retry.Accepted, Is.False);
                Assert.That(retry.Message.Text, Is.EqualTo("retry refused"));
                Assert.That(transport.PauseCount, Is.EqualTo(1));
                Assert.That(transport.ResumeCount, Is.EqualTo(1));
                Assert.That(transport.RetryCount, Is.EqualTo(1));
                Assert.That(transport.LastRetryIntentId, Is.EqualTo("i1"));
            });
        }

        [Test]
        public async Task CancellingDoesNotCompleteOperationHandle()
        {
            FakeRobotIntentTransport transport = new()
            {
                Snapshot = Snapshot(ExecutionStateEnum.Cancelling)
            };
            RobotIntentControllerClient controller = new(transport);

            await using IntentOperationHandle handle = await controller.TrackOperationAsync(
                "i1",
                new NodeId(10));
            Task completed = await Task.WhenAny(handle.Completion, Task.Delay(100));

            Assert.That(completed, Is.Not.SameAs(handle.Completion));
        }

        [Test]
        public async Task MissionHandleReadsInitialStateAfterSubscribing()
        {
            FakeRobotIntentTransport transport = new()
            {
                MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Succeeded)
            };
            RobotIntentControllerClient controller = new(transport);

            await using MissionHandle handle = await controller.TrackMissionAsync(
                "mission-1",
                new NodeId(20));

            MissionSnapshot terminal = await AwaitWithTimeoutAsync(handle.Completion, TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(terminal.ExecutionState, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(transport.SubscribeCount, Is.EqualTo(1));
                Assert.That(transport.ReadMissionSnapshotCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task MissionHandleRefreshesAfterReconnect()
        {
            FakeRobotIntentTransport transport = new()
            {
                MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using MissionHandle handle = await controller.TrackMissionAsync(
                "mission-1",
                new NodeId(20));
            transport.MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Failed) with
            {
                Failure = IntentFailureEnum.Other,
                FailureMessage = new LocalizedText("executor failure")
            };
            transport.PublishReconnect();

            MissionSnapshot terminal = await AwaitWithTimeoutAsync(handle.Completion, TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(terminal.ExecutionState, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(terminal.Failure, Is.EqualTo(IntentFailureEnum.Other));
                Assert.That(terminal.FailureMessage.Text, Is.EqualTo("executor failure"));
                Assert.That(transport.ReadMissionSnapshotCount, Is.GreaterThanOrEqualTo(2));
            });
        }

        [Test]
        public async Task MissionHandleTimeoutRemainsIncompleteWhenRefreshObservesTerminalState()
        {
            FakeRobotIntentTransport transport = new()
            {
                MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);

            await using MissionHandle handle = await controller.TrackMissionAsync(
                "mission-1",
                new NodeId(20));
            transport.MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Succeeded);

            MissionWaitResult result = await handle.WaitForCompletionAsync(TimeSpan.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.False);
                Assert.That(result.Current.ExecutionState, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(handle.Completion.IsCompleted, Is.True);
            });
        }

        [Test]
        public async Task MissionHandleCancellationDelegatesToTransport()
        {
            FakeRobotIntentTransport transport = new()
            {
                MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Executing),
                CancelMissionOutcome = new IntentCommandOutcome(false)
            };
            RobotIntentControllerClient controller = new(transport);

            await using MissionHandle handle = await controller.TrackMissionAsync(
                "mission-1",
                new NodeId(20));
            IntentCommandOutcome outcome = await handle.CancelAsync(StopModeEnum.QuickStop);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.False);
                Assert.That(transport.CancelMissionCount, Is.EqualTo(1));
                Assert.That(transport.LastCancelMissionId, Is.EqualTo("mission-1"));
                Assert.That(transport.LastCancelMissionStopMode, Is.EqualTo(StopModeEnum.QuickStop));
            });
        }

        [Test]
        public async Task MissionHandleDisposalStopsItsSubscription()
        {
            FakeRobotIntentTransport transport = new()
            {
                MissionSnapshot = MissionSnapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);
            MissionHandle handle = await controller.TrackMissionAsync("mission-1", new NodeId(20));
            await WaitUntilAsync(
                () => transport.ActiveSubscriptionCount == 1,
                TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            await handle.DisposeAsync();

            await WaitUntilAsync(
                () => transport.ActiveSubscriptionCount == 0,
                TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            Assert.That(transport.ActiveSubscriptionCount, Is.Zero);
        }

        [Test]
        public async Task AuthorityLeaseReleasesOnDisposeAndReportsLoss()
        {
            FakeRobotIntentTransport transport = new()
            {
                AuthorityOutcome = new CommandAuthorityOutcome(true, new NodeId(1)),
                ControlOwner = new NodeId(1)
            };
            RobotIntentControllerClient controller = new(transport);

            await using (CommandAuthorityLease lease = await controller.RequestAuthorityAsync())
            {
                transport.ControlOwner = new NodeId(2);
                transport.PublishOwner(new NodeId(2));
                await WaitUntilAsync(() => Equals(lease.CurrentOwner, new NodeId(2)), TimeSpan.FromSeconds(1))
                    .ConfigureAwait(false);

                Assert.That(lease.CurrentOwner, Is.EqualTo(new NodeId(2)));
            }
            Assert.That(transport.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AuthorityLeaseKeepsGrantWhenInitialNotificationIsOwnOwner()
        {
            NodeId owner = new(1);
            FakeRobotIntentTransport transport = new()
            {
                AuthorityOutcome = new CommandAuthorityOutcome(true, owner),
                ControlOwner = owner
            };
            RobotIntentControllerClient controller = new(transport);

            await using CommandAuthorityLease lease = await controller.RequestAuthorityAsync();
            int notifications = 0;
            lease.OwnerChanged += _ => Interlocked.Increment(ref notifications);
            transport.PublishOwner(owner);
            await WaitUntilAsync(() => Volatile.Read(ref notifications) > 0, TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(lease.Granted, Is.True);
                Assert.That(lease.CurrentOwner, Is.EqualTo(owner));
            });
        }

        [Test]
        public async Task AuthorityLeaseDisposeIsIdempotentAndReleasesOnce()
        {
            FakeRobotIntentTransport transport = new()
            {
                AuthorityOutcome = new CommandAuthorityOutcome(true, new NodeId(1)),
                ControlOwner = new NodeId(1)
            };
            RobotIntentControllerClient controller = new(transport);
            CommandAuthorityLease lease = await controller.RequestAuthorityAsync();

            await lease.DisposeAsync();
            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());

            Assert.That(transport.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AuthorityLeaseDisposeAfterClosedSessionDoesNotThrow()
        {
            FakeRobotIntentTransport transport = new()
            {
                AuthorityOutcome = new CommandAuthorityOutcome(true, new NodeId(1)),
                ControlOwner = new NodeId(1),
                ReleaseException = ServiceResultException.Create(StatusCodes.BadSessionClosed, "closed")
            };
            RobotIntentControllerClient controller = new(transport);
            CommandAuthorityLease lease = await controller.RequestAuthorityAsync();

            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());
            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());

            Assert.That(transport.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AuthorityLeaseDisposeAfterRefusalDoesNotRelease()
        {
            FakeRobotIntentTransport transport = new()
            {
                AuthorityOutcome = new CommandAuthorityOutcome(false, new NodeId(2)),
                ControlOwner = new NodeId(2)
            };
            RobotIntentControllerClient controller = new(transport);
            CommandAuthorityLease lease = await controller.RequestAuthorityAsync();

            await lease.DisposeAsync();
            await lease.DisposeAsync();

            Assert.That(transport.ReleaseCount, Is.Zero);
        }

        [Test]
        public async Task OperationHandleDisposeIsIdempotentDuringCallback()
        {
            FakeRobotIntentTransport transport = new()
            {
                Snapshot = Snapshot(ExecutionStateEnum.Executing)
            };
            RobotIntentControllerClient controller = new(transport);
            IntentOperationHandle handle = await controller.TrackOperationAsync("i1", new NodeId(10));
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var allowCallbackToExit = new ManualResetEventSlim(false);
            handle.Changed += _ =>
            {
                callbackEntered.TrySetResult(true);
                Assert.That(allowCallbackToExit.Wait(TimeSpan.FromSeconds(1)), Is.True);
            };

            transport.PublishChange(Variant.From((int)ExecutionStateEnum.Succeeded));
            await AwaitWithTimeoutAsync(callbackEntered.Task, TimeSpan.FromSeconds(1));
            Task dispose = handle.DisposeAsync().AsTask();
            allowCallbackToExit.Set();

            Assert.DoesNotThrowAsync(async () => await dispose);
            Assert.DoesNotThrowAsync(async () => await handle.DisposeAsync());
        }

        [Test]
        public async Task MissionUpdateRejectsNonIncreasingIdLocally()
        {
            FakeRobotIntentTransport transport = new();
            RobotIntentControllerClient controller = new(transport);
            MissionDataType mission = RobotIntentBuilder.Mission("m1")
                .WithMissionUpdateId(3)
                .HorizonStep("a", RobotIntentBuilder.Wait(1).Build())
                .Build();

            _ = await controller.SubmitMissionAsync(mission);

            MissionUpdateOutcome outcome = await controller.UpdateMissionAsync("m1", 3, []);

            Assert.That(outcome.Result, Is.EqualTo(MissionUpdateResultEnum.Outdated));
            Assert.That(transport.UpdateMissionCount, Is.Zero);
        }

        [Test]
        public async Task MissionUpdateIdsAreTrackedPerMission()
        {
            FakeRobotIntentTransport transport = new();
            RobotIntentControllerClient controller = new(transport);

            _ = await controller.SubmitMissionAsync(RobotIntentBuilder.Mission("a")
                .WithMissionUpdateId(5)
                .HorizonStep("a1", RobotIntentBuilder.Wait(1).Build())
                .Build());
            _ = await controller.SubmitMissionAsync(RobotIntentBuilder.Mission("b")
                .WithMissionUpdateId(1)
                .HorizonStep("b1", RobotIntentBuilder.Wait(1).Build())
                .Build());
            MissionUpdateOutcome accepted = await controller.UpdateMissionAsync("b", 2, []);
            MissionUpdateOutcome outdated = await controller.UpdateMissionAsync("a", 4, []);

            Assert.Multiple(() =>
            {
                Assert.That(accepted.Result, Is.EqualTo(MissionUpdateResultEnum.Accepted));
                Assert.That(outdated.Result, Is.EqualTo(MissionUpdateResultEnum.Outdated));
                Assert.That(transport.UpdateMissionCount, Is.EqualTo(1));
                Assert.That(transport.LastUpdateMissionId, Is.EqualTo("b"));
                Assert.That(transport.LastMissionUpdateId, Is.EqualTo(2));
            });
        }

        [Test]
        public void RobotIntentClientRegistersEncodeablesForSessionFactory()
        {
            var telemetry = new Mock<ITelemetryContext>();
            var sourceContext = ServiceMessageContext.Create(telemetry.Object);
            sourceContext.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/UA/RobotIntent/");
            sourceContext.Factory.Builder.AddOpcUaRobotIntent().Commit();
            var targetContext = ServiceMessageContext.Create(telemetry.Object);
            targetContext.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/UA/RobotIntent/");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Factory).Returns(targetContext.Factory);
            session.SetupGet(s => s.MessageContext).Returns(targetContext);

            Pose3DDataType pose = CreatePose();
            byte[] encoded = EncodeExtensionObject(sourceContext, new ExtensionObject(pose.BinaryEncodingId, pose));
            ExtensionObject undecoded = DecodeExtensionObject(targetContext, encoded);
            Assert.That(undecoded.TryGetValue(out Pose3DDataType? undecodedPose, targetContext), Is.False);

            _ = new RobotIntentClient(session.Object, telemetry.Object);
            _ = new RobotIntentClient(session.Object, telemetry.Object);

            ExtensionObject decoded = DecodeExtensionObject(targetContext, encoded);

            Assert.That(decoded.TryGetValue(out Pose3DDataType? decodedPose, targetContext), Is.True);
            Assert.That(decodedPose!.FrameId, Is.EqualTo("world"));
            Assert.That(decodedPose.Position.Span[0], Is.EqualTo(1.0d));
        }

        [Test]
        public async Task ChannelLeaseRenewsAndSurfacesRefusalMessage()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    EndpointUrl = "opc.tcp://rt",
                    PayloadDescriptor = "payload",
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddMilliseconds(30)),
                    Message = new LocalizedText("ok")
                }
            };

            await using RealTimeChannelLease lease = new(transport, "rt1", TimeSpan.FromMilliseconds(30));
            await lease.OpenAsync();
            await AwaitWithTimeoutAsync(transport.WaitForOpenChannelCountAsync(2), TimeSpan.FromSeconds(1));

            Assert.That(transport.OpenChannelCount, Is.GreaterThan(1));
            Assert.That(lease.EndpointUrl, Is.EqualTo("opc.tcp://rt"));

            transport.ChannelResult = transport.ChannelResult with
            {
                Granted = false,
                Message = new LocalizedText("busy")
            };
            await lease.RenewAsync();

            Assert.That(lease.Granted, Is.False);
            Assert.That(lease.Message.Text, Is.EqualTo("busy"));
        }

        [Test]
        public async Task ChannelLeaseRenewLoopRetriesAfterServiceFailure()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    EndpointUrl = "opc.tcp://rt",
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddMilliseconds(5))
                }
            };
            transport.EnqueueChannelResult(transport.ChannelResult);
            transport.EnqueueChannelFault(ServiceResultException.Create(StatusCodes.BadTimeout, "transient"));
            transport.EnqueueChannelResult(new RealTimeChannelOpenResult
            {
                Granted = true,
                EndpointUrl = "opc.tcp://rt2",
                LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddSeconds(1))
            });

            await using RealTimeChannelLease lease = new(transport, "rt1", TimeSpan.FromMilliseconds(30));
            await lease.OpenAsync();
            await WaitUntilAsync(() => transport.OpenChannelCount >= 3, TimeSpan.FromSeconds(2));

            Assert.That(transport.OpenChannelCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(lease.Granted, Is.True);
            Assert.That(lease.EndpointUrl, Is.EqualTo("opc.tcp://rt2"));
        }

        [Test]
        public async Task ChannelLeaseDisposeClosesAfterRenewFailure()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    EndpointUrl = "opc.tcp://rt",
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddMilliseconds(5))
                }
            };
            transport.EnqueueChannelResult(transport.ChannelResult);
            transport.EnqueueChannelFault(ServiceResultException.Create(StatusCodes.BadTimeout, "transient"));

            var lease = new RealTimeChannelLease(transport, "rt1", TimeSpan.FromMilliseconds(30));
            await lease.OpenAsync();
            await AwaitWithTimeoutAsync(transport.WaitForOpenChannelCountAsync(2), TimeSpan.FromSeconds(1));

            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());
            Assert.That(transport.CloseChannelCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ChannelLeaseDisposeIsIdempotentAndClosesOnce()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    EndpointUrl = "opc.tcp://rt",
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddSeconds(1))
                }
            };

            RealTimeChannelLease lease = new(transport, "rt1", TimeSpan.FromMilliseconds(30));
            await lease.OpenAsync();
            await lease.DisposeAsync();
            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());

            Assert.That(transport.CloseChannelCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ChannelLeaseDisposeAfterClosedSessionDoesNotThrow()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    EndpointUrl = "opc.tcp://rt",
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddSeconds(1))
                },
                CloseException = ServiceResultException.Create(StatusCodes.BadSessionClosed, "closed")
            };

            RealTimeChannelLease lease = new(transport, "rt1", TimeSpan.FromMilliseconds(30));
            await lease.OpenAsync();

            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());
            Assert.DoesNotThrowAsync(async () => await lease.DisposeAsync());
            Assert.That(transport.CloseChannelCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ChannelLeaseDisposeAfterRefusalDoesNotClose()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = false,
                    Message = new LocalizedText("busy")
                }
            };

            RealTimeChannelLease lease = new(transport, "rt1", TimeSpan.FromMilliseconds(30));
            await lease.OpenAsync();
            await lease.DisposeAsync();
            await lease.DisposeAsync();

            Assert.That(transport.CloseChannelCount, Is.Zero);
        }

        [Test]
        public async Task SubmitIntentRefusalThrowsAndTrySubmitReturnsRefusal()
        {
            FakeRobotIntentTransport transport = new()
            {
                SubmitResult = new IntentSubmissionResult
                {
                    Accepted = false,
                    Failure = IntentFailureEnum.QueueFull,
                    Message = new LocalizedText("busy")
                }
            };
            RobotIntentControllerClient controller = new(transport);

            IntentDataType throwingIntent = RobotIntentBuilder.Wait(1).Build();
            IntentDataType refusalAwareIntent = RobotIntentBuilder.Wait(2).Build();

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await controller.SubmitIntentAsync(throwingIntent));
            IntentSubmissionResult result = await controller.TrySubmitIntentAsync(refusalAwareIntent);

            Assert.Multiple(() =>
            {
                Assert.That(ex?.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.QueueFull));
                Assert.That(transport.SubmitIntentCount, Is.EqualTo(2));
                Assert.That(transport.LastSubmittedIntent, Is.SameAs(refusalAwareIntent));
            });
        }

        [Test]
        public async Task ControllerMissionAndCancelMethodsDelegateToTransport()
        {
            FakeRobotIntentTransport transport = new()
            {
                CancelMissionOutcome = new IntentCommandOutcome(false)
            };
            RobotIntentControllerClient controller = new(transport);
            MissionDataType mission = RobotIntentBuilder.Mission("m1")
                .WithMissionUpdateId(4)
                .HorizonStep("a", RobotIntentBuilder.Wait(1).Build())
                .Build();

            MissionSubmissionResult submit = await controller.SubmitMissionAsync(mission);
            MissionUpdateOutcome update = await controller.UpdateMissionAsync("m1", 5, []);
            IntentCommandOutcome cancel = await controller.CancelMissionAsync("m1", StopModeEnum.QuickStop);

            Assert.Multiple(() =>
            {
                Assert.That(submit.Accepted, Is.True);
                Assert.That(update.Result, Is.EqualTo(MissionUpdateResultEnum.Accepted));
                Assert.That(cancel.Accepted, Is.False);
                Assert.That(transport.SubmitMissionCount, Is.EqualTo(1));
                Assert.That(transport.LastSubmittedMission, Is.SameAs(mission));
                Assert.That(transport.UpdateMissionCount, Is.EqualTo(1));
                Assert.That(transport.LastUpdateMissionId, Is.EqualTo("m1"));
                Assert.That(transport.LastMissionUpdateId, Is.EqualTo(5));
                Assert.That(transport.LastUpdateSteps.Count, Is.Zero);
                Assert.That(transport.CancelMissionCount, Is.EqualTo(1));
                Assert.That(transport.LastCancelMissionId, Is.EqualTo("m1"));
                Assert.That(transport.LastCancelMissionStopMode, Is.EqualTo(StopModeEnum.QuickStop));
            });
        }

        [Test]
        public async Task ChannelLeaseExpiredLeaseUsesNonZeroRenewDelay()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddSeconds(-1))
                }
            };

            await using RealTimeChannelLease lease = new(transport, "rt1", TimeSpan.FromSeconds(1));
            await lease.OpenAsync();
            TimeSpan delay = InvokeComputeRenewDelay(lease);

            Assert.That(delay, Is.GreaterThan(TimeSpan.FromMilliseconds(250)));
        }

        [Test]
        public async Task ControllerOpensRealTimeChannelLease()
        {
            FakeRobotIntentTransport transport = new()
            {
                ChannelResult = new RealTimeChannelOpenResult
                {
                    Granted = true,
                    EndpointUrl = "opc.tcp://rt",
                    LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddSeconds(1))
                }
            };
            RobotIntentControllerClient controller = new(transport);

            await using RealTimeChannelLease lease = await controller.OpenRealTimeChannelAsync(
                "rt1",
                TimeSpan.FromSeconds(1));

            Assert.Multiple(() =>
            {
                Assert.That(lease.Granted, Is.True);
                Assert.That(lease.EndpointUrl, Is.EqualTo("opc.tcp://rt"));
                Assert.That(transport.OpenChannelCount, Is.EqualTo(1));
                Assert.That(transport.LastOpenChannelId, Is.EqualTo("rt1"));
                Assert.That(transport.LastRequestedLease, Is.EqualTo(1000.0).Within(1e-9));
            });
        }

        [Test]
        public void RobotIntentExtensionMethodsCreateClientsAndValidateNulls()
        {
            Mock<ISession> session = CreateSession();
            var telemetry = new Mock<ITelemetryContext>();
            var streaming = new Mock<IStreamingSubscription>();

            RobotIntentClient fromSession = session.Object.RobotIntent(telemetry.Object, streaming.Object);
            var robotics = new RoboticsClient(session.Object, telemetry.Object);
            RobotIntentClient fromRobotics = robotics.RobotIntent(streaming.Object);
            RobotIntentControllerClient controller = robotics.RobotIntentController(new NodeId(123), streaming.Object);

            Assert.That(fromSession, Is.Not.Null);
            Assert.That(fromRobotics, Is.Not.Null);
            Assert.That(controller.ControllerId, Is.EqualTo(new NodeId(123)));
            Assert.Throws<ArgumentNullException>(() => SessionRobotIntentExtensions.RobotIntent(
                null!,
                telemetry.Object));
            Assert.Throws<ArgumentNullException>(() => session.Object.RobotIntent(null!));
        }

        [Test]
        public void RobotIntentDiRegistrationIsIdempotent()
        {
            var services = new ServiceCollection();
            var builder = new TestClientBuilder(services);

            builder.AddRobotIntentClient();
            builder.AddRobotIntentClient();
            builder.AddRoboticsClient();
            builder.AddRoboticsClient();

            Assert.That(CountService<Func<CancellationToken, Task<RobotIntentClient>>>(services), Is.EqualTo(1));
            Assert.That(CountService<RoboticsClientFactory>(services), Is.EqualTo(1));
            Assert.That(CountService<Func<CancellationToken, Task<RoboticsClient>>>(services), Is.EqualTo(1));
        }

        [Test]
        public void RobotIntentDiFactoriesResolveWhenTheSessionFactoryIsRegistered()
        {
            var services = new ServiceCollection();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            services.AddSingleton(telemetry);
            Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                _ => throw new InvalidOperationException("not connected");
            services.AddSingleton(sessionFactory);
            var builder = new TestClientBuilder(services);

            builder.AddRobotIntentClient();
            builder.AddRoboticsClient();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(
                    provider.GetRequiredService<Func<CancellationToken, Task<RobotIntentClient>>>(),
                    Is.Not.Null);
                Assert.That(provider.GetRequiredService<RoboticsClientFactory>(), Is.Not.Null);
                Assert.That(
                    provider.GetRequiredService<Func<CancellationToken, Task<RoboticsClient>>>(),
                    Is.Not.Null);
            });
        }

        [Test]
        public void RobotIntentDiFactoriesExplainThatAddClientMustComeFirst()
        {
            var services = new ServiceCollection();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            services.AddSingleton(telemetry);
            var builder = new TestClientBuilder(services);

            builder.AddRobotIntentClient();
            builder.AddRoboticsClient();

            using ServiceProvider provider = services.BuildServiceProvider();

            InvalidOperationException? intent = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<Func<CancellationToken, Task<RobotIntentClient>>>());
            InvalidOperationException? robotics = Assert.Throws<InvalidOperationException>(
                () => provider.GetRequiredService<RoboticsClientFactory>());

            Assert.Multiple(() =>
            {
                Assert.That(intent!.Message, Does.Contain("AddClient"));
                Assert.That(robotics!.Message, Does.Contain("AddClient"));
            });
        }

        [Test]
        public void RobotIntentDiRegistrationRejectsANullBuilder()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaRoboticsClientBuilderExtensions.AddRobotIntentClient(null!));
                Assert.Throws<ArgumentNullException>(
                    () => OpcUaRoboticsClientBuilderExtensions.AddRoboticsClient(null!));
            });
        }

        private static TimeSpan InvokeComputeRenewDelay(RealTimeChannelLease lease)
        {
            MethodInfo? method = typeof(RealTimeChannelLease).GetMethod(
                "ComputeRenewDelay",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (TimeSpan)method!.Invoke(lease, [])!;
        }

        private static async Task AwaitWithTimeoutAsync(Task task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task));
            await task.ConfigureAwait(false);
        }

        private static async Task<T> AwaitWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(task));
            return await task.ConfigureAwait(false);
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!predicate() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.That(predicate(), Is.True);
        }

        private static int CountService<T>(IServiceCollection services)
        {
            int count = 0;
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(T))
                {
                    count++;
                }
            }
            return count;
        }

        private static Mock<ISession> CreateSession()
        {
            var telemetry = new Mock<ITelemetryContext>();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry.Object);
            context.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/UA/RobotIntent/");
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(context);
            session.SetupGet(s => s.Factory).Returns(context.Factory);
            return session;
        }

        private static Pose3DDataType CreatePose()
        {
            return new Pose3DDataType
            {
                FrameId = "world",
                Position = ArrayOf.Create([1.0d, 2.0d, 3.0d]),
                Orientation = ArrayOf.Create([0.0d, 0.0d, 0.0d, 1.0d])
            };
        }

        private static byte[] EncodeExtensionObject(
            IServiceMessageContext context,
            ExtensionObject extensionObject)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
            {
                encoder.WriteExtensionObject("Value", extensionObject);
            }
            return stream.ToArray();
        }

        private static ExtensionObject DecodeExtensionObject(IServiceMessageContext context, byte[] encoded)
        {
            using var stream = new MemoryStream(encoded);
            using var decoder = new BinaryDecoder(stream, context);
            return decoder.ReadExtensionObject("Value");
        }

        private static IntentOperationSnapshot Snapshot(ExecutionStateEnum state)
        {
            return new IntentOperationSnapshot
            {
                Operation = new NodeId(10),
                ExecutionState = state,
                Result = new IntentResultDataType
                {
                    IntentId = "i1",
                    State = state
                }
            };
        }

        private static MissionSnapshot MissionSnapshot(ExecutionStateEnum state)
        {
            return new MissionSnapshot
            {
                MissionNode = new NodeId(20),
                MissionId = "mission-1",
                ExecutionState = state
            };
        }

        private static IEnumerable<TestCaseData> TerminalExecutionStates()
        {
            yield return new TestCaseData(ExecutionStateEnum.Succeeded);
            yield return new TestCaseData(ExecutionStateEnum.Failed);
            yield return new TestCaseData(ExecutionStateEnum.Cancelled);
            yield return new TestCaseData(ExecutionStateEnum.Retriable);
        }

        private sealed class TestClientBuilder(IServiceCollection services) : IOpcUaClientBuilder
        {
            public IServiceCollection Services { get; } = services;
        }

        private sealed class FakeRobotIntentTransport : IRobotIntentTransport, IDisposable
        {
            public event RobotIntentReconnectHandler? Reconnected;

            public NodeId ControllerId { get; } = new(100);

            public NamespaceTable NamespaceUris { get; } = new();

            public IServiceMessageContext MessageContext { get; } =
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create(true));

            public ILogger Logger { get; } = NullLogger.Instance;

            public IntentOperationSnapshot Snapshot { get; set; } = new();

            public RobotIntentControllerState ControllerState { get; set; } = new();

            public ArrayOf<IntentOperationSnapshot> Operations { get; set; } = [];

            public ArrayOf<MissionSnapshot> Missions { get; set; } = [];

            public IntentCommandOutcome CancelOutcome { get; set; } = new(true);

            public IntentSubmissionResult SubmitResult { get; set; } = new()
            {
                Accepted = true,
                IntentId = "i1",
                Operation = new NodeId(10)
            };

            public IntentCommandOutcome PauseOutcome { get; set; } = new(true);

            public IntentCommandOutcome ResumeOutcome { get; set; } = new(true);

            public IntentSubmissionResult RetryResult { get; set; } = new() { Accepted = true };

            public IntentCommandOutcome CancelMissionOutcome { get; set; } = new(true);

            public CommandAuthorityOutcome AuthorityOutcome { get; set; } = new(false, NodeId.Null);

            public NodeId ControlOwner { get; set; } = NodeId.Null;

            public RealTimeChannelOpenResult ChannelResult { get; set; } = new()
            {
                Granted = true,
                LeaseExpiry = new DateTimeUtc(DateTime.UtcNow.AddSeconds(1))
            };

            public int SubscribeCount { get; private set; }

            public int ReadSnapshotCount { get; private set; }

            public int ReadMissionSnapshotCount { get; private set; }

            public int ActiveSubscriptionCount => Volatile.Read(ref m_activeSubscriptionCount);

            public int ReleaseCount { get; private set; }

            public int UpdateMissionCount { get; private set; }

            public int OpenChannelCount { get; private set; }

            public int CloseChannelCount { get; private set; }

            public int PauseCount { get; private set; }

            public int ResumeCount { get; private set; }

            public int RetryCount { get; private set; }

            public int CancelMissionCount { get; private set; }

            public int CancelAllCount { get; private set; }

            public int SubmitIntentCount { get; private set; }

            public int CancelIntentCount { get; private set; }

            public int SubmitMissionCount { get; private set; }

            public IntentDataType? LastSubmittedIntent { get; private set; }

            public string? LastCancelIntentId { get; private set; }

            public StopModeEnum LastCancelStopMode { get; private set; }

            public string? LastRetryIntentId { get; private set; }

            public MissionDataType? LastSubmittedMission { get; private set; }

            public string? LastUpdateMissionId { get; private set; }

            public uint LastMissionUpdateId { get; private set; }

            public ArrayOf<MissionStepDataType> LastUpdateSteps { get; private set; } = [];

            public string? LastCancelMissionId { get; private set; }

            public StopModeEnum LastCancelMissionStopMode { get; private set; }

            public string? LastOpenChannelId { get; private set; }

            public double LastRequestedLease { get; private set; }

            public string? LastCloseChannelId { get; private set; }

            public Queue<Func<RealTimeChannelOpenResult>> ChannelResponses { get; } = new();

            public Exception? ReleaseException { get; set; }

            public Exception? CloseException { get; set; }

            private ConcurrentQueue<RobotIntentDataChange> ChangeNotifications { get; } = new();

            public Task WaitForOpenChannelCountAsync(int count)
            {
                lock (m_stateLock)
                {
                    if (OpenChannelCount >= count)
                    {
                        return Task.CompletedTask;
                    }
                    m_openChannelCountTarget = count;
                    m_openChannelCountReached = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    return m_openChannelCountReached.Task;
                }
            }

            public void EnqueueChannelResult(RealTimeChannelOpenResult result)
            {
                ChannelResponses.Enqueue(() => result);
            }

            public void EnqueueChannelFault(Exception exception)
            {
                ChannelResponses.Enqueue(() => throw exception);
            }

            public void PublishOwner(NodeId owner)
            {
                m_ownerNotifications.Enqueue(owner);
                m_notificationAvailable.Release();
            }

            public void PublishChange(Variant value)
            {
                PublishChange("ExecutionState", value);
            }

            public void PublishChange(string browseName, Variant value)
            {
                ChangeNotifications.Enqueue(new RobotIntentDataChange(ChildNode(browseName), value));
                m_notificationAvailable.Release();
            }

            public void PublishReconnect()
            {
                Reconnected?.Invoke();
            }

            public void Dispose()
            {
                m_notificationAvailable.Dispose();
            }

            public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<RobotIntentNodeLookupEntry>>([]);
            }

            public ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
            {
                return new ValueTask<RobotIntentControllerInfo>(new RobotIntentControllerInfo());
            }

            public ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default)
            {
                return new ValueTask<RobotIntentControllerState>(ControllerState);
            }

            public ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<IntentOperationSnapshot>>(Operations);
            }

            public ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<MissionSnapshot>>(Missions);
            }

            public ValueTask<IntentSubmissionResult> SubmitIntentAsync(
                IntentDataType intent,
                CancellationToken ct = default)
            {
                SubmitIntentCount++;
                LastSubmittedIntent = intent;
                return new ValueTask<IntentSubmissionResult>(SubmitResult);
            }

            public ValueTask<IntentCommandOutcome> CancelIntentAsync(
                string intentId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                CancelIntentCount++;
                LastCancelIntentId = intentId;
                LastCancelStopMode = stopMode;
                return new ValueTask<IntentCommandOutcome>(CancelOutcome);
            }

            public ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default)
            {
                CancelAllCount++;
                return new ValueTask<uint>(2u);
            }

            public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default)
            {
                PauseCount++;
                return new ValueTask<IntentCommandOutcome>(PauseOutcome);
            }

            public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default)
            {
                ResumeCount++;
                return new ValueTask<IntentCommandOutcome>(ResumeOutcome);
            }

            public ValueTask<IntentSubmissionResult> RetryAsync(string intentId, CancellationToken ct = default)
            {
                RetryCount++;
                LastRetryIntentId = intentId;
                return new ValueTask<IntentSubmissionResult>(RetryResult);
            }

            public ValueTask<MissionSubmissionResult> SubmitMissionAsync(
                MissionDataType mission,
                CancellationToken ct = default)
            {
                SubmitMissionCount++;
                LastSubmittedMission = mission;
                return new ValueTask<MissionSubmissionResult>(new MissionSubmissionResult
                {
                    Accepted = true,
                    MissionId = mission.MissionId ?? string.Empty,
                    Operation = new NodeId(20)
                });
            }

            public ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
                string missionId,
                uint missionUpdateId,
                ArrayOf<MissionStepDataType> steps,
                CancellationToken ct = default)
            {
                UpdateMissionCount++;
                LastUpdateMissionId = missionId;
                LastMissionUpdateId = missionUpdateId;
                LastUpdateSteps = steps;
                return new ValueTask<MissionUpdateOutcome>(new MissionUpdateOutcome(
                    MissionUpdateResultEnum.Accepted,
                    LocalizedText.Null));
            }

            public ValueTask<IntentCommandOutcome> CancelMissionAsync(
                string missionId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                CancelMissionCount++;
                LastCancelMissionId = missionId;
                LastCancelMissionStopMode = stopMode;
                return new ValueTask<IntentCommandOutcome>(CancelMissionOutcome);
            }

            public ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default)
            {
                return new ValueTask<CommandAuthorityOutcome>(AuthorityOutcome);
            }

            public ValueTask ReleaseControlAsync(CancellationToken ct = default)
            {
                ReleaseCount++;
                if (ReleaseException != null)
                {
                    throw ReleaseException;
                }
                return default;
            }

            public ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
                string channelId,
                double requestedLease,
                CancellationToken ct = default)
            {
                lock (m_stateLock)
                {
                    OpenChannelCount++;
                    LastOpenChannelId = channelId;
                    LastRequestedLease = requestedLease;
                    if (OpenChannelCount >= m_openChannelCountTarget)
                    {
                        m_openChannelCountReached?.TrySetResult(true);
                    }
                }
                if (ChannelResponses.Count > 0)
                {
                    return new ValueTask<RealTimeChannelOpenResult>(ChannelResponses.Dequeue()());
                }
                return new ValueTask<RealTimeChannelOpenResult>(ChannelResult);
            }

            public ValueTask<bool> CloseRealTimeChannelAsync(string channelId, CancellationToken ct = default)
            {
                CloseChannelCount++;
                LastCloseChannelId = channelId;
                if (CloseException != null)
                {
                    throw CloseException;
                }
                return new ValueTask<bool>(true);
            }

            public ValueTask<NodeId> ResolveChildAsync(NodeId root, string browseName, CancellationToken ct = default)
            {
                return new ValueTask<NodeId>(ChildNode(browseName));
            }

            public ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(
                NodeId operation,
                CancellationToken ct = default)
            {
                ReadSnapshotCount++;
                return new ValueTask<IntentOperationSnapshot>(Snapshot);
            }

            public MissionSnapshot MissionSnapshot { get; set; } = new();

            public ValueTask<MissionSnapshot> ReadMissionSnapshotAsync(
                NodeId mission,
                CancellationToken ct = default)
            {
                ReadMissionSnapshotCount++;
                return new ValueTask<MissionSnapshot>(MissionSnapshot with { MissionNode = mission });
            }

            public ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
            {
                return new ValueTask<NodeId>(ControlOwner);
            }

            public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
                ArrayOf<NodeId> nodeIds,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                SubscribeCount++;
                Interlocked.Increment(ref m_activeSubscriptionCount);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await m_notificationAvailable.WaitAsync(ct).ConfigureAwait(false);
                        if (ChangeNotifications.TryDequeue(out RobotIntentDataChange change))
                        {
                            yield return change;
                        }
                        else if (m_ownerNotifications.TryDequeue(out NodeId owner))
                        {
                            yield return new RobotIntentDataChange(new NodeId(1), Variant.From(owner));
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref m_activeSubscriptionCount);
                }
            }

            private readonly ConcurrentQueue<NodeId> m_ownerNotifications = new();
            private readonly SemaphoreSlim m_notificationAvailable = new(0);
            private readonly System.Threading.Lock m_stateLock = new();
            private int m_activeSubscriptionCount;
            private TaskCompletionSource<bool>? m_openChannelCountReached;
            private int m_openChannelCountTarget = int.MaxValue;

            private static NodeId ChildNode(string browseName)
            {
                return browseName switch
                {
                    "ExecutionState" => new NodeId(1),
                    "Progress" => new NodeId(2),
                    "CurrentPose" => new NodeId(3),
                    "Result" => new NodeId(4),
                    "ControlOwner" => new NodeId(5),
                    _ => new NodeId((uint)Math.Abs(browseName.GetHashCode(StringComparison.Ordinal)))
                };
            }
        }
    }
}
