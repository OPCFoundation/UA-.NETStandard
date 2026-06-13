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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// V2-engine counterparts to the seven integration tests in the
    /// classic <c>SubscriptionTest</c> (Classic project). Each test
    /// creates its own <see cref="ManagedSession"/> via
    /// <see cref="ManagedSessionBuilder"/> so the test does not depend
    /// on the inherited base-class session (which uses the classic
    /// engine for backwards-compatibility).
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionTests : ClientTestFramework
    {
        private static readonly string s_saveFile = Path.Combine(
            Path.GetTempPath(), "SubscriptionV2Test.bin");

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
        public async Task AddSubscriptionV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(AddSubscriptionV2Async), ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 0
                    });

                Assert.That(session.SubscriptionManager.Count, Is.EqualTo(1));

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Verify add + remove of a monitored item
                NodeId timeNode = VariableIds.Server_ServerStatus_CurrentTime;
                Assert.That(
                    subscription.TryAddMonitoredItem("CurrentTime", timeNode,
                        o => o with { SamplingInterval = TimeSpan.FromMilliseconds(250) },
                        out IMonitoredItem? item),
                    Is.True);
                Assert.That(item, Is.Not.Null);
                Assert.That(subscription.MonitoredItems.Count, Is.EqualTo(1u));

                bool gotData = await handler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(gotData, Is.True);

                // Toggle publishing via options push
                ISubscription sub = subscription;
                // The new options monitor is internal; test the OnChange
                // path indirectly by adding a second monitored item with
                // a different sampling interval and verifying the
                // V2 manager picks it up.
                Assert.That(
                    sub.TryAddMonitoredItem("State",
                        VariableIds.Server_ServerStatus_State,
                        o => o with { SamplingInterval = TimeSpan.FromMilliseconds(500) },
                        out IMonitoredItem? stateItem),
                    Is.True);
                Assert.That(stateItem, Is.Not.Null);
                Assert.That(sub.MonitoredItems.Count, Is.EqualTo(2u));

                bool stateCreated = await WaitForAsync(
                    () => stateItem!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(stateCreated, Is.True);

                // ConditionRefreshAsync — exposed on V2 ISubscription
                await sub.ConditionRefreshAsync(ct).ConfigureAwait(false);

                // Republish: V2 engine handles gaps automatically. We
                // exercise the raw service call here to keep parity with
                // the classic test that asserts BadMessageNotAvailable
                // for a sequence number that the server doesn't have.
                // TODO(V2): expose RepublishAsync(seq, ct) on ISubscription.
                var internalSub =
                    (Client.Subscriptions.Subscription)((LogicalSubscription)subscription).Primary;
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await session.RepublishAsync(null,
                        internalSub.Id, internalSub.LastSequenceNumberProcessed + 100, ct)
                        .ConfigureAwait(false))!;
                Assert.That(sre.StatusCode,
                    Is.EqualTo(StatusCodes.BadMessageNotAvailable));

                // Remove an item, then dispose subscription
                Assert.That(sub.MonitoredItems.TryRemove(item!.ClientHandle), Is.True);
                Assert.That(sub.MonitoredItems.Count, Is.EqualTo(1u));

                await sub.DisposeAsync().ConfigureAwait(false);
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
        public async Task SaveAndLoadSubscriptionV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(SaveAndLoadSubscriptionV2Async), ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Add several monitored items
                IList<NodeId> testSet = GetTestSetSimulation(session.NamespaceUris);
                int itemCount = Math.Min(4, testSet.Count);
                for (int i = 0; i < itemCount; i++)
                {
                    NodeId node = testSet[i];
                    string name = string.Format(CultureInfo.InvariantCulture,
                        "sim-{0}", i);
                    Assert.That(
                        subscription.TryAddMonitoredItem(name, node,
                            o => o with { SamplingInterval = TimeSpan.FromMilliseconds(100) },
                            out _),
                        Is.True);
                }

                // Wait for first data
                bool gotData = await handler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(gotData, Is.True);

                // Save
                if (File.Exists(s_saveFile))
                {
                    File.Delete(s_saveFile);
                }
                using (FileStream output = File.Create(s_saveFile))
                {
                    await session.SubscriptionManager.SaveAsync(
                        output, session.MessageContext, null, ct)
                        .ConfigureAwait(false);
                }
                Assert.That(File.Exists(s_saveFile), Is.True);
                Assert.That(new FileInfo(s_saveFile).Length, Is.GreaterThan(0));

                // Load into a fresh session
                ManagedSession reloadSession = await ConnectV2Async(nameof(SaveAndLoadSubscriptionV2Async) + "_reload", ct)
                    .ConfigureAwait(false);
                try
                {
                    var reloadHandler = new RecordingSubscriptionHandler();
                    using FileStream input = File.OpenRead(s_saveFile);
                    IReadOnlyList<ISubscription> loaded = await reloadSession
                        .SubscriptionManager.LoadAsync(input,
                            reloadSession.MessageContext,
                            _ => reloadHandler,
                            transferSubscriptions: false,
                            ct).ConfigureAwait(false);

                    Assert.That(loaded, Has.Count.EqualTo(1));
                    ISubscription rehydrated = loaded[0];

                    bool recreated = await WaitForAsync(() => rehydrated.Created,
                        TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    Assert.That(recreated, Is.True,
                        "Loaded subscription should be re-created on the server");

                    Assert.That(rehydrated.MonitoredItems.Count,
                        Is.EqualTo((uint)itemCount));

                    bool dataAfterLoad = await reloadHandler.WaitForFirstDataAsync(
                        TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                    Assert.That(dataAfterLoad, Is.True,
                        "Reloaded subscription should resume publishing");

                    await rehydrated.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await reloadSession.CloseAsync().ConfigureAwait(false);
                    await reloadSession.DisposeAsync().ConfigureAwait(false);
                }

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
                if (File.Exists(s_saveFile))
                {
                    try
                    {
                        File.Delete(s_saveFile);
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
        }

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task SequentialPublishingV2Async(CancellationToken ct)
        {
            // V2 publish channel guarantees per-subscription in-order
            // notification delivery (the prioritized publish-ack queue
            // + per-subscription dispatch serialization). This test
            // mirrors the classic SequentialPublishingSubscriptionAsync
            // load pattern: many subscriptions, many items per
            // subscription, a tight publishing interval, and a hard
            // assertion that the per-subscription sequence number
            // monotonically increases for every dispatch.
            ManagedSession session = await ConnectV2Async(
                nameof(SequentialPublishingV2Async), ct).ConfigureAwait(false);
            try
            {
                const int subscriptionCount = 5;
                const int itemsPerSubscription = 10;
                var subs = new ISubscription[subscriptionCount];
                var handlers = new SequentialOrderingHandler[subscriptionCount];

                IList<NodeId> simNodes =
                    GetTestSetSimulation(session.NamespaceUris);

                for (int i = 0; i < subscriptionCount; i++)
                {
                    handlers[i] = new SequentialOrderingHandler();
                    subs[i] = session.AddSubscription(handlers[i],
                        new Client.Subscriptions.SubscriptionOptions
                        {
                            PublishingInterval = TimeSpan.FromMilliseconds(100),
                            KeepAliveCount = 10,
                            LifetimeCount = 100,
                            PublishingEnabled = true
                        });
                    for (int j = 0; j < itemsPerSubscription && j < simNodes.Count; j++)
                    {
                        Assert.That(subs[i].TryAddMonitoredItem(
                            string.Format(CultureInfo.InvariantCulture,
                                "sub-{0}-item-{1}", i, j),
                            simNodes[j],
                            o => o with { SamplingInterval = TimeSpan.Zero },
                            out _), Is.True);
                    }
                }

                for (int i = 0; i < subscriptionCount; i++)
                {
                    int idx = i;
                    Assert.That(await WaitForAsync(() => subs[idx].Created,
                        TimeSpan.FromSeconds(10), ct).ConfigureAwait(false),
                        Is.True);
                }

                // Let publishes flow.
                await Task.Delay(3000, ct).ConfigureAwait(false);

                for (int i = 0; i < subscriptionCount; i++)
                {
                    SequentialOrderingHandler h = handlers[i];
                    Assert.That(h.SawOutOfOrder, Is.False,
                        $"Subscription {i} observed out-of-order sequence number");
                    Assert.That(h.NotificationCount, Is.GreaterThan(0),
                        $"Subscription {i} should have received at least one notification");
                }

                for (int i = 0; i < subscriptionCount; i++)
                {
                    await subs[i].DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private sealed class SequentialOrderingHandler : ISubscriptionNotificationHandler
        {
            public int NotificationCount { get; private set; }
            public bool SawOutOfOrder { get; private set; }
            private uint m_lastSeq;
            private readonly Lock m_lock = new();

            public ValueTask OnDataChangeNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                Observe(sequenceNumber);
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                Observe(sequenceNumber);
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                PublishState publishStateMask)
            {
                Observe(sequenceNumber);
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(ISubscription subscription,
                Client.Subscriptions.SubscriptionState state, PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private void Observe(uint seq)
            {
                lock (m_lock)
                {
                    NotificationCount++;
                    if (m_lastSeq != 0 && seq < m_lastSeq)
                    {
                        SawOutOfOrder = true;
                    }
                    m_lastSeq = seq;
                }
            }
        }

        [Test]
        [Order(400)]
        [CancelAfter(60_000)]
        public async Task PublishRequestCountV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(PublishRequestCountV2Async), ct).ConfigureAwait(false);
            try
            {
                ISubscriptionManager manager = session.SubscriptionManager;
                manager.MinPublishWorkerCount = 3;

                const int subscriptionCount = 5;
                const int itemsPerSubscription = 5;
                IList<NodeId> simNodes = GetTestSetSimulation(session.NamespaceUris);
                var subs = new ISubscription[subscriptionCount];
                var handlers = new RecordingSubscriptionHandler[subscriptionCount];

                for (int i = 0; i < subscriptionCount; i++)
                {
                    handlers[i] = new RecordingSubscriptionHandler();
                    subs[i] = session.AddSubscription(handlers[i],
                        new Client.Subscriptions.SubscriptionOptions
                        {
                            PublishingInterval = TimeSpan.FromMilliseconds(100),
                            KeepAliveCount = 10,
                            LifetimeCount = 100,
                            PublishingEnabled = true
                        });
                    for (int j = 0; j < itemsPerSubscription && j < simNodes.Count; j++)
                    {
                        string name = string.Format(CultureInfo.InvariantCulture,
                            "sub-{0}-item-{1}", i, j);
                        Assert.That(subs[i].TryAddMonitoredItem(name, simNodes[j],
                            o => o with { SamplingInterval = TimeSpan.Zero },
                            out _), Is.True);
                    }
                }

                for (int i = 0; i < subscriptionCount; i++)
                {
                    int index = i;
                    bool created = await WaitForAsync(() => subs[index].Created,
                        TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    Assert.That(created, Is.True);
                }

                await Task.Delay(2000, ct).ConfigureAwait(false);

                Assert.That(manager.GoodPublishRequestCount, Is.GreaterThan(0));
                Assert.That(manager.MaxPublishWorkerCount,
                    Is.GreaterThanOrEqualTo(manager.MinPublishWorkerCount));

                for (int i = 0; i < subscriptionCount; i++)
                {
                    await subs[i].DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(500)]
        [CancelAfter(60_000)]
        public async Task KeepAliveHandlerV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(KeepAliveHandlerV2Async), ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        KeepAliveCount = 1,
                        PublishingInterval = TimeSpan.FromMilliseconds(250),
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Add a static item so we get an initial value
                Assert.That(subscription.TryAddMonitoredItem("State",
                    VariableIds.Server_ServerStatus_State,
                    o => o with { SamplingInterval = TimeSpan.Zero },
                    out IMonitoredItem? item),
                    Is.True);
                Assert.That(item, Is.Not.Null);

                await handler.WaitForFirstDataAsync(TimeSpan.FromSeconds(10), ct)
                    .ConfigureAwait(false);
                int initialData = handler.DataChangeCount;

                // Wait for some keep-alives (1 KA-count + 250ms interval
                // means a KA every ~250ms on a static value).
                await Task.Delay(2000, ct).ConfigureAwait(false);
                Assert.That(handler.KeepAliveCount, Is.GreaterThanOrEqualTo(1),
                    "Should have received at least one keep-alive in 2s");

                // ResendData: V2 doesn't expose this on ISubscription. We
                // exercise the raw service call so the test still
                // verifies the server resends the cached value.
                // TODO(V2): expose ResendDataAsync on ISubscription.
                var internalSub =
                    (Client.Subscriptions.Subscription)((LogicalSubscription)subscription).Primary;
                CallMethodRequest[] resend =
                [
                    new()
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_ResendData,
                        InputArguments = [new Variant(internalSub.Id)]
                    }
                ];
                await session.CallAsync(null, resend.ToArrayOf(), ct)
                    .ConfigureAwait(false);

                // Expect a second value soon after ResendData
                bool secondData = await handler.WaitForDataCountAsync(
                    initialData + 1, TimeSpan.FromSeconds(10), ct)
                    .ConfigureAwait(false);
                Assert.That(secondData, Is.True,
                    "Server should resend the cached value after ResendData");

                // ConditionRefresh: V2 surfaces this on ISubscription.
                await subscription.ConditionRefreshAsync(ct).ConfigureAwait(false);

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(600)]
        [CancelAfter(60_000)]
        public async Task SetTriggeringTrackingV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(SetTriggeringTrackingV2Async), ct).ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 100
                    });

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Triggering item is in Reporting mode; triggered items
                // are in Sampling mode so they only report when
                // triggered.
                Assert.That(subscription.TryAddMonitoredItem("Trigger",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { MonitoringMode = MonitoringMode.Reporting },
                    out IMonitoredItem? triggering), Is.True);
                Assert.That(subscription.TryAddMonitoredItem("Triggered1",
                    VariableIds.Server_ServerStatus_State,
                    o => o with { MonitoringMode = MonitoringMode.Sampling },
                    out IMonitoredItem? triggered1), Is.True);
                Assert.That(subscription.TryAddMonitoredItem("Triggered2",
                    VariableIds.Server_ServerStatus_BuildInfo,
                    o => o with { MonitoringMode = MonitoringMode.Sampling },
                    out IMonitoredItem? triggered2), Is.True);

                bool allCreated = await WaitForAsync(
                    () => triggering!.Created && triggered1!.Created && triggered2!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(allCreated, Is.True);

                SetTriggeringResult response = await subscription
                    .SetTriggeringAsync(
                        triggering!,
                        [triggered1!, triggered2!],
                        null,
                        ct).ConfigureAwait(false);
                Assert.That(response, Is.Not.Null);
                Assert.That(response.AddResults, Has.Count.EqualTo(2));
                Assert.That(StatusCode.IsGood(response.AddResults[0].Status), Is.True);
                Assert.That(StatusCode.IsGood(response.AddResults[1].Status), Is.True);

                // Verify local tracking was updated: triggered items
                // remember who triggers them via the N:M-aware
                // TriggeringItems projection (reverse "what does this
                // item trigger" lookup is on demand via the
                // subscription's items, no eager list).
                Assert.That(triggered1!.TriggeringItems, Contains.Item(triggering!));
                Assert.That(triggered2!.TriggeringItems, Contains.Item(triggering!));

                // Remove one of the links
                SetTriggeringResult removeResponse = await subscription
                    .SetTriggeringAsync(triggering!,
                        null,
                        [triggered1!],
                        ct).ConfigureAwait(false);
                Assert.That(removeResponse.RemoveResults, Has.Count.EqualTo(1));
                Assert.That(StatusCode.IsGood(removeResponse.RemoveResults[0].Status), Is.True);
                Assert.That(triggered1.TriggeringItems, Does.Not.Contain(triggering!));
                Assert.That(triggered2.TriggeringItems, Contains.Item(triggering!));

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(601)]
        [CancelAfter(60_000)]
        public async Task DeclarativeTriggeredByNamesV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(DeclarativeTriggeredByNamesV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                // Add the triggering item first.
                Assert.That(subscription.TryAddMonitoredItem("DeclTrig",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { MonitoringMode = MonitoringMode.Reporting },
                    out IMonitoredItem? trig), Is.True);

                // Add the triggered item with TriggeredByNames declared
                // in its options — the engine should reconcile and
                // issue SetTriggering automatically once both items
                // are created.
                Assert.That(subscription.TryAddMonitoredItem("DeclTgt",
                    VariableIds.Server_ServerStatus_State,
                    o => o with
                    {
                        MonitoringMode = MonitoringMode.Sampling,
                        TriggeredByNames = ["DeclTrig"]
                    },
                    out IMonitoredItem? tgt), Is.True);

                Assert.That(await WaitForAsync(
                    () => trig!.Created && tgt!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                // The desired-state projection is populated
                // immediately from the options at TryAdd time.
                Assert.That(tgt!.TriggeringItems, Contains.Item(trig!));
                Assert.That(trig!.TriggeredItems, Contains.Item(tgt!));

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(602)]
        [CancelAfter(60_000)]
        public async Task NToMTriggeringV2Async(CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(NToMTriggeringV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                Assert.That(subscription.TryAddMonitoredItem("nmTrigA",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { MonitoringMode = MonitoringMode.Reporting },
                    out IMonitoredItem? trigA), Is.True);
                Assert.That(subscription.TryAddMonitoredItem("nmTrigB",
                    VariableIds.Server_ServerStatus_State,
                    o => o with { MonitoringMode = MonitoringMode.Reporting },
                    out IMonitoredItem? trigB), Is.True);
                Assert.That(subscription.TryAddMonitoredItem("nmShared",
                    VariableIds.Server_ServerStatus_BuildInfo,
                    o => o with { MonitoringMode = MonitoringMode.Sampling },
                    out IMonitoredItem? shared), Is.True);

                Assert.That(await WaitForAsync(
                    () => trigA!.Created && trigB!.Created && shared!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                // Link the shared item under BOTH triggering items via
                // two separate imperative calls — this is the N:M
                // pattern explicitly enabled by the V2 API.
                SetTriggeringResult rA = await subscription.SetTriggeringAsync(
                    trigA!, [shared!], null, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(rA.AddResults[0].Status), Is.True);
                SetTriggeringResult rB = await subscription.SetTriggeringAsync(
                    trigB!, [shared!], null, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(rB.AddResults[0].Status), Is.True);

                // shared.TriggeringItems reflects both relationships.
                Assert.That(shared!.TriggeringItems, Contains.Item(trigA!));
                Assert.That(shared!.TriggeringItems, Contains.Item(trigB!));
                // Reverse: each triggering item knows about shared.
                Assert.That(trigA!.TriggeredItems, Contains.Item(shared!));
                Assert.That(trigB!.TriggeredItems, Contains.Item(shared!));

                // Remove only the link from trigA — trigB's link
                // survives, demonstrating that N:M is preserved
                // per-link.
                SetTriggeringResult rRemove = await subscription.SetTriggeringAsync(
                    trigA, null, [shared], ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(rRemove.RemoveResults[0].Status), Is.True);
                Assert.That(shared.TriggeringItems, Does.Not.Contain(trigA));
                Assert.That(shared.TriggeringItems, Contains.Item(trigB!));

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [Order(700)]
        [CancelAfter(60_000)]
        public async Task ConcurrentAddMonitoredItemsNoDuplicatesV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(nameof(ConcurrentAddMonitoredItemsNoDuplicatesV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                const int itemCount = 10;
                IList<NodeId> simNodes = GetTestSetSimulation(session.NamespaceUris);
                NodeId node = simNodes.Count > 0
                    ? simNodes[0]
                    : VariableIds.Server_ServerStatus_CurrentTime;

                // Concurrently call TryAdd for the same unique names.
                // TryAdd is name-keyed; concurrent calls for the same
                // name from different threads should all return the same
                // item without creating duplicates.
                var addTasks = new Task<bool>[itemCount];
                for (int i = 0; i < itemCount; i++)
                {
                    int index = i;
                    addTasks[i] = Task.Run(() =>
                    {
                        string name = string.Format(CultureInfo.InvariantCulture,
                            "item-{0}", index);
                        return subscription.TryAddMonitoredItem(name, node,
                            o => o with { SamplingInterval = TimeSpan.Zero },
                            out _);
                    }, ct);
                }
                await Task.WhenAll(addTasks).ConfigureAwait(false);

                // Each unique name should map to exactly one item; total
                // count should be itemCount with no duplicates.
                Assert.That(subscription.MonitoredItems.Count,
                    Is.EqualTo((uint)itemCount),
                    "Concurrent TryAdd of unique names must not create duplicates");

                // Wait for the server to confirm creation
                bool allCreated = await WaitForAsync(() =>
                {
                    int createdCount = 0;
                    foreach (IMonitoredItem item in subscription.MonitoredItems.Items)
                    {
                        if (item.Created)
                        {
                            createdCount++;
                        }
                    }
                    return createdCount == itemCount;
                }, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(allCreated, Is.True,
                    "All items should be created exactly once on the server");

                // Verify each item has a distinct server-assigned id
                var serverIds = new HashSet<uint>();
                foreach (IMonitoredItem item in subscription.MonitoredItems.Items)
                {
                    Assert.That(item.ServerId, Is.GreaterThan(0u));
                    Assert.That(serverIds.Add(item.ServerId), Is.True,
                        $"Duplicate server id {item.ServerId} for item {item.Name}");
                }

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
                .WithSessionTimeout(TimeSpan.FromSeconds(120))
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
