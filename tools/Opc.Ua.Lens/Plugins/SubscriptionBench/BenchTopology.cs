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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Server-independent scaling engine for the Subscription Bench. It owns the live
/// topology (subscriptions and their monitored items) and converges it toward the
/// requested slider targets, drawing items from the variable pool round-robin.
/// Growth and shrink use the injected <see cref="IBenchResourceFactory"/> so the
/// desktop drives the real V2 stack while tests drive a fake that records created
/// and disposed resources. Shared subscription and item settings are applied to
/// every current resource and used as the template for subsequently created ones.
/// All mutating operations are serialized and honour cancellation so a disconnect
/// or disposal can cancel and await in-flight scaling before releasing resources.
/// </summary>
internal sealed class BenchTopology : IAsyncDisposable
{
    private readonly IBenchResourceFactory m_factory;
    private readonly ILogger m_log;
    private readonly SemaphoreSlim m_gate = new(1, 1);
    private readonly System.Threading.Lock m_sync = new();
    private readonly List<SubEntry> m_subs = new();
    private SubscriptionConfig m_subConfig;
    private MonitoredItemSettings m_itemSettings;
    private long m_itemKeySeq;
    private bool m_disposed;

    public BenchTopology(
        IBenchResourceFactory factory,
        SubscriptionConfig subscriptionConfig,
        MonitoredItemSettings itemSettings,
        ILogger log)
    {
        m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        m_subConfig = subscriptionConfig ?? throw new ArgumentNullException(nameof(subscriptionConfig));
        m_itemSettings = itemSettings ?? throw new ArgumentNullException(nameof(itemSettings));
        m_log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Outcome of a converge pass: the achieved subscription count and the achieved
    /// per-subscription item count, whether the engine was unavailable and any
    /// surfaced error message.
    /// </summary>
    public readonly record struct ConvergeResult(
        int Subscriptions,
        int ItemsPerSubscription,
        bool EngineUnavailable,
        string? Error);

    /// <summary>
    /// True when the connected engine can host bench subscriptions.
    /// </summary>
    public bool IsReady => m_factory.IsReady;

    /// <summary>
    /// Number of live subscriptions.
    /// </summary>
    public int SubscriptionCount
    {
        get
        {
            lock (m_sync)
            {
                return m_subs.Count;
            }
        }
    }

    /// <summary>
    /// Total number of live monitored items across every subscription.
    /// </summary>
    public int TotalItemCount
    {
        get
        {
            lock (m_sync)
            {
                int total = 0;
                foreach (SubEntry entry in m_subs)
                {
                    total += entry.Items.Count;
                }
                return total;
            }
        }
    }

    /// <summary>
    /// Server-revised publishing interval of the representative (first) subscription,
    /// or null when no subscription is live.
    /// </summary>
    public double? RepresentativePublishingIntervalMs
    {
        get
        {
            lock (m_sync)
            {
                return m_subs.Count > 0 ? m_subs[0].Sub.RevisedPublishingIntervalMs : null;
            }
        }
    }

    /// <summary>
    /// Brings the live topology in line with the requested targets. An empty pool
    /// caps the item target at zero (subscriptions may still be torn down). Growth
    /// stops and surfaces the first failure rather than spinning.
    /// </summary>
    public async Task<ConvergeResult> ConvergeAsync(
        int targetSubscriptions,
        int targetItemsPerSubscription,
        IReadOnlyList<NodeId> pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pool);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ConvergeCoreAsync(
                targetSubscriptions, targetItemsPerSubscription, pool, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Stores new shared subscription parameters and applies them to every live
    /// subscription; subsequently created subscriptions use them too.
    /// </summary>
    public async Task ApplySubscriptionSettingsAsync(SubscriptionConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<SubEntry> snapshot;
            lock (m_sync)
            {
                m_subConfig = config;
                snapshot = new List<SubEntry>(m_subs);
            }
            foreach (SubEntry entry in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entry.Sub.ApplySubscriptionConfig(config);
            }
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Stores new shared monitored-item settings and applies them to every live
    /// item across every subscription; subsequently created items use them too.
    /// </summary>
    public async Task<int> ApplyItemSettingsAsync(MonitoredItemSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<IBenchItem> items = new();
            lock (m_sync)
            {
                m_itemSettings = settings;
                foreach (SubEntry entry in m_subs)
                {
                    items.AddRange(entry.Items);
                }
            }
            foreach (IBenchItem item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.ApplySettings(settings);
            }
            return items.Count;
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Counts the live monitored items currently reporting a bad status.
    /// </summary>
    public long CountBadItems()
    {
        List<IBenchItem> items = new();
        lock (m_sync)
        {
            foreach (SubEntry entry in m_subs)
            {
                items.AddRange(entry.Items);
            }
        }
        long bad = 0;
        foreach (IBenchItem item in items)
        {
            if (item.IsBad)
            {
                bad++;
            }
        }
        return bad;
    }

    public async ValueTask DisposeAsync()
    {
        lock (m_sync)
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
        }
        await m_gate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<SubEntry> snapshot;
            lock (m_sync)
            {
                snapshot = new List<SubEntry>(m_subs);
                m_subs.Clear();
            }
            foreach (SubEntry entry in snapshot)
            {
                await DisposeSubAsync(entry.Sub).ConfigureAwait(false);
            }
        }
        finally
        {
            m_gate.Release();
            m_gate.Dispose();
        }
    }

    private async Task<ConvergeResult> ConvergeCoreAsync(
        int targetSubscriptions,
        int targetItems,
        IReadOnlyList<NodeId> pool,
        CancellationToken cancellationToken)
    {
        targetSubscriptions = Math.Max(0, targetSubscriptions);
        targetItems = Math.Max(0, targetItems);
        if (pool.Count == 0)
        {
            targetItems = 0;
        }

        string? error = null;
        bool engineUnavailable = false;

        int currentSubs;
        lock (m_sync)
        {
            currentSubs = m_subs.Count;
        }

        if (targetSubscriptions > currentSubs)
        {
            if (!m_factory.IsReady)
            {
                engineUnavailable = true;
                BenchTopologyLog.EngineUnavailable(m_log);
            }
            else
            {
                for (int i = currentSubs; i < targetSubscriptions; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IBenchSubscription sub;
                    try
                    {
                        sub = m_factory.CreateSubscription();
                        sub.ApplySubscriptionConfig(SnapshotSubConfig());
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Surface stack creation failures and stop growing rather
                        // than spinning; matches the tool's other stack boundaries.
                        error = ex.Message;
                        BenchTopologyLog.ConvergeFailed(m_log, ex);
                        break;
                    }
                    lock (m_sync)
                    {
                        m_subs.Add(new SubEntry(sub));
                    }
                }
            }
        }
        else if (targetSubscriptions < currentSubs)
        {
            List<SubEntry> doomed = new();
            lock (m_sync)
            {
                int removeCount = m_subs.Count - targetSubscriptions;
                int startIdx = m_subs.Count - removeCount;
                for (int i = startIdx; i < m_subs.Count; i++)
                {
                    doomed.Add(m_subs[i]);
                }
                m_subs.RemoveRange(startIdx, removeCount);
            }
            foreach (SubEntry entry in doomed)
            {
                await DisposeSubAsync(entry.Sub).ConfigureAwait(false);
            }
        }

        MonitoredItemSettings itemSettings = SnapshotItemSettings();
        List<SubEntry> subsSnapshot;
        lock (m_sync)
        {
            subsSnapshot = new List<SubEntry>(m_subs);
        }
        foreach (SubEntry entry in subsSnapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentItems;
            lock (m_sync)
            {
                currentItems = entry.Items.Count;
            }
            if (targetItems > currentItems && pool.Count > 0)
            {
                List<IBenchItem> added = new();
                for (int slot = currentItems; slot < targetItems; slot++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    NodeId node = pool[slot % pool.Count];
                    string key = "bench-" + Interlocked.Increment(ref m_itemKeySeq)
                        .ToString(CultureInfo.InvariantCulture);
                    IBenchItem? item;
                    try
                    {
                        item = entry.Sub.TryAddItem(key, node, itemSettings);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        error ??= ex.Message;
                        BenchTopologyLog.ConvergeFailed(m_log, ex);
                        break;
                    }
                    if (item is null)
                    {
                        BenchTopologyLog.ItemAddFailed(m_log, slot);
                        break;
                    }
                    added.Add(item);
                }
                lock (m_sync)
                {
                    entry.Items.AddRange(added);
                }
            }
            else if (targetItems < currentItems)
            {
                List<IBenchItem> doomed = new();
                lock (m_sync)
                {
                    int removeCount = entry.Items.Count - targetItems;
                    int startIdx = entry.Items.Count - removeCount;
                    for (int i = startIdx; i < entry.Items.Count; i++)
                    {
                        doomed.Add(entry.Items[i]);
                    }
                    entry.Items.RemoveRange(startIdx, removeCount);
                }
                foreach (IBenchItem item in doomed)
                {
                    try
                    {
                        item.Remove();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        error ??= ex.Message;
                        BenchTopologyLog.ConvergeFailed(m_log, ex);
                    }
                }
            }
        }

        int finalSubs;
        int itemsPerSub;
        lock (m_sync)
        {
            finalSubs = m_subs.Count;
            if (finalSubs == 0)
            {
                itemsPerSub = targetItems;
            }
            else
            {
                itemsPerSub = int.MaxValue;
                foreach (SubEntry entry in m_subs)
                {
                    itemsPerSub = Math.Min(itemsPerSub, entry.Items.Count);
                }
            }
        }
        return new ConvergeResult(finalSubs, itemsPerSub, engineUnavailable, error);
    }

    private async Task DisposeSubAsync(IBenchSubscription sub)
    {
        try
        {
            await sub.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Disposal failures must not abort teardown; surface and continue.
            BenchTopologyLog.SubscriptionDisposeFailed(m_log, ex);
        }
    }

    private SubscriptionConfig SnapshotSubConfig()
    {
        lock (m_sync)
        {
            return m_subConfig;
        }
    }

    private MonitoredItemSettings SnapshotItemSettings()
    {
        lock (m_sync)
        {
            return m_itemSettings;
        }
    }

    private sealed class SubEntry
    {
        public SubEntry(IBenchSubscription sub)
        {
            Sub = sub;
        }

        public IBenchSubscription Sub { get; }

        public List<IBenchItem> Items { get; } = new();
    }
}

internal static partial class BenchTopologyLog
{
    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchConvergeFailed,
        Level = LogLevel.Warning,
        Message = "Subscription Bench converge failed while creating a subscription.")]
    public static partial void ConvergeFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchEngineUnavailable,
        Level = LogLevel.Warning,
        Message = "Subscription Bench requires the V2 (channel) subscription engine to grow.")]
    public static partial void EngineUnavailable(ILogger logger);

    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchSubscriptionDisposeFailed,
        Level = LogLevel.Warning,
        Message = "Subscription Bench failed to dispose a subscription during shrink or teardown.")]
    public static partial void SubscriptionDisposeFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = UaLensEventIds.SubscriptionBenchItemAddFailed,
        Level = LogLevel.Debug,
        Message = "Subscription Bench could not add a monitored item at slot {Slot}.")]
    public static partial void ItemAddFailed(ILogger logger, int slot);
}
