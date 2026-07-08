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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using V2Options = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

#pragma warning disable CA2007, CA2000

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Tests for the secondary-partition idle-delete path on
    /// <see cref="CompositeMonitoredItemCollection"/>. Uses
    /// <see cref="FakeTimeProvider"/> to drive timer expiry
    /// deterministically.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [Category("LogicalSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SecondaryPartitionIdleDeleteTests
    {
        [Test]
        public async Task PrimaryPartitionIsNeverDisposedAsync()
        {
            // The primary partition must survive even when its
            // monitored-item count drops to zero — its server-side
            // id is the wrapper's stable identifier.
            FakeManagedSubscription primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            var policy = new PartitionPlacementPolicy(10);
            var timeProvider = new FakeTimeProvider();
            int disposeCalls = 0;
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => throw new InvalidOperationException("no secondary expected"),
                timeProvider,
                TimeSpan.FromSeconds(10),
                _ => { disposeCalls++; return default; });

            Assert.That(composite.TryAdd("a",
                Make(new V2Options()), out IMonitoredItem? a), Is.True);
            Assert.That(composite.TryRemove(a!.ClientHandle), Is.True);

            timeProvider.Advance(TimeSpan.FromSeconds(60));
            await Task.Delay(50).ConfigureAwait(false);

            Assert.That(disposeCalls, Is.Zero,
                "primary partition is exempt from idle-delete");
        }

        [Test]
        public async Task SecondaryPartitionDisposedAfterIdleTimeoutAsync()
        {
            FakeManagedSubscription primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            FakeManagedSubscription secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1); // forces a secondary on item 2
            var timeProvider = new FakeTimeProvider();
            var disposeSignal = new TaskCompletionSource<IManagedSubscription>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary,
                timeProvider,
                TimeSpan.FromSeconds(10),
                p => { disposeSignal.TrySetResult(p); return default; });

            Assert.That(composite.TryAdd("a",
                Make(new V2Options()), out IMonitoredItem? a), Is.True);
            Assert.That(composite.TryAdd("b",
                Make(new V2Options()), out IMonitoredItem? b), Is.True);
            Assert.That(secondary.MonitoredItems.Count, Is.EqualTo(1u));

            // Remove the secondary's only item — timer arms.
            Assert.That(composite.TryRemove(b!.ClientHandle), Is.True);
            Assert.That(secondary.MonitoredItems.Count, Is.Zero);

            // Before the timeout, the disposer must not fire.
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            Assert.That(disposeSignal.Task.IsCompleted, Is.False);

            // After the timeout, the disposer fires (Task.Run + async
            // hops, so wait briefly).
            timeProvider.Advance(TimeSpan.FromSeconds(10));
            IManagedSubscription disposed = await disposeSignal.Task
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(disposed, Is.SameAs(secondary));
        }

        [Test]
        public async Task ReAddingItemBeforeTimeoutCancelsIdleDeleteAsync()
        {
            FakeManagedSubscription primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            FakeManagedSubscription secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var timeProvider = new FakeTimeProvider();
            int disposeCalls = 0;
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary,
                timeProvider,
                TimeSpan.FromSeconds(10),
                _ => { disposeCalls++; return default; });

            Assert.That(composite.TryAdd("a",
                Make(new V2Options()), out IMonitoredItem? a), Is.True);
            Assert.That(composite.TryAdd("b",
                Make(new V2Options()), out IMonitoredItem? b), Is.True);

            Assert.That(composite.TryRemove(b!.ClientHandle), Is.True);

            // Advance halfway then re-populate the secondary so the
            // timer re-checks state on fire and bails out.
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            Assert.That(composite.TryAdd("c",
                Make(new V2Options()), out _), Is.True);

            timeProvider.Advance(TimeSpan.FromSeconds(60));
            await Task.Delay(50).ConfigureAwait(false);

            Assert.That(disposeCalls, Is.Zero,
                "re-adding an item before timeout must keep the partition alive");
        }

        [Test]
        public void IdleDeleteDisabledByInfiniteTimeout()
        {
            // Infinite timeout = feature off, no timer is ever armed
            // regardless of secondary disposer being supplied.
            FakeManagedSubscription primary = NewFake(1);
            primary.MonitoredItems = new InMemoryCollection();
            FakeManagedSubscription secondary = NewFake(2);
            secondary.MonitoredItems = new InMemoryCollection();

            var policy = new PartitionPlacementPolicy(1);
            var timeProvider = new FakeTimeProvider();
            int disposeCalls = 0;
            var composite = new CompositeMonitoredItemCollection(
                [primary],
                new object(),
                policy,
                () => secondary,
                timeProvider,
                Timeout.InfiniteTimeSpan,
                _ => { disposeCalls++; return default; });

            Assert.That(composite.TryAdd("a",
                Make(new V2Options()), out _), Is.True);
            Assert.That(composite.TryAdd("b",
                Make(new V2Options()), out IMonitoredItem? b), Is.True);
            Assert.That(composite.TryRemove(b!.ClientHandle), Is.True);

            timeProvider.Advance(TimeSpan.FromHours(1));
            Assert.That(disposeCalls, Is.Zero);
        }

        private static FakeManagedSubscription NewFake(uint id)
        {
            return new FakeManagedSubscription { Id = id, Created = true };
        }

        private static OptionsMonitor<V2Options> Make(V2Options opts)
        {
            return new OptionsMonitor<V2Options>(opts);
        }

        /// <summary>
        /// Re-uses the same minimal in-memory collection helper as
        /// CompositeMonitoredItemCollectionTests; kept local so this
        /// file is independent.
        /// </summary>
        private sealed class InMemoryCollection : IMonitoredItemCollection
        {
            private readonly Dictionary<string, FakeMonitoredItem> m_byName
                = new(StringComparer.Ordinal);
            private readonly Dictionary<uint, FakeMonitoredItem> m_byHandle = [];

            public uint Count => (uint)m_byHandle.Count;

            public IEnumerable<IMonitoredItem> Items
            {
                get
                {
                    var snapshot = new List<IMonitoredItem>(m_byHandle.Count);
                    foreach (FakeMonitoredItem v in m_byHandle.Values)
                    {
                        snapshot.Add(v);
                    }
                    return snapshot;
                }
            }

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
                return [];
            }

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
            public IEnumerable<IMonitoredItem> TriggeringItems => [];
            public IEnumerable<IMonitoredItem> TriggeredItems => [];
            public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
#endif
