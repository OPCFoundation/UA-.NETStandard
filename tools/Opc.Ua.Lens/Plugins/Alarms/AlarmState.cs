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
using Opc.Ua;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Bounded condition/branch reconciliation. Only a complete, loss-free refresh
/// can remove an unseen retained branch. The owner serializes access.
/// </summary>
internal sealed class AlarmState
{
    public AlarmState(int conditionCapacity = AlarmLimits.Conditions, int historyCapacity = AlarmLimits.History)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(conditionCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(conditionCapacity, AlarmLimits.Conditions);
        ArgumentOutOfRangeException.ThrowIfLessThan(historyCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(historyCapacity, AlarmLimits.History);
        m_conditionCapacity = conditionCapacity;
        m_historyCapacity = historyCapacity;
    }

    public bool IsRefreshActive { get; private set; }

    public bool IsObserving => m_isObserving;

    public void SetObserving(bool observing, DateTimeOffset now, string detail)
    {
        m_isObserving = observing;
        if (observing)
        {
            m_observationGeneration++;
        }
        Invalidate(now, detail);
        IsRefreshActive = false;
        m_partitions.Clear();
    }

    public bool RequestRefresh(ArrayOf<uint> partitions, DateTimeOffset now)
    {
        CheckDeadline(now);
        if (!m_isObserving || IsRefreshActive)
        {
            return false;
        }
        if (partitions.Count > AlarmLimits.Partitions)
        {
            Invalidate(now, "Too many refresh partitions; no complete snapshot can be claimed.");
            return false;
        }
        m_partitions.Clear();
        foreach (uint partition in partitions)
        {
            m_partitions.TryAdd(partition, new RefreshPartition());
        }
        if (m_partitions.Count == 0)
        {
            m_partitions.Add(0, new RefreshPartition());
        }
        m_refreshCycle++;
        m_refreshInvalid = false;
        IsRefreshActive = true;
        m_refreshState = AlarmRefreshState.Requested;
        m_refreshDetail = "Refresh requested; waiting for RefreshStart and RefreshEnd from every source partition.";
        m_deadline = now.AddSeconds(30);
        MarkStale();
        AddHistory(now, m_refreshDetail);
        return true;
    }

    public void Apply(AlarmUpdate update, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(update);
        CheckDeadline(now);
        switch (update.Kind)
        {
            case AlarmUpdateKind.Condition when update.Condition is { } condition:
                ApplyCondition(condition, update.PartitionId, now);
                break;
            case AlarmUpdateKind.RefreshStart:
                BeginPartition(update.PartitionId, now);
                break;
            case AlarmUpdateKind.RefreshEnd:
                EndPartition(update.PartitionId, now);
                break;
            case AlarmUpdateKind.RefreshRequired:
                Invalidate(now, "RefreshRequired received; the server's retained condition snapshot must be rebuilt.");
                break;
            case AlarmUpdateKind.Loss:
                m_droppedUpdates++;
                Invalidate(now, update.Detail);
                break;
            case AlarmUpdateKind.Created:
            case AlarmUpdateKind.Recovered:
                m_observationGeneration++;
                Invalidate(now, update.Detail);
                IsRefreshActive = false;
                m_partitions.Clear();
                break;
            case AlarmUpdateKind.Unavailable:
                Invalidate(now, update.Detail);
                IsRefreshActive = false;
                m_partitions.Clear();
                break;
        }
    }

    public void ObserveDroppedUpdates(long count, DateTimeOffset now)
    {
        if (count <= m_lastStreamDropped)
        {
            return;
        }
        m_droppedUpdates += count - m_lastStreamDropped;
        m_lastStreamDropped = count;
        Invalidate(now, "The bounded client event queue overflowed; refresh is incomplete. Request a new refresh.");
    }

    public void ResetStreamCounters()
    {
        m_lastStreamDropped = 0;
    }

    public void RefreshFailed(DateTimeOffset now, string detail)
    {
        Invalidate(now, detail);
        IsRefreshActive = false;
        m_partitions.Clear();
    }

    public void AddHistory(DateTimeOffset now, string detail)
    {
        while (m_history.Count >= m_historyCapacity)
        {
            m_history.Dequeue();
        }
        m_history.Enqueue(new AlarmHistoryEntry(now, AlarmLimits.Text(detail)));
        m_revision++;
    }

    public bool TryGetCondition(AlarmKey key, out AlarmRow? row)
    {
        if (m_conditions.TryGetValue(key, out Entry? entry))
        {
            row = entry.Row;
            return true;
        }
        row = null;
        return false;
    }

    public AlarmSnapshot Snapshot(DateTimeOffset now)
    {
        CheckDeadline(now);
        return new AlarmSnapshot(
            [.. m_conditions.Values.Select(static entry => entry.Row)
                .OrderByDescending(static row => row.Condition.Severity)
                .ThenByDescending(static row => row.Revision)],
            [.. m_history.Reverse()],
            m_refreshState,
            m_refreshDetail,
            m_isObserving,
            m_droppedUpdates,
            m_evictedConditions,
            m_receivedEvents,
            m_observationGeneration,
            m_revision);
    }

    private void ApplyCondition(AlarmCondition condition, uint partitionId, DateTimeOffset now)
    {
        m_receivedEvents++;
        bool inRefresh = IsRefreshActive &&
            m_partitions.TryGetValue(partitionId, out RefreshPartition? partition) &&
            partition.Started && !partition.Ended;
        long seenCycle = inRefresh ? m_refreshCycle : 0;
        if (m_conditions.TryGetValue(condition.Key, out Entry? existing))
        {
            if (inRefresh)
            {
                existing.SeenCycle = seenCycle;
            }
            AlarmCondition previous = existing.Row.Condition;
            bool older = inRefresh && existing.Generation == m_observationGeneration &&
                condition.Time.HasValue && previous.Time.HasValue && condition.Time < previous.Time;
            if (condition.EventId == previous.EventId || older)
            {
                existing.Row = existing.Row with { IsStale = false, PartitionId = partitionId };
                existing.Generation = m_observationGeneration;
                m_revision++;
                return;
            }
            m_arrivalOrder.Remove(existing.Order);
            m_arrivalOrder.AddLast(existing.Order);
            existing.Row = new AlarmRow(condition, partitionId, false, ++m_revision);
            existing.SeenCycle = seenCycle;
            existing.Generation = m_observationGeneration;
        }
        else
        {
            if (m_conditions.Count >= m_conditionCapacity && m_arrivalOrder.First is { } oldest)
            {
                LinkedListNode<AlarmKey>? candidate = m_arrivalOrder.First;
                while (candidate is not null)
                {
                    if (!m_conditions[candidate.Value].Row.Condition.Retain)
                    {
                        oldest = candidate;
                        break;
                    }
                    candidate = candidate.Next;
                }
                bool retainedEvicted = m_conditions[oldest.Value].Row.Condition.Retain;
                m_conditions.Remove(oldest.Value);
                m_arrivalOrder.Remove(oldest);
                m_evictedConditions++;
                if (retainedEvicted)
                {
                    Invalidate(now,
                        "The condition limit was reached; retained branches were evicted. The view is incomplete.");
                }
            }
            LinkedListNode<AlarmKey> order = m_arrivalOrder.AddLast(condition.Key);
            m_conditions.Add(condition.Key,
                new Entry(new AlarmRow(condition, partitionId, false, ++m_revision),
                    order, seenCycle, m_observationGeneration));
        }
        AddHistory(now, $"{condition.ConditionName} [{condition.Key.BranchId}] " +
            $"retain={condition.Retain}: {condition.Message}");
    }

    private void BeginPartition(uint partitionId, DateTimeOffset now)
    {
        if (!IsRefreshActive)
        {
            RequestRefresh([partitionId], now);
        }
        if (!m_partitions.TryGetValue(partitionId, out RefreshPartition? partition) || partition.Started)
        {
            Invalidate(now, "Unexpected or duplicate RefreshStart; refresh cannot be reconciled safely.");
            return;
        }
        partition.Started = true;
        if (!m_refreshInvalid)
        {
            m_refreshState = AlarmRefreshState.Receiving;
            m_refreshDetail = "Receiving retained conditions; commands use only freshly observed rows.";
        }
        AddHistory(now, $"RefreshStart from partition {partitionId}.");
    }

    private void EndPartition(uint partitionId, DateTimeOffset now)
    {
        if (!IsRefreshActive || !m_partitions.TryGetValue(partitionId, out RefreshPartition? partition) ||
            !partition.Started || partition.Ended)
        {
            Invalidate(now, "RefreshEnd without a matching RefreshStart; previous branches were preserved.");
            return;
        }
        partition.Ended = true;
        AddHistory(now, $"RefreshEnd from partition {partitionId}.");
        if (m_partitions.Values.Any(static item => !item.Ended))
        {
            return;
        }
        IsRefreshActive = false;
        if (m_refreshInvalid)
        {
            m_refreshState = AlarmRefreshState.Incomplete;
            return;
        }
        AlarmKey[] obsolete = [.. m_conditions.Where(pair =>
            pair.Value.Row.Condition.Retain && pair.Value.SeenCycle != m_refreshCycle).Select(static pair => pair.Key)];
        foreach (AlarmKey key in obsolete)
        {
            m_arrivalOrder.Remove(m_conditions[key].Order);
            m_conditions.Remove(key);
        }
        m_refreshState = AlarmRefreshState.Complete;
        m_refreshDetail = "Complete retained-condition refresh received; unseen retained branches were reconciled.";
        AddHistory(now, m_refreshDetail);
    }

    private void Invalidate(DateTimeOffset now, string detail)
    {
        m_refreshInvalid = true;
        m_refreshState = AlarmRefreshState.Incomplete;
        m_refreshDetail = AlarmLimits.Text(detail);
        MarkStale();
        AddHistory(now, detail);
    }

    private void CheckDeadline(DateTimeOffset now)
    {
        if (IsRefreshActive && now >= m_deadline)
        {
            RefreshFailed(now, "Incomplete refresh: its start/end markers did not arrive within 30 seconds. " +
                "The server may not support or allow ConditionRefresh. Previous branches were preserved.");
        }
    }

    private void MarkStale()
    {
        foreach (Entry entry in m_conditions.Values)
        {
            entry.Row = entry.Row with { IsStale = true };
        }
        m_revision++;
    }

    private readonly int m_conditionCapacity;
    private readonly int m_historyCapacity;
    private readonly Dictionary<AlarmKey, Entry> m_conditions = [];
    private readonly LinkedList<AlarmKey> m_arrivalOrder = [];
    private readonly Queue<AlarmHistoryEntry> m_history = [];
    private readonly Dictionary<uint, RefreshPartition> m_partitions = [];
    private AlarmRefreshState m_refreshState;
    private string m_refreshDetail = "Not observed. Select a source and start observation.";
    private bool m_isObserving;
    private bool m_refreshInvalid;
    private long m_refreshCycle;
    private long m_observationGeneration;
    private long m_revision;
    private long m_lastStreamDropped;
    private long m_droppedUpdates;
    private long m_evictedConditions;
    private long m_receivedEvents;
    private DateTimeOffset m_deadline;

    private sealed class Entry(AlarmRow row, LinkedListNode<AlarmKey> order, long seenCycle, long generation)
    {
        public AlarmRow Row { get; set; } = row;

        public LinkedListNode<AlarmKey> Order { get; } = order;

        public long SeenCycle { get; set; } = seenCycle;

        public long Generation { get; set; } = generation;
    }

    private sealed class RefreshPartition
    {
        public bool Started { get; set; }

        public bool Ended { get; set; }
    }
}
