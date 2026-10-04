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

using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies durable lifetime-unit conversion and publish errors for sessions without a publish queue.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    public sealed class DurableKeepAliveRegressionTests
    {
        /// <summary>
        /// Verifies that durable keep-alive limits convert configured hours to milliseconds before revising counts.
        /// </summary>
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

        /// <summary>
        /// Verifies that long durable lifetimes are capped correctly without overflowing a 32-bit millisecond value.
        /// </summary>
        [Test]
        [TestCase(1193, 1.0, 4_294_800_000u)]
        [TestCase(1194, 1.0, uint.MaxValue)]
        public void DurableLifetimeConversionDoesNotOverflowUInt32Milliseconds(
            int hours,
            double interval,
            uint expected)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new RevisionHooks(server.Object, hours))
            {
                Assert.That(manager.Lifetime(interval, 1, uint.MaxValue), Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// Verifies the existing 2000-hour lifetime boundary remains exact.
        /// </summary>
        [Test]
        public void DurableLifetimeConversionPreservesExistingBoundary()
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

        /// <summary>
        /// Verifies that a missing publish queue distinguishes a closing session from one without subscriptions.
        /// </summary>
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

        /// <summary>
        /// Exposes subscription count revision using a configurable maximum durable lifetime.
        /// </summary>
        private sealed class RevisionHooks : SubscriptionManager
        {
            /// <summary>
            /// Creates a manager with durable subscriptions enabled and the requested lifetime limit in hours.
            /// </summary>
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

            /// <summary>
            /// Revises a keep-alive count for the supplied publishing interval and durability mode.
            /// </summary>
            public uint KeepAlive(double interval, uint count, bool durable)
            {
                return CalculateKeepAliveCount(interval, count, durable);
            }

            /// <summary>
            /// Revises a durable lifetime count using the supplied publishing interval and keep-alive count.
            /// </summary>
            public uint Lifetime(double interval, uint keepAlive, uint count)
            {
                return CalculateLifetimeCount(interval, keepAlive, count, true);
            }
        }
    }
}
