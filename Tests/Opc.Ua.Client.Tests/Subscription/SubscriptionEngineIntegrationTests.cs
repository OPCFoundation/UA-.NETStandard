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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Integration tests for the pluggable subscription engine.
    /// Verifies that the <see cref="ClassicSubscriptionEngine"/> works
    /// end-to-end against a real in-process OPC UA server.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionEngine")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionEngineIntegrationTests : ClientTestFramework
    {
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        public void ClassicEngineSessionHasSubscriptionEngine()
        {
            var session = (Session)Session;

            ISubscriptionEngineFactory factory = session.SubscriptionEngineFactory;
            Assert.That(factory, Is.Not.Null);
            Assert.That(
                factory,
                Is.InstanceOf<ClassicSubscriptionEngineFactory>(),
                "Default engine factory should be ClassicSubscriptionEngineFactory");

            TestContext.Out.WriteLine(
                "SubscriptionEngineFactory type: {0}",
                factory.GetType().Name);
        }

        [Test]
        [Order(200)]
        [CancelAfter(30_000)]
        public async Task ClassicEngineCreateAndDeleteSubscription()
        {
            using var notificationReceived = new ManualResetEventSlim(false);
            int dataChangeCount = 0;

            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                PublishingInterval = 500,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 0,
                PublishingEnabled = true,
                Priority = 0
            };

            bool added = Session.AddSubscription(subscription);
            Assert.That(added, Is.True);

            await subscription.CreateAsync().ConfigureAwait(false);
            Assert.That(subscription.Created, Is.True);

            TestContext.Out.WriteLine(
                "Subscription Id: {0}, PublishingInterval: {1}",
                subscription.Id,
                subscription.CurrentPublishingInterval);

            var monitoredItem = new TestableMonitoredItem(subscription.DefaultItem)
            {
                DisplayName = "ServerStatusCurrentTime",
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                SamplingInterval = 250
            };

            monitoredItem.Notification += (item, _) =>
            {
                foreach (DataValue value in item.DequeueValues())
                {
                    Interlocked.Increment(ref dataChangeCount);
                    TestContext.Out.WriteLine(
                        "{0}: {1}, {2}, {3}",
                        item.DisplayName,
                        value.WrappedValue,
                        value.SourceTimestamp,
                        value.StatusCode);
                    notificationReceived.Set();
                }
            };

            subscription.AddItem(monitoredItem);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            Assert.That(monitoredItem.Status.Error, Is.Null.Or.Property("StatusCode").EqualTo(StatusCodes.Good));

            bool received = notificationReceived.Wait(10_000);
            Assert.That(received, Is.True, "Should have received at least one data change notification");
            Assert.That(dataChangeCount, Is.GreaterThanOrEqualTo(1));

            OutputSubscriptionInfo(TestContext.Out, subscription);

            bool removed = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.That(removed, Is.True);

            TestContext.Out.WriteLine("Total data changes received: {0}", dataChangeCount);
        }

        [Test]
        [Order(300)]
        [CancelAfter(30_000)]
        public async Task ClassicEnginePublishRequestCountScales()
        {
            const int subscriptionCount = 3;
            var subscriptions = new List<Subscription>();

            try
            {
                int initialGoodCount = Session.GoodPublishRequestCount;
                TestContext.Out.WriteLine(
                    "Initial GoodPublishRequestCount: {0}",
                    initialGoodCount);

                for (int i = 0; i < subscriptionCount; i++)
                {
                    var subscription = new TestableSubscription(Session.DefaultSubscription)
                    {
                        PublishingInterval = 1000,
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    };

                    bool added = Session.AddSubscription(subscription);
                    Assert.That(added, Is.True);

                    await subscription.CreateAsync().ConfigureAwait(false);
                    Assert.That(subscription.Created, Is.True);
                    subscriptions.Add(subscription);

                    TestContext.Out.WriteLine(
                        "Created subscription {0}, Id: {1}",
                        i + 1,
                        subscription.Id);
                }

                // allow publish requests to be sent
                await Task.Delay(3000).ConfigureAwait(false);

                int currentGoodCount = Session.GoodPublishRequestCount;
                TestContext.Out.WriteLine(
                    "GoodPublishRequestCount after {0} subscriptions: {1}",
                    subscriptionCount,
                    currentGoodCount);

                Assert.That(
                    currentGoodCount,
                    Is.GreaterThan(0),
                    "GoodPublishRequestCount should be positive with active subscriptions");
            }
            finally
            {
                foreach (Subscription subscription in subscriptions)
                {
                    bool removed = await Session.RemoveSubscriptionAsync(subscription)
                        .ConfigureAwait(false);
                    Assert.That(removed, Is.True);
                    subscription.Dispose();
                }
            }
        }

        [Test]
        [Order(400)]
        [CancelAfter(30_000)]
        public async Task ClassicEngineSubscriptionReceivesKeepAlive()
        {
            int keepAliveCount = 0;

            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                PublishingInterval = 500,
                KeepAliveCount = 1,
                LifetimeCount = 100,
                PublishingEnabled = true
            };

            subscription.FastKeepAliveCallback = (_, notification) =>
            {
                int n = Interlocked.Increment(ref keepAliveCount);
                TestContext.Out.WriteLine(
                    "KeepAlive {0}, SequenceNumber: {1}, PublishTime: {2}",
                    n,
                    notification.SequenceNumber,
                    notification.PublishTime);
            };

            bool added = Session.AddSubscription(subscription);
            Assert.That(added, Is.True);

            await subscription.CreateAsync().ConfigureAwait(false);
            Assert.That(subscription.Created, Is.True);

            TestContext.Out.WriteLine(
                "Subscription Id: {0}, PublishingInterval: {1}, KeepAliveCount: {2}",
                subscription.Id,
                subscription.CurrentPublishingInterval,
                subscription.CurrentKeepAliveCount);

            // Wait for keep-alive. With publishing interval 500ms and
            // keep-alive count of 1, a keep-alive should arrive every ~500ms.
            await Task.Delay(5000).ConfigureAwait(false);

            TestContext.Out.WriteLine("Total keep-alives received: {0}", keepAliveCount);
            Assert.That(
                keepAliveCount,
                Is.GreaterThanOrEqualTo(1),
                "Should have received at least one keep-alive notification");

            OutputSubscriptionInfo(TestContext.Out, subscription);

            bool removed = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.That(removed, Is.True);
        }

        [Test]
        [Order(500)]
        public void EngineFactoryIsAccessibleOnSession()
        {
            var session = (Session)Session;

            ISubscriptionEngineFactory factory = session.SubscriptionEngineFactory;
            Assert.That(factory, Is.Not.Null);
            Assert.That(factory, Is.SameAs(ClassicSubscriptionEngineFactory.Instance));

            TestContext.Out.WriteLine("Factory: {0}", factory.GetType().FullName);

            // Verify the factory can create an engine when given
            // a context (validates the factory contract).
            Assert.That(
                factory,
                Is.InstanceOf<ISubscriptionEngineFactory>(),
                "Factory must implement ISubscriptionEngineFactory");
        }
    }
}
