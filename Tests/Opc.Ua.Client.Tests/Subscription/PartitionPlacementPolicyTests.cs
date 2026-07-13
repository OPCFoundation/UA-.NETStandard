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

using System;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using V2Options = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Unit tests for <see cref="PartitionPlacementPolicy"/>. The
    /// policy is the strict-affinity / first-fit / no-grow brain of
    /// the composite collection; these tests pin every decision path
    /// (zero cap = unbounded, affinity pinning, capacity rollover,
    /// reactive cap fallback, partition removal).
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class PartitionPlacementPolicyTests
    {
        [Test]
        public void ConstructorTreatsZeroAsUnboundedCap()
        {
            var policy = new PartitionPlacementPolicy(0);
            Assert.That(policy.MaxItemsPerPartition, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void ConstructorPreservesExplicitCap()
        {
            var policy = new PartitionPlacementPolicy(100);
            Assert.That(policy.MaxItemsPerPartition, Is.EqualTo(100u));
        }

        [Test]
        public void DecideOnEmptyPartitionListNeedsNewPartition()
        {
            var policy = new PartitionPlacementPolicy(10);
            PlacementDecision decision = policy.Decide(new V2Options(), []);

            Assert.That(decision.RequiresNewPartition, Is.True);
            Assert.That(decision.UseExistingPartition, Is.False);
            Assert.That(decision.RejectStrictAffinityFull, Is.False);
            Assert.That(decision.Partition, Is.Null);
        }

        [Test]
        public void DecidePicksFirstPartitionWithCapacity()
        {
            var policy = new PartitionPlacementPolicy(2);
            FakeManagedSubscription first = NewFake(id: 1);
            FakeManagedSubscription second = NewFake(id: 2);
            policy.OnPartitionAdded(first);
            policy.OnPartitionAdded(second);

            // Fill the first to its cap.
            policy.OnItemAdded(new V2Options(), first);
            policy.OnItemAdded(new V2Options(), first);

            PlacementDecision decision = policy.Decide(new V2Options(),
                [first, second]);

            Assert.That(decision.UseExistingPartition, Is.True);
            Assert.That(decision.Partition, Is.SameAs(second));
        }

        [Test]
        public void DecideAffinityFreeItemFitsInPartitionWithRoom()
        {
            var policy = new PartitionPlacementPolicy(3);
            FakeManagedSubscription part = NewFake(id: 1);
            policy.OnPartitionAdded(part);
            policy.OnItemAdded(new V2Options(), part);

            PlacementDecision decision = policy.Decide(new V2Options(),
                [part]);

            Assert.That(decision.UseExistingPartition, Is.True);
            Assert.That(decision.Partition, Is.SameAs(part));
        }

        [Test]
        public void DecidePinsAffinityGroupToFirstChosenPartition()
        {
            var policy = new PartitionPlacementPolicy(10);
            FakeManagedSubscription part = NewFake(id: 1);
            policy.OnPartitionAdded(part);

            var withAffinity = new V2Options { Affinity = "ALPHA" };

            // First add — picks `part`, pins ALPHA → part.
            PlacementDecision first = policy.Decide(withAffinity,
                [part]);
            Assert.That(first.UseExistingPartition, Is.True);
            Assert.That(first.Partition, Is.SameAs(part));

            // Record the placement.
            policy.OnItemAdded(withAffinity, part);

            // Now add a second partition with capacity — the policy
            // must still send ALPHA to `part` because affinity is
            // strictly pinned.
            FakeManagedSubscription part2 = NewFake(id: 2);
            policy.OnPartitionAdded(part2);

            PlacementDecision second = policy.Decide(withAffinity,
                [part, part2]);
            Assert.That(second.UseExistingPartition, Is.True);
            Assert.That(second.Partition, Is.SameAs(part),
                "affinity-tagged items must stay pinned to the first partition that received them");
        }

        [Test]
        public void DecideRejectsAffinityGroupWhenPinnedPartitionFull()
        {
            var policy = new PartitionPlacementPolicy(2);
            FakeManagedSubscription part = NewFake(id: 1);
            FakeManagedSubscription part2 = NewFake(id: 2);
            policy.OnPartitionAdded(part);
            policy.OnPartitionAdded(part2);

            var withAffinity = new V2Options { Affinity = "BETA" };
            policy.OnItemAdded(withAffinity, part);
            policy.OnItemAdded(withAffinity, part);
            // Pinned partition is now full (count == cap).

            PlacementDecision decision = policy.Decide(withAffinity,
                [part, part2]);

            Assert.That(decision.RejectStrictAffinityFull, Is.True,
                "strict-affinity contract must reject rather than split the group");
            Assert.That(decision.UseExistingPartition, Is.False);
            Assert.That(decision.RequiresNewPartition, Is.False);
            Assert.That(decision.Partition, Is.SameAs(part),
                "rejection diagnostic should report the pinned partition");
        }

        [Test]
        public void DecideNoExistingPartitionFitsRequestsNewPartition()
        {
            var policy = new PartitionPlacementPolicy(1);
            FakeManagedSubscription part = NewFake(id: 1);
            policy.OnPartitionAdded(part);
            policy.OnItemAdded(new V2Options(), part);

            PlacementDecision decision = policy.Decide(new V2Options(),
                [part]);

            Assert.That(decision.RequiresNewPartition, Is.True);
        }

        [Test]
        public void OnPartitionCapReachedSkipsPartitionInFuturePlacement()
        {
            var policy = new PartitionPlacementPolicy(uint.MaxValue);
            FakeManagedSubscription part = NewFake(id: 1);
            FakeManagedSubscription part2 = NewFake(id: 2);
            policy.OnPartitionAdded(part);
            policy.OnPartitionAdded(part2);

            // Server returned BadTooManyMonitoredItems against `part`
            // — even though the cap is unbounded, mark no-grow so
            // subsequent placements skip it.
            policy.OnPartitionCapReached(part);

            PlacementDecision decision = policy.Decide(new V2Options(),
                [part, part2]);
            Assert.That(decision.UseExistingPartition, Is.True);
            Assert.That(decision.Partition, Is.SameAs(part2));
            Assert.That(policy.IsNoGrow(part), Is.True);
            Assert.That(policy.IsNoGrow(part2), Is.False);
        }

        [Test]
        public void OnItemRemovedDecreasesCountAndRestoresCapacity()
        {
            var policy = new PartitionPlacementPolicy(2);
            FakeManagedSubscription part = NewFake(id: 1);
            policy.OnPartitionAdded(part);
            policy.OnItemAdded(new V2Options(), part);
            policy.OnItemAdded(new V2Options(), part);

            Assert.That(policy.GetCount(part), Is.EqualTo(2u));

            policy.OnItemRemoved(part);
            Assert.That(policy.GetCount(part), Is.EqualTo(1u));

            // Removal must restore capacity for new items.
            PlacementDecision decision = policy.Decide(new V2Options(),
                [part]);
            Assert.That(decision.UseExistingPartition, Is.True);
        }

        [Test]
        public void AffinityIndexSurvivesItemRemovalButClearsOnPartitionRemoval()
        {
            var policy = new PartitionPlacementPolicy(5);
            FakeManagedSubscription part = NewFake(id: 1);
            policy.OnPartitionAdded(part);
            var opts = new V2Options { Affinity = "GAMMA" };
            policy.OnItemAdded(opts, part);

            // Single item removed — affinity pin stays.
            policy.OnItemRemoved(part);
            Assert.That(policy.TryGetAffinityPartition("GAMMA"), Is.SameAs(part));

            // Partition removed — affinity pin cleared so a future
            // add picks/mints freely.
            policy.OnPartitionRemoved(part);
            Assert.That(policy.TryGetAffinityPartition("GAMMA"), Is.Null);
            Assert.That(policy.GetCount(part), Is.Zero);
            Assert.That(policy.IsNoGrow(part), Is.False);
        }

        [Test]
        public void DecideThrowsOnNullArguments()
        {
            var policy = new PartitionPlacementPolicy(10);

            Assert.That(() => policy.Decide(null!, []),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => policy.Decide(new V2Options(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorTreatsZeroMaxPartitionCountAsUnbounded()
        {
            // DoS guard: 0 means uint.MaxValue (no cap) so legacy
            // callers that do not yet thread the option through
            // keep their old behaviour.
            var policy = new PartitionPlacementPolicy(
                maxItemsPerPartition: 5, maxPartitionCount: 0);

            Assert.That(policy.MaxPartitionCount, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void ConstructorPreservesExplicitMaxPartitionCount()
        {
            var policy = new PartitionPlacementPolicy(
                maxItemsPerPartition: 5, maxPartitionCount: 4);

            Assert.That(policy.MaxPartitionCount, Is.EqualTo(4u));
        }

        [Test]
        public void DecideRejectsOnceMaxPartitionCountReached()
        {
            // DoS guard: once partitions == maxPartitionCount and no
            // existing partition has capacity, Decide must surface
            // RejectMaxPartitionCountReached instead of asking for a
            // new partition. This is what stops a hostile or buggy
            // server returning Bad_TooManyMonitoredItems on every
            // CreateMonitoredItems reply from causing unbounded
            // client memory + server handle growth.
            var policy = new PartitionPlacementPolicy(
                maxItemsPerPartition: 1, maxPartitionCount: 2);
            FakeManagedSubscription p1 = NewFake(id: 1);
            FakeManagedSubscription p2 = NewFake(id: 2);
            policy.OnPartitionAdded(p1);
            policy.OnPartitionAdded(p2);
            policy.OnItemAdded(new V2Options(), p1);
            policy.OnItemAdded(new V2Options(), p2);

            // Both partitions full. Adding a third would require
            // minting a new partition past the cap.
            PlacementDecision decision = policy.Decide(
                new V2Options(),
                [p1, p2]);

            Assert.That(decision.RejectMaxPartitionCountReached, Is.True);
            Assert.That(decision.RequiresNewPartition, Is.False);
            Assert.That(decision.UseExistingPartition, Is.False);
            Assert.That(decision.RejectStrictAffinityFull, Is.False);
            Assert.That(decision.Partition, Is.Null);
        }

        [Test]
        public void DecideAllowsExistingPartitionEvenAtMaxPartitionCount()
        {
            // The DoS guard only blocks NEW partition mints. If an
            // existing partition can host the item, the cap does
            // not apply.
            var policy = new PartitionPlacementPolicy(
                maxItemsPerPartition: 5, maxPartitionCount: 2);
            FakeManagedSubscription p1 = NewFake(id: 1);
            FakeManagedSubscription p2 = NewFake(id: 2);
            policy.OnPartitionAdded(p1);
            policy.OnPartitionAdded(p2);
            // p1 has room (count=0, cap=5)
            PlacementDecision decision = policy.Decide(
                new V2Options(),
                [p1, p2]);

            Assert.That(decision.UseExistingPartition, Is.True);
            Assert.That(decision.Partition, Is.SameAs(p1));
        }

        [Test]
        public void DecideAllowsMintUntilMaxPartitionCount()
        {
            // First mint up to N-1 succeeds (RequiresNewPartition);
            // mint N is blocked.
            var policy = new PartitionPlacementPolicy(
                maxItemsPerPartition: 1, maxPartitionCount: 3);
            FakeManagedSubscription p1 = NewFake(id: 1);
            policy.OnPartitionAdded(p1);
            policy.OnItemAdded(new V2Options(), p1);

            // 1 existing partition, full. Mint 2nd → allowed.
            PlacementDecision d2 = policy.Decide(
                new V2Options(),
                [p1]);
            Assert.That(d2.RequiresNewPartition, Is.True);

            FakeManagedSubscription p2 = NewFake(id: 2);
            policy.OnPartitionAdded(p2);
            policy.OnItemAdded(new V2Options(), p2);

            // 2 existing partitions, both full. Mint 3rd → allowed
            // (count==2 < cap==3).
            PlacementDecision d3 = policy.Decide(
                new V2Options(),
                [p1, p2]);
            Assert.That(d3.RequiresNewPartition, Is.True);

            FakeManagedSubscription p3 = NewFake(id: 3);
            policy.OnPartitionAdded(p3);
            policy.OnItemAdded(new V2Options(), p3);

            // 3 existing partitions, all full. Mint 4th would
            // exceed cap → reject.
            PlacementDecision d4 = policy.Decide(
                new V2Options(),
                [p1, p2, p3]);
            Assert.That(d4.RejectMaxPartitionCountReached, Is.True);
        }

        private static FakeManagedSubscription NewFake(uint id)
        {
            return new FakeManagedSubscription
            {
                Id = id,
                Created = true
            };
        }
    }
}
