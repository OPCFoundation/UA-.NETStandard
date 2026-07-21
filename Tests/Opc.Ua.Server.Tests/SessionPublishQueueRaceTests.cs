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
    [NonParallelizable]
    public sealed class SessionPublishQueueRaceTests
    {
        [Test]
        [CancelAfter(10000)]
        public async Task PublishReadyTransitionCompletesQueuedRequestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var subscriptionManager = new Mock<ISubscriptionManager>();
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.SubscriptionManager).Returns(subscriptionManager.Object);

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            session.Setup(s => s.IsSecureChannelValid(It.IsAny<string>())).Returns(true);

            using var queue = new SessionPublishQueue(server.Object, session.Object, 10);
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var subscriptionManager = new Mock<ISubscriptionManager>();
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.SubscriptionManager).Returns(subscriptionManager.Object);

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            session.Setup(s => s.IsSecureChannelValid(It.IsAny<string>())).Returns(true);

            using var queue = new SessionPublishQueue(server.Object, session.Object, 10);
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
                    CancellationToken.None).ConfigureAwait(false),
                Is.SameAs(subscription.Object));

            Task<ISubscription> publishTask = queue.PublishAsync(
                "channel1",
                DateTime.MaxValue,
                false,
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
                "The timer did not assign the already-ready Subscription to the queued Publish request.");
            Assert.That(
                await publishTask.ConfigureAwait(false),
                Is.SameAs(subscription.Object));
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
