/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;

namespace Opc.Ua.Client.Subscriptions
{
    public sealed partial class SubscriptionTests
    {
        /// <summary>
        /// With pooled notifications the data change and event memory passed to the handler is
        /// rented and returned (cleared) once the handler returned; without pooling the handler
        /// may keep it. Values the handler copied stay valid in both modes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(10_000)]
        public async Task PooledNotificationMemoryIsReturnedAfterHandlerAsync(bool pooled, CancellationToken ct)
        {
            ReadOnlyMemory<DataValueChange> keptChanges = default;
            ReadOnlyMemory<EventNotification> keptEvents = default;
            var copiedChanges = new List<DataValueChange>();
            var copiedEvents = new List<EventNotification>();
            m_mockNotificationDataHandler.Setup(value => value.OnDataChangeNotificationAsync(
                    It.IsAny<ISubscription>(), It.IsAny<uint>(), It.IsAny<DateTime>(),
                    It.IsAny<ReadOnlyMemory<DataValueChange>>(), It.IsAny<PublishState>(),
                    It.IsAny<IReadOnlyList<string>>()))
                .Callback((ISubscription _, uint _, DateTime _, ReadOnlyMemory<DataValueChange> changes,
                    PublishState _, IReadOnlyList<string> _) =>
                {
                    keptChanges = changes;
                    copiedChanges.AddRange(changes.ToArray());
                })
                .Returns(default(ValueTask));
            m_mockNotificationDataHandler.Setup(value => value.OnEventDataNotificationAsync(
                    It.IsAny<ISubscription>(), It.IsAny<uint>(), It.IsAny<DateTime>(),
                    It.IsAny<ReadOnlyMemory<EventNotification>>(), It.IsAny<PublishState>(),
                    It.IsAny<IReadOnlyList<string>>()))
                .Callback((ISubscription _, uint _, DateTime _, ReadOnlyMemory<EventNotification> events,
                    PublishState _, IReadOnlyList<string> _) =>
                {
                    keptEvents = events;
                    copiedEvents.AddRange(events.ToArray());
                })
                .Returns(default(ValueTask));
            m_completion.PoolNotifications = pooled;
            await using var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);

            await subscription.OnPublishReceivedAsync(CreateMixedMessage(), [3], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);

            Assert.That(copiedChanges.Select(change => change.ClientHandle), Is.EqualTo(new uint[] { 11, 13 }));
            Assert.That(copiedEvents, Has.Count.EqualTo(1));
            Assert.That(copiedEvents[0].ClientHandle, Is.EqualTo(12));
            Assert.That(copiedEvents[0].Fields[0], Is.EqualTo(Variant.From("event")));
            Assert.That(keptChanges.Length, Is.EqualTo(1));
            Assert.That(keptEvents.Length, Is.EqualTo(1));
            if (pooled)
            {
                // returned to the pool with its references cleared.
                Assert.That(keptChanges.Span[0].ClientHandle, Is.Zero);
                Assert.That(keptEvents.Span[0].ClientHandle, Is.Zero);
                Assert.That(keptEvents.Span[0].Fields.IsNull, Is.True);
            }
            else
            {
                Assert.That(keptChanges.Span[0].ClientHandle, Is.EqualTo(13));
                Assert.That(keptEvents.Span[0].ClientHandle, Is.EqualTo(12));
            }
        }

        /// <summary>
        /// A body kept as bytes reaches the handler decoded, and the message does not keep the
        /// decoded notification alive after the dispatch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(10_000)]
        public async Task DeferredBodyIsDispatchedAndDroppedAfterHandlerAsync(bool pooled, CancellationToken ct)
        {
            var delivered = new List<EventNotification>();
            m_mockNotificationDataHandler.Setup(value => value.OnEventDataNotificationAsync(
                    It.IsAny<ISubscription>(), It.IsAny<uint>(), It.IsAny<DateTime>(),
                    It.IsAny<ReadOnlyMemory<EventNotification>>(), It.IsAny<PublishState>(),
                    It.IsAny<IReadOnlyList<string>>()))
                .Callback((ISubscription _, uint _, DateTime _, ReadOnlyMemory<EventNotification> events,
                    PublishState _, IReadOnlyList<string> _) => delivered.AddRange(events.ToArray()))
                .Returns(default(ValueTask));
            m_completion.PoolNotifications = pooled;
            await using var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);

            ExtensionObject body = DecodeDeferred(new EventNotificationList
            {
                Events = [new EventFieldList { ClientHandle = 12, EventFields = [Variant.From("event")] }]
            });
            var message = new NotificationMessage
            {
                SequenceNumber = 3,
                PublishTime = DateTime.UtcNow,
                NotificationData = [body]
            };
            await subscription.OnPublishReceivedAsync(message, [3], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);

            Assert.That(delivered, Has.Count.EqualTo(1));
            Assert.That(delivered[0].ClientHandle, Is.EqualTo(12));
            Assert.That(delivered[0].Fields[0], Is.EqualTo(Variant.From("event")));
            Assert.That(body.TryGetValue(out EventNotificationList? _), Is.False);
        }

        /// <summary>
        /// A duplicate is never delivered: its body goes back to the pool undecoded.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DuplicateMessageReleasesItsDeferredBodyAsync(CancellationToken ct)
        {
            await using var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);
            await subscription.OnPublishReceivedAsync(BuildDataMessage(1), [1], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);

            ExtensionObject duplicate = await PublishDeferredAsync(subscription, 1).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(2).WaitAsync(ct).ConfigureAwait(false);

            Assert.That(duplicate.TryGetValue(out EventNotificationList? _), Is.False);
        }

        /// <summary>
        /// A message buffered behind a gap gives up its body when the subscription goes away.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DisposalReleasesTheBodyOfABufferedMessageAsync(CancellationToken ct)
        {
            var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);
            await subscription.OnPublishReceivedAsync(BuildDataMessage(1), [1], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);

            ExtensionObject buffered = await PublishDeferredAsync(subscription, 5).ConfigureAwait(false);

            // The duplicate is processed after the buffered message, so its
            // acknowledgement proves that the worker got past it.
            await subscription.OnPublishReceivedAsync(BuildDataMessage(1), [1], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(2).WaitAsync(ct).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(buffered.TryGetValue(out EventNotificationList? _), Is.False);
        }

        /// <summary>
        /// A message still in the queue gives up its body when the subscription goes away.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DisposalReleasesTheBodyOfAQueuedMessageAsync(CancellationToken ct)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Returns((SubscriptionNotificationMessage _, CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask(Task.Delay(Timeout.Infinite, token));
                });
            var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);
            await subscription.OnPublishReceivedAsync(CreateMixedMessage(), [3], []).ConfigureAwait(false);
            await entered.Task.WaitAsync(ct).ConfigureAwait(false);

            ExtensionObject queued = await PublishDeferredAsync(subscription, 4).ConfigureAwait(false);
            await subscription.DisposeAsync().ConfigureAwait(false);

            Assert.That(queued.TryGetValue(out EventNotificationList? _), Is.False);
        }

        private async Task<ExtensionObject> PublishDeferredAsync(TestSubscription subscription, uint sequenceNumber)
        {
            ExtensionObject body = DecodeDeferred(new EventNotificationList
            {
                Events = [new EventFieldList { ClientHandle = 12, EventFields = [Variant.From("event")] }]
            });
            await subscription.OnPublishReceivedAsync(
                new NotificationMessage
                {
                    SequenceNumber = sequenceNumber,
                    PublishTime = DateTime.UtcNow,
                    NotificationData = [body]
                },
                [sequenceNumber],
                []).ConfigureAwait(false);
            return body;
        }

        private ExtensionObject DecodeDeferred(EventNotificationList notification)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            context.DeferNotificationBodies = true;
            using var encoder = new BinaryEncoder(context);
            encoder.WriteExtensionObject(null, new ExtensionObject(notification));
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer()!, context);
            return decoder.ReadExtensionObject(null);
        }

        /// <summary>
        /// The original constructor and deconstruction remain usable without continuation metadata.
        /// </summary>
        [TestCase(null)]
        [TestCase(false)]
        [TestCase(true)]
        public void WholeMessageConstructionPreservesCompatibility(bool? moreNotifications)
        {
            DateTime publishTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            ArrayOf<SubscriptionNotificationPayload> notifications = [];
            ArrayOf<string> stringTable = ["diagnostic"];
            var original = new SubscriptionNotificationMessage(default, publishTime, notifications, stringTable);
            SubscriptionNotificationMessage annotated = original with { MoreNotifications = moreNotifications };
            (AcknowledgementHandle acknowledgement, DateTime time,
                ArrayOf<SubscriptionNotificationPayload> payloads, ArrayOf<string> strings) = annotated;

            Assert.That(original.MoreNotifications, Is.Null);
            Assert.That(default(SubscriptionNotificationMessage).MoreNotifications, Is.Null);
            Assert.That(annotated.MoreNotifications, Is.EqualTo(moreNotifications));
            Assert.That(acknowledgement.IsNull, Is.True);
            Assert.That(time, Is.EqualTo(publishTime));
            Assert.That(payloads, Is.EqualTo(notifications));
            Assert.That(strings, Is.EqualTo(stringTable));
        }

        /// <summary>
        /// Every partition must begin shutdown before retirement waits for a sibling whose
        /// keep-alive is queued behind the active whole-message handler's forwarding gate.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(20_000)]
        public async Task LogicalDisposalCancelsAllPartitionsBeforeWaitingAsync(
            bool primaryActive, CancellationToken ct)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken handlerToken = default;
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Returns((SubscriptionNotificationMessage _, CancellationToken token) =>
                {
                    handlerToken = token;
                    entered.TrySetResult(true);
                    return new ValueTask(release.Task.WaitAsync(token));
                });
            m_mockSubscriptionServices.Setup(value => value.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse { Results = [StatusCodes.Good] });
            var forwarding = new PartitionForwardingHandler(m_mockNotificationDataHandler.Object);
            var primary = new TestSubscription(m_session, forwarding, m_completion,
                OptionsFactory.Create<SubscriptionOptions>(), m_telemetry, 7);
            var secondary = new TestSubscription(m_session, forwarding, m_completion,
                OptionsFactory.Create<SubscriptionOptions>(), m_telemetry, 8);
            var logical = new LogicalSubscription(primary);
            logical.AppendPreloadedPartition(secondary);
            logical.AttachForwardingHandler(forwarding);
            forwarding.BindLogical(logical);
            TestSubscription activePartition = primaryActive ? primary : secondary;
            TestSubscription waitingPartition = primaryActive ? secondary : primary;
            waitingPartition.OnKeepAliveAsync = () =>
            {
                ValueTask dispatch = forwarding.OnKeepAliveNotificationAsync(
                    waitingPartition, 1, DateTime.UtcNow, PublishState.KeepAlive);
                waiting.TrySetResult(!dispatch.IsCompleted);
                return dispatch;
            };
            Task? disposing = null;
            try
            {
                await activePartition.OnPublishReceivedAsync(CreateMixedMessage(), [3], []).ConfigureAwait(false);
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                await waitingPartition.OnPublishReceivedAsync(BuildKeepAliveMessage(1), null, [])
                    .ConfigureAwait(false);
                Assert.That(await waiting.Task.WaitAsync(ct).ConfigureAwait(false), Is.True);

                disposing = logical.DisposeAsync().AsTask();
                await disposing.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                Assert.That(handlerToken.IsCancellationRequested, Is.True);
                Assert.That(primary.Created, Is.False);
                Assert.That(secondary.Created, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await (disposing ?? logical.DisposeAsync().AsTask()).WaitAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Manager shutdown must release a Publish worker blocked by a full ingress buffer
        /// before waiting for subscription shutdown to cancel the active message handler.
        /// </summary>
        [Test]
        [CancelAfter(20_000)]
        public async Task ManagerDisposalCancelsSaturatedMessageAdmissionAsync(CancellationToken ct)
        {
            const int kCapacity = 1024;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var saturated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken handlerToken = default;
            int handlerCalls = 0;
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Returns((SubscriptionNotificationMessage _, CancellationToken token) =>
                {
                    Interlocked.Increment(ref handlerCalls);
                    handlerToken = token;
                    entered.TrySetResult(true);
                    return new ValueTask(release.Task.WaitAsync(token));
                });
            var acknowledgements = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            var confirmations = (ConfirmedAcknowledgementProvider)acknowledgements.Provider!;
            var context = new FakeSubscriptionManagerContext
            {
                AcknowledgementProvider = acknowledgements.Provider,
                CreateSubscriptionFactory = (handler, _, queue) =>
                    new TestSubscription(m_session, handler, queue, m_options, m_telemetry, 7)
                    {
                        OnAdmissionBackpressure = () => saturated.TrySetResult(true)
                    }
            };
            int received = 0;
            int sentAcknowledgements = 0;
            context.OnPublishAsync = (_, sent, _) =>
            {
                Interlocked.Add(ref sentAcknowledgements, sent.Count);
                uint sequenceNumber = (uint)Interlocked.Increment(ref received);
                return new ValueTask<PublishResponse>(new PublishResponse
                {
                    SubscriptionId = 7,
                    MoreNotifications = true,
                    Results = new StatusCode[sent.Count],
                    AvailableSequenceNumbers = sequenceNumber == 1 ? [1u] : [1u, sequenceNumber],
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = sequenceNumber,
                        NotificationData = [new(new DataChangeNotification
                        {
                            MonitoredItems =
                            [
                                new MonitoredItemNotification
                                {
                                    ClientHandle = 1,
                                    Value = new DataValue(Variant.From(sequenceNumber))
                                }
                            ]
                        })]
                    }
                });
            };
            var manager = new SubscriptionManager(context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            Task? disposing = null;
            string stage = "starting the subscription manager";
            try
            {
                manager.Add(m_mockNotificationDataHandler.Object,
                    m_options.Configure(options => options with { DisableUnboundedItemMode = true }));
                manager.Resume();
                stage = "waiting for the notification handler";
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                stage = "waiting for ingress saturation";
                await saturated.Task.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref received), Is.EqualTo(kCapacity + 1));

                stage = "waiting for manager shutdown";
                disposing = manager.DisposeAsync().AsTask();
                await disposing.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                stage = "checking shutdown invariants";
                Assert.That(handlerToken.IsCancellationRequested, Is.True);
                Assert.That(manager.ShutdownToken.IsCancellationRequested, Is.True);
                Assert.That(Volatile.Read(ref handlerCalls), Is.EqualTo(1),
                    "Queued messages must not enter application callbacks after manager shutdown.");
                Assert.That(Volatile.Read(ref sentAcknowledgements), Is.Zero);
                Assert.That(confirmations.TrackedAcknowledgements, Is.Zero);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                WriteTimeoutDiagnostics();
                throw;
            }
            catch (TimeoutException)
            {
                WriteTimeoutDiagnostics();
                throw;
            }
            finally
            {
                release.TrySetResult(true);
                await (disposing ?? manager.DisposeAsync().AsTask())
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            }

            void WriteTimeoutDiagnostics()
            {
                TestContext.Progress.WriteLine(
                    $"Test timed out while {stage}. Shutdown requested: " +
                    $"{manager.ShutdownToken.IsCancellationRequested}; " +
                    $"handler canceled: {handlerToken.IsCancellationRequested}; " +
                    $"publish workers: {manager.PublishWorkerCount}; " +
                    $"responses received: {Volatile.Read(ref received)}.");
            }
        }

        /// <summary>
        /// A concrete handler implementing both interfaces can use the existing registration
        /// API and the whole-message entry point without an explicit interface cast.
        /// </summary>
        [Test]
        public void CombinedHandlersHaveUnambiguousRegistration()
        {
            var handler = new NotificationMessageDispatch.HandlerAdapter(
                new Mock<ISubscriptionNotificationMessageHandler>().Object);
            var options = new SubscriptionOptions { PublishingEnabled = true };
            ManagedSession session = null!;

            Assert.That(() => session.AddSubscription(handler, options),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
            Assert.That(() => session.AddMessageSubscription(handler, options),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        /// <summary>
        /// Whole-message registration rejects missing dependencies at its API boundary.
        /// </summary>
        [Test]
        public void WholeMessageRegistrationRejectsMissingInputs()
        {
            ISubscriptionManager manager = new Mock<ISubscriptionManager>().Object;
            ISubscriptionNotificationMessageHandler handler =
                new Mock<ISubscriptionNotificationMessageHandler>().Object;
            Assert.That(() => SubscriptionNotificationMessageExtensions.Add(null!, handler, m_options),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("manager"));
            Assert.That(() => SubscriptionNotificationMessageExtensions.Add(manager, null!, m_options),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("handler"));
            Assert.That(() => manager.Add(handler, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("options"));
        }

        /// <summary>
        /// Mixed payloads are delivered once, in wire order, including through the partition
        /// forwarding adapter. Projected data survives recycling of the wire containers.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [CancelAfter(10_000)]
        public async Task WholeMessageDeliveryPreservesMixedPayloadsAsync(
            bool partitioned, bool moreNotifications, CancellationToken ct)
        {
            var received = new TaskCompletionSource<SubscriptionNotificationMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Callback((SubscriptionNotificationMessage message, CancellationToken _) =>
                    received.TrySetResult(message))
                .Returns(default(ValueTask));
            using var forwarding = new PartitionForwardingHandler(m_mockNotificationDataHandler.Object);
            ISubscriptionNotificationHandler handler = partitioned ? forwarding : m_mockNotificationDataHandler.Object;
            m_completion.PoolNotifications = true;
            await using var subscription = new TestSubscription(
                m_session, handler, m_completion, m_options, m_telemetry, 7);
            NotificationMessage wire = CreateMixedMessage();
            wire.MoreNotifications = moreNotifications;

            await subscription.OnPublishReceivedAsync(wire, [3], ["diagnostic"]).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);
            Assert.That(received.Task.IsCompleted, Is.True,
                "The whole message must be delivered before queuing its ack.");
            SubscriptionNotificationMessage message = await received.Task.WaitAsync(ct).ConfigureAwait(false);

            Assert.That(message.Acknowledgement.SubscriptionId, Is.EqualTo(7));
            Assert.That(message.Acknowledgement.SequenceNumber, Is.EqualTo(3));
            Assert.That(message.MoreNotifications, Is.EqualTo(moreNotifications));
            Assert.That(message.StringTable.Count, Is.EqualTo(1));
            Assert.That(message.StringTable[0], Is.EqualTo("diagnostic"));
            Assert.That(message.Notifications.Count, Is.EqualTo(4));
            Assert.That(message.Notifications[0].Kind, Is.EqualTo(SubscriptionNotificationKind.DataChange));
            Assert.That(message.Notifications[1].Kind, Is.EqualTo(SubscriptionNotificationKind.Event));
            Assert.That(message.Notifications[2].Kind, Is.EqualTo(SubscriptionNotificationKind.StatusChange));
            Assert.That(message.Notifications[3].Kind, Is.EqualTo(SubscriptionNotificationKind.DataChange));
            Assert.That(message.Notifications[0].DataChanges[0].ClientHandle, Is.EqualTo(11));
            Assert.That(message.Notifications[1].Events[0].ClientHandle, Is.EqualTo(12));
            Assert.That(message.Notifications[3].DataChanges[0].ClientHandle, Is.EqualTo(13));
            Assert.That(message.Notifications[1].Events[0].Acknowledgement, Is.EqualTo(message.Acknowledgement));
            m_mockNotificationDataHandler.Verify(value => value.OnDataChangeNotificationAsync(
                It.IsAny<ISubscription>(), It.IsAny<uint>(), It.IsAny<DateTime>(),
                It.IsAny<ReadOnlyMemory<DataValueChange>>(), It.IsAny<PublishState>(),
                It.IsAny<IReadOnlyList<string>>()), Times.Never);
            m_mockNotificationDataHandler.Verify(value => value.OnEventDataNotificationAsync(
                It.IsAny<ISubscription>(), It.IsAny<uint>(), It.IsAny<DateTime>(),
                It.IsAny<ReadOnlyMemory<EventNotification>>(), It.IsAny<PublishState>(),
                It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        /// <summary>
        /// Gap recovery must not turn a Republish response into a known end of a Publish burst.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [CancelAfter(10_000)]
        public async Task WholeMessageRepublishHasUnknownSplitMetadataAsync(
            bool partitioned, bool staleMoreNotifications, CancellationToken ct)
        {
            var received = new ConcurrentQueue<SubscriptionNotificationMessage>();
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Callback((SubscriptionNotificationMessage message, CancellationToken _) => received.Enqueue(message))
                .Returns(default(ValueTask));
            using var forwarding = new PartitionForwardingHandler(m_mockNotificationDataHandler.Object);
            ISubscriptionNotificationHandler handler = partitioned ? forwarding : m_mockNotificationDataHandler.Object;
            await using var subscription = new TestSubscription(
                m_session, handler, m_completion, m_options, m_telemetry, 7);
            NotificationMessage recovered = BuildDataMessage(2);
            recovered.MoreNotifications = staleMoreNotifications;
            m_mockSubscriptionServices.Setup(value => value.RepublishAsync(
                    It.IsAny<RequestHeader>(), 7, 2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RepublishResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    NotificationMessage = recovered
                });
            NotificationMessage first = BuildDataMessage(1);
            first.MoreNotifications = true;
            await subscription.OnPublishReceivedAsync(first, [1], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);
            await subscription.OnPublishReceivedAsync(BuildDataMessage(3), [1, 2, 3], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(3).WaitAsync(ct).ConfigureAwait(false);

            SubscriptionNotificationMessage[] delivered = [.. received];
            Assert.That(delivered, Has.Length.EqualTo(3));
            Assert.That(delivered[0].MoreNotifications, Is.True);
            Assert.That(delivered[1].Acknowledgement.SequenceNumber, Is.EqualTo(2));
            Assert.That(delivered[1].MoreNotifications, Is.Null);
            Assert.That(delivered[2].MoreNotifications, Is.False);
            m_mockSubscriptionServices.Verify(value => value.RepublishAsync(
                It.IsAny<RequestHeader>(), 7, 2, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Disposal cancels the handler before waiting for the dispatch gate it holds.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task WholeMessageDisposalCancelsHandlerBeforeRetiringGenerationAsync(CancellationToken ct)
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken handlerToken = default;
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Returns((SubscriptionNotificationMessage _, CancellationToken token) =>
                {
                    handlerToken = token;
                    entered.TrySetResult(true);
                    return new ValueTask(release.Task.WaitAsync(token));
                });
            var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);
            Task? disposing = null;
            try
            {
                await subscription.OnPublishReceivedAsync(CreateMixedMessage(), [3], []).ConfigureAwait(false);
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                disposing = subscription.DisposeAsync().AsTask();
                await disposing.WaitAsync(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                Assert.That(handlerToken.IsCancellationRequested, Is.True);
            }
            finally
            {
                release.TrySetResult(true);
                await (disposing ?? subscription.DisposeAsync().AsTask()).WaitAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The classic projection preserves the same message shape without V2 item objects.
        /// </summary>
        [Test]
        public void ClassicMessageProjectionPreservesEveryPayload()
        {
            var handle = new AcknowledgementHandle(new AcknowledgementScope(new object(), 1, 7), 3);
            SubscriptionNotificationMessage message =
                NotificationMessageDispatch.CreateClassic(CreateMixedMessage(), handle);

            Assert.That(message.Notifications.Count, Is.EqualTo(4));
            Assert.That(message.Notifications[0].DataChanges[0].Acknowledgement, Is.EqualTo(handle));
            Assert.That(message.Notifications[0].DataChanges[0].MonitoredItem, Is.Null);
            Assert.That(message.Notifications[1].Events[0].ClientHandle, Is.EqualTo(12));
            Assert.That(message.Notifications[2].Status, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A body that failed to decode costs only itself: the valid payloads of its message
        /// are projected in order.
        /// </summary>
        [Test]
        public void WholeMessageProjectionSkipsMalformedDeferredBody()
        {
            NotificationMessage mixed = CreateMixedMessage();
            var message = new NotificationMessage
            {
                SequenceNumber = 3,
                PublishTime = DateTime.UtcNow,
                NotificationData =
                [
                    mixed.NotificationData[0],
                    DecodeMalformedDeferred(),
                    mixed.NotificationData[1],
                    mixed.NotificationData[2]
                ]
            };

            SubscriptionNotificationMessage projected = NotificationMessageDispatch.CreateClassic(message, default);

            Assert.That(projected.Notifications.Count, Is.EqualTo(3));
            Assert.That(projected.Notifications[0].DataChanges[0].ClientHandle, Is.EqualTo(11));
            Assert.That(projected.Notifications[1].Events[0].ClientHandle, Is.EqualTo(12));
            Assert.That(projected.Notifications[2].Status, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// The valid payloads next to a malformed body reach a whole-message handler, and the
        /// message is acknowledged.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task WholeMessageHandlerReceivesValidPayloadsNextToMalformedBodyAsync(CancellationToken ct)
        {
            var received = new List<SubscriptionNotificationMessage>();
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Returns((SubscriptionNotificationMessage message, CancellationToken _) =>
                {
                    received.Add(message);
                    return default;
                });
            await using var subscription = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry, 7);

            var message = new NotificationMessage
            {
                SequenceNumber = 3,
                PublishTime = DateTime.UtcNow,
                NotificationData =
                [
                    DecodeMalformedDeferred(),
                    new ExtensionObject(new StatusChangeNotification { Status = StatusCodes.Good })
                ]
            };
            await subscription.OnPublishReceivedAsync(message, [3], []).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);

            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].Notifications.Count, Is.EqualTo(1));
            Assert.That(received[0].Notifications[0].Status, Is.EqualTo(StatusCodes.Good));
        }

        private ExtensionObject DecodeMalformedDeferred()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            context.DeferNotificationBodies = true;
            // an EventNotificationList (i=916) with a binary body of an impossible array length.
            byte[] encoded = [1, 0, 0x94, 0x03, 1, 7, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3];
            using var decoder = new BinaryDecoder(encoded, context);
            return decoder.ReadExtensionObject(null);
        }

        /// <summary>
        /// Unknown payloads cannot masquerade as a successfully projected complete message.
        /// </summary>
        [Test]
        public void WholeMessageProjectionRejectsUnknownPayload()
        {
            var message = new NotificationMessage
            {
                NotificationData = [new ExtensionObject(new ApplicationDescription())]
            };
            Assert.That(() => NotificationMessageDispatch.CreateClassic(message, default),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError));
        }

        private static NotificationMessage CreateMixedMessage()
        {
            return new NotificationMessage
            {
                SequenceNumber = 3,
                PublishTime = DateTime.UtcNow,
                NotificationData =
                [
                    new ExtensionObject(new DataChangeNotification
                    {
                        MonitoredItems = [new MonitoredItemNotification { ClientHandle = 11 }]
                    }),
                    new ExtensionObject(new EventNotificationList
                    {
                        Events = [new EventFieldList { ClientHandle = 12, EventFields = [Variant.From("event")] }]
                    }),
                    new ExtensionObject(new StatusChangeNotification { Status = StatusCodes.Good }),
                    new ExtensionObject(new DataChangeNotification
                    {
                        MonitoredItems = [new MonitoredItemNotification { ClientHandle = 13 }]
                    })
                ]
            };
        }
    }
}
