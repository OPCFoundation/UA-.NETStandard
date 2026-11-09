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
using Opc.Ua;
using Opc.Ua.Client.Subscriptions;

namespace UaLens.Plugins.Continuity;

internal enum ContinuityEvidenceKind
{
    Started,
    Stopped,
    Connection,
    Transport,
    Lifecycle,
    Data,
    KeepAlive,
    SequenceDiscontinuity,
    CounterBoundary,
    Republish,
    Recovered,
    Transferred,
    Recreated,
    Durable,
    SnapshotSaved,
    RestoreRequested,
    Requirement,
    Error,
    CleanupConfirmed,
    CleanupUncertain
}

/// <summary>
/// Callback-processing evidence. A null timestamp or zero partition id means unknown.
/// Receipt is metadata capture inside the handler, not measured network-packet arrival.
/// </summary>
internal sealed record ContinuityEvidence(
    long Ordinal,
    double ElapsedMs,
    DateTime ReceivedUtc,
    ContinuityEvidenceKind Kind,
    Guid ClientSessionId,
    long ClientSubscriptionId,
    uint PartitionServerId,
    uint? SequenceNumber,
    DateTime? PublishUtc,
    DateTime? SourceUtc,
    uint? StatusCode,
    decimal? NumericSample,
    string Detail)
{
    public SubscriptionState? LifecycleState { get; init; }

    public PublishState PublishFlags { get; init; }

    public string TimeText => ElapsedMs.ToString("N0", CultureInfo.InvariantCulture) + " ms";

    public string CorrelationText => string.Format(
        CultureInfo.InvariantCulture,
        "client:{0} / partition:{1} / seq:{2}",
        ClientSubscriptionId,
        PartitionServerId == 0 ? "unknown" : PartitionServerId.ToString(CultureInfo.InvariantCulture),
        SequenceNumber?.ToString(CultureInfo.InvariantCulture) ?? "unknown");

    public string TimingText => string.Format(
        CultureInfo.InvariantCulture,
        "source {0} · publish {1} · callback {2:HH:mm:ss.fff} UTC",
        SourceUtc?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "unknown",
        PublishUtc?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "unknown",
        ReceivedUtc);

    public string StatusText => StatusCode is uint code
        ? new StatusCode(code).ToString()
        : "status not supplied";
}

internal sealed record ContinuityCounters(
    long Values,
    long BadValues,
    long UnknownPartitionValues,
    long SequenceDiscontinuities,
    long MissingMessages,
    long RepublishAttempts,
    long RecoveryObservations,
    long TransferObservations,
    long CreationObservations,
    long MonotonicDiscontinuities,
    long NonNumericSamples,
    long QueueOverflowObservations,
    long CounterBoundaryObservations,
    long EvictedEvidence,
    long UntrackedStreams);

internal sealed record ContinuityTimelineSnapshot(
    ArrayOf<ContinuityEvidence> Entries,
    ContinuityCounters Counters);

/// <summary>
/// Bounded reducer shared by the V2 handlers and desktop. Aggregate counters survive
/// timeline eviction; partition-local sequences never get mixed into one stream.
/// </summary>
internal sealed class ContinuityTimeline
{
    public ContinuityTimeline(TimeProvider? timeProvider = null, int capacity = 500)
    {
        if (capacity is < 1 or > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        m_time = timeProvider ?? TimeProvider.System;
        m_capacity = capacity;
        m_started = m_time.GetTimestamp();
    }

    public void Record(
        ContinuityEvidenceKind kind,
        string detail,
        Guid clientSessionId = default,
        long clientSubscriptionId = 0,
        uint partitionServerId = 0,
        uint? sequenceNumber = null,
        DateTime? publishUtc = null,
        DateTime? sourceUtc = null,
        uint? statusCode = null,
        decimal? numericSample = null,
        PublishState publishState = PublishState.None)
    {
        ArgumentNullException.ThrowIfNull(detail);
        lock (m_gate)
        {
            Append(kind, detail, clientSessionId, clientSubscriptionId, partitionServerId,
                sequenceNumber, publishUtc, sourceUtc, statusCode, numericSample, publishState: publishState);
        }
    }

    public void ObserveState(
        Guid sessionId,
        long subscriptionId,
        SubscriptionState state,
        PublishState publishState,
        long missing,
        long republish)
    {
        lock (m_gate)
        {
            UpdateStackCounters(sessionId, subscriptionId, missing, republish);
            if (state == SubscriptionState.Created)
            {
                m_creations++;
                ResetBaselines(subscriptionId);
            }
            string description = state == SubscriptionState.Opened && publishState != PublishState.None
                ? $"Publish flags: {publishState}; no separate lifecycle transition supplied."
                : $"{state}; publish flags: {publishState}";
            if (state == SubscriptionState.Created)
            {
                description += "; sequence baselines reset because the callback does not identify a partition.";
            }
            Append(ContinuityEvidenceKind.Lifecycle, description,
                sessionId, subscriptionId,
                lifecycle: state == SubscriptionState.Opened && publishState != PublishState.None ? null : state,
                publishState: publishState);
            if ((publishState & PublishState.Republish) != 0)
            {
                Append(ContinuityEvidenceKind.Republish,
                    "V2 issued a republish attempt; success is not implied.", sessionId, subscriptionId);
            }
            if ((publishState & PublishState.Recovered) != 0)
            {
                m_recoveries++;
                Append(ContinuityEvidenceKind.Recovered,
                    "V2 reported publishing recovery; this is not proof of zero loss.", sessionId, subscriptionId);
            }
            if ((publishState & PublishState.Transferred) != 0)
            {
                m_transfers++;
                Append(ContinuityEvidenceKind.Transferred,
                    "V2 reported a transfer transition.", sessionId, subscriptionId);
            }
        }
    }

    public void ObserveValue(
        Guid sessionId,
        long subscriptionId,
        uint partitionId,
        uint sequence,
        DateTime publishUtc,
        string? itemKey,
        in DataValue value,
        bool monotonicSample,
        long missing,
        long republish,
        bool continueSampleSeries = false,
        PublishState publishState = PublishState.None)
    {
        lock (m_gate)
        {
            m_values++;
            bool good = StatusCode.IsGood(value.StatusCode);
            if (!good)
            {
                m_badValues++;
            }
            if (value.StatusCode.Overflow)
            {
                m_overflows++;
            }
            if (partitionId == 0)
            {
                m_unknownPartitions++;
            }
            else
            {
                ObserveSequence(sessionId, subscriptionId, partitionId, sequence);
            }
            UpdateStackCounters(sessionId, subscriptionId, missing, republish);
            decimal? sample = GetNumericSample(value.WrappedValue);
            if (monotonicSample && good)
            {
                if (!string.IsNullOrEmpty(itemKey))
                {
                    // Zero is a private run-owned series key, not a server or
                    // client subscription id. It spans an explicit handover.
                    ObserveSample(continueSampleSeries ? 0 : subscriptionId, itemKey, sample);
                }
                else
                {
                    m_untracked++;
                }
            }
            DateTime? source = value.SourceTimestamp.IsNull ? null : (DateTime)value.SourceTimestamp;
            Append(ContinuityEvidenceKind.Data,
                value.StatusCode.Overflow
                    ? "Server queue overflow bit reported; queued values were discarded."
                    : good ? "Value callback observed." : "Non-good value status observed.",
                sessionId, subscriptionId, partitionId, sequence,
                publishUtc == DateTime.MinValue ? null : publishUtc, source,
                value.StatusCode.Code, sample, publishState: publishState);
        }
    }

    public ContinuityTimelineSnapshot Snapshot()
    {
        lock (m_gate)
        {
            return new ContinuityTimelineSnapshot(
                new ArrayOf<ContinuityEvidence>(m_entries.ToArray()),
                new ContinuityCounters(
                    m_values, m_badValues, m_unknownPartitions, m_sequenceDiscontinuities,
                    m_missing, m_republish, m_recoveries, m_transfers, m_creations,
                    m_monotonicDiscontinuities, m_nonNumeric, m_overflows, m_counterBoundaries, m_evicted, m_untracked));
        }
    }

    private void ResetBaselines(long subscriptionId)
    {
        var sequences = new List<(long, uint)>();
        foreach ((long, uint) key in m_sequences.Keys)
        {
            if (key.Item1 == subscriptionId)
            {
                sequences.Add(key);
            }
        }
        foreach ((long, uint) key in sequences)
        {
            m_sequences.Remove(key);
        }
        var samples = new List<(long, string)>();
        foreach ((long, string) key in m_samples.Keys)
        {
            if (key.Item1 == subscriptionId)
            {
                samples.Add(key);
            }
        }
        foreach ((long, string) key in samples)
        {
            m_samples.Remove(key);
        }
    }

    private void ObserveSequence(Guid sessionId, long subscriptionId, uint partitionId, uint sequence)
    {
        if (sequence == 0)
        {
            return;
        }
        var key = (subscriptionId, partitionId);
        if (m_sequences.TryGetValue(key, out uint previous))
        {
            if (sequence == previous)
            {
                return;
            }
            uint expected = previous == uint.MaxValue ? 1 : previous + 1;
            uint distance = unchecked(sequence - expected);
            if (distance != 0)
            {
                m_sequenceDiscontinuities++;
                Append(ContinuityEvidenceKind.SequenceDiscontinuity,
                    "Non-consecutive partition sequence observed; gap, replay or recreation requires stack evidence.",
                    sessionId, subscriptionId, partitionId, sequence);
                if (distance > int.MaxValue)
                {
                    return;
                }
            }
        }
        else if (m_sequences.Count == MaxStreams)
        {
            m_untracked++;
            return;
        }
        m_sequences[key] = sequence;
    }

    private void ObserveSample(long subscriptionId, string itemKey, decimal? sample)
    {
        if (sample is not decimal numeric || decimal.Truncate(numeric) != numeric)
        {
            m_nonNumeric++;
            return;
        }
        var key = (subscriptionId, itemKey);
        if (m_samples.TryGetValue(key, out decimal previous))
        {
            if (numeric != previous && numeric - previous != 1)
            {
                m_monotonicDiscontinuities++;
            }
        }
        else if (m_samples.Count == MaxStreams)
        {
            m_untracked++;
            return;
        }
        m_samples[key] = numeric;
    }

    private void UpdateStackCounters(Guid sessionId, long id, long missing, long republish)
    {
        if (!m_stackCounters.TryGetValue(id, out (long Missing, long Republish) previous) &&
            m_stackCounters.Count == MaxStreams)
        {
            m_untracked++;
            return;
        }
        if (missing < previous.Missing || republish < previous.Republish)
        {
            m_counterBoundaries++;
            Append(ContinuityEvidenceKind.CounterBoundary,
                "V2 counters decreased (reset or partition removal). Counts across this boundary are uncertain.",
                sessionId, id);
        }
        m_missing += Math.Max(0, missing - previous.Missing);
        m_republish += Math.Max(0, republish - previous.Republish);
        m_stackCounters[id] = (Math.Max(0, missing), Math.Max(0, republish));
    }

    private void Append(
        ContinuityEvidenceKind kind,
        string detail,
        Guid sessionId,
        long subscriptionId,
        uint partitionId = 0,
        uint? sequence = null,
        DateTime? publishUtc = null,
        DateTime? sourceUtc = null,
        uint? statusCode = null,
        decimal? sample = null,
        SubscriptionState? lifecycle = null,
        PublishState publishState = PublishState.None)
    {
        if (m_entries.Count == m_capacity)
        {
            m_entries.Dequeue();
            m_evicted++;
        }
        m_entries.Enqueue(new ContinuityEvidence(
            ++m_ordinal,
            m_time.GetElapsedTime(m_started).TotalMilliseconds,
            m_time.GetUtcNow().UtcDateTime,
            kind, sessionId, subscriptionId, partitionId, sequence == 0 ? null : sequence,
            publishUtc, sourceUtc, statusCode, sample,
            detail.Length > 512 ? detail[..512] : detail)
        {
            LifecycleState = lifecycle,
            PublishFlags = publishState
        });
    }

    private static decimal? GetNumericSample(Variant value)
    {
        if (value.TryGetValue(out long signed))
        {
            return signed;
        }
        if (value.TryGetValue(out ulong unsigned))
        {
            return unsigned;
        }
        if (value.TryGetValue(out int integer))
        {
            return integer;
        }
        if (value.TryGetValue(out uint natural))
        {
            return natural;
        }
        if (value.TryGetValue(out short small))
        {
            return small;
        }
        if (value.TryGetValue(out ushort smallNatural))
        {
            return smallNatural;
        }
        if (value.TryGetValue(out byte octet))
        {
            return octet;
        }
        if (value.TryGetValue(out sbyte smallSigned))
        {
            return smallSigned;
        }
        return null;
    }

    private const int MaxStreams = 256;
    private readonly System.Threading.Lock m_gate = new();
    private readonly TimeProvider m_time;
    private readonly int m_capacity;
    private readonly long m_started;
    private readonly Queue<ContinuityEvidence> m_entries = new();
    private readonly Dictionary<(long, uint), uint> m_sequences = new();
    private readonly Dictionary<(long, string), decimal> m_samples = new();
    private readonly Dictionary<long, (long Missing, long Republish)> m_stackCounters = new();
    private long m_ordinal;
    private long m_values;
    private long m_badValues;
    private long m_unknownPartitions;
    private long m_sequenceDiscontinuities;
    private long m_missing;
    private long m_republish;
    private long m_recoveries;
    private long m_transfers;
    private long m_creations;
    private long m_monotonicDiscontinuities;
    private long m_nonNumeric;
    private long m_overflows;
    private long m_counterBoundaries;
    private long m_evicted;
    private long m_untracked;
}
