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
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Foundation tests for <see cref="LogicalSubscription"/>. These
    /// verify the single-partition fast path (the only path supported
    /// in the foundational milestone) — every <see cref="ISubscription"/>
    /// member must delegate to the primary partition with no observable
    /// behaviour change, and the new
    /// <see cref="IPartitionedSubscription"/> members must report the
    /// single-partition layout consistently.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class LogicalSubscriptionTests
    {
        [Test]
        public void ConstructorThrowsOnNullPrimary()
        {
            Assert.That(
                () => new LogicalSubscription(null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property(nameof(ArgumentNullException.ParamName))
                    .EqualTo("primary"));
        }

        [Test]
        public async Task ImplementsPartitionedSubscriptionAndIsSubscriptionAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 42, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                Assert.That(sut, Is.InstanceOf<ISubscription>());
                Assert.That(sut, Is.InstanceOf<IPartitionedSubscription>());
                Assert.That(sut, Is.InstanceOf<ILogicalSubscription>());
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SinglePartitionFastPathDelegatesAllSubscriptionMembersAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 42, created: true);
            fake.CurrentPublishingInterval = TimeSpan.FromMilliseconds(500);
            fake.CurrentPriority = 7;
            fake.CurrentLifetimeCount = 1200;
            fake.CurrentKeepAliveCount = 30;
            fake.CurrentPublishingEnabled = true;
            fake.CurrentMaxNotificationsPerPublish = 256;
            fake.MissingMessageCount = 11;
            fake.RepublishMessageCount = 5;

            LogicalSubscription sut = new(fake);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sut.Created, Is.True);
                    Assert.That(sut.CurrentPublishingInterval,
                        Is.EqualTo(fake.CurrentPublishingInterval));
                    Assert.That(sut.CurrentPriority, Is.EqualTo(fake.CurrentPriority));
                    Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(fake.CurrentLifetimeCount));
                    Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(fake.CurrentKeepAliveCount));
                    Assert.That(sut.CurrentPublishingEnabled,
                        Is.EqualTo(fake.CurrentPublishingEnabled));
                    Assert.That(sut.CurrentMaxNotificationsPerPublish,
                        Is.EqualTo(fake.CurrentMaxNotificationsPerPublish));
                    Assert.That(sut.MissingMessageCount, Is.EqualTo(11));
                    Assert.That(sut.RepublishMessageCount, Is.EqualTo(5));
                    Assert.That(sut.ServerId, Is.EqualTo(42u));
                });
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CreatedFollowsPrimaryStateThroughTransitionsAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 0, created: false);
            LogicalSubscription sut = new(fake);
            try
            {
                Assert.That(sut.Created, Is.False, "before primary is created");

                fake.Id = 99;
                fake.Created = true;

                Assert.That(sut.Created, Is.True, "after primary is created");
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PartitionCountIsOneForSinglePartitionAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                Assert.That(sut.PartitionCount, Is.EqualTo(1));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PartitionIdsContainsExactlyThePrimaryIdAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                Assert.That(sut.PartitionIds, Has.Count.EqualTo(1));
                Assert.That(sut.PartitionIds[0], Is.EqualTo(11u));
                // The single-partition fast path must round-trip the same
                // value as ServerId so consumers can correlate by id.
                Assert.That(sut.PartitionIds[0], Is.EqualTo(sut.ServerId));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PartitionsExposesThePrimaryAsTheOnlyEntryAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                Assert.That(sut.Partitions, Has.Count.EqualTo(1));
                Assert.That(sut.Partitions[0], Is.SameAs(fake));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetAsDurableForwardsToPrimaryAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            var requested = TimeSpan.FromHours(2);
            LogicalSubscription sut = new(fake);
            try
            {
                TimeSpan revised = await sut.SetAsDurableAsync(requested).ConfigureAwait(false);

                Assert.That(fake.SetAsDurableCalls, Has.Count.EqualTo(1));
                Assert.That(fake.SetAsDurableCalls[0].Lifetime, Is.EqualTo(requested));
                Assert.That(revised, Is.EqualTo(requested));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConditionRefreshForwardsToPrimaryAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                await sut.ConditionRefreshAsync().ConfigureAwait(false);
                Assert.That(fake.ConditionRefreshAsyncCalls, Is.EqualTo(1));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RecreateForwardsToPrimaryAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                await sut.RecreateAsync().ConfigureAwait(false);
                Assert.That(fake.RecreateAsyncCalls, Is.EqualTo(1));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NotifyPausedFanOutsToEveryPartitionAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);
            try
            {
                sut.NotifySubscriptionManagerPaused(true);
                sut.NotifySubscriptionManagerPaused(false);

                Assert.That(fake.NotifySubscriptionManagerPausedCalls,
                    Is.EqualTo(s_pausedTrueFalseSequence));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeForwardsToEveryPartitionAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            LogicalSubscription sut = new(fake);

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(fake.DisposeAsyncCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task MonitoredItemsDelegatesToPrimaryInFastPathAsync()
        {
            FakeManagedSubscription fake = CreateFake(id: 11, created: true);
            // The wrapper's MonitoredItems is a composite, not the
            // primary's collection directly. In single-partition
            // fast-path mode it must delegate every member to the
            // primary's collection — verify by writing through the
            // primary and reading through the wrapper.
            var sentinel = new RecordingMonitoredItemCollection();
            fake.MonitoredItems = sentinel;

            LogicalSubscription sut = new(fake);
            try
            {
                _ = sut.MonitoredItems.Count;
                Assert.That(sentinel.CountCalls, Is.EqualTo(1),
                    "fast-path Count must be served by the primary collection");

                _ = sut.MonitoredItems.TryGetMonitoredItemByName("nope", out _);
                Assert.That(sentinel.TryGetByNameCalls, Is.EqualTo(1),
                    "fast-path name lookup must be served by the primary collection");
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static FakeManagedSubscription CreateFake(uint id, bool created)
        {
            return new FakeManagedSubscription
            {
                Id = id,
                Created = created,
                MonitoredItems = new FakeMonitoredItemCollection()
            };
        }

        private static readonly bool[] s_pausedTrueFalseSequence = [true, false];

        /// <summary>
        /// Minimal pass-through implementation of
        /// <see cref="IMonitoredItemCollection"/> so the fake
        /// subscription has a non-null collection without pulling in
        /// the production composite implementation.
        /// </summary>
        private sealed class FakeMonitoredItemCollection : IMonitoredItemCollection
        {
            public uint Count => 0;

            public IEnumerable<IMonitoredItem> Items => [];

            public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
                out IMonitoredItem? monitoredItem)
            {
                monitoredItem = null;
                return false;
            }

            public bool TryGetMonitoredItemByName(string name,
                out IMonitoredItem? monitoredItem)
            {
                monitoredItem = null;
                return false;
            }

            public bool TryAdd(string name,
                IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options,
                out IMonitoredItem? monitoredItem)
            {
                monitoredItem = null;
                return false;
            }

            public bool TryRemove(uint clientHandle)
            {
                return false;
            }

            public IReadOnlyList<IMonitoredItem> Update(
                IReadOnlyList<(string Name, IOptionsMonitor<MonitoredItems.MonitoredItemOptions> Options)> state)
            {
                return [];
            }
        }

        /// <summary>
        /// Counts every member invocation so tests can prove the
        /// fast-path delegated through to the primary partition.
        /// </summary>
        private sealed class RecordingMonitoredItemCollection : IMonitoredItemCollection
        {
            public int CountCalls { get; private set; }
            public int TryGetByNameCalls { get; private set; }

            public uint Count
            {
                get
                {
                    CountCalls++;
                    return 0;
                }
            }

            public IEnumerable<IMonitoredItem> Items => [];

            public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
                out IMonitoredItem? monitoredItem)
            {
                monitoredItem = null;
                return false;
            }

            public bool TryGetMonitoredItemByName(string name,
                out IMonitoredItem? monitoredItem)
            {
                TryGetByNameCalls++;
                monitoredItem = null;
                return false;
            }

            public bool TryAdd(string name,
                IOptionsMonitor<MonitoredItems.MonitoredItemOptions> options,
                out IMonitoredItem? monitoredItem)
            {
                monitoredItem = null;
                return false;
            }

            public bool TryRemove(uint clientHandle)
            {
                return false;
            }

            public IReadOnlyList<IMonitoredItem> Update(
                IReadOnlyList<(string Name, IOptionsMonitor<MonitoredItems.MonitoredItemOptions> Options)> state)
            {
                return [];
            }
        }
    }
}
