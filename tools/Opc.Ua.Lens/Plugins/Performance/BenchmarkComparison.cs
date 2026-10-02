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

namespace UaLens.Plugins.Performance;

internal enum BenchmarkEvidenceStatus
{
    Match,
    Different,
    Unknown
}

internal readonly record struct BenchmarkMetricDelta
{
    public BenchmarkMetricDelta(double baseline, double selected)
    {
        if (!double.IsFinite(baseline) || baseline < 0 || !double.IsFinite(selected) || selected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseline), "Metrics must be finite and nonnegative.");
        }
        Baseline = baseline;
        Selected = selected;
    }

    public double Baseline { get; }
    public double Selected { get; }
    public double AbsoluteDelta => Selected - Baseline;
    public double? RelativePercent => RelativeChange(Baseline, AbsoluteDelta);
    public string RelativeText => FormatRelative(Baseline, RelativePercent);

    internal static double? RelativeChange(double baseline, double delta)
    {
        if (baseline == 0)
        {
            return delta == 0 ? 0 : null;
        }
        double percent = delta / baseline * 100;
        return double.IsFinite(percent) ? percent : null;
    }

    internal static string FormatRelative(double baseline, double? percent)
    {
        return percent is { } value
            ? FormatSigned(value) + "%"
            : baseline == 0 ? "n/a (zero baseline)" : "n/a (outside numeric range)";
    }

    internal static string FormatSigned(double value)
    {
        return (value > 0 ? "+" : string.Empty) + value.ToString("G9", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Counter differences stay integral, including values above double's exact
/// integer range. Only the relative percentage uses floating-point arithmetic.
/// </summary>
internal readonly record struct BenchmarkCountDelta
{
    public BenchmarkCountDelta(long baseline, long selected)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseline);
        ArgumentOutOfRangeException.ThrowIfNegative(selected);
        Baseline = baseline;
        Selected = selected;
    }

    public long Baseline { get; }
    public long Selected { get; }
    public long AbsoluteDelta => Selected - Baseline;
    public double? RelativePercent => BenchmarkMetricDelta.RelativeChange(Baseline, AbsoluteDelta);
    public string RelativeText => BenchmarkMetricDelta.FormatRelative(Baseline, RelativePercent);
}

internal sealed record BenchmarkMetricRow(
    string Metric,
    string BaselineText,
    string SelectedText,
    string AbsoluteText,
    string RelativeText)
{
    public static BenchmarkMetricRow Create(string name, BenchmarkMetricDelta delta)
    {
        return new BenchmarkMetricRow(
            name,
            delta.Baseline.ToString("G9", CultureInfo.InvariantCulture),
            delta.Selected.ToString("G9", CultureInfo.InvariantCulture),
            BenchmarkMetricDelta.FormatSigned(delta.AbsoluteDelta),
            delta.RelativeText);
    }

    public static BenchmarkMetricRow Create(string name, BenchmarkCountDelta delta)
    {
        return new BenchmarkMetricRow(
            name,
            delta.Baseline.ToString("N0", CultureInfo.InvariantCulture),
            delta.Selected.ToString("N0", CultureInfo.InvariantCulture),
            delta.AbsoluteDelta.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture),
            delta.RelativeText);
    }
}

internal sealed record BenchmarkEvidence(
    string Criterion,
    string? BaselineValue,
    string? SelectedValue,
    BenchmarkEvidenceStatus Status)
{
    public string BaselineText => BaselineValue ?? "Unknown (not captured)";
    public string SelectedText => SelectedValue ?? "Unknown (not captured)";
    public string StatusText => Status.ToString();
}

internal sealed record BenchmarkDistributionRow(
    int BucketIndex,
    long? BaselineCount,
    long? SelectedCount,
    double? BaselinePercent,
    double? SelectedPercent)
{
    public string BucketText => BucketIndex == LatencyHistogram.BucketCount - 1
        ? ">= 10000 ms (overflow)"
        : string.Format(CultureInfo.InvariantCulture, "[{0:G6}, {1:G6}) ms",
            LatencyHistogram.BucketLowerMs(BucketIndex), LatencyHistogram.BucketUpperMs(BucketIndex));

    public double? PercentagePointDelta => SelectedPercent - BaselinePercent;
    public string BaselineCountText => BaselineCount?.ToString("N0", CultureInfo.InvariantCulture) ?? "Missing";
    public string SelectedCountText => SelectedCount?.ToString("N0", CultureInfo.InvariantCulture) ?? "Missing";
    public string BaselinePercentText => FormatPercent(BaselinePercent);
    public string SelectedPercentText => FormatPercent(SelectedPercent);
    public string DeltaText => PercentagePointDelta?.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture)
        ?? "n/a";

    private static string FormatPercent(double? percent)
    {
        return percent?.ToString("0.###", CultureInfo.InvariantCulture) ?? "n/a";
    }
}

/// <summary>
/// Selected-minus-baseline values and explicit comparability evidence. Distribution
/// rows use actual bucket counts only, normalized independently by observed samples.
/// This is a table comparison, not an overlay of the live charts.
/// </summary>
internal sealed class BenchmarkComparison
{
    public BenchmarkComparison(BenchmarkRun baseline, BenchmarkRun selected)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(selected);
        baseline.Validate();
        selected.Validate();
        Baseline = baseline;
        Selected = selected;
        AchievedRate = new BenchmarkMetricDelta(baseline.AchievedRate, selected.AchievedRate);
        TotalOps = new BenchmarkCountDelta(baseline.TotalOps, selected.TotalOps);
        Errors = new BenchmarkCountDelta(baseline.ErrorCount, selected.ErrorCount);
        MeanLatency = new BenchmarkMetricDelta(baseline.MeanLatencyMs, selected.MeanLatencyMs);
        P50 = new BenchmarkMetricDelta(baseline.P50Ms, selected.P50Ms);
        P90 = new BenchmarkMetricDelta(baseline.P90Ms, selected.P90Ms);
        P99 = new BenchmarkMetricDelta(baseline.P99Ms, selected.P99Ms);
        Metrics =
        [
            BenchmarkMetricRow.Create("Throughput (ops/s)", AchievedRate),
            BenchmarkMetricRow.Create("Completed ops", TotalOps),
            BenchmarkMetricRow.Create("Errors", Errors),
            BenchmarkMetricRow.Create("Mean latency (ms)", MeanLatency),
            BenchmarkMetricRow.Create("p50 (ms)", P50),
            BenchmarkMetricRow.Create("p90 (ms)", P90),
            BenchmarkMetricRow.Create("p99 (ms)", P99),
            CreateElapsedRow(baseline.ElapsedSeconds, selected.ElapsedSeconds)
        ];
        Evidence = BuildEvidence(baseline, selected);
        Compatibility = BenchmarkEvidenceStatus.Match;
        foreach (BenchmarkEvidence evidence in Evidence)
        {
            if (evidence.Status == BenchmarkEvidenceStatus.Different)
            {
                Compatibility = BenchmarkEvidenceStatus.Different;
                break;
            }
            if (evidence.Status == BenchmarkEvidenceStatus.Unknown)
            {
                Compatibility = BenchmarkEvidenceStatus.Unknown;
            }
        }
        Distribution = BuildDistribution(baseline.Distribution, selected.Distribution);
    }

    public BenchmarkRun Baseline { get; }
    public BenchmarkRun Selected { get; }
    public BenchmarkMetricDelta AchievedRate { get; }
    public BenchmarkCountDelta TotalOps { get; }
    public BenchmarkCountDelta Errors { get; }
    public BenchmarkMetricDelta MeanLatency { get; }
    public BenchmarkMetricDelta P50 { get; }
    public BenchmarkMetricDelta P90 { get; }
    public BenchmarkMetricDelta P99 { get; }
    public ArrayOf<BenchmarkMetricRow> Metrics { get; }
    public ArrayOf<BenchmarkEvidence> Evidence { get; }
    public ArrayOf<BenchmarkDistributionRow> Distribution { get; }
    public BenchmarkEvidenceStatus Compatibility { get; }

    public bool IsComparable => Compatibility == BenchmarkEvidenceStatus.Match &&
        Baseline.Completion == BenchmarkCompletion.Completed && Selected.Completion == BenchmarkCompletion.Completed;

    public string Summary => Compatibility switch
    {
        BenchmarkEvidenceStatus.Different =>
            "Known configuration differences. Deltas are descriptive, not an equivalent-workload result.",
        BenchmarkEvidenceStatus.Unknown =>
            "Comparability unknown: required evidence is missing. Equal or absent notes do not prove a match.",
        _ => IsComparable
            ? "Captured settings, target and security match. Server load and client environment are not controlled."
            : "Captured evidence matches, but stopped or failed runs are incomplete; interpret deltas accordingly."
    };

    public string DistributionStatus =>
        $"Baseline: {Baseline.DistributionStatus}\nSelected: {Selected.DistributionStatus}";

    public string MetricCaveat =>
        "Delta = selected - baseline; relative delta uses the baseline. Latency percentiles are bucket estimates. " +
        "Legacy CSV may use approximate means and requested-duration throughput.";

    // Avalonia ItemsSource consumes IEnumerable; the comparison model retains ArrayOf values.
    public IEnumerable<BenchmarkMetricRow> MetricRows => Metrics.Span.ToArray();
    public IEnumerable<BenchmarkEvidence> EvidenceRows => Evidence.Span.ToArray();
    public IEnumerable<BenchmarkDistributionRow> DistributionRows => Distribution.Span.ToArray();

    private static BenchmarkMetricRow CreateElapsedRow(double? baseline, double? selected)
    {
        if (baseline is { } b && selected is { } s)
        {
            return BenchmarkMetricRow.Create("Elapsed incl. drain (s)", new BenchmarkMetricDelta(b, s));
        }
        return new BenchmarkMetricRow(
            "Elapsed incl. drain (s)",
            baseline?.ToString("G9", CultureInfo.InvariantCulture) ?? "Missing",
            selected?.ToString("G9", CultureInfo.InvariantCulture) ?? "Missing",
            "n/a (missing data)",
            "n/a (missing data)");
    }

    private static ArrayOf<BenchmarkEvidence> BuildEvidence(BenchmarkRun baseline, BenchmarkRun selected)
    {
        BenchmarkConfiguration? b = baseline.Configuration;
        BenchmarkConfiguration? s = selected.Configuration;
        return
        [
            Compare("Workload", b?.Mode.ToString(), s?.Mode.ToString()),
            Compare("Value generator", b?.Generator.ToString(), s?.Generator.ToString()),
            Compare("Rate / burst", Rate(b), Rate(s)),
            Compare("Requested duration (s)", Number(b?.DurationSeconds), Number(s?.DurationSeconds)),
            Compare("Max in flight", Number(b?.MaxConcurrency), Number(s?.MaxConcurrency)),
            Compare("Target (namespace URI)", b?.TargetNodeId, s?.TargetNodeId),
            Compare("Write type / rank", WriteType(b), WriteType(s)),
            Compare("Method parent", MethodParent(b), MethodParent(s)),
            Compare("Generated input signature", InputSignature(b, false), InputSignature(s, false)),
            Compare("Input data types", InputSignature(b, true), InputSignature(s, true)),
            Compare("Endpoint", b?.EndpointUrl, s?.EndpointUrl),
            Compare("Server application URI", b?.ServerApplicationUri, s?.ServerApplicationUri),
            Compare("Security mode", b?.SecurityMode?.ToString(), s?.SecurityMode?.ToString()),
            Compare("Security policy", b?.SecurityPolicyUri, s?.SecurityPolicyUri),
            Compare("Authentication type", b?.UserTokenType?.ToString(), s?.UserTokenType?.ToString()),
            Compare("Completion", Completion(baseline), Completion(selected)),
            Compare("Measurement basis", MeasurementBasis(baseline), MeasurementBasis(selected))
        ];
    }

    private static BenchmarkEvidence Compare(string criterion, string? baseline, string? selected)
    {
        BenchmarkEvidenceStatus status = baseline is null || selected is null
            ? BenchmarkEvidenceStatus.Unknown
            : string.Equals(baseline, selected, StringComparison.Ordinal)
                ? BenchmarkEvidenceStatus.Match
                : BenchmarkEvidenceStatus.Different;
        return new BenchmarkEvidence(criterion, baseline, selected, status);
    }

    private static string? Number(double? number)
    {
        return number?.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string? Rate(BenchmarkConfiguration? configuration)
    {
        return configuration is null ? null : configuration.UnboundedBurst
            ? "Unbounded burst"
            : Number(configuration.TargetRate) + " ops/s";
    }

    private static string? WriteType(BenchmarkConfiguration? configuration)
    {
        return configuration is null ? null : configuration.Mode == BenchmarkMode.Call
            ? "Not applicable"
            : $"{configuration.TargetType}; rank={configuration.TargetValueRank}";
    }

    private static string? MethodParent(BenchmarkConfiguration? configuration)
    {
        return configuration is null ? null : configuration.Mode == BenchmarkMode.Write
            ? "Not applicable"
            : configuration.ObjectNodeId;
    }

    private static string? InputSignature(BenchmarkConfiguration? configuration, bool dataTypes)
    {
        if (configuration is null)
        {
            return null;
        }
        if (configuration.Mode == BenchmarkMode.Write)
        {
            return "Not applicable";
        }
        if (configuration.InputArguments.IsNull)
        {
            return null;
        }
        if (configuration.InputArguments.IsEmpty)
        {
            return "(no arguments)";
        }
        var signature = new List<string>(configuration.InputArguments.Count);
        foreach (BenchmarkArgumentConfiguration argument in configuration.InputArguments)
        {
            if (dataTypes && argument.DataTypeId is null)
            {
                return null;
            }
            signature.Add(dataTypes ? argument.DataTypeId! : $"{argument.GeneratedType}; rank={argument.ValueRank}");
        }
        return string.Join(" | ", signature);
    }

    private static string? Completion(BenchmarkRun run)
    {
        return run.Completion == BenchmarkCompletion.Unknown ? null : run.Completion.ToString();
    }

    private static string? MeasurementBasis(BenchmarkRun run)
    {
        return run.ElapsedSeconds.HasValue && run.Distribution is not null
            ? "Measured elapsed incl. drain; measured mean; schema-1 bucket percentiles"
            : null;
    }

    private static ArrayOf<BenchmarkDistributionRow> BuildDistribution(
        BenchmarkDistribution? baseline,
        BenchmarkDistribution? selected)
    {
        var rows = new List<BenchmarkDistributionRow>(LatencyHistogram.BucketCount);
        for (int i = 0; i < LatencyHistogram.BucketCount; i++)
        {
            long? b = baseline?.BucketCounts[i];
            long? s = selected?.BucketCounts[i];
            if (b.GetValueOrDefault() == 0 && s.GetValueOrDefault() == 0)
            {
                continue;
            }
            rows.Add(new BenchmarkDistributionRow(
                i, b, s,
                baseline is { SampleCount: > 0 } ? b / (double)baseline.SampleCount * 100 : null,
                selected is { SampleCount: > 0 } ? s / (double)selected.SampleCount * 100 : null));
        }
        return new ArrayOf<BenchmarkDistributionRow>(rows.ToArray());
    }
}
