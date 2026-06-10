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
using Opc.Ua.Client.Subscriptions.MonitoredItems;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// V2 Snapshot / RestoreAsync round-trip tests. Exercises the
    /// non-transfer leg (transferSubscriptions:false) end-to-end:
    /// snapshot the manager, restore on a fresh manager, verify
    /// configuration + items survived the round-trip and publish
    /// resumes.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("SnapshotRestore")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionSnapshotTests : ClientTestFramework
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
        public async Task SnapshotRoundTripPreservesConfigAndItemsAsync(
            CancellationToken ct)
        {
            ManagedSession originSession = await ConnectV2Async(
                nameof(SnapshotRoundTripPreservesConfigAndItemsAsync) + "_origin", ct)
                .ConfigureAwait(false);
            ManagedSession? targetSession = null;
            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription origin = originSession.AddSubscription(
                    originHandler, new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 50,
                        MaxNotificationsPerPublish = 42
                    });
                bool created = await WaitForAsync(() => origin.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                Assert.That(origin.TryAddMonitoredItem("Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(250) },
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? timeItem), Is.True);
                Assert.That(origin.TryAddMonitoredItem("State",
                    VariableIds.Server_ServerStatus_State,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(500) },
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? stateItem), Is.True);
                bool allCreated = await WaitForAsync(
                    () => timeItem!.Created && stateItem!.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(allCreated, Is.True);

                // Capture snapshots via the fluent extension.
                IReadOnlyList<SubscriptionStateSnapshot> snapshots =
                    originSession.SnapshotSubscriptions();
                Assert.That(snapshots, Has.Count.EqualTo(1));
                SubscriptionStateSnapshot subSnap = snapshots[0];
                Assert.That(subSnap.PublishingIntervalMs,
                    Is.EqualTo(500));
                Assert.That(subSnap.Priority, Is.EqualTo(50));
                Assert.That(subSnap.MaxNotificationsPerPublish,
                    Is.EqualTo(42u));
                Assert.That(subSnap.ServerId, Is.GreaterThan(0u));
                Assert.That(subSnap.MonitoredItems.Count, Is.EqualTo(2));

                MonitoredItemStateSnapshot timeSnap = default!;
                MonitoredItemStateSnapshot stateSnap = default!;
                foreach (MonitoredItemStateSnapshot it in subSnap.MonitoredItems)
                {
                    if (it.Name == "Time")
                    {
                        timeSnap = it;
                    }
                    else if (it.Name == "State")
                    {
                        stateSnap = it;
                    }
                }
                Assert.That(timeSnap, Is.Not.Null);
                Assert.That(stateSnap, Is.Not.Null);
                Assert.That(timeSnap.SamplingIntervalMs,
                    Is.EqualTo(250));
                Assert.That(stateSnap.SamplingIntervalMs,
                    Is.EqualTo(500));
                Assert.That(timeSnap.ServerId, Is.GreaterThan(0u));
                Assert.That(stateSnap.ServerId, Is.GreaterThan(0u));

                // Restore on a fresh session (transferSubscriptions:false).
                targetSession = await ConnectV2Async(
                    nameof(SnapshotRoundTripPreservesConfigAndItemsAsync) + "_target", ct)
                    .ConfigureAwait(false);
                var targetHandler = new RecordingSubscriptionHandler();
                IReadOnlyList<ISubscription> restored = await targetSession
                    .RestoreSubscriptionsAsync(snapshots,
                        _ => targetHandler,
                        transferSubscriptions: false, ct)
                    .ConfigureAwait(false);

                Assert.That(restored, Has.Count.EqualTo(1));
                ISubscription rehydrated = restored[0];
                bool rehydrated_created = await WaitForAsync(
                    () => rehydrated.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(rehydrated_created, Is.True);
                Assert.That(rehydrated.MonitoredItems.Count, Is.EqualTo(2u));
                Assert.That(rehydrated.MonitoredItems.TryGetMonitoredItemByName(
                    "Time", out _), Is.True);
                Assert.That(rehydrated.MonitoredItems.TryGetMonitoredItemByName(
                    "State", out _), Is.True);

                bool gotData = await targetHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(gotData, Is.True,
                    "Restored subscription should publish on the target session");

                await rehydrated.DisposeAsync().ConfigureAwait(false);
                await origin.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try { await originSession.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                try { await originSession.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                if (targetSession != null)
                {
                    try { await targetSession.CloseAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                    try { await targetSession.DisposeAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                }
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task SnapshotCapturesTriggeringForReplayAsync(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SnapshotCapturesTriggeringForReplayAsync), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                Assert.That(sub.TryAddMonitoredItem("Trigger",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { MonitoringMode = MonitoringMode.Reporting },
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? triggering), Is.True);
                Assert.That(sub.TryAddMonitoredItem("Triggered",
                    VariableIds.Server_ServerStatus_State,
                    o => o with { MonitoringMode = MonitoringMode.Sampling },
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? triggered), Is.True);
                bool both = await WaitForAsync(
                    () => triggering!.Created && triggered!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(both, Is.True);

                await ((Opc.Ua.Client.Subscriptions.LogicalSubscription)sub).SetTriggeringAsync(triggering!.ClientHandle,
                    [triggered!.ClientHandle], [], ct).ConfigureAwait(false);

                SubscriptionStateSnapshot snap = ((Opc.Ua.Client.Subscriptions.LogicalSubscription)sub).Snapshot();
                MonitoredItemStateSnapshot? triggerSnap = null;
                MonitoredItemStateSnapshot? triggeredSnap = null;
                foreach (MonitoredItemStateSnapshot it in snap.MonitoredItems)
                {
                    if (it.Name == "Trigger")
                    {
                        triggerSnap = it;
                    }
                    else if (it.Name == "Triggered")
                    {
                        triggeredSnap = it;
                    }
                }
                Assert.That(triggerSnap, Is.Not.Null);
                Assert.That(triggeredSnap, Is.Not.Null);
                // The snapshot stores the triggering relationship only
                // on the triggered side; the reverse "items I trigger"
                // set is reconstructed on demand from sibling items.
                Assert.That(triggeredSnap!.TriggeringItemClientHandle,
                    Is.EqualTo(triggering.ClientHandle));

                await sub.DisposeAsync().ConfigureAwait(false);
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
