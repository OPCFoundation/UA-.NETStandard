/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using UaLens.Plugins.SubscriptionBench;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class BenchThroughputCountersTests
{
    [Test]
    public void RotateReportsThePreviousSecondsThroughput()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(100, 0);

        BenchThroughputCounters.Sample sample = counters.Rotate();

        Assert.That(sample.Last1s, Is.EqualTo(100));
        Assert.That(sample.TotalValues, Is.EqualTo(100));
        Assert.That(sample.TotalErrors, Is.Zero);
    }

    [Test]
    public void CumulativeTotalsAccumulateAcrossSeconds()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(10, 1);
        counters.Rotate();
        counters.Record(20, 2);
        counters.Rotate();

        Assert.That(counters.TotalValues, Is.EqualTo(30));
        Assert.That(counters.TotalErrors, Is.EqualTo(3));
    }

    [Test]
    public void AveragesDivideTheWindowedSumByTheWindowLength()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(60, 0);

        BenchThroughputCounters.Sample sample = counters.Rotate();

        Assert.That(sample.Last1s, Is.EqualTo(60));
        Assert.That(sample.Avg10s, Is.EqualTo(6.0).Within(1e-9));
        Assert.That(sample.Avg60s, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void ResetClearsTotalsAndBuckets()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(50, 5);

        counters.Reset();
        BenchThroughputCounters.Sample sample = counters.Rotate();

        Assert.That(counters.TotalValues, Is.Zero);
        Assert.That(counters.TotalErrors, Is.Zero);
        Assert.That(sample.Last1s, Is.Zero);
    }
}
