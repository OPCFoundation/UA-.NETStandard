/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class SessionPublishQueueTests
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<ISession> m_sessionMock;
        private Mock<ISubscriptionManager> m_subscriptionManagerMock;
        private ITelemetryContext m_telemetry;
        private const int kMaxPublishRequests = 10;

        private sealed class TestParkSink : IRequestParkSink
        {
            public int ParkedCount => m_parkedCount;

            public void NotifyParked()
            {
                Interlocked.Increment(ref m_parkedCount);
            }

            private int m_parkedCount;
        }

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_sessionMock = new Mock<ISession>();
            m_subscriptionManagerMock = new Mock<ISubscriptionManager>();

            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.SubscriptionManager).Returns(m_subscriptionManagerMock.Object);

            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            m_sessionMock.Setup(s => s.IsSecureChannelValid(It.IsAny<string>())).Returns(true);
        }

        [Test]
        public void Constructor_NullArgs_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionPublishQueue(null!, m_sessionMock.Object, kMaxPublishRequests));
            Assert.Throws<ArgumentNullException>(() => new SessionPublishQueue(m_serverMock.Object, null!, kMaxPublishRequests));
        }

        [Test]
        public void PublishAsync_NoSubscriptions_ThrowsBadNoSubscription()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            ServiceResultException ex =
                Assert.CatchAsync<ServiceResultException>(() => queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public void PublishAsync_QueueFull_FailsOldestRequestAndQueuesNewRequest()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var queued = new List<Task<ISubscriptionPublishPipeline>>();
            for (int ii = 0; ii < kMaxPublishRequests; ii++)
            {
                queued.Add(queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None));
            }
            Assert.That(queued.TrueForAll(t => !t.IsCompleted), Is.True);

            // OPC 10000-4, 5.14.5.1: the oldest request is de-queued, the new one is queued.
            Task<ISubscriptionPublishPipeline> newRequest =
                queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            ServiceResultException ex =
                Assert.CatchAsync<ServiceResultException>(async () => await queued[0].ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyPublishRequests));
            Assert.That(newRequest.IsCompleted, Is.False);
            Assert.That(queued.GetRange(1, kMaxPublishRequests - 1).TrueForAll(t => !t.IsCompleted), Is.True);

            // Capacity accounting stays exact: the next request evicts the next oldest only.
            Task<ISubscriptionPublishPipeline> nextRequest =
                queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(queued[1].IsFaulted, Is.True);
            Assert.That(queued[2].IsCompleted, Is.False);
            Assert.That(nextRequest.IsCompleted, Is.False);
        }

        [Test]
        public void PublishAsync_RequeueWhenQueueFull_DoesNotFailQueuedRequests()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var queued = new List<Task<ISubscriptionPublishPipeline>>();
            for (int ii = 0; ii < kMaxPublishRequests; ii++)
            {
                queued.Add(queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None));
            }

            // A requeued request is already being processed and is not a new Publish request.
            Task<ISubscriptionPublishPipeline> requeued =
                queue.PublishAsync("channel1", DateTime.MaxValue, true, null, CancellationToken.None);

            Assert.That(requeued.IsCompleted, Is.False);
            Assert.That(queued.TrueForAll(t => !t.IsCompleted), Is.True);
        }

        [Test]
        public void PublishAsync_AcceptsMoreRequestsThanSubscriptions()
        {
            // The configured limit is lower than the number of Subscriptions.
            const int subscriptionCount = 3;
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, 1);

            for (uint id = 1; id <= subscriptionCount; id++)
            {
                var subMock = new Mock<ISubscriptionPublishPipeline>();
                subMock.Setup(s => s.Id).Returns(id);
                queue.Add(subMock.Object);
            }

            // OPC 10000-4, 5.14.5.1: more queued Publish requests than Subscriptions are accepted.
            var queued = new List<Task<ISubscriptionPublishPipeline>>();
            for (int ii = 0; ii <= subscriptionCount; ii++)
            {
                queued.Add(queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None));
            }
            Assert.That(queued.TrueForAll(t => !t.IsCompleted), Is.True);

            Task<ISubscriptionPublishPipeline> overflow =
                queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            ServiceResultException ex =
                Assert.CatchAsync<ServiceResultException>(async () => await queued[0].ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyPublishRequests));
            Assert.That(overflow.IsCompleted, Is.False);
        }

        [Test]
        public async Task PublishAsync_ReturnsSubscriptionIfReadyAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            queue.Requeue(subMock.Object); // Sets ReadyToPublish to true

            ISubscriptionPublishPipeline result = await queue
                .PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task PublishTimerPreservesReadySubscriptionTimestampOrderAsync()
        {
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                kMaxPublishRequests);

            PublishingState publishingState = PublishingState.Idle;
            var timerOrder = new List<ISubscriptionPublishPipeline>();
            var subscription1 = new Mock<ISubscriptionPublishPipeline>();
            subscription1.Setup(s => s.Id).Returns(1);
            subscription1.Setup(s => s.Priority).Returns(1);
            subscription1
                .Setup(s => s.PublishTimerExpired(It.IsAny<bool>()))
                .Callback(() => timerOrder.Add(subscription1.Object))
                .Returns(() => publishingState);
            queue.Add(subscription1.Object);

            var subscription2 = new Mock<ISubscriptionPublishPipeline>();
            subscription2.Setup(s => s.Id).Returns(2);
            subscription2.Setup(s => s.Priority).Returns(1);
            subscription2
                .Setup(s => s.PublishTimerExpired(It.IsAny<bool>()))
                .Callback(() => timerOrder.Add(subscription2.Object))
                .Returns(() => publishingState);
            queue.Add(subscription2.Object);

            queue.PublishTimerExpired();
            Assert.That(timerOrder, Has.Count.EqualTo(2));

            ISubscriptionPublishPipeline newerSubscription = timerOrder[0];
            ISubscriptionPublishPipeline olderSubscription = timerOrder[1];
            queue.PublishCompleted(olderSubscription, false);
            DateTime timestampBoundary = DateTime.UtcNow;
            Assert.That(
                SpinWait.SpinUntil(
                    () => DateTime.UtcNow > timestampBoundary,
                    TimeSpan.FromSeconds(1)),
                Is.True,
                "The clock did not advance while preparing distinct subscription timestamps.");
            queue.PublishCompleted(newerSubscription, false);

            queue.Requeue(newerSubscription);
            queue.Requeue(olderSubscription);
            publishingState = PublishingState.NotificationsAvailable;
            timerOrder.Clear();
            queue.PublishTimerExpired();

            Assert.That(timerOrder, Has.Count.EqualTo(2));
            Assert.That(timerOrder[0], Is.SameAs(newerSubscription));
            Assert.That(timerOrder[1], Is.SameAs(olderSubscription));
            ISubscriptionPublishPipeline result = await queue.PublishAsync(
                "channel1",
                DateTime.MaxValue,
                false,
                null,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.SameAs(olderSubscription));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task PublishTimerAssignsWaitingRequestToHighestPrioritySubscriptionAsync()
        {
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                kMaxPublishRequests);

            // The queued subscriptions are enumerated in an unspecified order, so the
            // priority is derived from the observed order: the Subscription the publish
            // timer visits first is the one with the lowest priority.
            var timerOrder = new List<ISubscriptionPublishPipeline>();
            var subscription1 = new Mock<ISubscriptionPublishPipeline>();
            var subscription2 = new Mock<ISubscriptionPublishPipeline>();

            byte GetPriority(ISubscriptionPublishPipeline subscription)
            {
                return timerOrder.Count > 0 && ReferenceEquals(timerOrder[0], subscription)
                    ? (byte)1
                    : (byte)200;
            }

            subscription1.Setup(s => s.Id).Returns(1);
            subscription1.Setup(s => s.Priority).Returns(() => GetPriority(subscription1.Object));
            subscription1
                .Setup(s => s.PublishTimerExpired(It.IsAny<bool>()))
                .Callback(() => timerOrder.Add(subscription1.Object))
                .Returns(PublishingState.NotificationsAvailable);
            queue.Add(subscription1.Object);

            subscription2.Setup(s => s.Id).Returns(2);
            subscription2.Setup(s => s.Priority).Returns(() => GetPriority(subscription2.Object));
            subscription2
                .Setup(s => s.PublishTimerExpired(It.IsAny<bool>()))
                .Callback(() => timerOrder.Add(subscription2.Object))
                .Returns(PublishingState.NotificationsAvailable);
            queue.Add(subscription2.Object);

            Task<ISubscriptionPublishPipeline> request = queue.PublishAsync(
                "channel1",
                DateTime.MaxValue,
                false,
                null,
                CancellationToken.None);
            Assert.That(request.IsCompleted, Is.False);

            queue.PublishTimerExpired();

            Assert.That(timerOrder, Has.Count.EqualTo(2));

            ISubscriptionPublishPipeline result = await request.ConfigureAwait(false);
            Assert.That(result.Priority, Is.EqualTo(200));
            Assert.That(result, Is.SameAs(timerOrder[1]));
        }

        [Test]
        public void PublishAsync_WhenParked_NotifiesParkSinkOnce()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object); // added but not ready, so the request must park

            var sink = new TestParkSink();
            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, sink, CancellationToken.None);

            Assert.That(task.IsCompleted, Is.False, "The request should be parked.");
            Assert.That(
                sink.ParkedCount,
                Is.EqualTo(1),
                "The park sink should be notified exactly once when the request parks.");
        }

        [Test]
        public void PublishAsync_WhenParked_CancelServiceCancelsRequest()
        {
            // The OPC UA Cancel service cancels outstanding requests via
            // RequestManager.CancelRequests -> RequestLifetime.TryCancel. That must also
            // cancel a Publish request that has parked (released its worker) while waiting
            // for the next notification - not only a client-side cancellation token.
            using var requestManager = new RequestManager(m_serverMock.Object);
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object); // added but not ready, so the request must park

            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(1));

            const uint requestHandle = 77;
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null!,
                RequestType.Publish,
                requestLifetime,
                sessionMock.Object);
            requestManager.RequestReceived(context);

            var sink = new TestParkSink();
            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, sink, requestLifetime.CancellationToken);

            Assert.That(task.IsCompleted, Is.False, "The request should park while it waits.");
            Assert.That(
                sink.ParkedCount,
                Is.EqualTo(1),
                "A parked request releases its processing worker at the park point.");

            // A Cancel service call carrying the Publish request handle.
            requestManager.CancelRequests(context.SessionId, requestHandle, out uint cancelCount);

            Assert.That(cancelCount, Is.EqualTo(1), "The Cancel service should match the parked Publish request.");
            Assert.CatchAsync<OperationCanceledException>(() => task);
            Assert.That(task.IsCanceled, Is.True, "The parked Publish request must complete as canceled.");
            Assert.That(
                context.OperationStatus.Code,
                Is.EqualTo(StatusCodes.BadRequestCancelledByClient),
                "The canceled Publish must carry the Cancel service status code.");
        }

        [Test]
        public async Task PublishAsync_WhenSubscriptionReady_DoesNotNotifyParkSinkAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);
            queue.Requeue(subMock.Object); // ready, so the request returns immediately

            var sink = new TestParkSink();
            ISubscriptionPublishPipeline result = await queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, sink, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(subMock.Object));
            Assert.That(
                sink.ParkedCount,
                Is.Zero,
                "A request that completes immediately must not be reported as parked.");
        }

        [Test]
        public void PublishAsync_NoSubscriptions_DoesNotNotifyParkSink()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var sink = new TestParkSink();

            Assert.CatchAsync<ServiceResultException>(
                () => queue.PublishAsync("channel1", DateTime.MaxValue, false, sink, CancellationToken.None));
            Assert.That(
                sink.ParkedCount,
                Is.Zero,
                "A request that faults immediately must not be reported as parked.");
        }

        [Test]
        public async Task Add_And_PublishTimerExpired_AssignsSubscriptionToRequestAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False);

            queue.PublishTimerExpired();

            ISubscriptionPublishPipeline result = await task.ConfigureAwait(false);
            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void Close_ClearsQueuesAndSignalsSessionClosed()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.Session).Returns(m_sessionMock.Object);
            subMock
                .Setup(s => s.SessionClosed(m_sessionMock.Object))
                .Returns(true);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            IList<ISubscriptionPublishPipeline> subs = queue.Close();

            Assert.That(subs, Has.Count.EqualTo(1));
            Assert.That(subs[0], Is.SameAs(subMock.Object));
            subMock.Verify(s => s.SessionClosed(m_sessionMock.Object), Times.Once);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public void Close_DoesNotCloseSubscriptionOwnedByAnotherSession()
        {
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                kMaxPublishRequests);
            var destinationSession = new Mock<ISession>();
            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.Session).Returns(destinationSession.Object);
            queue.Add(subMock.Object);

            IList<ISubscriptionPublishPipeline> subs = queue.Close();

            // The queue asks every queued subscription and keeps only the ones that report
            // they were released, so ownership is decided by the subscription itself.
            Assert.That(subs, Is.Empty);
            subMock.Verify(s => s.SessionClosed(m_sessionMock.Object), Times.Once);
        }

        [Test]
        public void Remove_RemovesSubscription()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            queue.Remove(subMock.Object, removeQueuedRequests: true);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public async Task TryPublishCustomStatus_CompletesRemainingRequestsAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task1 = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            bool published = queue.TryPublishCustomStatus(StatusCodes.Good);
            Assert.That(published, Is.True);

            ISubscriptionPublishPipeline result = await task1.ConfigureAwait(false);
            Assert.That(result, Is.Null); // Good status completes with null to allow sending custom status

            bool publishedAgain = queue.TryPublishCustomStatus(StatusCodes.Good);
            Assert.That(publishedAgain, Is.False);
        }

        [Test]
        public void Acknowledge_ValidAcks_ReturnsGood()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.Acknowledge(It.IsAny<OperationContext>(), 10))
                   .Returns(StatusCodes.Good);

            queue.Add(subMock.Object);

            var acks = (ArrayOf<SubscriptionAcknowledgement>)[
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 10 }
            ];

            var context = new OperationContext(new RequestHeader(), null!, RequestType.Publish, RequestLifetime.None, m_sessionMock.Object);

            queue.Acknowledge(context, acks, out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagInfos);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagInfos.Count, Is.Zero);
        }

        [Test]
        public void Acknowledge_InvalidSubscription_ReturnsBadSubscriptionIdInvalid()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var acks = (ArrayOf<SubscriptionAcknowledgement>)[
                new SubscriptionAcknowledgement { SubscriptionId = 99, SequenceNumber = 10 }
            ];

            var context = new OperationContext(new RequestHeader(), null!, RequestType.Publish, RequestLifetime.None, m_sessionMock.Object);

            queue.Acknowledge(context, acks, out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagInfos);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public void Acknowledge_SubscriptionThrows_ReturnsPerAckResultAndKeepsOtherResults()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            // A subscription deleted/transferred concurrently throws from Acknowledge.
            var deleted = new Mock<ISubscriptionPublishPipeline>();
            deleted.Setup(s => s.Id).Returns(1);
            deleted.Setup(s => s.Acknowledge(It.IsAny<OperationContext>(), It.IsAny<uint>()))
                .Throws(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));
            var healthy = new Mock<ISubscriptionPublishPipeline>();
            healthy.Setup(s => s.Id).Returns(2);
            healthy.Setup(s => s.Acknowledge(It.IsAny<OperationContext>(), 10))
                .Returns(StatusCodes.Good);
            queue.Add(deleted.Object);
            queue.Add(healthy.Object);

            var acks = (ArrayOf<SubscriptionAcknowledgement>)[
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 5 },
                new SubscriptionAcknowledgement { SubscriptionId = 2, SequenceNumber = 10 }
            ];
            var context = new OperationContext(new RequestHeader(), null!, RequestType.Publish, RequestLifetime.None, m_sessionMock.Object);

            queue.Acknowledge(context, acks, out ArrayOf<StatusCode> results, out _);

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            Assert.That(results[1], Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task PublishCompleted_MoreNotifications_AssignsNextRequestAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            // Mark sub as publishing by manually triggering assignment
            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired();

            Task<ISubscriptionPublishPipeline> task2 = queue.PublishAsync("channel2", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task2.IsCompleted, Is.False);

            // Complete first publish and state there's more notifications
            queue.PublishCompleted(subMock.Object, moreNotifications: true);

            ISubscriptionPublishPipeline result = await task2.ConfigureAwait(false);
            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void PublishCompleted_NoMoreNotifications_SetsReadyToPublishFalse()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired(); // Gets assigned, sets ReadyToPublish = true, Publishing = true

            queue.PublishCompleted(subMock.Object, moreNotifications: false);

            Task<ISubscriptionPublishPipeline> task2 = queue.PublishAsync("channel2", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task2.IsCompleted, Is.False); // Still incomplete because it's no longer ready
        }

        /// <summary>
        /// Review U9: the publish timer tells the subscriptions whether a Publish request is
        /// queued (PublishingReqQueued, OPC 10000-4 §5.14.1.3).
        /// </summary>
        [Test]
        public void PublishTimerPassesWhetherAPublishRequestIsQueued()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.Idle);
            queue.Add(subMock.Object);

            queue.PublishTimerExpired();
            subMock.Verify(s => s.PublishTimerExpired(false), Times.Once);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False);

            queue.PublishTimerExpired();
            subMock.Verify(s => s.PublishTimerExpired(true), Times.Once);
        }

        [Test]
        public void PublishTimerExpired_Idle_SetsReadyToPublishFalse()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // Make it ready
            queue.Requeue(subMock.Object);

            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.Idle);
            queue.PublishTimerExpired(); // Should set ReadyToPublish = false

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False); // Since it's idle, task is queued instead of fulfilled
        }

        [Test]
        public void TryPublishCustomStatus_BadStatus_CompletesRequestWithException()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            bool published = queue.TryPublishCustomStatus(StatusCodes.BadNotConnected);
            Assert.That(published, Is.True);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public void PublishAsync_TimesOut_ThrowsBadTimeout()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            DateTime timeout = DateTime.UtcNow.AddMilliseconds(-1000); // Already past timeout
            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", timeout, false, null, CancellationToken.None);

            // Since operationTimeout < DateTime.MaxValue and timeOut <= 0, wait, timeOut calculation:
            // timeOut = operationTimeout.AddMilliseconds(500) - DateTime.UtcNow
            // If already past, timeout immediately? Let's use a short delay.
            DateTime timeout2 = DateTime.UtcNow.AddMilliseconds(1);
            Task<ISubscriptionPublishPipeline> task2 = queue.PublishAsync("channel2", timeout2, false, null, CancellationToken.None);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task2);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void PublishAsyncWithMaximumTimeoutHintParksRequest()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // OPC 10000-4, 7.33: any UInt32 TimeoutHint is valid; the largest one exceeds
            // the timer range of CancellationTokenSource on every platform.
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(uint.MaxValue);
            Task<ISubscriptionPublishPipeline>? task = null;
            Assert.DoesNotThrow(() => task = queue.PublishAsync(
                "channel1", deadline, false, null, CancellationToken.None));

            Assert.That(task!.IsCompleted, Is.False);

            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired();
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(task.Result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void PublishAsyncWithTimeoutHintBeyondTimerRangeTimesOutAtDeadline()
        {
            var timeProvider = new FakeTimeProvider();
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                kMaxPublishRequests,
                timeProvider);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // A 30 day TimeoutHint exceeds the Int32.MaxValue ms (~24.86 days) timer limit:
            // the request must not time out before its deadline (OPC 10000-4, 7.33).
            TimeSpan hint = TimeSpan.FromDays(30);
            DateTime deadline = timeProvider.GetUtcNow().UtcDateTime + hint;
            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync(
                "channel1", deadline, false, null, CancellationToken.None);

            timeProvider.Advance(TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromSeconds(1));
            Assert.That(task.IsCompleted, Is.False);

            timeProvider.Advance(hint - TimeSpan.FromMilliseconds(int.MaxValue) - TimeSpan.FromSeconds(2));
            Assert.That(task.IsCompleted, Is.False);

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void RequeueServesParkedRequest()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> first = queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Task<ISubscriptionPublishPipeline> second = queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired();
            Assert.That(first.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(second.IsCompleted, Is.False);

            // The first request returned a status message instead and put the
            // subscription back: the parked request must be served right away.
            queue.Requeue(subMock.Object);

            Assert.That(second.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(second.Result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void RestoreTransferClaimServesParkedRequestWhenReady()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.TryBeginTransfer(It.IsAny<ISession>())).Returns(true);
            queue.Add(subMock.Object);

            Assert.That(
                queue.TryClaimForTransfer(
                    subMock.Object,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim? claim),
                Is.True);

            Task<ISubscriptionPublishPipeline> parked = queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            // An in-flight Publish completes with more notifications while claimed.
            queue.PublishCompleted(subMock.Object, moreNotifications: true);
            Assert.That(parked.IsCompleted, Is.False);

            // The transfer rolls back: the parked request gets the ready subscription.
            Assert.That(queue.RestoreTransferClaim(claim!), Is.True);

            Assert.That(parked.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(parked.Result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void PublishAsyncParksWhileOnlySubscriptionIsClaimedForTransfer()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.TryBeginTransfer(It.IsAny<ISession>())).Returns(true);
            queue.Add(subMock.Object);

            Assert.That(
                queue.TryClaimForTransfer(
                    subMock.Object,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim? claim),
                Is.True);

            // OPC 10000-4, 5.14.5: the session still owns the subscription.
            Task<ISubscriptionPublishPipeline> parked = queue.PublishAsync(
                "channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(parked.IsCompleted, Is.False);

            queue.RemoveQueuedRequests();
            Assert.That(parked.IsCompleted, Is.False);

            // The transfer completes, so the session no longer owns a subscription.
            queue.CompleteTransferClaim(claim!);
            queue.RemoveQueuedRequests();

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => parked);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public void RemoveQueuedRequests_NoSubscriptions_FailsRequests()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            // Force add a request bypassing normal validations by providing a sub then removing it without auto-removal
            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            queue.Remove(subMock.Object, removeQueuedRequests: false);
            Assert.That(task.IsCompleted, Is.False);

            queue.RemoveQueuedRequests();

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public void AssignSubscriptionToRequest_InvalidSecureChannel_ThrowsBadSecureChannelIdInvalid()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // Initial publish request with a specific channel ID
            Task<ISubscriptionPublishPipeline> task = queue.PublishAsync("invalid_channel", DateTime.MaxValue, false, null, CancellationToken.None);

            // Mock the session to return false for this channel
            m_sessionMock.Setup(s => s.IsSecureChannelValid("invalid_channel")).Returns(false);

            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired(); // Triggers assignment

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public async Task GetSubscriptionToPublish_SelectsHighestPriorityAndOldestAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var sub1 = new Mock<ISubscriptionPublishPipeline>();
            sub1.Setup(s => s.Id).Returns(1);
            sub1.Setup(s => s.Priority).Returns(10);
            queue.Add(sub1.Object);

            await Task.Delay(15).ConfigureAwait(false); // Ensure older timestamp

            var sub3 = new Mock<ISubscriptionPublishPipeline>();
            sub3.Setup(s => s.Id).Returns(3);
            sub3.Setup(s => s.Priority).Returns(20); // Same high priority, older
            queue.Add(sub3.Object);

            await Task.Delay(15).ConfigureAwait(false); // Ensure newer timestamp

            var sub2 = new Mock<ISubscriptionPublishPipeline>();
            sub2.Setup(s => s.Id).Returns(2);
            sub2.Setup(s => s.Priority).Returns(20); // Highest priority, newer
            queue.Add(sub2.Object);

            // Make them ready sequentially
            queue.Requeue(sub1.Object);
            queue.Requeue(sub2.Object);
            queue.Requeue(sub3.Object);

            // Publish Request should return sub3 (highest priority, oldest)
            ISubscriptionPublishPipeline result1 = await queue
                .PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result1, Is.SameAs(sub3.Object));

            // Next should be sub2 (highest priority, newer)
            ISubscriptionPublishPipeline result2 = await queue
                .PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result2, Is.SameAs(sub2.Object));

            // Next should be sub1 (lower priority)
            ISubscriptionPublishPipeline result3 = await queue
                .PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result3, Is.SameAs(sub1.Object));
        }

        [Test]
        public async Task PublishAsync_RequeueTrue_AddsToFrontOfQueueAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscriptionPublishPipeline>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscriptionPublishPipeline> taskA = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Task<ISubscriptionPublishPipeline> taskB = queue.PublishAsync("channel1", DateTime.MaxValue, true, null, CancellationToken.None);
            Task<ISubscriptionPublishPipeline> taskC = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);

            // First expiration should complete taskB because it was requeued (added to front)
            queue.PublishTimerExpired();
            ISubscriptionPublishPipeline resultB = await taskB.ConfigureAwait(false);
            Assert.That(resultB, Is.SameAs(subMock.Object));
            Assert.That(taskA.IsCompleted, Is.False);
            Assert.That(taskC.IsCompleted, Is.False);

            queue.Remove(subMock.Object, false);

            // Need another subscription to fulfill the next request
            var subMock2 = new Mock<ISubscriptionPublishPipeline>();
            subMock2.Setup(s => s.Id).Returns(2);
            subMock2.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock2.Object);

            // Second expiration should complete taskA (it was the first added with requeue=false)
            queue.PublishTimerExpired();
            ISubscriptionPublishPipeline resultA = await taskA.ConfigureAwait(false);
            Assert.That(resultA, Is.SameAs(subMock2.Object));
            Assert.That(taskC.IsCompleted, Is.False);

            queue.Remove(subMock2.Object, false);

            // Need a third subscription to fulfill the last request
            var subMock3 = new Mock<ISubscriptionPublishPipeline>();
            subMock3.Setup(s => s.Id).Returns(3);
            subMock3.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock3.Object);

            // Third expiration should complete taskC
            queue.PublishTimerExpired();
            ISubscriptionPublishPipeline resultC = await taskC.ConfigureAwait(false);
            Assert.That(resultC, Is.SameAs(subMock3.Object));
        }

        [Test]
        public async Task Concurrency_MultipleRequestsAndSubscriptionsAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, 100);

            const int numItems = 50;

            var subs = new List<Mock<ISubscriptionPublishPipeline>>();
            for (int i = 0; i < numItems; i++)
            {
                var subMock = new Mock<ISubscriptionPublishPipeline>();
                subMock.Setup(s => s.Id).Returns((uint)(i + 1));
                subMock.Setup(s => s.Priority).Returns((byte)(i % 5));
                subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
                subs.Add(subMock);
                queue.Add(subMock.Object);
            }

            using var startGate = new ManualResetEventSlim(false);
            var publishTasks = new List<Task<ISubscriptionPublishPipeline>>();
            var timerTasks = new List<Task>();

            // Start multiple threads requesting publish
            for (int i = 0; i < numItems; i++)
            {
                publishTasks.Add(Task.Run(() =>
                {
                    startGate.Wait();
                    return queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
                }));
            }

            // Start multiple threads mimicking subscriptions being ready or timer expiring
            for (int i = 0; i < numItems; i++)
            {
                int index = i;
                timerTasks.Add(Task.Run(() =>
                {
                    startGate.Wait();
                    queue.PublishCompleted(subs[index].Object, true);
                }));
            }

            // Open the gate
            startGate.Set();

            // Wait for publishers to finish producing
            await Task.WhenAll(timerTasks).ConfigureAwait(false);

            // Wait for consumers to get their subscriptions
            Task<ISubscriptionPublishPipeline[]> resultsTask = Task.WhenAll(publishTasks);
            Task completedTask = await Task.WhenAny(resultsTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            queue.Close();

            if (completedTask != resultsTask)
            {
                Assert.Fail("Timed out waiting for publish tasks to complete.");
            }

            ISubscriptionPublishPipeline[] results = await resultsTask.ConfigureAwait(false);

            // Verify results
            int validSubscriptions = 0;
            var returnedSubIds = new HashSet<uint>();
            foreach (ISubscriptionPublishPipeline result in results)
            {
                if (result != null)
                {
                    validSubscriptions++;
                    returnedSubIds.Add(result.Id);
                }
            }

            Assert.That(validSubscriptions, Is.EqualTo(numItems), "All publish requests should have received a subscription.");
            Assert.That(returnedSubIds, Has.Count.EqualTo(numItems), "All subscriptions should have been processed.");
        }
    }
}
