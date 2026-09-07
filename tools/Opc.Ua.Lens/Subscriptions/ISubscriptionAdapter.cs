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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Subscriptions;

internal sealed record SubscriptionConfig
{
    public TimeSpan PublishingInterval { get; init; } = TimeSpan.FromMilliseconds(1000);
    public uint LifetimeCount { get; init; } = 1000;
    public uint KeepAliveCount { get; init; } = 10;
    public uint MaxNotificationsPerPublish { get; init; } = 1000;
    public byte Priority { get; init; }
    public bool PublishingEnabled { get; init; } = true;

    // ----- Engine-level pipeline depth -----
    // Honoured by both engines:
    //   V2:      aliased to MinPublishWorkerCount / MaxPublishWorkerCount on
    //            the SubscriptionManager — the publish-worker pool size IS
    //            the request pipeline depth in the V2 model.
    //   Classic: drives ClassicSubscriptionEngine.MinPublishRequestCount /
    //            MaxPublishRequestCount (no worker abstraction in classic).
    public int MinPublishRequestCount { get; init; } = 2;
    public int MaxPublishRequestCount { get; init; } = 15;
}

internal sealed record MonitoredItemConfig
{
    public int Id { get; init; }
    public required string DisplayName { get; init; }
    public required NodeId NodeId { get; init; }
    public uint AttributeId { get; init; } = Attributes.Value;
    public TimeSpan SamplingInterval { get; init; } = TimeSpan.FromMilliseconds(1000);
    public uint QueueSize { get; init; } = 1;
    public bool DiscardOldest { get; init; } = true;
    public MonitoringMode MonitoringMode { get; init; } = MonitoringMode.Reporting;
    public bool IsEvent { get; init; }

    /// <summary>
    /// Optional <see cref="Opc.Ua.DataChangeFilter"/> for value-monitored items.
    /// When null the server applies its default (Trigger = StatusValue,
    /// DeadbandType = None).  Ignored for event monitored items (the adapters
    /// install the default <see cref="EventFilter"/> instead).
    /// </summary>
    public DataChangeFilter? DataChangeFilter { get; init; }
}

/// <summary>
/// Per-monitored-item live counters / latest sample, tracked by both
/// engine adapters and surfaced to the Subscription tab's status sub-pane
/// (B1) via <see cref="ISubscriptionAdapter.TryGetItemStats"/>.
/// </summary>
/// <remarks>
/// Writes happen on the adapter notification hot path (one writer per item
/// in practice); reads happen on the UI thread under a 250 ms throttle.
/// <see cref="Samples"/> is bumped with <see cref="Interlocked"/>;
/// <see cref="LastStatus"/> / <see cref="LastValueText"/> / <see cref="HasValue"/>
/// are guarded by a per-instance lock so the (status, value) pair is read
/// consistently.
/// </remarks>
internal sealed class MonitoredItemLiveStats
{
    private long m_samples;
    private readonly object m_sync = new();
    private StatusCode m_lastStatus;
    private string m_lastValueText = "—";
    private bool m_hasValue;

    /// <summary>Total notifications observed for this item (data + event).</summary>
    public long Samples => Interlocked.Read(ref m_samples);

    /// <summary>Last server-supplied status code (Good if never received).</summary>
    public StatusCode LastStatus
    {
        get { lock (m_sync) { return m_lastStatus; } }
    }

    /// <summary>Last variant rendered as a short string (or "—" if none).</summary>
    public string LastValueText
    {
        get { lock (m_sync) { return m_lastValueText; } }
    }

    /// <summary>True once at least one value has been recorded.</summary>
    public bool HasValue
    {
        get { lock (m_sync) { return m_hasValue; } }
    }

    /// <summary>
    /// Record an inbound DataChange — increments the sample counter and
    /// captures the latest status code and a truncated string rendering of
    /// the value for the UI.
    /// </summary>
    internal void RecordValue(DataValue value)
    {
        Interlocked.Increment(ref m_samples);
        string text = FormatVariant(value.WrappedValue);
        lock (m_sync)
        {
            m_lastStatus = value.StatusCode;
            m_lastValueText = text;
            m_hasValue = true;
        }
    }

    /// <summary>
    /// Record an inbound Event notification — only the counter advances;
    /// event fields aren't a single scalar value.
    /// </summary>
    internal void RecordEvent()
    {
        Interlocked.Increment(ref m_samples);
    }

    private static string FormatVariant(Variant v)
    {
        if (v.IsNull)
        {
            return "(null)";
        }
        string s = v.ToString() ?? string.Empty;
        if (s.Length > 64)
        {
            s = string.Concat(s.AsSpan(0, 61), "...");
        }
        return s;
    }
}

internal sealed class SubscriptionCounters
{
    private long m_dataMessages;
    private long m_eventMessages;
    private long m_keepAlives;
    private long m_dataValues;
    private long m_eventValues;

    public long DataMessages => Interlocked.Read(ref m_dataMessages);
    public long EventMessages => Interlocked.Read(ref m_eventMessages);
    public long KeepAlives => Interlocked.Read(ref m_keepAlives);
    public long DataValues => Interlocked.Read(ref m_dataValues);
    public long EventValues => Interlocked.Read(ref m_eventValues);

    internal void IncDataMessage(int values)
    {
        Interlocked.Increment(ref m_dataMessages);
        Interlocked.Add(ref m_dataValues, values);
    }
    internal void IncEventMessage(int values)
    {
        Interlocked.Increment(ref m_eventMessages);
        Interlocked.Add(ref m_eventValues, values);
    }
    internal void IncKeepAlive() => Interlocked.Increment(ref m_keepAlives);

    public void Reset()
    {
        Interlocked.Exchange(ref m_dataMessages, 0);
        Interlocked.Exchange(ref m_eventMessages, 0);
        Interlocked.Exchange(ref m_keepAlives, 0);
        Interlocked.Exchange(ref m_dataValues, 0);
        Interlocked.Exchange(ref m_eventValues, 0);
    }
}

internal interface ISubscriptionAdapter : IAsyncDisposable
{
    SubscriptionCounters Counters { get; }
    ChannelReader<NotificationEvent> Events { get; }
    TimeSpan CurrentPublishingInterval { get; }
    uint CurrentKeepAliveCount { get; }
    uint CurrentLifetimeCount { get; }

    /// <summary>
    /// Number of publish workers currently active in the engine's worker pool.
    /// V2 engine: from <c>ISubscriptionManager.PublishWorkerCount</c>.
    /// Classic engine: always 0 (no worker abstraction — the legacy model
    /// fires publish requests inline rather than via a pool).
    /// </summary>
    int PublishWorkerCount { get; }

    /// <summary>
    /// Number of publish requests in flight that have already received a
    /// successful response (the engine is processing them).  Sourced from
    /// <c>Session.GoodPublishRequestCount</c> in both engines.
    /// </summary>
    int GoodPublishRequestCount { get; }

    /// <summary>
    /// Number of publish requests that resulted in a service-level error.
    /// V2 only; Classic returns 0.
    /// </summary>
    int BadPublishRequestCount { get; }

    /// <summary>
    /// Total number of notification messages detected as missing during
    /// gap-walking of the SequenceNumber. Each missing slot triggers a
    /// republish — see <see cref="RepublishMessageCount"/>. V2 only;
    /// Classic returns 0.
    /// </summary>
    long MissingMessageCount { get; }

    /// <summary>
    /// Total number of republish requests issued by the engine to recover
    /// missing messages (counts every attempt, regardless of whether the
    /// server still holds the message). V2 only; Classic returns 0.
    /// </summary>
    long RepublishMessageCount { get; }

    /// <summary>
    /// Total number of <see cref="NotificationEvent"/>s the adapter
    /// dropped because the visualisation channel was full (bounded
    /// drop-oldest).  Surfaced on the animation seq line so the user
    /// can see when bursts overrun the renderer.
    /// </summary>
    long DroppedNotificationCount { get; }

    /// <summary>Configured floor for the V2 worker pool. 0 on Classic.</summary>
    int MinPublishWorkerCount { get; }
    /// <summary>Configured ceiling for the V2 worker pool. 0 on Classic.</summary>
    int MaxPublishWorkerCount { get; }
    /// <summary>Configured minimum publish-request pipeline depth.</summary>
    int MinPublishRequestCount { get; }
    /// <summary>Configured maximum publish-request pipeline depth.</summary>
    int MaxPublishRequestCount { get; }

    /// <summary>
    /// True if the engine exposes a distinct publish-worker pool concept
    /// (V2). Classic does not — the dialog hides worker fields when this is
    /// false.
    /// </summary>
    bool HasWorkerPool { get; }

    Task ApplySubscriptionAsync(SubscriptionConfig config, CancellationToken ct);
    Task<int> AddItemAsync(MonitoredItemConfig config, CancellationToken ct);
    Task RemoveItemAsync(int id, CancellationToken ct);

    /// <summary>
    /// Change the server-side monitoring mode for a single monitored item.
    /// On success the adapter's local <see cref="MonitoredItemConfig"/>
    /// snapshot in <see cref="Items"/> is updated so the caller can
    /// re-render the Mode column from the confirmed state.
    /// </summary>
    Task SetMonitoringModeAsync(int id, MonitoringMode mode, CancellationToken ct);

    /// <summary>
    /// Try to fetch the live counters / last value snapshot for a
    /// monitored item.  Returns false when the id is unknown to the
    /// adapter (e.g. after disconnect/rebind, before any notification).
    /// </summary>
    bool TryGetItemStats(int id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MonitoredItemLiveStats? stats);

    IReadOnlyList<MonitoredItemConfig> Items { get; }
}

/// <summary>
/// Helper that builds the standard "default" <see cref="EventFilter"/> the SDK uses for
/// event monitored items (EventId / EventType / SourceName / Time / Message / Severity).
/// </summary>
internal static class DefaultEventFilters
{
    public static EventFilter Build()
    {
        var filter = new EventFilter();
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.SourceName));
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Time));
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Severity));
        return filter;
    }
}
