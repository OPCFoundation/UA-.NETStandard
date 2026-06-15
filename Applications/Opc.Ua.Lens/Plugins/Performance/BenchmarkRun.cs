/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

/// <summary>
/// Aggregate snapshot of a completed Performance benchmark run.  Stored
/// in <see cref="PerformancePlugin.RunHistory"/> and serialized to CSV
/// by the Save Results / Load Results commands so users can keep a
/// per-machine baseline of throughput and latency.
/// </summary>
internal sealed record BenchmarkRun
{
    /// <summary>Wall-clock time the run completed, in UTC.</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>Configured target rate in ops/sec (0 = unbounded burst).</summary>
    public double TargetRate { get; init; }

    /// <summary>Measured throughput in ops/sec averaged over the run.</summary>
    public double AchievedRate { get; init; }

    /// <summary>Total ops completed (including errored ops).</summary>
    public long TotalOps { get; init; }

    /// <summary>Approximate mean latency in milliseconds.</summary>
    public double MeanLatencyMs { get; init; }

    /// <summary>50th percentile latency in milliseconds.</summary>
    public double P50Ms { get; init; }

    /// <summary>90th percentile latency in milliseconds.</summary>
    public double P90Ms { get; init; }

    /// <summary>99th percentile latency in milliseconds.</summary>
    public double P99Ms { get; init; }

    /// <summary>Total errored ops observed during the run.</summary>
    public long ErrorCount { get; init; }

    /// <summary>Free-form notes (target description, mode, generator).</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>CSV header row prepended to <see cref="ToCsvRow"/> output.</summary>
    public const string CsvHeader =
        "timestamp_utc,target_rate,achieved_rate,total_ops,mean_latency_ms,p50_ms,p90_ms,p99_ms,errors,notes";

    /// <summary>
    /// Format this run as a single CSV row using
    /// <see cref="CultureInfo.InvariantCulture"/>.  Numeric fields use
    /// fixed precision so files are easy to diff across runs.
    /// </summary>
    public string ToCsvRow()
    {
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
    /// Returns null when the row is malformed so the loader can skip
    /// rather than fail the entire import.
    /// </summary>
    public static BenchmarkRun? TryParseCsvRow(string row)
    {
        if (string.IsNullOrWhiteSpace(row))
        {
            return null;
        }

        List<string> fields = ParseCsvFields(row);
        if (fields.Count < 9)
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

        return new BenchmarkRun
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
    }

    private static readonly System.Buffers.SearchValues<char> s_csvQuoteChars =
        System.Buffers.SearchValues.Create(",\"\n\r");

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
    /// Minimal CSV field splitter that honors double-quoted fields and
    /// escaped <c>""</c> quotes inside them.  Sufficient for the rows
    /// emitted by <see cref="ToCsvRow"/> plus hand-edited Notes fields.
    /// </summary>
    private static List<string> ParseCsvFields(string row)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
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
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '"' && sb.Length == 0)
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}

/// <summary>
/// View-model wrapper for a <see cref="BenchmarkRun"/> bound to the
/// history ListBox.  Carries an <see cref="IsHighlighted"/> flag that
/// <see cref="PerformancePlugin"/> toggles for the most recent 3 rows
/// when "Compare last 3" is enabled, plus pre-formatted display
/// strings so the XAML can stay free of converters.
/// </summary>
internal sealed partial class BenchmarkRunRow : ObservableObject
{
    public BenchmarkRunRow(BenchmarkRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    /// <summary>The underlying immutable run record.</summary>
    public BenchmarkRun Run { get; }

    [ObservableProperty]
    private bool m_isHighlighted;

    /// <summary>Local-time short timestamp, e.g. <c>2025-01-04 14:32:15</c>.</summary>
    public string TimestampDisplay => Run.TimestampUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Target rate rounded to whole ops/sec.</summary>
    public string TargetRateDisplay => Run.TargetRate.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Achieved rate rounded to whole ops/sec.</summary>
    public string AchievedRateDisplay => Run.AchievedRate.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Total ops with thousands separators.</summary>
    public string TotalOpsDisplay => Run.TotalOps.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>p50 in ms (2 fractional digits).</summary>
    public string P50Display => Run.P50Ms.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>p99 in ms (2 fractional digits).</summary>
    public string P99Display => Run.P99Ms.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Error count with thousands separators.</summary>
    public string ErrorCountDisplay => Run.ErrorCount.ToString("N0", CultureInfo.InvariantCulture);
}
