// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        private Mock<IServerInternal> m_serverMock = null!;
        private Mock<ISession> m_sessionMock = null!;
        private Mock<ISubscriptionManager> m_subscriptionManagerMock = null!;
        private ITelemetryContext m_telemetry = null!;

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

            var subs = new List<Mock<ISubscriptionPublishPipeline>>();
            for (int i = 0; i < NumItems; i++)
            {
                var subMock = new Mock<ISubscriptionPublishPipeline>();
                subMock.Setup(s => s.Id).Returns((uint)(i + 1));
                subMock.Setup(s => s.Priority).Returns((byte)(i % 5));
                subMock.Setup(s => s.PublishTimerExpired(It.IsAny<bool>())).Returns(PublishingState.NotificationsAvailable);
                subs.Add(subMock);
                queue.Add(subMock.Object);
            }

            using var startGate = new ManualResetEventSlim(false);
            var publishTasks = new List<Task<ISubscriptionPublishPipeline>>(NumItems);
            var timerTasks = new List<Task>(NumItems);

            // Start multiple threads requesting publish
            for (int i = 0; i < NumItems; i++)
            {
                publishTasks.Add(Task.Run(() =>
                {
                    startGate.Wait();
                    return queue.PublishAsync("channel1", DateTime.MaxValue, false, null, CancellationToken.None);
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
            Task<ISubscriptionPublishPipeline[]> resultsTask = Task.WhenAll(publishTasks);
            await Task.WhenAny(resultsTask, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);

            queue.Close();
        }
    }
}
