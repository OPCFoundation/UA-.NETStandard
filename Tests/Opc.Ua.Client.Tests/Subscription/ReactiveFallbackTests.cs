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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;

#pragma warning disable CA2007, CA2000

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Tests for the reactive-fallback path on
    /// <see cref="CompositeMonitoredItemCollection"/>: when a
    /// partition's
    /// <see cref="Subscription.OnPartitionCapReached"/>
    /// hook signals
    /// <see cref="StatusCodes.BadTooManyMonitoredItems"/>, the
    /// placement policy must mark the partition no-grow so
    /// subsequent <see cref="CompositeMonitoredItemCollection.TryAdd"/>
    /// calls fan out instead of re-targeting the same partition.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ReactiveFallbackTests
    {
        [Test]
        public void OnPartitionCapReachedMarksPartitionNoGrow()
        {
            FakeManagedSubscription primary = NewFake(1);
            FakeManagedSubscription secondary = NewFake(2);
            var policy = new PartitionPlacementPolicy(uint.MaxValue);
            int factoryCalls = 0;
            var partitions = new List<IManagedSubscription> { primary };
            var lockObj = new object();
            var composite = new CompositeMonitoredItemCollection(
                partitions, lockObj, policy,
                () => { factoryCalls++; return secondary; });

            // Until the reactive fallback fires the policy sees an
            // unbounded cap on the primary, so any TryAdd targets it.
            Assert.That(policy.IsNoGrow(primary), Is.False);

            // Simulate the server returning Bad_TooManyMonitoredItems
            // on the primary partition.
            composite.OnPartitionCapReached(primary);

            Assert.That(policy.IsNoGrow(primary), Is.True,
                "primary must be flagged no-grow after the reactive fallback");

            // Next placement decision must skip the primary and mint
            // a new partition via the factory.
            PlacementDecision decision = policy.Decide(
                new MonitoredItemOptions(),
                partitions);
            Assert.That(decision.RequiresNewPartition, Is.True,
                "primary is no-grow and is the only registered partition — " +
                "the policy must request a new partition");
        }

        [Test]
        public void OnPartitionCapReachedIsNoopForSinglePartitionFastPath()
        {
            // Fast path: composite has no policy / no factory. The
            // reactive fallback hook degrades to a no-op so callers
            // can wire it unconditionally.
            FakeManagedSubscription primary = NewFake(1);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object());

            Assert.DoesNotThrow(() => composite.OnPartitionCapReached(primary));
        }

        [Test]
        public void OnPartitionCapReachedThrowsOnNull()
        {
            FakeManagedSubscription primary = NewFake(1);
            var policy = new PartitionPlacementPolicy(uint.MaxValue);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => NewFake(2));

            Assert.That(() => composite.OnPartitionCapReached(null!),
                Throws.TypeOf<ArgumentNullException>());
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
