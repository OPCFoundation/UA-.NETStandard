// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using UaLens.Diagnostics;

namespace UaLens.Plugins.Continuity;

/// <summary>
/// Projects only bounded primitive evidence before returning pooled notifications.
/// State callbacks have no partition argument: that identifier stays unknown.
/// </summary>
internal sealed class ContinuityNotificationHandler : ISubscriptionNotificationHandler
{
    public ContinuityNotificationHandler(
        ContinuityTimeline timeline,
        Guid clientSessionId,
        bool monotonicSample,
        PublishLogObserver? publishLog = null,
        bool continueSampleSeries = false)
    {
        m_timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        m_clientSessionId = clientSessionId;
        m_monotonicSample = monotonicSample;
        m_publishLog = publishLog;
        m_continueSampleSeries = continueSampleSeries;
    }

    public long CreationObservations => Interlocked.Read(ref m_creations);

    public long TransferObservations => Interlocked.Read(ref m_transfers);

    public string StateSummary => Volatile.Read(ref m_stateSummary);

    public DateTime? LastDataUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref m_lastDataTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public DateTime? LastKeepAliveUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref m_lastKeepAliveTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public ValueTask OnDataChangeNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ReadOnlyMemory<DataValueChange> notification,
        PublishState publishStateMask,
        IReadOnlyList<string> stringTable)
    {
        Interlocked.Exchange(ref m_lastDataTicks, DateTime.UtcNow.Ticks);
        long id = DiagnosticCorrelation.Subscription(subscription);
        ReadOnlySpan<DataValueChange> span = notification.Span;
        uint partition = 0;
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly DataValueChange change = ref span[i];
            if (i != 0 && partition != change.PartitionServerId)
            {
                RecordPublish(subscription, partition, sequenceNumber, publishTime, count, PublishLogKind.Data);
                count = 0;
            }
            partition = change.PartitionServerId;
            count++;
            m_timeline.ObserveValue(
                m_clientSessionId, id, partition, sequenceNumber, publishTime,
                change.MonitoredItem?.Name, change.Value, m_monotonicSample,
                subscription.MissingMessageCount, subscription.RepublishMessageCount,
                m_continueSampleSeries, publishStateMask);
        }
        if (count != 0)
        {
            RecordPublish(subscription, partition, sequenceNumber, publishTime, count, PublishLogKind.Data);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnEventDataNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ReadOnlyMemory<EventNotification> notification,
        PublishState publishStateMask,
        IReadOnlyList<string> stringTable)
    {
        if (notification.Length > 0)
        {
            m_timeline.Record(ContinuityEvidenceKind.Error,
                "Unexpected event payload on a value-only lab subscription; event fields were not retained.",
                m_clientSessionId, DiagnosticCorrelation.Subscription(subscription), sequenceNumber: sequenceNumber);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnKeepAliveNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        PublishState publishStateMask)
    {
        Interlocked.Exchange(ref m_lastKeepAliveTicks, DateTime.UtcNow.Ticks);
        m_timeline.Record(ContinuityEvidenceKind.KeepAlive,
            "Keep-alive callback. Partition is not supplied; sequence is not a delivered data message.",
            m_clientSessionId, DiagnosticCorrelation.Subscription(subscription), sequenceNumber: sequenceNumber,
            publishUtc: publishTime == DateTime.MinValue ? null : publishTime,
            publishState: publishStateMask);
        RecordPublish(subscription, 0, sequenceNumber, publishTime, 0, PublishLogKind.KeepAlive);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSubscriptionStateChangedAsync(
        ISubscription subscription,
        SubscriptionState state,
        PublishState publishStateMask,
        CancellationToken ct = default)
    {
        Volatile.Write(ref m_stateSummary,
            state == SubscriptionState.Opened && publishStateMask != PublishState.None
                ? $"Publish flags: {publishStateMask}; lifecycle unspecified."
                : $"{state}; publish flags: {publishStateMask}");
        if (state == SubscriptionState.Created)
        {
            Interlocked.Increment(ref m_creations);
        }
        if ((publishStateMask & PublishState.Transferred) != 0)
        {
            Interlocked.Increment(ref m_transfers);
        }
        m_timeline.ObserveState(
            m_clientSessionId, DiagnosticCorrelation.Subscription(subscription),
            state, publishStateMask, subscription.MissingMessageCount, subscription.RepublishMessageCount);
        return ValueTask.CompletedTask;
    }

    private void RecordPublish(
        ISubscription subscription,
        uint partition,
        uint sequence,
        DateTime publish,
        int count,
        PublishLogKind kind)
    {
        m_publishLog?.RecordClient(subscription, partition, sequence, publish, count, kind, m_clientSessionId);
    }

    private readonly ContinuityTimeline m_timeline;
    private readonly Guid m_clientSessionId;
    private readonly bool m_monotonicSample;
    private readonly bool m_continueSampleSeries;
    private readonly PublishLogObserver? m_publishLog;
    private long m_creations;
    private long m_transfers;
    private long m_lastDataTicks;
    private long m_lastKeepAliveTicks;
    private string m_stateSummary = "Unknown / no state callback observed.";
}
