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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    public sealed partial class SubscriptionTests
    {
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
            Assert.That(() => SubscriptionNotificationMessageExtensions.Add(manager, handler, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("options"));
        }

        /// <summary>
        /// Mixed payloads are delivered once, in wire order, including through the partition
        /// forwarding adapter. Projected data survives recycling of the wire containers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(10_000)]
        public async Task WholeMessageDeliveryPreservesMixedPayloadsAsync(bool partitioned, CancellationToken ct)
        {
            var received = new TaskCompletionSource<SubscriptionNotificationMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<ISubscriptionNotificationMessageHandler> messages =
                m_mockNotificationDataHandler.As<ISubscriptionNotificationMessageHandler>();
            messages.Setup(value => value.OnNotificationMessageAsync(
                    It.IsAny<SubscriptionNotificationMessage>(), It.IsAny<CancellationToken>()))
                .Callback((SubscriptionNotificationMessage message, CancellationToken _) =>
                {
                    received.TrySetResult(message);
                })
                .Returns(default(ValueTask));
            using var forwarding = new PartitionForwardingHandler(m_mockNotificationDataHandler.Object);
            ISubscriptionNotificationHandler handler = partitioned ? forwarding : m_mockNotificationDataHandler.Object;
            m_completion.PoolNotifications = true;
            await using var subscription = new TestSubscription(
                m_session, handler, m_completion, m_options, m_telemetry, 7);
            NotificationMessage wire = CreateMixedMessage();

            await subscription.OnPublishReceivedAsync(wire, [3], ["diagnostic"]).ConfigureAwait(false);
            await m_completion.WaitForQueuedAckAsync(1).WaitAsync(ct).ConfigureAwait(false);
            Assert.That(received.Task.IsCompleted, Is.True,
                "The whole message must be delivered before queuing its ack.");
            SubscriptionNotificationMessage message = await received.Task.WaitAsync(ct).ConfigureAwait(false);

            Assert.That(message.Acknowledgement.SubscriptionId, Is.EqualTo(7));
            Assert.That(message.Acknowledgement.SequenceNumber, Is.EqualTo(3));
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
