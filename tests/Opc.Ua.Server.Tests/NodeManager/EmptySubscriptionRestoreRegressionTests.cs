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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class EmptySubscriptionRestoreRegressionTests
    {
        [Test]
        public async Task RestoreSubscriptionWithoutMonitoredItemsSucceedsAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                server.SetupGet(value => value.IsRunning).Returns(false);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                server.SetupGet(value => value.DiagnosticsNodeManager)
                    .Returns(new Mock<IDiagnosticsNodeManager>().Object);

                var stored = new StoredSubscription
                {
                    Id = 41,
                    IsDurable = true,
                    PublishingInterval = 1000,
                    MaxKeepaliveCount = 10,
                    MaxLifetimeCount = 600,
                    LifetimeCounter = 7,
                    MaxMessageCount = 10,
                    MaxNotificationsPerPublish = 100,
                    SequenceNumber = 1,
                    Priority = 5,
                    UserIdentityToken = new AnonymousIdentityToken(),
                    SentMessages = [],
                    MonitoredItems = []
                };
                var store = new Mock<ISubscriptionStore>();
                store.Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(true, [stored]));
                Dictionary<uint, ArrayOf<uint>> restoredIds = null;
                store.Setup(value => value.OnSubscriptionRestoreCompleteAsync(
                        It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<Dictionary<uint, ArrayOf<uint>>, CancellationToken>(
                        (ids, _) => restoredIds = ids)
                    .Returns(default(ValueTask));
                server.SetupGet(value => value.SubscriptionStore).Returns(store.Object);

                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        DurableSubscriptionsEnabled = true,
                        MaxSubscriptionCount = 10,
                        MaxDurableSubscriptionLifetimeInHours = 24
                    }
                };
                using var master = new MasterNodeManager(server.Object, configuration, null, [], null);
                server.SetupGet(value => value.NodeManager).Returns(master);
                using var manager = new SubscriptionManager(server.Object, configuration, new FakeTimeProvider());

                await manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

                IList<ISubscription> subscriptions = manager.GetSubscriptions();
                Assert.That(subscriptions, Has.Count.EqualTo(1));
                ISubscription subscription = subscriptions[0];
                IStoredSubscription persisted = subscription.ToStorableSubscription();
                Assert.Multiple(() =>
                {
                    Assert.That(subscription.Id, Is.EqualTo(41));
                    Assert.That(subscription.MonitoredItemCount, Is.Zero);
                    Assert.That(subscription.IsDurable, Is.True);
                    Assert.That(subscription.IsDeleted, Is.False);
                    Assert.That(subscription.Diagnostics.SubscriptionId, Is.EqualTo(41));
                    Assert.That(subscription.Diagnostics.MonitoredItemCount, Is.Zero);
                    Assert.That(persisted.LifetimeCounter, Is.EqualTo(7));
                    Assert.That(persisted.MaxLifetimeCount, Is.EqualTo(stored.MaxLifetimeCount));
                    Assert.That(persisted.MonitoredItems, Is.Empty);
                    Assert.That(restoredIds, Contains.Key(41u));
                    Assert.That(restoredIds[41], Is.Empty);
                });
            }
        }
    }
}
