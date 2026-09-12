/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class BenchTopologyTests
{
    private static readonly NodeId[] s_pool = { new(1u), new(2u), new(3u) };

    private static BenchTopology Create(FakeBenchFactory factory)
    {
        return new BenchTopology(
            factory,
            new SubscriptionConfig(),
            new MonitoredItemSettings(),
            NullLogger.Instance);
    }

    [Test]
    public async Task ScalingUpCreatesSubscriptionsAndItemsFromThePoolRoundRobin()
    {
        var factory = new FakeBenchFactory();
        await using BenchTopology topology = Create(factory);

        BenchTopology.ConvergeResult result =
            await topology.ConvergeAsync(3, 4, s_pool, CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.Subscriptions, Is.EqualTo(3));
        Assert.That(result.ItemsPerSubscription, Is.EqualTo(4));
        Assert.That(result.EngineUnavailable, Is.False);
        Assert.That(result.Error, Is.Null);
        Assert.That(topology.SubscriptionCount, Is.EqualTo(3));
        Assert.That(topology.TotalItemCount, Is.EqualTo(12));
        Assert.That(factory.Created, Has.Count.EqualTo(3));
        foreach (FakeSub sub in factory.Created)
        {
            Assert.That(sub.LiveItemCount, Is.EqualTo(4));
            Assert.That(sub.ConfigApplyCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(sub.Items[0].Node, Is.EqualTo(s_pool[0]));
            Assert.That(sub.Items[1].Node, Is.EqualTo(s_pool[1]));
            Assert.That(sub.Items[3].Node, Is.EqualTo(s_pool[0]));
        }
    }

    [Test]
    public async Task ScalingDownDisposesSubscriptionsAndRemovesTrailingItems()
    {
        var factory = new FakeBenchFactory();
        await using BenchTopology topology = Create(factory);
        await topology.ConvergeAsync(3, 4, s_pool, CancellationToken.None).ConfigureAwait(false);

        await topology.ConvergeAsync(1, 2, s_pool, CancellationToken.None).ConfigureAwait(false);

        Assert.That(topology.SubscriptionCount, Is.EqualTo(1));
        Assert.That(topology.TotalItemCount, Is.EqualTo(2));
        Assert.That(factory.Disposed, Has.Count.EqualTo(2));
        FakeSub remaining = factory.Created.Single(sub => !sub.Disposed);
        Assert.That(remaining.LiveItemCount, Is.EqualTo(2));
        Assert.That(remaining.Items.Count(item => item.Removed), Is.EqualTo(2));
    }

    [Test]
    public async Task StoppingToZeroReleasesEverySubscriptionAndItem()
    {
        var factory = new FakeBenchFactory();
        await using BenchTopology topology = Create(factory);
        await topology.ConvergeAsync(2, 3, s_pool, CancellationToken.None).ConfigureAwait(false);

        await topology.ConvergeAsync(0, 0, Array.Empty<NodeId>(), CancellationToken.None).ConfigureAwait(false);

        Assert.That(topology.SubscriptionCount, Is.Zero);
        Assert.That(topology.TotalItemCount, Is.Zero);
        Assert.That(factory.Disposed, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DisposingReleasesEverySubscription()
    {
        var factory = new FakeBenchFactory();
        BenchTopology topology = Create(factory);
        await topology.ConvergeAsync(2, 2, s_pool, CancellationToken.None).ConfigureAwait(false);

        await topology.DisposeAsync().ConfigureAwait(false);

        Assert.That(factory.Disposed, Has.Count.EqualTo(2));
        Assert.That(topology.SubscriptionCount, Is.Zero);
    }

    [Test]
    public async Task SubscriptionSettingsApplyToEveryCurrentAndSubsequentlyCreatedSubscription()
    {
        var factory = new FakeBenchFactory();
        await using BenchTopology topology = Create(factory);
        await topology.ConvergeAsync(2, 0, s_pool, CancellationToken.None).ConfigureAwait(false);

        var updated = new SubscriptionConfig { KeepAliveCount = 42 };
        await topology.ApplySubscriptionSettingsAsync(updated, CancellationToken.None).ConfigureAwait(false);

        foreach (FakeSub sub in factory.Created)
        {
            Assert.That(sub.LastConfig!.KeepAliveCount, Is.EqualTo(42u));
        }

        await topology.ConvergeAsync(3, 0, s_pool, CancellationToken.None).ConfigureAwait(false);
        Assert.That(factory.Created[^1].LastConfig!.KeepAliveCount, Is.EqualTo(42u));
    }

    [Test]
    public async Task ItemSettingsApplyToEveryCurrentItemAndToItemsAddedLater()
    {
        var factory = new FakeBenchFactory();
        await using BenchTopology topology = Create(factory);
        await topology.ConvergeAsync(2, 3, s_pool, CancellationToken.None).ConfigureAwait(false);

        var settings = new MonitoredItemSettings { QueueSize = 9 };
        int applied = await topology.ApplyItemSettingsAsync(settings, CancellationToken.None).ConfigureAwait(false);

        Assert.That(applied, Is.EqualTo(6));
        foreach (FakeSub sub in factory.Created)
        {
            foreach (FakeItem item in sub.Items.Where(i => !i.Removed))
            {
                Assert.That(item.Settings.QueueSize, Is.EqualTo(9u));
            }
        }

        await topology.ConvergeAsync(2, 5, s_pool, CancellationToken.None).ConfigureAwait(false);
        foreach (FakeSub sub in factory.Created)
        {
            Assert.That(sub.Items[^1].Settings.QueueSize, Is.EqualTo(9u));
        }
    }

    [Test]
    public async Task EngineUnavailableIsReportedAndNoSubscriptionsAreCreated()
    {
        var factory = new FakeBenchFactory { Ready = false };
        await using BenchTopology topology = Create(factory);

        BenchTopology.ConvergeResult result =
            await topology.ConvergeAsync(2, 2, s_pool, CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.EngineUnavailable, Is.True);
        Assert.That(result.Subscriptions, Is.Zero);
        Assert.That(topology.SubscriptionCount, Is.Zero);
        Assert.That(factory.Created, Is.Empty);
    }

    [Test]
    public async Task CreateFailureSurfacesTheErrorAndClampsToTheAchievedCount()
    {
        var factory = new FakeBenchFactory { CreateFailAfter = 2 };
        await using BenchTopology topology = Create(factory);

        BenchTopology.ConvergeResult result =
            await topology.ConvergeAsync(5, 1, s_pool, CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Subscriptions, Is.EqualTo(2));
        Assert.That(topology.SubscriptionCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ItemAddRejectionStopsGrowingItemsForThatSubscription()
    {
        var factory = new FakeBenchFactory { ItemAddFailAfter = 2 };
        await using BenchTopology topology = Create(factory);

        BenchTopology.ConvergeResult result =
            await topology.ConvergeAsync(1, 5, s_pool, CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.ItemsPerSubscription, Is.EqualTo(2));
        Assert.That(topology.TotalItemCount, Is.EqualTo(2));
    }

    [Test]
    public async Task AnEmptyPoolCapsItemsAtZeroButStillScalesSubscriptions()
    {
        var factory = new FakeBenchFactory();
        await using BenchTopology topology = Create(factory);

        BenchTopology.ConvergeResult result =
            await topology.ConvergeAsync(2, 10, Array.Empty<NodeId>(), CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.Subscriptions, Is.EqualTo(2));
        Assert.That(topology.TotalItemCount, Is.Zero);
    }

    [Test]
    public async Task AlreadyCancelledConvergeThrowsAndCreatesNothing()
    {
        var factory = new FakeBenchFactory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThatAsync(async () =>
        {
            await using BenchTopology topology = Create(factory);
            await topology.ConvergeAsync(5, 5, s_pool, cts.Token).ConfigureAwait(false);
        }, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(factory.Created, Is.Empty);
    }

    [Test]
    public async Task CancellationMidConvergeStopsFurtherWorkAndPropagates()
    {
        using var cts = new CancellationTokenSource();
        var factory = new FakeBenchFactory();
        factory.OnCreate = created =>
        {
            if (created == 2)
            {
                cts.Cancel();
            }
        };
        await using BenchTopology topology = Create(factory);

        await Assert.ThatAsync(
            () => topology.ConvergeAsync(6, 0, s_pool, cts.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(factory.Created, Has.Count.LessThan(6));
    }

    private sealed class FakeBenchFactory : IBenchResourceFactory
    {
        public bool Ready { get; set; } = true;
        public int CreateFailAfter { get; set; } = int.MaxValue;
        public int ItemAddFailAfter { get; set; } = int.MaxValue;
        public Action<int>? OnCreate { get; set; }
        public List<FakeSub> Created { get; } = new();
        public List<FakeSub> Disposed { get; } = new();

        public bool IsReady => Ready;

        public IBenchSubscription CreateSubscription()
        {
            OnCreate?.Invoke(Created.Count);
            if (Created.Count >= CreateFailAfter)
            {
                throw new InvalidOperationException("create failed");
            }
            var sub = new FakeSub(this);
            Created.Add(sub);
            return sub;
        }
    }

    private sealed class FakeSub : IBenchSubscription
    {
        private readonly FakeBenchFactory m_owner;

        public FakeSub(FakeBenchFactory owner)
        {
            m_owner = owner;
        }

        public List<FakeItem> Items { get; } = new();
        public int ConfigApplyCount { get; private set; }
        public SubscriptionConfig? LastConfig { get; private set; }
        public bool Disposed { get; private set; }

        public double RevisedPublishingIntervalMs => 1000;

        public int LiveItemCount => Items.Count(item => !item.Removed);

        public void ApplySubscriptionConfig(SubscriptionConfig config)
        {
            ConfigApplyCount++;
            LastConfig = config;
        }

        public IBenchItem? TryAddItem(string key, NodeId node, MonitoredItemSettings settings)
        {
            if (Items.Count(item => !item.Removed) >= m_owner.ItemAddFailAfter)
            {
                return null;
            }
            var item = new FakeItem(node, settings);
            Items.Add(item);
            return item;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            m_owner.Disposed.Add(this);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeItem : IBenchItem
    {
        public FakeItem(NodeId node, MonitoredItemSettings settings)
        {
            Node = node;
            Settings = settings;
        }

        public NodeId Node { get; }
        public MonitoredItemSettings Settings { get; private set; }
        public bool Removed { get; private set; }

        public bool IsBad => false;

        public void ApplySettings(MonitoredItemSettings settings)
        {
            Settings = settings;
        }

        public bool Remove()
        {
            Removed = true;
            return true;
        }
    }
}
