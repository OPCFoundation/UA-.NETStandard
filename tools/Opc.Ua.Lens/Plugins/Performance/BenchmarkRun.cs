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
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UaLens.Plugins.Performance;

internal enum BenchmarkCompletion
{
    Unknown,
    Completed,
    Stopped,
    Failed
}

/// <summary>
/// Immutable completed-run results. Legacy CSV has aggregates only; richer
/// archives also retain measured elapsed time, configuration and genuine buckets.
/// </summary>
internal sealed record BenchmarkRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Wall-clock time the run completed, in UTC.
    /// </summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>
    /// Configured target rate in ops/sec (zero means unbounded burst).
    /// </summary>
    public double TargetRate { get; init; }

    /// <summary>
    /// Completed operations divided by actual elapsed time, including drain.
    /// Legacy CSV may have used the requested duration instead.
    /// </summary>
    public double AchievedRate { get; init; }

    /// <summary>
    /// Total completed operations, including errors.
    /// </summary>
    public long TotalOps { get; init; }

    /// <summary>
    /// Measured mean latency in milliseconds; legacy CSV means may be approximate.
    /// </summary>
    public double MeanLatencyMs { get; init; }

    /// <summary>
    /// Bucket upper-bound estimate of the 50th percentile, in milliseconds.
    /// </summary>
    public double P50Ms { get; init; }

    /// <summary>
    /// Bucket upper-bound estimate of the 90th percentile, in milliseconds.
    /// </summary>
    public double P90Ms { get; init; }

    /// <summary>
    /// Bucket upper-bound estimate of the 99th percentile, in milliseconds.
    /// </summary>
    public double P99Ms { get; init; }

    /// <summary>
    /// Total errored operations observed during the run.
    /// </summary>
    public long ErrorCount { get; init; }

    public double? ElapsedSeconds { get; init; }
    public BenchmarkCompletion Completion { get; init; }
    public BenchmarkConfiguration? Configuration { get; init; }
    public BenchmarkDistribution? Distribution { get; init; }

    /// <summary>
    /// Free-form notes, never parsed to invent configuration evidence.
    /// </summary>
    public string Notes { get; init; } = string.Empty;

    public string DistributionStatus => Distribution is null
        ? "Missing distribution: aggregate-only run; histogram samples were not saved."
        : string.Format(CultureInfo.InvariantCulture,
            "{0:N0} observed samples in {1} fixed buckets; {2:N0} at or above 10 s.",
            Distribution.SampleCount, LatencyHistogram.BucketCount, Distribution.OverflowCount);

    public string ConfigurationStatus => Configuration is null
        ? "Unknown configuration: settings, target and security were not captured."
        : "Configuration evidence captured at Run; missing fields remain unknown.";

    public static BenchmarkRun Create(
        BenchmarkConfiguration configuration,
        BenchmarkDistribution distribution,
        long errorCount,
        TimeSpan elapsed,
        DateTime timestampUtc,
        BenchmarkCompletion completion,
        string notes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(distribution);
        ArgumentNullException.ThrowIfNull(notes);
        var run = new BenchmarkRun
        {
            TimestampUtc = timestampUtc,
            TargetRate = configuration.TargetRate,
            AchievedRate = elapsed.TotalSeconds > 0 ? distribution.SampleCount / elapsed.TotalSeconds : 0,
            TotalOps = distribution.SampleCount,
            MeanLatencyMs = distribution.MeanMs,
            P50Ms = distribution.GetPercentile(0.50),
            P90Ms = distribution.GetPercentile(0.90),
            P99Ms = distribution.GetPercentile(0.99),
            ErrorCount = errorCount,
            ElapsedSeconds = elapsed.TotalSeconds,
            Completion = completion,
            Configuration = configuration,
            Distribution = distribution,
            Notes = notes
        };
        run.Validate();
        return run;
    }

    public void Validate()
    {
        if (Id == Guid.Empty || TimestampUtc == default || TimestampUtc.Kind != DateTimeKind.Utc)
        {
            throw new FormatException("A benchmark run requires an identity and UTC completion timestamp.");
        }
        if (!IsNonnegativeFinite(TargetRate) || !IsNonnegativeFinite(AchievedRate) ||
            !IsNonnegativeFinite(MeanLatencyMs) || !IsNonnegativeFinite(P50Ms) ||
            !IsNonnegativeFinite(P90Ms) || !IsNonnegativeFinite(P99Ms) ||
            P50Ms > P90Ms || P90Ms > P99Ms)
        {
            throw new FormatException("Benchmark rates and latencies must be finite, nonnegative and ordered.");
        }
        if (TotalOps < 0 || ErrorCount < 0 || ErrorCount > TotalOps)
        {
            throw new FormatException("Benchmark operation and error counts are inconsistent.");
        }
        if (ElapsedSeconds is { } elapsed &&
            (!IsNonnegativeFinite(elapsed) || (TotalOps > 0 && elapsed == 0)))
        {
            throw new FormatException("Benchmark elapsed time is invalid.");
        }
        if (ElapsedSeconds is { } measured &&
            AchievedRate != (measured > 0 ? TotalOps / measured : 0))
        {
            throw new FormatException("Benchmark throughput does not match completed operations and elapsed time.");
        }
        if (!Enum.IsDefined(Completion) || Notes is null)
        {
            throw new FormatException("Benchmark completion or notes are invalid.");
        }
        Configuration?.Validate();
        if (Configuration is { } configuration && configuration.TargetRate != TargetRate)
        {
            throw new FormatException("Benchmark aggregates do not match the captured target rate.");
        }
        if (Distribution is { } distribution &&
            (distribution.SampleCount != TotalOps || distribution.MeanMs != MeanLatencyMs ||
                distribution.GetPercentile(0.50) != P50Ms ||
                distribution.GetPercentile(0.90) != P90Ms ||
                distribution.GetPercentile(0.99) != P99Ms))
        {
            throw new FormatException("Benchmark aggregates do not match the observed distribution.");
        }
    }

    /// <summary>
    /// Format this run as a single CSV row using
    /// <see cref="CultureInfo.InvariantCulture"/>.  Numeric fields use
    /// fixed precision so files are easy to diff across runs.
    /// </summary>
    public string ToCsvRow()
    {
        Validate();
        return string.Format(CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
            TimestampUtc.ToString("o", CultureInfo.InvariantCulture),
            TargetRate.ToString("F3", CultureInfo.InvariantCulture),
            AchievedRate.ToString("F3", CultureInfo.InvariantCulture),
            TotalOps.ToString(CultureInfo.InvariantCulture),
            MeanLatencyMs.ToString("F3", CultureInfo.InvariantCulture),
            P50Ms.ToString("F3", CultureInfo.InvariantCulture),
            P90Ms.ToString("F3", CultureInfo.InvariantCulture),
            P99Ms.ToString("F3", CultureInfo.InvariantCulture),
            ErrorCount.ToString(CultureInfo.InvariantCulture),
            CsvEscape(Notes));
    }

    /// <summary>
    /// Parse a single CSV row produced by <see cref="ToCsvRow"/>.
    /// Returns null for malformed rows. The archive loader reports that failure
    /// explicitly and does not replace the existing history with a partial import.
    /// </summary>
    public static BenchmarkRun? TryParseCsvRow(string row)
    {
        if (string.IsNullOrWhiteSpace(row))
        {
            return null;
        }

        List<string>? fields = ParseCsvFields(row);
        if (fields is null || fields.Count is < 9 or > 10)
        {
            return null;
        }

        if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime ts))
        {
            return null;
        }

        if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double target) ||
            !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double achieved) ||
            !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out long total) ||
            !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double mean) ||
            !double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double p50) ||
            !double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double p90) ||
            !double.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double p99) ||
            !long.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out long errors))
        {
            return null;
        }

        string notes = fields.Count > 9 ? fields[9] : string.Empty;

        var run = new BenchmarkRun
        {
            TimestampUtc = ts.Kind == DateTimeKind.Utc ? ts : ts.ToUniversalTime(),
            TargetRate = target,
            AchievedRate = achieved,
            TotalOps = total,
            MeanLatencyMs = mean,
            P50Ms = p50,
            P90Ms = p90,
            P99Ms = p99,
            ErrorCount = errors,
            Notes = notes
        };
        try
        {
            run.Validate();
            return run;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        bool quote = s.AsSpan().IndexOfAny(s_csvQuoteChars) >= 0;
        if (!quote)
        {
            return s;
        }

        return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// Parses quoted fields, including embedded newlines and escaped quotes.
    /// </summary>
    private static List<string>? ParseCsvFields(string row)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        bool afterQuote = false;
        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < row.Length && row[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                        afterQuote = true;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
                afterQuote = false;
            }
            else if (c == '"' && sb.Length == 0 && !afterQuote)
            {
                inQuotes = true;
            }
            else if (afterQuote || c is '"' or '\r' or '\n')
            {
                return null;
            }
            else
            {
                sb.Append(c);
            }
        }
        if (inQuotes)
        {
            return null;
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static bool IsNonnegativeFinite(double value)
    {
        return double.IsFinite(value) && value >= 0;
    }

    public const string CsvHeader =
        "timestamp_utc,target_rate,achieved_rate,total_ops,mean_latency_ms,p50_ms,p90_ms,p99_ms,errors,notes";

    private static readonly System.Buffers.SearchValues<char> s_csvQuoteChars =
        System.Buffers.SearchValues.Create(",\"\n\r");
}

/// <summary>
/// A history row with independent baseline and optional latest-three markers.
/// </summary>
internal sealed partial class BenchmarkRunRow : ObservableObject
{
    public BenchmarkRunRow(BenchmarkRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public BenchmarkRun Run { get; }

    public string TimestampDisplay => Run.TimestampUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string SelectionDisplay => $"{TimestampDisplay} | {Run.Id.ToString("N")[..8]}";

    public string TargetRateDisplay => Run.TargetRate.ToString("N0", CultureInfo.InvariantCulture);

    public string AchievedRateDisplay => Run.AchievedRate.ToString("N0", CultureInfo.InvariantCulture);

    public string TotalOpsDisplay => Run.TotalOps.ToString("N0", CultureInfo.InvariantCulture);

    public string P50Display => Run.P50Ms.ToString("F2", CultureInfo.InvariantCulture);

    public string P99Display => Run.P99Ms.ToString("F2", CultureInfo.InvariantCulture);

    public string ErrorCountDisplay => Run.ErrorCount.ToString("N0", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool m_isHighlighted;

    [ObservableProperty]
    private bool m_isBaseline;
}
