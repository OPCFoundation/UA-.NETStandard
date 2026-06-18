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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// V2 live <c>ConditionRefresh</c> against the reference server.
    /// Subscribes to events on the <c>Server</c> object, invokes
    /// <see cref="Client.Subscriptions.MonitoredItems.IMonitoredItem.ConditionRefreshAsync"/>, and
    /// asserts the standard
    /// <c>RefreshStartEventType</c> / <c>RefreshEndEventType</c>
    /// boundary events flow through the V2 handler. Per OPC UA Part 9
    /// §4.5, ConditionRefresh always emits these boundary events
    /// regardless of whether the server has active conditions.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("ConditionRefresh")]
    [Category("LiveAlarms")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class MonitoredItemConditionRefreshLiveTests : ClientTestFramework
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
        public async Task ConditionRefreshObservesRefreshBoundaryEventsV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(ConditionRefreshObservesRefreshBoundaryEventsV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                // EventType is captured as Fields[0] via a SimpleAttributeOperand
                // (BrowseName=EventType). The handler records every observed
                // EventType NodeId so the test can match RefreshStart/End.
                var handler = new RefreshEventHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false), Is.True);

                var eventFilter = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                            AttributeId = Attributes.Value
                        },
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            BrowsePath = [new QualifiedName(BrowseNames.SourceName)],
                            AttributeId = Attributes.Value
                        }
                    ],
                    WhereClause = new ContentFilter()
                };

                Assert.That(sub.TryAddMonitoredItem(
                    "ServerEvents",
                    ObjectIds.Server,
                    o => o with
                    {
                        AttributeId = Attributes.EventNotifier,
                        SamplingInterval = TimeSpan.Zero,
                        QueueSize = 200,
                        Filter = eventFilter
                    },
                    out Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                Assert.That(item, Is.Not.Null);
                bool itemCreated = await WaitForAsync(() => item!.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(itemCreated, Is.True);

                // Per-item ConditionRefresh — the new V2 API. Server
                // must respond with RefreshStartEvent followed (after
                // any active conditions) by RefreshEndEvent for the
                // (subscriptionId, monitoredItemId) pair.
                await item!.ConditionRefreshAsync(ct).ConfigureAwait(false);

                bool sawStart = await WaitForAsync(
                    () => handler.SawRefreshStart,
                    TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                bool sawEnd = await WaitForAsync(
                    () => handler.SawRefreshEnd,
                    TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(sawStart, Is.True,
                        "Expected RefreshStartEventType from the V2 handler " +
                        "after per-item ConditionRefresh");
                    Assert.That(sawEnd, Is.True,
                        "Expected RefreshEndEventType from the V2 handler " +
                        "after per-item ConditionRefresh");
                });

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await session.CloseAsync().ConfigureAwait(false);
                }
                catch
                { /* best effort */
                }
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch
                { /* best effort */
                }
            }
        }

        /// <summary>
        /// Handler that scans the first <see cref="EventNotification.Fields"/>
        /// entry (mapped to <c>EventType</c> by the test's event filter
        /// SelectClauses[0]) and remembers whether RefreshStart /
        /// RefreshEnd were observed.
        /// </summary>
        private sealed class RefreshEventHandler : ISubscriptionNotificationHandler
        {
            public bool SawRefreshStart => Volatile.Read(ref m_sawStart) != 0;
            public bool SawRefreshEnd => Volatile.Read(ref m_sawEnd) != 0;

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<EventNotification> span = notification.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    ArrayOf<Variant> fields = span[i].Fields;
                    if (fields.Count < 1)
                    {
                        continue;
                    }
                    Variant typeVariant = fields[0];
                    if (!typeVariant.TryGetValue(out NodeId eventTypeId) ||
                        eventTypeId.IsNull)
                    {
                        continue;
                    }
                    if (eventTypeId.Equals(ObjectTypeIds.RefreshStartEventType))
                    {
                        Interlocked.Exchange(ref m_sawStart, 1);
                    }
                    else if (eventTypeId.Equals(ObjectTypeIds.RefreshEndEventType))
                    {
                        Interlocked.Exchange(ref m_sawEnd, 1);
                    }
                }
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Client.Subscriptions.SubscriptionState state, PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private int m_sawStart;
            private int m_sawEnd;
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
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
                .ConnectAsync(ct).ConfigureAwait(false);
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
