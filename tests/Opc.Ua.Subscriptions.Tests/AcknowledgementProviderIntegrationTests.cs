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

// CA2016: integration tests intentionally call cleanup in finally without forwarding the test
// cancellation token, which may already be cancelled when cleanup must still run.
#pragma warning disable CA2016

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using ClassicSubscription = Opc.Ua.Client.Subscription;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// End-to-end tests of the acknowledgement provider model against the in-process
    /// reference server: in confirmation mode a notification is only acknowledged after the
    /// application confirmed its handle, on the V2 engine and on the classic engine.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class AcknowledgementProviderIntegrationTests : ClientTestFramework
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
        [CancelAfter(90_000)]
        public async Task ConfirmedNotificationsAreAcknowledgedOnTheV2EngineAsync(CancellationToken ct)
        {
            var observer = new RecordingObserver();
            ManagedSessionType session = await ConnectAsync(
                nameof(ConfirmedNotificationsAreAcknowledgedOnTheV2EngineAsync), observer, classic: false, ct)
                .ConfigureAwait(false);
            var handler = new HandleCollectingHandler();
            try
            {
                Assert.That(session.Mode, Is.EqualTo(AcknowledgementMode.AfterConfirmation));
                ISubscription subscription = session.AddSubscription(handler, new Client.Subscriptions.SubscriptionOptions
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(200),
                    PublishingEnabled = true,
                    KeepAliveCount = 10,
                    LifetimeCount = 100
                });
                Assert.That(subscription.TryAddMonitoredItem(
                    "CurrentTime",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(100), QueueSize = 10 },
                    out _), Is.True);

                await WaitForAsync(() => handler.Handles.Count >= 3, ct).ConfigureAwait(false);
                AcknowledgementHandle[] received = [.. handler.Handles.Take(3)];
                Assert.That(received.All(handle => !handle.IsNull), Is.True);

                // Unconfirmed notifications stay unacknowledged for several publish cycles.
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                Assert.That(received.Any(observer.WasAcknowledged), Is.False,
                    "Nothing is acknowledged before the application confirmed it.");

                foreach (AcknowledgementHandle handle in received)
                {
                    session.Confirm(handle);
                }
                await WaitForAsync(() => received.All(observer.WasAcknowledged), ct).ConfigureAwait(false);

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(90_000)]
        public async Task ConfirmedNotificationsAreAcknowledgedOnTheClassicEngineAsync(CancellationToken ct)
        {
            var observer = new RecordingObserver();
            ManagedSessionType session = await ConnectAsync(
                nameof(ConfirmedNotificationsAreAcknowledgedOnTheClassicEngineAsync), observer, classic: true, ct)
                .ConfigureAwait(false);
            var handles = new ConcurrentQueue<AcknowledgementHandle>();
            try
            {
                var subscription = new ClassicSubscription(Telemetry)
                {
                    PublishingEnabled = true,
                    PublishingInterval = 200,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
#pragma warning disable CS0618 // The classic engine is exercised through its fast callback.
                    FastDataChangeCallback = (owner, notification, _) =>
                        {
                            if (owner.TryGetAcknowledgementHandle(notification, out AcknowledgementHandle handle))
                            {
                                handles.Enqueue(handle);
                            }
                        }
                };
#pragma warning restore CS0618
                subscription.AddItem(new MonitoredItem(Telemetry)
                {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = 100,
                    QueueSize = 10
                });
                Assert.That(session.AddSubscription(subscription), Is.True);
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                await WaitForAsync(() => handles.Count >= 3, ct).ConfigureAwait(false);
                AcknowledgementHandle[] received = [.. handles.Take(3)];

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                Assert.That(received.Any(observer.WasAcknowledged), Is.False,
                    "Nothing is acknowledged before the application confirmed it.");

                foreach (AcknowledgementHandle handle in received)
                {
                    session.Confirm(handle);
                }
                await WaitForAsync(() => received.All(observer.WasAcknowledged), ct).ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task<ManagedSessionType> ConnectAsync(
            string name,
            RecordingObserver observer,
            bool classic,
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(name)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .AcknowledgeAfterConfirmation()
                .WithAcknowledgementObserver(observer);
            if (classic)
            {
                builder.UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance);
            }
            return await builder.ConnectAsync(ct).ConfigureAwait(false);
        }

        private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!predicate())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Timed out waiting for the expected condition.");
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        private sealed class RecordingObserver : ISubscriptionAcknowledgementObserver
        {
            public bool WasAcknowledged(AcknowledgementHandle handle)
            {
                return m_acknowledged.ContainsKey(handle);
            }

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    if (outcome.Disposition == AcknowledgementDisposition.Acknowledged)
                    {
                        m_acknowledged.TryAdd(outcome.Handle, 0);
                    }
                }
                return default;
            }

            private readonly ConcurrentDictionary<AcknowledgementHandle, byte> m_acknowledged = new();
        }

        private sealed class HandleCollectingHandler : ISubscriptionNotificationHandler
        {
            public ConcurrentQueue<AcknowledgementHandle> Handles { get; } = new();

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                if (notification.Length > 0)
                {
                    Handles.Enqueue(notification.Span[0].Acknowledgement);
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
