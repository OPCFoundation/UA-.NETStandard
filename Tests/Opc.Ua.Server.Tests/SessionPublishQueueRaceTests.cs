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

            Lock queueLock = GetPrivateLock(queue, "m_lock");
            Lock splitPublishLock = TryGetPrivateLock(queue, "m_subscriptionPublishLock");
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
                    Thread.Sleep(100);
                    splitPublishLock.Enter();
                    splitPublishLock.Exit();
                }
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

        private static Lock GetPrivateLock(SessionPublishQueue queue, string fieldName)
        {
            return TryGetPrivateLock(queue, fieldName) ??
                throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        }

        private static Lock TryGetPrivateLock(SessionPublishQueue queue, string fieldName)
        {
            return typeof(SessionPublishQueue)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(queue) as Lock;
        }
    }
}
