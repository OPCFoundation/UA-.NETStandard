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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using V2Options = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

#pragma warning disable CA2007 // ConfigureAwait — test code
#pragma warning disable CA2000 // Dispose objects — composite owns partition fakes

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Unit tests for <see cref="CompositeMonitoredItemCollection"/>.
    /// These tests exercise both the single-partition fast path (no
    /// factory / no policy) and the multi-partition growable path
    /// (factory + policy supplied), focusing on:
    /// <list type="bullet">
    /// <item><description>Global name uniqueness — re-add of the same
    /// name returns <c>false</c>.</description></item>
    /// <item><description>Strict-affinity placement — same affinity
    /// always lands in the same partition; rejection when the pinned
    /// partition is full.</description></item>
    /// <item><description>On-demand partition creation — the factory
    /// is called only when no existing partition has capacity.</description></item>
    /// <item><description>Single-partition fast-path delegates with
    /// no composite-side indexing.</description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CompositeMonitoredItemCollectionTests
    {
        [Test]
        public void ConstructorThrowsOnEmptyPartitionList()
        {
            Assert.That(() => new CompositeMonitoredItemCollection(
                new List<IManagedSubscription>(),
                new object()),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ConstructorThrowsOnNullArguments()
        {
            Assert.That(() => new CompositeMonitoredItemCollection(null!, new object()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new CompositeMonitoredItemCollection(
                new List<IManagedSubscription> { NewFake(1) }, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void FastPathDelegatesAllOperationsToPrimary()
        {
            // No policy + no factory ⇒ fast path. Every member must
            // call straight through to the primary's collection.
            var primary = NewFake(1);
            var inner = new RecordingCollection();
            primary.MonitoredItems = inner;

            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object());

            _ = composite.Count;
            _ = composite.Items;
            _ = composite.TryGetMonitoredItemByName("x", out _);
            _ = composite.TryGetMonitoredItemByClientHandle(99, out _);
            _ = composite.TryAdd("y",
                MakeOptions(new V2Options()),
                out _);
            _ = composite.TryRemove(7);
            _ = composite.Update(Array.Empty<(string, IOptionsMonitor<V2Options>)>());

            Assert.Multiple(() =>
            {
                Assert.That(inner.CountCalls, Is.EqualTo(1));
                Assert.That(inner.ItemsCalls, Is.EqualTo(1));
                Assert.That(inner.GetByNameCalls, Is.EqualTo(1));
                Assert.That(inner.GetByHandleCalls, Is.EqualTo(1));
                Assert.That(inner.TryAddCalls, Is.EqualTo(1));
                Assert.That(inner.TryRemoveCalls, Is.EqualTo(1));
                Assert.That(inner.UpdateCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public void TryAddPlacesItemInPrimaryWhenCapacityAvailable()
        {
            var primary = NewFake(1);
            var primaryInner = new InMemoryCollection();
            primary.MonitoredItems = primaryInner;

            var policy = new PartitionPlacementPolicy(10);
            int factoryInvocations = 0;
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => { factoryInvocations++; return NewFake(99); });

            Assert.That(composite.TryAdd("a",
                MakeOptions(new V2Options()),
                out IMonitoredItem? added), Is.True);
            Assert.That(added, Is.Not.Null);
            Assert.That(factoryInvocations, Is.Zero, "primary had capacity — no new partition needed");
            Assert.That(primaryInner.Items.Count(), Is.EqualTo(1));
        }

        [Test]
        public void TryAddMintsNewPartitionWhenPrimaryAtCap()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();

            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1); // primary holds at most 1
            int factoryInvocations = 0;
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => { factoryInvocations++; return secondary; });

            Assert.That(composite.TryAdd("a",
                MakeOptions(new V2Options()), out _), Is.True);
            Assert.That(composite.TryAdd("b",
                MakeOptions(new V2Options()), out _), Is.True);

            Assert.That(factoryInvocations, Is.EqualTo(1),
                "second add must trigger one factory call to mint the secondary partition");
            Assert.That(primary.MonitoredItems.Count, Is.EqualTo(1u));
            Assert.That(secondary.MonitoredItems.Count, Is.EqualTo(1u));
            Assert.That(composite.Count, Is.EqualTo(2u));
        }

        [Test]
        public void TryAddRejectsDuplicateNameAcrossPartitions()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary);

            Assert.That(composite.TryAdd("dup",
                MakeOptions(new V2Options()), out _), Is.True);

            // Force a new partition via cap exhaustion, then attempt
            // to add the same name again — global name uniqueness
            // must reject the second add even though it would land
            // in a different partition.
            Assert.That(composite.TryAdd("other",
                MakeOptions(new V2Options()), out _), Is.True);
            Assert.That(composite.TryAdd("dup",
                MakeOptions(new V2Options()), out IMonitoredItem? second), Is.False);
            Assert.That(second, Is.Null);
        }

        [Test]
        public void TryAddRespectsStrictAffinity()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(10);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary);

            // Force primary to fill so secondary mints, then start
            // pinning items with affinity to secondary.
            // Use a cap-1 policy variant for this assertion.
            var pinnedPolicy = new PartitionPlacementPolicy(1);
            var pinnedComposite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                pinnedPolicy,
                () => secondary);

            var affinity = new V2Options { Affinity = "PINNED" };
            // Item 1 (no affinity) → primary.
            Assert.That(pinnedComposite.TryAdd("seed",
                MakeOptions(new V2Options()), out _), Is.True);

            // Item 2 (affinity PINNED) → mints secondary, pins PINNED → secondary.
            Assert.That(pinnedComposite.TryAdd("p1",
                MakeOptions(affinity), out IMonitoredItem? p1), Is.True);
            Assert.That(p1, Is.Not.Null);
            Assert.That(secondary.MonitoredItems.Count, Is.EqualTo(1u));

            // Item 3 (affinity PINNED) — would normally fit in primary
            // (it has 0 capacity left), but secondary is full too. With
            // strict affinity, must reject.
            Assert.That(pinnedComposite.TryAdd("p2",
                MakeOptions(affinity), out IMonitoredItem? p2), Is.False);
            Assert.That(p2, Is.Null);
            Assert.That(pinnedComposite.Count, Is.EqualTo(2u));
        }

        [Test]
        public void TryRemoveLooksUpOwningPartitionAndDelegates()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary);

            Assert.That(composite.TryAdd("first",
                MakeOptions(new V2Options()), out IMonitoredItem? a), Is.True);
            Assert.That(composite.TryAdd("second",
                MakeOptions(new V2Options()), out IMonitoredItem? b), Is.True);

            Assert.That(composite.TryRemove(b!.ClientHandle), Is.True);
            Assert.That(composite.Count, Is.EqualTo(1u));
            Assert.That(secondary.MonitoredItems.Count, Is.Zero);
            Assert.That(primary.MonitoredItems.Count, Is.EqualTo(1u),
                "removal must target the owning partition only");
        }

        [Test]
        public void TryGetByNameReturnsItemAcrossPartitions()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary);

            Assert.That(composite.TryAdd("alpha", MakeOptions(new V2Options()), out _), Is.True);
            Assert.That(composite.TryAdd("beta",  MakeOptions(new V2Options()), out _), Is.True);

            Assert.That(composite.TryGetMonitoredItemByName("alpha", out IMonitoredItem? alpha), Is.True);
            Assert.That(alpha?.Name, Is.EqualTo("alpha"));

            Assert.That(composite.TryGetMonitoredItemByName("beta", out IMonitoredItem? beta), Is.True);
            Assert.That(beta?.Name, Is.EqualTo("beta"));
        }

        [Test]
        public void TryGetByClientHandleReturnsItemAcrossPartitions()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary);

            Assert.That(composite.TryAdd("alpha", MakeOptions(new V2Options()), out IMonitoredItem? a), Is.True);
            Assert.That(composite.TryAdd("beta", MakeOptions(new V2Options()), out IMonitoredItem? b), Is.True);

            Assert.That(composite.TryGetMonitoredItemByClientHandle(b!.ClientHandle, out var found), Is.True);
            Assert.That(found?.ClientHandle, Is.EqualTo(b.ClientHandle));
        }

        [Test]
        public void ItemsEnumeratesAcrossAllPartitions()
        {
            var primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary);

            Assert.That(composite.TryAdd("a", MakeOptions(new V2Options()), out _), Is.True);
            Assert.That(composite.TryAdd("b", MakeOptions(new V2Options()), out _), Is.True);

            // .Order() is net7+ only; use Array.Sort for net48 parity.
            string[] names = [.. composite.Items.Select(i => i.Name)];
            Array.Sort(names, StringComparer.Ordinal);
            Assert.That(names, Is.EqualTo(s_expectedAlphaBeta));
        }

        private static readonly string[] s_expectedAlphaBeta = ["a", "b"];

        private static FakeManagedSubscription NewFake(uint id)
        {
            return new FakeManagedSubscription
            {
                Id = id,
                Created = true,
                MonitoredItems = new InMemoryCollection()
            };
        }

        private static OptionsMonitor<V2Options> MakeOptions(V2Options options)
        {
            return new OptionsMonitor<V2Options>(options);
        }

        /// <summary>
        /// Counts every member invocation so fast-path delegation can
        /// be asserted by call counts rather than reference identity.
        /// </summary>
        private sealed class RecordingCollection : IMonitoredItemCollection
        {
            public int CountCalls { get; private set; }
            public int ItemsCalls { get; private set; }
            public int GetByNameCalls { get; private set; }
            public int GetByHandleCalls { get; private set; }
            public int TryAddCalls { get; private set; }
            public int TryRemoveCalls { get; private set; }
            public int UpdateCalls { get; private set; }

            public uint Count
            {
                get { CountCalls++; return 0; }
            }

            public IEnumerable<IMonitoredItem> Items
            {
                get { ItemsCalls++; return Array.Empty<IMonitoredItem>(); }
            }

            public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
                out IMonitoredItem? monitoredItem)
            {
                GetByHandleCalls++;
                monitoredItem = null;
                return false;
            }

            public bool TryGetMonitoredItemByName(string name,
                out IMonitoredItem? monitoredItem)
            {
                GetByNameCalls++;
                monitoredItem = null;
                return false;
            }

            public bool TryAdd(string name,
                IOptionsMonitor<V2Options> options,
                out IMonitoredItem? monitoredItem)
            {
                TryAddCalls++;
                monitoredItem = null;
                return false;
            }

            public bool TryRemove(uint clientHandle)
            {
                TryRemoveCalls++;
                return false;
            }

            public IReadOnlyList<IMonitoredItem> Update(
                IReadOnlyList<(string Name, IOptionsMonitor<V2Options> Options)> state)
            {
                UpdateCalls++;
                return Array.Empty<IMonitoredItem>();
            }
        }

        /// <summary>
        /// In-memory collection that actually stores added items so
        /// the composite's placement and delegation logic can be
        /// exercised end-to-end without spinning up the real engine.
        /// Mimics production by allocating <see cref="ClientHandle"/>
        /// from a process-global counter so handles stay unique
        /// across partition instances.
        /// </summary>
        private sealed class InMemoryCollection : IMonitoredItemCollection
        {
            private readonly Dictionary<string, FakeMonitoredItem> m_byName
                = new(StringComparer.Ordinal);
            private readonly Dictionary<uint, FakeMonitoredItem> m_byHandle = [];

            public uint Count => (uint)m_byHandle.Count;

            public IEnumerable<IMonitoredItem> Items => m_byHandle.Values.ToArray();

            public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
                [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
            {
                if (m_byHandle.TryGetValue(clientHandle, out FakeMonitoredItem? item))
                {
                    monitoredItem = item;
                    return true;
                }
                monitoredItem = null;
                return false;
            }

            public bool TryGetMonitoredItemByName(string name,
                [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
            {
                if (m_byName.TryGetValue(name, out FakeMonitoredItem? item))
                {
                    monitoredItem = item;
                    return true;
                }
                monitoredItem = null;
                return false;
            }

            public bool TryAdd(string name,
                IOptionsMonitor<V2Options> options,
                out IMonitoredItem? monitoredItem)
            {
                if (m_byName.ContainsKey(name))
                {
                    monitoredItem = null;
                    return false;
                }
                var item = new FakeMonitoredItem(name,
                    (uint)Interlocked.Increment(ref s_nextHandle));
                m_byName[name] = item;
                m_byHandle[item.ClientHandle] = item;
                monitoredItem = item;
                return true;
            }

            public bool TryRemove(uint clientHandle)
            {
                if (!m_byHandle.Remove(clientHandle, out FakeMonitoredItem? item))
                {
                    return false;
                }
                m_byName.Remove(item.Name);
                return true;
            }

            public IReadOnlyList<IMonitoredItem> Update(
                IReadOnlyList<(string Name, IOptionsMonitor<V2Options> Options)> state)
            {
                // Not exercised in these tests.
                return Array.Empty<IMonitoredItem>();
            }

            // Globally-unique counter mirrors production
            // MonitoredItem.GlobalClientHandleUint so handles do not
            // collide across InMemoryCollection instances within a
            // single composite.
            private static int s_nextHandle;
        }

        private sealed class FakeMonitoredItem : IMonitoredItem
        {
            public FakeMonitoredItem(string name, uint clientHandle)
            {
                Name = name;
                ClientHandle = clientHandle;
            }

            public string Name { get; }
            public uint Order => 0;
            public uint ServerId => 0;
            public bool Created => false;
            public ServiceResult Error => ServiceResult.Good;
            public MonitoringFilterResult? FilterResult => null;
            public MonitoringMode CurrentMonitoringMode => MonitoringMode.Reporting;
            public TimeSpan CurrentSamplingInterval => TimeSpan.Zero;
            public uint CurrentQueueSize => 0;
            public uint ClientHandle { get; }
            public IEnumerable<IMonitoredItem> TriggeringItems => Array.Empty<IMonitoredItem>();
            public IEnumerable<IMonitoredItem> TriggeredItems => Array.Empty<IMonitoredItem>();

            public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
