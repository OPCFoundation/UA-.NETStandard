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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.Subscriptions;
using UaLens.Diagnostics;
using UaLens.Plugins.Continuity;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class ContinuityTimelineTests
{
    [Test]
    public void PartitionAndLogicalSubscriptionSequencesAreNeverMixed()
    {
        var timeline = new ContinuityTimeline();
        Observe(timeline, 1, 10, 1);
        Observe(timeline, 1, 20, 80);
        Observe(timeline, 2, 10, 900);
        Observe(timeline, 1, 10, 2);
        Observe(timeline, 1, 20, 81);
        Observe(timeline, 2, 10, 901);

        ContinuityTimelineSnapshot snapshot = timeline.Snapshot();
        Assert.That(snapshot.Counters.Values, Is.EqualTo(6));
        Assert.That(snapshot.Counters.SequenceDiscontinuities, Is.Zero);
        Assert.That(snapshot.Entries.ToList().Select(row => row.PartitionServerId),
            Is.EquivalentTo(new uint[] { 10, 20, 10, 10, 20, 10 }));
    }

    [Test]
    public void UnknownPartitionsAndKeepAlivesDoNotInventSequenceLoss()
    {
        var timeline = new ContinuityTimeline();
        Observe(timeline, 1, 0, 1);
        Observe(timeline, 1, 0, 500);
        timeline.Record(ContinuityEvidenceKind.KeepAlive, "Keep alive", sequenceNumber: 1000);
        Observe(timeline, 1, 10, 1);
        Observe(timeline, 1, 10, 2);

        ContinuityCounters counters = timeline.Snapshot().Counters;
        Assert.That(counters.UnknownPartitionValues, Is.EqualTo(2));
        Assert.That(counters.SequenceDiscontinuities, Is.Zero);
        Assert.That(counters.MissingMessages, Is.Zero);
    }

    [Test]
    public void SequenceWrapReplayAndRealStackCountersRemainDistinct()
    {
        var timeline = new ContinuityTimeline();
        Observe(timeline, 1, 10, uint.MaxValue);
        Observe(timeline, 1, 10, 1);
        Observe(timeline, 1, 10, 3);
        Observe(timeline, 1, 10, 2);
        Observe(timeline, 1, 10, 4);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Modified, PublishState.Republish, 5, 3);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Modified, PublishState.None, 5, 3);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Modified, PublishState.Recovered, 5, 3);

        ContinuityCounters counters = timeline.Snapshot().Counters;
        Assert.That(counters.SequenceDiscontinuities, Is.EqualTo(2));
        Assert.That(counters.MissingMessages, Is.EqualTo(5), "Count cumulative V2 slots once, not per callback.");
        Assert.That(counters.RepublishAttempts, Is.EqualTo(3));
        Assert.That(counters.RecoveryObservations, Is.EqualTo(1));
        Assert.That(counters.TransferObservations, Is.Zero);
    }

    [Test]
    public void CounterDecreasesPreserveObservedIncreasesAndExposeTheUncertainBoundary()
    {
        var timeline = new ContinuityTimeline();
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Modified, PublishState.None, 8, 7);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Created, PublishState.None, 1, 1);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Modified, PublishState.None, 2, 3);

        Assert.That(timeline.Snapshot().Counters.MissingMessages, Is.EqualTo(9));
        Assert.That(timeline.Snapshot().Counters.RepublishAttempts, Is.EqualTo(9));
        Assert.That(timeline.Snapshot().Counters.CounterBoundaryObservations, Is.EqualTo(1));
    }

    [Test]
    public void CreationEvidenceResetsSequenceBaselinesInsteadOfMisclassifyingEveryNewMessageAsReplay()
    {
        var timeline = new ContinuityTimeline();
        Observe(timeline, 1, 10, 900);
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Created, PublishState.None, 0, 0);
        Observe(timeline, 1, 10, 1);
        Observe(timeline, 1, 10, 2);

        Assert.That(timeline.Snapshot().Counters.SequenceDiscontinuities, Is.Zero);
        Assert.That(timeline.Snapshot().Counters.CreationObservations, Is.EqualTo(1));
    }

    [Test]
    public void QueueOverflowAndUnidentifiedSamplesAreExplicitRatherThanInferredAsTransportLoss()
    {
        var timeline = new ContinuityTimeline();
        var overflow = new DataValue(Variant.From(100), StatusCodes.Good.SetOverflow(true));
        timeline.ObserveValue(Guid.Empty, 1, 10, 1, s_time, null, overflow, true, 0, 0);

        ContinuityTimelineSnapshot snapshot = timeline.Snapshot();
        Assert.That(snapshot.Counters.QueueOverflowObservations, Is.EqualTo(1));
        Assert.That(snapshot.Counters.UntrackedStreams, Is.EqualTo(1));
        Assert.That(snapshot.Counters.MonotonicDiscontinuities, Is.Zero);
        Assert.That(snapshot.Counters.MissingMessages, Is.Zero);
        Assert.That(snapshot.Entries[0].Detail, Does.Contain("Server queue overflow"));
    }

    [Test]
    public void PublishOnlyStateCallbackDoesNotInventAnotherSubscriptionOpenedTransition()
    {
        var timeline = new ContinuityTimeline();
        timeline.ObserveState(Guid.Empty, 1, SubscriptionState.Opened, PublishState.Republish, 1, 1);

        Assert.That(timeline.Snapshot().Entries[0].Detail, Does.Contain("no separate lifecycle"));
        Assert.That(timeline.Snapshot().Entries[0].Detail, Does.Not.Contain("Opened"));
        Assert.That(timeline.Snapshot().Counters.RepublishAttempts, Is.EqualTo(1));
    }

    [Test]
    public void NumericSampleContractIsOptInAndDoesNotTreatBadQualityAsMissingSamples()
    {
        var timeline = new ContinuityTimeline();
        Sample(timeline, 10, StatusCodes.Good);
        Sample(timeline, 11, StatusCodes.Good);
        Sample(timeline, 14, StatusCodes.Good);
        Sample(timeline, 1000, StatusCodes.BadNoCommunication);
        Sample(timeline, 15, StatusCodes.Good);
        var textValue = new DataValue(Variant.From("not-a-counter"));
        timeline.ObserveValue(Guid.Empty, 1, 1, 0, s_time, "counter", textValue, true, 0, 0);

        ContinuityCounters counters = timeline.Snapshot().Counters;
        Assert.That(counters.MonotonicDiscontinuities, Is.EqualTo(1));
        Assert.That(counters.NonNumericSamples, Is.EqualTo(1));
        Assert.That(counters.BadValues, Is.EqualTo(1));
        Assert.That(counters.MissingMessages, Is.Zero);
    }

    [Test]
    public void ExplicitRunSampleSeriesContinuesAcrossSubscriptionHandoverWithoutMergingPublishSequences()
    {
        var timeline = new ContinuityTimeline();
        var first = new DataValue(Variant.From(10));
        var second = new DataValue(Variant.From(15));
        timeline.ObserveValue(Guid.Empty, 1, 10, 900, s_time, "sample-1",
            first, true, 0, 0, continueSampleSeries: true);
        timeline.ObserveState(Guid.Empty, 2, SubscriptionState.Created, PublishState.None, 0, 0);
        timeline.ObserveValue(Guid.Empty, 2, 20, 1, s_time, "sample-1",
            second, true, 0, 0, continueSampleSeries: true);

        Assert.That(timeline.Snapshot().Counters.MonotonicDiscontinuities, Is.EqualTo(1));
        Assert.That(timeline.Snapshot().Counters.SequenceDiscontinuities, Is.Zero);
    }

    [Test]
    public void BoundedEvictionPreservesTotalsAndMonotonicOrderWhenWallClockMovesBackwards()
    {
        var time = new TimelineTimeProvider();
        var timeline = new ContinuityTimeline(time, capacity: 3);
        for (uint i = 1; i <= 20; i++)
        {
            Observe(timeline, 1, 10, i);
            time.Advance();
        }

        ContinuityTimelineSnapshot snapshot = timeline.Snapshot();
        Assert.That(snapshot.Entries.Count, Is.EqualTo(3));
        Assert.That(snapshot.Counters.Values, Is.EqualTo(20));
        Assert.That(snapshot.Counters.EvictedEvidence, Is.EqualTo(17));
        Assert.That(snapshot.Entries[0].Ordinal, Is.EqualTo(18));
        Assert.That(snapshot.Entries[2].Ordinal, Is.EqualTo(20));
        Assert.That(snapshot.Entries[2].ElapsedMs, Is.GreaterThan(snapshot.Entries[0].ElapsedMs));
        Assert.That(snapshot.Entries[2].ReceivedUtc, Is.LessThan(snapshot.Entries[0].ReceivedUtc));
    }

    [Test]
    public async Task RealV2HandlerSharesClientCorrelationAndPreservesActualPartitionIds()
    {
        var timeline = new ContinuityTimeline();
        var publish = new PublishLogObserver(action => action());
        var subscription = new Mock<ISubscription>();
        Guid sessionId = Guid.NewGuid();
        var handler = new ContinuityNotificationHandler(timeline, sessionId, false, publish);
        var changes = new[]
        {
            new DataValueChange(null, new DataValue(Variant.From(1)), null) { PartitionServerId = 42 },
            new DataValueChange(null, new DataValue(Variant.From(2)), null) { PartitionServerId = 43 }
        };

        await handler.OnDataChangeNotificationAsync(
            subscription.Object, 7, s_time, changes, PublishState.None, Array.Empty<string>()).ConfigureAwait(false);
        await handler.OnKeepAliveNotificationAsync(
            subscription.Object, 8, s_time, PublishState.KeepAlive).ConfigureAwait(false);
        await handler.OnSubscriptionStateChangedAsync(
            subscription.Object, SubscriptionState.Modified,
            PublishState.Transferred | PublishState.Recovered, CancellationToken.None).ConfigureAwait(false);

        ContinuityTimelineSnapshot snapshot = timeline.Snapshot();
        Assert.That(publish.Entries[0].SubscriptionId, Is.EqualTo(42));
        Assert.That(publish.Entries[1].SubscriptionId, Is.EqualTo(43));
        Assert.That(publish.Entries[2].SubscriptionId, Is.Zero);
        Assert.That(publish.Entries[0].ClientSubscriptionId, Is.EqualTo(snapshot.Entries[0].ClientSubscriptionId));
        Assert.That(publish.Entries[0].ClientSessionId, Is.EqualTo(sessionId));
        Assert.That(snapshot.Entries.ToList().Where(row => row.Kind != ContinuityEvidenceKind.Data)
            .All(row => row.PartitionServerId == 0), Is.True);
        Assert.That(snapshot.Counters.TransferObservations, Is.EqualTo(1));
        Assert.That(snapshot.Counters.RecoveryObservations, Is.EqualTo(1));
        Assert.That(snapshot.Counters.MissingMessages, Is.Zero);
    }

    private static void Observe(ContinuityTimeline timeline, long subscription, uint partition, uint sequence)
    {
        var value = new DataValue(Variant.From(1));
        timeline.ObserveValue(Guid.Empty, subscription, partition, sequence, s_time, "value", value, false, 0, 0);
    }

    private static void Sample(ContinuityTimeline timeline, int number, StatusCode status)
    {
        var value = new DataValue(Variant.From(number), status);
        timeline.ObserveValue(Guid.Empty, 1, 1, 0, s_time, "counter", value, true, 0, 0);
    }

    private sealed class TimelineTimeProvider : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => m_ticks;

        public override DateTimeOffset GetUtcNow() => new(s_time.AddTicks(-m_ticks));

        public void Advance() => m_ticks += TimeSpan.TicksPerSecond;

        private long m_ticks;
    }

    private static readonly DateTime s_time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
