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
 * WHETHER IN AN ACTION OF CONTRACT, TORT OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class BenchmarkComparisonTests
{
    [Test]
    public void EveryMetricUsesSelectedMinusChosenBaseline()
    {
        BenchmarkRun baseline = BenchmarkComparisonTestData.Aggregate();
        BenchmarkRun selected = baseline with
        {
            Id = Guid.NewGuid(),
            AchievedRate = 125,
            TotalOps = 250,
            ErrorCount = 1,
            MeanLatencyMs = 1.5,
            P50Ms = 0.75,
            P90Ms = 4,
            P99Ms = 12
        };

        var comparison = new BenchmarkComparison(baseline, selected);

        Assert.That(comparison.AchievedRate.AbsoluteDelta, Is.EqualTo(25));
        Assert.That(comparison.AchievedRate.RelativePercent, Is.EqualTo(25));
        Assert.That(comparison.TotalOps.AbsoluteDelta, Is.EqualTo(50));
        Assert.That(comparison.TotalOps.RelativePercent, Is.EqualTo(25));
        Assert.That(comparison.Errors.AbsoluteDelta, Is.EqualTo(-1));
        Assert.That(comparison.Errors.RelativePercent, Is.EqualTo(-50));
        Assert.That(comparison.MeanLatency.AbsoluteDelta, Is.EqualTo(-0.5));
        Assert.That(comparison.MeanLatency.RelativePercent, Is.EqualTo(-25));
        Assert.That(comparison.P50.AbsoluteDelta, Is.EqualTo(-0.25));
        Assert.That(comparison.P50.RelativePercent, Is.EqualTo(-25));
        Assert.That(comparison.P90.AbsoluteDelta, Is.EqualTo(-1));
        Assert.That(comparison.P90.RelativePercent, Is.EqualTo(-20));
        Assert.That(comparison.P99.AbsoluteDelta, Is.EqualTo(2));
        Assert.That(comparison.P99.RelativePercent, Is.EqualTo(20));
        Assert.That(comparison.Metrics[0].BaselineText, Is.EqualTo("100"));
        Assert.That(comparison.Metrics[0].SelectedText, Is.EqualTo("125"));
        Assert.That(comparison.Metrics[0].AbsoluteText, Is.EqualTo("+25"));
        Assert.That(comparison.Metrics[0].RelativeText, Is.EqualTo("+25%"));
    }

    [Test]
    public void ReversingBaselineReversesAbsoluteButChangesRelativeDenominator()
    {
        var forward = new BenchmarkMetricDelta(100, 125);
        var reverse = new BenchmarkMetricDelta(125, 100);

        Assert.That(forward.AbsoluteDelta, Is.EqualTo(25));
        Assert.That(reverse.AbsoluteDelta, Is.EqualTo(-25));
        Assert.That(forward.RelativePercent, Is.EqualTo(25));
        Assert.That(reverse.RelativePercent, Is.EqualTo(-20));
    }

    [TestCase(0d, 0d, 0d, 0d)]
    [TestCase(0d, 5d, 5d, null)]
    [TestCase(5d, 0d, -5d, -100d)]
    public void ZeroBaselineHasExplicitDefinedOrUndefinedRelativeChange(
        double baseline,
        double selected,
        double absolute,
        double? percent)
    {
        var delta = new BenchmarkMetricDelta(baseline, selected);

        Assert.That(delta.AbsoluteDelta, Is.EqualTo(absolute));
        Assert.That(delta.RelativePercent, Is.EqualTo(percent));
        Assert.That(delta.RelativeText, Is.EqualTo(percent.HasValue
            ? percent == 0 ? "0%" : "-100%"
            : "n/a (zero baseline)"));
    }

    [Test]
    public void CounterDeltaRetainsUnitsAboveDoubleIntegerPrecision()
    {
        const long baseline = 9_007_199_254_740_992;
        var delta = new BenchmarkCountDelta(baseline, baseline + 1);
        BenchmarkMetricRow row = BenchmarkMetricRow.Create("Completed ops", delta);

        Assert.That(delta.AbsoluteDelta, Is.EqualTo(1));
        Assert.That(delta.RelativePercent, Is.EqualTo(100.0 / baseline));
        Assert.That(row.AbsoluteText, Is.EqualTo("+1"));
        Assert.That(row.SelectedText, Is.EqualTo("9,007,199,254,740,993"));
        Assert.That(row.RelativeText, Is.Not.EqualTo("0%"));
    }

    [TestCase(0L, 0L, 0L, 0d)]
    [TestCase(0L, 3L, 3L, null)]
    [TestCase(3L, 0L, -3L, -100d)]
    [TestCase(long.MaxValue, 0L, -long.MaxValue, -100d)]
    public void CounterZeroAndExtremeBoundariesRemainFinite(
        long baseline,
        long selected,
        long absolute,
        double? percent)
    {
        var delta = new BenchmarkCountDelta(baseline, selected);
        Assert.That(delta.AbsoluteDelta, Is.EqualTo(absolute));
        Assert.That(delta.RelativePercent, Is.EqualTo(percent));
    }

    [Test]
    public void RelativeOverflowIsMissingRatherThanInfinityOrZero()
    {
        var delta = new BenchmarkMetricDelta(double.Epsilon, double.MaxValue);

        Assert.That(delta.AbsoluteDelta, Is.EqualTo(double.MaxValue));
        Assert.That(delta.RelativePercent, Is.Null);
        Assert.That(delta.RelativeText, Is.EqualTo("n/a (outside numeric range)"));
        Assert.That(new BenchmarkMetricDelta(1e306, 2e306).RelativePercent, Is.EqualTo(100));
    }

    [TestCase(double.NaN, 1d)]
    [TestCase(double.PositiveInfinity, 1d)]
    [TestCase(1d, double.NegativeInfinity)]
    [TestCase(-1d, 1d)]
    [TestCase(1d, -1d)]
    public void InvalidMetricsAreRejected(double baseline, double selected)
    {
        Assert.That(() => new BenchmarkMetricDelta(baseline, selected),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void MatchingCapturedEvidenceIsComparableWithoutClaimingEnvironmentalControl()
    {
        BenchmarkRun baseline = BenchmarkComparisonTestData.Captured();
        BenchmarkRun selected = BenchmarkComparisonTestData.Captured(1);
        var comparison = new BenchmarkComparison(baseline, selected);

        Assert.That(comparison.Compatibility, Is.EqualTo(BenchmarkEvidenceStatus.Match));
        Assert.That(comparison.IsComparable, Is.True);
        Assert.That(comparison.EvidenceRows.Select(row => row.Status),
            Is.All.EqualTo(BenchmarkEvidenceStatus.Match));
        Assert.That(comparison.Summary, Does.Contain("not controlled"));
        Assert.That(comparison.Metrics[^1].BaselineText, Is.EqualTo("2"));
        Assert.That(comparison.Metrics[^1].AbsoluteText, Is.EqualTo("0"));
    }

    [TestCase("Workload")]
    [TestCase("Value generator")]
    [TestCase("Rate / burst")]
    [TestCase("Requested duration (s)")]
    [TestCase("Max in flight")]
    [TestCase("Target (namespace URI)")]
    [TestCase("Write type / rank")]
    [TestCase("Endpoint")]
    [TestCase("Server application URI")]
    [TestCase("Security mode")]
    [TestCase("Security policy")]
    [TestCase("Authentication type")]
    public void MismatchedConfigurationHasSpecificEvidence(string criterion)
    {
        BenchmarkConfiguration baseline = BenchmarkComparisonTestData.Configuration();
        BenchmarkConfiguration changed = criterion switch
        {
            "Workload" => baseline with { Mode = BenchmarkMode.Call },
            "Value generator" => baseline with { Generator = ValueGenerator.Sequential },
            "Rate / burst" => baseline with { TargetRate = 200 },
            "Requested duration (s)" => baseline with { DurationSeconds = 20 },
            "Max in flight" => baseline with { MaxConcurrency = 8 },
            "Target (namespace URI)" => baseline with { TargetNodeId = "nsu=urn:other;s=Value" },
            "Write type / rank" => baseline with { TargetValueRank = ValueRanks.OneDimension },
            "Endpoint" => baseline with { EndpointUrl = "opc.tcp://localhost:4841/" },
            "Server application URI" => baseline with { ServerApplicationUri = "urn:another:server" },
            "Security mode" => baseline with { SecurityMode = MessageSecurityMode.Sign },
            "Security policy" => baseline with { SecurityPolicyUri = SecurityPolicies.None },
            "Authentication type" => baseline with { UserTokenType = UserTokenType.Certificate },
            _ => throw new ArgumentOutOfRangeException(nameof(criterion))
        };

        var comparison = new BenchmarkComparison(
            BenchmarkComparisonTestData.Captured(configuration: baseline),
            BenchmarkComparisonTestData.Captured(1, changed));

        Assert.That(comparison.IsComparable, Is.False);
        Assert.That(comparison.Compatibility, Is.EqualTo(BenchmarkEvidenceStatus.Different));
        Assert.That(comparison.EvidenceRows.Single(row => row.Criterion == criterion).Status,
            Is.EqualTo(BenchmarkEvidenceStatus.Different));
        Assert.That(comparison.Summary, Does.Contain("descriptive"));
        Assert.That(comparison.AchievedRate.AbsoluteDelta, Is.Zero);
    }

    [Test]
    public void UnknownEvidenceIsNotAMatchEvenWhenBothSidesAndNotesAreIdentical()
    {
        BenchmarkRun baseline = BenchmarkComparisonTestData.Aggregate();
        BenchmarkRun selected = baseline with { Id = Guid.NewGuid() };
        var comparison = new BenchmarkComparison(baseline, selected);

        Assert.That(comparison.Compatibility, Is.EqualTo(BenchmarkEvidenceStatus.Unknown));
        Assert.That(comparison.IsComparable, Is.False);
        Assert.That(comparison.EvidenceRows.Select(row => row.Status),
            Is.All.EqualTo(BenchmarkEvidenceStatus.Unknown));
        Assert.That(comparison.Distribution.Count, Is.Zero);
        Assert.That(comparison.DistributionStatus, Does.Contain("Missing distribution"));
        Assert.That(comparison.Metrics[^1].AbsoluteText, Is.EqualTo("n/a (missing data)"));
        Assert.That(comparison.Summary, Does.Contain("notes do not prove a match"));
    }

    [Test]
    public void KnownMismatchIsNotHiddenByOtherUnknownEvidence()
    {
        BenchmarkConfiguration baseline = BenchmarkComparisonTestData.Configuration() with { SecurityMode = null };
        BenchmarkConfiguration selected = baseline with { TargetRate = 300 };
        var comparison = new BenchmarkComparison(
            BenchmarkComparisonTestData.Captured(configuration: baseline),
            BenchmarkComparisonTestData.Captured(1, selected));

        Assert.That(comparison.Compatibility, Is.EqualTo(BenchmarkEvidenceStatus.Different));
        Assert.That(comparison.EvidenceRows.Single(row => row.Criterion == "Security mode").Status,
            Is.EqualTo(BenchmarkEvidenceStatus.Unknown));
        Assert.That(comparison.EvidenceRows.Single(row => row.Criterion == "Rate / burst").Status,
            Is.EqualTo(BenchmarkEvidenceStatus.Different));
    }

    [TestCase(nameof(BenchmarkCompletion.Stopped))]
    [TestCase(nameof(BenchmarkCompletion.Failed))]
    public void MatchingPartialRunsAreNotEquivalentCompletedRuns(string completionName)
    {
        BenchmarkCompletion completion = Enum.Parse<BenchmarkCompletion>(completionName);
        BenchmarkRun baseline = BenchmarkComparisonTestData.Captured() with { Completion = completion };
        BenchmarkRun selected = BenchmarkComparisonTestData.Captured(1) with { Completion = completion };
        var comparison = new BenchmarkComparison(baseline, selected);

        Assert.That(comparison.Compatibility, Is.EqualTo(BenchmarkEvidenceStatus.Match));
        Assert.That(comparison.IsComparable, Is.False);
        Assert.That(comparison.Summary, Does.Contain("incomplete"));
    }

    [Test]
    public void StoppedRunThroughputUsesMeasuredElapsedRatherThanRequestedDuration()
    {
        BenchmarkConfiguration configuration = BenchmarkComparisonTestData.Configuration() with
        {
            DurationSeconds = 3600
        };
        var histogram = new LatencyHistogram();
        histogram.Record(1);
        histogram.Record(3);
        BenchmarkRun run = BenchmarkRun.Create(configuration, histogram.Capture(), 1,
            TimeSpan.FromSeconds(0.25), BenchmarkComparisonTestData.Epoch, BenchmarkCompletion.Stopped, "Stopped");

        Assert.That(run.AchievedRate, Is.EqualTo(8));
        Assert.That(run.ElapsedSeconds, Is.EqualTo(0.25));
        Assert.That(run.MeanLatencyMs, Is.EqualTo(2));
        Assert.That(run.Configuration!.DurationSeconds, Is.EqualTo(3600));
        Assert.That(run.Completion, Is.EqualTo(BenchmarkCompletion.Stopped));
        Assert.That(run.Distribution!.SampleCount, Is.EqualTo(2));
        Assert.That(run.ErrorCount, Is.EqualTo(1));
    }
}

internal static class BenchmarkComparisonTestData
{
    public static BenchmarkConfiguration Configuration()
    {
        return new BenchmarkConfiguration
        {
            Mode = BenchmarkMode.Write,
            Generator = ValueGenerator.Fixed,
            TargetRate = 100,
            DurationSeconds = 10,
            MaxConcurrency = 4,
            TargetNodeId = "nsu=urn:ualens:comparison;s=Value",
            TargetType = BuiltInType.Double,
            TargetValueRank = ValueRanks.Scalar,
            InputArguments = ArrayOf<BenchmarkArgumentConfiguration>.Empty,
            EndpointUrl = "opc.tcp://localhost:4840/",
            ServerApplicationUri = "urn:ualens:comparison:server",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserTokenType = UserTokenType.Anonymous
        };
    }

    public static BenchmarkRun Captured(int sequence = 0, BenchmarkConfiguration? configuration = null)
    {
        var histogram = new LatencyHistogram();
        histogram.Record(1);
        histogram.Record(1);
        histogram.Record(2);
        histogram.Record(8);
        return BenchmarkRun.Create(configuration ?? Configuration(), histogram.Capture(), 1,
            TimeSpan.FromSeconds(2), Epoch.AddSeconds(sequence), BenchmarkCompletion.Completed, "Captured run");
    }

    public static BenchmarkRun Aggregate(int sequence = 0)
    {
        return new BenchmarkRun
        {
            TimestampUtc = Epoch.AddSeconds(sequence),
            TargetRate = 100,
            AchievedRate = 100,
            TotalOps = 200,
            MeanLatencyMs = 2,
            P50Ms = 1,
            P90Ms = 5,
            P99Ms = 10,
            ErrorCount = 2,
            Notes = "mode=Write; gen=Fixed; target=Value"
        };
    }

    public static readonly DateTime Epoch = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
}
