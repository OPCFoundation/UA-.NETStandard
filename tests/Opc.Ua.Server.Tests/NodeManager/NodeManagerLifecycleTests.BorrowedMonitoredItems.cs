/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Retiring a borrowed facade transitions its pre-existing raw item in the owning subscription.
        /// </summary>
        /// <param name="replace">Whether a new manager adopts the item instead of invalidating it.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BorrowedRawItemTransitionsOnFacadeRetirementAsync(bool replace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using BorrowedOwnershipLiveResource owner = await CreateBorrowedOwnershipLiveResourceAsync(
                kModelNamespaceUri + ":BorrowedRawItem", timeout.Token).ConfigureAwait(false);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse created = await services.CreateSubscriptionAsync(
                header, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            ISubscription subscription = owner.Server.SubscriptionManager.GetSubscriptions()
                .Single(value => value.Id == created.SubscriptionId);
            const uint ItemId = uint.MaxValue - 9;
            IDataChangeMonitoredItemQueue queue = owner.Server.MonitoredItemQueueFactory
                .CreateDataChangeQueue(false, ItemId);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(7), StatusCodes.Good), ServiceResult.Good);
            var stored = new StoredMonitoredItem
            {
                Id = ItemId,
                SubscriptionId = subscription.Id,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = owner.Value.NodeId,
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000000,
                TimestampsToReturn = TimestampsToReturn.Both,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(Variant.From(7), StatusCodes.Good),
                IsDetached = true,
                RestoredDataChangeQueue = queue
            };
            using var item = new MonitoredItem(owner.Server, stored);
            item.SubscriptionCallback = subscription;
            var binding = (IMonitoredItemGenerationBinding)item;
            binding.BindSubscription(subscription);
            IDisposable membership = ((Subscription)subscription).IncludeExternalMonitoredItem(item);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration? active = null;
            NodeManagerBatchResult? retirement = null;
            TrackingLifecycleNodeManager? replacement = null;
            try
            {
                var rawSource = (INodeManagerMonitoredItemSource)owner.Raw;
                var adoption = new MonitoredItemAdoption(binding.SourceRevision);
                ServiceResult adopted = await rawSource.AdoptSourceAsync(item, adoption, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(adopted), Is.True);
                Assert.That(item.NodeManager, Is.SameAs(owner.Raw));
                Assert.That(binding.IsSourceUnavailable, Is.False);
                Assert.That(((ISubscriptionMonitoredItemLifecycle)subscription).ContainsMonitoredItem(item), Is.True);
                var rawHandle = RequireLifecycleValue(item.ManagerHandle as NodeHandle);
                MonitoredNode2 rawNode = RequireLifecycleValue(rawHandle.MonitoredNode);
                Assert.That(rawNode.DataChangeMonitoredItems.TryGetValue(item.Id, out var rawItem), Is.True);
                Assert.That(rawItem, Is.SameAs(item));
                NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(owner.Raw);
                Assert.That(original.MonitoredItems.TryGetValue(item, out ISubscription? originalOwner), Is.True);
                Assert.That(originalOwner, Is.SameAs(subscription));
                long originalRevision = binding.SourceRevision;

                var candidate = new BorrowedOwnershipCandidate(owner);
                var factory = new BorrowedOwnershipFactory(owner.NamespaceUri, candidate.Lease);
                await using (IPreparedNodeManagerBatch addition = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult result = await addition.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    active = result.Registrations[0];
                }

                NodeManagerRegistration published = RequireLifecycleValue(active);
                Assert.That(item.NodeManager, Is.SameAs(owner.Raw));
                Assert.That(item.Generation!.TryGetGeneration(out NodeManagerGeneration? issuer), Is.True);
                Assert.That(issuer, Is.SameAs(original));
                bool recognized =
                    NodeManagerLifecycle.HasActiveMonitoredItems(owner.Server, candidate.NodeManager);

                NodeManagerBatchChange change = replace
                    ? NodeManagerBatchChange.Replace(
                        published,
                        CreateTrackingNodeManagementFactory(
                            kSecondRegistrationValue, value => replacement = value, owner.NamespaceUri),
                        immediate: false)
                    : NodeManagerBatchChange.Remove(published);
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [change], timeout.Token).ConfigureAwait(false))
                {
                    retirement = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                    Assert.That(retirement.CleanupFailure, Is.Null);
                    active = replace ? retirement.Registrations[0] : null;
                }
                await retirement.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(original.MonitoredItems.ContainsKey(item), Is.False);
                Assert.That(rawNode.DataChangeMonitoredItems.ContainsKey(item.Id), Is.False);
                Assert.That(((ISubscriptionMonitoredItemLifecycle)subscription).ContainsMonitoredItem(item), Is.True);
                Assert.That(item.SubscriptionCallback, Is.SameAs(subscription));
                Assert.That(candidate.Lease.RetireCount, Is.EqualTo(1));
                Assert.That(recognized, Is.True);
                AssertBorrowedOwnershipNotDestroyed(owner, candidate);
                if (replace)
                {
                    TrackingLifecycleNodeManager next = RequireLifecycleValue(replacement);
                    Assert.That(item.NodeManager, Is.SameAs(next));
                    Assert.That(binding.IsSourceUnavailable, Is.False);
                    Assert.That(binding.IsTerminal, Is.False);
                    Assert.That(binding.SourceRevision, Is.EqualTo(originalRevision + 1));
                    Assert.That(active!.Identity.MonitoredItems.TryGetValue(item, out ISubscription? nextOwner), Is.True);
                    Assert.That(nextOwner, Is.SameAs(subscription));
                    Assert.That(item.ItemsInQueue, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(item.NodeManager, Is.Null);
                    Assert.That(binding.IsSourceUnavailable, Is.True);
                    Assert.That(binding.IsTerminal, Is.True);
                    Assert.That(binding.SourceRevision, Is.EqualTo(originalRevision));
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    if (item.NodeManager is INodeManagerMonitoredItemSource source)
                    {
                        ServiceResult released = await source.ReleaseSourceAsync(
                            item, binding.SourceRevision, cleanup.Token).ConfigureAwait(false);
                        Assert.That(ServiceResult.IsGood(released), Is.True);
                    }
                }
                finally
                {
                    binding.UnbindSubscription(subscription);
                    membership.Dispose();
                    try
                    {
                        try
                        {
                            if (retirement is not null)
                            {
                                await retirement.Retirement.WaitAsync(cleanup.Token).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            if (active is not null &&
                                lifecycle.Registrations.ToList().Any(value => ReferenceEquals(value, active)))
                            {
                                await RemoveAndRetireAsync(lifecycle, active, cleanup.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        await DeleteSubscriptionAsync(services, subscription.Id).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
