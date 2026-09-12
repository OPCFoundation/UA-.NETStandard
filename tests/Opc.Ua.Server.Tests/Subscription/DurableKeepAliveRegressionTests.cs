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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    public sealed class DurableKeepAliveRegressionTests
    {
        [TestCase(1, 1000.0, 0u, 3u)]
        [TestCase(1, 1000.0, 10u, 10u)]
        [TestCase(1, 1000.0, 3599u, 3599u)]
        [TestCase(1, 1000.0, 3600u, 3600u)]
        [TestCase(1, 1000.0, 3601u, 3600u)]
        [TestCase(24, 1000.0, 10u, 10u)]
        [TestCase(2000, 1000.0, 10u, 10u)]
        [TestCase(int.MaxValue, 1000.0, 10u, 10u)]
        public void DurableKeepAliveUsesMillisecondsInsteadOfHours(
            int hours, double interval, uint requested, uint expected)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new RevisionHooks(server.Object, hours))
            {
                Assert.That(manager.KeepAlive(interval, requested, true), Is.EqualTo(expected));
                Assert.That(manager.KeepAlive(interval, 10, false), Is.EqualTo(10));
            }
        }

        [Test]
        public void DurableLifetimeConversionDoesNotOverflowUInt32Milliseconds()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new RevisionHooks(server.Object, 2000))
            {
                Assert.That(manager.Lifetime(1000, 1, 7_199_999), Is.EqualTo(7_199_999));
                Assert.That(manager.Lifetime(1000, 1, 7_200_000), Is.EqualTo(7_200_000));
                Assert.That(manager.Lifetime(1000, 1, 7_200_001), Is.EqualTo(7_200_000));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PublishMissingQueueKeepsClosingSessionDistinct(bool closing)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new RevisionHooks(server.Object, 24))
            {
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(1, 1));
                session.SetupGet(value => value.IsClosing).Returns(closing);
                using var context = new OperationContext(session.Object, DiagnosticsMasks.None);
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.PublishAsync(context, [], null).ConfigureAwait(false));
                Assert.That(error.StatusCode,
                    Is.EqualTo(closing ? StatusCodes.BadSessionClosed : StatusCodes.BadNoSubscription));
            }
        }

        private sealed class RevisionHooks : SubscriptionManager
        {
            public RevisionHooks(IServerInternal server, int hours)
                : base(server, new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxDurableSubscriptionLifetimeInHours = hours,
                        MaxPublishingInterval = int.MaxValue,
                        MaxSubscriptionLifetime = 60000,
                        MinSubscriptionLifetime = 0,
                        DurableSubscriptionsEnabled = true
                    }
                })
            {
            }

            public uint KeepAlive(double interval, uint count, bool durable)
            {
                return CalculateKeepAliveCount(interval, count, durable);
            }

            public uint Lifetime(double interval, uint keepAlive, uint count)
            {
                return CalculateLifetimeCount(interval, keepAlive, count, true);
            }
        }
    }
}
