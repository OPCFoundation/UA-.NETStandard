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

#nullable enable

#pragma warning disable CA2016

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// V2-engine counterparts to the four classic-engine integration
    /// tests in <c>SubscriptionEngineIntegrationTests</c> (Classic
    /// project). Each test creates its own <see cref="ManagedSession"/>
    /// via <see cref="ManagedSessionBuilder"/> (which defaults to the
    /// V2 engine) so the test does not depend on the inherited
    /// <see cref="ClientTestFramework.Session"/> (whose engine factory
    /// is still classic in the base fixture default for back-compat).
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionEngine")]
    [Category("V2")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionEngineIntegrationTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        [CancelAfter(60_000)]
        public async Task V2EngineSessionHasV2EngineAsync(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(V2EngineSessionHasV2EngineAsync), ct)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
                ISubscriptionManager manager = session.RequireSubscriptionManager();
                Assert.That(manager, Is.Not.Null,
                    "V2 session must expose ISubscriptionManager");
                Assert.That(manager.Count, Is.Zero);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task V2EngineCreateAndDeleteSubscriptionAsync(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(V2EngineCreateAndDeleteSubscriptionAsync), ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(
                    handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 0
                    });

                Assert.That(session.RequireSubscriptionManager().Count, Is.EqualTo(1));

                bool created = await WaitForAsync(
                    () => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True,
                    "Subscription should be created on the server");

                TestContext.Out.WriteLine(
                    "V2 subscription PublishingInterval={0}, KeepAliveCount={1}, LifetimeCount={2}",
                    subscription.CurrentPublishingInterval,
                    subscription.CurrentKeepAliveCount,
                    subscription.CurrentLifetimeCount);

                NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;
                bool added = subscription.TryAddMonitoredItem(
                    "ServerStatusCurrentTime",
                    nodeId,
                    o => o with
                    {
                        SamplingInterval = TimeSpan.FromMilliseconds(250)
                    },
                    out _);
                Assert.That(added, Is.True);

                bool firstData = await handler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(firstData, Is.True,
                    "V2 handler should have received at least one data change");
                Assert.That(handler.DataChangeCount, Is.GreaterThanOrEqualTo(1));

                TestContext.Out.WriteLine("Total V2 data changes received: {0}",
                    handler.DataChangeCount);

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task V2EnginePublishRequestCountScalesAsync(CancellationToken ct)
        {
            const int subscriptionCount = 3;
            ManagedSession session = await ConnectV2Async(
                nameof(V2EnginePublishRequestCountScalesAsync), ct).ConfigureAwait(false);
            try
            {
                ISubscriptionManager manager = session.RequireSubscriptionManager();
                int initial = manager.GoodPublishRequestCount;
                TestContext.Out.WriteLine("Initial V2 GoodPublishRequestCount: {0}",
                    initial);

                var subscriptions = new ISubscription[subscriptionCount];
                var handlers = new RecordingSubscriptionHandler[subscriptionCount];
                for (int i = 0; i < subscriptionCount; i++)
                {
                    handlers[i] = new RecordingSubscriptionHandler();
                    subscriptions[i] = session.AddSubscription(handlers[i],
                        new Client.Subscriptions.SubscriptionOptions
                        {
                            PublishingInterval = TimeSpan.FromMilliseconds(1000),
                            KeepAliveCount = 10,
                            LifetimeCount = 100,
                            PublishingEnabled = true
                        });
                }

                for (int i = 0; i < subscriptionCount; i++)
                {
                    int index = i;
                    bool created = await WaitForAsync(
                        () => subscriptions[index].Created,
                        TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    Assert.That(created, Is.True,
                        $"Subscription {i} should be created");
                }

                // allow publish requests to flow for a couple of cycles
                await Task.Delay(3000, ct).ConfigureAwait(false);

                int current = manager.GoodPublishRequestCount;
                TestContext.Out.WriteLine(
                    "V2 GoodPublishRequestCount after {0} subscriptions: {1}",
                    subscriptionCount, current);
                Assert.That(current, Is.GreaterThan(0),
                    "V2 manager should be issuing publish requests");

                for (int i = 0; i < subscriptionCount; i++)
                {
                    await subscriptions[i].DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(400)]
        [CancelAfter(60_000)]
        public async Task V2EngineSubscriptionReceivesKeepAliveAsync(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(V2EngineSubscriptionReceivesKeepAliveAsync), ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 1,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });

                bool created = await WaitForAsync(
                    () => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                bool firstKA = await handler.WaitForFirstKeepAliveAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(firstKA, Is.True,
                    "Should have received at least one V2 keep-alive notification");
                Assert.That(handler.KeepAliveCount, Is.GreaterThanOrEqualTo(1));

                TestContext.Out.WriteLine("Total V2 keep-alives: {0}",
                    handler.KeepAliveCount);

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task<ManagedSession> ConnectV2Async(
            string sessionName, CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            return await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .ConnectAsync(ct)
                .ConfigureAwait(false);
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> predicate, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return true;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return predicate();
        }
    }
}
