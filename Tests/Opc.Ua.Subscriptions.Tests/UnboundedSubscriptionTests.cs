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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.TestFramework;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Integration tests for the V2 unbounded-item mode of
    /// <see cref="LogicalSubscription"/> against the in-process
    /// reference server. The reference server's default
    /// MaxMonitoredItemsPerSubscription is large, so these tests
    /// drive partition splitting by setting
    /// <see cref="SubscriptionOptions.MaxMonitoredItemsPerPartition"/>
    /// to a small value rather than reconfiguring the server.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("V2")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class UnboundedSubscriptionTests : ClientTestFramework
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
        [CancelAfter(120_000)]
        public async Task ItemsOverflowSplitIntoMultiplePartitionsAsync(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(ItemsOverflowSplitIntoMultiplePartitionsAsync), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                const uint perPartitionCap = 25;
                const int totalItems = 100;

                ISubscription subscription = session.AddSubscription(handler,
                    new V2SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 0,
                        MaxMonitoredItemsPerPartition = perPartitionCap
                    });

                Assert.That(subscription, Is.InstanceOf<IPartitionedSubscription>());
                Assert.That(subscription, Is.InstanceOf<LogicalSubscription>());

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                // Add `totalItems` items all monitoring the same
                // server variable but with unique composite-level
                // names so they coexist. With per-partition cap of
                // `perPartitionCap`, the engine must mint
                // ceil(totalItems / perPartitionCap) partitions.
                NodeId timeNode = VariableIds.Server_ServerStatus_CurrentTime;
                for (int i = 0; i < totalItems; i++)
                {
                    Assert.That(
                        subscription.TryAddMonitoredItem(
                            $"unbounded_{i}",
                            timeNode,
                            o => o with
                            {
                                SamplingInterval = TimeSpan.FromMilliseconds(500)
                            },
                            out IMonitoredItem? _),
                        Is.True,
                        $"item {i} should add when the engine partitions transparently");
                }

                Assert.That(subscription.MonitoredItems.Count,
                    Is.EqualTo((uint)totalItems));

                const int expectedPartitions =
                    (totalItems + (int)perPartitionCap - 1) / (int)perPartitionCap;
                var partitioned = (IPartitionedSubscription)subscription;
                Assert.That(partitioned.PartitionCount,
                    Is.EqualTo(expectedPartitions),
                    "engine must mint exactly ceil(totalItems / cap) partitions");
                Assert.That(partitioned.PartitionIds,
                    Has.Count.EqualTo(expectedPartitions));

                // Wait until every partition becomes Created.
                bool allCreated = await WaitForAsync(
                    () => subscription.Created,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                Assert.That(allCreated, Is.True,
                    "all partitions must reach Created");

                // Notifications must flow.
                bool gotData = await handler.WaitForFirstDataAsync(
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(gotData, Is.True);

                // Items in different partitions carry the same names
                // but distinct ClientHandles — verify global handle
                // uniqueness and that each item belongs to exactly
                // one partition's collection.
                IMonitoredItem[] allItems = subscription.MonitoredItems.Items
                    .ToArray();
                Assert.That(allItems, Has.Length.EqualTo(totalItems));
                Assert.That(allItems.Select(i => i.ClientHandle).Distinct().ToArray(), Has.Length.EqualTo(totalItems),
                    "ClientHandle must be globally unique across partitions");

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                { await session.CloseAsync(ct: CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
                try
                { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(120_000)]
        public async Task StrictAffinityPinsItemsToSamePartitionAsync(
            CancellationToken ct)
        {
            ManagedSession session = await ConnectV2Async(
                nameof(StrictAffinityPinsItemsToSamePartitionAsync), ct)
                .ConfigureAwait(false);
            try
            {
                var handler = new RecordingSubscriptionHandler();
                ISubscription subscription = session.AddSubscription(handler,
                    new V2SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 0,
                        MaxMonitoredItemsPerPartition = 5
                    });
                Assert.That(await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(10), ct).ConfigureAwait(false), Is.True);

                NodeId timeNode = VariableIds.Server_ServerStatus_CurrentTime;

                // Fill the primary with no-affinity items to force a
                // secondary on the first tagged add.
                for (int i = 0; i < 5; i++)
                {
                    Assert.That(subscription.TryAddMonitoredItem(
                        $"primary_{i}", timeNode, o => o, out IMonitoredItem? _),
                        Is.True);
                }

                // First tagged item — picks any partition with capacity
                // (the secondary, since primary is full). Pin GROUP-A
                // to that partition.
                Assert.That(subscription.TryAddMonitoredItem(
                    "tagged_0", timeNode,
                    o => o with { Affinity = "GROUP-A" },
                    out IMonitoredItem? first), Is.True);

                // 4 more tagged items — must land in the same partition.
                for (int i = 1; i < 5; i++)
                {
                    Assert.That(subscription.TryAddMonitoredItem(
                        $"tagged_{i}", timeNode,
                        o => o with { Affinity = "GROUP-A" },
                        out IMonitoredItem? _),
                        Is.True,
                        $"affinity-tagged item {i} should fit in the pinned partition");
                }

                // 6th tagged item — pinned partition is now full,
                // strict affinity contract REJECTS rather than splits.
                Assert.That(subscription.TryAddMonitoredItem(
                    "tagged_5", timeNode,
                    o => o with { Affinity = "GROUP-A" },
                    out IMonitoredItem? overflow), Is.False,
                    "strict affinity must reject the 6th tagged item");
                Assert.That(overflow, Is.Null);

                // A different affinity group still works.
                Assert.That(subscription.TryAddMonitoredItem(
                    "groupB_0", timeNode,
                    o => o with { Affinity = "GROUP-B" },
                    out IMonitoredItem? _), Is.True);

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                { await session.CloseAsync(ct: CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
                try
                { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }

        [Test]
        [Order(300)]
        [CancelAfter(120_000)]
        public async Task SaveLoadRoundTripsAllPartitionsAsync(
            CancellationToken ct)
        {
            // Capture phase — add enough items to force at least 3
            // partitions, snapshot every partition via
            // SnapshotAllPartitions(), close the session.
            ManagedSession originSession = await ConnectV2Async(
                nameof(SaveLoadRoundTripsAllPartitionsAsync) + "_origin", ct)
                .ConfigureAwait(false);
            const uint perPartitionCap = 10;
            const int totalItems = 30;
            IReadOnlyList<SubscriptionStateSnapshot> savedSnapshots;

            try
            {
                var originHandler = new RecordingSubscriptionHandler();
                ISubscription origin = originSession.AddSubscription(originHandler,
                    new V2SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(500),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        PublishingEnabled = true,
                        Priority = 0,
                        MaxMonitoredItemsPerPartition = perPartitionCap
                    });
                Assert.That(await WaitForAsync(() => origin.Created,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false), Is.True);

                NodeId timeNode = VariableIds.Server_ServerStatus_CurrentTime;
                for (int i = 0; i < totalItems; i++)
                {
                    Assert.That(origin.TryAddMonitoredItem(
                        $"saveload_{i}", timeNode,
                        o => o with { SamplingInterval = TimeSpan.FromMilliseconds(500) },
                        out _), Is.True);
                }

                Assert.That(((IPartitionedSubscription)origin).PartitionCount,
                    Is.EqualTo((totalItems + (int)perPartitionCap - 1) / (int)perPartitionCap));

                // Wait until all partitions and their items are
                // Created so the snapshot captures real server ids.
                bool itemsCreated = await WaitForAsync(
                    () => origin.MonitoredItems.Items.All(i => i.Created),
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                Assert.That(itemsCreated, Is.True);

                savedSnapshots = originSession.SnapshotSubscriptions();
                Assert.That(savedSnapshots,
                    Has.Count.EqualTo(((IPartitionedSubscription)origin).PartitionCount),
                    "every partition must contribute one snapshot");
                Assert.That(savedSnapshots.Select(s => s.LogicalGroupId).Distinct().Count(),
                    Is.EqualTo(1), "all snapshots must share the same LogicalGroupId");
                Assert.That(savedSnapshots.Select(s => s.PartitionIndex).OrderBy(i => i),
                    Is.EqualTo(Enumerable.Range(0, savedSnapshots.Count)),
                    "PartitionIndex values must be contiguous 0..N-1");
            }
            finally
            {
                try
                { await originSession.CloseAsync(ct: CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
                try
                { await originSession.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }

            // Restore phase — fresh session, regroup the snapshots
            // into one LogicalSubscription with all items.
            ManagedSession targetSession = await ConnectV2Async(
                nameof(SaveLoadRoundTripsAllPartitionsAsync) + "_target", ct)
                .ConfigureAwait(false);
            try
            {
                var targetHandler = new RecordingSubscriptionHandler();
                IReadOnlyList<ISubscription> restored = await targetSession
                    .RestoreSubscriptionsAsync(savedSnapshots,
                        _ => targetHandler,
                        transferSubscriptions: false,
                        ct)
                    .ConfigureAwait(false);
                Assert.That(restored, Has.Count.EqualTo(1),
                    "regrouped snapshots must restore as one logical subscription");
                ISubscription restoredSub = restored[0];
                Assert.That(restoredSub, Is.InstanceOf<IPartitionedSubscription>());

                bool restoredCreated = await WaitForAsync(() => restoredSub.Created,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                Assert.That(restoredCreated, Is.True,
                    "every restored partition must reach Created");
                Assert.That(restoredSub.MonitoredItems.Count, Is.EqualTo((uint)totalItems));
                Assert.That(((IPartitionedSubscription)restoredSub).PartitionCount,
                    Is.EqualTo(savedSnapshots.Count),
                    "restored partition count must match the saved snapshot count");
                for (int i = 0; i < totalItems; i++)
                {
                    Assert.That(restoredSub.MonitoredItems
                        .TryGetMonitoredItemByName($"saveload_{i}", out _),
                        Is.True,
                        $"item saveload_{i} must round-trip through save/load");
                }

                await restoredSub.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                { await targetSession.CloseAsync(ct: CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
                try
                { await targetSession.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }

        [Test]
        [Order(400)]
        [CancelAfter(120_000)]
        public async Task DurableLifetimeAppliesToSecondaryPartitionsAsync(
            CancellationToken ct)
        {
            // SetSubscriptionDurable requires server-side support
            // not present in the in-process Quickstart reference
            // server — the comprehensive end-to-end coverage of the
            // multi-partition durable hook lives in
            // Opc.Ua.Subscriptions.Durable.Tests
            // (DurableLifetimeAppliesToAllPartitionsV2Async), which
            // uses a ReferenceServer fixture configured with
            // DurableSubscriptionsEnabled=true. This stub exists so
            // CI runners against the Quickstart server document the
            // coverage location and ignore gracefully.
            Assert.Ignore("Multi-partition durable coverage lives in " +
                "Opc.Ua.Subscriptions.Durable.Tests." +
                "SubscriptionDurableTests." +
                "DurableLifetimeAppliesToAllPartitionsV2Async — " +
                "requires DurableSubscriptionsEnabled on the server.");
            await Task.CompletedTask.ConfigureAwait(false);
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
