/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * See http://opcfoundation.org/License/MIT/1.00/ for the complete license.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Recycling.OperatorConsole;

namespace Opc.Ua.Recycling.Operator.Tests.Backend;

/// <summary>
/// Deterministic feed projection, retention and resource-bound regressions.
/// </summary>
[TestFixture]
public sealed class ConsoleFeedTests
{
    /// <summary>
    /// The initial disconnected projection never claims zero production observations.
    /// </summary>
    [Test]
    public void InitialStateHasNoFabricatedTotals()
    {
        using ConsoleFeed feed = Create();
        Assert.That(feed.State.Available, Is.False);
        Assert.That(feed.State.Summary.InputMg, Is.Null);
        Assert.That(feed.State.Summary.WipMg, Is.Null);
        Assert.That(feed.State.Summary.PackageCount, Is.Null);
        Assert.That(feed.State.SourceUtc, Is.Null);
    }

    /// <summary>
    /// Disposed old records cannot crowd every outstanding hold out of the live preview.
    /// </summary>
    [Test]
    public void LiveHoldPreviewPrioritizesOutstandingRecordsBeyondTheOldestTwoHundred()
    {
        using ConsoleFeed feed = Create();
        var holds = new QualityHold[205];
        for (int index = 0; index < holds.Length; index++)
        {
            holds[index] = new QualityHold(index + 1, 1, 5, "Color limit",
                index < 200 ? HoldDisposition.Released : HoldDisposition.Held, string.Empty, 0);
        }
        feed.Accept(Good(Image(1, 1) with { Holds = holds }));
        Assert.That(feed.State.Holds, Has.Length.EqualTo(5));
        Assert.That(feed.State.Holds[0].Id, Is.EqualTo("205"));
        Assert.That(feed.State.Holds[^1].Id, Is.EqualTo("201"));
        Assert.That(feed.State.Summary.HeldCount, Is.EqualTo(5));
    }

    /// <summary>
    /// Sixteen latest-only channels coalesce slow clients and release their consumer slots on disconnect.
    /// </summary>
    [Test]
    public void ConsumersAreLimitedToSixteenCoalesceLatestAndReleaseSlots()
    {
        using ConsoleFeed feed = Create();
        var consumers = new List<Channel<ConsoleState>>();
        for (int index = 0; index < 16; index++)
        {
            consumers.Add(feed.Subscribe()!);
        }
        Assert.That(feed.Subscribe(), Is.Null);
        feed.Accept(Good(Image(1, 1)));
        feed.Accept(Good(Image(2, 2)));
        foreach (Channel<ConsoleState> consumer in consumers)
        {
            Assert.That(consumer.Reader.TryRead(out ConsoleState? latest), Is.True);
            Assert.That(latest!.Tick, Is.EqualTo("2"));
            Assert.That(consumer.Reader.TryRead(out _), Is.False);
        }
        feed.Unsubscribe(consumers[0]);
        Channel<ConsoleState>? replacement = feed.Subscribe();
        Assert.That(replacement, Is.Not.Null);
        Assert.That(feed.Subscribe(), Is.Null);
        feed.Unsubscribe(replacement!);
        foreach (Channel<ConsoleState> consumer in consumers)
        {
            feed.Unsubscribe(consumer);
        }
    }

    /// <summary>
    /// An uncertain payload cannot overwrite the last good image and subsequent observations show a gap.
    /// </summary>
    [Test]
    public void UncertainRetainsLastGoodAndMarksTheNextMaterialObservationGap()
    {
        using ConsoleFeed feed = Create();
        CommittedSnapshot original = Image(9007199254740993, 1);
        feed.Accept(Good(original));
        feed.Accept(new CoordinatorNotification(Image(9007199254740994, 99), false, "uncertain")
        {
            StatusCode = StatusCodes.Uncertain
        });
        Assert.That(feed.Snapshot, Is.SameAs(original));
        Assert.That(feed.State.Available, Is.False);
        Assert.That(feed.State.Quality, Is.EqualTo(StatusCodes.Uncertain.ToString()));
        Assert.That(feed.State.Revision, Is.EqualTo("9007199254740993"));
        Assert.That(feed.State.Summary.InputMg, Is.EqualTo("9007199254740993"));
        feed.Accept(Good(Image(9007199254740995, 2)));
        Assert.That(feed.State.Trends[^1].Gap, Is.True);
        Assert.That(feed.State.Available, Is.True);
    }

    /// <summary>
    /// Administrative commits do not invent trend samples; material gaps and resets remain explicit.
    /// </summary>
    [Test]
    public void HeartbeatDoesNotSampleAndResetClearsOldRunWithAnExplicitGap()
    {
        using ConsoleFeed feed = Create();
        feed.Accept(Good(Image(1, 3)));
        feed.Accept(Good(Image(2, 3)));
        Assert.That(feed.State.Trends, Has.Length.EqualTo(1));
        feed.Accept(Good(Image(3, 5)));
        Assert.That(feed.State.Trends[^1].Gap, Is.True);
        feed.Accept(Good(Image(4, 0) with { RunId = "new-run", ConfigurationId = "new-config" }));
        Assert.That(feed.State.Trends, Has.Length.EqualTo(1));
        Assert.That(feed.State.Trends[0].RunId, Is.EqualTo("new-run"));
        Assert.That(feed.State.Trends[0].Gap, Is.True);
        Assert.That(feed.State.Trends[0].Tick, Is.EqualTo("0"));
    }

    /// <summary>
    /// A configuration change cannot be silently merged into the same run.
    /// </summary>
    [Test]
    public void ConfigurationMismatchRetainsTheOldRunAndRejectsTheObservation()
    {
        using ConsoleFeed feed = Create();
        CommittedSnapshot original = Image(1, 1);
        feed.Accept(Good(original));
        feed.Accept(Good(Image(2, 2) with { ConfigurationId = "incompatible" }));
        Assert.That(feed.Snapshot, Is.SameAs(original));
        Assert.That(feed.State.Available, Is.False);
        Assert.That(feed.State.Quality, Is.EqualTo("BadConfigurationError"));
        Assert.That(feed.State.Trends, Has.Length.EqualTo(1));
    }

    /// <summary>
    /// Trend storage remains bounded independently of coordinator history.
    /// </summary>
    [Test]
    public void TrendsRetainOnlyThreeHundredSixtyLogicalObservations()
    {
        using ConsoleFeed feed = Create();
        for (int index = 0; index < 362; index++)
        {
            feed.Accept(Good(Image(index, index)));
        }
        Assert.That(feed.State.Trends, Has.Length.EqualTo(360));
        Assert.That(feed.State.Trends[0].Tick, Is.EqualTo("2"));
        Assert.That(feed.State.Trends[^1].Tick, Is.EqualTo("361"));
    }

    /// <summary>
    /// A stale initialized bit cannot hide expired leases or unavailable process equipment.
    /// </summary>
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    public void MembershipRequiresAnUnexpiredLeaseAndAvailableStages(
        bool expired, bool unavailable, bool expected)
    {
        var clock = new TestClock();
        using ConsoleFeed feed = Create(clock);
        CommittedSnapshot snapshot = Image(1, 1) with
        {
            Participants =
            [
                new ProcessParticipant(RecyclingProcess.Sorting, "process", "session", true,
                    clock.GetUtcNow().UtcDateTime.AddSeconds(expired ? -1 : 1), 1)
            ],
            Stages =
            [
                new StageSnapshot
                {
                    Process = RecyclingProcess.Sorting,
                    State = unavailable ? EquipmentState.Unavailable : EquipmentState.Idle
                }
            ]
        };
        Assert.That(feed.ProjectProcess(snapshot, RecyclingProcess.Sorting).Initialized, Is.EqualTo(expected));
    }

    /// <summary>
    /// Creates a feed without starting a transport or introducing a runtime fake-data mode.
    /// </summary>
    private static ConsoleFeed Create(TimeProvider? clock = null) => new(
        _ => throw new InvalidOperationException("Transport must not start in projection tests."),
        new ConsoleOptions { LineId = "line" },
        Mock.Of<ITelemetryContext>(context => context.LoggerFactory == NullLoggerFactory.Instance), clock);

    /// <summary>
    /// Wraps a good authoritative observation.
    /// </summary>
    private static CoordinatorNotification Good(CommittedSnapshot image) => new(image, true, string.Empty);

    /// <summary>
    /// Creates a minimal immutable committed image with a JavaScript-unsafe exact mass.
    /// </summary>
    private static CommittedSnapshot Image(long revision, long tick) => new()
    {
        RunId = "run",
        ConfigurationId = "config",
        Revision = revision,
        Tick = tick,
        LogicalSeconds = tick,
        SourceTimestampUtc = DateTime.UnixEpoch.AddSeconds(tick),
        SourceMass = new MaterialMass(9007199254740993, 0, 0, 0, 0, 0),
        Stages = [new StageSnapshot { Process = RecyclingProcess.Sorting }]
    };

    /// <summary>
    /// Deterministic injectable wall clock.
    /// </summary>
    internal sealed class TestClock : TimeProvider
    {
        /// <summary>
        /// Reads the controlled UTC wall clock.
        /// </summary>
        public override DateTimeOffset GetUtcNow() => Now;

        /// <summary>
        /// Current simulated wall time, unrelated to material ticks.
        /// </summary>
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
