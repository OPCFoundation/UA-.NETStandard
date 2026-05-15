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
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Moq;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [MemoryDiagnoser]
    public class SessionPublishQueueBenchmarks
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<ISession> m_sessionMock;
        private Mock<ISubscriptionManager> m_subscriptionManagerMock;
        private ITelemetryContext m_telemetry;

        [Params(50, 500)]
        public int NumItems { get; set; }

        [GlobalSetup]
        public void Setup()
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

        [Benchmark]
        public async Task Concurrency_MultipleRequestsAndSubscriptions()
        {
            int maxPublishRequests = NumItems * 2;
            using var queue = new SessionPublishQueue(m_serverMock.Object, m_sessionMock.Object, maxPublishRequests);

            var subs = new List<Mock<ISubscription>>();
            for (int i = 0; i < NumItems; i++)
            {
                var subMock = new Mock<ISubscription>();
                subMock.Setup(s => s.Id).Returns((uint)(i + 1));
                subMock.Setup(s => s.Priority).Returns((byte)(i % 5));
                subMock.Setup(s => s.PublishTimerExpired()).Returns(PublishingState.NotificationsAvailable);
                subs.Add(subMock);
                queue.Add(subMock.Object);
            }

            using var startGate = new ManualResetEventSlim(false);
            var publishTasks = new List<Task<ISubscription>>(NumItems);
            var timerTasks = new List<Task>(NumItems);

            // Start multiple threads requesting publish
            for (int i = 0; i < NumItems; i++)
            {
                publishTasks.Add(Task.Run(() =>
                {
                    startGate.Wait();
                    return queue.PublishAsync("channel1", DateTime.MaxValue, false, CancellationToken.None);
                }));
            }

            // Start multiple threads mimicking subscriptions being ready or timer expiring
            for (int i = 0; i < NumItems; i++)
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
            await Task.WhenAny(resultsTask, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);

            queue.Close();
        }
    }
}
