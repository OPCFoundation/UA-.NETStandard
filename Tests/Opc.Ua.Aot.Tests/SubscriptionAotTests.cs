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
using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for subscription lifecycle operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class SubscriptionAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task CreateAndDeleteSubscriptionAsync()
        {
            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotCreateDelete",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "CurrentTime"
            };

            int notificationCount = 0;
            item.Notification += (_, _) =>
                Interlocked.Increment(ref notificationCount);

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            // Wait for at least one notification
            await Task.Delay(2000).ConfigureAwait(false);
            await Assert.That(notificationCount).IsGreaterThan(0);

            // Delete the subscription
            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsFalse();
        }

        [Test]
        public async Task ModifySubscriptionAsync()
        {
            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotModify",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();

            double originalInterval = subscription.CurrentPublishingInterval;

            // Modify the publishing interval
            subscription.PublishingInterval = 2000;
            await subscription.ModifyAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.CurrentPublishingInterval)
                .IsNotEqualTo(originalInterval);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task TransferSubscriptionAsync()
        {
            // Create source session with a subscription
            ISession sourceSession = await fixture.CreateSessionAsync("TransferSource")
                .ConfigureAwait(false);

            using var subscription = new Subscription(sourceSession.DefaultSubscription)
            {
                DisplayName = "AotTransfer",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            sourceSession.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "CurrentTime"
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();

            // Create target session
            ISession targetSession = await fixture.CreateSessionAsync("TransferTarget")
                .ConfigureAwait(false);

            // Clone subscriptions for transfer
            var sourceSubscriptions =
                new SubscriptionCollection(sourceSession.Subscriptions);
            SubscriptionCollection transferSubscriptions =
                sourceSubscriptions.CloneSubscriptions(false);

            foreach (Subscription s in transferSubscriptions)
            {
                targetSession.AddSubscription(s);
            }

            // Exercise the TransferSubscriptions service call.
            // The result depends on server state; for AOT testing the
            // important thing is that the serialization code path runs.
            await targetSession.TransferSubscriptionsAsync(
                transferSubscriptions, true, CancellationToken.None)
                .ConfigureAwait(false);

            // Cleanup
            targetSession.DeleteSubscriptionsOnClose = true;
            await targetSession.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            targetSession.Dispose();

            sourceSession.DeleteSubscriptionsOnClose = true;
            await sourceSession.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            sourceSession.Dispose();
        }

        [Test]
        public async Task KeepAliveAsync()
        {
            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotKeepAlive",
                PublishingEnabled = true,
                PublishingInterval = 500,
                KeepAliveCount = 1
            };

            int keepAliveCount = 0;
            subscription.FastKeepAliveCallback = (_, _) =>
                Interlocked.Increment(ref keepAliveCount);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_State,
                AttributeId = Attributes.Value,
                DisplayName = "ServerState"
            };

            subscription.AddItem(item);
            fixture.Session.AddSubscription(subscription);

            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // Wait for keep-alive notifications
            await Task.Delay(3000).ConfigureAwait(false);

            await Assert.That(keepAliveCount).IsGreaterThan(0);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Smoke test for the V2 unbounded-items mode under
        /// NativeAOT. Exercises every new code path
        /// (LogicalSubscription, CompositeMonitoredItemCollection,
        /// PartitionPlacementPolicy, PartitionForwardingHandler,
        /// and the SubscriptionManager partition factory) to confirm
        /// no missing trimming or reflection annotations break
        /// under AOT.
        /// </summary>
        [Test]
        public async Task UnboundedSubscriptionWithCapAotAsync()
        {
            Opc.Ua.Client.ManagedSession session = await fixture
                .CreateManagedSessionAsync("AotUnboundedSubscription")
                .ConfigureAwait(false);
            try
            {
                var handler = new AotRecordingHandler();
                Opc.Ua.Client.Subscriptions.ISubscription subscription =
                    session.AddSubscription(handler,
                        new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                        {
                            PublishingInterval = TimeSpan.FromMilliseconds(500),
                            KeepAliveCount = 10,
                            LifetimeCount = 100,
                            PublishingEnabled = true,
                            Priority = 0,
                            MaxMonitoredItemsPerPartition = 5
                        });
                // The concrete LogicalSubscription type is internal —
                // verify via the public IPartitionedSubscription
                // surface that we got a partition-aware wrapper.
                await Assert.That(subscription)
                    .IsAssignableTo<Opc.Ua.Client.Subscriptions.IPartitionedSubscription>();

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await Assert.That(created).IsTrue();

                // Add 12 items at cap=5 → forces 3 partitions; every
                // partition mint runs through the V2 factory path the
                // composite collection drives. Each item add exercises
                // the placement policy + composite indexing.
                NodeId timeNode = VariableIds.Server_ServerStatus_CurrentTime;
                for (int i = 0; i < 12; i++)
                {
                    bool added = subscription.MonitoredItems.TryAdd(
                        $"aot_unbounded_{i}",
                        new Opc.Ua.OptionsMonitor<
                            Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions>(
                            new Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions
                            {
                                StartNodeId = timeNode,
                                SamplingInterval = TimeSpan.FromMilliseconds(500)
                            }),
                        out _);
                    await Assert.That(added).IsTrue();
                }

                var partitioned =
                    (Opc.Ua.Client.Subscriptions.IPartitionedSubscription)subscription;
                await Assert.That(partitioned.PartitionCount).IsEqualTo(3);

                // Strict-affinity smoke: an Affinity-tagged item must
                // co-locate with previous items of the same tag. The
                // assertion is implicit — TryAdd returns true and the
                // composite name lookup succeeds (PartitionPlacementPolicy
                // pinned the tag to its first chosen partition).
                bool affinityAdded = subscription.MonitoredItems.TryAdd(
                    "aot_affinity_a",
                    new Opc.Ua.OptionsMonitor<
                        Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions>(
                        new Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions
                        {
                            StartNodeId = timeNode,
                            SamplingInterval = TimeSpan.FromMilliseconds(500),
                            Affinity = "aot_group"
                        }),
                    out Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem affinityItem);
                await Assert.That(affinityAdded).IsTrue();
                await Assert.That(affinityItem).IsNotNull();

                bool allCreated = await WaitForAsync(
                    () => subscription.MonitoredItems.Items.All(i => i.Created),
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await Assert.That(allCreated).IsTrue();

                // Notifications must flow through the
                // PartitionForwardingHandler dispatch path.
                bool gotData = await handler.WaitForAnyDataAsync(
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await Assert.That(gotData).IsTrue();

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await session.CloseAsync(ct: CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { /* best effort */ }
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                {
                    return true;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return predicate();
        }

        /// <summary>
        /// Minimal AOT-safe notification handler — counts
        /// data-change callbacks so the test can wait for the
        /// PartitionForwardingHandler dispatch path to deliver at
        /// least one notification.
        /// </summary>
        private sealed class AotRecordingHandler
            : Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler
        {
            private readonly TaskCompletionSource<bool> m_firstData =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask OnDataChangeNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<Opc.Ua.Client.Subscriptions.DataValueChange> notification,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                System.Collections.Generic.IReadOnlyList<string> stringTable)
            {
                m_firstData.TrySetResult(true);
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<Opc.Ua.Client.Subscriptions.EventNotification> notification,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                System.Collections.Generic.IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber, DateTime publishTime,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            public async Task<bool> WaitForAnyDataAsync(TimeSpan timeout)
            {
                Task winner = await Task.WhenAny(
                    m_firstData.Task, Task.Delay(timeout)).ConfigureAwait(false);
                return winner == m_firstData.Task;
            }
        }
    }
}
