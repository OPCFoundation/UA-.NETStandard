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
using Moq;
using NUnit.Framework;
using Opc.Ua;
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
        private const int MaxPublishRequests = 10;

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
            Assert.Throws<ArgumentNullException>(() => new SessionPublishQueue(null, m_sessionMock.Object, MaxPublishRequests));
            Assert.Throws<ArgumentNullException>(() => new SessionPublishQueue(m_serverMock.Object, null, MaxPublishRequests));
        }

        [Test]
        public void PublishAsync_NoSubscriptions_ThrowsBadNoSubscription()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var ex = Assert.CatchAsync<ServiceResultException>(() => queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None));
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
            var task1 = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);
            Assert.That(task1.IsCompleted, Is.False);

            // Second publish request should fail because max queue size is 1
            var ex = Assert.CatchAsync<ServiceResultException>(() => queue.PublishAsync("channel2", DateTime.MaxValue, false, CancellationToken.None));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyPublishRequests));
        }

        [Test]
        public async Task PublishAsync_ReturnsSubscriptionIfReady()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            queue.Requeue(subMock.Object); // Sets ReadyToPublish to true

            var result = await queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public async Task Add_And_PublishTimerExpired_AssignsSubscriptionToRequest()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False);

            queue.PublishTimerExpired();

            var result = await task.ConfigureAwait(false);
            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void Close_ClearsQueuesAndSignalsSessionClosed()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            var subs = queue.Close();

            Assert.That(subs.Count, Is.EqualTo(1));
            Assert.That(subs[0], Is.SameAs(subMock.Object));
            subMock.Verify(s => s.SessionClosed(), Times.Once);

            var ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public void Remove_RemovesSubscription()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            queue.Remove(subMock.Object, removeQueuedRequests: true);

            var ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public async Task TryPublishCustomStatus_CompletesRemainingRequests()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task1 = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            bool published = queue.TryPublishCustomStatus(StatusCodes.Good);
            Assert.That(published, Is.True);

            var result = await task1.ConfigureAwait(false);
            Assert.That(result, Is.Null); // Good status completes with null to allow sending custom status

            bool publishedAgain = queue.TryPublishCustomStatus(StatusCodes.Good);
            Assert.That(publishedAgain, Is.False);
        }

        [Test]
        public void Acknowledge_ValidAcks_ReturnsGood()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            subMock.Setup(s => s.Acknowledge(It.IsAny<OperationContext>(), 10))
                   .Returns(StatusCodes.Good);

            queue.Add(subMock.Object);

            var acks = new SubscriptionAcknowledgementCollection
            {
                new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 10 }
            };

            var context = new Opc.Ua.Server.OperationContext(new RequestHeader(), null, RequestType.Publish, m_sessionMock.Object);

            queue.Acknowledge(context, acks, out var results, out var diagInfos);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(diagInfos.Count, Is.EqualTo(0));
        }

        [Test]
        public void Acknowledge_InvalidSubscription_ReturnsBadSubscriptionIdInvalid()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var acks = new SubscriptionAcknowledgementCollection
            {
                new SubscriptionAcknowledgement { SubscriptionId = 99, SequenceNumber = 10 }
            };

            var context = new Opc.Ua.Server.OperationContext(new RequestHeader(), null, RequestType.Publish, m_sessionMock.Object);

            queue.Acknowledge(context, acks, out var results, out var diagInfos);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task PublishCompleted_MoreNotifications_AssignsNextRequest()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            // Mark sub as publishing by manually triggering assignment
            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired();

            var task2 = queue.PublishAsync("channel2", DateTime.MaxValue, false, CancellationToken.None);
            Assert.That(task2.IsCompleted, Is.False);

            // Complete first publish and state there's more notifications
            queue.PublishCompleted(subMock.Object, moreNotifications: true);

            var result = await task2.ConfigureAwait(false);
            Assert.That(result, Is.SameAs(subMock.Object));
        }

        [Test]
        public void PublishCompleted_NoMoreNotifications_SetsReadyToPublishFalse()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired(); // Gets assigned, sets ReadyToPublish = true, Publishing = true

            queue.PublishCompleted(subMock.Object, moreNotifications: false);

            var task2 = queue.PublishAsync("channel2", DateTime.MaxValue, false, CancellationToken.None);
            Assert.That(task2.IsCompleted, Is.False); // Still incomplete because it's no longer ready
        }

        [Test]
        public void PublishTimerExpired_Idle_SetsReadyToPublishFalse()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // Make it ready
            queue.Requeue(subMock.Object);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.Idle);
            queue.PublishTimerExpired(); // Should set ReadyToPublish = false

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.False); // Since it's idle, task is queued instead of fulfilled
        }

        [Test]
        public async Task TryPublishCustomStatus_BadStatus_CompletesRequestWithException()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            bool published = queue.TryPublishCustomStatus(StatusCodes.BadNotConnected);
            Assert.That(published, Is.True);

            var ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task PublishAsync_TimesOut_ThrowsBadTimeout()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var timeout = DateTime.UtcNow.AddMilliseconds(-1000); // Already past timeout
            var task = queue.PublishAsync("channel1", timeout, false, CancellationToken.None);

            // Since operationTimeout < DateTime.MaxValue and timeOut <= 0, wait, timeOut calculation:
            // timeOut = operationTimeout.AddMilliseconds(500) - DateTime.UtcNow
            // If already past, timeout immediately? Let's use a short delay.
            var timeout2 = DateTime.UtcNow.AddMilliseconds(1);
            var task2 = queue.PublishAsync("channel2", timeout2, false, CancellationToken.None);

            var ex = Assert.CatchAsync<ServiceResultException>(() => task2);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void RemoveQueuedRequests_NoSubscriptions_FailsRequests()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            // Force add a request bypassing normal validations by providing a sub then removing it without auto-removal
            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            var task = queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);

            queue.Remove(subMock.Object, removeQueuedRequests: false);
            Assert.That(task.IsCompleted, Is.False);

            queue.RemoveQueuedRequests();

            var ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public void AssignSubscriptionToRequest_InvalidSecureChannel_ThrowsBadSecureChannelIdInvalid()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1);
            queue.Add(subMock.Object);

            // Initial publish request with a specific channel ID
            var task = queue.PublishAsync("invalid_channel", DateTime.MaxValue, false, CancellationToken.None);

            // Mock the session to return false for this channel
            m_sessionMock.Setup(s => s.IsSecureChannelValid("invalid_channel")).Returns(false);

            subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
            queue.PublishTimerExpired(); // Triggers assignment

            var ex = Assert.CatchAsync<ServiceResultException>(() => task);
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public async Task GetSubscriptionToPublish_SelectsHighestPriorityAndOldest()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, MaxPublishRequests);

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
            var result1 = await queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result1, Is.SameAs(sub3.Object));

            // Next should be sub2 (highest priority, newer)
            var result2 = await queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result2, Is.SameAs(sub2.Object));

            // Next should be sub1 (lower priority)
            var result3 = await queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result3, Is.SameAs(sub1.Object));
        }

        [Test]
        public async Task Concurrency_MultipleRequestsAndSubscriptions()
        {
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, 100);

            int numItems = 50;

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
                    return queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);
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
            var resultsTask = Task.WhenAll(publishTasks);
            var completedTask = await Task.WhenAny(resultsTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            queue.Close();

            if (completedTask != resultsTask)
            {
                Assert.Fail("Timed out waiting for publish tasks to complete.");
            }

            var results = await resultsTask.ConfigureAwait(false);

            // Verify results
            int validSubscriptions = 0;
            var returnedSubIds = new HashSet<uint>();
            foreach (var result in results)
            {
                if (result != null)
                {
                    validSubscriptions++;
                    returnedSubIds.Add(result.Id);
                }
            }

            Assert.That(validSubscriptions, Is.EqualTo(numItems), "All publish requests should have received a subscription.");
            Assert.That(returnedSubIds.Count, Is.EqualTo(numItems), "All subscriptions should have been processed.");
        }
    }
}
