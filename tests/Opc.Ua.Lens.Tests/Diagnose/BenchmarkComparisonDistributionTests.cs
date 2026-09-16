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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class BenchmarkComparisonDistributionTests
{
    [Test]
    public void BucketBoundariesIncludeZeroAndUseTheExactOverflowThreshold()
    {
        double edge = LatencyHistogram.BucketLowerMs(20);
        var histogram = new LatencyHistogram();
        double[] samples =
        [
            0,
            0.0005,
            Math.BitDecrement(edge),
            edge,
            Math.BitIncrement(edge),
            Math.BitDecrement(LatencyHistogram.MaxMs),
            LatencyHistogram.MaxMs,
            Math.BitIncrement(LatencyHistogram.MaxMs)
        ];
        foreach (double sample in samples)
        {
            histogram.Record(sample);
        }
        BenchmarkDistribution snapshot = histogram.Capture();

        Assert.That(snapshot.BucketCounts.Count, Is.EqualTo(71));
        Assert.That(snapshot.SampleCount, Is.EqualTo(8));
        Assert.That(snapshot.BucketCounts[0], Is.EqualTo(2));
        Assert.That(snapshot.BucketCounts[19], Is.EqualTo(1));
        Assert.That(snapshot.BucketCounts[20], Is.EqualTo(2));
        Assert.That(snapshot.BucketCounts[69], Is.EqualTo(1));
        Assert.That(snapshot.BucketCounts[70], Is.EqualTo(2));
        Assert.That(histogram.Overflow, Is.EqualTo(2));
        Assert.That(LatencyHistogram.BucketLowerMs(0), Is.Zero);
        Assert.That(LatencyHistogram.BucketLowerMs(70), Is.EqualTo(10000));
        Assert.That(LatencyHistogram.BucketUpperMs(70), Is.EqualTo(double.PositiveInfinity));
    }

    [Test]
    public void EveryInteriorEdgeUsesHalfOpenBuckets()
    {
        for (int i = 1; i < LatencyHistogram.BucketCount - 1; i++)
        {
            double edge = LatencyHistogram.BucketLowerMs(i);
            Assert.That(LatencyHistogram.BucketIndex(Math.BitDecrement(edge)), Is.EqualTo(i - 1));
            Assert.That(LatencyHistogram.BucketIndex(edge), Is.EqualTo(i));
            Assert.That(LatencyHistogram.BucketIndex(Math.BitIncrement(edge)), Is.EqualTo(i));
        }
    }

    [Test]
    public void HighVolumeCaptureRetainsFixedCountsRatherThanIndividualSamples()
    {
        var histogram = new LatencyHistogram();
        for (int i = 0; i < 100_000; i++)
        {
            histogram.Record(i % 100);
        }
        BenchmarkDistribution snapshot = histogram.Capture();

        Assert.That(snapshot.BucketCounts.Count, Is.EqualTo(LatencyHistogram.BucketCount));
        Assert.That(snapshot.SampleCount, Is.EqualTo(100_000));
        Assert.That(snapshot.BucketCounts.Span.ToArray().Sum(), Is.EqualTo(100_000));
        Assert.That(snapshot.MeanMs, Is.EqualTo(49.5).Within(1e-9));
        Assert.That(snapshot.MaximumMs, Is.EqualTo(99));
        Assert.That(snapshot.OverflowCount, Is.Zero);
    }

    [Test]
    public void CapturedDistributionIsUnaffectedByLaterRecordsResetsOrInputMutation()
    {
        var histogram = new LatencyHistogram();
        histogram.Record(1);
        BenchmarkDistribution before = histogram.Capture();
        histogram.Record(10000);
        histogram.Reset();

        long[] source = new long[LatencyHistogram.BucketCount];
        source[LatencyHistogram.BucketIndex(1)] = 2;
        var imported = new BenchmarkDistribution(new ArrayOf<long>(source), 1, 1);
        source[LatencyHistogram.BucketIndex(1)] = 999;

        Assert.That(before.SampleCount, Is.EqualTo(1));
        Assert.That(before.MaximumMs, Is.EqualTo(1));
        Assert.That(before.OverflowCount, Is.Zero);
        Assert.That(imported.SampleCount, Is.EqualTo(2));
        Assert.That(imported.BucketCounts[LatencyHistogram.BucketIndex(1)], Is.EqualTo(2));
        Assert.That(histogram.Count, Is.Zero);
        Assert.That(histogram.Overflow, Is.Zero);
        Assert.That(histogram.MaxMsObserved, Is.Zero);
    }

    [Test]
    public async Task ConcurrentRecordingProducesInternallyConsistentSnapshots()
    {
        var histogram = new LatencyHistogram();
        var tasks = new Task[4];
        for (int i = 0; i < tasks.Length; i++)
        {
            double latency = i + 1;
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 1000; j++)
                {
                    histogram.Record(latency);
                    if (j % 100 == 0)
                    {
                        BenchmarkDistribution current = histogram.Capture();
                        Assert.That(current.BucketCounts.Span.ToArray().Sum(), Is.EqualTo(current.SampleCount));
                    }
                }
            });
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        BenchmarkDistribution snapshot = histogram.Capture();

        Assert.That(snapshot.SampleCount, Is.EqualTo(4000));
        Assert.That(snapshot.MeanMs, Is.EqualTo(2.5).Within(1e-10));
        Assert.That(snapshot.MaximumMs, Is.EqualTo(4));
        Assert.That(histogram.Count, Is.EqualTo(snapshot.SampleCount));
    }

    [Test]
    public void EmptyCaptureAndSnapshotBufferHaveExplicitZeroSamples()
    {
        var histogram = new LatencyHistogram();
        long[] buffer = new long[LatencyHistogram.BucketCount + 1];
        buffer[^1] = 99;

        Assert.That(histogram.Snapshot(buffer), Is.Zero);
        Assert.That(buffer[^1], Is.EqualTo(99));
        BenchmarkDistribution snapshot = histogram.Capture();
        Assert.That(snapshot.SampleCount, Is.Zero);
        Assert.That(snapshot.MeanMs, Is.Zero);
        Assert.That(snapshot.GetPercentile(0), Is.Zero);
        Assert.That(snapshot.GetPercentile(0.5), Is.Zero);
        Assert.That(snapshot.GetPercentile(1), Is.Zero);
        Assert.That(() => histogram.Snapshot(new long[LatencyHistogram.BucketCount - 1]),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OverflowPercentilesUseObservedMaximumAndMeanUsesActualLatencies()
    {
        var histogram = new LatencyHistogram();
        histogram.Record(1);
        histogram.Record(2);
        histogram.Record(10000);
        histogram.Record(20000);
        BenchmarkDistribution snapshot = histogram.Capture();

        Assert.That(snapshot.MeanMs, Is.EqualTo(7500.75).Within(1e-9));
        Assert.That(snapshot.MaximumMs, Is.EqualTo(20000));
        Assert.That(snapshot.OverflowCount, Is.EqualTo(2));
        Assert.That(snapshot.GetPercentile(0.99), Is.EqualTo(20000));
        Assert.That(snapshot.GetPercentile(1), Is.EqualTo(20000));
        Assert.That(histogram.GetPercentile(0.99), Is.EqualTo(snapshot.GetPercentile(0.99)));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(-0.001)]
    public void InvalidLatencyIsRejectedWithoutChangingCounts(double sample)
    {
        var histogram = new LatencyHistogram();

        Assert.That(() => histogram.Record(sample), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(histogram.Count, Is.Zero);
        Assert.That(histogram.MaxMsObserved, Is.Zero);
    }

    [TestCase(double.NaN)]
    [TestCase(-0.001)]
    [TestCase(1.001)]
    public void InvalidPercentileIsRejectedEvenForEmptyHistograms(double percentile)
    {
        var histogram = new LatencyHistogram();
        Assert.That(() => histogram.GetPercentile(percentile), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(70)]
    [TestCase(72)]
    public void WrongBucketSchemaSizeIsRejected(int count)
    {
        Assert.That(() => new BenchmarkDistribution(new ArrayOf<long>(new long[count]), 0, 0),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void NegativeOverflowingAndInconsistentBucketDataIsRejected()
    {
        long[] negative = new long[LatencyHistogram.BucketCount];
        negative[0] = -1;
        long[] overflowing = new long[LatencyHistogram.BucketCount];
        overflowing[0] = long.MaxValue;
        overflowing[1] = 1;
        long[] inconsistent = new long[LatencyHistogram.BucketCount];
        inconsistent[0] = 1;

        Assert.That(() => new BenchmarkDistribution(new ArrayOf<long>(negative), 0, 0),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => new BenchmarkDistribution(new ArrayOf<long>(overflowing), 0, 0),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => new BenchmarkDistribution(new ArrayOf<long>(inconsistent), 0, 10000),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => new BenchmarkDistribution(new ArrayOf<long>(inconsistent), double.NaN, 0),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DistributionRowsUseIndependentSampleDenominatorsAndPercentagePointDeltas()
    {
        var baseline = new LatencyHistogram();
        baseline.Record(1);
        baseline.Record(10);
        var selected = new LatencyHistogram();
        selected.Record(1);
        selected.Record(10);
        selected.Record(10);
        selected.Record(10);
        BenchmarkConfiguration configuration = BenchmarkComparisonTestData.Configuration();
        var comparison = new BenchmarkComparison(
            BenchmarkRun.Create(configuration, baseline.Capture(), 0, TimeSpan.FromSeconds(1),
                BenchmarkComparisonTestData.Epoch, BenchmarkCompletion.Completed, string.Empty),
            BenchmarkRun.Create(configuration, selected.Capture(), 1, TimeSpan.FromSeconds(1),
                BenchmarkComparisonTestData.Epoch.AddSeconds(1), BenchmarkCompletion.Completed, string.Empty));
        BenchmarkDistributionRow row = comparison.DistributionRows.Single(
            item => item.BucketIndex == LatencyHistogram.BucketIndex(1));

        Assert.That(comparison.Distribution.Count, Is.EqualTo(2));
        Assert.That(row.BaselineCount, Is.EqualTo(1));
        Assert.That(row.SelectedCount, Is.EqualTo(1));
        Assert.That(row.BaselinePercent, Is.EqualTo(50));
        Assert.That(row.SelectedPercent, Is.EqualTo(25));
        Assert.That(row.PercentagePointDelta, Is.EqualTo(-25));
        Assert.That(row.DeltaText, Is.EqualTo("-25"));
    }

    [Test]
    public void MissingDistributionNeverBecomesZeroCountsOrSyntheticPercentileBuckets()
    {
        var comparison = new BenchmarkComparison(
            BenchmarkComparisonTestData.Captured(), BenchmarkComparisonTestData.Aggregate(1));

        Assert.That(comparison.Distribution.Count, Is.EqualTo(3));
        foreach (BenchmarkDistributionRow row in comparison.Distribution)
        {
            Assert.That(row.BaselineCount, Is.GreaterThan(0));
            Assert.That(row.SelectedCount, Is.Null);
            Assert.That(row.SelectedCountText, Is.EqualTo("Missing"));
            Assert.That(row.SelectedPercent, Is.Null);
            Assert.That(row.PercentagePointDelta, Is.Null);
            Assert.That(row.DeltaText, Is.EqualTo("n/a"));
        }
    }
}
