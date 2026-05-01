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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;
using V2 = Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// End-to-end integration tests for <see cref="ManagedSessionBuilder"/>
    /// + <see cref="ManagedSessionSubscriptionExtensions"/> against the
    /// in-process reference fixture server. Verifies that the V2
    /// subscription engine, exposed through the new
    /// <see cref="ManagedSession.SubscriptionManager"/> property, can
    /// drive subscriptions and deliver data change notifications.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [Category("ManagedSession")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ManagedSessionSubscriptionManagerIntegrationTests
        : ClientTestFramework
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
        public async Task BuilderCreatesManagedSessionWithV2Engine(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(BuilderCreatesManagedSessionWithV2Engine))
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            try
            {
                Assert.That(session, Is.Not.Null);
                Assert.That(session.Connected, Is.True);

                ISubscriptionManager manager = session.SubscriptionManager;
                Assert.That(manager, Is.Not.Null);
                Assert.That(manager.Count, Is.EqualTo(0));

                TestContext.Out.WriteLine(
                    "ManagedSession connected, SessionId: {0}",
                    session.SessionId);
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
        public async Task BuilderAddSubscriptionCreatesServerSubscription(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(BuilderAddSubscriptionCreatesServerSubscription))
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            var handler = new RecordingHandler();
            try
            {
                V2.ISubscription subscription = session.AddSubscription(
                    handler,
                    new V2.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        Priority = 0
                    });

                Assert.That(subscription, Is.Not.Null);
                Assert.That(session.SubscriptionManager.Count, Is.EqualTo(1));

                // Wait for the subscription state machine to create
                // the subscription on the server (asynchronously after
                // Add returns).
                bool created = await WaitForAsync(
                    () => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(
                    created,
                    Is.True,
                    "Subscription should be created on the server");

                TestContext.Out.WriteLine(
                    "V2 subscription created: PublishingInterval={0}, KeepAliveCount={1}, LifetimeCount={2}",
                    subscription.CurrentPublishingInterval,
                    subscription.CurrentKeepAliveCount,
                    subscription.CurrentLifetimeCount);

                Assert.That(
                    subscription.CurrentPublishingInterval,
                    Is.EqualTo(TimeSpan.FromMilliseconds(500)));

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                {
                    return true;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return predicate();
        }

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task ClassicEngineThrowsWhenAccessingSubscriptionManager(
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(nameof(ClassicEngineThrowsWhenAccessingSubscriptionManager))
                .UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance)
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.Throws<InvalidOperationException>(
                    () => _ = session.SubscriptionManager);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private sealed class RecordingHandler : ISubscriptionNotificationHandler
        {
            private readonly TaskCompletionSource<bool> m_firstData
                = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int DataChangeCount;
            public int KeepAliveCount;
            public int EventCount;

            public ValueTask OnDataChangeNotificationAsync(
                V2.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                Interlocked.Add(ref DataChangeCount, notification.Length);
                m_firstData.TrySetResult(true);
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                V2.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                Interlocked.Add(ref EventCount, notification.Length);
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                V2.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                Interlocked.Increment(ref KeepAliveCount);
                return default;
            }

            public async Task<bool> WaitForDataAsync(
                TimeSpan timeout, CancellationToken ct)
            {
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource
                    .CreateLinkedTokenSource(timeoutCts.Token, ct);
                using (linked.Token.Register(() => m_firstData.TrySetResult(false)))
                {
                    return await m_firstData.Task.ConfigureAwait(false);
                }
            }
        }
    }
}
