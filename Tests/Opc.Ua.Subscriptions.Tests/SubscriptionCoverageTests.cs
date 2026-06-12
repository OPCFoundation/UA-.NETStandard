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
    /// V2 follow-up coverage: handler state-change callback,
    /// fluent stream-based LoadSubscriptionsAsync, SendInitialValuesOnTransfer
    /// behavior, snapshot edge cases (empty / with-filter / concurrent).
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("FollowUp")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionCoverageTests : ClientTestFramework
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

        // ===== 1. OnSubscriptionStateChangedAsync fires on lifecycle =====

        [Test]
        [Order(100)]
        [CancelAfter(60_000)]
        public async Task HandlerStateChangedFiresOnLifecycleV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(HandlerStateChangedFiresOnLifecycleV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                bool created = await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                bool sawStateChanges = await WaitForAsync(
                    () => handler.StateChangedCount > 0,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                Assert.That(sawStateChanges, Is.True,
                    "OnSubscriptionStateChangedAsync should fire during subscription lifecycle");

                IReadOnlyList<RecordedStateChange> snap = handler.GetStateChangeSnapshot();
                Assert.That(snap, Is.Not.Empty);

                // Sub-test: derive a "is currently created" indicator from
                // the callback stream — proves the handler-maintains-state
                // pattern that replaced the dropped PublishingStopped
                // property on ISubscription.
                bool sawCreated = false;
                foreach (RecordedStateChange c in snap)
                {
                    if (c.State is Client.Subscriptions.SubscriptionState.Created or
                        Client.Subscriptions.SubscriptionState.Modified or
                        Client.Subscriptions.SubscriptionState.Opened)
                    {
                        sawCreated = true;
                    }
                }
                Assert.That(sawCreated, Is.True,
                    "State stream should include at least one Created / " +
                    "Modified / Opened transition");

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ===== 2. Fluent stream-based LoadSubscriptionsAsync =====

        [Test]
        [Order(200)]
        [CancelAfter(60_000)]
        public async Task FluentLoadSubscriptionsAsyncStreamV2Async(
            CancellationToken ct)
        {
            ManagedSession originSession = await ConnectV2Async(
                nameof(FluentLoadSubscriptionsAsyncStreamV2Async) + "_origin", ct)
                .ConfigureAwait(false);
            ManagedSession? targetSession = null;
            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription origin = originSession.AddSubscription(originHandler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => origin.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);
                Assert.That(origin.TryAddMonitoredItem("Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(200) },
                    out _), Is.True);
                Assert.That(await originHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                using var ms = new MemoryStream();
                await originSession.SaveSubscriptionsAsync(ms, ct: ct)
                    .ConfigureAwait(false);
                byte[] saved = ms.ToArray();
                Assert.That(saved, Has.Length.GreaterThan(0));

                targetSession = await ConnectV2Async(
                    nameof(FluentLoadSubscriptionsAsyncStreamV2Async) + "_target", ct)
                    .ConfigureAwait(false);
                var targetHandler = new RecordingSubscriptionHandler();
                using var input = new MemoryStream(saved);
                IReadOnlyList<ISubscription> loaded = await targetSession
                    .LoadSubscriptionsAsync(input, _ => targetHandler,
                        transferSubscriptions: false, ct)
                    .ConfigureAwait(false);
                Assert.That(loaded, Has.Count.EqualTo(1));
                Assert.That(await WaitForAsync(() => loaded[0].Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false), Is.True);
                Assert.That(await targetHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false), Is.True);

                await loaded[0].DisposeAsync().ConfigureAwait(false);
                await origin.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await originSession.CloseAsync().ConfigureAwait(false);
                }
                catch
                { /* best effort */
                }
                try
                {
                    await originSession.DisposeAsync().ConfigureAwait(false);
                }
                catch
                { /* best effort */
                }
                if (targetSession != null)
                {
                    try
                    {
                        await targetSession.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                    try
                    {
                        await targetSession.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                }
            }
        }

        // ===== 3. SendInitialValuesOnTransfer behavior =====

        [Test]
        [Order(300)]
        [CancelAfter(60_000)]
        public async Task SendInitialValuesOnTransferV2Async(CancellationToken ct)
        {
            // When the test runs against a reference server that denies
            // cross-anonymous-session transfer with BadUserAccessDenied,
            // the V2 manager falls back to recreate. Recreate always
            // delivers an initial value (server first-sample behavior),
            // so the SendInitialValuesOnTransfer option's effect can't
            // be distinguished. Document that and verify the option
            // propagates structurally + the subscription works after
            // restore regardless of which path the server took.
            string saveFile = Path.Combine(Path.GetTempPath(),
                $"V2InitVals-{Guid.NewGuid():N}.bin");
            ManagedSession originSession = await ConnectV2Async(
                nameof(SendInitialValuesOnTransferV2Async) + "_origin", ct)
                .ConfigureAwait(false);
            originSession.DeleteSubscriptionsOnClose = false;
            ManagedSession? targetSession = null;
            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription origin = originSession.AddSubscription(originHandler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        SendInitialValuesOnTransfer = true
                    });
                Assert.That(await WaitForAsync(() => origin.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);
                Assert.That(origin.TryAddMonitoredItem("Time",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with { SamplingInterval = TimeSpan.FromMilliseconds(200) },
                    out _), Is.True);
                Assert.That(await originHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                // Snapshot includes the SendInitialValuesOnTransfer
                // option through SubscriptionStateSnapshot.ToOptions().
                SubscriptionStateSnapshot snap = ((Client.Subscriptions.Subscription)origin).Snapshot();
                Assert.That(snap.SendInitialValuesOnTransfer, Is.True,
                    "SendInitialValuesOnTransfer option must round-trip through Snapshot");

                using (FileStream output = File.Create(saveFile))
                {
                    await originSession.SaveSubscriptionsAsync(output, ct: ct)
                        .ConfigureAwait(false);
                }
                Assert.That(await originSession.CloseAsync().ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));

                targetSession = await ConnectV2Async(
                    nameof(SendInitialValuesOnTransferV2Async) + "_target", ct)
                    .ConfigureAwait(false);
                var targetHandler = new RecordingSubscriptionHandler();
                using FileStream input = File.OpenRead(saveFile);
                IReadOnlyList<ISubscription> loaded = await targetSession
                    .LoadSubscriptionsAsync(input, _ => targetHandler,
                        transferSubscriptions: true, ct)
                    .ConfigureAwait(false);
                Assert.That(loaded, Has.Count.EqualTo(1));
                bool gotData = await targetHandler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(gotData, Is.True,
                    "Target subscription should publish (transfer or recreate)");

                await loaded[0].DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await originSession.DisposeAsync().ConfigureAwait(false);
                }
                catch
                { /* best effort */
                }
                if (targetSession != null)
                {
                    try
                    {
                        await targetSession.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                    try
                    {
                        await targetSession.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                }
                try
                {
                    File.Delete(saveFile);
                }
                catch
                { /* best effort */
                }
            }
        }

        // ===== 4-6. Snapshot/Restore edge cases =====

        [Test]
        [Order(400)]
        [CancelAfter(60_000)]
        public async Task SnapshotEmptySubscriptionRoundTripV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SnapshotEmptySubscriptionRoundTripV2Async), ct)
                .ConfigureAwait(false);
            ManagedSession? target = null;
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);
                Assert.That(sub.MonitoredItems.Count, Is.Zero);

                SubscriptionStateSnapshot snap = ((Client.Subscriptions.Subscription)sub).Snapshot();
                Assert.That(snap.MonitoredItems.Count, Is.Zero);

                target = await ConnectV2Async(
                    nameof(SnapshotEmptySubscriptionRoundTripV2Async) + "_target", ct)
                    .ConfigureAwait(false);
                ISubscription restored = await ((SubscriptionManager)target.SubscriptionManager)
                    .RestoreAsync(
                        new RecordingSubscriptionHandler(),
                        snap,
                        transferSubscriptions: false,
                        ct)
                    .ConfigureAwait(false);
                Assert.That(await WaitForAsync(() => restored.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);
                Assert.That(restored.MonitoredItems.Count, Is.Zero);

                await restored.DisposeAsync().ConfigureAwait(false);
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
                if (target != null)
                {
                    try
                    {
                        await target.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                    try
                    {
                        await target.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                }
            }
        }

        [Test]
        [Order(500)]
        [CancelAfter(60_000)]
        public async Task SnapshotWithDataChangeFilterRoundTripV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SnapshotWithDataChangeFilterRoundTripV2Async), ct)
                .ConfigureAwait(false);
            ManagedSession? target = null;
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                var dataChangeFilter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValueTimestamp,
                    DeadbandType = (uint)DeadbandType.None,
                    DeadbandValue = 0.0
                };
                Assert.That(sub.TryAddMonitoredItem("Trigger",
                    VariableIds.Server_ServerStatus_CurrentTime,
                    o => o with
                    {
                        SamplingInterval = TimeSpan.FromMilliseconds(250),
                        Filter = dataChangeFilter
                    },
                    out IMonitoredItem? item), Is.True);
                Assert.That(await WaitForAsync(() => item!.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                SubscriptionStateSnapshot snap = ((Client.Subscriptions.Subscription)sub).Snapshot();
                Assert.That(snap.MonitoredItems.Count, Is.EqualTo(1));
                MonitoredItemStateSnapshot itemSnap = snap.MonitoredItems[0];
                Assert.That(itemSnap.Filter, Is.Not.Null);
                Assert.That(itemSnap.Filter, Is.InstanceOf<DataChangeFilter>());
                var restoredFilter = (DataChangeFilter)itemSnap.Filter!;
                Assert.That(restoredFilter.Trigger,
                    Is.EqualTo(dataChangeFilter.Trigger));
                Assert.That(restoredFilter.DeadbandType,
                    Is.EqualTo(dataChangeFilter.DeadbandType));

                // Round-trip through the manager
                target = await ConnectV2Async(
                    nameof(SnapshotWithDataChangeFilterRoundTripV2Async) + "_target", ct)
                    .ConfigureAwait(false);
                ISubscription restored = await ((SubscriptionManager)target.SubscriptionManager)
                    .RestoreAsync(
                        new RecordingSubscriptionHandler(),
                        snap,
                        transferSubscriptions: false,
                        ct)
                    .ConfigureAwait(false);
                Assert.That(await WaitForAsync(() => restored.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);
                Assert.That(restored.MonitoredItems.Count, Is.EqualTo(1u));

                await restored.DisposeAsync().ConfigureAwait(false);
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
                if (target != null)
                {
                    try
                    {
                        await target.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                    try
                    {
                        await target.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    { /* best effort */
                    }
                }
            }
        }

        [Test]
        [Order(600)]
        [CancelAfter(60_000)]
        public async Task SnapshotUnderConcurrentMutationV2Async(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(SnapshotUnderConcurrentMutationV2Async), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription sub = session.AddSubscription(handler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true
                    });
                Assert.That(await WaitForAsync(() => sub.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                // Concurrently add items while taking snapshots. The
                // snapshot must always return an internally consistent
                // count (the names must all exist) — proves the
                // manager-lock contract.
                const int itemCount = 20;
                var addTasks = new Task[itemCount];
                for (int i = 0; i < itemCount; i++)
                {
                    int idx = i;
                    addTasks[i] = Task.Run(() => sub.TryAddMonitoredItem(
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "item-{0}", idx),
                        VariableIds.Server_ServerStatus_CurrentTime,
                        o => o with { SamplingInterval = TimeSpan.Zero },
                        out _), ct);
                }
                var subConcrete = (Client.Subscriptions.Subscription)sub;
                var snapTasks = new Task<SubscriptionStateSnapshot>[5];
                for (int i = 0; i < snapTasks.Length; i++)
                {
                    snapTasks[i] = Task.Run(() => subConcrete.Snapshot(), ct);
                }
                await Task.WhenAll(addTasks).ConfigureAwait(false);
                SubscriptionStateSnapshot[] snaps = await Task
                    .WhenAll(snapTasks).ConfigureAwait(false);

                foreach (SubscriptionStateSnapshot s in snaps)
                {
                    int count = s.MonitoredItems.Count;
                    // Snapshot is captured at a point in time — count is
                    // between 0 and itemCount inclusive. The critical
                    // invariant: each captured item has a non-empty name
                    // (i.e. no partially-constructed entries leaked).
                    foreach (MonitoredItemStateSnapshot it in s.MonitoredItems)
                    {
                        Assert.That(it.Name, Is.Not.Null.And.Not.Empty);
                    }
                }

                Assert.That(sub.MonitoredItems.Count, Is.EqualTo((uint)itemCount));

                await sub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ===== helpers =====

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
