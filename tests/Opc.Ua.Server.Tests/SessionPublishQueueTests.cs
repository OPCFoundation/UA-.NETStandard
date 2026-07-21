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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
            Assert.Throws<ArgumentNullException>(() => new SessionPublishQueue(null, m_sessionMock.Object, kMaxPublishRequests));
            Assert.Throws<ArgumentNullException>(() => new SessionPublishQueue(m_serverMock.Object, null, kMaxPublishRequests));
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
        public void PublishAsync_QueueFull_ThrowsBadTooManyPublishRequests()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, 1);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // First publish request should be queued
            Task<ISubscription> task1 = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task1.IsCompleted, Is.False);

            // Second publish request should fail because max queue size is 1
            ServiceResultException ex =
                Assert.CatchAsync<ServiceResultException>(() => queue.PublishAsync("channel2", DateTime.MaxValue, false, null, CancellationToken.None));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyPublishRequests));
        }

        [Test]
        public async Task PublishAsync_ReturnsSubscriptionIfReadyAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            queue.Requeue(subMock.Object); // Sets ReadyToPublish to true

            ISubscription result = await queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        [NonParallelizable]
        [CancelAfter(10000)]
        public async Task PublishReadyTransitionCompletesQueuedRequestAsync()
        {
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                kMaxPublishRequests);
            var subscription = new Mock<ISubscription>();
            subscription.Setup(s => s.Id).Returns(1);
            queue.Add(subscription.Object);

            (Lock queueLock, Lock splitPublishLock) = GetPrivateLocks(queue);
            using var publishStarted = new ManualResetEventSlim();
            Task<ISubscription> publishTask;

            // The legacy split lock lets PublishAsync finish its ready check
            // before it blocks on the request-queue lock.
            queueLock.Enter();
            try
            {
                splitPublishLock?.Enter();
                try
                {
                    publishTask = Task.Run(() =>
                    {
                        publishStarted.Set();
                        return queue.PublishAsync(
                            "channel1",
                            DateTime.MaxValue,
                            false,
                            null,
                            CancellationToken.None);
                    });
                    Assert.That(
                        publishStarted.Wait(TimeSpan.FromSeconds(2)),
                        Is.True,
                        "The Publish request did not start.");
                }
                finally
                {
                    splitPublishLock?.Exit();
                }

                if (splitPublishLock != null)
                {
                    WaitForSplitLockTransition(splitPublishLock);
                }

                // System.Threading.Lock is reentrant. Keeping the queue lock
                // held preserves the historical check-before-enqueue window.
                queue.Requeue(subscription.Object);
            }
            finally
            {
                queueLock.Exit();
            }

            Task completed = await Task.WhenAny(
                publishTask,
                Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

            Assert.That(
                completed,
                Is.SameAs(publishTask),
                "The ready Subscription and queued Publish request lost their wake-up.");
            Assert.That(
                await publishTask.ConfigureAwait(false),
                Is.SameAs(subscription.Object));
        }

        [Test]
        [CancelAfter(10000)]
        public async Task PublishTimerAssignsRequeuedSubscriptionToWaitingRequestAsync()
        {
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                kMaxPublishRequests);
            var subscription = new Mock<ISubscription>();
            subscription.Setup(s => s.Id).Returns(1);
            subscription
                .Setup(s => s.PublishTimerExpired())
                .Returns(PublishingState.NotificationsAvailable);
            queue.Add(subscription.Object);

            queue.Requeue(subscription.Object);
            Assert.That(
                await queue.PublishAsync(
                    "channel1",
                    DateTime.MaxValue,
                    false,
                    null,
                    CancellationToken.None).ConfigureAwait(false),
                Is.SameAs(subscription.Object));

            Task<ISubscription> publishTask = queue.PublishAsync(
                "channel1",
                DateTime.MaxValue,
                false,
                null,
                CancellationToken.None);
            Assert.That(publishTask.IsCompleted, Is.False);

            queue.Requeue(subscription.Object);
            queue.PublishTimerExpired();

            Task completed = await Task.WhenAny(
                publishTask,
                Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

            Assert.That(
                completed,
                Is.SameAs(publishTask),
                "The timer did not assign the requeued Subscription to the waiting Publish request.");
            Assert.That(
                await publishTask.ConfigureAwait(false),
                Is.SameAs(subscription.Object));
        }

        [Test]
        public void PublishAsync_WhenParked_NotifiesParkSinkOnce()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object); // added but not ready, so the request must park

            var sink = new TestParkSink();
            Task<ISubscription> task = queue.PublishAsync(
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

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object); // added but not ready, so the request must park

            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(1));

            const uint requestHandle = 77;
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.Publish,
                requestLifetime,
                sessionMock.Object);
            requestManager.RequestReceived(context);

            var sink = new TestParkSink();
            Task<ISubscription> task = queue.PublishAsync(
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
                Is.EqualTo(StatusCodes.BadRequestCancelledByRequest),
                "The canceled Publish must carry the Cancel service status code.");
        }

        [Test]
        public async Task PublishAsync_WhenSubscriptionReady_DoesNotNotifyParkSinkAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);
            queue.Requeue(subMock.Object); // ready, so the request returns immediately

            var sink = new TestParkSink();
            ISubscription result = await queue.PublishAsync(
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

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False);

            queue.PublishTimerExpired();

            ISubscription result = await task.ConfigureAwait(false);
            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void Close_ClearsQueuesAndSignalsSessionClosed()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            IList<ISubscription> subs = queue.Close();

            Assert.That(subs, Has.Count.EqualTo(1));
            Assert.That(subs[0], Is.SameAs(subMock.Object));
            subMock.Verify(s => s.SessionClosed(), Times.Once);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public void Remove_RemovesSubscription()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            queue.Remove(subMock.Object, removeQueuedRequests: true);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public async Task TryPublishCustomStatus_CompletesRemainingRequestsAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task1 = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            bool published = queue.TryPublishCustomStatus(StatusCodes.Good);
            Assert.That(published, Is.True);

            ISubscription result = await task1.ConfigureAwait(false);
            Assert.That(result, Is.Null); // Good status completes with null to allow sending custom status

            bool publishedAgain = queue.TryPublishCustomStatus(StatusCodes.Good);
            Assert.That(publishedAgain, Is.False);
        }

        [Test]
        public void Acknowledge_ValidAcks_ReturnsGood()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.Acknowledge(It.IsAny<OperationContext>(), 10))
                   .Returns(StatusCodes.Good);

            queue.Add(subMock.Object);

            var acks = (ArrayOf<SubscriptionAcknowledgement>)[
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 10 }
            ];

            var context = new OperationContext(new RequestHeader(), null, RequestType.Publish, RequestLifetime.None, m_sessionMock.Object);

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

            var context = new OperationContext(new RequestHeader(), null, RequestType.Publish, RequestLifetime.None, m_sessionMock.Object);

            queue.Acknowledge(context, acks, out ArrayOf<StatusCode> results, out ArrayOf<DiagnosticInfo> diagInfos);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task PublishCompleted_MoreNotifications_AssignsNextRequestAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            // Mark sub as publishing by manually triggering assignment
            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired();

            Task<ISubscription> task2 = queue.PublishAsync("channel2", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task2.IsCompleted, Is.False);

            // Complete first publish and state there's more notifications
            queue.PublishCompleted(subMock.Object, moreNotifications: true);

            ISubscription result = await task2.ConfigureAwait(false);
            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void PublishCompleted_NoMoreNotifications_SetsReadyToPublishFalse()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired(); // Gets assigned, sets ReadyToPublish = true, Publishing = true

            queue.PublishCompleted(subMock.Object, moreNotifications: false);

            Task<ISubscription> task2 = queue.PublishAsync("channel2", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task2.IsCompleted, Is.False); // Still incomplete because it's no longer ready
        }

        [Test]
        public void PublishTimerExpired_Idle_SetsReadyToPublishFalse()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // Make it ready
            queue.Requeue(subMock.Object);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.Idle);
            queue.PublishTimerExpired(); // Should set ReadyToPublish = false

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False); // Since it's idle, task is queued instead of fulfilled
        }

        [Test]
        public void TryPublishCustomStatus_BadStatus_CompletesRequestWithException()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            bool published = queue.TryPublishCustomStatus(StatusCodes.BadNotConnected);
            Assert.That(published, Is.True);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public void PublishAsync_TimesOut_ThrowsBadTimeout()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            DateTime timeout = DateTime.UtcNow.AddMilliseconds(-1000); // Already past timeout
            Task<ISubscription> task = queue.PublishAsync("channel1", timeout, false, null, CancellationToken.None);

            // Since operationTimeout < DateTime.MaxValue and timeOut <= 0, wait, timeOut calculation:
            // timeOut = operationTimeout.AddMilliseconds(500) - DateTime.UtcNow
            // If already past, timeout immediately? Let's use a short delay.
            DateTime timeout2 = DateTime.UtcNow.AddMilliseconds(1);
            Task<ISubscription> task2 = queue.PublishAsync("channel2", timeout2, false, null, CancellationToken.None);

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task2);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void RemoveQueuedRequests_NoSubscriptions_FailsRequests()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            // Force add a request bypassing normal validations by providing a sub then removing it without auto-removal
            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> task = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

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

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // Initial publish request with a specific channel ID
            Task<ISubscription> task = queue.PublishAsync("invalid_channel", DateTime.MaxValue, false, null, CancellationToken.None);

            // Mock the session to return false for this channel
            m_sessionMock.Setup(s => s.IsSecureChannelValid("invalid_channel")).Returns(false);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired(); // Triggers assignment

            ServiceResultException ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public async Task GetSubscriptionToPublish_SelectsHighestPriorityAndOldestAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var sub1 = new Mock<ISubscription>();
            sub1.Setup(s => s.Id).Returns(1);
            sub1.Setup(s => s.Priority).Returns(10);
            queue.Add(sub1.Object);

            await Task.Delay(15).ConfigureAwait(false); // Ensure older timestamp

            var sub3 = new Mock<ISubscription>();
            sub3.Setup(s => s.Id).Returns(3);
            sub3.Setup(s => s.Priority).Returns(20); // Same high priority, older
            queue.Add(sub3.Object);

            await Task.Delay(15).ConfigureAwait(false); // Ensure newer timestamp

            var sub2 = new Mock<ISubscription>();
            sub2.Setup(s => s.Id).Returns(2);
            sub2.Setup(s => s.Priority).Returns(20); // Highest priority, newer
            queue.Add(sub2.Object);

            // Make them ready sequentially
            queue.Requeue(sub1.Object);
            queue.Requeue(sub2.Object);
            queue.Requeue(sub3.Object);

            // Publish Request should return sub3 (highest priority, oldest)
            ISubscription result1 = await queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result1, Is.SameAs(sub3.Object));

            // Next should be sub2 (highest priority, newer)
            ISubscription result2 = await queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result2, Is.SameAs(sub2.Object));

            // Next should be sub1 (lower priority)
            ISubscription result3 = await queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result3, Is.SameAs(sub1.Object));
        }

        [Test]
        public async Task PublishAsync_RequeueTrue_AddsToFrontOfQueueAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, kMaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            Task<ISubscription> taskA = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
            Task<ISubscription> taskB = queue.PublishAsync("channel1", DateTime.MaxValue, true, null, CancellationToken.None);
            Task<ISubscription> taskC = queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);

            // First expiration should complete taskB because it was requeued (added to front)
            queue.PublishTimerExpired();
            ISubscription resultB = await taskB.ConfigureAwait(false);
            Assert.That(resultB, Is.SameAs(subMock.Object));
            Assert.That(taskA.IsCompleted, Is.False);
            Assert.That(taskC.IsCompleted, Is.False);

            queue.Remove(subMock.Object, false);

            // Need another subscription to fulfill the next request
            var subMock2 = new Mock<ISubscription>();
            subMock2.Setup(s => s.Id).Returns(2);
            subMock2.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock2.Object);

            // Second expiration should complete taskA (it was the first added with requeue=false)
            queue.PublishTimerExpired();
            ISubscription resultA = await taskA.ConfigureAwait(false);
            Assert.That(resultA, Is.SameAs(subMock2.Object));
            Assert.That(taskC.IsCompleted, Is.False);

            queue.Remove(subMock2.Object, false);

            // Need a third subscription to fulfill the last request
            var subMock3 = new Mock<ISubscription>();
            subMock3.Setup(s => s.Id).Returns(3);
            subMock3.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock3.Object);

            // Third expiration should complete taskC
            queue.PublishTimerExpired();
            ISubscription resultC = await taskC.ConfigureAwait(false);
            Assert.That(resultC, Is.SameAs(subMock3.Object));
        }

        [Test]
        public async Task Concurrency_MultipleRequestsAndSubscriptionsAsync()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, 100);

            const int numItems = 50;

            var subs = new List<Mock<ISubscription>>();
            for (int i = 0; i < numItems; i++)
            {
                var subMock = new Mock<ISubscription>();
                subMock.Setup(s => s.Id).Returns((uint)(i + 1));
                subMock.Setup(s => s.Priority).Returns((byte)(i % 5));
                subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
                subs.Add(subMock);
                queue.Add(subMock.Object);
            }

            using var startGate = new ManualResetEventSlim(false);
            var publishTasks = new List<Task<ISubscription>>();
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
            Task<ISubscription[]> resultsTask = Task.WhenAll(publishTasks);
            Task completedTask = await Task.WhenAny(resultsTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            queue.Close();

            if (completedTask != resultsTask)
            {
                Assert.Fail("Timed out waiting for publish tasks to complete.");
            }

            ISubscription[] results = await resultsTask.ConfigureAwait(false);

            // Verify results
            int validSubscriptions = 0;
            var returnedSubIds = new HashSet<uint>();
            foreach (ISubscription result in results)
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

        private static (Lock QueueLock, Lock SplitPublishLock) GetPrivateLocks(
            SessionPublishQueue queue)
        {
            FieldInfo[] lockFields = [.. typeof(SessionPublishQueue)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(Lock))];
            if (lockFields.Length is 0 or > 2)
            {
                throw new InvalidOperationException(
                    $"Expected one or two private Lock fields, found: " +
                    $"{string.Join(", ", lockFields.Select(field => field.Name))}.");
            }

            FieldInfo queueLockField = lockFields.FirstOrDefault(field => field.Name == "m_lock") ??
                (lockFields.Length == 1
                    ? lockFields[0]
                    : throw new InvalidOperationException(
                        $"Could not identify the queue lock from: " +
                        $"{string.Join(", ", lockFields.Select(field => field.Name))}."));
            var queueLock = (Lock)queueLockField.GetValue(queue);
            Lock splitPublishLock = lockFields
                .Where(field => !ReferenceEquals(field, queueLockField))
                .Select(field => (Lock)field.GetValue(queue))
                .SingleOrDefault();
            return (queueLock, splitPublishLock);
        }

        private static void WaitForSplitLockTransition(Lock splitPublishLock)
        {
            bool enteredByPublisher = SpinWait.SpinUntil(
                () =>
                {
                    if (!splitPublishLock.TryEnter())
                    {
                        return true;
                    }
                    splitPublishLock.Exit();
                    return false;
                },
                TimeSpan.FromSeconds(2));
            Assert.That(
                enteredByPublisher,
                Is.True,
                "PublishAsync did not enter the legacy split lock.");

            bool releasedByPublisher = SpinWait.SpinUntil(
                () =>
                {
                    if (!splitPublishLock.TryEnter())
                    {
                        return false;
                    }
                    splitPublishLock.Exit();
                    return true;
                },
                TimeSpan.FromSeconds(2));
            Assert.That(
                releasedByPublisher,
                Is.True,
                "PublishAsync did not leave the legacy split lock.");
        }
    }
}
