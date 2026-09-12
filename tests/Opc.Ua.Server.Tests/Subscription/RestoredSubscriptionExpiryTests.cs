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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    public sealed class RestoredSubscriptionExpiryTests
    {
        [Test]
        public async Task RestoredDurableSubscriptionExpiresWithoutAReconnectingClientAsync()
        {
            var clock = new FakeTimeProvider();
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory, clock);
            using (queueFactory)
            {
                server.SetupGet(value => value.IsRunning).Returns(false);
                server.SetupGet(value => value.DiagnosticsNodeManager)
                    .Returns(Mock.Of<IDiagnosticsNodeManager>());
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(Mock.Of<ICoreNodeManager>());
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var stored = new StoredSubscription
                {
                    Id = 41,
                    IsDurable = true,
                    PublishingInterval = 1_000,
                    MaxKeepaliveCount = 1,
                    MaxLifetimeCount = 3,
                    LifetimeCounter = 2,
                    MaxMessageCount = 10,
                    SequenceNumber = 1,
                    UserIdentityToken = new AnonymousIdentityToken(),
                    SentMessages = [],
                    MonitoredItems = []
                };
                var store = new Mock<ISubscriptionStore>();
                store.Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(true, [stored]));
                server.SetupGet(value => value.SubscriptionStore).Returns(store.Object);
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        DurableSubscriptionsEnabled = true,
                        MaxSubscriptionCount = 10,
                        MinSubscriptionLifetime = 0,
                        MaxDurableSubscriptionLifetimeInHours = 24
                    }
                };
                using var master = new MasterNodeManager(server.Object, configuration, null, [], null);
                server.SetupGet(value => value.NodeManager).Returns(master);
                using var manager = new SubscriptionManager(server.Object, configuration, clock);
                var deleted = new TaskCompletionSource<StatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
                server.Setup(value => value.DeleteSubscriptionAsync(
                        It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                    .Returns<uint, CancellationToken>(async (id, ct) =>
                    {
                        try
                        {
                            StatusCode status = await manager.DeleteSubscriptionAsync(null!, id, ct)
                                .ConfigureAwait(false);
                            deleted.TrySetResult(status);
                        }
                        catch (Exception exception)
                        {
                            deleted.TrySetException(exception);
                            throw;
                        }
                    });

                await manager.RestoreSubscriptionsAsync().ConfigureAwait(false);
                ISubscription restored = manager.GetSubscriptions()[0];
                Assert.That(manager.CaptureAbandonedPublishTimerSnapshot(), Has.Count.EqualTo(1));
                clock.Advance(TimeSpan.FromMilliseconds(1_001));
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());
                Assert.That(deleted.Task.IsCompleted, Is.False);
                Assert.That(restored.ToStorableSubscription().LifetimeCounter, Is.EqualTo(2));

                clock.Advance(TimeSpan.FromMilliseconds(1_000));
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());
                StatusCode result = await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.GetSubscriptions(), Is.Empty);
                Assert.That(manager.CaptureAbandonedPublishTimerSnapshot(), Is.Empty);
                Assert.That(restored.IsDeleted, Is.True);
            }
        }
    }
}
